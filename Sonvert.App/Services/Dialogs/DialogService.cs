using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Sonvert.App.Views;
using System.Threading.Tasks;

namespace Sonvert.App.Services.Dialogs;

public class DialogService : IDialogService
{
    /// <summary>找当前的主窗口当 owner——ShowDialog 需要一个 owner 窗口
    /// 才能正确居中、正确挡住主窗口的交互（模态）。项目目前只有一个
    /// 顶层窗口（悬浮字幕窗口不算，那个本来就不需要跟对话框产生模态
    /// 关系），直接取 desktop 生命周期的 MainWindow 就够用，不需要更
    /// 复杂的"当前激活窗口是哪个"的判断逻辑。</summary>
    private static Window? GetOwnerWindow()
    {
        return Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;
    }

    public async Task<string?> ShowTextInputAsync(string title, string message, string confirmButtonText = "确定")
    {
        var dialog = TextInputDialog.Create(title, message, confirmButtonText);
        var owner = GetOwnerWindow();

        var result = owner is not null
            ? await dialog.ShowDialog<string?>(owner)
            : await ShowWithoutOwnerAsync<string?>(dialog);

        return string.IsNullOrWhiteSpace(result) ? null : result.Trim();
    }

    public async Task<bool> ShowConfirmationAsync(string title, string message, string confirmButtonText = "确定")
    {
        var dialog = ConfirmationDialog.Create(title, message, confirmButtonText);
        var owner = GetOwnerWindow();

        return owner is not null
            ? await dialog.ShowDialog<bool>(owner)
            : await ShowWithoutOwnerAsync<bool>(dialog);
    }

    /// <summary>理论上找不到主窗口的情况不应该发生（程序都跑起来了，
    /// 主窗口一定存在），但防御性地留一个退路——没有 owner 就用非模态的
    /// Show()，用一个 TaskCompletionSource 等它关闭，保证接口的
    /// "await 完就能拿到结果"这个约定在这种边缘情况下也不会被打破。
    /// 关闭时如果没有走各自的确认/取消按钮（比如直接点右上角关闭），
    /// 统一按 default(T) 处理——bool 场景下就是 false（等同于取消），
    /// 字符串场景下就是 null（等同于没输入）。</summary>
    private static Task<T> ShowWithoutOwnerAsync<T>(Window dialog)
    {
        var tcs = new TaskCompletionSource<T>();
        dialog.Closed += (_, _) => tcs.TrySetResult(default!);
        dialog.Show();
        return tcs.Task;
    }
}
