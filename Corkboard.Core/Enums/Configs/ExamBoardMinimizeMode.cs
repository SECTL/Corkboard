namespace Corkboard.Core.Enums.Configs;

/// <summary>
///     考试看板左下角那颗按钮**什么时候**变成「最小化」（而不是退出）。
///     <para>
///         用户的原话是「应该是在考试进行中才是最小化，平常都是只有退出」——考完就没什么好收起来的了，
///         这时候那颗按钮该老老实实当退出键用。所以默认值不是「一直」，而是 <see cref="InProgress" />。
///     </para>
///     <para>
///         ⚠️ 按**数字**落盘（配置管道没有字符串枚举转换器），只能往后追加成员，不能改已有数值。
///     </para>
/// </summary>
public enum ExamBoardMinimizeMode
{
    /// <summary>从不：那颗按钮永远只是「长按 2 秒退出」。</summary>
    Off = 0,

    /// <summary>只在考试进行中（开考到收卷之间）：默认这一档。</summary>
    InProgress = 1,

    /// <summary>考试进行中、或者快到开考了（阈值见「看板显示」里的即将开始阈值）。</summary>
    InProgressOrUpcoming = 2,

    /// <summary>一直：不管有没有考试，那颗按钮都是最小化。</summary>
    Always = 3
}
