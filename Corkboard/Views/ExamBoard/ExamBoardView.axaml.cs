using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Corkboard.Core.Controls.ExamBoard;
using Corkboard.Core.Exam.Layout;
using Corkboard.ViewModels.ExamBoard;

namespace Corkboard.Views.ExamBoard;

/// <summary>
///     考试看板的共享视图（放映窗 + 布局编辑窗）。
///     布局编辑窗的 VM 实现 <see cref="IExamBoardLayoutEditingHost" />，点击画布区块时由这里回传选中项；
///     放映窗的 VM 不实现，同一处点击不会产生任何动作。
/// </summary>
public partial class ExamBoardView : UserControl
{
    public ExamBoardView()
    {
        InitializeComponent();

        LayoutPanel.ComponentPressed += OnLayoutPanelComponentPressed;
    }

    /// <summary>
    ///     取某个区块在本视图坐标系里的实际矩形。布局编辑窗用它把属性面板摆到被点中的区块旁边。
    ///     <para>
    ///         默认版式取原来的 Grid 块，自定义版式取 <see cref="ExamBoardLayoutPanel" /> 里挂
    ///         <see cref="ExamBoardLayoutPanel.ComponentProperty" /> 的宿主；区块被「看板显示」设置藏起来、
    ///         或还没量出尺寸时返回 <see langword="false" />，调用方自己兜底摆位。
    ///     </para>
    /// </summary>
    public bool TryGetComponentBounds(ExamBoardLayoutComponentId component, out Rect bounds)
    {
        var target = LayoutPanel.IsVisible
            ? LayoutPanel.Children.OfType<ContentControl>()
                .FirstOrDefault(child => ExamBoardLayoutPanel.GetComponent(child) == component)
            : component switch
            {
                ExamBoardLayoutComponentId.Title => DefaultTitle,
                ExamBoardLayoutComponentId.Clock => DefaultClock,
                ExamBoardLayoutComponentId.ExamInfo => DefaultInfo,
                ExamBoardLayoutComponentId.ExamTable => DefaultTable,
                ExamBoardLayoutComponentId.ExitCorner => DefaultExit,
                _ => null
            };

        if (target is null)
        {
            bounds = default;
            return false;
        }

        var topLeft = target.TranslatePoint(new Point(0, 0), this);
        if (topLeft is null || target.Bounds.Width <= 0 || target.Bounds.Height <= 0)
        {
            bounds = default;
            return false;
        }

        bounds = new Rect(topLeft.Value, target.Bounds.Size);
        return true;
    }

    private void OnLayoutPanelComponentPressed(object? sender, ExamBoardLayoutComponentId? component)
    {
        if (DataContext is IExamBoardLayoutEditingHost host)
        {
            host.SelectComponent(component);
        }
    }
}