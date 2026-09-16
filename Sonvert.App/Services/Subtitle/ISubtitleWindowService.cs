namespace Sonvert.App.Services.Subtitle;

public interface ISubtitleWindowService
{
    void Show();
    void Hide();

    /// <summary>供主程序界面上的"解锁字幕"按钮调用。</summary>
    void Unlock();

    /// <summary>供主程序界面上锁定字幕窗口开关调用——跟悬浮字幕窗口
    /// 自己的 Ctrl+Shift+L 快捷键触发的是同一套逻辑（点透+ViewModel
    /// 状态），只是入口从窗口自身的键盘事件换成了首页的开关控件。</summary>
    void Lock();

    /// <summary>当前锁定状态——首页的锁定开关用这个做初始值和展示，
    /// 双向绑定时开关本身状态变化再回调 Lock()/Unlock()。直接透传
    /// SubtitleWindowViewModel.IsLocked，不在这一层重复维护一份。</summary>
    bool IsLocked { get; }
}