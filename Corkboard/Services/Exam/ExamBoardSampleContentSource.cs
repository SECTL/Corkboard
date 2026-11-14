using System.Collections.ObjectModel;
using Corkboard.Core.Exam;
using Corkboard.Core.Langs.Common;

namespace Corkboard.Services.Exam;

/// <summary>
///     布局编辑窗用的示例内容源：固定 2026-06-07 的三场考试，不读写真实看板文件。
///     科目文案走公共资源，跟真实看板一样受界面语言影响。
/// </summary>
public sealed class ExamBoardSampleContentSource : IExamBoardContentSource
{
    private readonly ExamBoardDocument _board;
    private readonly ReadOnlyObservableCollection<ExamEntry> _exams;

    public ExamBoardSampleContentSource()
    {
        var date = new DateOnly(2026, 6, 7);
        var exams = new ObservableCollection<ExamEntry>
        {
            new()
            {
                Subject = Resources.Exam_Layout_Sample_Subject_Chinese,
                Date = date,
                StartTime = new TimeOnly(8, 0),
                EndTime = new TimeOnly(10, 0)
            },
            new()
            {
                Subject = Resources.Exam_Layout_Sample_Subject_Math,
                Date = date,
                StartTime = new TimeOnly(10, 30),
                EndTime = new TimeOnly(12, 0),
                PaperCount = 2,
                PageCount = 8,
                QuestionCount = 23
            },
            new()
            {
                Subject = Resources.Exam_Layout_Sample_Subject_English,
                Date = date,
                StartTime = new TimeOnly(14, 0),
                EndTime = new TimeOnly(16, 0)
            }
        };

        _exams = new ReadOnlyObservableCollection<ExamEntry>(exams);
        _board = new ExamBoardDocument
        {
            Title = Resources.Exam_Layout_Sample_Title,
            Subtitle = Resources.Exam_Layout_Sample_Subtitle,
            Exams = exams.ToList()
        };
    }

    public ExamBoardDocument ActiveBoard => _board;

    public ReadOnlyObservableCollection<ExamEntry> Exams => _exams;

    public event EventHandler? Changed
    {
        add { }
        remove { }
    }
}