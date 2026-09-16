using System;
using System.Collections.Generic;
using System.IO;

namespace Sonvert.App.Settings;

/// <summary>
/// 程序设置。跟"历史记录"这类用户数据不同，这些是纯配置项，
/// 整体读写一份 JSON 文件即可，不需要用数据库。角色/情绪录音相关的数据
/// 已经挪到 SQLite 数据库里（见 Data/AppDbContext.cs），这里只保留
/// "当前选中哪个角色"这个引用（ActiveCharacterId）。
/// </summary>
public class AppSettings
{
    /// <summary>历史记录自动清理的保留天数——null 或 0 表示不开启这个功能，
    /// 永久保留所有历史记录。程序启动时由 HistoryRetentionCleaner 读取
    /// 这个值，删掉超期的记录。</summary>
    public int? HistoryRetentionDays { get; set; } = null;

    /// <summary>是否启用翻译术语表——关闭后，翻译前不再做 GlossaryReplacer
    /// 那步替换，即使术语表里配置了内容，也完全不生效，方便临时关闭这个
    /// 功能而不用清空整个术语表。</summary>
    public bool GlossaryEnabled { get; set; } = true;

    /// <summary>是否要把翻译结果合成语音并播放——关闭后，识别和翻译照常
    /// 进行（字幕/文字记录不受影响），只是跳过 TTS 合成和播放这两步。
    /// 用于"翻译游戏/电影字幕，只看不听"这类场景，避免合成语音和原始
    /// 音轨叠在一起变得嘈杂。</summary>
    public bool EnableTtsPlayback { get; set; } = true;

    // ---- 悬浮字幕 ----

    /// <summary>字幕功能总开关——跟"语音播报"完全独立，可以任意组合开关。</summary>
    public bool SubtitleEnabled { get; set; } = false;

    /// <summary>字幕内容模式：true 显示原文+译文两行，false 只显示译文。</summary>
    public bool SubtitleShowSourceText { get; set; } = true;

    public double SubtitleFontSize { get; set; } = 20;

    /// <summary>文字颜色，十六进制字符串（如 "#FFFFFF"）。</summary>
    public string SubtitleTextColor { get; set; } = "#FFFFFF";

    /// <summary>背景不透明度，0（完全透明）到 1（完全不透明）之间。</summary>
    public double SubtitleBackgroundOpacity { get; set; } = 0.7;

    /// <summary>
    /// 悬浮窗口上次的位置和大小——用户拖动调整后记住，下次打开沿用。
    /// 都是可空的：首次使用时是 null，由 SubtitleWindowService 决定一个
    /// 默认位置（屏幕底部居中），不需要在这里预先算好。
    /// </summary>
    public double? SubtitleWindowX { get; set; }
    public double? SubtitleWindowY { get; set; }
    public double SubtitleWindowWidth { get; set; } = 640;
    public double SubtitleWindowHeight { get; set; } = 140;

    // ---- SenseVoiceService ----
    public int SenseVoicePort { get; set; } = 8878;

    public string ModelPrecision { get; set; } = "fp32";

    public string SenseVoiceExecutablePath { get; set; } = DefaultDevExecutablePath();
    public string SenseVoiceArguments { get; set; } = "main.py";
    public string SenseVoiceWorkingDirectory { get; set; } = DefaultDevWorkingDirectory();

    public string VadModelPath { get; set; } = string.Empty;

    /// <summary>SenseVoice 模型资源目录（am.mvn / embedding.npy / *.onnx /
    /// *.bpe.model 所在的文件夹）。开发期默认指向 Sonvert.SenseVoiceService
    /// 项目下的 models 文件夹（跟以前手动跑 python main.py 时的相对路径
    /// "models" 是同一个地方）；打包后应该改成安装目录下统一的模型文件夹
    /// （比如 {安装目录}\models\sensevoice），跟 SenseVoiceWorkingDirectory
    /// 分开配置，这样换插件化的独立 exe 时不需要把模型文件也塞进
    /// PyInstaller 的产物目录里，模型更新不用重新打包整个服务。</summary>
    public string SenseVoiceModelsDirectory { get; set; } = DefaultDevModelsDirectory();
    /// <summary>选中的音频输入设备种类。</summary>
    public string InputDeviceKind { get; set; } = "Microphone"; // 曾经还有 "Loopback"，已删除（见 AudioInputDeviceOption.cs 顶部注释），现在恒为 "Microphone"

