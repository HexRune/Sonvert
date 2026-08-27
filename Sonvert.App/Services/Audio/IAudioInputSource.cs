using System;

namespace Sonvert.App.Services.Audio;

/// <summary>
/// 统一抽象"一路 16kHz/16bit/单声道的音频数据流"，不管它底层实际来自
/// 真实麦克风（NAudio.WaveInEvent）还是某个输出设备的回环采集
/// （NAudio.WasapiLoopbackCapture）——两者在 NAudio 里是完全不同的类，
/// 采样格式默认值也不一样（回环采集通常是系统当前的输出格式，比如
/// 44.1kHz/32bit float 立体声），这层抽象把这些差异都封装在具体实现
/// 类内部，对外统一暴露成"PCM16LE 单声道 16kHz 字节流"，
/// RecognitionSessionService 不需要关心音频到底来自哪种设备。
/// </summary>
public interface IAudioInputSource : IDisposable
{
    /// <summary>每次有新的一批采样到达时触发，float 数组是归一化到
    /// [-1, 1] 区间的单声道 16kHz 采样——跟原来 WaveInEvent 那套
    /// OnDataAvailable 转换出来的格式完全一致，方便复用后续的 VAD 处理
    /// 逻辑，不用重新适配格式。</summary>
    event EventHandler<float[]>? DataAvailable;

    void Start();
    void Stop();
}