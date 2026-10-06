using Microsoft.Extensions.Logging;
using Corkboard.Core.Abstraction;
using Corkboard.Core.Models;

namespace Corkboard.Core.Services.Config;

/// <summary>主配置句柄：全应用读写 <c>settings.json</c> 的唯一入口。</summary>
public class MainConfigHandler : ConfigHandlerBase<MainConfigModel>
{
    public MainConfigHandler(ILogger<MainConfigHandler> logger, ConfigServiceBase configService)
        : base(logger, configService, () => new MainConfigModel())
    {
    }
}
