using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Threading;
using Sonvert.App.Models;
using Sonvert.App.Services.Audio;
using Sonvert.App.Services.Characters;
using Sonvert.App.Services.Subtitle;
using Sonvert.App.Settings;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

namespace Sonvert.App.ViewModels;

public class LanguageOption
{
    public required string Code { get; init; }
    public required string DisplayName { get; init; }
}

/// <summary>翻译 API 服务商用的是哪种协议——决定 TranslationRouter 该把
/// 请求转发给 ApiTranslationService（OpenAI 兼容，DeepSeek/豆包这类）
/// 还是 AzureTranslationService（Azure Translator 专用协议）。</summary>
public enum TranslationApiKind
{
    OpenAiCompatible,
    Azure,
}

/// <summary>第三方翻译服务商的预设选项。下拉框选中某一项时，
/// 同时把 Model 和 Endpoint 一起写入设置——Home 页面追求"选完就能用"，
/// 不在这里单独放 Endpoint 输入框（那个留在"设置"页面给需要自定义
/// 网关地址的高级场景用，两边共享同一份 TranslationApiEndpoint 设置值，
/// 不会打架）。
///
/// 豆包(火山方舟)比较特殊：它的"模型"字段实际上填的是用户在火山方舟
/// 控制台开通模型后拿到的 Endpoint ID（形如 ep-xxxxxxxx-xxxxx），
/// 不是一个所有人通用的固定模型名，所以这里选中"豆包"预设时只自动填
/// Endpoint（API 网关地址是固定的），Model 留空，需要用户去"设置"页面
/// 手动填自己的 Endpoint ID。
///
/// Azure 翻译更特殊——它不是大模型，没有"模型"这个概念，ModelId 这个
/// 字段对它完全没意义（AzureTranslationService 根本不读这个字段），
/// 这里留空只是为了让 TranslationModelOption 这个类型对三种服务商都
/// 通用，不用为 Azure 单独搞一个不同结构的选项类。</summary>
public class TranslationModelOption
{
    public required string DisplayName { get; init; }
    public required string ModelId { get; init; }
    public required string Endpoint { get; init; }
    public required TranslationApiKind Kind { get; init; }
}

/// <summary>语音合成 API 服务商用的是哪种协议——目前只有 Azure 是真正
/// 实现了的，NotImplemented 对应"跳跃语音/火山引擎"这类占位选项，选中
/// 后 TtsRouter 会转发给 ApiTtsService（直接抛 NotImplementedException，
/// 提示尚未接入）。</summary>
public enum TtsApiKind
{
    NotImplemented,
    Azure,
}

/// <summary>语音合成第三方 API 的预设选项。
/// "跳跃语音/火山引擎"这两个仍然是纯占位（ApiTtsService 调用会直接抛
/// NotImplementedException），"Azure 语音合成"是真正实现了的——
/// 选中后额外需要区域+英文音色+中文音色+情绪跟随这几个 Azure 专属字段，
/// 见 HomeViewModel 里 IsAzureTtsSelected 的用法。</summary>
public class TtsModelOption
{
    public required string DisplayName { get; init; }
    public required string ModelId { get; init; }
    public required string Endpoint { get; init; }
    public required TtsApiKind Kind { get; init; }
}

/// <summary>Azure 语音合成的音色预设——英文/中文分开两份列表（见
/// EnglishVoiceOptions/ChineseVoiceOptions 的注释）。VoiceId 直接是
/// Azure 的音色名（如 "en-US-JennyNeural"），AzureTtsService 会从这个
/// 名字里解析出 SSML 需要的 locale，不需要在这里单独存一份。</summary>
public class TtsVoiceOption
{
    public required string DisplayName { get; init; }
    public required string VoiceId { get; init; }
}

/// <summary>
/// 首页——开播前最后确认一遍关键参数的地方，按"识别->翻译->合成"这条
/// 处理链路的顺序，分成三个板块。每个字段改了立刻写入 settings.json
/// （不等点保存按钮），因为这里的定位就是"随时调、随时生效"，跟"设置"
/// 页面那种"改完要点保存才生效"的定位不一样。
///
/// 视觉上按"调音台通道条"的思路组织：AUDIO IN(语音识别)/AUDIO OUT(语音
/// 合成) 放左列，TRANSLATE(翻译)/DISPLAY(字幕) 放右列，对应真实的
/// "音频处理"和"文本处理"两条并行链路。每张卡片标题旁的状态点绑定的是
/// 同一个 IsSessionRunning——因为当前架构里识别/翻译/合成是一个整体
/// 会话一起启停的，没有独立的单节点运行状态，四个点始终同步亮灭，
/// 如实反映"现在到底是不是在跑"，而不是为了好看伪造四个独立状态。
/// </summary>
public partial class HomeViewModel : ViewModelBase
{
    // 悬浮字幕
    private readonly ISubtitleWindowService _subtitleWindowService;

