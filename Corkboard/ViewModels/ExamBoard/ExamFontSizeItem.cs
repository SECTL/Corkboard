using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Corkboard.Core.Exam;
using Corkboard.Core.Icons;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.ViewModels.ExamBoard;

/// <summary>
///     看板上的**一处**文字：资源文案、默认字号、上限档位（一般 / 时钟），以及读写配置的那两个委托。
///     <para>
///         界面只认 <see cref="ExamBoardFontSizeCatalog" /> 这一张表，加一项新文字只要在那张表里加一行、
///         再补三个资源键即可，不用动 XAML。
///     </para>
///     <para>
///         它原来是「看板字号」设置页的行模型；那一页已经下架（字号只在布局编辑窗里按组件调），
///         但布局编辑窗的属性面板仍然用这一份，所以类型留在 <c>ViewModels/ExamBoard</c> 下。
///     </para>
/// </summary>
public partial class ExamFontSizeItem : ObservableObject
{
    /// <summary>
    ///     拖动浮窗里那句预览文字**最多按多大渲染**。看板字号能到 480（时钟那两档），
    ///     照实数渲染会把浮窗撑出屏幕；数值本身照实显示，超过这一档只在浮窗里补一句「预览已缩小」。
    /// </summary>
    public const double PreviewMaxFontSize = 96;

    private readonly Func<double> _read;
    private readonly Action<double> _write;

    public ExamFontSizeItem(
        string id,
        string name,
        string description,
        double defaultValue,
        bool isClock,
        Func<double> read,
        Action<double> write)
    {
        Id = id;
        Name = name;
        Description = description;
        DefaultValue = defaultValue;
        IsClock = isClock;
        _read = read;
        _write = write;
        _draft = (decimal)defaultValue;
    }

    /// <summary>这一项的标识：资源键的后半截，同时决定它读写哪个配置属性。</summary>
    public string Id { get; }

    /// <summary>设置卡上的名称（「时钟」这种）。</summary>
    public string Name { get; }

    /// <summary>名称下面那句简介：说清这一项是哪块字、改了会怎样。</summary>
    public string Description { get; }

    /// <summary>默认字号，「恢复默认字号」与坏值兜底都用它。</summary>
    public double DefaultValue { get; }

    /// <summary>是不是时钟那两档（上限更大）。</summary>
    public bool IsClock { get; }

    /// <summary>滑块下限。</summary>
    public double Minimum => ExamBoardTextStyle.MinFontSize;

    /// <summary>滑块上限：时钟用 <see cref="ExamBoardTextStyle.MaxClockFontSize" />，其余用一般上限。</summary>
    public double Maximum => IsClock ? ExamBoardTextStyle.MaxClockFontSize : ExamBoardTextStyle.MaxFontSize;

    /// <summary>界面上的草稿；拖动滑块的过程中只改它，松手（<see cref="Commit" />）才写进配置。</summary>
    [ObservableProperty]
    private decimal? _draft;

    /// <summary>滑块是不是正在被按住拖（拖动期间浮窗开着）。由编辑窗的指针处理器置位。</summary>
    [ObservableProperty]
    private bool _isDragging;

    /// <summary>
    ///     滑块的滑块值（<see cref="double" />，因为 Avalonia 的 <c>Slider</c> 只吃 double）。
    ///     <para>
    ///         它只是 <see cref="Draft" /> 的一层适配：写进来先取整、再夹进本项区间，
    ///         存回去还是草稿。**不要**在这里直接写配置——拖动一路会写几十次 <c>settings.json</c>。
    ///     </para>
    /// </summary>
    public double SliderValue
    {
        get => (double)(Draft ?? (decimal)DefaultValue);
        set
        {
            var rounded = Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), Minimum, Maximum);
            if (Draft is { } draft && (double)draft == rounded)
                return;

            Draft = (decimal)rounded;
        }
    }

    /// <summary>滑块右边那行字（「48 号」/「48 pt」），跟随界面语言。</summary>
    public string ValueText => $"{SliderValue:0} {CR.Settings_Exam_Font_Unit}";

    /// <summary>
    ///     浮窗里的预览文字：就用这一项自己的名字（「主标题」「只有时钟时」），
    ///     所以不用为 16 项各写一句示例文案，改名时预览自动跟着改。
    /// </summary>
    public string SampleText => Name;

    /// <summary>
    ///     退出图标那一项预览的不是字、是字形，这里给出它的字形；
    ///     其余项为 <c>null</c>（预览走文字那条路）。
    /// </summary>
    public string? SampleGlyph => Id == "ExitIcon" ? FluentIcons.ArrowExitFilled : null;

    /// <summary>预览用字形画（退出图标），还是用文字画。</summary>
    public bool IsIcon => SampleGlyph is not null;

    /// <summary>看板上这一处本来就是粗体的那几项，预览也跟着粗一点。</summary>
    public FontWeight PreviewFontWeight =>
        Id is "Title" or "Clock" or "ClockAlone" or "TableHeader" ? FontWeight.SemiBold : FontWeight.Normal;

    /// <summary>浮窗里预览文字的字号：照当前值，但最多 <see cref="PreviewMaxFontSize" />。</summary>
    public double PreviewFontSize => Math.Min(SliderValue, PreviewMaxFontSize);

    /// <summary>值超过了预览上限（浮窗里那句「预览已缩小」就靠它显隐）。</summary>
    public bool IsPreviewScaled => SliderValue > PreviewMaxFontSize;

    /// <summary>草稿一变，滑块位置、右边那行数值、浮窗里的预览与提示全要跟着刷。</summary>
    partial void OnDraftChanged(decimal? value)
    {
        OnPropertyChanged(nameof(SliderValue));
        OnPropertyChanged(nameof(ValueText));
        OnPropertyChanged(nameof(PreviewFontSize));
        OnPropertyChanged(nameof(IsPreviewScaled));
    }

    /// <summary>把草稿夹进合法区间并写回配置；夹过的值同步回界面，免得显示的和落盘的不是一个数。</summary>
    public void Commit()
    {
        var current = _read();
        var value = Draft is { } draft
            ? (IsClock ? ExamBoardTextStyle.ClampClock((double)draft, DefaultValue)
                       : ExamBoardTextStyle.Clamp((double)draft, DefaultValue))
            : (IsClock ? ExamBoardTextStyle.ClampClock(current, DefaultValue)
                       : ExamBoardTextStyle.Clamp(current, DefaultValue));

        if (Math.Abs(current - value) > double.Epsilon)
            _write(value);

        var normalized = (decimal)value;
        if (Draft != normalized)
            Draft = normalized;
    }

    /// <summary>把草稿重新对齐到配置里的值（夹取后）。</summary>
    public void ResetDraft()
    {
        var value = IsClock
            ? ExamBoardTextStyle.ClampClock(_read(), DefaultValue)
            : ExamBoardTextStyle.Clamp(_read(), DefaultValue);

        var normalized = (decimal)value;
        if (Draft != normalized)
            Draft = normalized;
    }
}