using Avalonia.Controls;

namespace Sonvert.App.Views;

/// <summary>通用确认弹窗的代码后置，设计思路跟 TextInputDialog 一样，
/// 见那个类的注释。</summary>
public partial class ConfirmationDialog : Window
{
    public ConfirmationDialog()
    {
        InitializeComponent();
    }

    public static ConfirmationDialog Create(string title, string message, string confirmButtonText)
    {
        var dialog = new ConfirmationDialog { Title = title };

        var messageText = dialog.FindControl<TextBlock>("MessageText")!;
        messageText.Text = message;

        var confirmButton = dialog.FindControl<Button>("ConfirmButton")!;
        confirmButton.Content = confirmButtonText;

        var cancelButton = dialog.FindControl<Button>("CancelButton")!;

        confirmButton.Click += (_, _) => dialog.Close(true);
        cancelButton.Click += (_, _) => dialog.Close(false);

        return dialog;
    }
}
