namespace Corkboard.Core.Board.AutoSave;

/// <summary>
///     自动存档（定时把作业板存成图片）的保存时机。
///     <para>
///         两种时机的差别只在<b>文件名</b>：一种一天一个固定名字（当天反复保存互相覆盖，最后只剩一份），
///         另一种每次保存都换一个带时分秒的新名字（一天的改动全留着）。裁剪规则两者一样，
///         都是「只留最近 N 天」。
///     </para>
///     <para>
///         枚举按数字落盘（见 <c>ConfigServiceBase.JsonOptions</c>，没有注册字符串枚举转换器），
///         所以只能在<b>末尾</b>增删成员，不要改动已有成员的数字。
///     </para>
/// </summary>
public enum BoardAutoSaveMode
{
    /// <summary>
    ///     每天一张：文件名只带日期，同一天里改多少次都覆盖同一个文件，
    ///     所以每天最后留下的就是那一天板子的最后样子。默认值。
    /// </summary>
    Daily = 0,

    /// <summary>每次修改都存一张：文件名带时分秒，一天的改动轨迹全留着，按保留天数裁剪。</summary>
    EveryChange = 1
}
