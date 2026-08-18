// ============================================================================
// RecognitionSessionService.cs - 麦克风采集 + VAD断句 + SenseVoice识别
// ----------------------------------------------------------------------------
// 直接移植自旧项目 AsrEngine.cs 里已经验证跑通的架构（麦克风采集 -> VAD ->
// 后台线程处理队列 -> 识别结果事件），VAD 部分（sherpa-onnx 的
// VoiceActivityDetector）原样保留，因为只确认过 SenseVoice 有 bug，VAD
// 没有证据表明有问题。
//
// 跟旧代码唯一的架构性差异：旧代码的情绪识别（EmotionEngine.Analyze）是
// 同步本地调用，这次换成 ISenseVoiceService.RecognizeAsync 是异步 HTTP
// 调用，没法用 BlockingCollection + 普通 Thread 那套同步阻塞的处理方式，
// 改用 Channel<float[]> 配合 async 消费循环。其余部分（断句逻辑、Dispose
// 时"先停止入队、等后台任务跑完、再释放VAD"这个顺序）原样保留，那是
// 踩过坑之后定下来的，没有理由重新犯一遍。
// ============================================================================

using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using NAudio.Wave;
using SherpaOnnx;
using Sonvert.App.Services.SenseVoice;
using Sonvert.App.Settings;

namespace Sonvert.App.Services.Recognition;

public class RecognitionResultEventArgs : EventArgs
{
    public string Text { get; init; } = "";
    public string? Emotion { get; init; }
    public string? Event { get; init; }
    public float[] AudioSamples { get; init; } = Array.Empty<float>();
    public int SampleRate { get; init; }
}

public interface IRecognitionSessionService : IAsyncDisposable
{
    /// <summary>识别出一句完整语音时触发。在后台处理线程上触发，
    /// 如果调用方要更新 UI，记得自己切回 UI 线程（Avalonia 的
    /// Dispatcher.UIThread.Post），这里不负责帮你切。</summary>
    event EventHandler<RecognitionResultEventArgs>? ResultReceived;

    /// <summary>开始录音+识别。会先确保 SenseVoiceService 子进程已启动
    /// 并加载好模型（如果还没有的话），然后开始麦克风采集。</summary>
    Task StartAsync();

    /// <summary>停止录音，停止接收新的语音段，但不会卸载 SenseVoice 模型
    /// （模型卸载由调用方决定何时做，通常是"停止翻译"按钮触发）。</summary>
    Task StopAsync();
}

public class RecognitionSessionService : IRecognitionSessionService
{
    private const int SampleRate = 16000;

    private readonly ISettingsService _settingsService;
    private readonly ISenseVoiceService _senseVoiceService;

    private VoiceActivityDetector? _vad;
    
    private WaveInEvent? _waveIn;

    // 用 Channel 替代旧代码里的 BlockingCollection——语义上是等价的
    // （生产者/消费者队列），但 Channel 原生支持 async 读取
    // （ReadAllAsync），能在消费循环里直接 await RecognizeAsync，
    // 不需要额外包一层同步等待。
    private Channel<float[]>? _segmentChannel;
    private Task? _processingTask;
    private CancellationTokenSource? _processingCts;

    private bool _isRunning;

    public event EventHandler<RecognitionResultEventArgs>? ResultReceived;

    public RecognitionSessionService(
        ISettingsService settingsService,
        ISenseVoiceService senseVoiceService)
    {
        _settingsService = settingsService;
        _senseVoiceService = senseVoiceService;
    }

