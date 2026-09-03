using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;

namespace Sonvert.App.ViewModels;

/// <summary>用于致谢清单里单独一条组件——名字、许可证、说明、
/// 项目地址、是否需要署名（决定 XAML 里显示"需署名"还是"建议致谢"这个
/// 标签，视觉上复用声音克隆页"必需/可选"那套标签样式）。</summary>
public class AcknowledgementItem
{
    public required string Name { get; init; }
    public required string License { get; init; }
    public required string Description { get; init; }
    public required string Url { get; init; }

    /// <summary>true = 许可证条款要求署名（比如 CC-BY 4.0、FunASR 的模型
    /// 开源协议），false = 没有强制要求、纯粹是礼貌致谢（大多数 MIT/
    /// Apache 2.0 的代码库，保留版权声明放在许可证文件里就满足条款，
    /// 界面上展示是加分项不是义务）。</summary>
    public required bool RequiresAttribution { get; init; }

    public string AttributionLabel => RequiresAttribution ? "署名" : "致谢";
}

public class AcknowledgementGroup
{
    public required string Title { get; init; }
    public required ObservableCollection<AcknowledgementItem> Items { get; init; }
}

/// <summary>
/// 关于页面——展示软件版本信息，以及用到的模型/开源项目的致谢清单。
///
/// 致谢清单不是随便列的：里面标"需署名"的几项（SenseVoice 模型、
/// OPUS-MT）是各自许可证条款明确要求的，不是可选礼貌行为——具体是
/// 什么意思、要做到什么程度，见每一项 Description 里的说明。这份清单
/// 只是界面上的摘要（名字+许可证类型+一句话说明+链接），不是完整的
/// 法律文本；如果以后想更严谨，可以在仓库里另外建一份
/// THIRD_PARTY_LICENSES.md 收录每个组件的完整许可证原文，这个页面
/// 加一个链接跳过去就行，现在先不做这一步。
/// </summary>
public partial class AboutViewModel : ViewModelBase
{
    public string AppName => "Sonvert";

    /// <summary>版本号直接从程序集元数据读取，不是写死的字符串——
    /// 这样每次改 .csproj 里的 Version 属性发布新版本，这里会自动跟着
    /// 更新，不需要多改一个地方、也不会出现"关于页面显示的版本号跟
    /// 实际不一致"这种低级失误。读不到时（比如开发环境没设置版本号）
    /// 兜底显示"开发版"，不显示一个奇怪的空字符串或者 "0.0.0.0"。</summary>
    public string Version
    {
        get
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            return version is null || version.ToString() == "0.0.0.0"
                ? "开发版"
                : $"v{version.Major}.{version.Minor}.{version.Build}";
        }
    }

    public string Tagline => "实时同声传译工具";

    public ObservableCollection<AcknowledgementGroup> AcknowledgementGroups { get; } = new()
    {
        new AcknowledgementGroup
        {
            Title = "语音识别",
            Items = new ObservableCollection<AcknowledgementItem>
            {
                
            },
        },
        new AcknowledgementGroup
        {
            Title = "机器翻译",
            Items = new ObservableCollection<AcknowledgementItem>
            {

            },
        },
        new AcknowledgementGroup
        {
            Title = "语音合成",
            Items = new ObservableCollection<AcknowledgementItem>
            {
            },
        },
        new AcknowledgementGroup
        {
            Title = "界面与基础框架",
            Items = new ObservableCollection<AcknowledgementItem>
            {
                new AcknowledgementItem
                {
                    Name = "Avalonia UI",
                    License = "MIT",
                    Description = "跨平台 .NET UI 框架，本项目的界面基于此构建。",
                    Url = "https://github.com/AvaloniaUI/Avalonia",
                    RequiresAttribution = false,
                },
                new AcknowledgementItem
                {
                    Name = "CommunityToolkit.Mvvm",
                    License = "MIT",
                    Description = "微软官方 MVVM 工具库，简化 ViewModel 里属性通知/命令的写法。",
                    Url = "https://github.com/CommunityToolkit/dotnet",
                    RequiresAttribution = false,
                },
                new AcknowledgementItem
                {
                    Name = "NAudio",
                    License = "MIT",
                    Description = "音频采集/播放/编解码。",
                    Url = "https://github.com/naudio/NAudio",
                    RequiresAttribution = false,
                },
                new AcknowledgementItem
                {
                    Name = "Entity Framework Core",
                    License = "MIT",
                    Description = "角色/历史记录等数据的本地 SQLite 存储。",
                    Url = "https://github.com/dotnet/efcore",
                    RequiresAttribution = false,
                },
            },
        },
    };

    /// <summary>第三方云服务——跟上面的开源模型不是一回事：这些不是打包
    /// 进软件里的模型文件，是运行时按需调用的在线接口，受各自的服务
    /// 条款约束，不属于开源许可证署名的范畴，这里列出来纯粹是"支持接入"
    /// 的说明，不是署名义务。</summary>
    public ObservableCollection<string> ThirdPartyServices { get; } = new()
    {
        "Azure 翻译", "Azure 语音合成",
    };

    [RelayCommand]
    private void OpenLink(string url)
    {
        // 用系统默认浏览器打开链接——UseShellExecute=true 是必须的，
        // .NET 的 Process.Start 默认不会走系统关联程序这条路，不加这个
        // 参数在 Windows 上直接打开 URL 会抛异常。
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
}
