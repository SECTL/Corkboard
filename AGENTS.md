# PROJECT KNOWLEDGE BASE

<!--
维护契约：
- 保持这份文件“耐久”：写稳定约定，不写某台机器上的临时观察。
- 改动项目结构、目标框架、本地化布局、工作流、导航/DI 规则或模块边界后，
  必须在同一次改动里更新本文件（以及受影响的嵌套 AGENTS.md）。
- 根目录规则对整个仓库生效；子目录 AGENTS.md 补充子树规则。
- 约定冲突时，`docs/project_rules.md` 是最终依据。
-->

**Last Update:** 2026-10（主界面去掉侧边导航栏，只承载默认主页面；设置界面保留导航栏、顶部「收纳」与返回按钮（返回箭头改由 Core 样式模板画），设置项改为平铺、不再分组；设置页改设置卡；应用字体换成随包分发的 MiSans；作业板：作业类型 + 字段值输入（数字用 NumericUpDown 自带步进键，不再自绘数字小键盘）、**取消作业名与完成勾选**、**同科目合并成一个区块**、作业内容多行可拖宽拖高（布置作业卡片有最小宽度／最大宽度／最大高度，上限按**看得见的宿主**算：壳内弹层取窗口内容区、只占其 70%，置底形态退回屏幕工作区，超了卡片内部滚动）、**弹层卡片整张都是拖动面**（按住卡片上输入控件以外的地方都能整张拖走，不再有抓手图标；位移夹在宿主里，卡片尺寸一变就重夹，置底形态下改拖独立窗口）、自绘页面级弹层、纯文字便签、三种排布（**排布切换在主页面顶部已取消，改到「设置 → 作业板」下拉框里选**；成排布局里每个区块各自定高，不跟着同行最高的那块变长）、排序与删除确认、名称/科目/类型可配；**作业内容是所见即所得富文本**（第三方 `AvaloniaRichEditor`，由 Core 的 `RichTextBlock` 承载，落盘成 HTML 片段；旧的 Markdown 渲染管线与相关单测已整体删除，旧作业由 `LegacyBoardContentConverter` 一次性迁移），可以给选中的字/段单独改颜色与字号，默认字号与默认颜色在「设置 → 作业板 → 内容默认样式」里配；**格式工具栏常驻在编辑区正上方**（库自带的 `RichEditorToolbar`，`Target` 在 code-behind 里指向编辑控件，文案由 Core 的 `RichTextLocalization` 跟着界面语言切），加粗/斜体/下划线/删除线、文字颜色、高亮、列表、缩进、行距、对齐、字号/字体/段落样式与插入表格图片都在上面，点一下直接对当前选区生效；**作业内容只有一个框**：就是一个始终可编辑的富文本编辑器，没有「原文态/效果态」切换；**作业数据移出 `data/config/`**，按创建日期归档到 `data/board/<年>/<月>/<日>/notes.json`；**时空回放**放在主界面标题栏右上角，连点两次才进入、超时自动退回，没作业时不显示入口；Debug 构建在壳的左下角加版本水印，照上游 SecRandom-C 的 `DevelopmentBuildAdorner`；外观设置补齐主题模式（跟随系统 / 浅色 / 深色）与主题色（跟随系统 / 自定义）；**基础设置新增主窗口不透明度（滑杆，落到原生窗口）与点击穿透**，主界面标题栏右上角「布置作业」左侧加设置按钮（打开设置窗口）；**基础设置新增「右下角布置作业按钮」**，打开后主窗口右下角浮一颗圆角加号（`MainView` 里叠在内容之上的一颗按钮，弹层遮罩盖得住它，回放/点击穿透时随其它入口一起隐藏，点一下开出布置作业表单）；**设置页「关于」页复刻上游 SecRandom-C**，改应用信息／作者／版本信息三张卡，外部链接经新服务 `IExternalLauncher`（`Corkboard/Services/Desktop/`）交给系统默认程序打开）；**作业板新增「自动清理过期作业」**（设置 → 作业板：开关（默认开启）+ 每天/每周某天某时刻，默认 4:00，用 `TimePicker`），到点只给**过了截止日期**的作业打上 `CleanedAt` 标记、从主页面拿掉（数据原地不动、不搬目录、不删文件，回放里照旧看得到），**判定只看截止日期**（没填截止日期的作业永远不会被自动清掉，到期当天也不算、第二天才算）；卡头的「立即清理」不看时刻、点一下当场把过了截止日期的清掉；**作业新增可选「截止日期」**（`BoardNote.DueDate`，布置作业表单里用 Avalonia 自带的 `CalendarDatePicker`），卡片上按「今天 / 明天 / yyyy-MM-dd / 过期 N 天」显示，过期标红）

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
  输入控件（`Button` / `ToggleButton` / `TextBox` / `ComboBox` / `NumericUpDown` /
  `CalendarDatePicker` / `RangeBase`）
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
  内容区（那个富文本编辑器）能拉多大 = 卡片上限扣掉<b>其它每一行的高度之和</b> + 行距 + 卡片内边距，
  那些行（**含底部「取消 / 保存」那一行**）是**逐个量 `Bounds.Height`** 的，
  **不要**用「内容栈整高减内容区」倒推：卡片顶到上限、内部开始滚动时内容栈的高度会被裁到视口那么高，
  倒推出来的「其它行高度」偏小，于是内容区能一路拉大、把底部按钮顶出卡片（踩过这个坑）。
