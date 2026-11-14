# PROJECT KNOWLEDGE BASE

<!--
维护契约：
- 保持这份文件“耐久”：写稳定约定，不写某台机器上的临时观察。
- 改动项目结构、目标框架、本地化布局、工作流、导航/DI 规则或模块边界后，
  必须在同一次改动里更新本文件（以及受影响的嵌套 AGENTS.md）。
- 根目录规则对整个仓库生效；子目录 AGENTS.md 补充子树规则。
- 约定冲突时：更具体的子目录 AGENTS.md 优先；仍判不出来就回到本文件，并在同一次改动里
  把矛盾消掉——不要两边各留一套。
-->

**Last Update:** 2026-11-01（设置页整理 + csproj 收敛）。逐条修订历史已搬到根目录 AGENTS-HISTORY.md
（按时间倒序记「哪次动了什么」）——本文件只留**当前成立**的约定，查某处设计为什么这样请去那份日志或 git log。

## 这是什么

Corkboard 是 GPLv3 的 C#/.NET 桌面应用：一块桌面作业板（便签）。业务规格与工程规则原本分写在 `docs/board.md` / `docs/project_rules.md`，
但 **`docs/` 在本 checkout 里被 .gitignore 忽略、只剩 `docs/sectl/` 的厂商 API 文档**
——所以规则一律以本文件与各子目录的 AGENTS.md 为准。
业务目前只有作业板（`Board`）一块，其余是框架层；**代码按「便签通用 / 富文本通用 / 板子框架 / 作业专属」
四类归目录**（见下节），以后加别的板子（课程表、待办……）时通用部件直接复用，不要再往通用类型上挂 `Board` 前缀。

## 技术栈：用什么库

| 用途 | 库 | 版本 | 说明 |
| --- | --- | --- | --- |
| UI 框架 | Avalonia | `$(AvaloniaVersion)` = 12.1.1 | 版本锚点在 `Directory.Build.props`，**工程文件里一律写 `$(AvaloniaVersion)`**，不要写死数字 |
| 桌面后端 | Avalonia.Desktop | 同上 | 平台窗口、渲染 |
| 控件与窗口外壳 | FluentAvaloniaUI | 3.0.2 | `FluentAvaloniaTheme`（主题/主题色）、`FAAppWindow`、`FANavigationView`、`FAFrame`、`FASettingsExpander`、`FAContentDialog`、`FAFontIconSource` |
| MVVM | CommunityToolkit.Mvvm | 8.4.2 | `ObservableObject` / `[ObservableProperty]` / `[RelayCommand]` |
| 集合操作 | DynamicData | 9.4.33 | `AddRange` / `RemoveMany` 等批量操作 |
| 宿主 / DI / 日志 / HTTP | Microsoft.Extensions.Hosting / DependencyInjection / Logging / Http | 10.0.x | `Host.CreateApplicationBuilder`，DI 只在一处装配 |
| ClassIsland 通知 | ClassIsland.Shared.IPC / dotnetCampus.Ipc | 2.1.0.1 | 进程间通知；失败返回结果对象，不抛到启动链路 |
| 测试 | xunit.v3 / xunit.runner.visualstudio / Microsoft.NET.Test.Sdk | 3.2.2 / 3.1.5 / 18.8.1 | `Corkboard.Core.Tests` |

新增依赖的规矩：

1. 先确认现有库能不能做（`FluentAvalonia` 已经覆盖控件、图标、对话框、设置卡，多数需求不用引新包）。
2. 版本尽量挂到 `Directory.Build.props` 的锚点上；只有单一工程用的包才写在工程文件里。
3. **改完依赖必须同步 `THIRD-PARTY-NOTICES.md`**（直接依赖表 + 随包分发资源表）。
4. 不要引第二套 UI 库、第二套 MVVM 库、第二套对话框/弹窗基础设施。

## 结构

```
Corkboard/
├── Corkboard/                  # 应用层：App 宿主、视图/ViewModel、应用服务、Langs、Assets
│   ├── Views/MainPages/        #   子目录 Board/、Homework/
│   ├── ViewModels/MainPages/   #   子目录 Board/、Homework/
│   ├── Views/SettingsPages/    #   子目录 Board/、Exam/
│   ├── Views/ExamBoard/        #   全屏考试看板窗口
│   ├── ViewModels/ExamBoard/   #   全屏考试看板的 ViewModel
│   └── Services/               #   Ui/、Board/、Homework/、Exam/、Time/、Cloud/、Auth/、Desktop/、Ipc/
├── Corkboard.Core/             # 核心：契约、配置/日志、领域服务、可复用控件与样式、Langs/Common
│   ├── Notes/                  #   Note、NoteStore、NoteDayFile、INoteStore、Legacy/（旧数据迁移）
│   ├── RichText/               #   RichTextStyle/Palette/HtmlStyling/ContentKind
│   ├── Board/                  #   板子框架：BoardService、NoteGrouping、排布排序枚举、回放
│   ├── Homework/               #   作业专属：HomeworkConfig、SectionCatalog、截止日期、清理、合并
│   ├── Exam/                   #   考试专属：ExamEntry、三档状态判定、时间格式化、ExamService、自动放映与看板切换策略、考试文件导入导出
│   ├── Time/                   #   NTP 校时：报文/客户端/国内源目录 + NtpTimeService（应用唯一时间来源）
│   ├── Imaging/                #   位图工具：IconRaster（面积平均缩帧）、IcoFile（拼多尺寸 ICO）
│   └── Controls|Styles|...     #   可复用控件与主题
├── Corkboard.Shared/           # 跨工程契约：ConfigBase、Utils（数据根/路径）。禁止 UI 依赖
├── Corkboard.Desktop/          # 入口：Program.cs 选择平台根并启动 Avalonia 桌面生命周期
├── Corkboard.Platforms.Abstractions/ # 平台无关契约（IPlatformServiceRoot、窗口能力）
├── Corkboard.Platforms/        # 启动上下文、DI 桥接、不支持平台的 stub
├── Corkboard.Platforms.Windows|Linux|MacOs/ # 各平台原生窗口能力实现边界
├── Corkboard4Ci.Interface/     # ClassIsland 插件侧 IPC 契约（[IpcPublic]）
├── Corkboard.Core.Tests/       # xUnit v3 测试
├── CLA.md                      # 贡献者许可协议 v1.0（2026-11-01 生效）：许可授予式，不做版权转让
├── CONTRIBUTING.md             # 贡献指南（含 CLA 签署流程）
├── docs/                       # 工程规则/规格/待办（checkout 未提供）
├── Directory.Build.props       # 全仓库属性：AvaloniaVersion 锚点 + TargetFramework/ImplicitUsings/Nullable + Version
├── Directory.Build.targets     # 唯一一处 Import Global.props（在工程正文之后求值）
├── Global.props                # 共享 MSBuild 策略：程序集信息生成、DebugType、allow unsafe 等
├── Platforms.props             # CorkboardPlatform 选择 + CORKBOARD_PLATFORM_* 宏
└── AGENTS-HISTORY.md           # 逐条修订历史（按时间倒序，不写进本文件）
```

**工程文件里不要再重复根上的属性**：`TargetFramework` / `ImplicitUsings` / `Nullable` 只在
`Directory.Build.props` 里写一次，`Global.props` 由 `Directory.Build.targets` 统一导入（**工程文件里不许
再出现 `<Import Project="..\Global.props" />`**，漏不加就少一份版本号与构建策略——Launcher 曾经就是这样才一直
顶着 1.0.0）。`Version` 的默认值也必须写在 `Directory.Build.props`：`Global.props` 排在工程正文之后，
那时 SDK 已经把空 `Version` 填成 1.0.0，「为空才赋值」的条件再也不成立。