    [ObservableProperty]
    private bool _subtitleEnabled;
    private readonly ISettingsService _settingsService;
    private readonly ICharacterRepository _characterRepository;
    private readonly LiveTranslationViewModel _liveTranslationViewModel;

    // ---- 会话运行状态（驱动四张卡片标题旁的状态点）----
    // 直接转发 LiveTranslationViewModel.IsRunning，不在这里重复维护一份
    // 状态：那边才是真正管理识别/翻译/合成流水线生命周期的地方，这里只是
    // "观察者"。见构造函数里的订阅逻辑。
    [ObservableProperty]
    private bool _isSessionRunning;

    /// <summary>会话运行中要不要允许改设置——运行中把这些下拉框/开关/
    /// 标签页全部禁用，是"不允许中途改配置导致状态不一致"这个诉求的
    /// 实现方式。绑到 Avalonia 控件的 IsEnabled 上，禁用之后 Fluent
    /// 主题会自动把控件变灰、拦截交互，不需要每个控件单独处理"禁用时
    /// 长什么样"，也不需要额外的提示文案（按最终确认的方案，就是单纯
    /// 变灰，不加锁图标/文字说明）。"开始翻译/停止"这颗按钮本身不受这个
    /// 属性控制——不然运行起来之后按钮也被锁住，就没法点停止了。</summary>
    public bool CanEditSettings => !IsSessionRunning;

    // ---- 语音识别板块 ----
    [ObservableProperty] private string _recognitionLanguage;
    [ObservableProperty] private string _modelPrecision;

    public ObservableCollection<AudioInputDeviceOption> AudioInputDevices { get; } = new();
    public ObservableCollection<AudioOutputDeviceOption> AudioOutputDevices { get; } = new();

    [ObservableProperty]
    private AudioOutputDeviceOption? _selectedOutputDevice;

    [ObservableProperty]
    private AudioInputDeviceOption? _selectedAudioInputDevice;

    /// <summary>输入设备旁边那个迷你电平表的实时电平 [0,1]。
    /// 这路数据来自 _micLevelPreviewSource——一个专门为了"测个电平"而
    /// 单独起的轻量采集实例，跟真正翻译会话用的 IAudioInputSource
    /// （在 RecognitionSessionService 里）完全独立、互不影响：不管有没有
    /// 点"开始翻译"，只要选中了输入设备，这个迷你电平表就应该会动，
    /// 方便开播前先看一眼"这个设备是不是真的有声音"，这跟只有开始翻译
    /// 才会动的顶部横幅栏波形是两码事。
    /// 代价是如果翻译会话也在运行，会有两路采集同时打开同一个设备——
    /// 在 Windows 上麦克风通常允许多个客户端共享读取（WASAPI 共享模式），
    /// 多占的 CPU 开销可以忽略，用两条独立路径换来的代码简单性更值。</summary>
    [ObservableProperty]
    private double _micLevel;

    private IAudioInputSource? _micLevelPreviewSource;

    // ---- 麦克风迷你电平表：5 格 LED，位置固定配色（低位绿/中位黄/
    // 高位红），是否"点亮"由当前 MicLevel 是否超过该格阈值决定。
    // 顶部横幅栏的电平指示（LiveTranslationViewModel.IsLevelSegmentNOn）
    // 用的是同一套"位置固定配色、按阈值点亮"的设计，只是格数更多、
    // 尺寸更大，两处逻辑保持一致，方便以后一起调整。
    public bool IsMicSegment1On => MicLevel > 0.15;
    public bool IsMicSegment2On => MicLevel > 0.35;
    public bool IsMicSegment3On => MicLevel > 0.55;
    public bool IsMicSegment4On => MicLevel > 0.75;
    public bool IsMicSegment5On => MicLevel > 0.9;

    [ObservableProperty]
    private bool _enableTtsPlayback;

    public ObservableCollection<LanguageOption> RecognitionLanguageOptions { get; } = new()
    {
        new LanguageOption { Code = "auto", DisplayName = "自动" },
        new LanguageOption { Code = "zh", DisplayName = "中文" },
        new LanguageOption { Code = "en", DisplayName = "英文" },
    };

