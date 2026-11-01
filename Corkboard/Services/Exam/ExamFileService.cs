using System.Text;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Corkboard.Core.Exam;
using Microsoft.Extensions.Logging;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.Services.Exam;

/// <summary>
///     考试文件的导入导出：把看板里的一场（或几场）考试存成 <c>.cbexam</c>，或者从这种文件读回来。
///     <para>
///         文件格式本身（外壳、版本、容错、收拾脏数据）全在 Core 的
///         <see cref="ExamEntryFile" /> 里；这里只负责「弹文件对话框 + 读写字节」这两件必须靠
///         Avalonia 平台能力做的事。
///     </para>
///     <para>
///         约定与作业板导出保持一致：用户取消对话框返回 <see langword="false" /> / <see langword="null" />；
///         取消之外的失败一律抛 <see cref="InvalidOperationException" />（调用方显示提示）；
///         必须在 UI 线程上调用（文件对话框要求）。
///     </para>
/// </summary>
public sealed class ExamFileService(ILogger<ExamFileService> logger)
{
    /// <summary>考试文件类型（保存 / 打开对话框里的「文件类型」下拉）。</summary>
    private static readonly FilePickerFileType ExamFileType = new(CR.Exam_File_FileType)
    {
        Patterns = [$"*.{ExamEntryFile.FileExtension}"],
        MimeTypes = ["application/json"]
    };

    /// <summary>
    ///     导出**一场**考试：弹保存对话框，把那一场写成一份 <c>.cbexam</c>。
    /// </summary>
    /// <returns>写盘成功为 <see langword="true" />；用户取消为 <see langword="false" />。</returns>
    public Task<bool> ExportExamAsync(
        ExamEntry exam,
        string? boardTitle,
        TopLevel topLevel,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(exam);

        return ExportExamsAsync([exam], boardTitle, ExamEntryFile.BuildSuggestedFileName(exam), topLevel,
            cancellationToken);
    }

    /// <summary>
    ///     导出**一整块看板**的考试：弹保存对话框，把里面的全部考试写成一份 <c>.cbexam</c>。
    ///     <para>
    ///         与单场导出走同一份格式（外壳里本来就是一串考试），所以导出来的文件既能只导回一场、
    ///         也能整份导回来。
    ///     </para>
    /// </summary>
    /// <returns>写盘成功为 <see langword="true" />；用户取消为 <see langword="false" />。</returns>
    public Task<bool> ExportBoardAsync(
        ExamBoardDocument board,
        TopLevel topLevel,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(board);

        var exams = board.Exams ?? [];
        return ExportExamsAsync(exams, board.ResolveTitle(), ExamEntryFile.BuildSuggestedFileName(board), topLevel,
            cancellationToken);
    }

    /// <summary>
    ///     导出一批考试（单场与整块看板都走这里）：弹保存对话框、把清单写成一份 <c>.cbexam</c>。
    /// </summary>
    private async Task<bool> ExportExamsAsync(
        IReadOnlyList<ExamEntry> exams,
        string? boardTitle,
        string suggestedFileName,
        TopLevel topLevel,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(topLevel);
        cancellationToken.ThrowIfCancellationRequested();

        IStorageFile? file;
        try
        {
            file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = CR.Exam_File_SaveTitle,
                SuggestedFileName = suggestedFileName,
                DefaultExtension = ExamEntryFile.FileExtension,
                ShowOverwritePrompt = true,
                FileTypeChoices = [ExamFileType]
            }).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "打开导出考试的保存对话框失败");
            throw new InvalidOperationException(CR.Exam_File_ExportFailed, exception);
        }

        if (file is null)
            return false;

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var content = ExamEntryFile.Create(exams, boardTitle, DateTime.Now).ToJson();

            await using var stream = await file.OpenWriteAsync().ConfigureAwait(true);
            if (stream.CanSeek)
            {
                stream.SetLength(0);
                stream.Position = 0;
            }

            // 不带 BOM：文件是给人看的 JSON，带 BOM 的话记事本之外的工具会多出一个字符。
            var bytes = new UTF8Encoding(false).GetBytes(content);
            await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(true);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(true);

            logger.LogInformation("考试已导出：{FileName}（{Count} 场）", file.Name, exams.Count);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "导出考试失败");
            throw new InvalidOperationException(CR.Exam_File_ExportFailed, exception);
        }
    }

    /// <summary>
    ///     导入考试：弹打开对话框，读回来一份「能直接存进看板」的清单。
    /// </summary>
    /// <returns>用户取消时为 <see langword="null" />；否则是清单（可能为空，交给调用方提示）。</returns>
    public async Task<IReadOnlyList<ExamEntry>?> ImportExamsAsync(
        TopLevel topLevel,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(topLevel);
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<IStorageFile> files;
        try
        {
            files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = CR.Settings_Exam_Schedule_Import,
                AllowMultiple = false,
                FileTypeFilter = [ExamFileType]
            }).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "打开导入考试的对话框失败");
            throw new InvalidOperationException(CR.Exam_File_ImportFailed, exception);
        }

        if (files.Count == 0)
            return null;

        var file = files[0];

        try
        {
            string content;
            await using (var stream = await file.OpenReadAsync().ConfigureAwait(true))
            using (var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
            {
                content = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(true);
            }

            if (!ExamEntryFile.TryParse(content, out var parsed) || parsed is null)
            {
                logger.LogWarning("导入的考试文件认不出来：{FileName}", file.Name);
                throw new InvalidOperationException(CR.Exam_File_ImportFailed);
            }

            var exams = parsed.ResolveImportableExams();
            logger.LogInformation("考试文件已读取：{FileName}（{Count} 场）", file.Name, exams.Count);
            return exams;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "读取考试文件失败");
            throw new InvalidOperationException(CR.Exam_File_ImportFailed, exception);
        }
    }
}
