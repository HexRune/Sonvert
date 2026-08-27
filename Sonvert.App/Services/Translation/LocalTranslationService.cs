using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Sonvert.App.Settings;

namespace Sonvert.App.Services.Translation;

/// <summary>
/// ITranslationService 的本地实现：跟 SenseVoiceService 一样，管理一个
/// Python 子进程（Sonvert.MTService）的生命周期，通过本地 HTTP 调用它。
/// 没有做识别那边的并发串行化处理——翻译请求量小、耗时短，暂时不需要。
/// </summary>
public class LocalTranslationService : ITranslationService, IAsyncDisposable
{
    private readonly ISettingsService _settingsService;
    private readonly HttpClient _httpClient;
    private readonly IGlossaryRepository _glossaryRepository;
    private Process? _process;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public LocalTranslationService(ISettingsService settingsService, IGlossaryRepository glossaryRepository)
    {
        _settingsService = settingsService;
        _glossaryRepository = glossaryRepository;
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri($"http://127.0.0.1:{_settingsService.Current.MTPort}"),
            Timeout = TimeSpan.FromSeconds(30),
        };
    }

    public async Task LoadModelAsync()
    {
        var response = await _httpClient.PostAsync("/model/load", null);
        await EnsureSuccessAsync(response);
    }

    public async Task StartAsync()
    {
        if (_process is { HasExited: false })
        {
            return;
        }

        var executablePath = _settingsService.Current.MTExecutablePath;
        var arguments = _settingsService.Current.MTArguments;
        var workingDirectory = _settingsService.Current.MTWorkingDirectory;

        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException(
                $"找不到 MTService 可执行文件: {executablePath}，" +
                "检查设置里的 MTExecutablePath（开发期应指向虚拟环境里的 python.exe）");
        }

        if (!Directory.Exists(workingDirectory))
        {
            throw new DirectoryNotFoundException(
                $"找不到 MTService 工作目录: {workingDirectory}，检查设置里的 MTWorkingDirectory");
        }

        await WriteServiceConfigAsync(workingDirectory);

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
            if (e.Data != null) Debug.WriteLine($"[MTService] {e.Data}");
        };
        _process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null) Debug.WriteLine($"[MTService:ERR] {e.Data}");
        };

        _process.Start();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        await WaitUntilHealthyAsync();
    }

    private async Task WriteServiceConfigAsync(string serviceDir)
    {
        var configPath = Path.Combine(serviceDir, "service_config.json");
        var config = new { port = _settingsService.Current.MTPort, host = "127.0.0.1" };
        await File.WriteAllTextAsync(configPath, JsonSerializer.Serialize(config));
    }

    private async Task WaitUntilHealthyAsync()
    {
        // 首次调用要懒加载模型，但 OPUS-MT 模型很小，FastAPI 启动本身
        // 不到 1 秒，这里沿用 SenseVoiceService 同样的探测节奏就够了。
        const int maxAttempts = 30;
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
                // 进程可能还没起来，忽略，继续重试。
            }

            await Task.Delay(delayMs);
        }

        throw new TimeoutException(
            $"MTService 启动超时（等待 {maxAttempts * delayMs / 1000} 秒仍未就绪）");
    }

    public async Task<TranslationResult> TranslateAsync(
        string text, string sourceLanguage, string targetLanguage)
    {
        var textToTranslate = text;
  
        if (_settingsService.Current.GlossaryEnabled)
        {
            var glossary = await _glossaryRepository.GetAllAsync();
            textToTranslate = GlossaryReplacer.Replace(text, glossary);
        }
        var request = new TranslateRequest
        {
            Text = textToTranslate,
            SourceLang = sourceLanguage,
            TargetLang = targetLanguage,
        };

        var response = await _httpClient.PostAsJsonAsync("/translate", request);
        await EnsureSuccessAsync(response);

        var result = await response.Content.ReadFromJsonAsync<TranslateResponse>(JsonOptions);
        if (result is null)
        {
            throw new InvalidOperationException("翻译结果反序列化失败（响应体为空）");
        }

        return new TranslationResult { TranslatedText = result.TranslatedText };
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
            await EnsureSuccessAsync(response);
        }
        catch (HttpRequestException)
        {
            // 进程可能已经因为别的原因先挂了，走下面的兜底逻辑。
        }

        var exited = _process.WaitForExit(3000);
        if (!exited)
        {
            _process.Kill();
        }

        _process.Dispose();
        _process = null;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;

        string errorMessage;
        try
        {
            var error = await response.Content.ReadFromJsonAsync<SenseVoice.ErrorResponse>(JsonOptions);
            errorMessage = error?.Error ?? "(未知错误，响应体解析失败)";
        }
        catch (JsonException)
        {
            errorMessage = await response.Content.ReadAsStringAsync();
        }

        throw new InvalidOperationException($"MTService 请求失败: [{(int)response.StatusCode}] {errorMessage}");
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _httpClient.Dispose();
    }
}