using System.Threading.Tasks;
using Sonvert.App.Settings;

namespace Sonvert.App.Services.Translation;

/// <summary>
/// 对外暴露的 ITranslationService 实现，内部按 AppSettings.TranslationProvider
/// 在 LocalTranslationService 和 ApiTranslationService 之间转发。
/// ViewModel 只注入这一个类型，完全不用关心切换逻辑。
///
/// 已知限制：如果用户在一次翻译会话运行期间临时切换设置，本地子进程
/// 可能还没被 StartAsync 拉起过——这个场景现在不用管，因为 api 分支
/// 目前调用即抛异常，等 API 真正实现后再补上"切换时自动 StartAsync
/// 目标实现"这个细节。
/// </summary>
public class TranslationRouter : ITranslationService
{
    private readonly ISettingsService _settingsService;
    private readonly LocalTranslationService _local;
    private readonly ApiTranslationService _api;

    public Task LoadModelAsync() => Active.LoadModelAsync();

    public TranslationRouter(
        ISettingsService settingsService,
        LocalTranslationService local,
        ApiTranslationService api)
    {
        _settingsService = settingsService;
        _local = local;
        _api = api;
    }

    private ITranslationService Active =>
        _settingsService.Current.TranslationProvider == "api" ? _api : _local;

    public Task StartAsync() => Active.StartAsync();

    public Task<TranslationResult> TranslateAsync(string text, string sourceLanguage, string targetLanguage)
        => Active.TranslateAsync(text, sourceLanguage, targetLanguage);

    public Task StopAsync() => Active.StopAsync();
}