依赖方向单向：`Shared` ← `Core` ← `Corkboard` ← `Desktop`；`Platforms.Abstractions` 不依赖 Core。

## 作业板域代码怎么归类（以后加别的板子照这个来）

- **四类固定**：便签通用 `Core/Notes`、富文本通用 `Core/RichText`、板子框架 `Core/Board`（排布/排序/分组/回放）、
  作业专属 `Core/Homework`（科目/截止日期/过期清理/合并）；控件与框架在 `Core/Controls|Styles|...`。加功能先定性，别塞进现成文件。
- **命名跟分类走**：通用实体 `Note`（不是 `BoardNote`），通用富文本 `RichText*`；板子框架保留 `Board*`；
  作业专属一律 `Homework*`。
  **不要再给通用类型挂 `Board` 前缀**。
- **App 层同构**：`Views|ViewModels/MainPages/Board|Homework/`、`Views/SettingsPages/Board|Homework/`、
  `Services/Board|Homework/`；弹层、对话框、窗口能力留在 `Services/Ui/`、`Views/` 根。
- **新板子**（课程表、待办……）：新建 `Core/<名>/` + `Views/MainPages/<名>/` + `ViewModels/MainPages/<名>/`，
  只复用 `Notes` / `RichText` / `Controls`，别改 `Board` / `Homework` 的公共契约。
- ⚠️ **改名 ≠ 改数据**：类名、命名空间、文件名随意改；**属性名、JSON 键、枚举数值、`PageInfo.Id`、
  资源键、数据路径、云端文件名全部冻结**。改完跑 build + test + `-culture en-US`。
- `Core/Notes/Legacy/` 三件套（`LegacyNoteFile` / `LegacyNoteContentConverter` / `LegacyTextFormatRange`）
  只服务旧数据迁移，新代码不要依赖。

## 设置怎么写（照这个流程走）

一个设置项从配置到界面一共五步，顺序固定：

**1）配置模型** —— 新配置项加到 `Corkboard.Core/Models/SubConfigs/<域>/` 下的子配置类，
用 `[ObservableProperty]`，并在 `MainConfigModel` 上以 `[ObservableProperty]` 暴露这个子配置。
**不要**把新配置项平铺到 `MainConfigModel` 根上。

```csharp
public partial class BoardSettingsConfig : ObservableObject
{
    [ObservableProperty] private BoardSortMode _sortMode = BoardSortMode.CreatedDescending;
}
```

**2）设置页** —— `Corkboard/Views/SettingsPages/<域>/XxxSettingsPage.axaml(.cs)` +
`Corkboard/ViewModels/SettingsPages/XxxSettingsPageViewModel.cs`。ViewModel 继承 `ViewModelBase`
（构造期拿 `MainConfigHandler`），页面构造期用 `IAppHost.GetService<XxxSettingsPageViewModel>()` 取 VM。

**3）注册** —— 只在 `Corkboard/App.axaml.cs` 的 `BuildHost` 里注册：
`AddSettingsPage<XxxSettingsPage>(CR.Settings_Xxx_Title)`，页面类上贴
`[PageInfo("settings.<域>.<页>", FluentIcons.XxxFilled)]`——**不带 `groupId`**：
`GroupId` 为空时注册表生成的是顶层导航项；「作业板」那一组是唯一的例外（见「壳层与导航」）。
**不要**手写 `PagesRegistryService` 的集合。注册会同时写导航注册表和键控 DI，两者必须成对。
只有同一域真的有两页以上、需要在侧边栏折叠归组时，才 `AddGroup(new PageGroupInfo(...))` 并给页面加
`groupId: "settings.<域>"`——单页分组只会白白多一个「点开才看见唯一子项」的 chevron。

**4）界面 = 设置卡** —— 页面根是 `ScrollViewer` 包一个
`<StackPanel Classes="page-container">`（Core 样式负责限宽居中与卡片间距），
每个设置项一张 `fa:FASettingsExpander`：`Header` 放名称、`Description` 放简介、`Footer` 放控件；
内容多的（列表、表单）放内容区，内容区可折叠。
一张卡里有多项时，里面按行放 `fa:FASettingsExpanderItem`（`Content` 名称、`Description` 简介、
`Footer` 控件）——名称与简介在左、控件在右，**不要把名称、控件、说明全堆在卡片左列**里，
范例见 `Corkboard/Views/SettingsPages/About/AboutSettingsPage.axaml`。

```xml
<fa:FASettingsExpander Header="{x:Static core:Resources.Settings_Basic_Language}">
    <fa:FASettingsExpander.Footer>
        <ComboBox ItemsSource="{Binding LanguageOptions}" SelectedItem="{Binding SelectedLanguage, Mode=TwoWay}" />
    </fa:FASettingsExpander.Footer>
</fa:FASettingsExpander>
```

**5）文案** —— 键加到 `Corkboard.Core/Langs/Common/Resources.resx` 与 `Resources.en-US.resx`，
并手写同步 `Resources.Designer.cs`（这个文件是手工维护的）。命名跟着现有键走：
`Settings_<组>_<项>` / `Settings_<组>_<项>Description`，页面标题是 `Settings_<组>_Title`。

### 设置的硬规矩

- **设置文案不加句号**：名称、简介、开关说明，中文不用「。」、英文不用「.」，短句收尾。
- **用户看得到的文案一律说「作业板」，不说「主窗口」**：`MainWindow` 只是承载作业板的那个窗体，
  在用户眼里它就是作业板。开关、简介、引导汇总、托盘项、确认框、CHANGELOG 都照这个叫法
  （例：`Settings_Basic_ShowStartupWindow` = 「启动时显示作业板」、启动汇总 = 「启动时显示作业板」/
  「启动后先待在托盘里」）。「主窗口」只许留在代码注释、类名与内部日志里。**改文案只改 resx 的
  value、键名冻结**（`ShowStartupWindow` / `Oobe_*` 这些键照旧）。
- **中英两套文案必须一一对齐**：`Corkboard.Core.Tests/LocalizationTests.cs` 会比对两份 resx 的键集合、
  拒绝空文案，缺一条就红；加键时中文、英文、`Resources.Designer.cs` 三处要一起加。
- **简介要像人说的话，不是标签**：一句话说清「这一项做什么、改了会怎样」，中文一条 10–21 个字
  （英文同量级的一句话），口语、具体、一条一件事。**别压成两三个词的标签**（「被别的窗口盖住」
  「跟着窗口变宽」这种读起来像字段名，用户看不懂），也别写成并列堆叠的功能清单；长短语由
  `FASettingsExpander` 的 `Description` 一行承载，放不下就先砍修饰、不要再加第二句。
- **不要直接双向绑 `TextBox.Text` 到配置**：`ConfigHandlerBase` 每次属性变化都会落一次盘（没有防抖），
  而 Avalonia 的 `UpdateSourceTrigger` 默认是 `PropertyChanged`——每敲一个字符就写一遍 `settings.json`。
  改成 VM 里的草稿属性 + 显式提交（回车 / 页面 `Unloaded`），范例见 `BoardSettingsPageViewModel.CommitBoardName`。
