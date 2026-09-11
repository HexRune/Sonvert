using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;
using Sonvert.App.Services.Characters;
using Sonvert.App.Settings;

namespace Sonvert.App.Services.Tts;

/// <summary>
/// 本地 TTS 引擎的第三个实现，用来跟 GPT-SoVITS/IndexTTS 做速度对比
/// （接入动机纯粹是测速，不是确定要换）。对接一个自建的 Qwen3-TTS
/// 微服务，具体的 Python 服务由用户自己实现——这个类只依赖下面这份
/// HTTP 接口约定，不关心 Python 那边内部调的是 qwen_tts 库的哪个函数。
///
/// 跟 IndexTtsService 最大的设计差异：情绪跟随（拿实时原始语音当情绪
/// 参考）这个技巧不用了——Qwen3-TTS 官方资料里没有类似 emo_audio_prompt
/// 这种"音频对音频"的情绪参数，它的语调更多是从参考音频本身的
/// in-context 特征里带出来的，额外的语气控制是文本指令风格。所以这里
/// SynthesizeAsync 的 liveEmotionAudio/liveEmotionSampleRate 两个参数
/// 目前直接忽略，不往下传——先把速度测出来，情绪这块要不要跟进、怎么跟
/// 等测完速度再说，不在这次范围内。
///
/// 音色参考走 PromptText+AudioPath，两者都是角色的 NEUTRAL 片段自带的
/// 字段（PromptText 本来是给 GPT-SoVITS 用的"这段参考音频里说的是什么"，
/// Qwen3-TTS 的参考音频克隆同样需要一份转写文本来提升还原度，直接复用，
/// 不用另外录。
///
/// PrewarmReferenceAudioAsync 在这个实现里是真正有效的操作，不是空实现——
/// Qwen3-TTS 官方例子里有 create_voice_clone_prompt 这一步：从参考音频提
/// 特征生成一个可复用的 prompt，避免每次合成都重新提一遍特征。这里调用
/// /prewarm 让 Python 端把这个 prompt 缓存住，后续 /synthesize 命中缓存
/// 会快很多——这可能是比"换模型"更直接的降延迟手段，值得在测速时对比
/// "调用过 prewarm" vs "没调用过" 两种情况。
/// </summary>
public class QwenTtsService : ITtsService, IAsyncDisposable
{
    private readonly ISettingsService _settingsService;
    private readonly ICharacterRepository _characterRepository;
    private readonly HttpClient _httpClient;
    private Process? _process;