    public bool IsFp32Selected
    {
        get => ModelPrecision == "fp32";
        set { if (value) ModelPrecision = "fp32"; }
    }

    public bool IsInt8Selected
    {
        get => ModelPrecision == "int8";
        set { if (value) ModelPrecision = "int8"; }
    }

    // ---- 翻译板块 ----
    [ObservableProperty] private string _targetLanguage;
    [ObservableProperty] private bool _glossaryEnabled;
    [ObservableProperty] private string _translationProvider;
    [ObservableProperty] private string _translationApiKey;
    [ObservableProperty] private TranslationModelOption? _selectedTranslationModel;

    /// <summary>API Key 输入框当前是否是"明文可见"状态——纯 UI 临时状态，
    /// 不落盘到设置文件（每次重新打开程序都应该默认隐藏，不需要记住上次
    /// 是否点开过），所以这里没有对应的 AppSettings 字段。</summary>
    [ObservableProperty] private bool _isTranslationApiKeyVisible;

    /// <summary>本地模型（OPUS-MT）支持的目标语言——只有中/英，因为
    /// 现在部署的就是 opus-mt-zh-en / opus-mt-en-zh 这两个方向专用模型，
    /// 不是通用多语言模型，加新语言需要单独训练/导出新模型，不是配置项
    /// 能解决的，所以这里就不留"预留扩展"的空位了。</summary>
    public ObservableCollection<LanguageOption> TargetLanguageOptions { get; } = new()
    {
        new LanguageOption { Code = "zh", DisplayName = "中文" },
        new LanguageOption { Code = "en", DisplayName = "英文" },
    };

    /// <summary>第三方大模型翻译支持的目标语言——现在先跟本地一样只有
    /// 中/英（还没接入具体服务商，不知道该开放哪些），但特意用一个独立
    /// 的集合而不是复用 TargetLanguageOptions：大模型天然是多语言的，
    /// 以后真正验证某个服务商能稳定做好某个语言对（比如中译日）之后，
    /// 往这个集合里加一项就行，不会影响本地模型那边的下拉框选项
    /// （本地那边加了也用不了，两边的"能选什么"必须分开维护）。
    /// 两个下拉框虽然选项列表不同，但绑定的是同一个 TargetLanguage 设置
    /// 值——不管当前是本地还是 API 模式，"目标语言"在概念上始终只有一个，
    /// 只是"能选的范围"跟着 Provider 变。</summary>
    public ObservableCollection<LanguageOption> ApiTargetLanguageOptions { get; } = new()
    {
        new LanguageOption { Code = "zh", DisplayName = "中文" },
        new LanguageOption { Code = "en", DisplayName = "英文" },
    };

    /// <summary>翻译服务商预设——DeepSeek/豆包走 OpenAI 兼容协议，
    /// Azure 翻译走专用协议（见 AzureTranslationService），后续要加新
    /// 服务商时在这里追加一项即可，UI 和保存逻辑都不用改。</summary>
    public ObservableCollection<TranslationModelOption> TranslationModelOptions { get; } = new()
    {
        new TranslationModelOption
        {
            DisplayName = "DeepSeek",
            ModelId = "deepseek-chat",
            Endpoint = "https://api.deepseek.com/v1",
            Kind = TranslationApiKind.OpenAiCompatible,
        },
        new TranslationModelOption
        {
            DisplayName = "豆包（火山方舟）",
            ModelId = "", // 需要用户在"设置"页面填自己的 Endpoint ID，见类注释
            Endpoint = "https://ark.cn-beijing.volces.com/api/v3",
            Kind = TranslationApiKind.OpenAiCompatible,
        },
        new TranslationModelOption
        {
            DisplayName = "Azure 翻译",
            ModelId = "", // Azure 没有"模型"概念，这个字段对它没意义
            Endpoint = "https://api.cognitive.microsofttranslator.com",
            Kind = TranslationApiKind.Azure,
        },
    };

    /// <summary>Azure 翻译专属——区域（对应请求头 Ocp-Apim-Subscription-
    /// Region）。选填：官方文档说单服务全局资源不需要，多服务/区域性
    /// 资源才需要，具体取决于用户创建的是哪种 Azure 资源，客户端不做
    /// 强校验，留空就是不带这个请求头。</summary>
    [ObservableProperty] private string _translationApiRegion = string.Empty;

