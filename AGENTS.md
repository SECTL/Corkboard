# PROJECT KNOWLEDGE BASE

<!--
维护契约：
- 保持这份文件“耐久”：写稳定约定，不写某台机器上的临时观察。
- 改动项目结构、目标框架、本地化布局、工作流、导航/DI 规则或模块边界后，
  必须在同一次改动里更新本文件（以及受影响的嵌套 AGENTS.md）。
- 根目录规则对整个仓库生效；子目录 AGENTS.md 补充子树规则。
- 约定冲突时，`docs/project_rules.md` 是最终依据。
-->

**Last Update:** 2026-10（主界面去掉侧边导航栏，只承载默认主页面；设置界面保留导航栏、顶部「收纳」与返回按钮（返回箭头改由 Core 样式模板画），设置项改为平铺、不再分组；设置页改设置卡；应用字体换成随包分发的 MiSans；作业板：作业类型 + 字段值输入（数字用 NumericUpDown 自带步进键，不再自绘数字小键盘）、**取消作业名与完成勾选**、**同科目合并成一个区块**、作业内容多行可拖宽拖高（布置作业卡片有最小宽度／最大宽度／最大高度，上限按**看得见的宿主**算：壳内弹层取窗口内容区、只占其 70%，置底形态退回屏幕工作区，超了卡片内部滚动）、**弹层卡片整张都是拖动面**（按住卡片上输入控件以外的地方都能整张拖走，不再有抓手图标；位移夹在宿主里，卡片尺寸一变就重夹，置底形态下改拖独立窗口）、自绘页面级弹层、纯文字便签、三种排布（**排布切换在主页面顶部已取消，改到「设置 → 作业板」下拉框里选**；成排布局里每个区块各自定高，不跟着同行最高的那块变长）、排序与删除确认、名称/科目/类型可配；**作业内容支持 Markdown 渲染**（Markdig，见 Core 的 `MarkdownInlineRenderer` / `MarkdownTextBlock`），并且可以给选中的字/段单独改颜色与字号、给整条作业定「整篇字号」，默认字号与默认颜色在「设置 → 作业板 → 内容默认样式」里配；**格式不在卡片上常驻工具栏**：选中文字后在选段上方弹一小条格式浮窗（第一行 字号·预设色板，第二行 加粗·斜体·清除格式），Markdown 记号由 `BoardMarkdownEditing` 直接写进原文（同一个按钮再点一次取消）；**作业数据移出 `data/config/`**，按创建日期归档到 `data/board/<年>/<月>/<日>/notes.json`；**时空回放**放在主界面标题栏右上角，连点两次才进入、超时自动退回，没作业时不显示入口；Debug 构建在壳的左下角加版本水印，照上游 SecRandom-C 的 `DevelopmentBuildAdorner`；外观设置补齐主题模式（跟随系统 / 浅色 / 深色）与主题色（跟随系统 / 自定义）；**基础设置新增主窗口不透明度（滑杆，落到原生窗口）与点击穿透**，主界面标题栏右上角「布置作业」左侧加设置按钮（打开设置窗口））

## 这是什么

