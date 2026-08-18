namespace Sonvert.App.Models;

/// <summary>
/// 界面上展示用的一条识别结果，从 RecognitionResultEventArgs 转换而来。
/// 单独建这个类而不是直接用 RecognitionResultEventArgs 绑定到界面，
/// 是因为 EventArgs 语义上是"一次性的事件参数"，不适合长期持有在
/// ObservableCollection 里给界面反复渲染，分开是更清楚的做法。
/// </summary>
public class RecognitionResultItem
{
    public required string Text { get; init; }
    public string? Emotion { get; init; }
    public string? Event { get; init; }
}
