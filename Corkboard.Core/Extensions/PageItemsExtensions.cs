using System.Collections.ObjectModel;
using DynamicData;
using FluentAvalonia.UI.Controls;
using Corkboard.Core.Attributes;
using Corkboard.Core.Controls;
using Corkboard.Core.Services;

namespace Corkboard.Core.Extensions;

/// <summary>
///     把注册表里的 <see cref="PageInfo" /> 转成 FluentAvalonia 导航项，并把扁平化结果
///     填进 <paramref name="flattenNavigationItems" /> 供选中态查找。
/// </summary>
public static class PageItemsExtensions
{
    public static List<FANavigationViewItemBase> ToNavigationViewItems(this IEnumerable<PageInfo> infosEnumerable,
        ObservableCollection<FANavigationViewItemBase> flattenNavigationItems)
    {
        var infos = infosEnumerable
            .Where(info => !info.IsHide)
            .ToList();
        var groups = infos
            .GroupBy(x => x.GroupId)
            .ToList();
        var addedGroups = new HashSet<string>();
        List<FANavigationViewItemBase> navigationViewItems = [];

        foreach (var i in infos)
        {
            if (i.GroupId != null && addedGroups.Contains(i.GroupId)) continue;

            FANavigationViewItemBase item;

            var group = PagesRegistryService.GroupItems.FirstOrDefault(group => group.Id == i.GroupId);
            if (i.GroupId != null && group != null)
            {
                var groupItem = new FANavigationViewItem
                {
                    IconSource = new FluentIconSource(group.IconGlyph),
                    Content = group.Name,
                    Tag = group
                };

                if (groups.FirstOrDefault(x => x.Key == i.GroupId) is { } groupItems)
                {
                    var children = groupItems
                        .Select(x => x.ToNavigationViewItemBase())
                        .ToList();
                    flattenNavigationItems.AddRange(children);
                    groupItem.MenuItems.AddRange(children);
                }

                addedGroups.Add(i.GroupId);
                item = groupItem;
            }
            else
            {
                item = i.ToNavigationViewItemBase();
                flattenNavigationItems.Add(item);
            }

            navigationViewItems.Add(item);
        }

        return navigationViewItems;
    }

    /// <summary>
    ///     展开包含 <paramref name="item" /> 的分组项，返回是否真的展开了分组。
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         FluentAvalonia 的 <c>FANavigationView</c> 必须先实例化出选中项的容器，才能给它做选择动画。
    ///         折叠分组里的子项不会生成容器，于是 <c>AnimateSelectionChanged</c> 会把自己无限重新投递到
    ///         Dispatcher：UI 线程被自己的消息循环饿死，窗口被系统判定为无响应（卡死），CPU 也会被打满。
    ///     </para>
    ///     <para>
    ///         因此选中分组内的页面前，必须先把祖先分组展开（并在展开后走一次布局，容器才会实例化）。
    ///     </para>
    /// </remarks>
    public static bool ExpandGroupsContaining(
        this IEnumerable<FANavigationViewItemBase> navigationItems,
        FANavigationViewItemBase item)
    {
        var expanded = false;
        foreach (var navigationItem in navigationItems)
        {
            if (navigationItem is not FANavigationViewItem group || group.IsExpanded)
                continue;

            var children = group.MenuItems;
            if (children is null || !children.Contains(item))
                continue;

            group.IsExpanded = true;
            expanded = true;
        }

        return expanded;
    }
}
