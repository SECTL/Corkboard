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
}
