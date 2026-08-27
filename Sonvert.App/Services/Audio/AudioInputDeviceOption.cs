namespace Sonvert.App.Services.Audio;

public enum AudioInputDeviceKind
{
    Microphone,
    Loopback,
}

/// <summary>
/// 下拉框里的一个设备选项。Id 的含义随 Kind 变化：Microphone 时是
/// NAudio 的 WaveIn 设备索引（整数字符串）；Loopback 时是
/// Windows Core Audio 的设备 Id（一长串 GUID 格式字符串）——两种
/// 设备体系的 Id 类型本来就不一样，统一存成字符串，具体怎么解析
/// 由 RecognitionSessionService 创建对应输入源时处理。
/// </summary>
public class AudioInputDeviceOption
{
    public required AudioInputDeviceKind Kind { get; init; }
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
}