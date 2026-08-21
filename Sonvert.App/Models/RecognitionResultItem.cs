using CommunityToolkit.Mvvm.ComponentModel;

namespace Sonvert.App.Models;

/// <summary>
/// 界面上展示用的一条识别结果。Text/Emotion/Event 识别完成时就确定了，
/// 用 init-only；TranslatedText 是识别完成后异步补上的，所以做成
/// ObservableProperty——翻译结果回来时更新它，界面能自动刷新。
/// </summary>
public partial class RecognitionResultItem : ObservableObject
{
    public required string Text { get; init; }
    public string? Emotion { get; init; }
    public string? Event { get; init; }

    [ObservableProperty]
    private string? _translatedText;
}