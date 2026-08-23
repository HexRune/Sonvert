using System.Threading.Tasks;

namespace Sonvert.App.Services.Audio;

/// <summary>队列里的一个占位——识别到一句话时立刻创建，此时还没有音频数据；
/// 翻译+合成做完之后调用 Complete 把结果填进去。如果这句话最终没有音频
/// （翻译失败、合成失败等），传 null，队列会跳过这句继续播下一句，
/// 不会因为一句失败就卡住后面所有排队的句子。</summary>
public class PlaybackSlot
{
    private readonly TaskCompletionSource<byte[]?> _tcs = new();
    public Task<byte[]?> AudioTask => _tcs.Task;
    public void Complete(byte[]? audioData) => _tcs.TrySetResult(audioData);
}

public interface IPlaybackQueueService
{
    /// <summary>在识别到一句话的那一刻立刻调用——这一步决定了播放顺序，
    /// 不要等翻译/合成做完再调用，否则顺序就跟着"谁先合成完"走了，
    /// 不是跟着"谁先说的"走。</summary>
    PlaybackSlot Enqueue();
}