    /// <summary>音频输入设备的 Id。"-1" 是一个特殊值，对应 Windows 的
    /// "跟随系统默认录音设备"这个约定，不是某个固定设备——用户以后在系统
    /// 设置里换了默认麦克风，这边不用重新选择就会自动跟着变。这也是默认值，
    /// 保证不用手动选设备也能直接用。</summary>
    public string InputDeviceId { get; set; } = "-1";

    // ---- 输入二（第二路独立麦克风类输入，比如 MixLine 虚拟麦克风）----
    // 场景：游戏内翻译——输入一是物理麦克风（自己说话 -> 英文，读给队友
    // 听），输入二是游戏语音路由过来的虚拟麦克风（队友说的英文 -> 中文，
    // 只出字幕不合成语音，理由见 EnableSecondInputSource 的注释）。

    /// <summary>是否启用输入二。默认关闭——这是个进阶功能，大多数用户
    /// 只需要输入一那一路，不应该默认就多起一路识别流水线。</summary>
    public bool EnableSecondInputSource { get; set; } = false;

    /// <summary>输入二的音频设备 Id，跟 InputDeviceId 是同一套设备体系
    /// （都是 AudioInputDeviceEnumerator 列出来的麦克风类设备），只是
    /// 各自独立选择，不共用同一个值。</summary>
    public string InputDeviceId2 { get; set; } = "-1";

    /// <summary>输入二的目标语言，独立于 TargetLanguage——你的典型场景
    /// 里两路方向是相反的（输入一 中->英，输入二 英->中）。</summary>
    public string TargetLanguage2 { get; set; } = "zh";

    /// <summary>输入二的识别语言，独立于 RecognitionLanguage——这是修复
    /// "接入第二路输入后英文被误识别成中文"这个问题的关键字段。SenseVoice
    /// 的 /recognize 接口本来就是每次请求带 language 参数（见
    /// SenseVoiceService.py 注释），不是加载模型时固定死的，之前的 bug
    /// 只是 C# 侧两路会话都读了同一个 RecognitionLanguage，没有独立开出
    /// 第二个字段。默认给 "en"——对应 TargetLanguage2 默认 "zh" 隐含的
    /// 典型场景（输入二是队友说英文，翻成中文字幕），跟 RecognitionLanguage
    /// 默认 "auto" 不一样，是因为两路场景不对称，没有一个通用的默认值。</summary>
    public string RecognitionLanguage2 { get; set; } = "en";

    /// <summary>字幕面板显示哪一路的结果——"All"/"Source1"/"Source2"。
    /// 只在 EnableSecondInputSource 为 true 时才有意义，两路都开着的
    /// 时候，字幕混在一起容易看花，给个筛选。</summary>
    public string SubtitleDisplayFilter { get; set; } = "All";

    /// <summary>悬浮字幕窗口单独一份筛选设置，跟上面 SubtitleDisplayFilter
    /// （主界面"实时翻译"页面里的筛选，运行中随时可切）是两回事——悬浮
    /// 窗口是给你在游戏里直接看的，可能跟主界面想看的不是同一路（比如
    /// 主界面开着"全部"方便自己复盘，悬浮窗口只想看"仅输入二"，界面
    /// 不会被输入一自己说的话刷屏）。这个必须在开始翻译前配置好，
    /// 运行中不支持随时切换（悬浮窗口本身没有筛选按钮，保持界面干净）。</summary>
    public string SubtitleWindowSourceFilter { get; set; } = "All";

