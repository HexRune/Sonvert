using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Sonvert.App.Services.Audio;

/// <summary>
/// 播放队列：后台一个消费循环，严格按 Enqueue 的顺序、串行播放。
/// 队列里的每一项都是"占位"，消费到某一项时会 await 它的 AudioTask——
/// 如果这句话还没合成完，这里会自然等待，不会提前跳到下一句去播，
/// 这正是"保证顺序 + 不同时播放"这两个要求的实现方式。
/// </summary>
public class PlaybackQueueService : IPlaybackQueueService, IAsyncDisposable
{
    private readonly IAudioPlaybackService _playbackService;
    private readonly Channel<PlaybackSlot> _channel = Channel.CreateUnbounded<PlaybackSlot>();
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _consumerLoopTask;

    public PlaybackQueueService(IAudioPlaybackService playbackService)
    {
        _playbackService = playbackService;
        _consumerLoopTask = Task.Run(ConsumeLoopAsync);
    }

    public PlaybackSlot Enqueue()
    {
        var slot = new PlaybackSlot();
        _channel.Writer.TryWrite(slot);
        return slot;
    }

    private async Task ConsumeLoopAsync()
    {
        try
        {
            await foreach (var slot in _channel.Reader.ReadAllAsync(_cts.Token))
            {
                try
                {
                    // 等这一句真正准备好（或者被标记为"跳过，没有音频"）——
                    // 这一步天然保证了"上一句播完/跳过之前，绝不会开始下一句"。
                    var audioData = await slot.AudioTask;
                    if (audioData is not null)
                    {
                        await _playbackService.PlayAsync(audioData);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[PlaybackQueue] 播放某一句时出错，跳过继续下一句: {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Dispose 时正常取消，不用当错误处理。
        }
    }

    public async ValueTask DisposeAsync()
    {
        _channel.Writer.Complete();
        _cts.Cancel();
        try
        {
            await _consumerLoopTask;
        }
        catch (OperationCanceledException)
        {
            // 正常取消流程。
        }
    }
}