    /// <summary>常见 Azure 区域预设，给区域输入框的下拉建议列表用——
    /// 这个下拉框是 IsEditable 的（Avalonia ComboBox 支持直接输入自定义
    /// 文本，不局限于列表里的选项），因为 Azure 的区域列表会变、也不是
    /// 每个区域都值得预置，这里只放几个常见的做"一键选择"的快捷方式，
    /// 不在列表里的区域用户直接手打就行。</summary>
    public ObservableCollection<string> AzureRegionOptions { get; } = new()
    {
        "eastus", "eastus2", "westus", "westus2", "westeurope", "northeurope",
        "southeastasia", "eastasia", "japaneast", "japanwest", "koreacentral", "australiaeast", "uksouth",
    };

    /// <summary>当前选中的翻译服务商是不是 Azure 翻译——控制区域输入框
    /// 是否显示（DeepSeek/豆包不需要这个字段）。</summary>
    public bool IsAzureTranslationSelected => SelectedTranslationModel?.Kind == TranslationApiKind.Azure;

    public bool IsTranslationLocalSelected
    {
        get => TranslationProvider == "local";
        set { if (value) TranslationProvider = "local"; }
    }

    public bool IsTranslationApiSelected
    {
        get => TranslationProvider == "api";
        set { if (value) TranslationProvider = "api"; }
    }

    /// <summary>API Key 输入框的 PasswordChar：可见时用 '\0'（Avalonia 里
    /// 空字符表示"不遮盖，正常显示文本"），隐藏时用一个圆点字符遮盖。
    /// 用计算属性而不是 XAML 里接一个 bool->char 转换器，是延续这个文件里
    /// IsFp32Selected 那种"给 XAML 暴露一个已经算好的展示值"的一贯写法，
    /// 不需要为这一个场景单独建转换器类。</summary>
    public char TranslationApiKeyPasswordChar => IsTranslationApiKeyVisible ? '\0' : '●';

    /// <summary>显隐切换按钮上显示的图标字形，用 Segoe MDL2 Assets 字体
    /// （项目里窗口最小化/最大化/关闭按钮已经在用这套图标字体，见
    /// MainWindow.axaml）。E890=睁眼(View)，ED1A=闭眼(Hide)——
    /// 图标含义是"点了之后会变成什么状态"，所以当前隐藏时显示睁眼图标
    /// （提示"点我可以看到"），当前可见时显示闭眼图标（提示"点我可以藏起来"）。</summary>
    public string TranslationApiKeyToggleGlyph => IsTranslationApiKeyVisible ? "\uED1A" : "\uE890";

    // ---- 语音合成板块 ----
    [ObservableProperty] private Character? _selectedCharacter;
    [ObservableProperty] private string _ttsProvider;
    [ObservableProperty] private string _ttsApiKey;
    [ObservableProperty] private TtsModelOption? _selectedTtsModel;
    [ObservableProperty] private bool _isTtsApiKeyVisible;

    public ObservableCollection<Character> Characters { get; } = new();

    /// <summary>TTS API 服务商预设——"跳跃语音/火山引擎"仍然是占位
    /// （ApiTtsService 调用会抛 NotImplementedException），"Azure 语音
    /// 合成"是真正实现了的（AzureTtsService）。</summary>
    public ObservableCollection<TtsModelOption> TtsModelOptions { get; } = new()
    {
        new TtsModelOption { DisplayName = "跳跃语音（待接入）", ModelId = "", Endpoint = "", Kind = TtsApiKind.NotImplemented },
        new TtsModelOption { DisplayName = "火山引擎语音合成（待接入）", ModelId = "", Endpoint = "", Kind = TtsApiKind.NotImplemented },
        new TtsModelOption { DisplayName = "Azure 语音合成", ModelId = "", Endpoint = "", Kind = TtsApiKind.Azure },
    };

    /// <summary>当前选中的语音合成服务商是不是 Azure——控制区域/音色/
    /// 情绪跟随这几个 Azure 专属字段是否显示。</summary>
    public bool IsAzureTtsSelected => SelectedTtsModel?.Kind == TtsApiKind.Azure;

    /// <summary>Azure 语音合成专属——区域，跟翻译那边不一样，这个是
    /// 必填的（直接拼进请求地址），所以下拉框做成不可编辑、只能从预置
    /// 列表里选，避免手滑输错导致请求直接失败。直接复用上面翻译区域
    /// 用的 AzureRegionOptions 集合，两边内容完全一样，不需要重复维护
    /// 一份。</summary>
    [ObservableProperty] private string _ttsApiRegion = string.Empty;

