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
    /// <summary>选中的音频输入设备种类。</summary>
    public string InputDeviceKind { get; set; } = "Microphone"; // "Microphone" | "Loopback"

    /// <summary>音频输入设备的 Id。"-1" 是一个特殊值，对应 Windows 的
    /// "跟随系统默认录音设备"这个约定，不是某个固定设备——用户以后在系统
    /// 设置里换了默认麦克风，这边不用重新选择就会自动跟着变。这也是默认值，
    /// 保证不用手动选设备也能直接用。</summary>
    public string InputDeviceId { get; set; } = "-1";

    /// <summary>音频输出设备的 Id——WASAPI 的 MMDevice.ID（一长串 GUID 格式
    /// 字符串）。空字符串表示"跟随系统默认播放设备"，是默认值。</summary>
    public string OutputDeviceId { get; set; } = string.Empty;
    public string RecognitionLanguage { get; set; } = "auto";

    private static string DefaultDevWorkingDirectory() => Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\Sonvert.SenseVoiceService"));

    private static string DefaultDevExecutablePath() =>
        Path.Combine(DefaultDevWorkingDirectory(), @"env\Scripts\python.exe");

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