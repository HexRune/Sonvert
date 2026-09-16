using Sonvert.App.Settings;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Sonvert.App.Services.Tts;

/// <summary>
/// ITtsService 的 Azure 语音合成实现。跟本地 GPT-SoVITS（LocalTtsService）
/// 完全不同的协议：没有声音克隆，靠 Azure 预置的神经网络音色朗读；
/// 情绪不是靠切换参考音频，是靠 SSML 的 mstts:express-as style 标签
/// 告诉 Azure "用什么语气念"，而且不是所有音色都支持这个标签——只有
/// 挑选过的、风格库比较丰富的音色才配得上"情绪跟随"这个功能，见
/// HomeViewModel 里 EnglishVoiceOptions/ChineseVoiceOptions 的选取说明。
///
/// 请求格式（REST，不是 SDK）：
///   POST https://{region}.tts.speech.microsoft.com/cognitiveservices/v1
///   Headers: Ocp-Apim-Subscription-Key: {key}
///            Content-Type: application/ssml+xml
///            X-Microsoft-OutputFormat: riff-24khz-16bit-mono-pcm
///   Body:    SSML 文本
///   Resp:    原始音频字节流（不是 JSON 包一层，直接就是音频数据本身）
///
/// 输出格式必须是 riff-*-pcm（真正的 WAV 容器），不能选 mp3/ogg 这类
/// 压缩格式——项目现有的 NAudioPlaybackService 播放逻辑是写死用
/// NAudio.Wave.WaveFileReader 解码的，只认 RIFF/WAV，给它 MP3 会直接
/// 播放失败。这是看现有播放代码发现的硬约束，不是随便选的格式。
/// 这个约束只针对 SynthesizeAsync 这条非流式路径——SynthesizeStreamingAsync
/// 见下面的注释，走的是另一个专门为流式设计的输出格式，两条路径互不影响。
/// </summary>
public class AzureTtsService : ITtsService
{
    private static readonly XNamespace SsmlNamespace = "http://www.w3.org/2001/10/synthesis";
    private static readonly XNamespace MsttsNamespace = "http://www.w3.org/2001/mstts";

    private readonly ISettingsService _settingsService;
    private readonly HttpClient _httpClient;

