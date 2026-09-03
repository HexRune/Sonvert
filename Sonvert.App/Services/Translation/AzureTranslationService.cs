using Sonvert.App.Settings;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Sonvert.App.Services.Translation;

/// <summary>
/// ITranslationService 的 Azure Translator 实现。这跟同目录下的
/// ApiTranslationService（DeepSeek/豆包那套 OpenAI 兼容协议）是完全独立
/// 的两套协议，所以单独开一个类，不往 ApiTranslationService 里加 if 分支——
/// Azure Translator 是专用机器翻译引擎，不是"跟大模型对话让它帮忙翻译"，
/// 请求/响应结构、鉴权方式都不一样：
///
///   POST {Endpoint}/translate?api-version=3.0&amp;from={src}&amp;to={dst}
///   Headers: Ocp-Apim-Subscription-Key: {key}
///            Ocp-Apim-Subscription-Region: {region}   (可选，见下)
///   Body:    [{"Text": "原文"}]
///   Resp:    [{"translations":[{"text":"译文","to":"en"}]}]
///
/// 配置沿用现有的 TranslationApiEndpoint/TranslationApiKey 两个设置字段
/// （首页选中"Azure 翻译"预设时会自动把 Endpoint 填成 Azure 的固定网关
/// 地址 https://api.cognitive.microsofttranslator.com），额外多一个
/// Azure 专属的 TranslationApiRegion。TranslationApiModel 对 Azure
/// 没有意义（它没有"模型"这个概念），这个类完全不读这个字段。
/// </summary>
public class AzureTranslationService : ITranslationService
{
    private readonly ISettingsService _settingsService;
    private readonly IGlossaryRepository _glossaryRepository;
    private readonly HttpClient _httpClient;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public AzureTranslationService(ISettingsService settingsService, IGlossaryRepository glossaryRepository)
    {
        _settingsService = settingsService;
        _glossaryRepository = glossaryRepository;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
    }

    public Task StartAsync() => Task.CompletedTask; // 调远程 API，不需要拉子进程

    public Task LoadModelAsync() => Task.CompletedTask; // 远程 API 不需要本地预加载

    public Task StopAsync() => Task.CompletedTask;

    public async Task<TranslationResult> TranslateAsync(
        string text, string sourceLanguage, string targetLanguage)
    {
        var settings = _settingsService.Current;

        if (string.IsNullOrWhiteSpace(settings.TranslationApiEndpoint) ||
            string.IsNullOrWhiteSpace(settings.TranslationApiKey))
        {
            throw new InvalidOperationException(
                "Azure 翻译未配置完整。请在首页填写翻译服务商的 API 地址和 API Key。");
        }

        // 术语表替换逻辑跟 ApiTranslationService 保持一致，保证不管走
        // 哪个 Provider，术语表这个功能的行为都一样。
        var textToTranslate = text;
        if (settings.GlossaryEnabled)
        {
            var glossary = await _glossaryRepository.GetAllAsync();
            textToTranslate = GlossaryReplacer.Replace(text, glossary);
        }

        var azureSource = ToAzureLanguageCode(sourceLanguage);
        var azureTarget = ToAzureLanguageCode(targetLanguage);

        var url = $"{settings.TranslationApiEndpoint.TrimEnd('/')}/translate" +
                  $"?api-version=3.0&from={azureSource}&to={azureTarget}";

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
        {
            // Azure 的请求体是一个数组——单次请求其实支持传多段文本一起翻译
            // （数组里放多个 {"Text":...}），但这里只有一句要翻译，传单元素
            // 数组就够了，不需要为了"以后可能批量翻译"这种假设去改调用方。
            Content = JsonContent.Create(new[] { new AzureTranslateRequestItem { Text = textToTranslate } }),
        };
        httpRequest.Headers.Add("Ocp-Apim-Subscription-Key", settings.TranslationApiKey);

        // 区域是选填的——官方文档说单服务全局资源不需要这个请求头，
        // 多服务/区域性资源才需要；不知道用户创建的是哪种资源，所以
        // 干脆"填了就加，没填就不加"，不做强校验。真要漏填了，Azure
        // 会返回 401，错误信息里能看出来。
        if (!string.IsNullOrWhiteSpace(settings.TranslationApiRegion))
        {
            httpRequest.Headers.Add("Ocp-Apim-Subscription-Region", settings.TranslationApiRegion);
        }

        var stopwatch = Stopwatch.StartNew();
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(httpRequest);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new InvalidOperationException($"调用 Azure 翻译失败（网络层）: {ex.Message}", ex);
        }

        await EnsureSuccessAsync(response);

        var result = await response.Content.ReadFromJsonAsync<List<AzureTranslateResponseItem>>(JsonOptions);
        stopwatch.Stop();

        var translatedText = result?.Count > 0 && result[0].Translations.Count > 0
            ? result[0].Translations[0].Text
            : null;

        if (string.IsNullOrEmpty(translatedText))
        {
            throw new InvalidOperationException("Azure 翻译返回结果为空（响应结构不符合预期或反序列化失败）");
        }

        Debug.WriteLine($"[AzureTranslation] elapsed={stopwatch.ElapsedMilliseconds}ms");

        return new TranslationResult
        {
            TranslatedText = translatedText,
            ElapsedMilliseconds = stopwatch.ElapsedMilliseconds,
        };
    }

    /// <summary>把项目内部的语言简写（"zh"/"en"）转成 Azure Translator
    /// 期望的语言代码。Azure 对中文要求区分简体/繁体（"zh-Hans"/
    /// "zh-Hant"），不能直接传"zh"——项目目前只支持简体中文这一个
    /// 中文变体，所以固定映射到 "zh-Hans"；英文双方代码一致，直接
    /// 透传。以后如果要支持更多语言，在这个 switch 里加分支即可。</summary>
    private static string ToAzureLanguageCode(string internalCode) => internalCode switch
    {
        "zh" => "zh-Hans",
        "en" => "en",
        _ => internalCode,
    };

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;

        string errorMessage;
        try
        {
            // Azure 的错误响应格式跟 OpenAI 兼容协议不一样：
            // {"error": {"code": 400xxx, "message": "..."}}
            var error = await response.Content.ReadFromJsonAsync<AzureErrorResponse>(JsonOptions);
            errorMessage = error?.Error?.Message ?? "(未知错误，响应体解析失败)";
        }
        catch (JsonException)
        {
            errorMessage = await response.Content.ReadAsStringAsync();
        }

        throw new InvalidOperationException($"Azure 翻译请求失败: [{(int)response.StatusCode}] {errorMessage}");
    }
}

// ---------------------------------------------------------------------
// Azure Translator REST API v3.0 的最小 DTO 集合，只覆盖 /translate
// 这一个接口用得到的字段。
// ---------------------------------------------------------------------

file class AzureTranslateRequestItem
{
    [JsonPropertyName("Text")]
    public string Text { get; set; } = string.Empty;
}

file class AzureTranslateResponseItem
{
    [JsonPropertyName("translations")]
    public List<AzureTranslation> Translations { get; set; } = new();
}

file class AzureTranslation
{
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("to")]
    public string To { get; set; } = string.Empty;
}

file class AzureErrorResponse
{
    [JsonPropertyName("error")]
    public AzureErrorDetail? Error { get; set; }
}

file class AzureErrorDetail
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}