- **「停手后再提交」的计时一律用 `Corkboard/Helpers/DebounceTimer`**（`Schedule(commit)` / `Flush()` / `Cancel()`），
  别在各页 VM 里再手搓 `DispatcherTimer`：以前 5 个设置页 VM 各写了一份，`Flush`（离开页面时把最后一次选择补上）
  和 `Cancel`（别处已经提交过，掐掉免得白写一遍盘）的语义要靠人记。同一个控件要按多个下标分别防抖时
  （色板那种）就放 `Dictionary<int, DebounceTimer>`。
- **平台不支持的能力整张卡隐藏**：`IsVisible` 绑平台能力查询（例：`IsPinToDesktopSupported`
  读 `IWindowFeatureService.SupportedFeatures`），不要留一个点不动的 `IsEnabled="False"` 开关，
  简介里也不要写「仅 Windows 支持」这类话。
- **确认对话框用 `FAContentDialog`**：`await dialog.ShowAsync(TopLevel.GetTopLevel(this))`，
  结果比对 `FAContentDialogResult.Primary`（见 `BoardPage.OnDeleteNoteClick`）。
- **表单弹层要自绘，不要开系统窗口**：走 `PageOverlayService`（单例）——页面调 `Show(控件)`，
  **壳（`MainView`）在根 `Panel` 上把遮罩与内容画出来**（`Border.scrim` / `Border.sheet`，样式在 Core）。
  理由：自绘的没有标题栏、不依赖桌面端多窗口能力（手机端一样能用）、样式完全可控；
  而**必须挂在壳那一层**——页面在 `FAFrame` 内容区里，遮罩盖不到壳的自绘标题栏，模态会「没盖全」。
  不要为表单引第三方弹窗，也不要为了省地方把它做成常驻输入区。
  `FAContentDialog` 只用于一句话的确认框（见 `BoardPage.OnDeleteNoteClick`）。
  ⚠️ **唯一例外：主窗口置底到桌面时要改用独立顶层窗口**。置底后主窗口是桌面宿主的子窗口，
  Windows 不给它键盘焦点（`hwndFocus` 恒为 0），壳内弹层**一个字都打不进去**，且无法绕过。
  此时 `PageOverlayService.UseSeparateWindow` 为真，表单由 `PageOverlayWindow`（无边框、不进任务栏）承载。
  ⚠️ **这个窗口要「透明窗 + 圆角实体底座」**：`TransparencyLevelHint="Transparent"` + `Background="Transparent"`，
  否则无边框窗口的方形底会在卡片四角露出**黑色角**（用户反馈过）；同时**不能**让内容变成半透明——
  独立窗口背后没有页面可叠、桌面会透上来，所以窗口内容外面必须自己叠一层
  `Border`：`SolidBackgroundFillColorQuarternaryBrush`（实体色，不是 Fluent 那几个半透明 CardFill）+
  `CornerRadius="8"` + `ClipToBounds="True"`，不画描边（里面的 `Border.sheet` 自带）。标志只在**置底实际生效**时置真，并要结合请求方向判断（`Apply` 在关闭时也会把
  `DesktopBottom` 报成已应用）。
  ⚠️ **拖动窗口与弹层会打架**：壳拖标题栏时会 `Capture` 指针，拖动结束**必须显式
  `Capture(null)`**——指针在窗口外抬起时收不到 `PointerReleased`，捕获留在壳上会让窗口点不动、
  输入框无法聚焦（表现就是「输入框打不了字」）；并且 `Overlay.IsOpen` 时既不开拖也不开边缘缩放
  （`MainView.OnWindowEdgeGuard` 首行就退）。**只有解锁态能拖标题栏**（见「## 壳层与导航」），
  整窗长按拖动已经下架。
- **布置作业表单顶部的布局**（`HomeworkAssignmentForm`）：第一行左边是「打字 / 手写」形态切换，
  右边依次是科目与截止日期（`CalendarDatePicker` + 清除按钮），**二级分类在新一行、在下面**；
  快捷标签 chips 放在分类行下面、内容区上面，**两种形态（含手写）都看得到**。
  内容区默认 680×400（`MinWidth` 420），右下角缩放手柄 26×26 DIP（触控也点得到；原来 18 太小），
  卡片默认更宽（`MinWidth` 480 / `MaxWidth` 960），
  内容区 `HorizontalAlignment="Center"`；卡片宽度由 `SyncSheetWidthToContent()` 跟着内容区走
  （= 内容区宽 + 卡片内边距，夹在 `MinWidth`/`MaxWidth` 里，显式写 `Sheet.Width`）——
  ⚠️ 卡片外面套着纵向 `ScrollViewer`、横向滚动关掉时它会用视口宽度去量内容，内容区再宽也撑不宽卡片，
  表现就是「拖大后卡片不跟着长、整框不居中」。拖手柄过程中、`FitContentAreaToLimit()` 末尾都要调一次。
  手写工具栏末尾的提示文案与空画布中间那句提示已按用户要求删掉，**不要再加回去**。
- **布置作业表单只有顶栏能拖动**（`HomeworkAssignmentForm` 的 `SheetHeader` + `OnHeaderPressed`）：
  第一行是 `<Grid x:Name="SheetHeader">`——左边标题、右边一颗 28×28 的关闭钮，整行挂着
  `Cursor="SizeAll"` 与「拖动移动卡片」提示，**这是卡片上唯一的拖动面**。别的行（表单行、内容区、
  底部按钮行）按住都不拖：曾经做过「整张卡片都能拖」，用户明确要求删掉（标题行与留白太容易误拖，
  触屏更甚）；反过来完全不让拖用户也不认（弹层挡住底下的板子时要能挪开）。所以是「只有顶栏能拖」这一档。
  拖动实现仍然是 `SheetDragLimits` + `RenderTransform` 位移、置底形态改拖 `PageOverlayWindow`，
  **不要退回整卡拖动**。两条踩过的坑：
  - ⚠️ 拖动处理器挂在**表单自己**身上（`AddHandler(..., RoutingStrategies.Bubble)`），
    不是挂在顶栏上——拖动期间事件只从捕获元素往上走，挂子元素上就一个都收不到（「按住拖不动」）。
    `PointerCaptureLost` 用 `Direct | Bubble | Tunnel` 三种路由全写。
  - ⚠️ **位移要用同一个坐标系**：按下时 `e.GetPosition(topLevel)`，移动时也要 `e.GetPosition(topLevel)`
    减起点。用屏幕绝对坐标算增量会恒得 0（两个量都在顶层坐标系里，窗口还没动）。
  - 顶栏右上那颗关闭钮与底部「取消」同一个命令（`CancelCommand`），弹出表单后不用滚到底就能退出。
  - ⚠️ **打开表单时不要当场 `_editor.Focus()`**。`AttachedToVisualTree` 里那一发
    `DispatcherPriority.Loaded` 的派发排在 `PointerPressed` 之前跑，会在用户按下的当口把键盘焦点
    抢到编辑区，一次按下引发的焦点转移让这一轮指针输入作废——表现就是「不先点一下输入框，
    取消 / 保存按钮点不动，点了输入框之后才正常」（用户报的就是这个），按住顶栏第一次也拖不动。
    现在走 `ScheduleInitialFocus()`：只挂一发 `PointerPressed`（Tunnel + handledEventsToo），
    等用户第一次真的点进表单，再 `DispatcherPriority.Input` 把焦点交给编辑区。
