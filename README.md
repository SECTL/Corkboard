<div align="center">

# Corkboard

**基于 Avalonia 的 .NET 桌面应用骨架，由 SecRandom-C 的工程框架移植而来**

</div>

## 这是什么

Corkboard 是一个**框架优先**的项目：它把 [SecRandom-C](../SecRandom-C) 里与业务无关的那部分工程结构
——分层、依赖注入、配置管道、日志、导航页面注册、本地化、主题、平台能力抽象——原样搬了过来，
业务只留了一个占位的 `Board` 模块。真实赛道确定后，替换 `Board` 即可，框架不需要重做。

> 以 GNU GPLv3 发布。它派生自 GPLv3 的 SecRandom-C，因此再发布同样必须遵循 GPLv3（见 [LICENSE](LICENSE)）。

## 现在的状态

| 能力 | 状态 |
| --- | --- |
| .NET 10 + Avalonia 12 + FluentAvalonia 3 桌面骨架，能编译、能启动 | ✅ |
| 分层：`Shared` → `Core` → `Corkboard`（应用层）→ `Desktop`（入口） | ✅ |
| Microsoft.Extensions.Hosting DI + `IAppHost` 静态服务定位 | ✅ |
| 配置管道：JSON / snake_case / 原子写入 / 属性变化自动落盘 / 旧版字段迁移 | ✅ |
| 文件日志（每次启动一个新日志文件、历史日志压缩与保留期清理） | ✅ |
| 导航页面注册表 + 键控 DI 页面工厂（`AddMainPage` / `AddSettingsPage` / `AddGroup`） | ✅ |
| 本地化：`Langs/<页面>/Resources.resx` + 多语言切换 | ✅ |
| 主题：跟随系统 / 亮 / 暗 + 自定义主题色 | ✅ |
| 平台抽象：`Platforms.Abstractions` + `Platforms` + Windows/Linux/macOS 实现边界 | ✅ |
| SECTL 账号登录（OAuth 2.0 PKCE + loopback 回调 + 令牌原子持久化 + 心跳） | ✅ 已填正式客户端 ID |
| SECTL 版本使用人数上报（`POST /api/stats/version`，每次启动一次，尽力而为） | ✅ |
| ClassIsland IPC 通知通道（进程间通知，失败可降级） | ✅ |
| Board 占位业务：便签增删改、置顶、落盘 | ✅ 占位 |
| xUnit v3 测试（页面注册、配置序列化、Board 服务） | ✅ |
| CI（构建 + 测试） | ✅ |
| 视图引擎（多窗口会话宿主）、插件 SDK、移动端头、图标源生成器、发布签名 | ⛔ 未移植，见 [docs/backlog.md](docs/backlog.md) |

## 目录结构

```
Corkboard/
├── Corkboard/                  # 应用层：App 宿主、窗口、视图、ViewModel、应用服务（Auth/Ipc）
├── Corkboard.Core/             # 核心：契约、配置/日志运行时、领域服务、可复用控件与样式
├── Corkboard.Shared/           # 跨工程共享：配置基类、路径工具（不允许 UI 依赖）
├── Corkboard.Desktop/          # 可执行入口：Avalonia 启动 + 平台选择
├── Corkboard.Platforms*/       # 平台抽象与 Windows/Linux/macOS 原生实现边界
├── Corkboard4Ci.Interface/     # ClassIsland 插件侧 IPC 契约
├── Corkboard.Core.Tests/       # xUnit v3 测试
├── docs/                       # 工程规则、导航、本地化、待办
├── Global.props                # 共享 MSBuild 策略，被各工程导入
├── Directory.Build.props       # Avalonia 版本锚点
└── Platforms.props             # 平台选择与 CORKBOARD_PLATFORM_* 宏
```

## 构建与运行

需要 .NET SDK 10（见 `global.json`）。

```powershell
dotnet build Corkboard.sln
dotnet test Corkboard.Core.Tests\Corkboard.Core.Tests.csproj
dotnet run --project Corkboard.Desktop\Corkboard.Desktop.csproj
```

跨平台发布示例：

```powershell
dotnet publish Corkboard.Desktop\Corkboard.Desktop.csproj -c Release -r win-x64
```

数据目录规则（与上游一致）：安装版默认写在可执行文件旁的 `data/`，那里不可写时退回
`%LOCALAPPDATA%\Corkboard\data`；便携包（`Corkboard.package.json` 标记 + `app-*` 目录）必须留在原地。

## SECTL 集成的两个标识

两个值**不是同一个东西**（上游 SecRandom 恰好同值，本项目不是），不要互相替代：

| 用途 | 值 | 定义位置 |
| --- | --- | --- |
| OAuth 客户端 ID（授权码换令牌、刷新令牌） | `6ac3d35400241eff9fbe` | [SectlAuthEndpoints.cs](Corkboard/Services/Auth/SectlAuthEndpoints.cs) `ClientId` |
| 平台标识 `platform_id`（服务端统计/版本上报的归并维度） | `platform_29648fc4ac3ba07d` | [GlobalConstants.cs](Corkboard.Core/GlobalConstants.cs) `PlatformId` |

约束：

- 登录只在 `SectlAuthEndpoints` 里读客户端 ID，统计只在 `GlobalConstants.PlatformId` 里读平台标识；
  两边都不许再出现第二处硬编码。
- 版本上报的载荷只带 `platform_id` / `version` / `device_uuid`，**从不**带 SECTL 账号 ID，
  因此版本分布无法与账号关联。规则校验在
  [VersionUsageReportPayload.cs](Corkboard.Core/Services/Stats/VersionUsageReportPayload.cs)（纯函数、有测试）。
- 客户端注册时需要在 SECTL 侧登记 loopback 重定向 `http://localhost:<动态端口>/callback`。

## 换成你的赛道时要动哪里

1. **业务**：删掉 `Corkboard.Core/Services/Board/`、`Models/Board/`、`ViewModels/MainPages/BoardPageViewModel.cs`、
   `Views/MainPages/BoardPage.axaml(.cs)`，按同样结构放新模块。
2. **导航**：改 `App.BuildHost` 里的 `AddMainPage` / `AddSettingsPage` / `AddGroup`，以及 `AppConsts.DefaultMainPageId`。
3. **命名**：`GlobalConstants.AppName`、`AppConsts`、协议 scheme、`Corkboard.package.json`、资源命名空间。
4. **标识**：OAuth 客户端 ID 在 `Corkboard/Services/Auth/SectlAuthEndpoints.cs`，
   平台标识 `platform_id` 在 `Corkboard.Core/GlobalConstants.cs` —— 换赛道（= 换 SECTL 平台）时两个都要改。

## 文档

- [AGENTS.md](AGENTS.md)：给 AI/新人的工程知识库（结构、代码地图、约定、命令）。
- [docs/project_rules.md](docs/project_rules.md)：分层与依赖方向、DI、配置、本地化等硬约定。
- [docs/navigation.md](docs/navigation.md)：加一个页面需要改哪几处。
- [docs/localization.md](docs/localization.md)：资源目录约定与切换语言的方式。
- [docs/backlog.md](docs/backlog.md)：尚未移植的上游能力清单及优先级。

## 许可证

GNU GPLv3，见 [LICENSE](LICENSE)。第三方组件说明见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。
