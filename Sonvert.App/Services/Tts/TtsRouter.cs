using System.Threading.Tasks;
using Sonvert.App.Settings;

namespace Sonvert.App.Services.Tts;

public class TtsRouter : ITtsService
{
    private readonly ISettingsService _settingsService;
    private readonly LocalTtsService _local;
    private readonly ApiTtsService _api;

    public TtsRouter(ISettingsService settingsService, LocalTtsService local, ApiTtsService api)
    {
        _settingsService = settingsService;
        _local = local;
        _api = api;
    }

    private ITtsService Active =>
        _settingsService.Current.TTSProvider == "api" ? _api : _local;

    public Task StartAsync() => Active.StartAsync();

    public Task<TtsResult> SynthesizeAsync(string text, string language, string emotion)
        => Active.SynthesizeAsync(text, language, emotion);
    public Task PrewarmReferenceAudioAsync(int characterId) => Active.PrewarmReferenceAudioAsync(characterId);
    public Task StopAsync() => Active.StopAsync();
}