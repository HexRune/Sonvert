using Avalonia.Controls;
using Avalonia.Input;

namespace Sonvert.App.Views;

/// <summary>
/// 通用文本输入弹窗的代码后置——纯粹是"设好文案、接好按钮/回车、
/// 把结果通过 Close(result) 传出去"这几件事，没有绑定真正的
/// ViewModel/DataContext，因为这种一次性的、跟具体业务完全无关的
/// 技术型小弹窗，没必要为了"严格 MVVM"专门建一个 ViewModel 类，
/// 直接在代码后置里处理反而更直接、更容易看懂。
/// </summary>
public partial class TextInputDialog : Window
{
    public TextInputDialog()
    {
        InitializeComponent();
    }

    /// <summary>调用方用这个静态方法弹窗，不需要自己拼装
    /// message/按钮文字这些控件的绑定细节。</summary>
    public static TextInputDialog Create(string title, string message, string confirmButtonText)
    {
        var dialog = new TextInputDialog { Title = title };

        var messageText = dialog.FindControl<TextBlock>("MessageText")!;
        messageText.Text = message;

        var confirmButton = dialog.FindControl<Button>("ConfirmButton")!;
        confirmButton.Content = confirmButtonText;

        var inputTextBox = dialog.FindControl<TextBox>("InputTextBox")!;
        var cancelButton = dialog.FindControl<Button>("CancelButton")!;

        confirmButton.Click += (_, _) => dialog.Close(inputTextBox.Text);
        cancelButton.Click += (_, _) => dialog.Close(null);

        // 回车直接确认，不用非得点"确定"按钮——跟很多软件里"填个名字
        // 敲回车就提交"的习惯一致。Shift+Enter 不处理成确认（虽然这个
        // 输入框不是多行的，但防御性地只处理不带修饰键的纯 Enter）。
        inputTextBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None)
            {
                dialog.Close(inputTextBox.Text);
            }
        };

        dialog.Opened += (_, _) => inputTextBox.Focus();

        return dialog;
    }
}
