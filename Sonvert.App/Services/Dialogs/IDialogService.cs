using System.Threading.Tasks;

namespace Sonvert.App.Services.Dialogs;

/// <summary>
/// 通用的模态小弹窗服务——"输入一个文本"和"确认一下要不要做某个破坏性
/// 操作"这两种交互在项目里不止声音克隆这一处会用到（比如以后删除历史
/// 记录、删除术语表条目大概率也会想要同样的确认框），所以做成一个
/// 独立的、跟具体业务无关的通用服务，不是写死在 VoiceCloningViewModel
/// 里的一次性代码。
///
/// ViewModel 只依赖这个接口，不直接持有任何 Window 引用——Window 的
/// 创建、ShowDialog、找 owner 窗口这些事情都封装在实现类里，这跟项目里
/// ISubtitleWindowService 的设计思路是一致的。
/// </summary>
public interface IDialogService
{
    /// <summary>弹出一个"输入一行文字"的对话框。用户点确认（或者在输入框
    /// 里直接按回车）返回输入的文字；点取消或者关闭窗口返回 null。
    /// 不会对输入内容做任何校验（比如是否为空）——校验逻辑是调用方
    /// 自己业务相关的判断，这个服务只负责"弹窗、拿到用户输入"这件事。</summary>
    Task<string?> ShowTextInputAsync(string title, string message, string confirmButtonText = "确定");

    /// <summary>弹出一个"确定/取消"的确认对话框，用于破坏性操作前的
    /// 二次确认。返回 true 表示用户点了确认。</summary>
    Task<bool> ShowConfirmationAsync(string title, string message, string confirmButtonText = "确定");
}
