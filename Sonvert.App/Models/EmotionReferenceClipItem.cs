using CommunityToolkit.Mvvm.ComponentModel;

namespace Sonvert.App.Models;

/// <summary>
/// TTSReferenceAudioByEmotion 这个 Dictionary 不方便直接绑定到一个可增删的
/// 列表控件上，所以设置页面里用这个可编辑的行对象做中转——加载设置时从
/// Dictionary 转成这个列表，保存时再转回 Dictionary。
/// </summary>
public partial class EmotionReferenceClipItem : ObservableObject
{
    [ObservableProperty]
    private string _emotion = string.Empty;

    [ObservableProperty]
    private string _audioPath = string.Empty;

    [ObservableProperty]
    private string _promptText = string.Empty;
}