- **格式工具栏常驻在编辑区正上方**（`BoardAssignmentForm` 的 `EditorToolbar`）：
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
    `SetForeground(默认颜色，留空则主题文字色)`。`BoardAssignmentForm` 现在**没有**这个入口。
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
  - **想自己挑色要靠我们自己加的那颗「自定义颜色」按钮**（`BoardAssignmentForm`）：库那两个颜色浮层只有
    「固定色板 + 手输色值」，手输不等于取色器，用户要的是能拖光谱自己挑。库的浮层改不了，于是走工具栏
    给宿主留的 `TrailingItems`（`AvaloniaList<Control>`，库还会清掉这些控件的 `Focusable`，所以点它不会
    把编辑器选区弄丢）加一颗色轮按钮，浮层里放 Avalonia 的 `ColorView`（光谱 + 分量，始终展开）——
    **别用 `ColorPicker`**：它自带下拉浮层，套进我们的 `Flyout` 会变成两层浮层。
    浮层底部两个按钮把挑中的颜色经库的公开命令 `SetForeground` / `SetHighlight` 应用到当前选区，
    点完 `flyout.Hide()`（`BoardAssignmentForm.axaml.cs` 的 `ApplyCustomColor`）。固定色板照旧保留，
    库自带那个手输框也还在，这只是一种补充。
    ⚠️ 这颗按钮与浮层都要标 `Classes="rte-keep-theme"`：`RichTextToolbarTheme` 是「颜色值精确匹配」换库里
    写死的浅色，我们自己控件的正常前景色（暗色主题下的纯白）正好命中那张表，不排除就会在暗色下被换成
    深底，字看不见（见 `RichTextToolbarTheme.KeepThemeClass` / `IsKeptSubtree`）。
  - 编辑器上 `ShowFormattingMenu="False"`（有常驻工具栏就不开右键菜单）、
    `ShowPageBoundaries="False"` / `ShowPageNumbers="False"`（便签不画 A4 纸）；外面必须套 `ScrollViewer`
    并给宽度约束——库控件没有内部滚动条且 `ClipToBounds=False`，不给宽度会按整张纸宽排、内容过高画到边界外。
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
- **过期作业是「打标记清理」不是「删文件、搬目录」**：到点由 `BoardCleanupService` 给命中的作业写上
  `BoardNote.CleanedAt`（`DateTimeOffset?`，`[JsonIgnore(WhenWritingNull)]`），**数据原地不动**——
  作业仍在 `IBoardService.Notes` 里、也仍写在它自己那个 `data/board/<年>/<月>/<日>/notes.json` 里，
  只是主页面不再显示（`BoardPageViewModel.RebuildNotes` 按 `BoardNote.IsOnBoard` 过滤）。
  之所以不真删：`BoardReplayTimeline` 把内存里的作业摊成时间轴，真删会连回放历史一起砍掉；
  用户要的就是「只把主页面上的清掉，文件本身还在」，回放期间照旧显示已清理的作业。
  ⚠️ **已清理的作业必须继续留在 `IBoardService.Notes` 里**：`SaveAll` 会删掉这一轮没落笔的日期文件，
  把它们从集合里摘掉就等于删文件。
  ⚠️ **`data/board/_trash/` 是旧版遗留目录**（`BoardNoteStore.EnumerateNoteFiles` 里按字面量 `_trash`
  排除）：它按 `Directory.EnumerateFiles(root, "notes.json", AllDirectories)` 扫，遗留回收目录里的
  `notes.json` 会被当成活作业读回界面，紧接着被 `SaveAll` 的「删掉这轮没写过的文件」连同目录一起删掉；
  旧版本已经归档的数据不能被这次改动动到。
- **批量清理走 `IBoardService.CleanMany(ids, cleanedAt)`**：只给 `CleanedAt is null` 的作业打标记、
  只落盘一次、集合变更只通知一次；**不要**循环调 `Update`（那会一条落盘一次、界面重建一次）。
