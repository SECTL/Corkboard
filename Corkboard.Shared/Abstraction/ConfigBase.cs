using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Corkboard.Shared.Abstraction;

/// <summary>
///     所有可持久化配置模型的基类。
///     <para>
///         继承 <see cref="ObservableObject" /> 是为了让配置句柄在属性变化时自动落盘，
///         因此子类必须用 <c>[ObservableProperty]</c> 生成 setter，而不是裸字段。
///     </para>
/// </summary>
public abstract class ConfigBase : ObservableObject
{
    /// <summary>该配置文件在磁盘上的绝对路径，由具体模型决定。</summary>
    [JsonIgnore]
    public abstract string ConfigFilePath { get; }
}