    /// <summary>Azure 语音合成专属——是否按识别到的情绪调整朗读语气。
    /// 默认关闭：第一次接入的用户如果没注意到这个开关，听到的应该是
    /// 平常的默认语气朗读，而不是意外地被加上风格化的语气。</summary>
    [ObservableProperty] private bool _ttsEmotionFollowEnabled;

    [ObservableProperty] private TtsVoiceOption? _selectedEnglishVoice;
    [ObservableProperty] private TtsVoiceOption? _selectedChineseVoice;

    /// <summary>英文音色预设——现在只放了一个 Jenny（Azure 官方风格库
    /// 最丰富的英文音色之一，配合"情绪跟随"效果最好），列表结构上是
    /// 为了以后好加新选项预留的：往这个集合里追加一项
    /// new TtsVoiceOption{...}，下拉框和保存逻辑完全不用改。</summary>
    public ObservableCollection<TtsVoiceOption> EnglishVoiceOptions { get; } = new()
    {
        new TtsVoiceOption { DisplayName = "Jenny（女声，情绪丰富）", VoiceId = "en-US-JennyNeural" },
    };

    /// <summary>中文音色预设——晓晓是 Azure 目前风格库最丰富的中文
    /// 音色，覆盖 SenseVoice 能识别的绝大部分情绪。同样为以后扩展留了
    /// 结构空间。</summary>
    public ObservableCollection<TtsVoiceOption> ChineseVoiceOptions { get; } = new()
    {
        new TtsVoiceOption { DisplayName = "晓晓 Xiaoxiao（女声，情绪最丰富）", VoiceId = "zh-CN-XiaoxiaoNeural" },
    };

    public bool IsTtsLocalSelected
    {
        get => TtsProvider == "local";
        set { if (value) TtsProvider = "local"; }
    }

    public bool IsTtsApiSelected
    {
        get => TtsProvider == "api";
        set { if (value) TtsProvider = "api"; }
    }

    public char TtsApiKeyPasswordChar => IsTtsApiKeyVisible ? '\0' : '●';

    public string TtsApiKeyToggleGlyph => IsTtsApiKeyVisible ? "\uED1A" : "\uE890";

    public event EventHandler? StartTranslationRequested;