Corkboard 是 GPLv3 的 C#/.NET 桌面应用：一块桌面作业板（便签）。业务规格见 `docs/board.md`，
工程规则见 `docs/project_rules.md`。业务只有作业板（`Board`）一块，其余是框架层。

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
├── Corkboard/                  # 应用层：App 宿主、视图/ViewModel、应用服务（Auth、Ipc、Desktop）、Langs、Assets
├── Corkboard.Core/             # 核心：契约、配置/日志、领域服务、可复用控件与样式、Langs/Common
├── Corkboard.Shared/           # 跨工程契约：ConfigBase、Utils（数据根/路径）。禁止 UI 依赖
├── Corkboard.Desktop/          # 入口：Program.cs 选择平台根并启动 Avalonia 桌面生命周期
├── Corkboard.Platforms.Abstractions/ # 平台无关契约（IPlatformServiceRoot、窗口能力）
├── Corkboard.Platforms/        # 启动上下文、DI 桥接、不支持平台的 stub
├── Corkboard.Platforms.Windows|Linux|MacOs/ # 各平台原生窗口能力实现边界
├── Corkboard4Ci.Interface/     # ClassIsland 插件侧 IPC 契约（[IpcPublic]）
├── Corkboard.Core.Tests/       # xUnit v3 测试
├── docs/                       # 工程规则、作业板规格、待办
├── Global.props                # 共享 MSBuild 策略（各工程 Import）
├── Directory.Build.props       # AvaloniaVersion 锚点
└── Platforms.props             # CorkboardPlatform 选择 + CORKBOARD_PLATFORM_* 宏
```

依赖方向单向：`Shared` ← `Core` ← `Corkboard` ← `Desktop`；`Platforms.Abstractions` 不依赖 Core。

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
`GroupId` 为空时注册表生成的是顶层导航项，设置界面侧边栏就是平铺的（当前状态）。
**不要**手写 `PagesRegistryService` 的集合。注册会同时写导航注册表和键控 DI，两者必须成对。
只有同一域真的有两页以上、需要在侧边栏折叠归组时，才 `AddGroup(new PageGroupInfo(...))` 并给页面加
`groupId: "settings.<域>"`——单页分组只会白白多一个「点开才看见唯一子项」的 chevron。

**4）界面 = 设置卡** —— 页面根是 `ScrollViewer` 包一个
`<StackPanel Classes="page-container">`（Core 样式负责限宽居中与卡片间距），
每个设置项一张 `fa:FASettingsExpander`：`Header` 放名称、`Description` 放简介、`Footer` 放控件；
内容多的（列表、表单）放内容区，内容区可折叠。

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
- **不要直接双向绑 `TextBox.Text` 到配置**：`ConfigHandlerBase` 每次属性变化都会落一次盘（没有防抖），
  而 Avalonia 的 `UpdateSourceTrigger` 默认是 `PropertyChanged`——每敲一个字符就写一遍 `settings.json`。
  改成 VM 里的草稿属性 + 显式提交（回车 / 页面 `Unloaded`），范例见 `BoardSettingsPageViewModel.CommitBoardName`。
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
  此时 `PageOverlayService.UseSeparateWindow` 为真，表单由 `PageOverlayWindow`（无边框、不进任务栏、
  不透明）承载。标志只在**置底实际生效**时置真，并要结合请求方向判断（`Apply` 在关闭时也会把
  `DesktopBottom` 报成已应用）。详见 `docs/board.md`。
  ⚠️ **弹层与整窗拖动会打架**：壳的 `HoldToDragWindow` 长按会捕获指针，拖动结束**必须显式
  `Capture(null)`**——指针在窗口外抬起时收不到 `PointerReleased`，捕获留在壳上会让整窗点不动、
  输入框无法聚焦（表现就是「输入框打不了字」）；并且弹层打开时不参与长按拖动。
- **弹层卡片整张都能拖走**（`BoardAssignmentForm`）：**整张卡片就是拖动面**——按住卡片上任何
  非输入控件的地方（标题、行距、留白、预览区）都能拖，**照例不要放「抓手」图标**：那是「只有这里能拖」
  的暗示，反而让人以为别处拖不动，光标与 `Resources.Board_MoveSheet` 提示留在标题行上就够了。
  输入控件（`Button` / `ToggleButton` / `TextBox` / `ComboBox` / `NumericUpDown` / `RangeBase`）
  与右下角缩放手柄命中时让出，排除表见 `IsInteractiveSource`（加控件时要一起看）。
  ⚠️ **拖动处理器必须跟指针捕获挂在同一个元素上**（都挂在表单上）：拖动期间指针事件只从捕获元素
  往上走，处理器挂在卡片里的子元素上就一个都收不到，表现就是「按住拖不动」。
  壳内弹层只给卡片叠一层 `RenderTransform` 位移、布局仍居中不动，所以能挪多远得自己夹——
  走 `Corkboard.Core/Controls/SheetDragLimits.ClampOffset`（纯逻辑、有单测），
  而且**卡片尺寸一变、宿主尺寸一变都要重夹一次**（拉大或缩窗口之后位移会越界，卡片跑到视野外就
  再也抓不回来）。**置底形态下承载表单的是 `PageOverlayWindow`**，
  那个窗口 `SizeToContent`、大小正好等于卡片，挪卡片等于把卡片推出窗口被裁掉，
  所以那时拖的是**窗口本身**（`Window.Position` 手动位移，屏幕绝对坐标，同主窗口标题栏）。
  收尾同样要显式 `Capture(null)`（见上一条）。
- **弹层卡片的尺寸上限按「看得见的宿主」算**（`BoardAssignmentForm` + `SheetSizeLimits`）：
  宽度 = 设计上限（720）与「可用宽度 − 留边」取小；高度 = **可用高度 × 70%**（下限 320 托底）。
  基准优先取宿主窗口的内容区——卡片是居中的，只能占七成、上下各留一成半，窗口再大也不会长成
  一条贴边的竖长板子，窗口小也不会被裁掉一半；**但 `SizeToContent` 的独立窗口要跳过它自己的尺寸**
  （卡片长大 → 窗口跟着长大 → 上限又抬高，两边互相追着长），那条路退回屏幕工作区。
  超过上限的内容由卡片自己的滚动条吃掉。策略与夹取算式是纯逻辑、有单测
  （`SheetSizeLimits` / `SheetDragLimits`），**不要在 code-behind 里另写一套**。
  内容区（原文 + 预览）能拉多大 = 卡片上限扣掉<b>其它每一行的高度之和</b> + 行距 + 卡片内边距，
  那些行（**含底部「取消 / 保存」那一行**）是**逐个量 `Bounds.Height`** 的，
  **不要**用「内容栈整高减内容区」倒推：卡片顶到上限、内部开始滚动时内容栈的高度会被裁到视口那么高，
  倒推出来的「其它行高度」偏小，于是内容区能一路拉大、把底部按钮顶出卡片（踩过这个坑）。
- **格式浮窗只在选中文字之后出现**（`BoardAssignmentForm` 的 `FormatBar` + `BoardMarkdownEditing`）：
  卡片上**不再常驻**字号/颜色工具栏，也**没有** Markdown 语法提示行；在原文框里松开指针时若选区非空，
  就把一小条 `Popup` 弹到**选段上方**——锚点是 code-behind 挪到指针落点的 1×1 `Border`，`Placement=Top`。
  浮窗上的东西刻意少，**两行**摆：第一行「字号 + 颜色（预设色板）」，第二行「加粗 / 斜体 / 清除格式」；
  标题 / 列表 / 行内代码 / 整篇字号这些按钮都拿掉了（普通用户用不上，还占地方）。
  - ⚠️ **选区必须在弹出那一刻记下来**：点浮窗会让原文框失焦，之后再读 `SelectionStart/End` 可能已经是空的
    （表现就是「只选了一个字却改了整行」——空选区会退化成「光标所在的那一段」）。
    所以每次套格式之前先 `PushFormatSelection()` 把记下的选段塞回 ViewModel。
  - 浮窗的开合自己管：**不要**用 `Popup.IsLightDismissEnabled`——字号下拉与取色器是浮窗自己拉起的子弹层，
    会被当成「点了别处」把浮窗（连同下拉）一起收掉。做法是在**顶层**挂 Tunnel + `handledEventsToo` 的
    `PointerPressed` 来收，并按「按下是否落在浮窗内容那棵树里」（`IsWithinFormatBar`，按 `Popup.Child`
    判定，兼容独立弹层窗口与同窗浮层两种托管）+「有没有子弹层开着」让出；Esc 也能收。
  - **颜色用预设色板**：色块本身就是按钮，点一下直接套上——原来是「先点开取色器、再选色」两次点击，
    用户第一下点完以为没生效。色板以外的颜色才走最后那个「+」开取色器。
  - 字号下拉显示**选段当前正在生效的字号**（`BoardTextFormatEditing.ResolveEffectiveStyle` →
    `BoardAssignmentFormViewModel.ResolveSelectionFontSize`），**不要**像原来那样当菜单用、选完复位成空：
    那会让用户先得猜「现在是多少号」。同步显示时用 `_suppressSizeApply` 挡住，
    免得写进去的那次选择变化被当成用户操作又套一遍。
  - Markdown 记号（加粗/斜体，**夹住选段**）由 `Corkboard.Core/Services/Board/BoardMarkdownEditing.Toggle` 算，
    **同一个按钮再点一次就取消**；它返回新选区，界面据此把 `TextBox` 的选区摆回去，
    用户能接着点下一个格式。纯逻辑、有单测。
  - 「清除格式」只清**颜色/字号标注**（`BoardTextFormatEditing`），Markdown 记号在原文里，按同一个按钮取消。
- **transient 的 ViewModel 订阅单例，必须在页面离开可视树时断开**：ViewModel 由键控 DI 以 transient
  创建，而配置 handler 与领域服务是单例，直接 `+=` 会让每开一次页面就往单例上多挂一个处理器
  （配置被重复保存、页面回收不掉）。做法是 VM 上留 `Detach()`，页面 `Unloaded` 里调用。

## 壳层与导航

- **主界面没有侧边导航栏**：`MainView` 里只有「标题栏 + 页面标题 + `FAFrame`」，
  内容区承载默认主页面（`AppConsts.DefaultMainPageId`）；其它主页面只能靠
  `MainView.SelectNavigationItemById(id)` 程序化切换，用户可见入口只有托盘菜单。
- **设置界面才有侧边导航栏**：`SettingsView` 里是 `FANavigationView`（`PaneDisplayMode="Left"`）
  + `MenuItemsSource` / `FooterMenuItemsSource`，内容区是 `FAFrame`。
  设置项当前是**平铺**的（页面 `[PageInfo]` 不带 `groupId`，侧边栏没有可折叠分组）。
- **设置界面标题栏有返回按钮**：`BackButton` 挂 `Classes="nav-back"`，`IsVisible` 绑
  `ViewModel.CanGoBack`，返回栈是 `SettingsViewModel.NavigationHistory`。箭头由
  `Corkboard.Core/Styles/NavigationBackButton.axaml` 的模板画（沿上游 SecRandom-C 的写法），
  所以按钮上**不要**再写 `Content` 字形或 `FontFamily`。
- **顶部「收纳」**：只有设置界面标题栏里有 `PaneToggleButtonStyle` 按钮，
  点一下切换 `NavigationView.IsPaneOpen`，折叠 / 展开设置导航栏。
- **主界面标题栏右上角承载主页面动作**：「设置」「布置作业」与「时空回放」都在这里。
  「设置」是主窗口里唯一的设置入口（`App.ShowSettingsWindow()`，另一个入口在托盘菜单），
  必须在「布置作业」左边、标 `ElementRole="User"`。
  作业板页面上没有任何按钮：连排布切换也搬进了设置（「设置 → 作业板 → 排布」下拉框，写回
  `BoardSettingsConfig.LayoutMode`），页面里只剩列表与一句回放提示。
  标题栏按下即拖动，所以这些按钮一律要标 `WindowDecorationProperties.ElementRole="User"`，
  并进 `MainView.IsInteractiveSource` 的排除表（`Button` / `ToggleButton` / `TextBox` / `ComboBox` / `Slider`），
  漏一个那个控件就点不动。主窗口是 `WindowDecorations.None`，没有系统按钮，所以右侧可以放东西
  （设置窗口相反，见下）。
- **主窗口不透明度与点击穿透都是窗口能力**（「设置 → 基础设置」里的两张卡）：
  `BasicSettingsConfig.MainWindowOpacity`（0–1 的标量）与 `ClickThrough`（开关）由 `MainWindow`
  在 `Opened` 与应用中把请求转给 `IWindowFeatureService`（不透明度走
  `WindowFeatures.WindowOpacity` + `WindowFeatureRequest.Opacity` 这个标量参数）。
  - 平台不支持的能力**整张卡隐藏**（`SupportedFeatures` 里没有对应位）：Linux 两样都没有，
    macOS 与 Windows 两样都有。
  - Windows 上两样共用 `WS_EX_LAYERED`：**只设样式不调 `SetLayeredWindowAttributes`，
    窗口会整体不显示**，所以 `TrySetExtendedStyles` 与 `TrySetWindowOpacity` 都要按同一份
    alpha 补一次分层属性；不透明度下限在平台侧兜底（设置页滑杆下限更高，20%）。
  - 点击穿透开着时主窗口一个控件都点不动，所以**标题栏右上角整块隐藏**
    （`MainView` 右侧动作区 `IsVisible` 绑 `!ViewModel.Config.Basic.ClickThrough`），
    不留一排点不动的按钮误导用户；设置只能从托盘菜单打开。
  - **锁定徽标必须放在主窗口之外的独立顶层窗口**（`Corkboard/Views/ClickThroughLockWindow.axaml`，
    由 `MainWindow.SyncLockBadge` 管理）：点击穿透是整窗的，画在主窗口里的「开锁」按钮自己也点不动，
    而需求就是「穿透开着时这颗按钮仍然可点」。它按主窗口标题栏最右上角实时摆位
    （`PlaceAtTitleBarCorner` 用 `PointToScreen`，**不能用 `Window.Position` 加偏移**——置底后主窗口是
    桌面子窗口，子窗口坐标不是屏幕坐标），所以主窗口 `PositionChanged` / `ScalingChanged` /
    `BoundsProperty` / `WindowStateProperty` 每一条变化都要重摆或收放（`SyncLockBadge`）：
    独立窗口不会自己跟着走。**这套「主动同步」不能省**，位置只在启动时算一次的版本表现为
    「按钮不跟随主窗口」。
  - ⚠️ **最小化会摘掉平台样式，还原时必须重放窗口能力**：Windows 在最小化时清除
    窗口的 `WS_EX_LAYERED` / `WS_EX_TRANSPARENT`，还会把置底窗口的 `WS_CHILD` 摘掉（父窗口变回
    `0x0`），这些都不会自己回来。所以 `MainWindow` 在 `WindowState` 变成非最小化时重放
    `ApplyPinToDesktop()` / `ApplyClickThrough()` / `ApplyWindowOpacity()`（幂等）；`WindowsDesktopBottom.TryAttach`
    也不能只信自己那张「已挂载」表，要用 `GetParent` 核对实际父窗口，否则会跳过重挂。
    漏掉这一环的后果是：配置写着穿透/不透明度/置底，界面也这么显示，窗口实际已经全部失效。
  - 不透明度滑杆与取色器同一套做法：绑 VM 草稿 + 400ms 防抖，离开页面 `FlushOpacityDraft()`，
    **不要直接双向绑配置**。
- ⚠️ **设置界面标题栏右侧放控件，必须先让出系统按钮那一带**：`FAAppWindow` 的最小化/最大化/关闭按钮
  画在标题栏右端、盖在页面内容**上面**，而本窗口 `ExtendsContentIntoTitleBar`，页面内容一直铺到标题栏底下，
  靠右的控件会被那三个按钮压住（表现为按钮和 × / 方块叠在一起）。这一带的宽度**不许写死常量**
  （随系统与缩放变），走平台能力 `IWindowFeatureService.GetSystemCaptionButtonWidth`
  （Windows 查 DWM 的系统按钮矩形，其它平台没有系统按钮时返回 0）；`SettingsView` 在 `Loaded` 里
  按它给右侧动作区（`TitleBarActionArea`）补右边距，「需要重启」提示因此正好落在最小化按钮左边，
  既不被吞掉、也不被推到最左边。以后往设置标题栏右侧加控件，放进 `TitleBarActionArea` 即可。
- **导航项来自注册表**：`PagesRegistryService` 的集合经 `PageItemsExtensions.ToNavigationViewItems`
  转成设置界面的导航项，按 `PageLocation.Top` / `Bottom` 分到上半区与底部。
  运行期不要增删导航集合，需要动态显隐就改 `IsVisible`。
- **页面实例走键控 DI**，`PageInfo.Id` 就是 DI 的 key；页面工厂在 `MainView` / `SettingsView` 里。
- **页面自己不要画标题**：两个壳都会渲染标题（主界面用 `MainViewModel.PageTitle`，作业板名称可在设置里改；
  设置界面用 `SelectedPageInfo.Name`），页面里再写一遍 `Classes="h1"` 就是重复；
  确实要隐藏就设 `PageInfo.HidePageTitle`。
- ⚠️ **设置界面选中导航项前必须保证容器已实例化**（否则窗口卡死）：初始选中推迟到首次布局之后，
  分组内的页面先用 `ExpandGroupsContaining` 展开祖先分组并走一次 `UpdateLayout()`。
  原因见 `docs/project_rules.md` 第 3 节。

## 配置管道与自动保存

三类：`ConfigBase`（模型基类，`Corkboard.Shared`）、`ConfigServiceBase`（读写实现）、
`ConfigHandlerBase<T>`（自动保存句柄）。落盘路径 `data/config/settings.json`。

- **自动保存覆盖到子配置**：`ConfigHandlerBase` 递归订阅根对象与任意深度的 `INotifyPropertyChanged`
  子对象，所以 `General.Basic.*` 这类改动会自动写盘，加子配置不需要额外接线。
- **但集合只订阅集合本身，不订阅集合里的元素**：`ObservableCollection<T>` 的增删会自动落盘，
  改元素字段不会。需要可编辑元素时自己在 VM 里订阅元素属性并显式 `MainConfigHandler.Save()`
  （范例见 `BoardSettingsPageViewModel.SaveAll`）。
- **业务数据不进 `settings.json`**：作业板数据全在 `data/board/`（与设置文件的 `data/config/` 分开），
  由 `BoardService` 在每次变更后落盘（`IBoardService.Types` / `Subjects` 是「调用方直接改集合、
  改完调 `Save()`」的用法）。这样绕开了上一条的坑，也把「应用偏好」和「用户数据」分开。
  设置页负责编辑它们，但**存储位置不等于设置项**：编辑界面在设置里，数据仍在 `data/board/`。
- **作业按创建日期分文件归档**：`data/board/<年>/<月>/<日>/notes.json`（`BoardNoteStore`），
  一天一个文件、同一天的多条作业合并其中；类型与科目这类**定义**才写单份 `data/board/board.json`。
  作业从内存整份重写时，没有落笔的旧日期文件会被删掉、空目录逐级回收。
  旧版挤在 `data/config/board.json` 里的数据由 `BoardService` 在归档为空时一次性搬迁（见 `LegacyBoardFile`）。
- 配置文件一律经配置服务（原子写入 + 统一 JSON 选项），**不要自己 `File.WriteAllText` 写配置**。
- **作业内容是「Markdown 原文 + 一层格式标注」**：`BoardNote.Content` 是唯一的文本真源，
  局部颜色/字号记在 `BoardNote.Formats`（`BoardTextFormatRange`：`start` / `length` / `color` / `font_size`，
  按原文偏移定位），整篇字号记在 `BoardNote.ContentFontSize`。**不要把样式塞进文本**——
  原文必须始终是可读、可编辑、可粘贴的 Markdown；渲染时才把标注套到对应字符上
  （Core 的 `MarkdownInlineRenderer` 解析 Markdig、`MarkdownTextBlock` 显示，两边共用同一个控件，
  预览与板子上看到的一定一致）。文本一改，标注由 `BoardTextFormatEditing.Shift` 按公共前后缀挪位，
  **不允许**在别处另写一套偏移换算；格式规则的默认值（默认字号 / 默认颜色）才是**设置**，
  落在 `settings.json` 的 `board_settings.default_content_*`，标注本身跟着作业存在 `notes.json` 里。
- 数据根只能通过 `Corkboard.Shared/Utils.cs` 访问，新增持久化目录走 `Utils.GetFilePath/GetDirectoryPath`。
- 枚举按数字落盘，因此只能在末尾增删枚举成员。

## 字体

- 应用字体是随包分发的 **MiSans**（`Corkboard/Assets/Fonts/MiSans/`，10 个字重，约 78 MB），
  许可见 `THIRD-PARTY-NOTICES.md`。字体靠 `Assets\**` 通配打进程序集，加一个字重就直接放大 `Corkboard.dll`。
- 换字体只有四处挂点，缺一不可：
  1. `Corkboard.Desktop/Program.cs` 的 `FontManagerOptions.DefaultFamilyName`
     （定首帧默认字体，取 `GlobalConstants.DefaultFontFamily`）；
  2. `Corkboard/App.axaml` 的 `AppFontFamily`；
  3. 同文件的 `ContentControlThemeFontFamily`——**这是 FluentAvalonia / Avalonia 控件主题取字体的键，
     不覆盖它等于没换**，且必须与 `AppFontFamily` 同值；
  4. `Corkboard.Core/Styles/StylesBase.axaml` 的 `:is(Window)` 与 `fa|FAContentDialog` 两条样式。
- ⚠️ MiSans 只有 `Regular` / `Bold` 属于 `MiSans` 家族，其余字重是**独立家族**
  （`MiSans Medium`、`MiSans Semibold`…）。实测 Avalonia 按 name ID 1 匹配，
  所以 `#MiSans` 只能解析到 Regular / Bold，SemiBold 请求会落到 Bold。
  要让某个字重真正生效，得把它当独立家族名来引用。
