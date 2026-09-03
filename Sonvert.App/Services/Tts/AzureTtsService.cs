using Sonvert.App.Settings;
using System;
using System.Diagnostics;
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

    public async Task<TtsResult> SynthesizeAsync(string text, string language, string emotion)
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
            throw new InvalidOperationException($"调用 Azure 语音合成失败（网络层）: {ex.Message}", ex);
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
