# PROJECT KNOWLEDGE BASE

<!--
维护契约：
- 保持这份文件“耐久”：写稳定约定，不写某台机器上的临时观察。
- 改动项目结构、目标框架、本地化布局、工作流、导航/DI 规则或模块边界后，
  必须在同一次改动里更新本文件（以及受影响的嵌套 AGENTS.md）。
- 根目录规则对整个仓库生效；子目录 AGENTS.md 补充子树规则。
- 约定冲突时，`docs/project_rules.md` 是最终依据。
-->

**Last Update:** 2026-01（骨架首版，移植自 SecRandom-C）

## OVERVIEW

Corkboard 是 GPLv3 的 C#/.NET 桌面应用骨架：技术栈为 .NET 10 + Avalonia 12 + FluentAvalonia 3 +
Microsoft.Extensions.Hosting DI + xUnit v3。业务目前只有占位的 Board 模块。

它移植自 SecRandom-C 的**工程框架**（不是业务代码）：分层、DI、配置、日志、导航、本地化、平台抽象。
改业务时不要动框架层；改框架时优先回看上游同名实现，避免无谓分叉。

## STRUCTURE

```
Corkboard/
├── Corkboard/                  # 应用层：App 宿主、视图/ViewModel、应用服务（Auth、Ipc）、Langs、Assets
├── Corkboard.Core/             # 核心：契约、配置/日志、领域服务、可复用控件与样式、Langs/Common
├── Corkboard.Shared/           # 跨工程契约：ConfigBase、Utils（数据根/路径）。禁止 UI 依赖
├── Corkboard.Desktop/          # 入口：Program.cs 选择平台根并启动 Avalonia 桌面生命周期
├── Corkboard.Platforms.Abstractions/ # 平台无关契约（IPlatformServiceRoot、窗口能力）
├── Corkboard.Platforms/        # 启动上下文、DI 桥接、不支持平台的 stub
├── Corkboard.Platforms.Windows|Linux|MacOs/ # 各平台原生窗口能力实现边界
├── Corkboard4Ci.Interface/     # ClassIsland 插件侧 IPC 契约（[IpcPublic]）
├── Corkboard.Core.Tests/       # xUnit v3 测试
├── docs/                       # 工程规则、导航、本地化、待办
├── Global.props                # 共享 MSBuild 策略（各工程 Import）
├── Directory.Build.props       # AvaloniaVersion 锚点
└── Platforms.props             # CorkboardPlatform 选择 + CORKBOARD_PLATFORM_* 宏
```

## WHERE TO LOOK

