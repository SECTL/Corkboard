using System;
using System.Reflection;
using Avalonia;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Run = AvaloniaRichEditor.Documents.Run;

namespace Corkboard.Core.Controls;

/// <summary>
///     AvaloniaRichEditor 1.2.1 的超链接能力补齐：库本身能渲染、能导出 <c>&lt;a href&gt;</c>，
///     但**没有公开**「读选区 / 给选区套链接 / 按坐标找链接」的 API——
///     <c>SetHyperlink(string?, Run?)</c>、<c>GetLinkRunAtPoint(Point)</c> 与选区字段
///     <c>_selectionStart</c> / <c>_selectionEnd</c> 都是 private，而 <c>TextPointer</c> 是公开类型。
///     这里只对这三样做反射，把能力收在一处：
///     <list type="bullet">
///         <item>给选中文字套链接（<see cref="TrySetHyperlink" />，<c>url</c> 传 null 就是移除）；</item>
///         <item>判断当前有没有选中文字（<see cref="HasSelection" />）；</item>
///         <item>只读卡片上按坐标命中链接（<see cref="GetLinkUriAt" />）；</item>
///         <item>编辑 / 移除点中的那个链接（<see cref="TrySetHyperlinkAt" />）。</item>
///     </list>
///     <para>
///         ⚠️ 升级 AvaloniaRichEditor 时必须先跑一遍 <c>RichTextLinks.IsSupported</c>：
///         任一样反射不到就整套功能静默降级（按钮不出现、卡片点击照常当普通点击），
///         **不要**在没有兜底的情况下改成直接调用。反射失败一律吞掉：这条路径挂在悬停与点击上，
///         不该因为库换了字段名就把整块作业板弄崩。
///     </para>
/// </summary>
internal static class RichTextLinks
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;

    private static readonly FieldInfo? SelectionStartField =
        typeof(RichEditor).GetField("_selectionStart", Instance);

    private static readonly FieldInfo? SelectionEndField =
        typeof(RichEditor).GetField("_selectionEnd", Instance);

    private static readonly FieldInfo? CaretPositionField =
        typeof(RichEditor).GetField("_caretPosition", Instance);

    private static readonly MethodInfo? SetHyperlinkMethod = FindMethod(
        "SetHyperlink", typeof(string), typeof(Run));

    private static readonly MethodInfo? GetLinkRunAtPointMethod = FindMethod(
        "GetLinkRunAtPoint", typeof(Point));

    private static readonly MethodInfo? GetPositionFromPointMethod = FindMethod(
        "GetPositionFromPoint", typeof(Point));

    /// <summary>这一版库的私有成员是否都还在；缺一样就用不了超链接增强。</summary>
    public static bool IsSupported =>
        SelectionStartField is not null
        && SelectionEndField is not null
        && SetHyperlinkMethod is not null
        && GetLinkRunAtPointMethod is not null;

    /// <summary>
    ///     「点已有链接 → 编辑 / 移除」还需要另外两样私有成员：按坐标取 <c>TextPointer</c>
    ///     （用来把选区收拢到点击处）和光标字段。缺了只关掉这条增强，不影响插入按钮与卡片点击。
    /// </summary>
    public static bool IsEditSupported =>
        IsSupported
        && CaretPositionField is not null
        && GetPositionFromPointMethod is not null;

    /// <summary>当前是否有选中文字（库的 <c>hasSelection</c> 判据：两个端点都在段落里且不相等）。</summary>
    public static bool HasSelection(RichEditor editor)
    {
        if (SelectionStartField?.GetValue(editor) is not TextPointer start
            || SelectionEndField?.GetValue(editor) is not TextPointer end)
        {
            return false;
        }

        return start.Paragraph is not null
               && end.Paragraph is not null
               && start.CompareTo(end) != 0;
    }

    /// <summary>把链接套到当前选区上（<paramref name="url" /> 为 null 表示去掉链接）。</summary>
    public static bool TrySetHyperlink(RichEditor editor, string? url)
    {
        if (SetHyperlinkMethod is null || editor.IsReadOnly || !HasSelection(editor))
            return false;

        try
        {
            // 第二个参数是「没有选区时直接改哪个 Run」，我们有选区，传 null 即可。
            SetHyperlinkMethod.Invoke(editor, [url, null]);
            return true;
        }
        catch (Exception)
        {
            // 见类注释：反射 / 库内部状态不对时只降级，不把异常抛到 UI 线程。
            return false;
        }
    }

    /// <summary>坐标处是链接的话返回它的地址，否则返回 null。</summary>
    public static string? GetLinkUriAt(RichEditor editor, Point position)
    {
        if (GetLinkRunAtPointMethod is null)
            return null;

        try
        {
            var run = GetLinkRunAtPointMethod.Invoke(editor, [position]) as Run;
            var uri = run?.NavigateUri;
            return string.IsNullOrWhiteSpace(uri) ? null : uri;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    ///     修改（<paramref name="url" /> 为 null 则移除）坐标处那个链接。
    ///     <para>
    ///         不能直接调 <c>SetHyperlink(url, null)</c>：库的实现是「有选区优先改选区」，
    ///         而用户点链接时编辑器里可能还留着别处的选区，那样会改错文字。
    ///         所以先把选区收拢到点击位置（和普通单击的效果一致），再把命中的 <c>Run</c> 作为第二个参数传进去。
    ///     </para>
    /// </summary>
    public static bool TrySetHyperlinkAt(RichEditor editor, Point position, string? url)
    {
        if (!IsEditSupported || editor.IsReadOnly)
            return false;

        try
        {
            if (GetLinkRunAtPointMethod!.Invoke(editor, [position]) is not Run run)
                return false;

            if (GetPositionFromPointMethod!.Invoke(editor, [position]) is not TextPointer pointer
                || pointer.Paragraph is null)
            {
                return false;
            }

            // 收拢选区：两端各自新建指针（TextPointer 的 Offset 是可变的，不能共用一个实例）。
            SelectionStartField!.SetValue(editor, new TextPointer(pointer.Paragraph, pointer.Offset));
            SelectionEndField!.SetValue(editor, new TextPointer(pointer.Paragraph, pointer.Offset));
            CaretPositionField!.SetValue(editor, new TextPointer(pointer.Paragraph, pointer.Offset));

            SetHyperlinkMethod!.Invoke(editor, [url, run]);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    ///     按名字 + 参数类型找私有方法。库的方法签名带可空标注，反射只认运行时类型，所以按
    ///     <see cref="Type" /> 逐个比对而不是写死参数个数。
    /// </summary>
    private static MethodInfo? FindMethod(string name, params Type[] parameterTypes)
    {
        foreach (var method in typeof(RichEditor).GetMethods(Instance))
        {
            if (!string.Equals(method.Name, name, StringComparison.Ordinal))
                continue;

            var parameters = method.GetParameters();
            if (parameters.Length != parameterTypes.Length)
                continue;

            var matches = true;
            for (var i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].ParameterType != parameterTypes[i])
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
                return method;
        }

        return null;
    }
}
