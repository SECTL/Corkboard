using CommunityToolkit.Mvvm.ComponentModel;

namespace Corkboard4Ci.Interface.Models;

/// <summary>
/// 一条通知条目。既可以是纯文本条目（<see cref="Label"/> 为空，只显示 <see cref="Text"/>），
/// 也可以是「标签 + 正文」的键值型条目。
/// </summary>
/// <remarks>
/// 源实现的 <c>NotificationItem</c> 带 <c>IsLottery</c> / <c>LotteryName</c> / <c>StudentId</c> /
/// <c>StudentName</c> 四个抽取语义字段；这里收敛为中性的 <see cref="Label"/> / <see cref="Text"/> /
/// <see cref="ItemId"/>，纯文本条目不再需要布尔开关区分。
/// </remarks>
public partial class NotificationItem : ObservableRecipient
{
    /// <summary>条目标签（可空）。为空表示该条目只有正文。</summary>
    [ObservableProperty] private string _label = string.Empty;

    /// <summary>条目正文。</summary>
    [ObservableProperty] private string _text = string.Empty;

    /// <summary>条目在发送方业务中的标识；0 表示未提供。</summary>
    [ObservableProperty] private int _itemId;

    /// <summary>条目当前是否仍然有效。</summary>
    [ObservableProperty] private bool _exists = true;
}
