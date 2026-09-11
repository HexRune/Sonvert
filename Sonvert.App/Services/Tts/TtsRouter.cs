using System.Threading.Tasks;
using Sonvert.App.Settings;

namespace Sonvert.App.Services.Tts;

/// <summary>
/// 对外暴露的 ITtsService 实现，内部按 AppSettings.TTSProvider 在
/// "本地"和"API 合成"之间转发；选了 local 之后，再按 TTSLocalEngine 在
/// LocalTtsService（GPT-SoVITS）、IndexTtsService、QwenTtsService 三者
/// 之间选择；选了 api 则按 TTSApiKind 在 ApiTtsService（占位/未实现的
/// 服务商）和 AzureTtsService（真正实现的 Azure 语音合成）之间选择——
/// local 和 api 两条分支各自内部都是"再细分一层协议/引擎"的同一种设计，
/// 跟 TranslationRouter 保持一致。
/// </summary>
public class TtsRouter : ITtsService
{
    private readonly ISettingsService _settingsService;
    private readonly LocalTtsService _local;
    private readonly IndexTtsService _indexTts;
    private readonly QwenTtsService _qwenTts;
    private readonly ApiTtsService _api;
    private readonly AzureTtsService _azure;
    private readonly AliyunTtsService _aliyun;

    public TtsRouter(
        ISettingsService settingsService,
        LocalTtsService local,
        IndexTtsService indexTts,
        QwenTtsService qwenTts,
        ApiTtsService api,
        AzureTtsService azure,
        AliyunTtsService aliyun)
    {
        _settingsService = settingsService;
        _local = local;
        _indexTts = indexTts;
        _qwenTts = qwenTts;
        _api = api;
        _azure = azure;
        _aliyun = aliyun;
    }

    private ITtsService Active
    {
        get
        {
            var settings = _settingsService.Current;
            if (settings.TTSProvider != "api")
            {
                return settings.TTSLocalEngine switch
                {
                    "indextts" => _indexTts,
                    "qwen-tts" => _qwenTts,
                    _ => _local,
                };
            }

            return settings.TTSApiKind switch
            {
                "azure" => _azure,
                "aliyun" => _aliyun,
                _ => _api,
            };
        }
    }

    public Task StartAsync() => Active.StartAsync();

    public Task<TtsResult> SynthesizeAsync(
        string text, string language, string emotion,
        float[]? liveEmotionAudio = null, int liveEmotionSampleRate = 0)
        => Active.SynthesizeAsync(text, language, emotion, liveEmotionAudio, liveEmotionSampleRate);
    public Task PrewarmReferenceAudioAsync(int characterId) => Active.PrewarmReferenceAudioAsync(characterId);
    public Task StopAsync() => Active.StopAsync();
}