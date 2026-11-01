using Avalonia.Media;

namespace Corkboard.Core.Services.Fonts;

/// <summary>
///     数字的「等宽数字」（OpenType <c>tnum</c>）开关。
///     <para>
///         为什么要这个：随包分发的 MiSans 默认用的是**比例数字**（实测 <c>0</c> 宽 634、<c>1</c> 宽 403、
///         <c>7</c> 宽 527，单位 upem=1000），所以「21:42:07」每跳一秒整块时钟的字宽都在变，
///         看上去字在左右抖。字体本身带了 <c>tnum</c> 特性（GSUB 里 lookup 30 把十个数字统一映射到
///         宽 594 的等宽字形），只要给 TextBlock 设上这个特性就稳了——**不用换字体、也不用另打包等宽字体**。
///     </para>
///     <para>
///         凡是「数字每秒都在变」或「数字要跨行对齐」的文字都走这里：考试看板的时钟、考试信息里的
///         时间区间与倒计时（「还剩 12 分 30 秒」一秒一跳，居中的那行会左右抖）、表格的时间列
///         （「08:00 – 10:00」上下要对齐）。
///     </para>
///     <para>
///         只对「字体选项 = 默认」生效：用户自己挑了别的字体时，数字宽度是那个字体的一部分观感，
///         我们不去改它（那时返回 <c>null</c>，TextBlock 就当没设过 FontFeatures）。
///     </para>
/// </summary>
public static class TabularFigures
{
    /// <summary>OpenType 等宽数字特性的标签。</summary>
    public const string TabularDigitsTag = "tnum";

    private static readonly FontFeatureCollection? TabularDigits = CreateTabularDigits();

    /// <summary>
    ///     按外观设置里的字体取该用的 OpenType 特性集合：默认字体给 <c>tnum</c>，其余给 <c>null</c>。
    ///     参数就是 <c>appearance.font</c> 的原始值（<see cref="FontFamilyCatalog.DefaultFontSentinel" /> 表示默认）。
    ///     <para>界面里凡是「数字要等宽」的 TextBlock 都绑这个，别只给时钟用。</para>
    /// </summary>
    public static FontFeatureCollection? ForNumbers(string? appearanceFont) =>
        UsesDefaultFont(appearanceFont) ? TabularDigits : null;

    /// <summary>时钟那一行（<see cref="ForNumbers" /> 的同义词，保留名字读起来更直白）。</summary>
    public static FontFeatureCollection? ForClock(string? appearanceFont) => ForNumbers(appearanceFont);

    /// <summary>这份字体设置是不是「默认字体」（空值也算默认，与 <see cref="FontFamilyCatalog.Resolve" /> 一致）。</summary>
    public static bool UsesDefaultFont(string? appearanceFont) =>
        string.IsNullOrWhiteSpace(appearanceFont) || appearanceFont == FontFamilyCatalog.DefaultFontSentinel;

    private static FontFeatureCollection? CreateTabularDigits()
    {
        // Avalonia 的解析器接受 "tnum" 与 "'tnum' 1" 两种写法；解析不出来就当没有这个能力，
        // 宁可退回比例数字，也不要在静态构造里抛异常把整个应用带崩。
        foreach (var text in new[] { TabularDigitsTag, $"'{TabularDigitsTag}' 1" })
        {
            try
            {
                return FontFeatureCollection.Parse(text);
            }
            catch (Exception)
            {
                // 换下一种写法再试。
            }
        }

        return null;
    }
}
