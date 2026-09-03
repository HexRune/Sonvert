using System.Threading.Tasks;
using Sonvert.App.Settings;

namespace Sonvert.App.Services.Tts;

/// <summary>
/// 对外暴露的 ITtsService 实现，内部按 AppSettings.TTSProvider 在
/// LocalTtsService 和"API 合成"之间转发；选了 API 之后，再按
/// AppSettings.TTSApiKind 在 ApiTtsService（占位/未实现的服务商）和
/// AzureTtsService（真正实现的 Azure 语音合成）之间二次选择——
/// 跟 TranslationRouter 是同样的两层路由设计。
/// </summary>
public class TtsRouter : ITtsService
{
    private readonly ISettingsService _settingsService;
    private readonly LocalTtsService _local;
    private readonly ApiTtsService _api;
    private readonly AzureTtsService _azure;

    public TtsRouter(
        ISettingsService settingsService,
        LocalTtsService local,
        ApiTtsService api,
        AzureTtsService azure)
    {
        _settingsService = settingsService;
        _local = local;
        _api = api;
        _azure = azure;
    }

    private ITtsService Active
    {
        get
        {
            var settings = _settingsService.Current;
            if (settings.TTSProvider != "api") return _local;

            return settings.TTSApiKind == "azure" ? _azure : _api;
        }
    }

    public Task StartAsync() => Active.StartAsync();

    public Task<TtsResult> SynthesizeAsync(string text, string language, string emotion)
        => Active.SynthesizeAsync(text, language, emotion);
    public Task PrewarmReferenceAudioAsync(int characterId) => Active.PrewarmReferenceAudioAsync(characterId);
    public Task StopAsync() => Active.StopAsync();
}