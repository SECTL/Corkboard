using Corkboard.Core.Handwriting;

namespace Corkboard.ViewModels.MainPages.Board;

/// <summary>
///     从手写作业的落盘 JSON 里取出画布高宽比（高 / 宽），给卡片上的
///     <c>HandwritingPreview</c> 定高用。
/// </summary>
/// <remarks>
///     解码统一走 Core 的 <see cref="HandwritingCodec" />，不要在这里另写一套 JSON 解析：
///     笔迹格式以后要加版本、换字段，兜底解析会和真格式悄悄对不上。
///     解不出来时退回 <see cref="HandwritingDocument.DefaultAspect" />（与预览控件的默认值一致），
///     卡片不会出现 0 高度。
/// </remarks>
internal static class BoardHandwritingAspect
{
    /// <summary>常见的横版便签比例（与 <c>HandwritingPreview.Aspect</c> 的默认值一致）。</summary>
    public const double DefaultAspect = HandwritingDocument.DefaultAspect;

    /// <summary>解析笔迹 JSON 里的高宽比；解析不了时返回 <see cref="DefaultAspect" />。</summary>
    public static double Resolve(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return DefaultAspect;

        return HandwritingCodec.TryDecode(json, out var document)
            ? HandwritingDocument.ClampAspect(document.Aspect)
            : DefaultAspect;
    }
}
