using System;
using System.IO;
using System.Threading.Tasks;
using NAudio.Wave;

namespace Sonvert.App.Services.Audio;

/// <summary>
/// 用 NAudio 的 WaveInEvent 录制麦克风。录制格式固定为 16kHz/单声道/16bit——
/// 这是语音场景的常见标准格式，GPT-SoVITS 的参考音频没有特殊格式要求，
/// 这个格式能保证干净、体积也不大。
/// </summary>
public class NAudioRecordingService : IAudioRecordingService
{
    private WaveInEvent? _waveIn;
    private MemoryStream? _memoryStream;
    private WaveFileWriter? _writer;
    private TaskCompletionSource<byte[]>? _stopTcs;

    public bool IsRecording { get; private set; }

    public void StartRecording()
    {
        if (IsRecording)
        {
            return;
        }

        _memoryStream = new MemoryStream();
        _waveIn = new WaveInEvent
        {
            WaveFormat = new WaveFormat(rate: 16000, bits: 16, channels: 1),
        };
        _writer = new WaveFileWriter(_memoryStream, _waveIn.WaveFormat);

        _waveIn.DataAvailable += (_, e) =>
        {
            _writer?.Write(e.Buffer, 0, e.BytesRecorded);
        };

        _waveIn.RecordingStopped += (_, _) =>
        {
            _writer?.Flush();
            var bytes = _memoryStream?.ToArray() ?? Array.Empty<byte>();
            _stopTcs?.TrySetResult(bytes);
        };

        _waveIn.StartRecording();
        IsRecording = true;
    }

    public Task<byte[]> StopRecordingAsync()
    {
        if (!IsRecording || _waveIn is null)
        {
            return Task.FromResult(Array.Empty<byte>());
        }

        _stopTcs = new TaskCompletionSource<byte[]>();
        _waveIn.StopRecording(); // 异步触发 RecordingStopped，不会立刻返回数据

        IsRecording = false;

        return _stopTcs.Task.ContinueWith(t =>
        {
            _writer?.Dispose();
            _memoryStream?.Dispose();
            _waveIn?.Dispose();
            _writer = null;
            _memoryStream = null;
            _waveIn = null;
            return t.Result;
        });
    }
}