using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Corkboard.Core.Enums;
using Corkboard.Core.Models.Board;
using BoardFieldValueParser = Corkboard.Core.Models.Board.BoardFieldValueParser;

namespace Corkboard.ViewModels.MainPages;

/// <summary>
///     新建作业时一个字段的输入草稿。子类与字段的取值类型一一对应，
///     页面的 <c>ItemsControl.DataTemplates</c> 按具体类型挑模板，所以子类都是 sealed、不互相继承。
/// </summary>
public abstract class BoardFieldDraft : ObservableObject
{
    protected BoardFieldDraft(BoardTypeField field)
    {
        Field = field;
    }

    public BoardTypeField Field { get; }

    public string Label => Field.DisplayLabel;

    /// <summary>用户填了东西没有。空字段不写进作业。</summary>
    public abstract bool HasValue { get; }

    /// <summary>写进 <see cref="BoardNote.Values" /> 的原始值文本。</summary>
    public abstract string RawValue { get; }

    /// <summary>
    ///     编辑已有作业时把存下的原始值文本填回输入框。解析不了的值按「没填」处理，
    ///     不抛异常——老数据里可能有手改过的内容。
    /// </summary>
    public abstract void LoadRawValue(string raw);

    protected void NotifyValueChanged()
    {
        OnPropertyChanged(nameof(HasValue));
        OnPropertyChanged(nameof(RawValue));
    }
}

/// <summary>单行文本字段。</summary>
public sealed partial class BoardTextFieldDraft : BoardFieldDraft
{
    public BoardTextFieldDraft(BoardTypeField field) : base(field)
    {
    }

    [ObservableProperty] private string _text = string.Empty;

    public override bool HasValue => !string.IsNullOrWhiteSpace(Text);

    public override string RawValue => Text.Trim();

    public override void LoadRawValue(string raw)
    {
        Text = raw ?? string.Empty;
    }

    partial void OnTextChanged(string value)
    {
        NotifyValueChanged();
    }
}

/// <summary>多行文本字段。</summary>
public sealed partial class BoardParagraphFieldDraft : BoardFieldDraft
{
    public BoardParagraphFieldDraft(BoardTypeField field) : base(field)
    {
    }

    [ObservableProperty] private string _text = string.Empty;

    public override bool HasValue => !string.IsNullOrWhiteSpace(Text);

    public override string RawValue => Text.Trim();

    public override void LoadRawValue(string raw)
    {
        Text = raw ?? string.Empty;
    }

    partial void OnTextChanged(string value)
    {
        NotifyValueChanged();
    }
}

/// <summary>单个数字字段，输入用步进器。</summary>
public sealed partial class BoardNumberFieldDraft : BoardFieldDraft
{
    public BoardNumberFieldDraft(BoardTypeField field) : base(field)
    {
    }

    [ObservableProperty] private decimal? _value;

    public override bool HasValue => Value.HasValue;

    public override string RawValue => Value?.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty;

    public override void LoadRawValue(string raw)
    {
        Value = BoardFieldValueParser.ParseNumber(raw);
    }

    partial void OnValueChanged(decimal? value)
    {
        NotifyValueChanged();
    }
}

/// <summary>
///     页数区间字段：起页与止页两个数字，输入用两个步进器，显示成一个整体。
///     原始值存成 <c>12-15</c>，格式化留给显示时做，数据层留得住结构。
/// </summary>
public sealed partial class BoardPageRangeFieldDraft : BoardFieldDraft
{
    public BoardPageRangeFieldDraft(BoardTypeField field) : base(field)
    {
    }

    [ObservableProperty] private decimal? _from;
    [ObservableProperty] private decimal? _to;

    public override bool HasValue => From.HasValue || To.HasValue;

    public override string RawValue =>
        $"{(From.HasValue ? From.Value.ToString("0.##", CultureInfo.InvariantCulture) : string.Empty)}-" +
        $"{(To.HasValue ? To.Value.ToString("0.##", CultureInfo.InvariantCulture) : string.Empty)}";

    public override void LoadRawValue(string raw)
    {
        var (from, to) = BoardFieldValueParser.SplitPageRange(raw);
        From = from;
        To = to;
    }

    partial void OnFromChanged(decimal? value)
    {
        NotifyValueChanged();
    }

    partial void OnToChanged(decimal? value)
    {
        NotifyValueChanged();
    }
}

/// <summary>按字段定义造出对应的输入草稿。</summary>
public static class BoardFieldDraftFactory
{
    public static BoardFieldDraft Create(BoardTypeField field)
    {
        return field.Kind switch
        {
            BoardFieldKind.Paragraph => new BoardParagraphFieldDraft(field),
            BoardFieldKind.Number => new BoardNumberFieldDraft(field),
            BoardFieldKind.PageRange => new BoardPageRangeFieldDraft(field),
            _ => new BoardTextFieldDraft(field)
        };
    }
}
