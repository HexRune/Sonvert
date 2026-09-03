using Sonvert.App.Models;
using Sonvert.App.Settings;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

namespace Sonvert.App.Services.Translation;

/// <summary>
/// ITranslationService 的第三方 API 实现。调用协议统一按 OpenAI Chat
/// Completions 格式发起（POST {Endpoint}/chat/completions，Bearer Token
/// 鉴权）——DeepSeek、豆包(火山方舟)、OpenAI 官方等主流服务商都兼容这套
/// 格式，因此换服务商只需要改 AppSettings 里 TranslationApiEndpoint/
/// TranslationApiKey/TranslationApiModel 三个字段，这个类本身不用改。
///
/// TranslationApiEndpoint 约定填"到版本号为止的 base url"，不包含
/// "/chat/completions"这段路径，例如：
///   - OpenAI:   https://api.openai.com/v1
///   - DeepSeek: https://api.deepseek.com/v1
///   - 豆包:     https://ark.cn-beijing.volces.com/api/v3
/// 由本类统一拼接末尾路径。
///
/// 当前是测试接入阶段的最小实现：每次翻译新建一次 HTTP 请求，不做重试、
/// 不做流式输出、温度写死。等确定要长期使用某个服务商、验证过效果和延迟
/// 之后，再考虑要不要加超时重试、流式、以及把 temperature 开放到设置里。
/// </summary>
public class ApiTranslationService : ITranslationService
{
    private readonly ISettingsService _settingsService;
    private readonly IGlossaryRepository _glossaryRepository;
    private readonly HttpClient _httpClient;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public ApiTranslationService(ISettingsService settingsService, IGlossaryRepository glossaryRepository)
    {
        _settingsService = settingsService;
        // 复用术语表仓储，跟 LocalTranslationService 一样在翻译前做替换，
        // 保证本地/API 两种模式下"术语表"这个功能的行为一致。
        _glossaryRepository = glossaryRepository;

        // 不在这里设 BaseAddress：Endpoint 是运行时可在设置里改的，
        // 每次请求时直接拼完整 URL，避免"改了设置但 HttpClient 里
        // 缓存的旧 BaseAddress 没更新"这种坑。
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
    }

    // ---- 生命周期方法 ----
    // API 实现没有本地子进程、没有本地模型可加载，这三个方法在
    // ITranslationService 接口里是必须实现的，但对 API 模式来说
    // 都是空操作——保留方法体只是为了满足接口，注释写清楚"为什么是空的"，
    // 避免以后看代码的人以为是漏写了。
    public Task StartAsync() => Task.CompletedTask; // 调远程 API，不需要拉子进程

    public Task LoadModelAsync() => Task.CompletedTask; // 远程 API 不需要本地预加载

    public Task StopAsync() => Task.CompletedTask; // 同上，没有子进程可关

