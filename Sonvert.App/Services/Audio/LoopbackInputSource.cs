using System;
using NAudio.Wave;
using NAudio.CoreAudioApi;

namespace Sonvert.App.Services.Audio;

/// <summary>
/// 从某个输出设备（比如"扬声器"、某个虚拟声卡）抓取"即将播放出来的所有
/// 声音"，用于"翻译游戏/电影里的对话"这类场景——不需要用户额外接线，
/// 直接选中他们平时在用的输出设备就行。
///
/// 关键限制）：这种方式抓到的是这个输出
/// 设备上所有程序混在一起的声音，没法只挑出某一个程序。如果同时有别的
/// 程序也在出声（提示音、后台音乐），会一起被采集进去干扰识别效果。后续看情况再加上"只抓某个程序的声音"的功能（NAudi
///
/// WasapiLoopbackCapture 默认用的采样格式是系统当前输出设备的实际格式
/// （常见是 44.1kHz/32bit float/双声道，不是我们需要的 16kHz/16bit/
/// 单声道），所以这里在 DataAvailable 回调里做了格式转换：先把交织的
/// 双声道 float 采样降混成单声道，再做采样率转换。
/// </summary>
public class LoopbackInputSource : IAudioInputSource
{
    private const int TargetSampleRate = 16000;
    private readonly MMDevice _device;
    private WasapiLoopbackCapture? _capture;

    public event EventHandler<float[]>? DataAvailable;

    public LoopbackInputSource(MMDevice device)
    {
        _device = device;
    }

    public void Start()
    {
        _capture = new WasapiLoopbackCapture(_device);
        _capture.DataAvailable += OnDataAvailable;
        _capture.StartRecording();
    }

    public void Stop()
    {
        if (_capture is null) return;
        _capture.StopRecording();
        _capture.DataAvailable -= OnDataAvailable;
        _capture.Dispose();
        _capture = null;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        var sourceFormat = _capture!.WaveFormat;

        // 第一步：把交织的多声道 32bit float 采样，按声道数分组求平均，
        // 降混成单声道——不做这一步的话，双声道数据会被错误地当成
        // 单声道的两倍长度数据，后续 VAD/识别会完全乱套。
        var floatSamples = BytesToFloatSamples(e.Buffer, e.BytesRecorded, sourceFormat.BitsPerSample);
        var monoSamples = DownmixToMono(floatSamples, sourceFormat.Channels);

        // 第二步：从源采样率（通常 44.1kHz）转换到目标 16kHz——用最简单的
        // 线性插值重采样，不追求音质，只追求"识别效果够用"，这类场景对
        // 音频保真度要求不高，没必要引入专门的重采样库增加依赖。
        var resampled = LinearResample(monoSamples, sourceFormat.SampleRate, TargetSampleRate);

        DataAvailable?.Invoke(this, resampled);
    }

    private static float[] BytesToFloatSamples(byte[] buffer, int bytesRecorded, int bitsPerSample)
    {
        var bytesPerSample = bitsPerSample / 8;
        var sampleCount = bytesRecorded / bytesPerSample;
        var samples = new float[sampleCount];

        for (var i = 0; i < sampleCount; i++)
        {
            samples[i] = BitConverter.ToSingle(buffer, i * bytesPerSample);
        }

        return samples;
    }

    private static float[] DownmixToMono(float[] samples, int channels)
    {
        if (channels <= 1) return samples;

        var frameCount = samples.Length / channels;
        var mono = new float[frameCount];

        for (var frame = 0; frame < frameCount; frame++)
        {
            float sum = 0;
            for (var ch = 0; ch < channels; ch++)
            {
                sum += samples[frame * channels + ch];
            }
            mono[frame] = sum / channels;
        }

        return mono;
    }

    private static float[] LinearResample(float[] samples, int sourceRate, int targetRate)
    {
        if (sourceRate == targetRate) return samples;

        var ratio = (double)targetRate / sourceRate;
        var outputLength = (int)(samples.Length * ratio);
        var output = new float[outputLength];

        for (var i = 0; i < outputLength; i++)
        {
            var sourceIndex = i / ratio;
            var indexFloor = (int)sourceIndex;
            var indexCeil = Math.Min(indexFloor + 1, samples.Length - 1);
            var fraction = sourceIndex - indexFloor;

            output[i] = (float)(samples[indexFloor] * (1 - fraction) + samples[indexCeil] * fraction);
        }

        return output;
    }

    public void Dispose() => Stop();
}