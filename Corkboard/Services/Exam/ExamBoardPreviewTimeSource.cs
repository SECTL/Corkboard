using Corkboard.Core.Exam;

namespace Corkboard.Services.Exam;

/// <summary>布局编辑窗用的假时钟：切状态时直接把「现在」拨到对应的示例时刻。</summary>
public sealed class ExamBoardPreviewTimeSource : IExamBoardTimeSource
{
    private DateTime _now = new(2026, 6, 7, 6, 0, 0);

    public DateTime NowLocal
    {
        get => _now;
        set
        {
            if (_now == value)
            {
                return;
            }

            _now = value;
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? StatusChanged;
}