- **弹层卡片的尺寸上限按「看得见的宿主」算**（`HomeworkAssignmentForm` + `SheetSizeLimits`）：
  宽度 = 设计上限（720）与「可用宽度 − 留边」取小；高度 = **可用高度 × 70%**（下限 320 托底）。
  基准优先取宿主窗口的内容区——卡片是居中的，只能占七成、上下各留一成半，窗口再大也不会长成
  一条贴边的竖长板子，窗口小也不会被裁掉一半；**但 `SizeToContent` 的独立窗口要跳过它自己的尺寸**
  （卡片长大 → 窗口跟着长大 → 上限又抬高，两边互相追着长），那条路退回屏幕工作区。
  超过上限的内容由卡片自己的滚动条吃掉。策略与夹取算式是纯逻辑、有单测
  （`SheetSizeLimits` / `SheetDragLimits`），**不要在 code-behind 里另写一套**。
  内容区（那个富文本编辑器）能拉多大 = 卡片上限扣掉<b>其它每一行的高度之和</b> + 行距 + 卡片内边距，
  那些行（**含底部「取消 / 保存」那一行**）是**逐个量 `Bounds.Height`** 的，
  **不要**用「内容栈整高减内容区」倒推：卡片顶到上限、内部开始滚动时内容栈的高度会被裁到视口那么高，
  倒推出来的「其它行高度」偏小，于是内容区能一路拉大、把底部按钮顶出卡片（踩过这个坑）。
- **格式工具栏常驻在编辑区正上方**（`HomeworkAssignmentForm` 的 `EditorToolbar`）：
  内容区第一行就是库自带的 `rte:RichEditorToolbar`（`ToolbarLevel="Normal"`、`ShowFileActions="False"`、
  `ShowPageControls="False"`、`Focusable="False"`），它的 `Target` **在 code-behind 构造函数里**
  指向编辑控件——`Target` 为空时工具栏 `IsVisible=False`、一个按钮都不渲染，而且**不报任何错**，
  漏了这一句的表现只是「工具栏不见了」。
  按钮打的是库的公开命令（`ToggleBold` / `SetForeground` / `SetFontSize`…），
  **全部作用于编辑器当前选区**，所以宿主不需要（也拿不到）任何选区读写接口。
  - **不要再造第二套格式栏**：库的工具栏已经覆盖加粗/斜体/下划线/删除线、文字颜色、高亮、
    列表、缩进、行距、对齐、字号、字体、段落样式与插入表格/图片/分隔线。
    要加自己的动作就塞 `LeadingItems` / `TrailingItems`（`AvaloniaList<Control>`），别在 XAML 里另拼一排。
  - 实测容量（600×260、挂树后）：`Minimal` = 6 按钮 + 字号框；`Normal` = 20 按钮 + 4 下拉
    （Font / Font Size / Paragraph Style / Alignment）；`Maximum` = 23 按钮 + 7 下拉
    （多 Export / Import / Print 与 View zoom / Paper size / Page orientation）。
    `Auto` 在任何宽度下都**等于 `Normal`**（不会降级成 `Minimal`），直接用 `Normal` 最省心。
  - ⚠️ **库的工具栏没有任何「清除格式」按钮**（`ClearFormatting` 只在本地化表里，没有公开命令）：
    要让选段回到默认样式只能自己加一个按钮调 `SetFontSize(默认字号)` +
    `SetForeground(默认颜色，留空则主题文字色)`。`HomeworkAssignmentForm` 现在**没有**这个入口。
  - **工具栏文案走 Core 的 `RichTextLocalization.Apply(bool chinese)`**（`App.InitializeLanguages`
    里跟着界面语言调一次）：库只内置 en/ko 两张表，而 `RichEditorLocalization.Language` 默认就是 `"zh"`
    （空表 → 全部回退英文），**不显式设置就永远是英文**；`Register(language, dict)` 是**合并语义**，
    只覆盖给出的键、其余继续回退英文，我们的中文表把库内置的 119 个 key 全覆盖了。
    切语言时库会把工具栏子控件整棵重建，**不用**宿主自己刷新。
  - **颜色入口的色板就是「设置 → 作业板 → 快速颜色」里配的那几个**：库把色板放在
    `RichEditorToolbar.Palette` 这个**静态**数组上（文字颜色与高亮两个弹出层共用），
    而且是在**造工具栏的时候**才读一次——已经建出来的工具栏不会跟着变。所以由 Core 的
    `RichTextToolbarPalette.Apply(IEnumerable<Color>)` 去写这个静态数组，只在两处调：
    `App.Initialize`（早于任何窗口，必须赶在第一个工具栏建出来之前）与设置页
    `BoardSettingsPageViewModel.PersistPalette()`（色板唯一的落盘漏斗：新增/删除/前移后移/
    取色器防抖提交都走它）里落盘之后。改完色板重开一次表单即生效，不需要刷已经开着的那个。
    库对 `null` 与**空数组**是直接抛异常的，`RichTextToolbarPalette` 因此在输入为空时原样保留库自带色板。
    浮层末尾库自带一个手输 `#RRGGBB` 的输入框（走 `Color.TryParse`，与色板无关），色板里解析不了的串
    不会抛异常、只会画成黑色。
  - **想自己挑色要靠我们自己加的那颗「自定义颜色」按钮**（`HomeworkAssignmentForm`）：库那两个颜色浮层只有
    「固定色板 + 手输色值」，手输不等于取色器，用户要的是能拖光谱自己挑。库的浮层改不了，于是走工具栏
    给宿主留的 `TrailingItems`（`AvaloniaList<Control>`，库还会清掉这些控件的 `Focusable`，所以点它不会
    把编辑器选区弄丢）加一颗色轮按钮，浮层里放 Avalonia 的 `ColorView`（光谱 + 分量，始终展开）——
    **别用 `ColorPicker`**：它自带下拉浮层，套进我们的 `Flyout` 会变成两层浮层。
    浮层底部两个按钮把挑中的颜色经库的公开命令 `SetForeground` / `SetHighlight` 应用到当前选区，
    点完 `flyout.Hide()`（`HomeworkAssignmentForm.axaml.cs` 的 `ApplyCustomColor`）。固定色板照旧保留，
    库自带那个手输框也还在，这只是一种补充。
    ⚠️ 这颗按钮与浮层都要标 `Classes="rte-keep-theme"`：`RichTextToolbarTheme` 是「颜色值精确匹配」换库里
    写死的浅色，我们自己控件的正常前景色（暗色主题下的纯白）正好命中那张表，不排除就会在暗色下被换成
    深底，字看不见（见 `RichTextToolbarTheme.KeepThemeClass` / `IsKeptSubtree`）。
    ⚠️ **颜色匹配必须带上「属性角色」**（`RichTextToolbarTheme.Role`：底色 / 墨色 / 描边）：同一个颜色
    在不同属性上含义不同——库写死的白是「控件底色」，而暗色主题里的白正好是主文字色
    （`TextFillColorPrimaryBrush`）。不分角色地按颜色换，会把主题自己的白前景当成库的白底，
    换成 `ControlFillColorDefaultBrush`（暗色下只有 6% 白），表现就是**深色主题下工具栏图标与下拉文字
    几乎看不见**（用户反馈过一次）。白色条目因此只对底色生效，`#3C4043`/黑/灰那些墨色条目只对墨色生效，
    描边条目只对描边生效；`Shape` 的 `Fill`/`Stroke` 两种角色都认（实心层用 `Fill` 画墨色、线条层用
    `Stroke`）。**别把 `Role` 参数省掉退回按颜色一刀切**。
  - 编辑器上 `ShowFormattingMenu="False"`（有常驻工具栏就不开右键菜单）、
    `ShowPageBoundaries="False"` / `ShowPageNumbers="False"`（便签不画 A4 纸）；外面必须套 `ScrollViewer`
    并给宽度约束——库控件没有内部滚动条且 `ClipToBounds=False`，不给宽度会按整张纸宽排、内容过高画到边界外。