- 图标字体是另一个键（`FluentSystemIconsResizeable`），别和应用字体混用；
  页面里不要写死 `FontFamily`。
- ⚠️ **图标字形只能查 Fluent System Icons 映射表**：常量在 `Corkboard.Core/Icons/FluentIcons.cs`，
  完整表在上游 `SecRandom-C/SecRandom.Core/Assets/FluentSystemIcons-Resizable.json`
  （两边字体文件字节一致，码位可直接对照）。**不要照抄 Segoe MDL2 / Segoe Fluent Icons 的码位**——
  例：`E72B` 是 MDL2 的「返回」，在本字体里是文档图标，画出来完全不是那个意思。
  能用样式模板里的 `FluentIcons.*` 常量就别在 XAML 里手写 `&#xXXXX;`（范例：`Button.nav-back` 画返回箭头）。
- **主题与主题色**（设置 → 个性化 → 外观）：`Appearance.Theme`（跟随系统 / 浅色 / 深色）与
  `ThemeColorMode`（跟随系统 = 系统个性化里用户挑的主题色 / 自定义 = `ThemeColor`）由
  `App.ApplyThemeSettings` 写到 `FluentAvaloniaTheme` 上，写法照上游 SecRandom-C，**顺序不能颠倒**：
  先设 `PreferSystemTheme` 再给显式变体（反了会被 FluentAvalonia 的系统跟踪覆盖）；
  「自定义」要先关 `PreferUserAccentColor` 再写 `CustomAccentColor`，切回「跟随系统」必须把
  `CustomAccentColor` 清成 `null`，否则自定义色一直挂在主题上。取色器绑 VM 的草稿
  `ThemeColorDraft` + 400ms 防抖、离开页面时 `FlushThemeColorDraft()`，
  **不要直接双向绑配置**（拖光谱每帧都变，`ConfigHandlerBase` 没有防抖）。
