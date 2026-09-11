using System.Collections.Generic;
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
        // MixLine/VB-Cable 这类工具创建的虚拟麦克风设备，在 Windows 看来
        // 就是普通的录音设备，会跟真实麦克风一起出现在这个列表里，不需要
        // 额外识别或者特殊处理。
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

        return options;
    }
}