| 任务 | 位置 | 说明 |
| --- | --- | --- |
| 构建/测试/运行 | `Corkboard.sln`、`Corkboard.Desktop` | 没有 Makefile/CMake。`dotnet build Corkboard.sln`、`dotnet test Corkboard.Core.Tests` |
| 进程入口 | `Corkboard.Desktop/Program.cs` | 发布版本程序集 → 准备数据根 → 选择平台根 → Avalonia 桌面生命周期 |
| DI 装配 | `Corkboard/App.axaml.cs` 的 `BuildHost` | **唯一**注册点；新增服务只改这里 |
| 语言/主题启动期应用 | `Corkboard/App.axaml.cs` | 语言必须先于 XAML 加载，否则 `x:Static` 资源取到旧文化 |
| 静态服务定位 | `Corkboard.Core/Abstraction/IAppHost.cs` | 只在 Avalonia 控件构造期使用 |
| 导航页面注册 | `Corkboard.Core/Extensions/Registry/PagesRegistryExtensions.cs` | `AddMainPage`/`AddSettingsPage`/`AddGroup`，同时写注册表与键控 DI |
| 导航页面注册表 | `Corkboard.Core/Services/PagesRegistryService.cs` | 静态集合，只允许启动期写入 |
| 主导航壳 | `Corkboard/Views/MainView.axaml.cs` | 默认页 `AppConsts.DefaultMainPageId`，设置入口打开独立窗口 |
| 设置导航壳 | `Corkboard/Views/SettingsView.axaml.cs` | 带返回栈；默认页 `settings.general.basic` |
| 配置读写 | `Corkboard.Core/Services/Config/FileConfigService.cs`、`Abstraction/ConfigHandlerBase.cs` | 原子写入；属性变化自动保存 |
| 配置模型 | `Corkboard.Core/Models/MainConfigModel.cs` + `Models/SubConfigs/` | 新配置项挂到子配置下，不要平铺到根 |
| 路径与数据根 | `Corkboard.Shared/Utils.cs` | 便携包/安装版规则；`Utils.GetFilePath(...)` |
| 日志 | `Corkboard.Core/Services/Logging/` | 文件日志 + 压缩与保留期清理 |
| 本地化 | `Corkboard*/Langs/<页面>/Resources.resx` | 每页/每窗口一个目录；Designer 手写维护 |
| 账号登录 | `Corkboard/Services/Auth/` | OAuth 2.0 PKCE + loopback；`SectlAuthEndpoints.ClientId = 6ac3d35400241eff9fbe` |
| 版本使用人数上报 | `Corkboard/Services/PlatformVersionReportService.cs`、`Corkboard.Core/Services/Stats/VersionUsageReportPayload.cs` | 每次启动一次，尽力而为；平台标识是 `GlobalConstants.PlatformId`，**与 OAuth 客户端 ID 是两个不同的值** |
| ClassIsland 通知 | `Corkboard/Services/Ipc/`、`Corkboard4Ci.Interface/` | 失败返回结果对象，不抛到启动链路 |
| 平台能力 | `Corkboard.Platforms.Abstractions/`、`Corkboard.Platforms.*/` | 视图不得直接调用平台 API |
| 占位业务 | `Corkboard.Core/Services/Board/`、`Views/MainPages/BoardPage.*` | 换成真实赛道时整体替换 |
| 工程规则 | `docs/project_rules.md` | 最强约定来源 |

## CODE MAP

| 符号 | 类型 | 位置 | 作用 |
| --- | --- | --- | --- |
| `Program.Main` | 入口 | `Corkboard.Desktop/Program.cs` | 版本程序集、数据根、平台根、Avalonia 启动 |
| `App` | Avalonia 应用 | `Corkboard/App.axaml.cs` | 语言、主题、`BuildHost`、窗口生命周期、退出 |
| `AppConsts` | 常量 | `Corkboard/App.Consts.cs` | 窗口 scope、默认页面 Id、协议 |
| `IAppHost` | 静态服务定位 | `Corkboard.Core/Abstraction/IAppHost.cs` | `Host` 与 `GetService<T>` |
| `MainWindow` | 窗口外壳 | `Corkboard/Views/MainWindow.axaml.cs` | 按 scope 承载主界面/设置界面，记忆窗口尺寸 |
| `MainView` / `SettingsView` | 导航壳 | `Corkboard/Views/` | 构建菜单、键控 DI 页面工厂、页面切换动效 |
| `PagesRegistryService` | 注册表 | `Corkboard.Core/Services/PagesRegistryService.cs` | 主/设置/分组集合 |
| `PageInfo` | 特性 | `Corkboard.Core/Attributes/PageInfo.cs` | 页面 Id、图标、分组、隐藏与标题策略 |
| `ConfigBase` / `ConfigServiceBase` / `ConfigHandlerBase<T>` | 配置管道 | `Corkboard.Shared`、`Corkboard.Core/Abstraction/` | 模型基类、读写实现、自动保存句柄 |
| `MainConfigModel` / `MainConfigHandler` | 主配置 | `Corkboard.Core/Models`、`Services/Config` | `data/config/settings.json` |
| `BoardService` / `IBoardService` | 占位领域 | `Corkboard.Core/Services/Board/` | 便签 CRUD + 落盘样板 |
| `SectlAuthService` | 应用服务 | `Corkboard/Services/Auth/` | PKCE 登录、单飞刷新、令牌持久化 |
| `PlatformVersionReportService` | 托管服务 | `Corkboard/Services/PlatformVersionReportService.cs` | 启动后向 `POST /api/stats/version` 上报一次版本；载荷只含平台标识 + 版本 + 设备标识 |
| `VersionUsageReportPayload` | 纯模型 | `Corkboard.Core/Services/Stats/` | 上报载荷与版本/设备标识校验（可单测） |
| `ClassIslandIpcConnection` | 应用服务 | `Corkboard/Services/Ipc/` | 通知投递与连接探测，结果化降级 |
| `GlobalConstants` | 常量 | `Corkboard.Core/GlobalConstants.cs` | 版本、应用名、协议、图标字体 |

