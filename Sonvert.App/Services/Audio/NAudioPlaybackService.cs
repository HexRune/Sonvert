using System;
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