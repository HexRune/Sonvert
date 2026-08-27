using System;
using NAudio.Wave;

namespace Sonvert.App.Services.Audio;

/// <summary>真实麦克风采集，逻辑跟之前直接写在 RecognitionSessionService
/// 里的那部分完全一样，只是搬到了这个独立类里，方便跟回环采集实现
/// 共用同一个 IAudioInputSource 接口。</summary>
public class MicrophoneInputSource : IAudioInputSource
{
    private const int SampleRate = 16000;
    private readonly int _deviceNumber;
    private WaveInEvent? _waveIn;

    public event EventHandler<float[]>? DataAvailable;

    public MicrophoneInputSource(int deviceNumber)
    {
        _deviceNumber = deviceNumber;
    }

    public void Start()
    {
        _waveIn = new WaveInEvent
        {
            WaveFormat = new WaveFormat(SampleRate, 16, 1),
            BufferMilliseconds = 100,
            DeviceNumber = _deviceNumber,
        };
        _waveIn.DataAvailable += OnDataAvailable;
        _waveIn.StartRecording();
    }

    public void Stop()
    {
        if (_waveIn is null) return;
        _waveIn.StopRecording();
        _waveIn.DataAvailable -= OnDataAvailable;
        _waveIn.Dispose();
        _waveIn = null;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        var sampleCount = e.BytesRecorded / 2;
        var samples = new float[sampleCount];
        for (var i = 0; i < sampleCount; i++)
        {
            var sample = BitConverter.ToInt16(e.Buffer, i * 2);
            samples[i] = sample / 32768f;
        }
        DataAvailable?.Invoke(this, samples);
    }

    public void Dispose() => Stop();
}