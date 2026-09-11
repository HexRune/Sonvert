using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Sonvert.App.Services.Tts;

public interface ITtsService
{
    Task StartAsync();

    /// <summary>预热：把指定角色已经录制过的所有情绪参考音频，提前提交给
    /// GPT-SoVITS 做一次特征提取（调用它的 /set_refer_audio 接口）。
    /// 这一步能不能真正加速后续合成还没有 100% 确认（GPT-SoVITS 官方
    /// 没有明确文档说明 /tts 请求会不会复用这次预热的结果），实现这个方法
    /// 是为了实测验证——调用方应该在开始识别前调一次，然后对比第一次真实
    /// 合成的耗时跟之前的数据，看是否有实质性改善。</summary>
    Task PrewarmReferenceAudioAsync(int characterId);

    Task<TtsResult> SynthesizeAsync(
        string text, string language, string emotion,
        float[]? liveEmotionAudio = null, int liveEmotionSampleRate = 0);

    /// <summary>这个引擎是不是真正支持流式合成——只有走真正流式协议、
    /// 能在整句话生成完之前就陆续吐出音频数据的引擎（目前只有
    /// AliyunTtsService）返回 true。默认 false：GPT-SoVITS/IndexTTS/
    /// Qwen3-TTS 这些"一次性生成完整音频再返回"的引擎不需要重写这个，
    /// 保持默认值就是在告诉调用方"老老实实走 SynthesizeAsync"。</summary>
    bool SupportsStreaming => false;

    /// <summary>流式合成的音频格式——调用方需要知道这个才能正确初始化
    /// 播放缓冲区（NAudio 的 BufferedWaveProvider 要求提前知道采样率/
    /// 声道数/位深，不能边收数据边猜）。只有 SupportsStreaming 为 true
    /// 时才应该访问这个属性；默认实现直接抛异常，不提供一个看似合理但
    /// 实际没有意义的占位值。</summary>
    (int SampleRate, int Channels, int BitsPerSample) StreamingAudioFormat =>
        throw new NotSupportedException("这个 TTS 引擎不支持流式合成，不应该查询 StreamingAudioFormat");

    /// <summary>流式合成：陆续吐出裸 PCM 音频数据块（已经剥离了 WAV/MP3
    /// 之类的容器格式头，纯采样数据），而不是等整句话生成完再一次性
    /// 返回。只有 SupportsStreaming 为 true 的实现才需要真正重写这个
    /// 方法；默认实现直接抛 NotSupportedException——调用方应该先检查
    /// SupportsStreaming 再决定调用 SynthesizeAsync 还是这个方法，不应该
    /// 指望这个默认实现被真正触发到。</summary>
    IAsyncEnumerable<byte[]> SynthesizeStreamingAsync(
        string text, string language, string emotion,
        float[]? liveEmotionAudio = null, int liveEmotionSampleRate = 0) =>
        throw new NotSupportedException("这个 TTS 引擎不支持流式合成");

    Task StopAsync();
}