- **清理规则只有一条：过了截止日期**（`BoardExpiryPolicy.IsExpired`，纯函数、`today` 从参数进来）。
  没填截止日期的作业不管建了多久都不会被清掉（曾经的「保留天数」已按用户要求删掉）；
  到期**当天**不算过期、第二天才算，判据与作业条上那行「过期 N 天」同源
  （`BoardDueDateFormatter.IsOverdue`），两边不能各写一套。
  `DueDate` 是 `BoardNote` 上的 `DateOnly?` 结构化字段（`[JsonIgnore(WhenWritingNull)]`，没填的不落盘），
  **不是** `BoardFieldKind.Date` 自定义字段——清理器要统一读取，不能靠字段名去猜。
  ⚠️ `BoardService.Update` 是逐字段复制的，**给 `BoardNote` 加字段必须同时加进 `Update`**；
  它按 `Id` 定位、`CreatedAt` 与 `Order` 不参与编辑（编辑今天这条作业不该动到以前的）。
- **只有一个触发时刻**：`BoardCleanupSchedule.ShouldRun` 比「最近一个清理时刻」与进程内的
  `BoardCleanupService.LastRun`（每天/每周某天某时刻），漏掉的时刻下次启动自然补清、空跑无害，
  因此不需要状态文件。曾经的「过期当天清理时刻」已按用户要求删掉：到了清理时刻那一轮，
  凡过了截止日期的都清，不再有第二个时间点。
  应用层由 `BoardCleanupHostedService`（`BackgroundService`，注册见
  `App.axaml.cs` 的 `AddHostedService`）每分钟看一眼是否到点——不睡到下一个时刻，是因为用户随时会改时刻
  或关开关；动手前先 `Dispatcher.UIThread.InvokeAsync` 回 UI 线程，打标记会重建界面上的列表。
  批量清理走 `IBoardService.CleanMany`（见上一条），**不要**循环调 `Update`（那会一条落盘一次、界面重建一次）。
  这个功能**默认开启**（`CleanupEnabled = true`，用户明确要的默认值）。
  ⚠️ **手动「立即清理」不看时刻、也不看开关**（`RunCleanupNow` 直接调 `BoardCleanupService.Run`）：
  点一下就该见效，把当天/以前过了截止日期的作业当场清掉——曾经的问题正是「到点判定」把它挡住了。
  ⚠️ 改这里必须同时改 `Corkboard.Core.Tests/BoardCleanupTests.cs`：判定只看截止日期（没期限的永不清、
  到期当天不清、第二天清）、手动清不看时刻、已清理的作业留在原文件、不重复计数、
  旧 `_trash` 目录对存储层不可见，这些都有用例。
  设置页里那个时刻用 Avalonia 自带的 `TimePicker`（`SelectedTime` 是 `TimeSpan?`；FluentAvalonia 里
  **没有**时间类控件），走草稿 + `LostFocus`/离页提交，不要直接双向绑到配置。
  界面上截止日期用 Avalonia 自带的 `CalendarDatePicker`（`SelectedDate` 在 Avalonia 12 里是
  `DateTime?`、不是 `DateTimeOffset?`）配一个「清除」按钮——选择器自己没地方把已挑的日期退回空值，
  「不设截止日期」这个状态只能由外面这个按钮给。
  ⚠️ 它是 `TemplatedControl`（既不是 `Button` 也不是 `TextBox`），**必须加进
  `BoardAssignmentForm.axaml.cs` 的 `IsInteractiveSource` 排除表**，否则按在它身上会被当成「拖卡片」。
  卡片上由 `BoardDueDateFormatter.Describe` 说成「今天 / 明天 / yyyy-MM-dd / 过期 N 天」。
