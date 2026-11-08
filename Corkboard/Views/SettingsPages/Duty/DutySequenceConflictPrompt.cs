using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Corkboard.ViewModels.SettingsPages.Duty;
using FluentAvalonia.UI.Controls;

namespace Corkboard.Views.SettingsPages.Duty;

/// <summary>序号冲突弹窗的文案表（照 SR 的 <c>SequenceNumberConflictText</c>）。</summary>
internal sealed record DutySequenceConflictText(
    string Title,
    string ShiftContent,
    string NotNumericContent,
    string DuplicatedContent,
    string PreviewHeader,
    string PreviewLine,
    string PreviewEllipsis,
    string ShiftPrimary,
    string AppendSecondary,
    string KeepDuplicatePrimary,
    string Close,
    string UnnamedMember);

/// <summary>
///     新增成员时序号重复的四档确认框：顺延并插入 / 追加到末尾 / 保留重复并添加 / 返回修改。
///     <para>
///         照 SR-C 的 <c>SequenceNumberConflictPrompt</c> 搬过来，只把文案换成中文硬编码。
///         一句话的确认框走 <see cref="FAContentDialog" />（Corkboard 规范：表单用抽屉、一句话用对话框）。
///     </para>
/// </summary>
internal static class DutySequenceConflictPrompt
{
    /// <summary>默认中文文案。</summary>
    public static DutySequenceConflictText CreateDefaultText() => new(
        "序号重复",
        "序号「{0}」已经被「{1}」占用。可以把「{1}」及其后的 {2} 名成员的序号各加 1，把新成员插到「{1}」的位置；" +
        "也可以让新成员用序号「{3}」追加到末尾。\n\n{4}",
        "序号「{0}」已经被「{1}」占用，而且「{0}」不是纯数字，没法自动顺延。可以保留重复序号直接添加，或者返回修改。",
        "名单里已经有 {1} 名成员用序号「{0}」，自动顺延消除不了重复。可以保留重复序号直接添加，或者返回修改。",
        "顺延预览",
        "{0}. {1} → {2}",
        "……",
        "顺延并插入",
        "追加到末尾",
        "保留重复并添加",
        "返回修改",
        "（未命名）");

    /// <summary>弹框并把用户的选择翻回 <see cref="DutySequenceConflictChoice" />。</summary>
    public static async Task<DutySequenceConflictChoice> ShowAsync(
        TopLevel owner,
        DutySequenceConflict conflict,
        IReadOnlyList<string> displayNames,
        DutySequenceConflictText text)
    {
        var collidingIndex = conflict.DuplicateIndices.Count > 0
            ? conflict.DuplicateIndices[0]
            : conflict.InsertIndex;

        var dialog = new FAContentDialog
        {
            Title = text.Title,
            DefaultButton = FAContentDialogButton.Close
        };

        switch (conflict.Kind)
        {
            case DutySequenceConflictKind.Shiftable:
                dialog.Content = BuildContent(string.Format(
                    CultureInfo.CurrentCulture,
                    text.ShiftContent,
                    conflict.NewId,
                    ResolveName(displayNames, collidingIndex, text.UnnamedMember),
                    conflict.ShiftEntries.Count,
                    conflict.AppendNumber?.ToString(CultureInfo.InvariantCulture) ?? conflict.NewId,
                    BuildPreview(conflict, displayNames, text)));
                dialog.PrimaryButtonText = text.ShiftPrimary;
                dialog.SecondaryButtonText = text.AppendSecondary;
                dialog.CloseButtonText = text.Close;
                break;

            case DutySequenceConflictKind.AlreadyDuplicated:
                dialog.Content = BuildContent(string.Format(
                    CultureInfo.CurrentCulture,
                    text.DuplicatedContent,
                    conflict.NewId,
                    conflict.DuplicateIndices.Count));
                dialog.PrimaryButtonText = text.KeepDuplicatePrimary;
                dialog.CloseButtonText = text.Close;
                break;

            default:
                dialog.Content = BuildContent(string.Format(
                    CultureInfo.CurrentCulture,
                    text.NotNumericContent,
                    conflict.NewId,
                    ResolveName(displayNames, collidingIndex, text.UnnamedMember)));
                dialog.PrimaryButtonText = text.KeepDuplicatePrimary;
                dialog.CloseButtonText = text.Close;
                break;
        }

        var result = await dialog.ShowAsync(owner);

        if (conflict.Kind != DutySequenceConflictKind.Shiftable)
        {
            return result == FAContentDialogResult.Primary
                ? DutySequenceConflictChoice.KeepDuplicate
                : DutySequenceConflictChoice.EditAgain;
        }

        return result switch
        {
            FAContentDialogResult.Primary => DutySequenceConflictChoice.ShiftAndInsert,
            FAContentDialogResult.Secondary => DutySequenceConflictChoice.AppendAtEnd,
            _ => DutySequenceConflictChoice.EditAgain
        };
    }

    /// <summary>顺延预览：表头 + 最多 4 行；超出时前 3 行 + 省略号 + 最后一行。</summary>
    private static string BuildPreview(
        DutySequenceConflict conflict,
        IReadOnlyList<string> displayNames,
        DutySequenceConflictText text)
    {
        if (conflict.ShiftEntries.Count == 0)
            return string.Empty;

        var entries = conflict.ShiftEntries
            .OrderBy(entry => entry.OldNumber)
            .ToList();

        var builder = new StringBuilder(text.PreviewHeader);
        void AppendLine(DutySequenceShiftEntry entry)
        {
            builder.Append('\n');
            builder.Append(string.Format(
                CultureInfo.CurrentCulture,
                text.PreviewLine,
                entry.OldId,
                ResolveName(displayNames, entry.Index, text.UnnamedMember),
                entry.NewNumber.ToString(CultureInfo.InvariantCulture)));
        }

        if (entries.Count <= 4)
        {
            foreach (var entry in entries)
                AppendLine(entry);

            return builder.ToString();
        }

        foreach (var entry in entries.Take(3))
            AppendLine(entry);

        builder.Append('\n').Append(text.PreviewEllipsis);
        AppendLine(entries[^1]);
        return builder.ToString();
    }

    private static string ResolveName(IReadOnlyList<string> displayNames, int index, string unnamedMember)
    {
        if (index < 0 || index >= displayNames.Count)
            return unnamedMember;

        var name = displayNames[index]?.Trim() ?? string.Empty;
        return name.Length == 0 ? unnamedMember : name;
    }

    private static TextBlock BuildContent(string text) => new()
    {
        Text = text,
        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        MaxWidth = 460
    };
}