    public QwenTtsService(ISettingsService settingsService, ICharacterRepository characterRepository)
    {
        _settingsService = settingsService;
        _characterRepository = characterRepository;
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri($"http://127.0.0.1:{_settingsService.Current.QwenTtsPort}"),
            Timeout = TimeSpan.FromSeconds(60),
        };
    }

    public async Task StartAsync()
    {
        if (_process is { HasExited: false })
        {
            return;
        }

        var settings = _settingsService.Current;
        var executablePath = settings.QwenTtsExecutablePath;
        var workingDirectory = settings.QwenTtsWorkingDirectory;
        var modelsDirectory = settings.QwenTtsModelsDirectory;

        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            throw new FileNotFoundException(
                $"找不到 QwenTTSService 的可执行文件: {executablePath}，检查设置里的 QwenTtsExecutablePath");
        }

        if (string.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory))
        {
            throw new DirectoryNotFoundException(
                $"找不到 QwenTTSService 工作目录: {workingDirectory}，检查设置里的 QwenTtsWorkingDirectory");
        }

        if (string.IsNullOrWhiteSpace(modelsDirectory) || !Directory.Exists(modelsDirectory))
        {
            throw new DirectoryNotFoundException(
                $"找不到 Qwen3-TTS 模型目录: {modelsDirectory}，检查设置里的 QwenTtsModelsDirectory");
        }

        await WriteServiceConfigAsync(workingDirectory, settings);

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = settings.QwenTtsArguments,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        _process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null) Debug.WriteLine($"[QwenTTSService] {e.Data}");
        };
        _process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null) Debug.WriteLine($"[QwenTTSService:ERR] {e.Data}");
        };

        _process.Start();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        await WaitUntilHealthyAsync();
    }

    /// <summary>跟 SenseVoiceService/IndexTtsService 同样的模式——把端口/
    /// 模型目录写进 exe 旁边的 service_config.json。</summary>
    private async Task WriteServiceConfigAsync(string workingDirectory, AppSettings settings)
    {
        var configPath = Path.Combine(workingDirectory, "service_config.json");
        var config = new
        {
            port = settings.QwenTtsPort,
            host = "127.0.0.1",
            resource_dir = Path.GetFullPath(settings.QwenTtsModelsDirectory),
        };
        await File.WriteAllTextAsync(configPath, System.Text.Json.JsonSerializer.Serialize(config));
    }

    private async Task WaitUntilHealthyAsync()
    {
        const int maxAttempts = 60;
        const int delayMs = 500;

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            try
            {
                var response = await _httpClient.GetAsync("/health");
                if (response.IsSuccessStatusCode) return;
            }
            catch (HttpRequestException)
            {
                // 忽略，继续重试。
            }

            await Task.Delay(delayMs);
        }

        throw new TimeoutException($"QwenTTSService 启动超时（等待 {maxAttempts * delayMs / 1000} 秒仍未就绪）");
    }

    public async Task<TtsResult> SynthesizeAsync(
        string text, string language, string emotion,
        float[]? liveEmotionAudio = null, int liveEmotionSampleRate = 0)
    {
        // liveEmotionAudio/liveEmotionSampleRate 故意不用——见类顶部注释。
        var settings = _settingsService.Current;

        if (settings.ActiveCharacterId is not { } characterId)
        {
            throw new InvalidOperationException("还没选择角色，先在首页选一个角色再开始翻译");
        }

        var timbreClip = await _characterRepository.ResolveClipAsync(characterId, "NEUTRAL", language);
        if (timbreClip is null)
        {
            throw new InvalidOperationException(
                $"角色 {characterId} 还没有任何参考音频（中文/英文的 NEUTRAL 至少要录一个），先去\"声音克隆\"页面录一段");
        }

        var query =
            $"/synthesize?text={Uri.EscapeDataString(text)}" +
            $"&lang={Uri.EscapeDataString(language)}" +
            $"&spk_audio_path={Uri.EscapeDataString(timbreClip.AudioPath)}" +
            $"&ref_text={Uri.EscapeDataString(timbreClip.PromptText)}";

        var response = await _httpClient.PostAsync(query, content: null);

        if (!response.IsSuccessStatusCode)
        {
            var rawBody = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"Qwen3-TTS 合成失败: [{(int)response.StatusCode}] {rawBody}");
        }

        var audioData = await response.Content.ReadAsByteArrayAsync();
        return new TtsResult { AudioData = audioData, MediaType = "wav" };
    }

    /// <summary>真正有效的预热——不是空实现。把角色 NEUTRAL 片段的音色特征
    /// 提前提取好并缓存在 Python 服务那一侧，后续这个角色的每次
    /// /synthesize 调用命中缓存，跳过重新提特征这一步。调用方应该在
    /// "开始翻译"之前调一次，而不是等第一句话合成时才现提。</summary>
    public async Task PrewarmReferenceAudioAsync(int characterId)
    {
        var settings = _settingsService.Current;
        var targetLanguage = settings.TargetLanguage;

        var timbreClip = await _characterRepository.ResolveClipAsync(characterId, "NEUTRAL", targetLanguage);
        if (timbreClip is null)
        {
            // 没有参考音频就不用预热了，SynthesizeAsync 到时候会用更明确的
            // 报错信息告诉用户去录一段——这里静默跳过，不重复报错。
            return;
        }

        var response = await _httpClient.PostAsJsonAsync("/prewarm", new
        {
            spk_audio_path = timbreClip.AudioPath,
            ref_text = timbreClip.PromptText,
        });
        response.EnsureSuccessStatusCode();
    }

    public async Task StopAsync()
    {
        if (_process is null || _process.HasExited)
        {
            return;
        }

        try
        {
            var response = await _httpClient.PostAsync("/shutdown", null);
            response.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException)
        {
            // 请求本身失败（比如进程已经因为别的原因先挂了），走下面的兜底逻辑。
        }

        var exited = _process.WaitForExit(3000);
        if (!exited)
        {
            _process.Kill(entireProcessTree: true);
        }

        _process.Dispose();
        _process = null;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _httpClient.Dispose();
    }
}
