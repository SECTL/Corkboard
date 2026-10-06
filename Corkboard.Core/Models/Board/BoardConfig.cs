using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using Corkboard.Core.Enums;
using Corkboard.Shared;
using Corkboard.Shared.Abstraction;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.Core.Models.Board;

/// <summary>
///     作业板的**定义**数据：<c>data/board/board.json</c>，只放作业类型、科目与手动排序顺序。
///     <para>
///         这些是业务数据而不是应用偏好，所以既不挂到 <c>settings.json</c>，也不和设置文件同处
///         <c>data/config/</c>。**作业本身不在这里**：作业按创建日期分文件存放在
///         <c>data/board/&lt;年&gt;/&lt;月&gt;/&lt;日&gt;/notes.json</c>（见 <c>BoardNoteStore</c>）。
///     </para>
/// </summary>
public partial class BoardConfig : ConfigBase
{
    /// <summary>作业数据根目录名，位于 <c>data/</c> 之下、与 <c>config/</c> 平级。</summary>
    public const string StorageDirectoryName = "board";

    public const string StorageFileName = "board.json";

    /// <summary>作业类型。首次运行会给几种常见类型打底，用户可以自己改。</summary>
    [ObservableProperty] private List<BoardTypeDef> _types = CreateSeedTypes();

    [ObservableProperty]
    private List<string> _subjects =
    [
        CR.Board_Seed_Subject_Chinese,
        CR.Board_Seed_Subject_Math,
        CR.Board_Seed_Subject_English
    ];

    /// <summary>
    ///     手动排序下的科目区块先后，由主界面长按手柄拖动后落盘。只记科目名，**不记作业**：
    ///     区块内的作业顺序仍由 <see cref="BoardNote.Order" /> 决定。
    ///     <para>
    ///         不在表里的科目（新建的、或表里删过又加回来的）排在最后，
    ///     所以这个表缺项不会让任何作业消失。
    ///     </para>
    /// </summary>
    [ObservableProperty] private List<string> _subjectOrder = [];

    [JsonIgnore]
    public override string ConfigFilePath => Utils.GetFilePath(StorageDirectoryName, StorageFileName);

    /// <summary>
    ///     打底的作业类型。用户删光之后文件里会是空数组，重新加载时不会被这里覆盖
    ///     （属性初始化器先跑，反序列化再用文件里的值盖掉）。
    /// </summary>
    private static List<BoardTypeDef> CreateSeedTypes()
    {
        return
        [
            new BoardTypeDef
            {
                Name = CR.Board_Seed_Type_Workbook,
                Fields = [new BoardTypeField { Label = CR.Board_Seed_Field_Pages, Kind = BoardFieldKind.PageRange }]
            },
            new BoardTypeDef
            {
                Name = CR.Board_Seed_Type_Copy,
                Fields =
                [
                    new BoardTypeField { Label = CR.Board_Seed_Field_Content, Kind = BoardFieldKind.Text },
                    new BoardTypeField { Label = CR.Board_Seed_Field_Times, Kind = BoardFieldKind.Number }
                ]
            },
            new BoardTypeDef
            {
                Name = CR.Board_Seed_Type_Recite,
                Fields = [new BoardTypeField { Label = CR.Board_Seed_Field_Content, Kind = BoardFieldKind.Text }]
            },
            // 卷子通常什么都不用填，直接写个名字就行——留一个没有字段的类型做示范。
            new BoardTypeDef { Name = CR.Board_Seed_Type_Paper }
        ];
    }
}