- **用户可换字体**：设置 → 个性化 → 外观（`Views/SettingsPages/Personalized/AppearanceSettingsPage.axaml`）
  选字体族与字重，分别落到 `AppearanceSettingsConfig.Font`（哨兵值 `FontFamilyCatalog.DefaultFontSentinel`
  表示随包字体，其余是系统家族名）与 `FontWeight`；由 `App.RefreshAppearanceSettings` 写回上面那三个资源，
  改完立即生效、不用重启。候选表由 `FontFamilyCatalog.NormalizeFamilyNames` 过滤
  （去掉 `compositefont:` 前缀与 `@` 竖排变体）并排序后生成，纯逻辑、有单测。

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

## 命令

```powershell
dotnet build Corkboard.sln -v m                       # 全量构建
dotnet build Corkboard.Core\Corkboard.Core.csproj     # 单工程快速验证
dotnet test Corkboard.Core.Tests\Corkboard.Core.Tests.csproj
dotnet run --project Corkboard.Desktop\Corkboard.Desktop.csproj
```

## 注意事项

- **主窗口拖动用手动位移**（捕获指针 + `Window.Position`，见 `MainView`），不要用 `Window.BeginMoveDrag`：
  窗口置底到桌面后是桌面宿主的子窗口，原生标题拖动对它不生效。标题栏按下即拖；其余位置在
  `BasicSettingsConfig.HoldToDragWindow` 打开时长按判定后拖动（**整窗任意位置**，靠移动阈值让出）。
