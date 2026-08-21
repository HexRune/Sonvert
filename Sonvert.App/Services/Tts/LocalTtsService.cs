using Sonvert.App.Settings;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

namespace Sonvert.App.Services.Tts;

/// <summary>
/// ITtsService 的本地实现：拉起 GPT-SoVITS 官方 api_v2.py 作为子进程，
/// 通过 HTTP 调用它的 /tts 接口。生命周期管理模式跟 SenseVoiceService/
/// LocalTranslationService 一致（启动子进程 -> 探测就绪 -> 调接口 -> 关闭）。
/// </summary>
public class LocalTtsService : ITtsService, IAsyncDisposable
{
    private readonly ISettingsService _settingsService;
    private readonly HttpClient _httpClient;
    private Process? _process;

    public LocalTtsService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
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
            if (e.Data != null) Debug.WriteLine($"[TTSService] {e.Data}"); // GPT-SoVITS 的正常日志走 stderr，不额外标 :ERR，避免误导
        };

        _process.Start();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        await WaitUntilHealthyAsync();
    }

    private async Task WaitUntilHealthyAsync()
    {
        // api_v2.py 没有单独的 /health 接口，用 FastAPI 自带的 /docs 页面探测。
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
                // 进程可能还没起来，忽略，继续重试。
            }

            await Task.Delay(delayMs);
        }

        throw new TimeoutException(
            $"GPT-SoVITS 服务启动超时（等待 {maxAttempts * delayMs / 1000} 秒仍未就绪）");
    }

    public async Task<TtsResult> SynthesizeAsync(string text, string language, string emotion)
    {
        var settings = _settingsService.Current;
        var byEmotion = settings.TTSReferenceAudioByEmotion;

        if (!byEmotion.TryGetValue(emotion, out var clip) || string.IsNullOrWhiteSpace(clip.AudioPath))
        {
            // 没为这个情绪单独配参考音频，退回中性，不直接报错——
            // 让合成正常继续比因为缺一个情绪标签就中断整条流水线更重要。
            if (!byEmotion.TryGetValue("NEUTRAL", out clip) || string.IsNullOrWhiteSpace(clip.AudioPath))
            {
                throw new InvalidOperationException(
                    "还没配置任何参考音频（至少要配置 NEUTRAL），先在设置里选一段主播的语音样本");
            }
        }

        var requestBody = new Dictionary<string, object>
        {
            ["text"] = text,
            ["text_lang"] = language,
            ["ref_audio_path"] = clip.AudioPath,
            ["prompt_text"] = clip.PromptText,
            ["prompt_lang"] = settings.TTSReferenceAudioLanguage,
            ["media_type"] = "wav",
            ["streaming_mode"] = false,
        };

        var response = await _httpClient.PostAsJsonAsync("/tts", requestBody);

        if (!response.IsSuccessStatusCode)
        {
            string errorMessage;
            try
            {
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                errorMessage = doc.RootElement.TryGetProperty("message", out var msg)
                    ? msg.GetString() ?? "(未知错误)"
                    : "(未知错误，响应体解析失败)";
            }
            catch (JsonException)
            {
                errorMessage = await response.Content.ReadAsStringAsync();
            }

            throw new InvalidOperationException(
                $"TTS 合成失败: [{(int)response.StatusCode}] {errorMessage}");
        }

        var audioData = await response.Content.ReadAsByteArrayAsync();
        return new TtsResult { AudioData = audioData, MediaType = "wav" };
    }

    public async Task StopAsync()
    {
        if (_process is null || _process.HasExited)
        {
            return;
        }

        // api_v2.py 没有 /shutdown 接口，直接杀进程（连带杀掉它可能起的子进程）。
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