using CommunityToolkit.Mvvm.ComponentModel;

namespace Sonvert.App.ViewModels;

/// <summary>所有 ViewModel 的公共基类。目前是空的，先占位，
/// 以后如果有跨 ViewModel 复用的通用逻辑（比如统一的忙碌状态）再加进来。</summary>
public partial class ViewModelBase : ObservableObject
{
}