- 配置文件一律经配置服务（原子写入 + 统一 JSON 选项），**不要自己 `File.WriteAllText` 写配置**。
- **作业内容是所见即所得富文本（HTML 片段）**：`BoardNote.Content` 存的就是第三方富文本控件
  （`AvaloniaRichEditor`）`ToHtml()` 的输出，颜色/字号是片段里的行内 `<span style="…">`；
  `BoardNote.ContentKind`（`Markdown = 0` / `Html = 1`，枚举按数字落盘）标记这一段是哪种格式——
  **旧数据的 JSON 里没有这一项，读出来就是 0 = Markdown**，所以不需要额外的版本字段。
  写内容**一律走 `BoardNote.SetHtmlContent(html)`**（同时写 `Content` 与 `ContentKind`，
  并清空遗留字段），不要只改 `Content`。界面侧由 Core 的可复用控件 `RichTextBlock` 承载
  （卡片上只读、表单里可编辑），**不要在页面里直接用第三方 `RichEditor`**。
  默认字号 / 默认颜色是**设置**（`settings.json` 的 `board_settings.default_content_*`）：
  `RichTextBlock` 在装载时把它们逐文本节点补成行内 `<span>`（`BoardHtmlStyling.Apply`），
  回写时再按 style 声明逐条摘掉（`BoardHtmlStyling.Strip`）——**不要把默认样式烘进落盘的 HTML**，
  否则换主题、改设置都不会生效，而且主题文字色还得靠这层注入兜住。
  ⚠️ 这个控件**丢弃块级样式**（`<p style>` / `<div style>` / `<body style>`）与 `<code>`、`blockquote`，
  并会把颜色归一成 6 位大写 hex、把 px 折成 pt（×0.75）；它**没有任何选区读写接口**，
  默认文字色与字号也**不能**从外部属性灌进去（只认行内样式），尺寸上不给宽度约束会按整张 A4 纸宽排、
  内容过高会画到自己边界外——所以卡片里必须 `IsHitTestVisible="False"`，表单里必须外面套一层 `ScrollViewer`。
  ⚠️ **图片（`<img>`）要自己贴边**：库按 HTML 里写死的 `width`/`height` 画 `ImageBlock`（绘制处 `RichEditor.cs:11377`
  完全没有按可用宽度夹紧，只有表格单元格里的图会缩），插进卡片后超出内容宽度就被 `ClipToBounds` 从右边裁掉。
  `RichTextBlock` 因此在只读模式下重写 `MeasureOverride`、在量之前调 `FitImagesToWidth(可用宽度 - 20)`
  （`ImageContentInset = 20`，库内容区左右各留 10 DIP），直接改文档里 `ImageBlock` 的 `Width`/`Height` 等比缩小；
  编辑态**故意不动**——那时库要按插入宽度管，而且改完会被 `PublishContent` 回写，把缩小后的尺寸烘进落盘 HTML、原图再也放不大。
  ⚠️ 库渲染时把**没有显式 `Foreground` 的 run 写死成黑色**（`RichEditor.cs:3906`），输入法**预编辑串**的颜色也
  取自光标处那个 run（`BuildTextLayout` / `PreeditSourceProps`，取不到同样退回黑色）；空段落里一个 run 都没有，
  所以 `BoardHtmlStyling.Apply` 给空块垫一个带默认色的**零宽「颜色种子」**（`SeedChar`，普通文本是 `\u200B`）
  ——它**绝不允许落盘**，回写前必须 `BoardHtmlStyling.StripSeeds`（编辑器的 `ToHtml()` 会把它写成 `&#8203;`，
  所以摘的时候要解实体再比）。新敲进来的字没有颜色可继承，由 `RichTextBlock` 在打字路径上补色
  （`SetForeground`，触发点是 `TextChanged` / `IsModifiedChanged` / `OnTextInput` 与一条 150ms 心跳——
  库的 `TextChanged` 要等下一次重绘才发，真机上还会整段不发，光靠它自会黑字 + 回写停摆）；
  取主题色**必须显式带上 `ThemeVariant`** 去 `TryFindResource`，否则拿到的是亮色字典里的近黑 `#E4000000`。
- **`BoardNote.Formats` / `ContentFontSize` 只是读旧数据的遗留通道**（`BoardTextFormatRange` 因此退化成旧数据 DTO）：
  旧作业由 `BoardService` 在装载时经 `LegacyBoardContentConverter` 一次性转成 HTML 并立刻落盘，
  只在 `ContentKind == Markdown` 时动作（天然幂等）；转完这两项为空，序列化时整项省略。
  **新代码不要再往里写东西**；等确认没有旧数据了，可以连同 `BoardTextFormatRange` 与 `Markdig` 依赖一起删掉。
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
- **主窗口的边缘缩放也由壳自己接管**（`MainView` 的 `TryStartWindowResize` / `MoveWindowResize`）：
  主窗口是 `WindowDecorations.None`——**不能**改成 `BorderOnly`，它会保留 `WS_THICKFRAME`，Windows 于是在
  客户区外留出一圈原生边框（实测左/右/下各 11 物理像素、上 1），而分层半透明窗口不画那一圈，
  看上去就是一整条黑边（用户反馈「窗口左下右都有黑边」）。去掉原生边框就没有原生缩放热区了，
  所以贴边 6 DIP 以内由壳接管按下与移动，光标跟着边缘换；位置补偿与拖动一样按**屏幕绝对位移**算
  （West/North 缩放会同时挪窗口，相对位移会自我抵消），尺寸受窗口 `MinWidth` / `MinHeight` 约束。
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