- **transient 的 ViewModel 订阅单例，必须在页面离开可视树时断开**：ViewModel 由键控 DI 以 transient
  创建，而配置 handler 与领域服务是单例，直接 `+=` 会让每开一次页面就往单例上多挂一个处理器
  （配置被重复保存、页面回收不掉）。做法是 VM 上留 `Detach()`，页面 `Unloaded` 里调用。

## 本地化

- `Corkboard*/Langs/<页面>/Resources.resx`（+ `Resources.en-US.resx`），核心通用文案在
  `Corkboard.Core/Langs/Common/`。Designer 文件手写维护，加键时同步，`.csproj` 里登记依赖关系。
- **界面语言只有中文和英文**（`LanguageMode`）：枚举按数字落盘，只能在末尾增删成员；
  旧配置里的 `2` 由 `LanguageModeJsonConverter` 归一成简体中文。
- 语言下拉的显示名（简体中文 / English）固定用该语言自身的写法，两边 resx 刻意同值，
  不要改成跟随界面语言翻译。
- 注释与用户可见文本用中文；代码标识符用英文。

## 平台与 DI

- 平台相关代码只能出现在 `Corkboard.Platforms.<OS>`；先改 `Platforms.Abstractions` 的契约，再改实现。
  视图不得直接调用平台 API。
- DI 唯一注册点是 `Corkboard/App.axaml.cs` 的 `BuildHost`，不要在别处偷偷 `new`。
- 静态服务定位 `IAppHost.GetService<T>()` **只在 Avalonia 控件构造期**使用，其它位置用构造函数注入。
- 语言必须先于 XAML 加载，否则 `x:Static` 资源取到旧文化。

## 贡献者准入（CLA，对齐上游 SecRandom-C）

- 仓库根目录的 `CLA.md`（v1.0，2026-11-01 生效）是**许可授予**式协议、不做版权转让：默认出站仍是
  GPLv3（§4.1），调整出站许可证要提前 30 天公示并善意协商（§4.3 公示、§4.4 异议，异议不构成否决权），
  已发布版本的 GPLv3 授权不受影响（§7.1）。首次 PR 要在 PR 评论里签署，由 `.github/workflows/cla.yml`
  的 CLA Assistant 记录到 `signatures/version1/cla.json`；签署语句原文见 `CLA.md` §9.1 与 `CONTRIBUTING.md`，
  §9.5「追认历史贡献」的后缀与维护者白名单在那份工作流里。
- ⚠️ **不要原地改 `CLA.md` 的条款**：**版本号、生效日期与 `signatures/versionN/` 目录是一起动的三件套**
  ——改已签署版本的条款会让既有签名失去凭据。要改条款就发新版本 + 新签名目录，旧目录留着。
  ⚠️ 发 v2 时 `cla.yml` 里还有四处版本敏感点要一起改：头部注释的版本与日期、`path-to-signatures` 的
  `version1`、`signed-commit-message` 里的 `CLA v1.0`、`custom-notsigned-prcomment` 里的生效日期。
  `custom-pr-sign-comment` 必须保持空串（留空时 action 用内置默认语句与内置正则，追认后缀才签得上）。
- 工作流本身只是**红色提示**：要真正阻止未签署的 PR 合并，得在仓库设置里给 `main` 开分支保护、把
  `CLA Assistant` 设为 required status check（check 在界面里显示为 `CLA Assistant / cla`，写规则时填 `cla`）；
  回写签名文件还需要 `CLA_ASSISTANT_PAT`（缺了回落 `GITHUB_TOKEN`，通常没有提交权限）。
  **这些是仓库设置，不在任何文件里**，换机器/新建仓库要重新配。
- ⚠️ `CLA_ASSISTANT_PAT` 的值**不能带尾随换行**：用管道写 secret（`<值> | gh secret set …`）会把换行一起存进去，
  action 打 contents API 时报 `##[error]Could not retrieve repository contents. Status: 500`——不是 401，
  很容易被误读成权限、路径或分支写错。改用 `gh secret set <名字> -R <仓库> -b <值>` 写入即可。
- 分支名是 `main`（上游 SecRandom-C 用 `master`），`path-to-document`、`branch` 与 `signatures` 路径三处要一致。

## 崩溃恢复（对齐上游 SecRandom-C）

- **一整套照上游搬过来**：未处理异常 → 写崩溃报告 → 弹 `CrashRecoveryWindow`（复制报告 / 反馈问题 /
  忽略 / 重启 / 关闭，`PromptAndRestart` 那档还有 10 秒倒计时自动重启）→ 按配置决定重启还是只提示。
  配置项是 `General.CrashRecovery.Mode`（`CrashRecoveryMode`：`None = 0` / `RestartOnly = 1` /
  `PromptThenRestart = 2` / `PromptAndRestart = 3`，**枚举按数字落盘，只能在末尾增删**，默认第三档），
  设置页在「设置 → 基础设置 → 稳定性 → 崩溃恢复」。**没有**上游的「崩溃后禁用插件」那一项（Corkboard 没有插件系统）。
- **分三处放**：纯逻辑 + 参数定义在 `Corkboard.Core/CrashRecovery/`
  （`CrashRecoveryPromptOptions` 认 `--crash-report <路径>` / `--crash-auto-restart`，`CrashRecoveryLaunchPlan`
  按模式算新进程参数，`CrashRecoveryRestartGuard` 两分钟退避）；进程侧在 `Corkboard/Services/CrashRecovery/`
  （`CrashRecoveryRuntime` 写报告 / 起新进程 / 打开反馈 issue，`DispatcherCrashRecovery` 是那条「先弹窗、
  弹不出来再重启」的分叉）；界面是 `Corkboard/Views/CrashRecoveryWindow` + `CrashRecoveryView` +
  `Corkboard/ViewModels/CrashRecovery/CrashRecoveryPromptViewModel`。
- **两个入口都要接**：`Corkboard.Desktop/Program.cs` 挂 `AppDomain.CurrentDomain.UnhandledException`
  （包住 `StartWithClassicDesktopLifetime`，异常没被处理就 `TryHandleFatalException` 后重新抛），
  `Corkboard/App.axaml.cs` 挂 `Dispatcher.UIThread.UnhandledException`（先存配置、记日志，再交给
  `DispatcherCrashRecovery.TryRecover`，返回值写进 `e.Handled`）。**别只接一处**：UI 线程上的异常走
  Dispatcher、启动期与后台线程的异常走 AppDomain，漏一条就是「崩了什么都不显示」。
- **崩溃后那一轮只画崩溃窗口**：新进程带着 `--crash-report` 起来时，`OnFrameworkInitializationCompleted`
  在单实例转发与建主窗口**之前**就把 `MainWindow` 换成崩溃窗口然后 `return`（Host 仍要建，
  窗口读配置写日志靠它）。⚠️ 因此 `Program.cs` 里那句 `TryForwardToExistingInstance` 必须加
  `CrashRecoveryRuntime.StartupPromptOptions is null` 的前置条件——否则崩溃窗口会被转发给老进程、
  自己立刻退出，用户什么都看不到。
