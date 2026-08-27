namespace Sonvert.App.Services.Subtitle;

public interface ISubtitleWindowService
{
    void Show();
    void Hide();

    /// <summary>供主程序界面上的"解锁字幕"按钮调用。</summary>
    void Unlock();
}