    /// <summary>音频输出设备的 Id——WASAPI 的 MMDevice.ID（一长串 GUID 格式
    /// 字符串）。空字符串表示"跟随系统默认播放设备"，是默认值。</summary>
    public string OutputDeviceId { get; set; } = string.Empty;
    public string RecognitionLanguage { get; set; } = "auto";

    private static string DefaultDevWorkingDirectory() => Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\Sonvert.SenseVoiceService"));

    private static string DefaultDevExecutablePath() =>
        Path.Combine(DefaultDevWorkingDirectory(), @"env\Scripts\python.exe");

    private static string DefaultDevModelsDirectory() =>
        Path.Combine(DefaultDevWorkingDirectory(), "models");

    // ---- MTService ----
    /// <summary>
    /// 目标语言——识别出的语言如果不是这个值，就翻译成这个值；
    /// 识别出的语言如果正好是这个值，翻译成另一种（目前只支持 zh<->en
    /// 这一对，以后要支持更多语言时，这里的判断逻辑要跟着扩展）。
    /// </summary>
    public string TargetLanguage { get; set; } = "en";
    public int MTPort { get; set; } = 8879;
    public string MTExecutablePath { get; set; } = DefaultDevMTExecutablePath();
    public string MTArguments { get; set; } = "main.py";
    public string MTWorkingDirectory { get; set; } = DefaultDevMTWorkingDirectory();

    public string TranslationProvider { get; set; } = "local";
    public string TranslationApiEndpoint { get; set; } = string.Empty;
    public string TranslationApiKey { get; set; } = string.Empty;
    public string TranslationApiModel { get; set; } = string.Empty;

    /// <summary>标识当前选中的 API 翻译服务走哪种协议——"openai_compatible"
    /// （DeepSeek/豆包这类走 Chat Completions 协议的大模型）或者
    /// "azure"（Azure Translator，专用翻译协议，完全不同的请求/响应
    /// 格式）。TranslationRouter 靠这个字段在 ApiTranslationService 和
    /// AzureTranslationService 之间再选一次（TranslationProvider 只能
    /// 区分"本地/API"这一层，选了 API 之后具体走哪个协议由这个字段决定）。
    /// 用户不需要直接接触这个字段，选翻译服务商下拉框时跟着一起写入。</summary>
    public string TranslationApiKind { get; set; } = "openai_compatible";

    /// <summary>Azure Translator 专属——对应请求头 Ocp-Apim-Subscription-Region。
    /// 官方文档说单服务全局资源不需要这个，但多服务/区域性资源必须带，
    /// 具体要不要填取决于用户创建的是哪种资源，所以做成选填，不做强校验
    /// ——留空就不加这个请求头，真要漏填了 Azure 会返回鉴权失败，
    /// 错误信息里能看出来，不需要在客户端提前拦。</summary>
    public string TranslationApiRegion { get; set; } = string.Empty;

    private static string DefaultDevMTWorkingDirectory() => Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\Sonvert.MTService"));

    private static string DefaultDevMTExecutablePath() =>
        Path.Combine(DefaultDevMTWorkingDirectory(), @"env\Scripts\python.exe");

    // ---- TTSService ----
    public int TTSPort { get; set; } = 9880;
    public string TTSExecutablePath { get; set; } = string.Empty;
    public string TTSArguments { get; set; } =
        "api_v2.py -a 127.0.0.1 -p 9880 -c GPT_SoVITS/configs/tts_infer.yaml";
    public string TTSWorkingDirectory { get; set; } = string.Empty;
    public string TTSReferenceAudioLanguage { get; set; } = "zh";

    public string TTSProvider { get; set; } = "local";
    public string TTSApiEndpoint { get; set; } = string.Empty;
    public string TTSApiKey { get; set; } = string.Empty;
    public string TTSApiModel { get; set; } = string.Empty;

