using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Sonvert.App.Services.Translation;

/// <summary>翻译结果，ITranslationService.TranslateAsync 的返回值。</summary>
public class TranslationResult
{
    public required string TranslatedText { get; init; }

    /// <summary>本次翻译调用耗时（毫秒）。本地实现暂不填充（为 null）；
    /// API 实现会填充，方便测试阶段直接在 UI/日志里观察不同服务商的延迟，
    /// 不需要额外接秒表工具。</summary>
    public long? ElapsedMilliseconds { get; init; }
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

// ---------------------------------------------------------------------
// 以下是 OpenAI Chat Completions 协议的最小 DTO 集合。
// DeepSeek / 豆包(火山方舟) / OpenAI 官方 / 大多数第三方大模型网关
// 都兼容这一套 {model, messages, temperature} 请求体 + Bearer Token 鉴权，
// 所以 ApiTranslationService 只需要针对这一套协议写一次调用逻辑，
// 换服务商只需要在设置里改 Endpoint/Key/Model 三个字段，代码不用动。
// 如果以后要接入协议不兼容的服务（比如 Anthropic 原生 API 用的是
// /messages + x-api-key，字段结构也不同），需要单独再写一个实现类，
// 但暂时不需要。
// ---------------------------------------------------------------------

/// <summary>OpenAI 兼容协议的请求体（POST {endpoint}/chat/completions）。
/// 字段名和 JSON key 都严格照抄 OpenAI 官方协议，这样只要服务商说自己
/// "兼容 OpenAI 接口"，这个类基本不用改就能直接用。</summary>
public class ChatCompletionRequest
{
    /// <summary>具体模型 ID，来自 AppSettings.TranslationApiModel，
    /// 比如 "deepseek-chat"、"gpt-4o-mini"、火山方舟那边申请到的模型 ID。</summary>
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    /// <summary>对话消息列表。本项目场景固定是两条：一条 system（角色设定+
    /// 输出格式约束）+ 一条 user（待翻译原文），不维护多轮历史。</summary>
    [JsonPropertyName("messages")]
    public List<ChatMessage> Messages { get; set; } = new();

    /// <summary>翻译任务需要稳定输出，用较低温度；先写死，
    /// 后续如果需要可以挪到设置里做成可调参数。</summary>
    [JsonPropertyName("temperature")]
    public double Temperature { get; set; } = 0.3;
}

/// <summary>单条对话消息，Role 取值遵循 OpenAI 约定："system"/"user"/"assistant"。</summary>
public class ChatMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;
}

/// <summary>OpenAI 兼容协议的成功响应体（只取用得到的字段，其余原样忽略）。
/// 真实响应里还会有 id/created/usage(token计费) 等字段，这里没建对应属性，
/// 因为 JsonSerializer 默认会忽略 JSON 里存在但 C# 类里没有的字段，
/// 不会报错，所以不需要为用不上的字段也建模。</summary>
public class ChatCompletionResponse
{
    /// <summary>大模型可能会在一次请求里返回多个候选回复（取决于请求时的
    /// n 参数），这里没传 n，默认只有一个，所以只取 Choices[0]。</summary>
    [JsonPropertyName("choices")]
    public List<ChatCompletionChoice> Choices { get; set; } = new();
}

/// <summary>单个候选回复，实际要用的译文就在 Message.Content 里。</summary>
public class ChatCompletionChoice
{
    [JsonPropertyName("message")]
    public ChatMessage Message { get; set; } = new();
}

/// <summary>OpenAI 兼容协议的错误响应体，各家格式基本一致：
/// {"error": {"message": "...", ...}}。error 对象里通常还有 type/code
/// 字段，这里只取 message 用于展示给用户看，够用了。</summary>
public class ChatCompletionErrorResponse
{
    [JsonPropertyName("error")]
    public ChatCompletionErrorDetail? Error { get; set; }
}

public class ChatCompletionErrorDetail
{
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}