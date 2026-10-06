using System.ComponentModel;

namespace Corkboard.Core.Models;

/// <summary>导航分组：把若干页面收进一个可展开的父项。</summary>
public class PageGroupInfo
{
    public PageGroupInfo(string name, [Localizable(false)] string id, [Localizable(false)] string iconGlyph)
    {
        Name = name;
        Id = id;
        IconGlyph = iconGlyph;
    }

    public string Name { get; set; }
    public string Id { get; }
    public string IconGlyph { get; }
}
