using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Sonvert.App.Services.Audio;
using Sonvert.App.Services.Characters;
using Sonvert.App.Settings;

namespace Sonvert.App.Services.Tts;

/// <summary>
/// 本地 TTS 引擎的第二个实现，对接自建的 Sonvert.IndexTTSService（跟
/// SenseVoiceService/MTService 同一套自建微服务模式：独立进程 + FastAPI，
/// 不是调 GPT-SoVITS 那种官方脚本）。
///
/// 跟 LocalTtsService（GPT-SoVITS）最核心的设计差异：GPT-SoVITS 一条参考
/// 音频同时决定音色和语调，所以要为每个角色预录"7种情绪 x 2种语言"一整套
/// 参考库；IndexTTS2 把音色（spk_audio_prompt）和情绪（emo_audio_prompt）
/// 解耦成两个独立参数，这里改成：
///   - 音色参考：复用角色的 NEUTRAL 情绪片段（跟 GPT-SoVITS 共用同一份
///     录音，不用让用户重新录）。
///   - 情绪参考：直接用这句话识别时的原始语音（liveEmotionAudio），而不是
///     从预录情绪库里按标签选——这是这次接入 IndexTTS 的主要动机：保留
///     每一句话实际的情绪细节，而不是分到 7 个粗粒度的桶里。
///
/// 原始语音太短或者调用方没传（liveEmotionAudio 为 null）时，退回官方的
/// 8 维 emo_vector，按 SenseVoice 的情绪标签映射成对应维度设为 1，而不是
/// 完全不传情绪信息——具体映射表见 EmotionToVector。
/// </summary>
public class IndexTtsService : ITtsService, IAsyncDisposable
{
    private readonly ISettingsService _settingsService;
    private readonly ICharacterRepository _characterRepository;
    private readonly HttpClient _httpClient;
    private Process? _process;

    // 原始语音短于这个时长，认为"太短，情绪信息不可靠"，改用 emo_vector
    // 兜底。0.5 秒是经验值——比一个字的最短发音时间长，但没长到会把
    // 正常的短句（比如"好的"、"谢谢"）也误判成异常。
    private const double MinLiveEmotionAudioSeconds = 0.5;

