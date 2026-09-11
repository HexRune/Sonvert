using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Sonvert.App.Settings;

namespace Sonvert.App.Services.Audio;

public class NAudioPlaybackService : IAudioPlaybackService
{
    private readonly ISettingsService _settingsService;
    private WasapiOut? _currentOutputDevice;

    public NAudioPlaybackService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public Task PlayAsync(byte[] audioData)
    {
        var tcs = new TaskCompletionSource();

        var device = ResolveOutputDevice();

        // shareMode: Shared——跟系统里其他程序共享这个输出设备，不会
        // 独占它导致别的程序（比如你正在放的游戏/音乐）被打断，这是
        // 播放场景下的正常预期行为。
        var outputDevice = new WasapiOut(device, AudioClientShareMode.Shared, useEventSync: true, latency: 200);
        var stream = new MemoryStream(audioData);
        var reader = new WaveFileReader(stream);

        outputDevice.Init(reader);

        outputDevice.PlaybackStopped += (_, args) =>
        {
            outputDevice.Dispose();
            reader.Dispose();
            stream.Dispose();
            device.Dispose();

            if (ReferenceEquals(_currentOutputDevice, outputDevice))
            {
                _currentOutputDevice = null;
            }

            if (args.Exception != null) tcs.SetException(args.Exception);
            else tcs.SetResult();
        };

        _currentOutputDevice = outputDevice;
        outputDevice.Play();

        return tcs.Task;
    }

    public async Task PlayStreamingAsync(
        IAsyncEnumerable<byte[]> audioChunks, int sampleRate, int channels, int bitsPerSample)
    {
        var tcs = new TaskCompletionSource();
        var device = ResolveOutputDevice();

        var bufferedProvider = new BufferedWaveProvider(new WaveFormat(sampleRate, bitsPerSample, channels))
        {
            // 30 秒对一句实时翻译的语音来说是很宽裕的上限——真正的目的
            // 是给个足够大的缓冲区，不是指望真的会用满；DiscardOnBufferOverflow
            // 保持默认 false，宁可缓冲区满了抛异常让问题暴露出来，也不要
            // 悄悄丢音频数据导致播放出来的内容缺字。
            BufferDuration = TimeSpan.FromSeconds(30),
        };

        var outputDevice = new WasapiOut(device, AudioClientShareMode.Shared, useEventSync: true, latency: 200);
        outputDevice.Init(bufferedProvider);

        outputDevice.PlaybackStopped += (_, args) =>
        {
            outputDevice.Dispose();
            device.Dispose();

            if (ReferenceEquals(_currentOutputDevice, outputDevice))
            {
                _currentOutputDevice = null;
            }

            if (args.Exception != null) tcs.TrySetException(args.Exception);
            else tcs.TrySetResult();
        };

        _currentOutputDevice = outputDevice;

        var started = false;
        await foreach (var chunk in audioChunks)
        {
            bufferedProvider.AddSamples(chunk, 0, chunk.Length);

            // 第一块数据一到就立刻开始播放，不等后面的数据陆续到齐——
            // 这正是流式播放的核心：WasapiOut 播放的是这个
            // BufferedWaveProvider，缓冲区暂时没数据时它会自然输出静音
            // 等待，不会因为"当前没数据"就提前判定播放结束。
            if (!started)
            {
                outputDevice.Play();
                started = true;
            }
        }

        // 数据全部喂完了，但缓冲区里可能还有没放完的尾巴——不能立刻
        // Stop()，要等缓冲区真正播空再停，否则会把最后一截音频硬切掉。
        while (bufferedProvider.BufferedBytes > 0)
        {
            await Task.Delay(50);
        }
        outputDevice.Stop();

        await tcs.Task;
    }

    public Task StopAsync()
    {
        _currentOutputDevice?.Stop();
        return Task.CompletedTask;
    }

    private MMDevice ResolveOutputDevice()
    {
        var enumerator = new MMDeviceEnumerator();
        var deviceId = _settingsService.Current.OutputDeviceId;

        if (string.IsNullOrEmpty(deviceId))
        {
            return enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        }

        try
        {
            return enumerator.GetDevice(deviceId);
        }
        catch (Exception)
        {
            // 之前选的设备可能已经被拔掉/禁用了，找不到就兜底退回默认设备，
            // 不要让播放直接失败。
            return enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        }
    }
}