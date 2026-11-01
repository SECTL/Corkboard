using FluentAvalonia.UI.Controls;
using Corkboard.Core.Controls;
using Corkboard.Core.Enums;

namespace Corkboard.Core.Attributes;

/// <summary>
///     页面注册信息：贴在页面类型上，<c>AddMainPage</c> / <c>AddSettingsPage</c> 读取它，
///     导航菜单和键控 DI 页面工厂都以 <see cref="Id" /> 为准。
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class PageInfo : Attribute
{
    public PageInfo(string id, string iconGlyph, string? groupId = null, PageLocation location = PageLocation.Top,
        bool isHide = false, bool useFullWidth = false, bool hidePageTitle = false)
    {
        Id = id;
        IconGlyph = iconGlyph;
        GroupId = groupId;
        Location = location;

        IsHide = isHide;
        UseFullWidth = useFullWidth;
        HidePageTitle = hidePageTitle;
    }

    public string Name { get; set; } = string.Empty;
    public string Id { get; }
    public string IconGlyph { get; }
    public string? GroupId { get; }
    public PageLocation Location { get; }

    public bool IsHide { get; set; }
    public bool UseFullWidth { get; }
    public bool HidePageTitle { get; }

    /// <summary>该页面被注册时写入的控件类型，供页面工厂实例化。</summary>
    public Type? SettingsPageType { get; set; }

    public FANavigationViewItemBase ToNavigationViewItemBase()
    {
        return new FANavigationViewItem
        {
            IconSource = new FluentIconSource(IconGlyph),
            Content = Name,
            Tag = this
        };
    }
}
