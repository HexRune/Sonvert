namespace Sonvert.App.Services.Audio;

public enum AudioInputDeviceKind
{
    Microphone,
}

/// <summary>
/// 下拉框里的一个设备选项。Id 是 NAudio 的 WaveIn 设备索引（整数字符串）。
/// 曾经还支持 Loopback（抓某个输出设备上的所有声音）这种类型，已经
/// 删掉——回环采集会把本软件自己合成播放出来的语音也录进去，造成
/// "自己说的话被自己识别一遍"这种回声式误触发。需要"翻译游戏声音"这
/// 类场景，改用 MixLine 这类工具把游戏输出路由到一个虚拟麦克风，再在
/// 这里把那个虚拟麦克风当成普通 Microphone 类型选中即可，不需要回环。
/// </summary>
public class AudioInputDeviceOption
{
    public required AudioInputDeviceKind Kind { get; init; }
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
}