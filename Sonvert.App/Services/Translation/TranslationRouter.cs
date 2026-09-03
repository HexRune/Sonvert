using System.Threading.Tasks;
using Sonvert.App.Settings;

namespace Sonvert.App.Services.Translation;

/// <summary>
/// 对外暴露的 ITranslationService 实现，内部按 AppSettings.TranslationProvider
/// 在 LocalTranslationService 和"API 翻译"之间转发；选了 API 之后，
/// 再按 AppSettings.TranslationApiKind 在 ApiTranslationService（DeepSeek/
/// 豆包这类 OpenAI 兼容协议）和 AzureTranslationService（Azure Translator
/// 专用协议）之间二次选择——这两个协议差异太大（请求体结构、鉴权方式
/// 都不一样），没有共用同一个实现类，所以路由要分两层。
/// ViewModel 只注入这一个类型，完全不用关心这两层切换逻辑。
/// </summary>
public class TranslationRouter : ITranslationService
{
    private readonly ISettingsService _settingsService;
    private readonly LocalTranslationService _local;
    private readonly ApiTranslationService _api;
    private readonly AzureTranslationService _azure;

    public Task LoadModelAsync() => Active.LoadModelAsync();

    public TranslationRouter(
        ISettingsService settingsService,
        LocalTranslationService local,
        ApiTranslationService api,
        AzureTranslationService azure)
    {
        _settingsService = settingsService;
        _local = local;
        _api = api;
        _azure = azure;
    }

    private ITranslationService Active
    {
        get
        {
            var settings = _settingsService.Current;
            if (settings.TranslationProvider != "api") return _local;

            return settings.TranslationApiKind == "azure" ? _azure : _api;
        }
    }

    public Task StartAsync() => Active.StartAsync();

    public Task<TranslationResult> TranslateAsync(string text, string sourceLanguage, string targetLanguage)
        => Active.TranslateAsync(text, sourceLanguage, targetLanguage);

    public Task StopAsync() => Active.StopAsync();
}