    public async Task StartAsync()
    {
        if (_isRunning)
        {
            return;
        }

        var settings = _settingsService.Current;

        if (string.IsNullOrWhiteSpace(settings.VadModelPath))
        {
            throw new InvalidOperationException(
                "VAD 模型路径未配置（AppSettings.VadModelPath 为空），先在设置里指定 Silero VAD 的 onnx 文件路径");
        }

        // 确保 SenseVoiceService 子进程已经在跑、模型已加载。这两个方法内部
        // 都是幂等的（已经在跑/已经加载会直接返回），调用方不需要自己先判断
        // 状态，每次 StartAsync 老老实实调一遍就行。
        await _senseVoiceService.StartAsync();
        var precision = settings.ModelPrecision == "int8" ? ModelPrecision.Int8 : ModelPrecision.Fp32;
        await _senseVoiceService.LoadModelAsync(precision);

        var vadConfig = new VadModelConfig();
        vadConfig.SileroVad.Model = settings.VadModelPath;
        vadConfig.SampleRate = SampleRate;
        vadConfig.NumThreads = 1;
        vadConfig.Provider = "cpu";
        vadConfig.Debug = 0;

        // 缓冲区大小(秒)，跟旧代码保持一致：30 秒对直播场景单句话长度足够宽裕。
        _vad = new VoiceActivityDetector(vadConfig, 30f);

        _segmentChannel = Channel.CreateUnbounded<float[]>();
        _processingCts = new CancellationTokenSource();
        _processingTask = Task.Run(() => ProcessSegmentsLoopAsync(_processingCts.Token));

        _waveIn = new WaveInEvent
        {
            WaveFormat = new WaveFormat(SampleRate, 16, 1),
            BufferMilliseconds = 100,
            DeviceNumber = settings.InputDeviceIndex >= 0 ? settings.InputDeviceIndex : 0,
        };
        _waveIn.DataAvailable += OnDataAvailable;

        _isRunning = true;
        _waveIn.StartRecording();
    }

    public async Task StopAsync()
    {
        if (!_isRunning)
        {
            return;
        }
        _isRunning = false;

        _waveIn!.StopRecording();
        _waveIn.DataAvailable -= OnDataAvailable;
        _waveIn.Dispose();
        _waveIn = null;

        // 关键顺序（跟旧代码的教训一致）：先停止接收新任务、等后台处理任务
        // 彻底跑完退出，确认它已经不再使用 _vad，才轮到释放 VAD。
        // 提前释放会导致后台任务里出现"访问已释放对象"的异常。
        _segmentChannel!.Writer.Complete();
        if (_processingTask is not null)
        {
            await _processingTask;
        }

        _vad?.Dispose();
        _vad = null;
        _processingCts?.Dispose();
        _processingCts = null;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        int sampleCount = e.BytesRecorded / 2;
        var samples = new float[sampleCount];
        for (int i = 0; i < sampleCount; i++)
        {
            short sample = BitConverter.ToInt16(e.Buffer, i * 2);
            samples[i] = sample / 32768f;
        }

        _vad!.AcceptWaveform(samples);

        // VAD 判断出一段(或多段)完整语音了，取出来写进 Channel——这里只是
        // "入队"，很快就返回，真正耗时的识别在后台任务里做，不会堵住这个
        // 采集回调，新来的音频不会因此丢失。TryWrite 在 Unbounded channel
        // 上永远会成功，不需要 await。
        while (!_vad.IsEmpty())
        {
            var segment = _vad.Front();
            _vad.Pop();
            _segmentChannel!.Writer.TryWrite(segment.Samples);
        }
    }

    private async Task ProcessSegmentsLoopAsync(CancellationToken ct)
    {
        // ReadAllAsync 会一直等新数据，直到 Writer.Complete() 被调用
        // （在 StopAsync 里）才会自然结束循环，语义上对应旧代码里
        // GetConsumingEnumerable + CompleteAdding 那一套。
        await foreach (var samples in _segmentChannel!.Reader.ReadAllAsync(ct))
        {
            if (samples.Length == 0)
            {
                continue;
            }

            try
            {
                // 把 float32 [-1,1] 转成服务要求的 PCM16LE 字节，跟之前
                // Python 端测试脚本里的转换逻辑保持一致（四舍五入，不是
                // 直接截断，避免引入量化偏差）。
                var pcmBytes = ToPcm16Bytes(samples);
                var result = await _senseVoiceService.RecognizeAsync(pcmBytes);

                if (!string.IsNullOrEmpty(result.Text))
                {
                    ResultReceived?.Invoke(this, new RecognitionResultEventArgs
                    {
                        Text = result.Text,
                        Emotion = result.Emotion,
                        Event = result.Event,
                        AudioSamples = samples,
                        SampleRate = SampleRate,
                    });
                }
            }
            catch
            {
                // 单句识别失败不应该让整个后台处理任务崩掉退出（比如某一次
                // HTTP 调用超时），忽略这一句，继续处理后面排队的内容——
                // 跟旧代码的容错策略一致。
            }
        }
    }

    private static byte[] ToPcm16Bytes(float[] samples)
    {
        var bytes = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            short pcm = (short)Math.Round(Math.Clamp(samples[i] * 32767f, -32768f, 32767f));
            BitConverter.GetBytes(pcm).CopyTo(bytes, i * 2);
        }
        return bytes;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }
}