- **作业板区块的拖动排序是「按住即拖」，不是长按**（`BoardPage`）：科目标题行整条是拖动面，
  按下就 `Capture(this)`，挪动超过阈值开始换位置、松手落盘。捕获挂**页面**上而不是手柄上——区块一换位置
  ItemsControl 可能把容器连同手柄一起重建，挂在那上面会收不到 `PointerReleased`，捕获留在页面上就是
  「整窗点不动」。标题行标了 `WindowDragGesture.Suppress`，壳的整窗长按拖动会让出（两者判定不再撞车）。
  落位动画（谁给谁让位）由页面自己按帧算（`DispatcherTimer` + `TranslateTransform`）：
  Avalonia 12.1.1 的 `Animation.RunAsync` 只接受 `Visual`（传 Transform 直接 InvalidCastException），
  RenderTransform 也没有内置的过渡类型，别指望 `Transitions`。
- **置底到桌面**只走 `WindowFeatures.DesktopBottom`（`Corkboard/Views/MainWindow.axaml.cs` +
  `Corkboard.Platforms.Windows/WindowsDesktopBottom.cs`）：Windows 嵌进 Progman/WorkerW，
  拿不到宿主时退化为 Z 序最底。
- **托盘图标是常驻入口**（`Corkboard/Services/Desktop/TaskBarIconService.cs`）：显示主窗口 / 打开设置 /
  重启 / 退出。菜单文案复用 Core 的 `Menu_*` 键；平台能力看 `PlatformCapabilities.SupportsTrayIcon`。
- **开关右侧不显示 On/Off 文案**：`StylesBase.axaml` 已全局清空 `ToggleSwitch` 的
  `OnContent`/`OffContent`，页面里不要再逐个开关写这两个属性。
- 版本元数据由 git 提供：`GitInfo.props` 在构建期跑 git，生成器把 tag/分支/提交号写进
  `Corkboard.GitInfo`，只有 `EnableGitInfoGenerator=true` 的头程序集（当前 `Corkboard.Desktop`）
  这样做。没有 git 仓库或没有 tag 时回退成 `0.0.0` / `Unknown`，构建不失败。
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
- SECTL 平台标识（`GlobalConstants.PlatformId`，用于版本上报）与 OAuth 客户端 ID
  （`Corkboard/Services/Auth/SectlAuthEndpoints.cs`）是两个不同的值，不要混用。