- **重启前必须让出单实例互斥**：`CrashRecoveryRuntime.StartLaunchPlan` 先 `SingleInstanceGate.Release()`
  再 `Process.Start`，不释放的话新进程永远当不上第一实例（只 Dispose，互斥体本来就 `initiallyOwned: false`）。
- **退避是硬要求**：「崩—重启—再崩」会变成谁都没法操作的死循环，所以两分钟内只自动重启一次
  （状态在 `%TEMP%/Corkboard/crash-restart-guard.json`）；`PromptAndRestart` 退避窗口内退成「先提示」，
  `RestartOnly` 退避窗口内直接不重启。
- **报告落在数据根与临时目录**（`Utils.GetDirectoryPath("crashes")` + `%TEMP%/Corkboard/crashes`），
  读取时只认这两个根下的文件、且不超过 1MB（`ReadCrashReport`），UI 那头拿到的是文本、不是路径。
- **反馈只走 GitHub issue**（`https://github.com/SECTL/Corkboard/issues/new`，报告前 6000 字塞进 body）；
  上游那套「应用内反馈抽屉」没有搬——Corkboard 没有反馈服务，别照抄一个空壳。
- 文案独立一份：`Corkboard/Langs/CrashRecovery/`（`Resources.resx` / `Resources.en-US.resx` / 手写 Designer），
  **别再往公共 resx 里塞窗口文案**；设置卡那几个键仍在 `Corkboard.Core/Langs/Common/`
  （`Settings_Section_Stability` / `Settings_Basic_CrashRecovery*`）。
- 退出统一走 `RequestDesktopShutdown()`（`_desktopLifetime.Shutdown()`，不在 UI 线程就 `Post`）——
  **不要**在崩溃路径里直接 `Environment.Exit`（配置没落盘、托盘图标不回收）。

## 发布与自动更新（对齐上游 SecRandom-C）

