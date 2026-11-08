using Corkboard.Core.Actions;
using Corkboard.Core.Actions.Schedule;
using Corkboard.Shared;
using Corkboard.Services.Actions.Schedule;
using Corkboard.Services.Actions.Targets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Corkboard.Services.Actions;

/// <summary>
///     行动域在 App 宿主里的唯一装配点（设计文档 §8.2）：
///     存储、看板目标、执行器、调度器与三个日程源一次注册完。
///     <para>
///         调用方只需要在 <c>App.BuildHost</c> 里写一行 <c>services.AddActionServices();</c>；
///         别在别处再 new 这些服务，也别在这里注册 ViewModel（那是界面的键控 DI）。
///     </para>
/// </summary>
public static class ActionServiceCollectionExtensions
{
    /// <summary>注册行动窗口 / 日程中心需要的全部后台服务。</summary>
    public static IServiceCollection AddActionServices(this IServiceCollection services)
    {
        // 数据根：action/rules.json + state.json + runs/yyyy-MM-dd.jsonl。
        services.AddSingleton(new ActionStorePaths(Utils.GetDirectoryPath("action")));
        services.AddSingleton<ActionRuleStore>();
        services.AddSingleton<ActionStateStore>();
        services.AddSingleton<ActionRunStore>();

        // 三块看板：顺序就是列表 / 下拉框的显示顺序（作业板 → 考试看板 → 看板模式）。
        services.AddSingleton<IBoardTarget, HomeworkBoardTarget>();
        services.AddSingleton<IBoardTarget, ExamBoardTarget>();
        services.AddSingleton<IBoardTarget, DisplayBoardTarget>();
        services.AddSingleton<IBoardTargetRegistry, BoardTargetRegistry>();

        // 唯一执行出口 + 自定义行动的调度器。
        services.AddSingleton<ActionExecutor>();
        services.AddHostedService<ActionSchedulerHostedService>();

        // 日程源：自定义行动 + 两路系统日程（考试放映、看板模式）。
        services.AddSingleton<IScheduleSource, CustomActionScheduleSource>();
        services.AddSingleton<IScheduleSource, ExamAutoShowScheduleSource>();
        services.AddSingleton<IScheduleSource, DisplayAutoShowScheduleSource>();
        services.AddSingleton<IScheduleSourceRegistry, ScheduleSourceRegistry>();

        return services;
    }
}
