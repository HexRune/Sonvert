using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Sonvert.App.Settings;

namespace Sonvert.App.Services.SenseVoice;

/// <summary>
/// ISenseVoiceService 的实现：管理 Python 子进程的生命周期，并通过本地
/// HTTP 调用它暴露的识别接口。ViewModel 只依赖 ISenseVoiceService 这个
/// 接口，不需要知道这些细节。
/// </summary>
public class SenseVoiceService : ISenseVoiceService, IAsyncDisposable
{
    private readonly ISettingsService _settingsService;
    private readonly HttpClient _httpClient;

    // 保证 RecognizeAsync 不会被并发调用——Python 那边的模型推理是同步
    // 阻塞的，并发请求只会在队列里排队，不如从这边就直接串行化，
    // 语义更清楚，也避免调用方无意间发出并发请求导致的响应延迟看起来
    // "莫名其妙变长"。
    private readonly SemaphoreSlim _recognizeLock = new(1, 1);

    private Process? _process;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public SenseVoiceService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
        _httpClient = new HttpClient
        {
            // 端口来自设置，构造时确定一次。如果用户在设置里改了端口，
            // 需要重启一次服务（StopAsync 之后重新 StartAsync）才会生效——
            // 这个限制是合理的，改端口这种操作本来就应该要求重启子进程。
            BaseAddress = new Uri($"http://127.0.0.1:{_settingsService.Current.SenseVoicePort}"),
            Timeout = TimeSpan.FromSeconds(30), // 模型推理可能要几百毫秒到几秒，给足余量
        };
    }

    public async Task StartAsync()
    {
        if (_process is { HasExited: false })
        {
            // 已经在运行，不重复拉起。
            return;
        }

        // 这三项一起从设置里读——开发期是 "python.exe" + "main.py" 参数，
        // 打包后应该是安装程序写好的独立 exe 路径 + 空参数，见 AppSettings
        // 里这几个字段的注释。这里的代码完全不关心具体是哪种场景，
        // 只是老老实实按设置里给的值去启动进程。
        var executablePath = _settingsService.Current.SenseVoiceExecutablePath;
        var arguments = _settingsService.Current.SenseVoiceArguments;
        var workingDirectory = _settingsService.Current.SenseVoiceWorkingDirectory;

        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException(
                $"找不到 SenseVoiceService 可执行文件: {executablePath}，" +
                "检查设置里的 SenseVoiceExecutablePath 是否指向了正确的路径" +
                "（开发期应该是虚拟环境里的 python.exe，打包后是独立 exe）");
        }

        if (!Directory.Exists(workingDirectory))
        {
            throw new DirectoryNotFoundException(
                $"找不到 SenseVoiceService 工作目录: {workingDirectory}，" +
                "检查设置里的 SenseVoiceWorkingDirectory");
        }

        // service_config.json 是 Python 端 config.py 在进程启动时一次性读取的，
        // 不是运行时动态监听的文件，所以必须在 Process.Start 之前写好，
        // 写晚了端口不会生效。
        await WriteServiceConfigAsync(workingDirectory);

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true, // 不弹出黑色控制台窗口
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        // 把子进程的输出接到 Debug 日志里，方便开发时排查问题——
        // 之前手动在终端里跑 main.py 时看到的那些日志，现在会通过这里
        // 转发出来，而不是消失在一个看不见的后台进程里。
        _process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null) Debug.WriteLine($"[SenseVoiceService] {e.Data}");
        };
        _process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null) Debug.WriteLine($"[SenseVoiceService:ERR] {e.Data}");
        };

        _process.Start();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        await WaitUntilHealthyAsync();
    }

    private async Task WriteServiceConfigAsync(string serviceDir)
    {
        var configPath = Path.Combine(serviceDir, "service_config.json");
        var config = new
        {
            port = _settingsService.Current.SenseVoicePort,
            host = "127.0.0.1",
        };
        await File.WriteAllTextAsync(configPath, JsonSerializer.Serialize(config));
    }

    private async Task WaitUntilHealthyAsync()
    {
        // 模型库导入 + FastAPI 启动一般很快（不到 1 秒），但给足余量：
        // 每 500ms 探测一次，最多等 15 秒，超时就认为启动失败。
        const int maxAttempts = 30;
        const int delayMs = 500;

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            try
            {
                var response = await _httpClient.GetAsync("/health");
                if (response.IsSuccessStatusCode)
                {
                    return; // 服务就绪
                }
            }
            catch (HttpRequestException)
            {
                // 进程可能还没起来、端口还没开始监听，这是预期内的瞬时失败，
                // 忽略，继续重试。
            }

            await Task.Delay(delayMs);
        }

        throw new TimeoutException(
            $"SenseVoiceService 启动超时（等待 {maxAttempts * delayMs / 1000} 秒仍未就绪），" +
            "检查子进程日志（Debug 输出）确认具体报错");
    }

    public Task LoadModelAsync(ModelPrecision precision)
    {
        var request = LoadModelRequest.From(precision);
        return PostAsync<LoadModelRequest, LoadModelResponse>("/model/load", request);
    }

    public async Task UnloadModelAsync()
    {
        var response = await _httpClient.PostAsync("/model/unload", null);
        await EnsureSuccessAsync(response);
    }

    public async Task<RecognitionResult> RecognizeAsync(
        byte[] pcmBytes, string language = "auto", bool useItn = true)
    {
        // 串行化：同一时间只允许一次识别请求在途，见类顶部注释说明。
        await _recognizeLock.WaitAsync();
        try
        {
            var url = $"/recognize?language={Uri.EscapeDataString(language)}&use_itn={useItn}";
            using var content = new ByteArrayContent(pcmBytes);
            content.Headers.ContentType =
                new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");

            var response = await _httpClient.PostAsync(url, content);
            await EnsureSuccessAsync(response);

            var result = await response.Content.ReadFromJsonAsync<RecognitionResult>(JsonOptions);
            return result ?? throw new InvalidOperationException("识别结果反序列化失败（响应体为空）");
        }
        finally
        {
            _recognizeLock.Release();
        }
    }

    public async Task StopAsync()
    {
        if (_process is null || _process.HasExited)
        {
            return;
        }

        try
        {
            // 正常路径：请求 Python 端优雅退出。main.py 里的 /shutdown 会先
            // 把这个响应发出去，再异步延迟退出，所以这个请求预期能拿到
            // 正常的 200 响应，不会因为进程提前退出而连接中断报错。
            var response = await _httpClient.PostAsync("/shutdown", null);
            await EnsureSuccessAsync(response);
        }
        catch (HttpRequestException)
        {
            // 请求本身失败（比如进程已经因为别的原因先挂了），
            // 走下面的兜底逻辑，不让这里的异常影响停止流程。
        }

        // 给进程一点时间自己退出，超时了再强制 Kill，避免变成后台孤儿进程。
        var exited = _process.WaitForExit(3000);
        if (!exited)
        {
            _process.Kill();
        }

        _process.Dispose();
        _process = null;
    }

    private async Task<TResponse> PostAsync<TRequest, TResponse>(string url, TRequest body)
    {
        var response = await _httpClient.PostAsJsonAsync(url, body);
        await EnsureSuccessAsync(response);
        var result = await response.Content.ReadFromJsonAsync<TResponse>(JsonOptions);
        return result ?? throw new InvalidOperationException($"{url} 响应反序列化失败（响应体为空）");
    }

    /// <summary>
    /// Python 端约定：出错时返回对应 HTTP 状态码 + {"error": "..."}，不是统一 200。
    /// 这里统一处理，把 Python 的错误信息包进 .NET 异常里，方便上层直接看到
    /// 具体是什么问题（比如"model not loaded"），不用自己再解析响应体。
    /// </summary>
    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string errorMessage;
        try
        {
            var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(JsonOptions);
            errorMessage = error?.Error ?? "(未知错误，响应体解析失败)";
        }
        catch (JsonException)
        {
            errorMessage = await response.Content.ReadAsStringAsync();
        }

        throw new SenseVoiceServiceException(response.StatusCode, errorMessage);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _httpClient.Dispose();
        _recognizeLock.Dispose();
    }
}

/// <summary>
/// SenseVoiceService 调用失败时抛出的异常，带上 Python 端返回的 HTTP 状态码
/// 和错误信息，方便上层（比如 ViewModel）区分处理，例如 409 对应"模型未加载"。
/// </summary>
public class SenseVoiceServiceException : Exception
{
    public System.Net.HttpStatusCode StatusCode { get; }

    public SenseVoiceServiceException(System.Net.HttpStatusCode statusCode, string message)
        : base($"[{(int)statusCode}] {message}")
    {
        StatusCode = statusCode;
    }
}
