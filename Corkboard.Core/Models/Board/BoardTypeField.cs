using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using Corkboard.Core.Enums;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.Core.Models.Board;

/// <summary>作业类型里的一个字段，例如「页数」「遍数」「内容」。</summary>
public partial class BoardTypeField : ObservableObject
{
    [ObservableProperty] private Guid _id = Guid.NewGuid();

    /// <summary>字段名，用户自己填（如「页数」）。</summary>
    [ObservableProperty] private string _label = string.Empty;

    /// <summary>取值类型，决定输入控件与显示方式。</summary>
    [ObservableProperty] private BoardFieldKind _kind = BoardFieldKind.Text;

    /// <summary>
    ///     当前取值类型的下拉序号。设置页用 <c>SelectedIndex</c> 绑它，
    ///     这样下拉项可以直接用 <see cref="BoardFieldKinds.All" /> 的显示名列表，不用写转换器。
    /// </summary>
    [JsonIgnore]
    public int KindIndex
    {
        get => (int)Kind;
        set => Kind = (BoardFieldKind)Math.Clamp(value, 0, BoardFieldKinds.All.Count - 1);
    }

    /// <summary>字段名空着时列表里也得有个说法。</summary>
    [JsonIgnore]
    public string DisplayLabel => string.IsNullOrWhiteSpace(Label) ? CR.Board_UntitledField : Label.Trim();
}
