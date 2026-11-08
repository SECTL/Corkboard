namespace Corkboard.Core.Actions;

/// <summary>
///     行动数据在磁盘上的位置（<c>action/rules.json</c> / <c>action/state.json</c> /
/// <c>action/runs/yyyy-MM-dd.jsonl</c>）。
///     <para>
///         根目录由 App 层用 <c>Utils.GetDirectoryPath("action")</c> 给出（那个方法顺手建目录）；
///         这里只算路径，<b>不</b>负责创建 —— 也正因为如此，单测可以塞一个临时目录进来。
///     </para>
/// </summary>
public sealed class ActionStorePaths
{
    /// <summary>按数据根建一份路径表。</summary>
    /// <param name="root"><c>action/</c> 目录的绝对路径。</param>
    public ActionStorePaths(string root)
    {
        if (string.IsNullOrWhiteSpace(root))
            throw new ArgumentException("行动数据的根目录不能为空", nameof(root));

        Root = root;
    }

    /// <summary>行动数据根目录。</summary>
    public string Root { get; }

    /// <summary>规则文件：<c>rules.json</c>。</summary>
    public string RulesFile => Path.Combine(Root, "rules.json");

    /// <summary>状态文件：<c>state.json</c>（水位线 + 抑制表）。</summary>
    public string StateFile => Path.Combine(Root, "state.json");

    /// <summary>执行记录目录：<c>runs/</c>。</summary>
    public string RunsFolder => Path.Combine(Root, "runs");

    /// <summary>某一天的执行记录文件：<c>runs/yyyy-MM-dd.jsonl</c>。</summary>
    public string RunFile(DateOnly date) => Path.Combine(RunsFolder, $"{date:yyyy-MM-dd}.jsonl");
}
