using System.Collections.Generic;
using NAudio.CoreAudioApi;

namespace Sonvert.App.Services.Audio;

public static class AudioOutputDeviceEnumerator
{
    public static List<AudioOutputDeviceOption> GetAllOptions()
    {
        var options = new List<AudioOutputDeviceOption>
        {
            // 空字符串对应"系统默认输出设备"——跟 WASAPI 的
            // GetDefaultAudioEndpoint 配合使用，效果是跟随系统当前设置
            // 动态变化，不是固定绑死某个设备。
            new AudioOutputDeviceOption { Id = string.Empty, DisplayName = "系统默认输出设备" },
        };

        using var deviceEnumerator = new MMDeviceEnumerator();
        var renderDevices = deviceEnumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
        foreach (var device in renderDevices)
        {
            options.Add(new AudioOutputDeviceOption
            {
                Id = device.ID,
                DisplayName = $"🔊 {device.FriendlyName}",
            });
        }

        return options;
    }
}