    public AzureTtsService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
    }

    public Task StartAsync() => Task.CompletedTask; // 调远程 API，不需要拉子进程

    public Task PrewarmReferenceAudioAsync(int characterId) => Task.CompletedTask; // Azure 没有参考音频这个概念

    public Task StopAsync() => Task.CompletedTask;

    public async Task<TtsResult> SynthesizeAsync(
        string text, string language, string emotion,
        float[]? liveEmotionAudio = null, int liveEmotionSampleRate = 0)
    {
        var settings = _settingsService.Current;

        if (string.IsNullOrWhiteSpace(settings.TTSApiRegion) || string.IsNullOrWhiteSpace(settings.TTSApiKey))
        {
            throw new InvalidOperationException(
                "Azure 语音合成未配置完整。请在首页填写区域和 API Key。");
        }

        // 按目标语言选对应的音色——不是同一个音色兼顾两种语言，
        // 见类注释和 HomeViewModel 里两个音色下拉框分开设计的原因。
        var voiceName = language switch
        {
            "zh" => settings.TTSApiVoiceZh,
            "en" => settings.TTSApiVoiceEn,
            _ => throw new InvalidOperationException($"Azure 语音合成暂不支持语言代码: {language}"),
        };

        if (string.IsNullOrWhiteSpace(voiceName))
        {
            throw new InvalidOperationException(
                $"还没有给\"{language}\"这个语言选择 Azure 音色，请在首页选择对应的音色。");
        }

        var ssml = BuildSsml(text, voiceName, settings.TTSEmotionFollowEnabled ? emotion : null);

        var url = $"https://{settings.TTSApiRegion}.tts.speech.microsoft.com/cognitiveservices/v1";
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(ssml),
        };
        httpRequest.Content.Headers.ContentType = new MediaTypeHeaderValue("application/ssml+xml");
        httpRequest.Headers.Add("Ocp-Apim-Subscription-Key", settings.TTSApiKey);
        httpRequest.Headers.Add("X-Microsoft-OutputFormat", "riff-24khz-16bit-mono-pcm");
        httpRequest.Headers.UserAgent.Add(new ProductInfoHeaderValue("Sonvert", "1.0"));

        var stopwatch = Stopwatch.StartNew();
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(httpRequest);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // ex.Message 对 SSL 握手失败这类问题几乎不给任何有用信息
            // （.NET 自己都说"see inner exception"了），真正的原因在
            // ex.InnerException 里（比如具体是证书链验证失败、还是
            // TLS 版本协商不上、还是连接直接被重置）。之前这里只拼了
            // ex.Message，把这部分信息丢了，排查网络问题时日志基本
            // 没用——这次把 InnerException 的信息也带上。
            var detail = ex.InnerException?.Message ?? ex.Message;
            throw new InvalidOperationException($"调用 Azure 语音合成失败（网络层）: {ex.Message} | {detail}", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            // Azure 语音合成失败时返回的是普通文本/空响应体，不像翻译那两个
            // 接口有统一的 JSON 错误结构，直接把状态码和响应体原文抛出去，
            // 不用费劲反序列化一个不存在的错误格式。
            var body = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"Azure 语音合成请求失败: [{(int)response.StatusCode}] {body}");
        }

        var audioData = await response.Content.ReadAsByteArrayAsync();
        stopwatch.Stop();

        Debug.WriteLine($"[AzureTts] voice={voiceName} elapsed={stopwatch.ElapsedMilliseconds}ms");

        return new TtsResult
        {
            AudioData = audioData,
            MediaType = "wav",
        };
    }

    /// <summary>Azure 是第二个支持流式合成的引擎（第一个是阿里云）。
    /// 跟阿里云不一样的地方：阿里云走的是它自己的 WebSocket 协议，
    /// 是完全不同的一套连接/帧解析逻辑；Azure 这边用的还是
    /// SynthesizeAsync 那个同一个 REST 端点，唯一的区别是：
    /// 1) X-Microsoft-OutputFormat 换成 raw-24khz-16bit-mono-pcm
    ///    （裸 PCM，没有 WAV 容器头）而不是 riff-24khz-16bit-mono-pcm。
    ///    这不是随便换的——Azure 官方文档明确把输出格式分成 Streaming/
    ///    NonStreaming 两类，riff-* 因为文件头里要写死数据总长度，
    ///    必须等全部合成完才能确定，天然进不了 Streaming 那一类；
    ///    raw-* 没有这个问题，是文档里明确列在 Streaming 里的格式。
    ///    riff-* 那条路径（SynthesizeAsync）完全不受影响，两条路径
    ///    用的是两个不同的 OutputFormat 值，互不冲突。
    /// 2) 发请求时要用 HttpCompletionOption.ResponseHeadersRead，让
    ///    SendAsync 一收到响应头就返回，不用等整个响应体（对于流式
    ///    场景，说白了就是不知道要等多久）下载完才继续往下走——默认的
    ///    HttpCompletionOption.ResponseContentRead 会等整个 body 收完，
    ///    那样跟 SynthesizeAsync 的老实等待就没区别了，加了流式格式
    ///    也是白搭。
    /// 3) 读响应体不再用一次性的 ReadAsByteArrayAsync，而是拿到
    ///    Stream 之后循环读固定大小的缓冲区，读到多少就 yield return
    ///    多少——因为已经是裸 PCM，读到的字节可以直接扔给播放缓冲区，
    ///    不需要像处理 riff 格式那样解析/跳过容器头。
    /// 语言/音色选择、情绪转 Azure style 这些逻辑，跟 SynthesizeAsync
    /// 完全一样，直接复用 BuildSsml，没有另外写一份。</summary>
    public bool SupportsStreaming => true;

    /// <summary>对应 raw-24khz-16bit-mono-pcm 这个格式本身规定的采样率/
    /// 声道数/位深——这三个数字不是随便填的，是这个具体输出格式名字
    /// 里已经写明的参数，如果以后哪天换了别的采样率的 raw 格式，这里
    /// 要跟着改，两边必须完全对应，播放缓冲区才不会因为参数不匹配而
    /// 放出噪音或者变速变调。</summary>
    public (int SampleRate, int Channels, int BitsPerSample) StreamingAudioFormat => (24000, 1, 16);

    public async IAsyncEnumerable<byte[]> SynthesizeStreamingAsync(
        string text, string language, string emotion,
        float[]? liveEmotionAudio = null, int liveEmotionSampleRate = 0)
    {
        var settings = _settingsService.Current;

        if (string.IsNullOrWhiteSpace(settings.TTSApiRegion) || string.IsNullOrWhiteSpace(settings.TTSApiKey))
        {
            throw new InvalidOperationException(
                "Azure 语音合成未配置完整。请在首页填写区域和 API Key。");
        }

        var voiceName = language switch
        {
            "zh" => settings.TTSApiVoiceZh,
            "en" => settings.TTSApiVoiceEn,
            _ => throw new InvalidOperationException($"Azure 语音合成暂不支持语言代码: {language}"),
        };

        if (string.IsNullOrWhiteSpace(voiceName))
        {
            throw new InvalidOperationException(
                $"还没有给\"{language}\"这个语言选择 Azure 音色，请在首页选择对应的音色。");
        }

        var ssml = BuildSsml(text, voiceName, settings.TTSEmotionFollowEnabled ? emotion : null);

        var url = $"https://{settings.TTSApiRegion}.tts.speech.microsoft.com/cognitiveservices/v1";
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(ssml),
        };
        httpRequest.Content.Headers.ContentType = new MediaTypeHeaderValue("application/ssml+xml");
        httpRequest.Headers.Add("Ocp-Apim-Subscription-Key", settings.TTSApiKey);
        // 唯一跟 SynthesizeAsync 不一样的请求头——裸 PCM 而不是 WAV 容器，
        // 理由见上面类方法的注释。
        httpRequest.Headers.Add("X-Microsoft-OutputFormat", "raw-24khz-16bit-mono-pcm");
        httpRequest.Headers.UserAgent.Add(new ProductInfoHeaderValue("Sonvert", "1.0"));

        var stopwatch = Stopwatch.StartNew();
        HttpResponseMessage response;
        try
        {
            // ResponseHeadersRead 是这条路径能不能真正流式的关键——
            // 不加这个参数，SendAsync 会跟 SynthesizeAsync 一样傻等整个
            // 响应体下载完才返回，后面读流的代码就变得毫无意义。
            response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // 理由同 SynthesizeAsync 那边的同款修改——把 InnerException
            // 也带上，不然网络层的报错基本等于没打印有效信息。
            var detail = ex.InnerException?.Message ?? ex.Message;
            throw new InvalidOperationException($"调用 Azure 语音合成（流式）失败（网络层）: {ex.Message} | {detail}", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"Azure 语音合成（流式）请求失败: [{(int)response.StatusCode}] {body}");
        }

        var isFirstChunk = true;
        await using var stream = await response.Content.ReadAsStreamAsync();
        // 4KB 是随手选的一个折中值：太小会让每次 yield 的开销（协程切换、
        // 下游处理一次调用的固定成本）占比过高，太大又会让"边收边播"的
        // 颗粒度变粗，失去流式本身的意义。后续如果实测发现首字节延迟
        // 没有明显改善，这个数字是第一个可以调的旋钮。
        var buffer = new byte[4096];
        int bytesRead;
        while ((bytesRead = await stream.ReadAsync(buffer)) > 0)
        {
            if (isFirstChunk)
            {
                Debug.WriteLine($"[AzureTts] voice={voiceName} streaming first chunk elapsed={stopwatch.ElapsedMilliseconds}ms");
                isFirstChunk = false;
            }

            // 必须拷贝一份新数组再 yield——buffer 这个数组会在下一次循环
            // 被 ReadAsync 复用覆盖，直接把 buffer 本身 yield 出去，下游
            // 拿到的数据会在它还没处理完的时候就被写坏。
            var chunk = new byte[bytesRead];
            Array.Copy(buffer, chunk, bytesRead);
            yield return chunk;
        }

        stopwatch.Stop();
        Debug.WriteLine($"[AzureTts] voice={voiceName} streaming total elapsed={stopwatch.ElapsedMilliseconds}ms");
    }

    /// <summary>把 SenseVoice 的情绪标签映射成 Azure 支持的 style 值。
    /// 只覆盖 HomeViewModel 里当前预置的 Jenny/晓晓 这两个音色都支持的
    /// 风格——以后往音色列表里加新选项时，如果新音色的风格库跟这两个
    /// 不完全一样，这个映射表可能需要跟着调整（比如某个风格新音色不
    /// 支持，就要单独判断降级）。
    /// SURPRISED 没有直接对应的 Azure 风格，用 cheerful 近似替代
    /// （偏正向、有活力，是几个选项里语气最接近的）；NEUTRAL/
    /// EMO_UNKNOWN 以及"情绪跟随"关闭时，返回 null，表示不加 style
    /// 标签、走音色的默认语气。</summary>
    private static string? MapEmotionToAzureStyle(string? emotion) => emotion switch
    {
        "HAPPY" => "cheerful",
        "SAD" => "sad",
        "ANGRY" => "angry",
        "FEARFUL" => "fearful",
        "DISGUSTED" => "disgruntled",
        "SURPRISED" => "cheerful", // 近似替代，见方法注释
        _ => null, // NEUTRAL / EMO_UNKNOWN / null（情绪跟随关闭时传进来的就是 null）
    };

    /// <summary>用 System.Xml.Linq 拼 SSML，而不是手动拼字符串——
    /// 朗读的文本内容来自识别/翻译结果，可能包含 &amp;/&lt;/&gt; 这类
    /// XML 特殊字符，手动拼字符串容易漏转义导致生成的 SSML 本身就是
    /// 非法 XML，用 XElement 由它自动处理转义更可靠。</summary>
    private static string BuildSsml(string text, string voiceName, string? emotion)
    {
        // Azure 的音色命名规则是 "{语言}-{地区}-{名字}Neural"，比如
        // "en-US-JennyNeural"、"zh-CN-XiaoxiaoNeural"——取前两段用 '-'
        // 拼起来就是 SSML 需要的 xml:lang 值（"en-US"/"zh-CN"），不用
        // 为每个音色再单独维护一个 locale 字段。
        var localeParts = voiceName.Split('-');
        var locale = localeParts.Length >= 2 ? $"{localeParts[0]}-{localeParts[1]}" : voiceName;

        var style = MapEmotionToAzureStyle(emotion);

        var voiceContent = style is null
            ? (object)text
            : new XElement(MsttsNamespace + "express-as", new XAttribute("style", style), text);

        var speak = new XElement(SsmlNamespace + "speak",
            new XAttribute("version", "1.0"),
            new XAttribute(XNamespace.Xmlns + "mstts", MsttsNamespace.NamespaceName),
            new XAttribute(XNamespace.Xml + "lang", locale),
            new XElement(SsmlNamespace + "voice",
                new XAttribute("name", voiceName),
                voiceContent));

        return speak.ToString(SaveOptions.DisableFormatting);
    }
}
