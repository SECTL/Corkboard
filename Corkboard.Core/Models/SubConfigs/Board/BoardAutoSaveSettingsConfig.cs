using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using Corkboard.Core.Board.AutoSave;

namespace Corkboard.Core.Models.SubConfigs.Board;

/// <summary>
///     作业板自动存档（定时把板子存成图片）的设置。
///     <para>
///         JSON 键由 <c>ConfigServiceBase.JsonOptions</c> 的 snake_case 规则生成
///         （<c>board_auto_save.enabled</c> / <c>board_auto_save.folder</c> 等），
///         枚举按数字落盘，所以只能在末尾追加枚举成员。
///     </para>
///     <para>
///         默认<b>关</b>（用户明确要求「默认不开」）：它会往磁盘上一直写图，
///         得让人自己决定要不要开、以及图存在哪儿。
///         目录留空表示用软件自己的 <c>data/autosave</c>（见 <see cref="BoardAutoSavePolicy.ResolveFolder" />），
///         这样「默认的文件夹在软件自己底下」和「用户想改到桌面」两条路都留着。
///     </para>
/// </summary>
public partial class BoardAutoSaveSettingsConfig : ObservableObject
{
    /// <summary>
    ///     是否定时把作业板存成图片。默认关：没人愿意装完软件就发现磁盘上莫名其妙多了一堆图。
    /// </summary>
    [ObservableProperty] private bool _enabled;

    /// <summary>
    ///     保存时机：每天一张（当天覆盖、只留最后一份）还是每次修改都存一张。
    ///     默认「每天一张」——一天之内的改动有历史与回放兜着，图片更多是给「翻某天的板子」用的。
    /// </summary>
    [ObservableProperty] private BoardAutoSaveMode _mode = BoardAutoSaveMode.Daily;

    /// <summary>
    ///     存档目录。留空表示软件自己的 <c>data/autosave</c>；填了就按填的走。
    ///     读的时候一律过一遍 <see cref="BoardAutoSavePolicy.ResolveFolder" />（去空白、展开成绝对路径）。
    /// </summary>
    [ObservableProperty] private string _folder = string.Empty;

    /// <summary>
    ///     只保留最近多少天的图，超出的在每次保存后删掉。读的时候过一遍
    ///     <see cref="BoardAutoSavePolicy.ClampRetentionDays" />。
    /// </summary>
    [ObservableProperty] private int _retentionDays = BoardAutoSavePolicy.DefaultRetentionDays;

    /// <summary>实际要用的存档目录（绝对路径）。</summary>
    public string ResolveFolder() => BoardAutoSavePolicy.ResolveFolder(Folder);

    /// <summary>
    ///     目录是不是用户在设置里自己指定的（界面上用来决定要不要显示「恢复默认」）。
    ///     <para>
    ///         纯推导值，不落盘：它由 <see cref="Folder" /> 现算，写进 settings.json 只会多一个
    ///         改不了、还会跟 <see cref="Folder" /> 对不上的字段（手改过配置文件时尤其明显）。
    ///     </para>
    /// </summary>
    [JsonIgnore]
    public bool HasCustomFolder => BoardAutoSavePolicy.NormalizeFolder(Folder).Length > 0;

    /// <summary>
    ///     读盘后收拾一遍：枚举越界值与保留天数都夹回合法区间，手改坏的配置不会流到后台服务。
    ///     由 <c>MainConfigModel.NormalizeLoaded</c> 调用。
    /// </summary>
    public void Normalize()
    {
        if (!Enum.IsDefined(Mode))
            Mode = BoardAutoSaveMode.Daily;

        RetentionDays = BoardAutoSavePolicy.ClampRetentionDays(RetentionDays);
    }
}
