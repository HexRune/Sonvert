using Avalonia.Controls;
using Avalonia.Input;

namespace Sonvert.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // 拖拽移动窗口：整个自绘标题栏区域（除了右上角那几个按钮，因为
        // 按钮本身会拦截自己范围内的点击事件，不会冒泡触发这里）响应
        // 按下拖拽，直接调用 Avalonia 提供的 BeginMoveDrag——不需要自己
        // 计算鼠标位移量去手动挪窗口位置，系统级的拖拽/贴边吸附行为
        // 都是这个方法内置处理好的。
        var dragArea = this.FindControl<Grid>("TitleBarDragArea");
        if (dragArea is not null)
        {
            dragArea.PointerPressed += (_, e) =>
            {
                if (e.GetCurrentPoint(dragArea).Properties.IsLeftButtonPressed)
                {
                    BeginMoveDrag(e);
                }
            };

            // 双击标题栏空白区域，跟系统标题栏行为一致：切换最大化/还原。
            dragArea.DoubleTapped += (_, _) => ToggleMaximizeRestore();
        }

        var minimizeButton = this.FindControl<Button>("MinimizeButton");
        if (minimizeButton is not null)
        {
            minimizeButton.Click += (_, _) => WindowState = WindowState.Minimized;
        }

        var maximizeRestoreButton = this.FindControl<Button>("MaximizeRestoreButton");
        if (maximizeRestoreButton is not null)
        {
            maximizeRestoreButton.Click += (_, _) => ToggleMaximizeRestore();
        }

        var closeButton = this.FindControl<Button>("CloseButton");
        if (closeButton is not null)
        {
            // 直接调用 Close()——这会走窗口正常的关闭生命周期，
            // 触发 App.axaml.cs 里已经接好的 ShutdownRequested 处理逻辑
            // （拦截关闭请求、优雅停掉三个 Python 子进程、再真正退出），
            // 不是绕过那套清理逻辑直接杀进程。
            closeButton.Click += (_, _) => Close();
        }
    }

    private void ToggleMaximizeRestore()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }
}