    public HomeViewModel(ISettingsService settingsService, 
        ICharacterRepository characterRepository,
        ISubtitleWindowService subtitleWindowService,
        LiveTranslationViewModel liveTranslationViewModel)
    {
        _settingsService = settingsService;
        _characterRepository = characterRepository;
        _subtitleWindowService = subtitleWindowService;
        _liveTranslationViewModel = liveTranslationViewModel;

        _subtitleEnabled = settingsService.Current.SubtitleEnabled;

        var s = settingsService.Current;
        _recognitionLanguage = s.RecognitionLanguage;
        _modelPrecision = s.ModelPrecision;
        _targetLanguage = s.TargetLanguage;
        _glossaryEnabled = s.GlossaryEnabled;
        _translationProvider = s.TranslationProvider;
        _translationApiKey = s.TranslationApiKey;
        _translationApiRegion = s.TranslationApiRegion;
        _ttsProvider = s.TTSProvider;
        _ttsApiKey = s.TTSApiKey;
        _ttsApiRegion = s.TTSApiRegion;
        _ttsEmotionFollowEnabled = s.TTSEmotionFollowEnabled;
        _enableTtsPlayback = s.EnableTtsPlayback;

        // 根据已保存的 TranslationApiModel 反查是哪个预设——如果用户是在
        // "设置"页面手填的自定义模型名（不匹配任何预设），这里就选不中
        // 任何一项，下拉框显示为空，这是预期行为（说明当前用的是一个
        // Home 页面下拉框里没有的自定义配置，不应该强行归到某个预设上）。
        //
        // 匹配逻辑改成按 Kind + Endpoint 一起判断，不再单独看 ModelId
        // 是否非空——Azure 翻译这个预设的 ModelId 永远是空字符串（它
        // 没有"模型"概念），如果还按"ModelId 非空才算命中"的老逻辑，
        // Azure 会被误判成"未匹配任何预设"，下拉框显示为空，即使用户
        // 明明选的就是 Azure。改成：先按 TranslationApiKind 缩小范围
        // （openai_compatible 下还得看 ModelId 非空且匹配，因为这个
        // 协议下"豆包"这种模型留空的自定义配置也要能被"未匹配"正确
        // 识别；azure 下只看 Endpoint 是否对得上，因为 Azure 预设里
        // 只有这一项）。
        var savedKind = s.TranslationApiKind == "azure" ? TranslationApiKind.Azure : TranslationApiKind.OpenAiCompatible;
        _selectedTranslationModel = savedKind == TranslationApiKind.Azure
            ? TranslationModelOptions.FirstOrDefault(m => m.Kind == TranslationApiKind.Azure)
            : TranslationModelOptions.FirstOrDefault(m =>
                m.Kind == TranslationApiKind.OpenAiCompatible && m.ModelId == s.TranslationApiModel && m.ModelId != "");
        _selectedTtsModel = s.TTSApiKind == "azure"
            ? TtsModelOptions.FirstOrDefault(m => m.Kind == TtsApiKind.Azure)
            : TtsModelOptions.FirstOrDefault(m => m.Kind == TtsApiKind.NotImplemented && m.ModelId == s.TTSApiModel && m.ModelId != "");
        _selectedEnglishVoice = EnglishVoiceOptions.FirstOrDefault(v => v.VoiceId == s.TTSApiVoiceEn);
        _selectedChineseVoice = ChineseVoiceOptions.FirstOrDefault(v => v.VoiceId == s.TTSApiVoiceZh);

        // 会话运行状态：初始值取当前实际状态，之后通过 PropertyChanged
        // 订阅保持同步——不能只订阅不取初始值，否则如果 Home 页面是在
        // 会话已经在运行时才第一次被打开/重建，状态点会错误地显示"未运行"。
        _isSessionRunning = _liveTranslationViewModel.IsRunning;
        _liveTranslationViewModel.PropertyChanged += OnLiveTranslationViewModelPropertyChanged;

        foreach (var device in Sonvert.App.Services.Audio.AudioInputDeviceEnumerator.GetAllOptions())
        {
            AudioInputDevices.Add(device);
        }
        foreach (var device in AudioOutputDeviceEnumerator.GetAllOptions())
        {
            AudioOutputDevices.Add(device);
        }
        _selectedOutputDevice = AudioOutputDevices.FirstOrDefault(d => d.Id == s.OutputDeviceId);

        // 按存储的设置值，找到对应的那个设备对象重新选中——设置里存的是
        // Kind+Id 这两个字符串，界面上要匹配回具体的 AudioInputDeviceOption
        // 实例，两者不能直接比较相等（不是同一个对象引用）。
        _selectedAudioInputDevice = AudioInputDevices.FirstOrDefault(d =>
            d.Kind.ToString() == s.InputDeviceKind && d.Id == s.InputDeviceId);

        // 首页一打开（不管有没有开始翻译）就把迷你电平表跑起来，
        // 只要选中的设备存在——见 MicLevel 属性注释里的设计原因。
        if (_selectedAudioInputDevice is not null)
        {
            StartMicLevelPreview(_selectedAudioInputDevice);
        }

        _ = RefreshCharactersAsync();
    }

    /// <summary>启动（或者切换设备时重启）麦克风迷你电平表专用的那路
    /// 轻量采集。先释放旧的再开新的——不能不释放就换，NAudio 的采集对象
    /// 独占底层设备句柄，同一个进程里对同一个设备开两个 WaveInEvent
    /// 会互相干扰。</summary>
    private void StartMicLevelPreview(AudioInputDeviceOption device)
    {
        _micLevelPreviewSource?.Dispose();

        try
        {
            _micLevelPreviewSource = device.Kind == AudioInputDeviceKind.Loopback
                ? CreateLoopbackPreviewSource(device.Id)
                : CreateMicrophonePreviewSource(device.Id);
        }
        catch (Exception)
        {
            // 设备可能已经被拔掉/被其他程序独占——迷你电平表本来就是个
            // "锦上添花"的辅助功能，拿不到这路数据就静默放弃，不应该
            // 因为这个原因影响首页其他功能正常使用。
            _micLevelPreviewSource = null;
            return;
        }

        _micLevelPreviewSource.DataAvailable += OnMicLevelPreviewDataAvailable;
        _micLevelPreviewSource.Start();
    }

    private static IAudioInputSource CreateLoopbackPreviewSource(string deviceId)
    {
        using var deviceEnumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator();
        var mmDevice = deviceEnumerator.GetDevice(deviceId);
        return new LoopbackInputSource(mmDevice);
    }

