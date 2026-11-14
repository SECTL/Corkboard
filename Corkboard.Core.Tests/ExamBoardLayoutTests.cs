using Corkboard.Core.Enums.Configs;
using Corkboard.Core.Exam;
using Corkboard.Core.Exam.Layout;
using Corkboard.Core.Models.SubConfigs.Exam;

namespace Corkboard.Core.Tests;

/// <summary>
///     布局几何：百分比换算、12 列 / 1% 吸附、矩形夹取、八手柄缩放，以及配置归一与深拷贝。
/// </summary>
public class ExamBoardLayoutTests
{
    [Fact]
    public void PercentAndDip_RoundTrip()
    {
        Assert.Equal(960, ExamBoardLayoutMath.PercentToDip(50, 1920), 9);
        Assert.Equal(50, ExamBoardLayoutMath.DipToPercent(960, 1920), 9);
        Assert.Equal(0, ExamBoardLayoutMath.DipToPercent(10, 0), 9);
    }

    [Fact]
    public void SnapX_UsesTwelveColumns_SnapYUsesOnePercent()
    {
        Assert.Equal(3.4167, ExamBoardLayoutMath.SnapX(3.4), 3);
        Assert.Equal(49.9167, ExamBoardLayoutMath.SnapX(49.9), 3);
        Assert.Equal(11, ExamBoardLayoutMath.SnapY(10.6), 9);
    }

    [Fact]
    public void ClampRect_FixesNanAndOverflow()
    {
        var rect = ExamBoardLayoutMath.ClampRect(new ExamBoardLayoutRect(double.NaN, double.PositiveInfinity, 999, double.NaN));

        Assert.Equal(0, rect.X, 9);
        Assert.Equal(0, rect.Y, 9);
        Assert.Equal(100, rect.Width, 9);
        Assert.Equal(100, rect.Height, 9);

        var overflow = ExamBoardLayoutMath.ClampRect(new ExamBoardLayoutRect(80, 90, 30, 30));
        Assert.Equal(70, overflow.X, 9);
        Assert.Equal(70, overflow.Y, 9);
        Assert.Equal(30, overflow.Width, 9);
        Assert.Equal(30, overflow.Height, 9);
    }

    [Fact]
    public void Resize_KeepsMinimumSizeAndBounds()
    {
        var start = new ExamBoardLayoutRect(20, 20, 40, 40);

        var moved = ExamBoardLayoutMath.Resize(start, ExamBoardResizeHandle.TopLeft, -10, -10, 10, 10);
        Assert.Equal(10, moved.X, 9);
        Assert.Equal(10, moved.Y, 9);
        Assert.Equal(50, moved.Width, 9);
        Assert.Equal(50, moved.Height, 9);

        var shrunk = ExamBoardLayoutMath.Resize(start, ExamBoardResizeHandle.BottomRight, -100, -100, 10, 10);
        Assert.Equal(10, shrunk.Width, 9);
        Assert.Equal(10, shrunk.Height, 9);
    }

    [Fact]
    public void Defaults_GiveTitleTopLeftAndOthersCentered()
    {
        var title = ExamBoardLayoutDefaults.CreateComponent(ExamBoardLayoutComponentId.Title);
        var clock = ExamBoardLayoutDefaults.CreateComponent(ExamBoardLayoutComponentId.Clock);

        Assert.Equal(ExamBoardContentAlignment.TopLeft, title.ContentAlignment);
        Assert.Equal(ExamBoardContentAlignment.MiddleCenter, clock.ContentAlignment);
        Assert.Equal(1, clock.Scale, 9);
        Assert.Equal(1, clock.Opacity, 9);
    }

    [Fact]
    public void Config_NormalizeFillsNullAndClampsComponents()
    {
        var config = new ExamBoardLayoutConfig
        {
            Upcoming = null!,
            InProgress = new ExamBoardStatusLayout
            {
                IsCustomized = true,
                Clock = new ExamBoardComponentLayout
                {
                    X = -10,
                    Y = 95,
                    Width = 30,
                    Height = 30,
                    Scale = 9,
                    Opacity = 0,
                    ContentAlignment = (ExamBoardContentAlignment)99
                }
            }
        };

        config.Normalize();

        Assert.NotNull(config.Upcoming);
        Assert.False(config.Upcoming.IsCustomized);
        Assert.Equal(0, config.InProgress.Clock.X, 9);
        Assert.Equal(70, config.InProgress.Clock.Y, 9);
        Assert.Equal(30, config.InProgress.Clock.Width, 9);
        Assert.Equal(2, config.InProgress.Clock.Scale, 9);
        Assert.Equal(0.2, config.InProgress.Clock.Opacity, 9);
        Assert.Equal(ExamBoardContentAlignment.MiddleCenter, config.InProgress.Clock.ContentAlignment);
    }

    [Fact]
    public void Clone_IsDeepCopy()
    {
        var config = new ExamBoardLayoutConfig();
        config.Finished.IsCustomized = true;
        config.Finished.Title.X = 1;

        var clone = config.Clone();
        clone.Finished.Title.X = 2;

        Assert.Equal(1, config.Finished.Title.X, 9);
        Assert.True(clone.Finished.IsCustomized);
    }

    [Fact]
    public void For_ReturnsEachStatusBucket()
    {
        var config = new ExamBoardLayoutConfig();
        Assert.Same(config.NotStarted, config.For(ExamStatus.NotStarted));
        Assert.Same(config.Upcoming, config.For(ExamStatus.Upcoming));
        Assert.Same(config.InProgress, config.For(ExamStatus.InProgress));
        Assert.Same(config.Finished, config.For(ExamStatus.Finished));
    }
}