using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Sonvert.App.Services.Audio;
using Sonvert.App.Services.Characters;
using Sonvert.App.Settings;

namespace Sonvert.App.Services.Tts;

/// <summary>
/// 阿里云百炼 Qwen-Audio-TTS/CosyVoice 语音合成，走官方 WebSocket 协议
/// 直接实现（不经过 dashscope 的 Python SDK——这是纯云端 API 调用，没有
/// 必要为了调一个 WebSocket 接口去起一个 Python 子进程，直接在 C# 里用
/// ClientWebSocket 实现协议本身）。
///
/// 协议参考：
/// https://help.aliyun.com/en/model-studio/cosyvoice-websocket-api
/// https://help.aliyun.com/en/model-studio/cosyvoice-client-events
/// https://help.aliyun.com/en/model-studio/cosyvoice-server-events
///
/// 交互流程：建连接（整个进程生命周期只建一次，官方文档明确建议"复用
/// 连接，不要每次任务都新建"）→ 发 run-task → 等 task-started → 发
/// continue-task（文本）→ 发 finish-task → 陆续收到 result-generated
/// 事件 + 二进制音频帧，直到收到 task-finished → 这一轮任务结束，
/// 连接留着给下一轮用。
///
/// 跟本地引擎（IndexTTS/Qwen3-TTS 本地版）最大的架构差异：音色不是每次
/// 传参考音频文件，而是传一个提前"声音复刻"注册好的 voice_id（存在
/// Character.AliyunVoiceId 上）——复刻这个动作本身不是这个类做的事，
/// 是一次性的、需要参考音频先有公网可访问 URL 才能做的注册流程，见
/// Character.cs 里 AliyunVoiceId 字段的注释。
///
/// liveEmotionAudio/liveEmotionSampleRate 这两个参数这里不用——阿里云
/// 这套协议没有"传音频做情绪参考"这个机制，情绪控制是通过 instruction
/// 文本指令（比如"请用开心的语气说"），这个跟 IndexTTS 的音频情绪参考
/// 是完全不同的机制，第一版先不接，只做基本的音色克隆合成。
/// </summary>
public class AliyunTtsService : ITtsService, IAsyncDisposable
{
    private readonly ISettingsService _settingsService;
    private readonly ICharacterRepository _characterRepository;
    private ClientWebSocket? _webSocket;

    // 官方文档明确要求"同一时间只应该有一个进行中的任务"（result-generated
    // 的二进制音频帧不带 task_id，只能按发送顺序对应），这个信号量确保
    // 同一条连接上任务严格串行，不会有两句话的音频帧互相穿插。
    private readonly SemaphoreSlim _taskLock = new(1, 1);

    public AliyunTtsService(ISettingsService settingsService, ICharacterRepository characterRepository)
    {
        _settingsService = settingsService;
        _characterRepository = characterRepository;
    }

    public async Task StartAsync()
    {
        if (_webSocket is { State: WebSocketState.Open })
        {
            return;
        }

        var settings = _settingsService.Current;
        if (string.IsNullOrWhiteSpace(settings.TTSApiKey))
        {
            throw new InvalidOperationException("阿里云 API Key 还没填，去设置里配置 TTSApiKey");
        }
        if (string.IsNullOrWhiteSpace(settings.TTSAliyunWebSocketUrl))
        {
            throw new InvalidOperationException(
                "阿里云 WebSocket 地址还没填——每个百炼工作空间的地址都不一样，" +
                "去百炼控制台确认之后填进设置里的 TTSAliyunWebSocketUrl");
        }

        _webSocket?.Dispose();
        _webSocket = new ClientWebSocket();
        _webSocket.Options.SetRequestHeader("Authorization", $"Bearer {settings.TTSApiKey}");

        await _webSocket.ConnectAsync(new Uri(settings.TTSAliyunWebSocketUrl), CancellationToken.None);
    }