    private static IAudioInputSource CreateMicrophonePreviewSource(string deviceId)
    {
        var deviceNumber = int.TryParse(deviceId, out var idx) ? idx : -1;
        return new MicrophoneInputSource(deviceNumber);
    }

    /// <summary>迷你电平表的数据回调——在 NAudio 的采集线程上触发，
    /// 必须切回 UI 线程才能安全更新绑定属性，跟 LiveTranslationViewModel
    /// 里 OnLevelChanged 的处理方式一致。</summary>
    private void OnMicLevelPreviewDataAvailable(object? sender, float[] samples)
    {
        var level = AudioLevelCalculator.CalculateLevel(samples);
        Dispatcher.UIThread.Post(() => MicLevel = level);
    }

    partial void OnMicLevelChanged(double value)
    {
        OnPropertyChanged(nameof(IsMicSegment1On));
        OnPropertyChanged(nameof(IsMicSegment2On));
        OnPropertyChanged(nameof(IsMicSegment3On));
        OnPropertyChanged(nameof(IsMicSegment4On));
        OnPropertyChanged(nameof(IsMicSegment5On));
    }

    private void OnLiveTranslationViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LiveTranslationViewModel.IsRunning))
        {
            IsSessionRunning = _liveTranslationViewModel.IsRunning;
        }
    }

    partial void OnIsSessionRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(CanEditSettings));
    }

    partial void OnSubtitleEnabledChanged(bool value)
    {
        _settingsService.Current.SubtitleEnabled = value;
        _ = _settingsService.SaveAsync();

        if (value) _subtitleWindowService.Show();
        else _subtitleWindowService.Hide();
    }

    [RelayCommand]
    private void UnlockSubtitle()
    {
        _subtitleWindowService.Unlock();
    }

    partial void OnSelectedOutputDeviceChanged(AudioOutputDeviceOption? value)
    {
        if (value is null) return;
        _settingsService.Current.OutputDeviceId = value.Id;
        _ = _settingsService.SaveAsync();
    }

    partial void OnSelectedAudioInputDeviceChanged(AudioInputDeviceOption? value)
    {
        if (value is null) return;
        _settingsService.Current.InputDeviceKind = value.Kind.ToString();
        _settingsService.Current.InputDeviceId = value.Id;
        _ = _settingsService.SaveAsync();

        // 换了输入设备，迷你电平表也要跟着换成新设备的电平，不然会
        // 一直显示上一个设备的响度，误导用户以为新选的设备没声音。
        StartMicLevelPreview(value);
    }

    partial void OnEnableTtsPlaybackChanged(bool value)
    {
        _settingsService.Current.EnableTtsPlayback = value;
        _ = _settingsService.SaveAsync();
    }

    public async Task RefreshCharactersAsync()
    {
        var currentActiveId = _settingsService.Current.ActiveCharacterId;

        Characters.Clear();
        foreach (var character in await _characterRepository.GetAllAsync())
        {
            Characters.Add(character);
        }

        SelectedCharacter = Characters.FirstOrDefault(c => c.Id == currentActiveId);
    }

    partial void OnRecognitionLanguageChanged(string value)
    {
        _settingsService.Current.RecognitionLanguage = value;
        _ = _settingsService.SaveAsync();
    }

    partial void OnModelPrecisionChanged(string value)
    {
        _settingsService.Current.ModelPrecision = value;
        _ = _settingsService.SaveAsync();
        OnPropertyChanged(nameof(IsFp32Selected));
        OnPropertyChanged(nameof(IsInt8Selected));
    }

    partial void OnTargetLanguageChanged(string value)
    {
        _settingsService.Current.TargetLanguage = value;
        _ = _settingsService.SaveAsync();
    }

    partial void OnGlossaryEnabledChanged(bool value)
    {
        _settingsService.Current.GlossaryEnabled = value;
        _ = _settingsService.SaveAsync();
    }

    partial void OnTranslationProviderChanged(string value)
    {
        _settingsService.Current.TranslationProvider = value;
        _ = _settingsService.SaveAsync();
        OnPropertyChanged(nameof(IsTranslationLocalSelected));
        OnPropertyChanged(nameof(IsTranslationApiSelected));
    }

    partial void OnTranslationApiKeyChanged(string value)
    {
        _settingsService.Current.TranslationApiKey = value;
        _ = _settingsService.SaveAsync();
    }

    partial void OnIsTranslationApiKeyVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(TranslationApiKeyPasswordChar));
        OnPropertyChanged(nameof(TranslationApiKeyToggleGlyph));
    }

    [RelayCommand]
    private void ToggleTranslationApiKeyVisibility()
    {
        IsTranslationApiKeyVisible = !IsTranslationApiKeyVisible;
    }

    /// <summary>选中某个翻译模型预设时，把 ModelId 和 Endpoint 一起写入
    /// 设置——两者是配套的，不能只存其中一个（比如只存了 Endpoint 但
    /// Model 还是上一个服务商的，请求会直接失败）。</summary>
    partial void OnSelectedTranslationModelChanged(TranslationModelOption? value)
    {
        if (value is null) return;
        _settingsService.Current.TranslationApiModel = value.ModelId;
        _settingsService.Current.TranslationApiEndpoint = value.Endpoint;
        _settingsService.Current.TranslationApiKind =
            value.Kind == TranslationApiKind.Azure ? "azure" : "openai_compatible";
        _ = _settingsService.SaveAsync();
        OnPropertyChanged(nameof(IsAzureTranslationSelected));
    }

    partial void OnTranslationApiRegionChanged(string value)
    {
        _settingsService.Current.TranslationApiRegion = value;
        _ = _settingsService.SaveAsync();
    }

    partial void OnTtsProviderChanged(string value)
    {
        _settingsService.Current.TTSProvider = value;
        _ = _settingsService.SaveAsync();
        OnPropertyChanged(nameof(IsTtsLocalSelected));
        OnPropertyChanged(nameof(IsTtsApiSelected));
    }

    partial void OnTtsApiKeyChanged(string value)
    {
        _settingsService.Current.TTSApiKey = value;
        _ = _settingsService.SaveAsync();
    }

    partial void OnTtsApiRegionChanged(string value)
    {
        _settingsService.Current.TTSApiRegion = value;
        _ = _settingsService.SaveAsync();
    }

    partial void OnTtsEmotionFollowEnabledChanged(bool value)
    {
        _settingsService.Current.TTSEmotionFollowEnabled = value;
        _ = _settingsService.SaveAsync();
    }

    partial void OnSelectedEnglishVoiceChanged(TtsVoiceOption? value)
    {
        if (value is null) return;
        _settingsService.Current.TTSApiVoiceEn = value.VoiceId;
        _ = _settingsService.SaveAsync();
    }

    partial void OnSelectedChineseVoiceChanged(TtsVoiceOption? value)
    {
        if (value is null) return;
        _settingsService.Current.TTSApiVoiceZh = value.VoiceId;
        _ = _settingsService.SaveAsync();
    }

    partial void OnIsTtsApiKeyVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(TtsApiKeyPasswordChar));
        OnPropertyChanged(nameof(TtsApiKeyToggleGlyph));
    }

    [RelayCommand]
    private void ToggleTtsApiKeyVisibility()
    {
        IsTtsApiKeyVisible = !IsTtsApiKeyVisible;
    }

    partial void OnSelectedTtsModelChanged(TtsModelOption? value)
    {
        if (value is null) return;
        _settingsService.Current.TTSApiModel = value.ModelId;
        _settingsService.Current.TTSApiKind = value.Kind == TtsApiKind.Azure ? "azure" : string.Empty;
        // Endpoint 暂时不写：Azure 的地址是"区域+固定域名"拼出来的，
        // 不是一个固定 Endpoint 字符串；占位选项（跳跃语音/火山引擎）
        // 也还没有真实地址可填，等真正确定第二个可用服务商时再决定
        // 要不要给 Endpoint 这个字段派上用场。
        _ = _settingsService.SaveAsync();
        OnPropertyChanged(nameof(IsAzureTtsSelected));
    }

    partial void OnSelectedCharacterChanged(Character? value)
    {
        _settingsService.Current.ActiveCharacterId = value?.Id;
        _ = _settingsService.SaveAsync();
    }

    [RelayCommand]
    private void StartTranslation()
    {
        StartTranslationRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>主页"开始翻译"按钮在运行中会变成红色"停止"，点击这个
    /// 直接调用 LiveTranslationViewModel 的停止逻辑——不像开始那样要
    /// 经过 MainViewModel 转发再跳转页面，因为停止不需要做任何页面
    /// 跳转，留在当前页面直接停就行（说不定你就是在首页点的停止）。</summary>
    [RelayCommand]
    private async Task StopTranslationAsync()
    {
        await _liveTranslationViewModel.StopCommand.ExecuteAsync(null);
    }
}
