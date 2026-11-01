# 贡献指南

感谢你为 Corkboard 提交问题、提出建议、完善文档或贡献代码。

## 提交 Issue

- 使用 [GitHub Issues](https://github.com/SECTL/Corkboard/issues) 报告缺陷或提出功能请求。
- 提交缺陷时，请提供复现步骤、预期行为、实际行为、Corkboard 版本（设置 → 关于，或左下角水印）、Windows 版本与必要日志或截图。
- 提交功能请求时，请说明使用场景、预期行为，以及现有功能为什么满足不了。
- 不要在本项目 Issue、日志或截图中提交密码、OAuth 凭据、访问令牌或其他敏感数据。

## 开发环境

Corkboard 是基于 .NET 的跨平台桌面应用：

| 类别 | 技术 | 用途 |
| --- | --- | --- |
| 语言与运行时 | C# / .NET 10 | 应用、核心服务与测试 |
| 桌面 UI | Avalonia 12 + FluentAvaloniaUI | 跨平台桌面界面 |
| 依赖注入 | Microsoft.Extensions.Hosting | 应用宿主、服务与 ViewModel 组合 |
| MVVM | CommunityToolkit.Mvvm | `ObservableObject` / `[ObservableProperty]` / `[RelayCommand]` |
| 测试 | xUnit v3 | `Corkboard.Core.Tests` |
| 构建与发布 | GitHub Actions | 多架构构建、打包与发布 |

### 前置条件

- [.NET SDK 10.0.100 或更高的 10.0 特性带](https://dotnet.microsoft.com/download/dotnet/10.0)（`global.json` 已固定，`rollForward: latestFeature`）
- Git
- 运行桌面端需要对应平台的桌面环境；实际打包与安装方式以发行版本说明为准

### 获取与运行

```bash
git clone https://github.com/<your-account>/Corkboard.git
cd Corkboard
git remote add upstream https://github.com/SECTL/Corkboard.git

dotnet restore Corkboard.sln
dotnet build Corkboard.sln -v m
dotnet test Corkboard.Core.Tests\Corkboard.Core.Tests.csproj
dotnet run --project Corkboard.Desktop\Corkboard.Desktop.csproj
```

只验证某个 Core 侧改动时，可以先跑单工程：

```bash
dotnet build Corkboard.Core\Corkboard.Core.csproj
```

### 与 CI 对齐的语言环境

`.github/workflows/Build.yml` 跑在 **en-US** 的 Windows runner 上，而种子科目、默认文案这类值取自本地化资源。写死中文的断言在 CI 上必挂，所以改完测试请再按 CI 的语言环境跑一次：

```powershell
Corkboard.Core.Tests\bin\Release\net10.0\Corkboard.Core.Tests.exe -culture en-US
```

xUnit v3 的 runner 自带 `-culture` 开关，可用 `default` / `invariant` / `en-US`。

## 代码约定

提交前请阅读仓库根目录的 [AGENTS.md](AGENTS.md)（以及你改动目录下的子 `AGENTS.md`）。**`docs/` 在本仓库的 checkout 里是被忽略的**，工程规则一律以这些 `AGENTS.md` 为准；其中的约束优先于一般编码习惯。以下几条尤其重要：

- **先定性，再落文件**：代码按「便签通用 `Core/Notes` / 富文本通用 `Core/RichText` / 板子框架 `Core/Board` / 作业专属 `Core/Homework`、考试专属 `Core/Exam`」四类归目录，App 层同构（`Views|ViewModels/Services` 下按域分目录）。通用类型不要挂 `Board` 前缀。
- **改名 ≠ 改数据**：类名、命名空间、文件名随意改；**属性名、JSON 键、枚举数值、`PageInfo.Id`、资源键、数据路径、云端文件名全部冻结**。枚举只能在末尾增删成员。
- **依赖方向单向**：`Shared` ← `Core` ← `Corkboard` ← `Desktop`；`Platforms.Abstractions` 不依赖 Core。平台相关代码只能出现在 `Corkboard.Platforms.<OS>`，视图不得直接调用平台 API。
- **DI 唯一注册点是 `Corkboard/App.axaml.cs` 的 `BuildHost`**：可复用服务、设置页、主页面都在那里注册，不要在别处偷偷 `new`。静态服务定位 `IAppHost.GetService<T>()` 只在 Avalonia 控件构造期使用。
- **加设置项照五步走**：子配置类（`Core/Models/SubConfigs/<域>/`）→ 设置页与 ViewModel → 在 `BuildHost` 里 `AddSettingsPage<T>()` 并贴 `[PageInfo]` → 设置卡（`fa:FASettingsExpander` / `FASettingsExpanderItem`）→ 文案三处同步。不要把新配置项平铺到 `MainConfigModel` 根上。
- **文案三处同步**：中文 `Resources.resx`、英文 `Resources.en-US.resx` 与**手工维护**的 `Resources.Designer.cs` 一起加；`Corkboard.Core.Tests/LocalizationTests.cs` 会比对键集合与空文案。设置文案不加句号，用户可见处一律说「作业板」而不是「主窗口」。
- **`ConfigHandlerBase` 每次属性变化都会落盘**（没有防抖），所以不要直接双向绑 `TextBox.Text` 到配置；用 VM 的草稿属性 + 显式提交，「停手后再提交」一律用 `Corkboard/Helpers/DebounceTimer`。
- **transient 的 ViewModel 订阅单例，必须在页面 `Unloaded` 里 `Detach()`**，否则每开一次页面就往单例上多挂一个处理器。
- 新增依赖之前先确认现有库能不能做；确实要加时，版本挂到 `Directory.Build.props` 的锚点上，并同步 `THIRD-PARTY-NOTICES.md`。
- 注释与用户可见文本用中文，代码标识符用英文。

## 修改范围与测试

- 把改动保持在解决问题所需的最小范围，避免顺带重构无关代码；不要顺手改动正在被另一个会话修改的文件。
- 新增或修复的**纯逻辑**（解析、夹取、策略、合并、导入导出等）要放在 Core 并补 `Corkboard.Core.Tests` 的聚焦单测。
- UI、窗口、弹层、平台集成（置底、托盘、缩放、拖拽）改动必须**实际运行一次应用**验证；编译通过不能替代运行验证。
- 不要提交 `bin/`、`obj/`、`artifacts/`、`.tmp/` 或打包过程生成的文件。
- 改动项目结构、导航/DI 规则、本地化布局或模块边界时，**在同一次改动里**更新受影响的 `AGENTS.md`，并在 `AGENTS-HISTORY.md` 补一条「哪次动了什么」。

## 提交与 Pull Request

- 从 `main` 创建主题分支，并在开始前同步上游。
- 提交信息建议遵循 [Conventional Commits](https://www.conventionalcommits.org/zh-hans/v1.0.0/)，例如 `fix(board): 拖宽后卡片不跟着长`。
- PR 应说明问题、方案和验证方式；涉及 UI 时附上截图或录屏，涉及行为变化时说明兼容性与迁移影响（尤其是配置键、数据路径、云端文件名这类冻结项）。
- 提交 PR 前至少运行与改动范围相符的构建与测试命令，并在 PR 描述中说明未运行的检查及原因。
- 不要未经人工审阅直接提交生成式 AI 产出的代码、测试或文档；贡献者对提交内容的正确性和许可合规性负责。

## 贡献者许可协议（CLA）

仓库根目录的 [`CLA.md`](CLA.md) 是本项目的贡献者许可协议（版本 1.0，2026 年 11 月 1 日生效）。

- **首次提交 Pull Request 时会被要求在 PR 中签署。** 签署由 `.github/workflows/cla.yml` 的 CLA Assistant 自动记录，结果保存在 `signatures/version1/cla.json`。
- **未签署的 PR 会被标记为检查未通过。** 仓库对 `main` 开启分支保护、并把 CLA 检查设为必需状态检查之后，这类 PR 就无法合并（这项配置在仓库设置里，不在代码里）。这不影响你已经获得的 GPLv3 权利，只是该次贡献不会进入维护者可再许可的范围。
- 签署语句由机器人自动附在 PR 留言中。如果你的贡献全部是新的，直接发送：

  ```text
  I have read the CLA Document and I hereby sign the CLA
  ```

- 如果希望这次签署**同时覆盖你此前已提交的历史贡献**（`CLA.md` §9.5，可选），在句末追加一句：

  ```text
  I have read the CLA Document and I hereby sign the CLA, and I agree that this CLA also covers my prior contributions
  ```

- CLA 只做**许可授予**，不做版权转让：你仍然拥有自己贡献的版权，也仍然可以按 GPLv3 对它做任何事。维护者获得的是额外的一项「可再许可」授权，用于将来可能调整项目的出站许可证；调整前必须提前 30 天公示、并在公示期内与提出异议的贡献者善意协商（异议本身不构成否决权，见 `CLA.md` §4.3–§4.4）。
- CLA **不改变**任何已发布版本在 GNU GPLv3 下的授权，也**不覆盖** NuGet 依赖、随包分发的字体与图标等其他第三方作品。

## CI 与发布

仓库使用 GitHub Actions：

- `.github/workflows/Build.yml` 在推送到 `master` / `main` 和每个 Pull Request 上跑还原、构建与测试。
- `.github/workflows/Publish.yml` 负责多架构打包、签名清单与草稿 Release，**只有 `workflow_dispatch`**：维护者先改 `metadata.yaml` 里对应通道的 `tag` 并让它随那个发布标签提交，再对标签手动派发。
- `.github/workflows/cla.yml` 负责贡献者许可协议检查。维护者侧还要做两件事：在仓库 secrets 里配 `CLA_ASSISTANT_PAT`（否则签名写不回 `signatures/`），并在分支保护里把 CLA Assistant 设为必需状态检查。

常规贡献无需通过提交信息触发发布，也不需要动发布脚本。

感谢你的贡献。
