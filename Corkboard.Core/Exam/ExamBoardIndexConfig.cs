using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using Corkboard.Shared;
using Corkboard.Shared.Abstraction;

namespace Corkboard.Core.Exam;

/// <summary>
///     看板索引：<c>data/exam/boards.json</c>，只记「有哪些看板、现在用的是哪一块」。
///     <para>
///         内容本身在每块看板自己的文件里（<see cref="ExamBoardDocument" />）。
///         之所以要有这份索引，而不是去扫目录：<see cref="Abstraction.ConfigServiceBase" />
///         只有「按 <see cref="ConfigBase.ConfigFilePath" /> 读写一份配置」的能力、没有列目录的接口，
///         索引让服务层能只靠配置管道就知道全部看板，也顺便把下拉框的顺序固定成用户创建的顺序。
///     </para>
///     <para>
///         ⚠️ 索引里出现、文件却不在了的 Id 会被服务丢掉；文件在、索引里没有的看板看不见
///         （手工拷进来的文件不会自动出现，这是刻意取舍——扫描目录就得绕开配置管道）。
///     </para>
/// </summary>
public partial class ExamBoardIndexConfig : ConfigBase
{
    public const string StorageFileName = "boards.json";

    /// <summary>全部看板，顺序即下拉框里的顺序（按创建先后）。</summary>
    [ObservableProperty] private List<Guid> _boardIds = [];

    /// <summary>当前正在看的那一块。为空或指向不存在的看板时，服务会退回第一块。</summary>
    [ObservableProperty] private Guid _activeBoardId;

    [JsonIgnore]
    public override string ConfigFilePath =>
        Utils.GetFilePath(ExamBoardDocument.StorageDirectoryName, StorageFileName);
}