- **发布脚本**：`.github/workflows/Publish.yml`（只有 `workflow_dispatch`，手填一个**已存在**的发布标签，
  `vMAJOR.MINOR.PATCH` 或 `-beta.N` / `-alpha.N`）。两个 job：`validate_release`（按标签推出通道，
  要求 `metadata.yaml` 里该通道的 `tag` 与发布标签**完全一致**）→ `publish`（checkout 该 tag、
  矩阵 `win-x64` / `win-x86` / `win-arm64`、校验 `release-public-key.txt` 与私钥 secret 对得上、版本与标签一致；
  每架构跑 `full`（含框架）与 `light`（框架依赖）两次 publish；打 Launcher；Windows 出便携 ZIP 与 Inno 安装包；
  最后建**草稿** Release，发布说明由 `scripts/gen-release-note.ps1` 从 `CHANGELOG\` 生成）。
  - 矩阵目前**只开 Windows**；linux/macOS 的行在文件底部注释里留着，需要时再打开。
  - 环境依赖：`actions/checkout fetch-depth: 0`（版本号构建期由 git 给）、`RELEASE_TAG` 环境变量
    （**不叫 `VERSION`**——MSBuild 会把环境变量当同名属性读，restore 会直接报版本字符串非法）、
    `UPDATE_MANIFEST_PRIVATE_KEY_PEM_BASE64`、`WINDOWS_CODESIGN_PFX_BASE64` / `WINDOWS_CODESIGN_PFX_PASSWORD` /
    `WINDOWS_CODESIGN_TIMESTAMP_URL`（签名可选，缺失就跳过并 warning）、`SECTL_MIRROR_BASE`（可选仓库变量）。
- **发新版本的顺序**：先改 `metadata.yaml` 对应通道的 `tag` 并提交进那个 tag → 再对 tag 跑 Publish。
  发布 job 按 tag 取代码，改在 tag 之后没用；频道写旧了 Gate 会先拦（放过去就是一个谁都拿不到的更新）。
- **自动更新**（`Corkboard/Services/Updates/`，照上游「思拓创联升级中心」逐项对齐）：
  `metadata.yaml` 定通道（release / beta / alpha）→ `UpdateCenterService` 双来源（`ghproxy.sectl.cn` 镜像优先、
  回退 GitHub raw）→ Ed25519 签名清单 `Corkboard-update-manifest.json` + `.sig`
  （公钥 `Corkboard/Assets/Updates/release-public-key.txt`，avares 读）→ 下载校验 SHA-512 →
  按包标记 `Corkboard.Shared/Utils.PackageMarkerFileName` 分流部署。设置页是「设置 → 更新」，
  模式 `AutoUpdateMode`（0/1/2/3 照上游语义）、通道下拉 release/beta/alpha。
  `UpdateScheduler`（`BackgroundService`，启动 8s 后首检 + 每小时）只按配置动作，**不要在 UI 里另写调度**。
- **便携包形态（契约，Launcher 与更新客户端都靠它）**：ZIP 根 = `CorkboardLauncher.exe` +
  `app-<tag>-<suffix>/`（目录里有 `Corkboard.package.json`，`packageKind=portable-zip`）。
  更新时新版本搬进新的 `app-*/` 目录，启动器只认「带 `.current` 标记」的那一版；`.current` 由
  `DeployPortableAsync` 维护（新目录写、旧目录删），Launcher 自己**不写也不删**。旧 `app-*` 目录不自动清理
  （留着可回滚），`.partial` 目录切换未完成、启动器会跳过。
- **安装器形态**：Inno `Setup.iss`（`/DMyAppVersion` `/DMyAppOutDir` `/DBuildArch<Arch>`），装到
  `{localappdata}\SECTL\Corkboard`、三语（中/日/英）；安装版更新走带 `/S` 静默参数的安装器，装完由安装器拉起应用。
- **`Corkboard.Launcher`**（`Corkboard.Launcher/`，`PublishSingleFile`）是**照上游 `SecRandom.Launcher` 一比一搬的**
  （只把产品名/主程序名/包标记/环境变量/图标改掉，逻辑一行没删）：枚举 `app-*`，按「有 `.current` → 版本号 →
  构建号」排序，跳过带 `.partial` 或缺 `Corkboard.package.json` 的目录，设好 `CORKBOARD_PACKAGE_ROOT`
  （`Utils.PackageRootEnvironmentVariable`）后 `Process.Start` 子进程并**立即返回**——
  它不等待、不写 `.current`、不清理任何目录。应用侧重启走
  `App.RestartThroughLauncherAsync()`（先停 Host → 拉起 Launcher → Shutdown）；安装器分支走 `App.StopAsync()`。
- **签名密钥只放 GitHub secrets，不进仓库**：清单私钥与 PFX 都只在 CI 用；公钥文件必须与私钥配对
  （Publish 会先比对）。PFX 可以用自签（本机流程能跑通），但正式分发要受信 CA 证书，否则 SmartScreen 会挡。
- **本地验脚本**：`scripts/validate-release-metadata.ps1`（Gate 的同一套通道推导）与 `scripts/ReleaseManifest/`
  （生成/校验清单与签名）可直接跑，改发布约定时要一起改。

## 命令

```powershell
dotnet build Corkboard.sln -v m                       # 全量构建
dotnet build Corkboard.Core\Corkboard.Core.csproj     # 单工程快速验证
dotnet test Corkboard.Core.Tests\Corkboard.Core.Tests.csproj
dotnet run --project Corkboard.Desktop\Corkboard.Desktop.csproj
```

## 注意事项

- **主窗口拖动用手动位移**（捕获指针 + `Window.Position`，见 `MainView`），不要用 `Window.BeginMoveDrag`：
  窗口置底到桌面后是桌面宿主的子窗口，原生标题拖动对它不生效。**只有标题栏按下即拖**，而且只在解锁态
  （`BasicSettingsConfig.WindowFixed` 为假）；整窗长按拖动已经下架，⚠️ 别为「哪里都能拖」把它加回来
  （会跟弹层、卡片拖动抢指针）。
- **主窗口的边缘缩放也由壳自己接管**（`MainView` 的 `TryStartWindowResize` / `MoveWindowResize`）：
  主窗口是 `WindowDecorations.None`——**不能**改成 `BorderOnly`，它会保留 `WS_THICKFRAME`，Windows 于是在
  客户区外留出一圈原生边框（实测左/右/下各 11 物理像素、上 1），而分层半透明窗口不画那一圈，
  看上去就是一整条黑边（用户反馈「窗口左下右都有黑边」）。去掉原生边框就没有原生缩放热区了，
  所以贴边 8 DIP 以内由壳接管按下与移动，光标跟着边缘换；位置补偿与拖动一样按**屏幕绝对位移**算
  （West/North 缩放会同时挪窗口，相对位移会自我抵消），尺寸受窗口 `MinWidth` / `MinHeight` 约束。
  命中判定只看**窗口**坐标（`e.GetPosition(window)` + `window.Bounds.Size`），别再用 `MainView` 的
  `Bounds.Size` 量一遍——原因见上一条「缩放命中判定一律用窗口坐标」。
- **作业板区块的拖动排序是「按住即拖」，不是长按**（`BoardPage`）：科目标题行整条是拖动面，
  按下就 `Capture(this)`，挪动超过阈值开始换位置、松手落盘。捕获挂**页面**上而不是手柄上——区块一换位置
  ItemsControl 可能把容器连同手柄一起重建，挂在那上面会收不到 `PointerReleased`，捕获留在页面上就是
  「整窗点不动」。壳不会再跟它抢：整窗长按拖动已下架，壳只在标题栏拖动。
  落位动画（谁给谁让位）由页面自己按帧算（`DispatcherTimer` + `TranslateTransform`）：
  Avalonia 12.1.1 的 `Animation.RunAsync` 只接受 `Visual`（传 Transform 直接 InvalidCastException），
  RenderTransform 也没有内置的过渡类型，别指望 `Transitions`。
  拖动能力可以在「设置 → 作业板 → 常规 → 拖动排序」关掉（`BoardSettingsConfig.SubjectReorderEnabled`，
  默认开）：关掉后手柄整个隐藏（`BoardSectionItem.CanReorder` 同时看这个开关与 `!_replay.IsActive`），
  `OnReorderPressed` 里还有一道兜底判断。**排序方式下拉里的「手动」不要跟着删**——那只是隐藏拖动入口，
  已经存下的手动顺序照旧生效；配置一变要 `RebuildNotes()`，因为 `CanReorder` 记在列表项上。
- **置底到桌面**只走 `WindowFeatures.DesktopBottom`（`Corkboard/Views/MainWindow.axaml.cs` +
  `Corkboard.Platforms.Windows/WindowsDesktopBottom.cs`）：Windows 嵌进 Progman/WorkerW，
  拿不到宿主时退化为 Z 序最底。
- **托盘图标是常驻入口**（`Corkboard/Services/Desktop/TaskBarIconService.cs`）：**左右键都弹菜单**——
  右键走平台原生菜单（`TrayIcon.Menu` = `NativeMenu`），左键走应用自己画的
  `Corkboard/Views/TrayMenuWindow.axaml(.cs)`（贴着鼠标弹出、条目与右键那份完全一致）。
  条目只有一张表 `TrayMenuItem`（`Services/Desktop/`），两份菜单都从它映射，**不要**在托盘那边另写一套。
  - 为什么左键要自己画：Avalonia 的 Win32 托盘只在右键时弹 `NativeMenu`（`WM_TRAYMOUSE` 的 lParam
    514 → `Clicked`、517 → 弹菜单），左键只报一次点击。判据是 `PlatformCapabilities.SupportsTrayMenuOnLeftClick`：
    Windows 为 `false`（自己画），macOS 的 NSStatusItem 与 Linux 的 SNI/AppIndicator 为 `true`
    （**那两边不要再挂 `Clicked`，否则会同时弹出两个菜单**）。
  - 左键菜单必须是**独立顶层窗口**（透明、无边框、不进任务栏、置顶）：主窗口可能根本看不见
    （收纳成浮钮、或「启动时显示主窗口」关着），挂在主窗口视觉树里的 Popup 会跟着一起没掉。
    卡片样式交给 `MenuFlyoutPresenter` 自己的模板——右键那份原生菜单也是它画的，两边长得一样。
    位置从鼠标右下方展开、越界往左上翻、最后夹进 `Screen.WorkingArea`；点任意一项 / Esc /
    失焦时自己关（`CloseMenu()` 幂等）。
  - 鼠标位置只能问平台（Avalonia 的 `MouseDevice` 只给「某个 TopLevel 里的本地坐标」，窗口没被划过
    就是旧值）：走 `IPointerLocator`（`Corkboard.Platforms.Abstractions`，Windows 用 `GetCursorPos`，
    其它平台 `UnsupportedPointerLocator`），拿不到时退回主屏右下角——**不要**因此把菜单藏起来。
  - ⚠️ `x:Name` 生成的字段在**自己写了 `InitializeComponent()`** 的窗口里恒为 null（生成的那条赋值语句
    不会跑），取命名控件一律 `this.FindControl<T>("名字")`（`TrayMenuWindow` 的 `MenuPresenter` 就是这么写的）。
  - **作业板那一项是一颗开关，不是固定一句文案**：板子在屏幕上时写「隐藏作业板」、点下去
    `App.HideMainWindow()` 收起来；不在屏幕上时写「显示作业板」、点下去 `App.ShowMainWindow()`
    （先看 `BoardDockService.IsDocked` 决定是否 `Expand()`，再走 `MainWindow.ShowFromDock()`
    重放置底 / 不透明度——隐藏会摘掉这些平台样式，光 `Window.Show()` 补不回来。
    **不要**在托盘那边另写一套「显示窗口」）。
    - 判据是 `App.IsMainWindowShown`（`Corkboard/App.axaml.cs`）：窗口不可见、`WindowState == Minimized`、
      或 `BoardDockService.IsDocked` 三条任一为真就是「不在屏幕上」——三种状态下用户看到的都是
      没有板子，菜单就不该再说「隐藏」。只给「显示」用户没法从托盘收掉板子，只给「隐藏」则收掉之后
      再也叫不回来（主窗口没有任务栏按钮），所以必须二选一。
    - 隐藏走 `App.HideMainWindow()` → `MainWindow.HideForDock()`：窗口只藏起来不销毁，位置保留，
      顺带收掉收纳浮钮（它是独立窗口，只藏主窗口会把它留在屏幕上当孤儿）。
  - ⚠️ **原生菜单是快照，显隐一变就要重建**：`trayIcon.Menu` 是启动时按当次条目建出来的，
    主窗口藏起来 / 叫回来 / 最小化 / 还原 / 收纳展开都不改配置，不主动刷新就会一直挂着旧文案。
    `MainWindow` 在 `Loaded`、`WindowStateProperty` 变化、`HideForDock()`、`ShowFromDock()`、`OnClosing()`
    五处调 `NotifyBoardVisibilityChanged()` → `TaskBarIconService.NotifyBoardVisibilityChanged()`
    → `RefreshMenu()`（重建 `NativeMenu` + 关掉已弹出的自绘菜单）。改这五处要一起看。
  - 「启用作业板」关着时整项不列（`CreateMenuItems(bool boardEnabled, bool boardShown)`），考试看板与设置照旧。
  - 菜单文案复用 Core 的 `Menu_*` 键；平台能力看 `PlatformCapabilities.SupportsTrayIcon`。
- **窗口图标与托盘图标一律用 PNG**（`Corkboard/Services/Desktop/AppIcon.cs`，母图
  `Corkboard/Assets/Icons/Corkboard.png`）：`MainWindow` 构造里 `Icon = AppIcon.LoadBitmap()`、托盘用
  `AppIcon.LoadWindowIcon()`——**不要再挂 ICO**。⚠️ `FAAppWindow.Icon` 的声明类型是 `IImage`（它把基类
  `Window.Icon` 藏起来了），所以窗口那边给 `Bitmap`，只有托盘（`TrayIcon.Icon` 是 `WindowIcon`）才包一层。
  exe 文件图标（`Corkboard.Desktop.csproj` 的 `ApplicationIcon`）仍是 ICO——PE 资源只认 ICO，别跟着改。
- **开关右侧不显示 On/Off 文案**：`StylesBase.axaml` 已全局清空 `ToggleSwitch` 的
  `OnContent`/`OffContent`，页面里不要再逐个开关写这两个属性。
- 版本元数据由 git 提供，**机制与上游 SecRandom-C 完全一致**：头程序集（当前只有
  `Corkboard.Desktop`，工程里 `EnableGitInfoGenerator=true`）在构建期由 NuGet 包
  `SecRandom.SimpleGitInfoGenerator` 跑 `git describe/rev-parse`，生成 internal 的
  `SecRandom.GitInfo`（Tag/Branch/CommitHash/Version/AssemblyVersion）；根 `AssemblyInfo.cs`
  把它写成 `AssemblyVersion` 与 `AssemblyInformationalVersion`（`tag+commit`）。
  仓库内**不再有**自己的 roslyn 生成器 / `GitInfo.props`（已删）。类库通过
  `GlobalConstants.SetVersionAssembly` 读头程序集，`GlobalConstants.ReadMetadata` 反射的是
  `SecRandom.GitInfo.Branch`；没有 git 仓库或没有 tag 时回退成 `0.0.0` / `Unknown`，构建不失败。
- **Debug 构建的左下角版本水印**：两个壳（`MainView` / `SettingsView`）在 `Loaded` 里按
  `GlobalConstants.IsDevelopment` 往壳的 `AdornerLayer` 塞一个 `DevelopmentBuildAdorner`
  （`Corkboard.Core/Controls/`，模板在同一个目录的资源字典里、由 Core 的
  `StylesBase.axaml` 合并进应用资源；文案走 Core 公共 resx 的 `Debug_DevelopmentBuild_Notice`，
  第二行是 `GlobalConstants.VersionLong`）。写法照上游 SecRandom-C：Release 构建不注册，
  装饰层 `IsHitTestVisible=False`——它铺满整壳，吃掉命中测试就等于整窗点不动。
  独立小弹层窗口（`PageOverlayWindow`）不加：它是壳内表单的承载窗口，不是给用户看的「窗口」。
- `NuGet.config` 只声明公共源（nuget.org + ClassIsland MyGet）。**不要**把开发机专属的
  `fallbackPackageFolders` 提交进去（NuGet 遇到不存在的目录会直接 NU1301，CI 与其他机器都还原不了）。
  本机想复用包缓存用环境变量 `NUGET_FALLBACK_PACKAGES`。
- **测试不要依赖机器语言**：`.github/workflows/Build.yml` 跑在 en-US 的 Windows runner 上，而种子科目这类
  默认值取自本地化资源（`CR.Board_Seed_Subject_*`）。写死中文的断言在 CI 上必挂：`SetSubjectOrder`
  会把「不在科目清单里」的条目当已删除的科目清掉（曾因此红了 5 天）。改完测试在本机这样复现 CI 的语言环境：
  `Corkboard.Core.Tests\bin\Release\net10.0\Corkboard.Core.Tests.exe -culture en-US`
  （xUnit v3 的 runner 自带 `-culture` 开关，可用 `default` / `invariant` / `en-US`）。
- **发布与自动更新**见上一节「发布与自动更新」。关键坑：①job 级环境变量叫 `RELEASE_TAG` 而不是 `VERSION`
  ——MSBuild 会把环境变量当成同名属性读进去，`VERSION=v0.2.0` 会让 `dotnet restore` 直接报
  「"v0.2.0" 不是有效的版本字符串」；②`actions/checkout` 必须 `fetch-depth: 0`，
  因为版本号是构建期由 git 提供的（`SecRandom.SimpleGitInfoGenerator` 跑 `git describe`，浅克隆可能没有 tag）；
  ③**标签要打在你真正要发布的那次提交上**：仓库里的 `v0.1.0` 指向骨架首版，那份 `NuGet.config` 里有开发机
  专属的 `fallbackPackageFolders`，拿它派发必然 `NU1301: The local source 'D:\code\SecRandom-C\.nuget\packages' doesn't exist`。