    public IndexTtsService(ISettingsService settingsService, ICharacterRepository characterRepository)
    {
        _settingsService = settingsService;
        _characterRepository = characterRepository;
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri($"http://127.0.0.1:{_settingsService.Current.IndexTtsPort}"),
            // IndexTTS2 单句合成实测比 GPT-SoVITS 慢一些（尤其是还没做过
            // 优化的 attention backend），超时给宽松一点，避免正常合成
            // 被误判成超时。真正影响实时性的是"合成到底要多久"这个问题
            // 本身，超时时间只是不要因为设短了而帮倒忙。
            Timeout = TimeSpan.FromSeconds(90),
        };
    }

    public async Task StartAsync()
    {
        if (_process is { HasExited: false })
        {
            return;
        }

        var settings = _settingsService.Current;
        var executablePath = settings.IndexTtsExecutablePath;
        var workingDirectory = settings.IndexTtsWorkingDirectory;
        var modelsDirectory = settings.IndexTtsModelsDirectory;

        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            throw new FileNotFoundException(
                $"找不到 IndexTTSService 的可执行文件: {executablePath}，" +
                "检查设置里的 IndexTtsExecutablePath");
        }

        if (string.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory))
        {
            throw new DirectoryNotFoundException(
                $"找不到 IndexTTSService 工作目录: {workingDirectory}，检查设置里的 IndexTtsWorkingDirectory");
        }

        if (string.IsNullOrWhiteSpace(modelsDirectory) || !Directory.Exists(modelsDirectory))
        {
            throw new DirectoryNotFoundException(
                $"找不到 IndexTTS 模型目录: {modelsDirectory}，检查设置里的 IndexTtsModelsDirectory" +
                "（应该指向你训练/下载好的 checkpoints 目录，包含 config.yaml/gpt.pth 等文件）");
        }

        await WriteServiceConfigAsync(workingDirectory, settings);

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = settings.IndexTtsArguments,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        _process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null) Debug.WriteLine($"[IndexTTSService] {e.Data}");
        };
        _process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null) Debug.WriteLine($"[IndexTTSService:ERR] {e.Data}");
        };

        _process.Start();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        await WaitUntilHealthyAsync();
    }

    /// <summary>跟 SenseVoiceService/MTService 同样的模式——把端口/模型目录
    /// 写进 exe 旁边的 service_config.json，Python 端启动时读取。这个服务
    /// 直接照抄 SenseVoiceService 那次踩过的坑，config.py 从一开始就用
    /// sys.executable 定位（而不是 __file__），不会重复那个"打包后读不到
    /// 配置，安静地退回默认值"的问题。</summary>
    private async Task WriteServiceConfigAsync(string workingDirectory, AppSettings settings)
    {
        var configPath = Path.Combine(workingDirectory, "service_config.json");
        var config = new
        {
            port = settings.IndexTtsPort,
            host = "127.0.0.1",
            resource_dir = Path.GetFullPath(settings.IndexTtsModelsDirectory),
            version = settings.IndexTtsVersion,
        };
        await File.WriteAllTextAsync(configPath, System.Text.Json.JsonSerializer.Serialize(config));
    }

    private async Task WaitUntilHealthyAsync()
    {
        const int maxAttempts = 60; // IndexTTS2 加载模型（含几个辅助模型）比 SenseVoice/GPT-SoVITS 慢，多给点时间
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

        throw new TimeoutException(
            $"IndexTTSService 启动超时（等待 {maxAttempts * delayMs / 1000} 秒仍未就绪）");
    }

    public async Task<TtsResult> SynthesizeAsync(
        string text, string language, string emotion,
        float[]? liveEmotionAudio = null, int liveEmotionSampleRate = 0)
    {
        var settings = _settingsService.Current;

        if (settings.ActiveCharacterId is not { } characterId)
        {
            throw new InvalidOperationException("还没选择角色，先在首页选一个角色再开始翻译");
        }

        // 音色参考：复用 GPT-SoVITS 那套 NEUTRAL 片段，不用另外维护一份。
        var timbreClip = await _characterRepository.ResolveClipAsync(characterId, "NEUTRAL", language);
        if (timbreClip is null)
        {
            throw new InvalidOperationException(
                $"角色 {characterId} 还没有任何参考音频（中文/英文的 NEUTRAL 至少要录一个），先去\"声音克隆\"页面录一段");
        }

        var queryBuilder = new StringBuilder(
            $"/synthesize?text={Uri.EscapeDataString(text)}" +
            $"&lang={Uri.EscapeDataString(language)}" +
            $"&spk_audio_path={Uri.EscapeDataString(timbreClip.AudioPath)}" +
            $"&emo_alpha={settings.TTSEmoAlpha.ToString(System.Globalization.CultureInfo.InvariantCulture)}");

        HttpContent content;
        var liveAudioUsable = liveEmotionAudio is { Length: > 0 }
            && liveEmotionAudio.Length / (double)liveEmotionSampleRate >= MinLiveEmotionAudioSeconds;

        // 诊断日志——上一次排查发现光看 IndexTTS 自己打的
        // "Use the specified emotion vector" 只能确认走了 emo_vector 分支，
        // 没法确认 emo_audio_prompt 分支是不是真的收到了数据，这里把
        // 判断依据直接打出来，比继续靠猜强。确认问题定位之后可以删掉。
        var durationSeconds = liveEmotionAudio is { Length: > 0 } && liveEmotionSampleRate > 0
            ? liveEmotionAudio.Length / (double)liveEmotionSampleRate
            : (double?)null;
        Debug.WriteLine(
            $"[IndexTtsService] liveEmotionAudio.Length={liveEmotionAudio?.Length ?? -1}, " +
            $"sampleRate={liveEmotionSampleRate}, durationSeconds={durationSeconds?.ToString("F2") ?? "n/a"}, " +
            $"liveAudioUsable={liveAudioUsable}");

        if (liveAudioUsable)
        {
            // 情绪参考走请求体传字节——每句话的原始音频都不一样，不是
            // 持久化在磁盘上的固定文件，传字节比"C#先写临时文件、传路径、
            // 再删临时文件"更直接，也少了一层"删的时机万一晚了会不会
            // 撞车"的顾虑。
            var wavBytes = WavEncoder.EncodeFloatSamplesToWav(liveEmotionAudio!, liveEmotionSampleRate);
            Debug.WriteLine($"[IndexTtsService] 用实时原始音频当情绪参考，wav 字节数={wavBytes.Length}");
            content = new ByteArrayContent(wavBytes);
        }
        else
        {
            // 原始音频太短或者没传：退回官方 emo_vector，而不是完全不传
            // 情绪信息——具体映射见 EmotionToVector 的注释。
            var vector = EmotionToVector(emotion);
            Debug.WriteLine($"[IndexTtsService] 退回 emo_vector 兜底: [{string.Join(",", vector)}]");
            queryBuilder.Append($"&emo_vector={Uri.EscapeDataString(string.Join(",", vector))}");
            content = new ByteArrayContent(Array.Empty<byte>());
        }

        var response = await _httpClient.PostAsync(queryBuilder.ToString(), content);

        if (!response.IsSuccessStatusCode)
        {
            var rawBody = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"IndexTTS 合成失败: [{(int)response.StatusCode}] {rawBody}");
        }

        var audioData = await response.Content.ReadAsByteArrayAsync();
        return new TtsResult { AudioData = audioData, MediaType = "wav" };
    }

    /// <summary>SenseVoice 的 7 种情绪标签 -> IndexTTS2 官方 8 维 emo_vector
    /// （顺序固定：[happy, angry, sad, afraid, disgusted, melancholic,
    /// surprised, calm]，这个顺序是 IndexTTS2 自己定义的，不能改）。
    /// melancholic 这一维 SenseVoice 没有对应标签，恒为 0——没有强行找一个
    /// 近似标签往上凑，蹭出来的映射比"就是没有"更容易在实际效果上出问题。
    /// NEUTRAL 和识别不出情绪时的 EMO_UNKNOWN 都映射到 calm，这两种情况
    /// 语义上都是"平常语气"，没必要区分对待。</summary>
    private static double[] EmotionToVector(string emotion) => emotion switch
    {
        "HAPPY" => new[] { 1.0, 0, 0, 0, 0, 0, 0, 0 },
        "ANGRY" => new[] { 0, 1.0, 0, 0, 0, 0, 0, 0 },
        "SAD" => new[] { 0, 0, 1.0, 0, 0, 0, 0, 0 },
        "FEARFUL" => new[] { 0, 0, 0, 1.0, 0, 0, 0, 0 },
        "DISGUSTED" => new[] { 0, 0, 0, 0, 1.0, 0, 0, 0 },
        "SURPRISED" => new[] { 0, 0, 0, 0, 0, 0, 1.0, 0 },
        _ => new[] { 0, 0, 0, 0, 0, 0, 0, 1.0 }, // NEUTRAL / EMO_UNKNOWN / 其他未知标签 -> calm
    };

    public Task PrewarmReferenceAudioAsync(int characterId)
    {
        // IndexTTS2 不像 GPT-SoVITS 有专门的 /set_refer_audio 预热接口——
        // 音色参考和情绪参考都是每次 /synthesize 请求内联传入的，没有
        // "预先设置、后续复用"这种服务端状态，自然也没有对应的预热动作。
        // 保留这个方法是因为 ITtsService 接口要求实现它，调用方
        // （PrewarmReferenceAudioAsync 的调用点）不需要关心具体引擎是否
        // 真的做了什么。
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (_process is null || _process.HasExited)
        {
            return;
        }

        try
        {
            // 正常路径：请求 Python 端优雅退出，跟 SenseVoiceService 一样的
            // 模式（main.py 里的 /shutdown 会先把响应发出去，再异步延迟
            // 退出）。这里不用 LocalTtsService 那种直接 Kill 的写法——那是
            // 因为 GPT-SoVITS 调的是官方脚本，没有这个接口；IndexTTSService
            // 是我们自己写的，理应支持优雅关闭。
            var response = await _httpClient.PostAsync("/shutdown", null);
            response.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException)
        {
            // 请求本身失败（比如进程已经因为别的原因先挂了），走下面的
            // 兜底逻辑，不让这里的异常影响停止流程。
        }

        // 给进程一点时间自己退出，超时了再强制 Kill，避免变成后台孤儿进程。
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
