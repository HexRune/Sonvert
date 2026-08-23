using System.Threading.Tasks;

namespace Sonvert.App.Services.Audio;

/// <summary>录制麦克风音频，用于声音克隆的参考音频录制。跟 IAudioPlaybackService
/// 是一对，一个管录、一个管放，职责分开，互不依赖。</summary>
public interface IAudioRecordingService
{
    void StartRecording();

    /// <summary>停止录制，返回完整的 wav 格式字节流。</summary>
    Task<byte[]> StopRecordingAsync();

    bool IsRecording { get; }
}