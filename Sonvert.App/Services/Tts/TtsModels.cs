namespace Sonvert.App.Services.Tts;

/// <summary>合成结果——GPT-SoVITS /tts 接口直接返回音频字节流，不是 JSON，
/// 所以这里存的是原始音频数据，不是像翻译那样的一个字符串。</summary>
public class TtsResult
{
    public required byte[] AudioData { get; init; }

    /// <summary>音频格式，对应请求时的 media_type（wav/ogg/aac），
    /// 播放那边需要知道这个才能正确解码。</summary>
    public required string MediaType { get; init; }

    /// <summary>从发起合成请求到收到第一个音频数据帧的耗时（毫秒）。
    /// 只有走真正流式协议的引擎（目前是 AliyunTtsService）才会填这个值，
    /// GPT-SoVITS/IndexTTS/Qwen3-TTS 这些"一次性吐出完整音频"的引擎
    /// 保持 null——LiveTranslationViewModel 统计"合成耗时"时，这个值
    /// 存在就优先用它，因为对流式引擎来说"什么时候能开始播放"比"整句话
    /// 全部合成完要多久"更能反映真实的响应速度。</summary>
    public int? TimeToFirstByteMs { get; init; }
}