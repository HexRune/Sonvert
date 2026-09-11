using System.Collections.Generic;
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

    /// <summary>流式播放：陆续接收裸 PCM 音频块并边收边播，不用等
    /// audioChunks 全部枚举完才开始出声音。sampleRate/channels/
    /// bitsPerSample 描述的是 audioChunks 里每个块的数据格式——调用方
    /// 必须确保这三个参数跟实际数据一致，这里不做格式探测。</summary>
    Task PlayStreamingAsync(IAsyncEnumerable<byte[]> audioChunks, int sampleRate, int channels, int bitsPerSample);

    /// <summary>立刻中断当前正在播放的音频（如果有的话）。用户点"停止"时调用，
    /// 让 PlayAsync 里等待的那个 TaskCompletionSource 尽快完成，不用干等播完。</summary>
    Task StopAsync();
}

