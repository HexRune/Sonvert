namespace Sonvert.App.Models;

/// <summary>
/// 侧边栏一个菜单项。现在还只是纯数据 + 一个标题，
/// 等实际接入页面切换逻辑时，会加一个对应的页面标识/类型字段，
/// 目前先只做视觉骨架，不需要过度设计。
/// </summary>
public class NavItem
{
    public required string Title { get; init; }
}
