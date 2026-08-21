using System.Threading.Tasks;

namespace Sonvert.App.Services.Audio;

/// <summary>
/// 播放合成出来的语音。先做最简单的版本——直接从内存播放，
/// 不做设备选择、不做虚拟麦克风路由，这些留到后面单独设计输出设备
/// 那部分时再加。
/// </summary>
public interface IAudioPlaybackService
{
    Task PlayAsync(byte[] audioData);

    /// <summary>立刻中断当前正在播放的音频（如果有的话）。用户点"停止"时调用，
    /// 让 PlayAsync 里等待的那个 TaskCompletionSource 尽快完成，不用干等播完。</summary>
    Task StopAsync();
}

