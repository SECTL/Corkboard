using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using Corkboard4Ci.Interface.Enums;

namespace Corkboard4Ci.Interface.Models;

/// <summary>
/// 投递给 ClassIsland 侧插件（Corkboard4Ci）的一条通知。
/// </summary>
/// <remarks>
/// 源实现的 <c>NotificationData</c> 面向点名/抽奖，带 <c>ClassName</c> / <c>DrawCount</c> /
/// <c>ResultType</c>。这里改为中性的 <see cref="BoardId"/> / <see cref="ItemCount"/> /
/// <see cref="Level"/>，并补上通用的 <see cref="Title"/> / <see cref="Body"/> 文本通道。
/// JSON 契约字段命名风格保持不变（PascalCase 属性由序列化策略映射为 SnakeCaseLower）。
/// </remarks>
public partial class NotificationData : ObservableRecipient
{
    /// <summary>看板（通知目标）标识。</summary>
    [ObservableProperty] private string _boardId = string.Empty;

    /// <summary>通知标题。</summary>
    [ObservableProperty] private string _title = string.Empty;

    /// <summary>通知正文。可与 <see cref="Items"/> 同时使用，也可以只用其中一个。</summary>
    [ObservableProperty] private string _body = string.Empty;

    /// <summary>通知的条目列表。</summary>
    [ObservableProperty] private List<NotificationItem> _items = [];

    /// <summary>通知涉及的条目数量。</summary>
    [ObservableProperty] private int _itemCount = 1;

    /// <summary>显示时长（秒）。</summary>
    [ObservableProperty] private double _displayDuration = 5.0;

    /// <summary>是否使用动画。</summary>
    [ObservableProperty] private bool _animation = true;

    /// <summary>通知级别/阶段。</summary>
    [ObservableProperty] private NotificationLevel _level = NotificationLevel.Unknown;
}
