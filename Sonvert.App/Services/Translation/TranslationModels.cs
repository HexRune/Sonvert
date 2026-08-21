using System.Text.Json.Serialization;

namespace Sonvert.App.Services.Translation;

/// <summary>翻译结果，ITranslationService.TranslateAsync 的返回值。</summary>
public class TranslationResult
{
    public required string TranslatedText { get; init; }
}

/// <summary>对应 Python 服务 POST /translate 的请求体。</summary>
public class TranslateRequest
{
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("source_lang")]
    public string SourceLang { get; set; } = string.Empty;

    [JsonPropertyName("target_lang")]
    public string TargetLang { get; set; } = string.Empty;
}

/// <summary>对应 Python 服务 POST /translate 的成功响应体。</summary>
public class TranslateResponse
{
    [JsonPropertyName("translated_text")]
    public string TranslatedText { get; set; } = string.Empty;
}