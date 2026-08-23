using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Sonvert.App.Services.Characters;
using Sonvert.App.Settings;

namespace Sonvert.App.Services.Tts;

public class LocalTtsService : ITtsService, IAsyncDisposable
{
    private readonly ISettingsService _settingsService;
    private readonly ICharacterRepository _characterRepository;
    private readonly HttpClient _httpClient;
    private Process? _process;

    public LocalTtsService(ISettingsService settingsService, ICharacterRepository characterRepository)
    {
        _settingsService = settingsService;
        _characterRepository = characterRepository;
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri($"http://127.0.0.1:{_settingsService.Current.TTSPort}"),
            Timeout = TimeSpan.FromSeconds(60),
        };
    }

    public async Task StartAsync()
    {
        if (_process is { HasExited: false })
        {
            return;
        }

        var executablePath = _settingsService.Current.TTSExecutablePath;
        var arguments = _settingsService.Current.TTSArguments;
        var workingDirectory = _settingsService.Current.TTSWorkingDirectory;

        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            throw new FileNotFoundException(
                $"找不到 GPT-SoVITS 的 python.exe: {executablePath}，" +
                "先在设置里把 TTSExecutablePath 指向整合包里的 runtime\\python.exe");
        }

        if (string.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory))
        {
            throw new DirectoryNotFoundException(
                $"找不到 GPT-SoVITS 工作目录: {workingDirectory}，检查设置里的 TTSWorkingDirectory");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        _process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null) Debug.WriteLine($"[TTSService] {e.Data}");
        };
        _process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null) Debug.WriteLine($"[TTSService] {e.Data}");
        };

        _process.Start();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        await WaitUntilHealthyAsync();
    }

    private async Task WaitUntilHealthyAsync()
    {
        const int maxAttempts = 40;
        const int delayMs = 500;

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            try
            {
                var response = await _httpClient.GetAsync("/docs");
                if (response.IsSuccessStatusCode) return;
            }
            catch (HttpRequestException)
            {
                // 忽略，继续重试。
            }

            await Task.Delay(delayMs);
        }

        throw new TimeoutException(
            $"GPT-SoVITS 服务启动超时（等待 {maxAttempts * delayMs / 1000} 秒仍未就绪）");
    }

    private async Task<TtsResult> SynthesizeOnceAsync(
    string text, string language, string audioPath, string promptText, string promptLang)
    {
        var requestBody = new Dictionary<string, object>
        {
            ["text"] = text,
            ["text_lang"] = language,
            ["ref_audio_path"] = audioPath,
            ["prompt_text"] = promptText,
            ["prompt_lang"] = promptLang,
            ["media_type"] = "wav",
            ["streaming_mode"] = false,
            ["seed"] = -1, // -1 表示每次随机取样,配合重试机制，让重试时有机会抽到不同结果，
                           // 而不是每次都用同一个随机种子导致重试也复现同样的失败
        };

        var response = await _httpClient.PostAsJsonAsync("/tts", requestBody);

        if (!response.IsSuccessStatusCode)
        {
            var rawBody = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"TTS 合成失败: [{(int)response.StatusCode}] {rawBody}");
        }

        var audioData = await response.Content.ReadAsByteArrayAsync();
        return new TtsResult { AudioData = audioData, MediaType = "wav" };
    }
    public async Task<TtsResult> SynthesizeAsync(string text, string language, string emotion)
    {
        var settings = _settingsService.Current;

        if (settings.ActiveCharacterId is not { } characterId)
        {
            throw new InvalidOperationException("还没选择角色，先在首页选一个角色再开始翻译");
        }

        var resolvedClip = await _characterRepository.ResolveClipAsync(characterId, emotion);
        if (resolvedClip is null)
        {
            throw new InvalidOperationException(
                $"角色 {characterId} 还没有任何参考音频（至少要录制 NEUTRAL），先去\"声音克隆\"页面录一段");
        }

        const int maxAttempts = 3;
        TtsResult? lastResult = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var result = await SynthesizeOnceAsync(
                text, language, resolvedClip.AudioPath, resolvedClip.PromptText,
                settings.TTSReferenceAudioLanguage);

            lastResult = result;

            if (!IsAudioSuspiciouslyShort(result.AudioData, text))
            {
                return result; // 时长正常，直接用这次的结果
            }

            System.Diagnostics.Debug.WriteLine(
                $"[TTS] 第 {attempt} 次合成结果时长异常偏短（疑似提前截断），重试...");
        }

        // 重试次数用完还是异常，只能用最后一次的结果——总比完全没有声音要好，
        // 这种情况在调试阶段应该很少见（3 次都撞上概率性失败的可能性很低）。
        return lastResult!;
    }

    /// <summary>
    /// 粗略判断：正常语速下每个字符大致对应的最短时长（留了较宽松的余量，
    /// 避免把语速偏快的正常结果也误判成异常）。这不是精确公式，只是用来
    /// 兜底识别"明显被提前截断"这种数量级的异常，不需要特别精确。
    /// </summary>
    private static bool IsAudioSuspiciouslyShort(byte[] wavBytes, string text)
    {
        var duration = GetWavDurationSeconds(wavBytes);
        var minExpectedDuration = text.Length * 0.06; // 每字符至少 60ms，比正常语速快很多，留足余量
        return duration < minExpectedDuration;
    }

    private static double GetWavDurationSeconds(byte[] wavBytes)
    {
        using var stream = new System.IO.MemoryStream(wavBytes);
        using var reader = new NAudio.Wave.WaveFileReader(stream);
        return reader.TotalTime.TotalSeconds;
    }

    public async Task StopAsync()
    {
        if (_process is null || _process.HasExited)
        {
            return;
        }

        try
        {
            _process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // 进程可能已经自己退出了，忽略。
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