    /// <summary>标识当前选中的 API 语音合成服务走哪种协议——目前只有
    /// "azure"这一个真正实现了；其余占位选项（跳跃语音/火山引擎）选中时
    /// 这个字段还是空字符串，TtsRouter 遇到空值会转发给 ApiTtsService
    /// （它对这些占位选项直接抛 NotImplementedException，见该类注释）。
    /// 跟 TranslationApiKind 是同样的设计目的：一个"API"大类下可能有
    /// 协议完全不同的具体实现，需要再细分一层路由。</summary>
    public string TTSApiKind { get; set; } = string.Empty;

    /// <summary>Azure 语音合成专属——区域，直接拼进请求地址
    /// （https://{region}.tts.speech.microsoft.com/...），必填，不像
    /// 翻译那边的区域是选填的。</summary>
    public string TTSApiRegion { get; set; } = string.Empty;

    /// <summary>Azure 语音合成专属——英文/中文各自选中的音色 ID
    /// （比如 "en-US-JennyNeural"）。分两个字段而不是一个，是因为
    /// SynthesizeAsync 的 language 参数决定了这一句该用哪种语言朗读，
    /// 需要各自配置好对应语言的音色，不用每次翻译方向变了手动切换。</summary>
    public string TTSApiVoiceEn { get; set; } = string.Empty;
    public string TTSApiVoiceZh { get; set; } = string.Empty;

    /// <summary>Azure 语音合成专属——是否按 SenseVoice 识别到的情绪调整
    /// 朗读语气（SSML mstts:express-as style）。关闭时统一用默认语气，
    /// 不传 style 标签。默认关闭，避免用户第一次接入时因为不了解这个
    /// 功能突然听到风格化的朗读感到意外。</summary>
    public bool TTSEmotionFollowEnabled { get; set; } = false;

    // ---- IndexTTS（本地 TTS 引擎的第二个选项，跟 GPT-SoVITS 并存）----

    /// <summary>本地 TTS 走哪个具体引擎——"gpt-sovits"（默认，沿用现有）或
    /// "indextts"。只在 TTSProvider == "local" 时有意义；TtsRouter 在
    /// local 分支里再按这个字段二次路由。默认值选 gpt-sovits 是为了让
    /// 老用户升级后行为不变，不会突然发现 TTS 换了引擎。</summary>
    public string TTSLocalEngine { get; set; } = "gpt-sovits";

    public int IndexTtsPort { get; set; } = 9990;
    public string IndexTtsExecutablePath { get; set; } = string.Empty;
    public string IndexTtsArguments { get; set; } = string.Empty;
    public string IndexTtsWorkingDirectory { get; set; } = string.Empty;

    /// <summary>IndexTTS 的 checkpoints 目录（config.yaml/gpt.pth/bpe.model
    /// 等所在的文件夹）。跟 SenseVoiceModelsDirectory 是同一个思路：模型
    /// 文件外部化，不进打包产物，方便你在自己机器上微调完之后直接把
    /// checkpoints 目录整个换掉，不用重新打包这个服务。</summary>
    public string IndexTtsModelsDirectory { get; set; } = string.Empty;

    /// <summary>用 IndexTTS 2.0（"v2"）还是 2.5（"v2.5"）——两个版本各有
    /// 取舍（2.5 更快，2.0 实测音色保真度更好），暂时不定死一个，跟
    /// ModelPrecision 一样的模式：改这个设置只是存下来，不做热重载，下次
    /// "开始翻译"重新拉起 IndexTTSService 进程时才会用新版本重新加载
    /// 模型。</summary>
    public string IndexTtsVersion { get; set; } = "v2.5";

    /// <summary>情绪参考音频的影响强度，对应 IndexTTS2 的 emo_alpha 参数，
    /// 范围 0~1。首页语音合成板块开放了一个滑块直接调这个值，每次开始
    /// 翻译前调整都会实时生效（不需要重启 IndexTTS 服务——这个参数是
    /// 跟着每次 /synthesize 请求一起传的，不是模型加载时的固定配置）。
    /// 默认 0.7，不是官方默认的 1.0——见跟用户讨论时的结论：把情绪参考
    /// 强度拉满，在直播间实时人声（而不是干净的预录音频）这个场景下
    /// 可能会一定程度牺牲音色保真度，具体多少需要用户自己用这个滑块试。</summary>
    public double TTSEmoAlpha { get; set; } = 0.7;