- SECTL 平台标识（`GlobalConstants.PlatformId`，用于版本上报）与 OAuth 客户端 ID
  （`Corkboard/Services/Auth/SectlAuthEndpoints.cs`）是两个不同的值，不要混用。

## 详细约定在哪（按子系统拆开了）

根目录这份文件放**跨层**的规矩（技术栈、结构、域归类、设置怎么加、本地化、命令）。
各子系统的细节拆到了子目录的 `AGENTS.md`：在那里干活时它会被自动加载，别在根文件里再抄一遍。

| 位置 | 管什么 |
| --- | --- |
| `Corkboard.Core/AGENTS.md` | 配置管道与自动保存、本机专属项 `[LocalOnly]`、历史存储与时空回放、字体、平台统计上报 |
| `Corkboard.Core/Exam/AGENTS.md` | 考试看板（多份看板、编辑窗口、快速布置、模板、全屏三档、自动放映、导入导出、看板字号）与 NTP 校时 |
| `Corkboard/AGENTS.md` | 壳层与导航（主界面/设置界面标题栏、那颗锁、收纳、弹层、账号与云同步页）、应用图标、首次运行引导（OOBE） |
| `Corkboard/Views/AGENTS.md` | 看板模式（全屏大时钟 + 一言/天气）、作业板界面功能（便签、富文本、手写、排序、清理入口、导出） |

**文里说「见「## 某某」」时**：先在根目录这份里找，没有就按上表去对应的子目录 `AGENTS.md` 找；
逐条「哪次改动动了什么」的修订历史在根目录 `AGENTS-HISTORY.md`。

改结构、改导航/DI 规则、改本地化布局或模块边界时，**同一次改动里**把受影响的 `AGENTS.md` 一起改掉
（可能不止一份）。根目录这份有 64 KB 的加载预算，已经贴得很紧——**新增内容优先写进子目录那一份**，
不要往这里堆。
