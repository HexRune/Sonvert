namespace Sonvert.App.Services.Tts;

/// <summary>合成结果——GPT-SoVITS /tts 接口直接返回音频字节流，不是 JSON，
/// 所以这里存的是原始音频数据，不是像翻译那样的一个字符串。</summary>
public class TtsResult
{
    public required byte[] AudioData { get; init; }

    /// <summary>音频格式，对应请求时的 media_type（wav/ogg/aac），
    /// 播放那边需要知道这个才能正确解码。</summary>
    public required string MediaType { get; init; }
}