    public async Task<TtsResult> SynthesizeAsync(
        string text, string language, string emotion,
        float[]? liveEmotionAudio = null, int liveEmotionSampleRate = 0)
    {
        var (voiceId, model) = await PrepareRequestAsync();

        await _taskLock.WaitAsync();
        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            int? timeToFirstByteMs = null;
            var pcmBuffer = new MemoryStream();

            await foreach (var chunk in RunSynthesisTaskStreamingAsync(text, voiceId, model))
            {
                timeToFirstByteMs ??= (int)stopwatch.ElapsedMilliseconds;
                pcmBuffer.Write(chunk, 0, chunk.Length);
            }

            // RunSynthesisTaskStreamingAsync 吐出来的是已经剥离容器格式头的
            // 裸 PCM，这里要重新包一层 WAV 头才是个能被 NAudio/播放器直接
            // 打开的合法文件——跟 StreamingAudioFormat 用的是同一组参数，
            // 两边必须保持一致，不然包出来的 WAV 头跟实际数据对不上。
            var (sampleRate, channels, bitsPerSample) = StreamingAudioFormat;
            var wavBytes = WavEncoder.EncodePcmBytesToWav(pcmBuffer.ToArray(), sampleRate, channels, bitsPerSample);

            return new TtsResult { AudioData = wavBytes, MediaType = "wav", TimeToFirstByteMs = timeToFirstByteMs };
        }
        finally
        {
            _taskLock.Release();
        }
    }

    public bool SupportsStreaming => true;

    /// <summary>固定 24000Hz/单声道/16bit——对应请求里写死的 sample_rate:
    /// 24000，CosyVoice/Qwen-Audio-TTS 的 wav 输出是 16bit PCM 单声道，
    /// 这个组合没有暴露成可配置项，改 sample_rate 请求参数的话这里要
    /// 跟着改，两处不同步会导致播放出来的声音变调/变速。</summary>
    public (int SampleRate, int Channels, int BitsPerSample) StreamingAudioFormat => (24000, 1, 16);

    public async IAsyncEnumerable<byte[]> SynthesizeStreamingAsync(
        string text, string language, string emotion,
        float[]? liveEmotionAudio = null, int liveEmotionSampleRate = 0)
    {
        var (voiceId, model) = await PrepareRequestAsync();

        await _taskLock.WaitAsync();
        try
        {
            await foreach (var chunk in RunSynthesisTaskStreamingAsync(text, voiceId, model))
            {
                yield return chunk;
            }
        }
        finally
        {
            _taskLock.Release();
        }
    }

    /// <summary>确保连接活着、解析出这次请求要用的 voice_id——
    /// SynthesizeAsync 和 SynthesizeStreamingAsync 共用这一步，避免两份
    /// 重复代码。</summary>
    private async Task<(string VoiceId, string Model)> PrepareRequestAsync()
    {
        if (_webSocket is not { State: WebSocketState.Open })
        {
            // 连接可能因为空闲超时或者网络问题掉了，重连一次而不是直接报错——
            // 云端连接不像本地子进程那样"一直开着就一定还活着"，断线是
            // 正常会发生的事，具备自动重连比要求调用方每次都先检查健康
            // 状态更省心。
            await StartAsync();
        }

        var settings = _settingsService.Current;

        // 优先用设置页手动填的 voice_id（测试用，见 AppSettings.TTSAliyunManualVoiceId
        // 的注释）；没填的话才去查角色身上的 AliyunVoiceId——这个字段
        // 保留着，不删，等以后做完整的应用内声音复刻注册流程时会用上。
        string voiceId;
        if (!string.IsNullOrWhiteSpace(settings.TTSAliyunManualVoiceId))
        {
            voiceId = settings.TTSAliyunManualVoiceId;
        }
        else
        {
            if (settings.ActiveCharacterId is not { } characterId)
            {
                throw new InvalidOperationException("还没选择角色，先在首页选一个角色再开始翻译");
            }

            var character = await _characterRepository.GetByIdAsync(characterId);
            if (string.IsNullOrWhiteSpace(character?.AliyunVoiceId))
            {
                throw new InvalidOperationException(
                    $"角色 {characterId} 还没有阿里云声音复刻的 voice_id，也没有在设置里手动填一个测试用的——" +
                    "两者选一个：填 Character.AliyunVoiceId，或者在首页语音合成设置里填 TtsAliyunManualVoiceId");
            }
            voiceId = character.AliyunVoiceId;
        }

        return (voiceId, settings.TTSAliyunModel);
    }

    /// <summary>协议核心实现，两个公开方法（一次性返回完整结果的
    /// SynthesizeAsync、陆续吐 chunk 的 SynthesizeStreamingAsync）都基于
    /// 这一份逻辑，不重复实现协议细节。调用方必须已经持有 _taskLock。
    /// 吐出来的每个 byte[] 都是剥离了 WAV 容器头之后的裸 PCM 数据——
    /// 第一帧原始数据检测到 "RIFF" 头就砍掉标准的 44 字节头，后续帧
    /// 本来就是纯 PCM，不用处理。</summary>
    private async IAsyncEnumerable<byte[]> RunSynthesisTaskStreamingAsync(string text, string voiceId, string model)
    {
        var taskId = Guid.NewGuid().ToString();

        await SendJsonAsync(new
        {
            header = new { action = "run-task", task_id = taskId, streaming = "duplex" },
            payload = new
            {
                task_group = "audio",
                task = "tts",
                function = "SpeechSynthesizer",
                model,
                parameters = new
                {
                    text_type = "PlainText",
                    voice = voiceId,
                    format = "wav",
                    sample_rate = 24000,
                },
                input = new { },
            },
        });

        await WaitForServerEventAsync(taskId, "task-started");

        await SendJsonAsync(new
        {
            header = new { action = "continue-task", task_id = taskId, streaming = "duplex" },
            payload = new { input = new { text } },
        });

        await SendJsonAsync(new
        {
            header = new { action = "finish-task", task_id = taskId, streaming = "duplex" },
            payload = new { input = new { } },
        });

        // finish-task 发出去之后，陆续收到 result-generated（每个事件后面
        // 紧跟一帧二进制音频）直到 task-finished——这里不区分具体是哪个
        // sentence-* 子类型，只要是二进制帧就按顺序往外吐，因为一次
        // 调用对应的是一整句要合成的文本，不需要在这一层区分内部再
        // 拆分出的子句。
        var isFirstBinaryFrame = true;
        var buffer = ArrayPool<byte>.Shared.Rent(65536);
        try
        {
            while (true)
            {
                using var frame = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await _webSocket!.ReceiveAsync(buffer, CancellationToken.None);
                    frame.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Binary)
                {
                    var bytes = frame.ToArray();
                    if (isFirstBinaryFrame)
                    {
                        isFirstBinaryFrame = false;
                        bytes = StripWavHeaderIfPresent(bytes);
                    }
                    if (bytes.Length > 0) yield return bytes;
                    continue;
                }

                // 文本消息：是某个 result-generated/task-finished/task-failed 事件。
                frame.Position = 0;
                using var doc = await JsonDocument.ParseAsync(frame);
                var header = doc.RootElement.GetProperty("header");
                var eventType = header.GetProperty("event").GetString();
                var eventTaskId = header.GetProperty("task_id").GetString();

                if (eventTaskId != taskId) continue;

                if (eventType == "task-failed")
                {
                    var errorMessage = header.TryGetProperty("error_message", out var msg) ? msg.GetString() : "未知错误";
                    throw new InvalidOperationException($"阿里云 TTS 任务失败: {errorMessage}");
                }

                if (eventType == "task-finished") yield break;

                // 其余情况（result-generated 的 sentence-begin/synthesis/end）
                // 只是进度信息，音频数据已经在紧随其后的二进制帧里处理过了，
                // 这里不需要对文本内容做任何事。
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>标准 WAV 文件头固定 44 字节（RIFF/WAVE/fmt /data 这几个
    /// 子块，没有额外扩展字段的最简单情况）。先确认前 4 字节是 "RIFF"
    /// 签名再剥离，不是"反正是第一帧就无脑砍 44 字节"——万一服务端某天
    /// 改成直接吐裸 PCM（不带头），这里应该保持数据原样，而不是错误地
    /// 砍掉真实音频数据的开头一截。</summary>
    private static byte[] StripWavHeaderIfPresent(byte[] bytes)
    {
        if (bytes.Length > 44 && bytes[0] == 'R' && bytes[1] == 'I' && bytes[2] == 'F' && bytes[3] == 'F')
        {
            return bytes[44..];
        }
        return bytes;
    }

    private async Task SendJsonAsync(object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        var bytes = Encoding.UTF8.GetBytes(json);
        await _webSocket!.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);
    }

    /// <summary>阻塞等到收到指定 task_id、指定事件类型的文本消息为止。
    /// 只在 run-task 之后等 task-started 这一步用——后面 continue-task/
    /// finish-task 发出去之后走的是 RunSynthesisTaskStreamingAsync 里的
    /// 循环，因为那段时间文本事件和二进制音频帧是混着来的。</summary>
    private async Task WaitForServerEventAsync(string taskId, string expectedEvent)
    {
        var buffer = new byte[8192];
        while (true)
        {
            var result = await _webSocket!.ReceiveAsync(buffer, CancellationToken.None);
            if (result.MessageType != WebSocketMessageType.Text) continue;

            using var doc = JsonDocument.Parse(buffer.AsMemory(0, result.Count));
            var header = doc.RootElement.GetProperty("header");
            var eventType = header.GetProperty("event").GetString();
            var eventTaskId = header.GetProperty("task_id").GetString();

            if (eventTaskId != taskId) continue; // 理论上不该发生（同一条连接上任务严格串行），防御性跳过

            if (eventType == "task-failed")
            {
                var errorMessage = header.TryGetProperty("error_message", out var msg) ? msg.GetString() : "未知错误";
                throw new InvalidOperationException($"阿里云 TTS 任务失败: {errorMessage}");
            }

            if (eventType == expectedEvent) return;
        }
    }

    public Task PrewarmReferenceAudioAsync(int characterId)
    {
        // 阿里云这边"预热"的等价物是声音复刻注册，那是一次性动作、
        // 需要参考音频先有公网 URL 才能做，不是每次开始翻译前都要做的
        // 事——见 Character.cs 里 AliyunVoiceId 的注释。这里保持空实现。
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (_webSocket is not { State: WebSocketState.Open })
        {
            return;
        }

        try
        {
            await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
        }
        catch (WebSocketException)
        {
            // 连接可能已经因为别的原因断了，忽略，走下面的 Dispose 兜底。
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _webSocket?.Dispose();
        _taskLock.Dispose();
    }
}