    // ---- 阿里云百炼（云端 API，Qwen-Audio-TTS/CosyVoice）----
    // 挂在 TTSApiKind 下（跟 "azure" 平级），不是本地引擎——这是纯粹的
    // 云端调用，不需要起子进程。API Key 复用下面通用的 TTSApiKey 字段。

    /// <summary>WebSocket 服务地址，包含每个百炼工作空间专属的子域名前缀
    /// （比如 wss://ws-xxxxxxxx.cn-beijing.maas.aliyuncs.com/api-ws/v1/inference）。
    /// 这部分因账号而异，不能写死在代码里，需要用户自己去百炼控制台确认
    /// 之后填进来。</summary>
    public string TTSAliyunWebSocketUrl { get; set; } = string.Empty;

    /// <summary>合成时用的模型名，比如 "qwen-audio-3.0-tts-flash"。声音
    /// 复刻（注册 Character.AliyunVoiceId 时用的 target_model）必须跟这个
    /// 值一致，否则合成会报 voice/model 不匹配的错误。</summary>
    public string TTSAliyunModel { get; set; } = "qwen-audio-3.0-tts-flash";

    /// <summary>手动填的 voice_id，方便测试用——阿里云那边完整的"注册
    /// 声音克隆"流程（本地参考音频 -> 传到公网可访问的地址 -> 调注册
    /// 接口 -> 拿到 voice_id -> 存到角色身上）还没有做进应用里，这个字段
    /// 是绕开这一整套、先手动粘贴一个已经在别处注册好的 voice_id 进来
    /// 测试用的。非空时优先用这个，而不是去查 Character.AliyunVoiceId——
    /// Character.AliyunVoiceId 这个字段不删，留着给以后做完整注册流程时
    /// 用。</summary>
    public string TTSAliyunManualVoiceId { get; set; } = string.Empty;

    // ---- Qwen3-TTS（本地 TTS 引擎的第三个选项，用于跟 GPT-SoVITS/
    // IndexTTS 做速度对比）----

    public int QwenTtsPort { get; set; } = 9991;
    public string QwenTtsExecutablePath { get; set; } = string.Empty;
    public string QwenTtsArguments { get; set; } = string.Empty;
    public string QwenTtsWorkingDirectory { get; set; } = string.Empty;

    /// <summary>Qwen3-TTS 的模型目录（HuggingFace snapshot 或者你自己转换
    /// 好的权重目录）。跟 IndexTtsModelsDirectory/SenseVoiceModelsDirectory
    /// 是同一个思路：模型文件外部化，不进打包产物。</summary>
    public string QwenTtsModelsDirectory { get; set; } = string.Empty;

    // ---- 角色（声音克隆）----

    /// <summary>当前选中的角色 Id。null 表示还没选任何角色（首次使用、
    /// 还没建过角色），这种情况下不允许开始翻译，UI 层要给出提示。</summary>
    public int? ActiveCharacterId { get; set; }

    /// <summary>
    /// 【迁移专用】旧版本里全局的情绪参考音频配置。程序启动时如果发现
    /// 这里有值、且数据库里还没有任何角色，会自动创建一个"默认角色"
    /// 并把这份配置迁移过去，迁移完成后 LegacyTtsReferenceAudioMigrated
    /// 会被设为 true，不会重复迁移。迁移逻辑稳定运行几个版本之后，
    /// 这个字段和下面的 TtsReferenceClip 类可以整个删除。
    /// </summary>
    public Dictionary<string, TtsReferenceClip> TTSReferenceAudioByEmotion { get; set; } = new();

    public bool LegacyTtsReferenceAudioMigrated { get; set; } = false;

    public class TtsReferenceClip
    {
        public string AudioPath { get; set; } = string.Empty;
        public string PromptText { get; set; } = string.Empty;
    }
}