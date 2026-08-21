using System;
using System.IO;
using System.Threading.Tasks;
using NAudio.Wave;

namespace Sonvert.App.Services.Audio;

public class NAudioPlaybackService : IAudioPlaybackService
{
    // 这套设计假设同一时间只有一句在播放（LiveTranslationViewModel 是
    // 顺序调用 PlayAsync 的），所以只用一个字段记录"当前正在播放的设备"
    // 就够了，不需要维护一个列表。
    private WaveOutEvent? _currentOutputDevice;

    public Task PlayAsync(byte[] audioData)
    {
        var tcs = new TaskCompletionSource();

        var outputDevice = new WaveOutEvent();
        var stream = new MemoryStream(audioData);
        var reader = new WaveFileReader(stream);

        outputDevice.Init(reader);

        outputDevice.PlaybackStopped += (_, args) =>
        {
            outputDevice.Dispose();
            reader.Dispose();
            stream.Dispose();

            // 只有当它还是"当前"这一个的时候才清空字段——避免极端情况下
            // 上一句的 PlaybackStopped 回调，把已经开始播放的下一句的
            // 引用给清掉了。
            if (ReferenceEquals(_currentOutputDevice, outputDevice))
            {
                _currentOutputDevice = null;
            }

            // Stop() 主动调用触发的 PlaybackStopped，args.Exception 是 null，
            // 跟正常播完的情况区分不开，但对这里的用途来说不需要区分——
            // 不管是播完了还是被打断了，调用方只关心"这次 PlayAsync 结束了"。
            if (args.Exception != null)
            {
                tcs.SetException(args.Exception);
            }
            else
            {
                tcs.SetResult();
            }
        };

        _currentOutputDevice = outputDevice;
        outputDevice.Play();

        return tcs.Task;
    }

    public Task StopAsync()
    {
        // Stop() 是同步的，调用后会触发上面注册的 PlaybackStopped 回调，
        // 对应那次 PlayAsync 的 Task 会随之完成——不需要在这里自己再
        // 完成一次 TaskCompletionSource，回调里已经处理了。
        _currentOutputDevice?.Stop();
        return Task.CompletedTask;
    }
}