using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.Core.Models.Board;

/// <summary>
///     一种作业（如「练习册」「抄写」）：它决定新建作业时要填哪些字段。
///     <para>
///         「页数」「遍数」这类字段是<b>类型的一部分</b>，不是所有作业都有的通用属性；
///         科目之类的通用属性挂在 <see cref="BoardNote" /> 上。
///     </para>
/// </summary>
public partial class BoardTypeDef : ObservableObject
{
    [ObservableProperty] private Guid _id = Guid.NewGuid();

    /// <summary>类型名，显示在新建作业的类型选择与便签上。</summary>
    [ObservableProperty] private string _name = string.Empty;

    /// <summary>字段清单。顺序就是输入表单里的先后，也是便签上字段值的显示顺序。</summary>
    [ObservableProperty] private ObservableCollection<BoardTypeField> _fields = [];

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? CR.Board_Untitled : Name.Trim();
}
