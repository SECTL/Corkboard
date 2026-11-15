using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Corkboard.Core.Controls.ExamBoard;
using Corkboard.Core.Exam.Layout;
using Corkboard.Core.Models.SubConfigs.Exam;
using Corkboard.ViewModels.ExamBoard;

namespace Corkboard.Views.ExamBoard;

/// <summary>
///     考试看板的共享视图（放映窗 + 布局编辑窗）。
///     布局编辑窗的 VM 实现 <see cref="IExamBoardLayoutEditingHost" />，点击画布区块时由这里回传选中项；
///     放映窗的 VM 不实现，同一处点击不会产生任何动作。
///     <para>
///         两个窗口看到的是**同一套分支**：这一档状态没自定义过就走默认 Grid（它会按「看板显示」设置把时钟居中），
///         自定义过才走 <see cref="ExamBoardLayoutPanel" />。布局编辑窗因此和成品长得一模一样，不再自己套面板。
///     </para>
/// </summary>
public partial class ExamBoardView : UserControl
{
    public ExamBoardView()
    {
        InitializeComponent();

        LayoutPanel.ComponentPressed += OnLayoutPanelComponentPressed;

        // 默认 Grid 分支也要能点选区块（编辑窗用；放映窗的 VM 不实现编辑宿主，点了没反应）。
        AddHandler(PointerPressedEvent, OnDefaultLayoutPressed, RoutingStrategies.Bubble,
            handledEventsToo: true);
    }

    /// <summary>
    ///     取某个区块在本视图坐标系里的实际矩形。布局编辑窗用它把属性面板摆到被点中的区块旁边，
    ///     也用它把「系统版式实测出来的位置」抄进草稿。
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
            : FindDefaultBlock(component);

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

    private Control? FindDefaultBlock(ExamBoardLayoutComponentId component) => component switch
    {
        ExamBoardLayoutComponentId.Title => DefaultTitle,
        ExamBoardLayoutComponentId.Clock => DefaultClock,
        ExamBoardLayoutComponentId.ExamInfo => DefaultInfo,
        ExamBoardLayoutComponentId.ExamTable => DefaultTable,
        ExamBoardLayoutComponentId.ExitCorner => DefaultExit,
        _ => null
    };

    private void OnLayoutPanelComponentPressed(object? sender, ExamBoardLayoutComponentId? component)
    {
        if (DataContext is IExamBoardLayoutEditingHost host)
        {
            host.SelectComponent(component);
        }
    }

    /// <summary>默认 Grid 分支里点区块：命中哪个就把哪个交给编辑宿主，点空白（null）收起面板。</summary>
    private void OnDefaultLayoutPressed(object? sender, PointerPressedEventArgs e)
    {
        if (LayoutPanel.IsVisible || DataContext is not IExamBoardLayoutEditingHost host)
        {
            return;
        }

        ExamBoardLayoutComponentId? hit = null;
        for (var visual = e.Source as Visual; visual is not null && visual != this;
             visual = visual.GetVisualParent())
        {
            foreach (var component in Enum.GetValues<ExamBoardLayoutComponentId>())
            {
                if (ReferenceEquals(visual, FindDefaultBlock(component)))
                {
                    hit = component;
                    break;
                }
            }

            if (hit is not null)
            {
                break;
            }
        }

        host.SelectComponent(hit);
    }
}
