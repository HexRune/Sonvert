using System.Collections.Generic;
using System.Threading.Tasks;

namespace Sonvert.App.Services.Audio;

/// <summary>队列里的一个占位——识别到一句话时立刻创建，此时还没有音频数据；
/// 翻译+合成做完之后调用 Complete 把结果填进去。如果这句话最终没有音频
/// （翻译失败、合成失败等），传 null，队列会跳过这句继续播下一句，
/// 不会因为一句失败就卡住后面所有排队的句子。
///
/// AudioTask/Complete 是原来就有的"整段字节，一次性完成"路径，GPT-SoVITS/
/// IndexTTS/Qwen3-TTS 这些非流式引擎继续用这条路，一个字没动。
/// StreamingTask/CompleteStreaming 是新加的"陆续吐 chunk"路径，只有
/// AliyunTtsService 这类真正支持流式的引擎会用到。一个 slot 只会用到
/// 两者之一——消费者用 Task.WhenAny 等哪个先完成，就按哪条路径处理，
/// 不会同时触发。</summary>
public class PlaybackSlot
{
    private readonly TaskCompletionSource<byte[]?> _tcs = new();
    public Task<byte[]?> AudioTask => _tcs.Task;
    public void Complete(byte[]? audioData) => _tcs.TrySetResult(audioData);

    private readonly TaskCompletionSource<StreamingPlaybackPayload?> _streamingTcs = new();
    public Task<StreamingPlaybackPayload?> StreamingTask => _streamingTcs.Task;

    public void CompleteStreaming(IAsyncEnumerable<byte[]> chunks, int sampleRate, int channels, int bitsPerSample) =>
        _streamingTcs.TrySetResult(new StreamingPlaybackPayload(chunks, sampleRate, channels, bitsPerSample));
}

/// <summary>流式播放需要的音频块序列 + 格式信息——NAudio 的
/// BufferedWaveProvider 要求提前知道采样率/声道数/位深，不能边收数据
/// 边猜，所以格式信息跟 chunk 序列一起传递。</summary>
public record StreamingPlaybackPayload(
    IAsyncEnumerable<byte[]> Chunks, int SampleRate, int Channels, int BitsPerSample);

public interface IPlaybackQueueService
{
    /// <summary>在识别到一句话的那一刻立刻调用——这一步决定了播放顺序，
    /// 不要等翻译/合成做完再调用，否则顺序就跟着"谁先合成完"走了，
    /// 不是跟着"谁先说的"走。</summary>
    PlaybackSlot Enqueue();
}