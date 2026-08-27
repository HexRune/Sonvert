using System.Collections.Generic;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Sonvert.App.Services.Audio;

public static class AudioInputDeviceEnumerator
{
    public static List<AudioInputDeviceOption> GetAllOptions()
    {
        var options = new List<AudioInputDeviceOption>
        {
            // 排在最前面，Id="-1" 对应 WaveInEvent 的 DeviceNumber=-1
            // 这个特殊值（Windows 多媒体 API 里叫 WAVE_MAPPER），效果是
            // "由系统决定当前用哪个录音设备"，不是固定绑死某一个设备。
            new AudioInputDeviceOption
            {
                Kind = AudioInputDeviceKind.Microphone,
                Id = "-1",
                DisplayName = "系统默认麦克风",
            },
        };

        // 真实麦克风类设备——用 NAudio 传统的 WaveInEvent 枚举方式。
        for (var i = 0; i < WaveInEvent.DeviceCount; i++)
        {
            var caps = WaveInEvent.GetCapabilities(i);
            options.Add(new AudioInputDeviceOption
            {
                Kind = AudioInputDeviceKind.Microphone,
                Id = i.ToString(),
                DisplayName = $"🎤 {caps.ProductName}",
            });
        }

        // 输出设备（用来做回环采集）——用 Core Audio API 枚举当前处于
        // "启用"状态的渲染设备（也就是正常能拿来放声音的那些）。
        using var deviceEnumerator = new MMDeviceEnumerator();
        var renderDevices = deviceEnumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
        foreach (var device in renderDevices)
        {
            options.Add(new AudioInputDeviceOption
            {
                Kind = AudioInputDeviceKind.Loopback,
                Id = device.ID,
                DisplayName = $"🔊 {device.FriendlyName}（回环）",
            });
        }

        return options;
    }
}