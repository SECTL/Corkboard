using CommunityToolkit.Mvvm.ComponentModel;
using Corkboard.Core.Exam;

namespace Corkboard.Core.Models.SubConfigs.Exam;

/// <summary>
///     自动放映（到点自动全屏弹考试看板）的**跨看板**参数（<c>settings.json</c> 的 <c>exam_auto_show</c> 段）。
///     <para>
///         这里只有「什么时候弹、什么时候收」；**哪块看板参加**是看板自己的事
///         （<see cref="Exam.ExamBoardDocument.AutoShowEnabled" />，默认关）。
///         这样换看板不用重设参数，改参数也不会把没参加的看板一起放出来。
///     </para>
///     <para>
///         数值一律过 <see cref="ExamAutoShowPolicy" /> 的夹取，手改过 settings.json 也不会出怪值。
///     </para>
/// </summary>
public partial class ExamAutoShowSettingsConfig : ObservableObject
{
    /// <summary>开考前多少分钟开始放映。默认 10 分钟。</summary>
    [ObservableProperty] private int _leadMinutes = ExamAutoShowPolicy.DefaultLeadMinutes;

    /// <summary>
    ///     开考之后还补不补弹。默认开——软件可能是在考试中间才启动的，
    ///     这时候也该把看板推到最前面，不然整个考试期间都看不见。
    /// </summary>
    [ObservableProperty] private bool _alsoShowAfterStart = true;

    /// <summary>
    ///     最后一场考完再过多少分钟收起看板。默认 10 分钟；0 表示收卷就收。
    ///     用户要的就是「考完不一定立刻关」：留一段缓冲，别在收卷铃响的那一秒把屏幕刷掉。
    /// </summary>
    [ObservableProperty] private int _closeDelayMinutes = ExamAutoShowPolicy.DefaultCloseDelayMinutes;

    /// <summary>读盘后收拾一遍：两个数值夹进合法区间（手改的脏数据不进界面）。</summary>
    public void Normalize()
    {
        LeadMinutes = ExamAutoShowPolicy.ClampLeadMinutes(LeadMinutes);
        CloseDelayMinutes = ExamAutoShowPolicy.ClampCloseDelayMinutes(CloseDelayMinutes);
    }
}
