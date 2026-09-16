namespace Sonvert.App.Models;

/// <summary>
/// 侧边栏一个菜单项。现在还只是纯数据 + 一个标题，
/// 等实际接入页面切换逻辑时，会加一个对应的页面标识/类型字段，
/// 目前先只做视觉骨架，不需要过度设计。
/// </summary>
public class NavItem
{
    public required string Title { get; init; }

    /// <summary>Segoe MDL2 Assets 图标字体的码位（如 "\uE80F"）——
    /// 用系统自带的图标字体渲染，不用切图/引入图标库依赖，跟标题栏
    /// 最小化/最大化/关闭那几个按钮是同一套做法。只在 Windows 上有
    /// 这个字体，这个项目本来就是 Windows 专用的（依赖好几个 Windows
    /// 专属的音频/Win32 组件），不存在跨平台顾虑。</summary>
    public required string IconGlyph { get; init; }
}