    public async Task<TranslationResult> TranslateAsync(
        string text, string sourceLanguage, string targetLanguage)
    {
        var settings = _settingsService.Current;

        // 第一步：校验配置是否齐全。三项缺一不可，缺了就没法拼请求，
        // 提前抛出比等到 HTTP 层报"401"或"host not found"更容易定位问题。
        if (string.IsNullOrWhiteSpace(settings.TranslationApiEndpoint) ||
            string.IsNullOrWhiteSpace(settings.TranslationApiKey) ||
            string.IsNullOrWhiteSpace(settings.TranslationApiModel))
        {
            throw new InvalidOperationException(
                "第三方翻译 API 未配置完整。请在设置里填写 TranslationApiEndpoint / " +
                "TranslationApiKey / TranslationApiModel 三项。");
        }

        // 第二步：术语表替换（跟 LocalTranslationService 逻辑完全一致）。
        // 保证不管走本地模型还是第三方 API，术语表这个功能的行为都一样，
        // 用户切换 Provider 时不会感知到术语替换"时有时无"。
        var textToTranslate = text;
        if (settings.GlossaryEnabled)
        {
            var glossary = await _glossaryRepository.GetAllAsync();
            textToTranslate = GlossaryReplacer.Replace(text, glossary);
        }

        // 第三步：组装 OpenAI 兼容协议的请求体。
        // system 消息负责"定规矩"（角色设定+只输出译文的约束），
        // user 消息就是要翻译的原文本身——这是最基础的两条消息结构，
        // 没有做多轮上下文（不需要，每句字幕独立翻译）。
        var systemPrompt = BuildSystemPrompt(sourceLanguage, targetLanguage);
        var request = new ChatCompletionRequest
        {
            Model = settings.TranslationApiModel,
            Messages = new List<ChatMessage>
            {
                new() { Role = "system", Content = systemPrompt },
                new() { Role = "user", Content = textToTranslate },
            },
        };

        // 第四步：拼 URL + 鉴权头。TrimEnd('/') 是防用户在设置里
        // 多打了一个结尾斜杠（比如填成 ".../v1/"），避免拼出 "v1//chat/completions"
        // 这种双斜杠路径导致某些网关 404。
        var url = $"{settings.TranslationApiEndpoint.TrimEnd('/')}/chat/completions";
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(request),
        };
        httpRequest.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.TranslationApiKey);

        // 第五步：发请求，同时用 Stopwatch 掐表——这就是"测延迟"的全部实现，
        // 不需要额外工具，调用结束后把耗时打到 Debug 输出窗口，
        // 顺便也塞进返回值里（ElapsedMilliseconds），方便以后 UI 要展示的话直接取。
        var stopwatch = Stopwatch.StartNew();
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(httpRequest);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // 网络层异常（连不上/超时）单独包一层，跟"服务端返回了错误状态码"
            // （EnsureSuccessAsync 那种）区分开，方便排查是网络问题还是配置问题。
            throw new InvalidOperationException($"调用翻译 API 失败（网络层）: {ex.Message}", ex);
        }

        // 第六步：先检查 HTTP 状态码，非 2xx 直接在这里抛异常并终止，
        // 不会走到下面的反序列化逻辑。
        await EnsureSuccessAsync(response);

        // 第七步：反序列化响应体，取 choices[0].message.content 作为译文。
        // 计时在这里停止（包含了反序列化耗时，属于"从发出请求到拿到可用译文"
        // 的完整耗时，跟用户实际感知到的延迟一致）。
        var result = await response.Content.ReadFromJsonAsync<ChatCompletionResponse>(JsonOptions);
        stopwatch.Stop();

        var translatedText = result?.Choices.Count > 0
            ? result.Choices[0].Message.Content.Trim()
            : null;

        if (string.IsNullOrEmpty(translatedText))
        {
            throw new InvalidOperationException("翻译 API 返回结果为空（choices 为空或反序列化失败）");
        }

        // 把每次调用的模型名+耗时打到 VS 输出窗口，测试阶段肉眼观察
        // 不同服务商/不同模型的延迟差异，不需要额外接监控工具。
        Debug.WriteLine($"[ApiTranslation] model={settings.TranslationApiModel} " +
                         $"elapsed={stopwatch.ElapsedMilliseconds}ms");

        return new TranslationResult
        {
            TranslatedText = translatedText,
            ElapsedMilliseconds = stopwatch.ElapsedMilliseconds,
        };
    }

    /// <summary>把 "zh"/"en" 这类简写转成大模型更容易理解的自然语言指令，
    /// 并明确要求"只输出译文"——大模型比专用翻译模型更容易"多嘴"
    /// （加解释、加引号、复述原文），这里在 prompt 层面先约束住。</summary>
    private static string BuildSystemPrompt(string sourceLanguage, string targetLanguage)
    {
        var sourceName = LanguageDisplayName(sourceLanguage);
        var targetName = LanguageDisplayName(targetLanguage);

        return $"你是专业的同声传译员，负责将直播间说话人的{sourceName}原文实时翻译成{targetName}。" +
               "只输出翻译结果本身，不要加任何解释、备注、引号或者重复原文。" +
               "保持口语化、简洁，符合直播场景的语气。";
    }

    /// <summary>语言代码转自然语言名称，只覆盖当前项目支持的 zh/en 这一对；
    /// 传入其他代码时原样返回（留了扩展空间，不会直接报错），
    /// 以后要支持更多语言对时在这里加 case 就行。</summary>
    private static string LanguageDisplayName(string languageCode) => languageCode switch
    {
        "zh" => "中文",
        "en" => "英文",
        _ => languageCode,
    };

    /// <summary>统一处理非 2xx 响应：优先按 OpenAI 兼容协议的错误体
    /// {"error":{"message":...}} 解析出可读的错误信息；如果响应体不是
    /// 这个格式（比如网关本身报错，返回的是 HTML 或纯文本），就退化成
    /// 直接把原始响应体内容抛出来，保证任何情况下报错信息都不会丢。</summary>
    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;

        string errorMessage;
        try
        {
            var error = await response.Content.ReadFromJsonAsync<ChatCompletionErrorResponse>(JsonOptions);
            errorMessage = error?.Error?.Message ?? "(未知错误，响应体解析失败)";
        }
        catch (JsonException)
        {
            errorMessage = await response.Content.ReadAsStringAsync();
        }

        throw new InvalidOperationException($"翻译 API 请求失败: [{(int)response.StatusCode}] {errorMessage}");
    }
}