## CONVENTIONS

- 依赖方向单向：`Shared` ← `Core` ← `Corkboard` ← `Desktop`；`Platforms.Abstractions` 不依赖 Core。
- **不要**在视图里写死业务服务：页面构造期用 `IAppHost.GetService<T>()`，其它位置用构造函数注入。
- 页面必须同时出现在导航注册表与键控 DI（用 `AddMainPage`/`AddSettingsPage`，不要手写集合）。
- 运行期不要增删导航集合，需要动态显隐时改 `IsVisible`。
- 新配置项加到 `Models/SubConfigs/` 下的子配置类，并在 `MainConfigModel` 上以 `[ObservableProperty]` 暴露。
- 配置文件一律经 `ConfigServiceBase`（原子写入 + 统一 JSON 选项），不要自己 `File.WriteAllText` 写配置。
- 本地化：每个页面/窗口一个 `Langs/<名>/Resources.resx`（+ `Resources.en-US.resx`），
  Designer 文件手写维护、加键时同步；`.csproj` 里登记 resx 与 Designer 依赖关系。
- 平台相关代码只能出现在 `Corkboard.Platforms.<OS>`；先改 `Platforms.Abstractions` 的契约，再改实现。
- 数据根只能通过 `Utils` 访问；新增持久化目录走 `Utils.GetFilePath/GetDirectoryPath`。
- 注释与用户可见文本用中文；代码标识符用英文。

## COMMANDS

```powershell
dotnet build Corkboard.sln -v m                       # 全量构建
dotnet build Corkboard.Core\Corkboard.Core.csproj     # 单工程快速验证
dotnet test Corkboard.Core.Tests\Corkboard.Core.Tests.csproj
dotnet run --project Corkboard.Desktop\Corkboard.Desktop.csproj
```

## NOTES

- SECTL 的两个标识是**两个不同的值**，不要混用：OAuth 客户端 ID 在
  `Corkboard/Services/Auth/SectlAuthEndpoints.cs`（`6ac3d35400241eff9fbe`），
  平台标识 `platform_id` 在 `Corkboard.Core/GlobalConstants.cs`（`platform_29648fc4ac3ba07d`）。
  上游 SecRandom 两者恰好同值，本项目不是；改统计不影响登录，反之亦然。
- `NuGet.config` 只声明公共源（nuget.org + ClassIsland MyGet）。**不要**把开发机专属的
  `fallbackPackageFolders` 提交进去：NuGet 遇到不存在的目录会直接 NU1301 失败，CI 与其他机器都还原不了。
  本机想复用已有包缓存时用环境变量 `NUGET_FALLBACK_PACKAGES`（详见 NuGet.config 注释）。
- 版本元数据由 git 提供：`GitInfo.props` 在构建期跑 git，`roslyn/Corkboard.GitInfoGenerator`
  把 tag/分支/提交号生成成 `Corkboard.GitInfo`，只有 `EnableGitInfoGenerator=true` 的头程序集
  （当前是 `Corkboard.Desktop`）编译根 `AssemblyInfo.cs` 并因此关闭 SDK 的程序集信息生成。
  没有 git 仓库或没有 tag 时回退成 `0.0.0` / `Unknown`，构建不失败。
- 尚未移植的上游能力清单见 `docs/backlog.md`，其中包括视图引擎、插件 SDK、移动端头、图标源生成器。
