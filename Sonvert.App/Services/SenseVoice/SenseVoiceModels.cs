using System.Text.Json.Serialization;

namespace Sonvert.App.Services.SenseVoice;

/// <summary>
/// 对应 Python 服务 POST /recognize 的响应体。
/// 字段名用 JsonPropertyName 对齐 Python 那边返回的 snake_case/原始字段名，
/// 这样反序列化时不用额外配置命名策略转换。
/// </summary>
public class RecognitionResult
{
    /// <summary>识别出的文本内容。</summary>
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// 识别到的语言，取值见 Python 端 LANGUAGE_IDS：zh/en/yue/ja/ko/nospeech。
    /// 可能为 null（模型没能判断出语言时）。
    /// </summary>
    [JsonPropertyName("language")]
    public string? Language { get; set; }

    /// <summary>
    /// 情绪标签，例如 NEUTRAL/HAPPY/ANGRY/SAD/EMO_UNKNOWN 等。
    /// 可能为 null —— 如果 Python 端日志报过 "Unrecognized tag"，
    /// 说明遇到了没收录进 KNOWN_EMOTIONS 的新标签，这里会是 null，
    /// 需要回头去 Python 端补充那个集合。
    /// </summary>
    [JsonPropertyName("emotion")]
    public string? Emotion { get; set; }

    /// <summary>
    /// 事件标签，例如 Speech/BGM/Applause/Laughter 等。
    /// 已知发现：int8 精度下这个字段的准确性会明显下降（同一段音频，
    /// fp32 判断为 Speech，int8 会误判成 BGM），文本和情绪不受影响。
    /// </summary>
    [JsonPropertyName("event")]
    public string? Event { get; set; }
}

/// <summary>
/// 支持的模型精度。用枚举而不是裸字符串，避免调用方手滑传错值
/// （比如打成 "Int8" 或 "FP32"）——序列化时再转换成 Python 端要求的小写字符串。
/// </summary>
public enum ModelPrecision
{
    Int8,
    Fp32,
}

/// <summary>
/// 对应 Python 服务 POST /model/load 的请求体：{"precision": "int8" | "fp32"}
/// </summary>
public class LoadModelRequest
{
    [JsonPropertyName("precision")]
    public string Precision { get; set; } = "fp32";

    public static LoadModelRequest From(ModelPrecision precision) => new()
    {
        // Python 端只认小写的 "int8"/"fp32"，这里做一次转换，
        // 调用方不用关心大小写细节。
        Precision = precision == ModelPrecision.Int8 ? "int8" : "fp32",
    };
}

/// <summary>
/// 对应 Python 服务 POST /model/load 的成功响应：{"success": true, "load_time_ms": ...}
/// </summary>
public class LoadModelResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("load_time_ms")]
    public double LoadTimeMs { get; set; }
}

/// <summary>
/// 对应 Python 服务任意接口出错时的响应体：{"error": "..."}
/// main.py 里约定过，错误都是"对应 HTTP 状态码 + 这个结构"，不是统一 200。
/// </summary>
public class ErrorResponse
{
    [JsonPropertyName("error")]
    public string Error { get; set; } = string.Empty;
}

/// <summary>
/// 对应 Python 服务 GET /health 的响应：{"status": "ok", "model_loaded": bool}
/// </summary>
public class HealthResponse
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("model_loaded")]
    public bool ModelLoaded { get; set; }
}