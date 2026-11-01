# Corkboard/Views/ 子树约定

根目录 `AGENTS.md` 里的跨层规矩（技术栈、结构、域归类、设置怎么加、本地化、命令）在本子树同样成立，这里只补本子树自己的约定。

## 看板模式（全屏大时钟 + 作业）

看板模式是**第三块屏**：一块独立的全屏窗口，左上角一个大时钟、其余地方铺作业，
给「把屏幕摆在桌上给别人看」用（自习、值班表、班级公告都一样）。它既不属于作业板也不属于考试看板，
所以设置侧边栏里是**顶层页**（`settings.display`，`PageInfo` 上不写 `groupId`），排在「外观」之后、「作业板」之前。
**它叫「展示」不叫「自习」**：代码域一律是 `Display`（`Core/Display/`、`DisplaySettingsConfig`、`DisplayBoardMetrics`、
`DisplayTextStyle`、`DisplaySchedulePolicy`、`DisplayBoardUri`、`Views|ViewModels/DisplayBoard/`、
`Views|ViewModels/SettingsPages/Display/`、`Services/Display/`），**不要再出现 `Study` 命名**。

- **配置段是老名字 `study` 的继任者**：新键是 `display`，老配置里写 `study` 的由
  `MainConfigModel.LegacyDisplayOnLoad`（`[JsonPropertyName("study")]` 只写属性）接住；
  URI 主机是 `corkboard://display`，老链接 `corkboard://study` 由 `DisplayBoardUri.LegacyHost` + `IsKnownHost` 继续认。
- **窗口是独立全屏窗口，不是作业板的一层遮罩**：要盖住任务栏只有 `WindowState="FullScreen"` 能做到；
  作业板主窗口可能是「置底到桌面」的挂件形态，遮罩铺不满屏幕。窗口里**直接嵌 `BoardPage`**
  （`x:Name="EmbeddedBoard"`），编辑 / 删除 / 拖拽排序 / 富文本渲染全都白拿一份，不要重抄一遍 XAML。
- **时钟固定显示在左上角，位置不可配**（用户要求「时间的显示位置不能改，只能显示到左上角」）：
  以前那套 `DisplayLayout`（`ClockLeft` / `ClockRight` / `ClockTop` / `BoardOnly`）与设置页里的四档分段按钮
  **已经整体下架**，别再搬回来。网格固定两行三列（`RowDefinitions="Auto,*"`、`ColumnDefinitions="Auto,*,Auto"`），
  时钟的格子直接在 XAML 里写死（`Grid.Row="0" Grid.Column="0"`、`HorizontalAlignment="Left"`、
  `VerticalAlignment="Bottom"`，不再走绑定）；作业区永远在第二行跨满三列。
  ⚠️ **不要再退回「2×2 平分」那一版**：那样 220 号的时钟会占掉半屏、顶上还空一大片（用户反馈过「一大块大块」「上面太空」）。
  老配置里残留的 `layout` 由 `DisplaySettingsConfig.LegacyLayoutOnLoad` 只写属性读一次折算
  （`3` =「只有作业」→ `ShowClock = false`），落盘时不会再写回去。
- **字号默认值与考试看板分开**：`DisplayTextStyle`（时钟 96 / 日期 22 / 标题 22 / 退出图标 24，
  天气 22 / 一言 18，秒数是主字号的 `ClockSecondsScale = 0.42`），时钟与其他文字的上下限仍复用
  `ExamBoardTextStyle`（8–120 / 8–480）。
  **不要**再让看板模式借 `DefaultClockAloneFontSize = 220`——那是考试看板独占半屏用的。
- **天气与一言的字号是用户可调的独立设置项**（`DisplaySettingsConfig.WeatherFontSize` / `QuoteFontSize`，
  默认 22 / 18，区间 18–96 / 14–72，`Resolved*` 与 `Normalize` 都走 `DisplayTextStyle.WeatherFontSize` /
  `QuoteFontSize` 夹取；坏值退回默认值而不是夹到边上）：
  - 它们**曾经**是从时钟字号折算出来的（温度 = 时钟 × 0.2293…），结果是改一下时钟就把角落那块天气
    一起放大，用户要的是「单独能调」，所以基准点换成了天气自己。
  - 图标 / 「多云」那行现象 / 底下一排小字仍然由**天气字号**折算（倍率
    `WeatherIconToTemperatureRatio = 2.5`、`WeatherConditionToTemperatureRatio = 0.62`、
    `WeatherFactToTemperatureRatio = 0.48`，各带上下限且从小到大排好），**不要**给它们各开一个滑块。
  - 全屏窗口绑的是 `DisplayBoardViewModel.WeatherFontSize` / `QuoteFontSize`（`RefreshFontSizes()`
    每帧装配时从配置的 `Resolved*` 抄一遍），设置页那两颗滑块写的是配置本体。
- **钟面文字跟着界面语言走**：一律走 `Core/Display/DisplayClockFormatter`（`Format` 出主字 / 秒 / 上午下午 / 日期四段，
  `FormatDate` 三种写法 `DisplayDateStyle`：`Full` / `MonthDay` / `Numeric`）——**不要再写死中文格式串**。
- **钟面默认是一整串「21:42:07」**（`DisplayClockText.Combined`，与考试看板那个钟同一个写法）：
  「大秒」那一档（默认）主时间、冒号、秒都在这一个 `TextBlock` 里，**不许拆成两个并排的 `TextBlock` 拼**——
  那样冒号没了（拼出「04:03 47」），而且并排那一截不带主时间的 `FontWeight`，两截一粗一细，
  看着就是「两个单独的显示状态」（用户截图反馈过）。拆开只服务「小秒」那一档：冒号留在主时间末尾，
  `TextBlock.clock-seconds` 那只尾巴（`ShowClockSecondsTail`、字号 `ClockSecondsScale`）同样写 `SemiBold`。
  设置页字号预览的示例也读 `Combined`，别在预览里另拼一个带空格的版本。
  制式由 `DisplayClockFormat`（`TwentyFourHour = 0` / `TwelveHour = 1`）决定，12 小时制把 AM/PM 画在秒的右边，
  由 `DisplayBoardViewModel.ClockPeriod` + `ShowClockPeriod` 控制；秒的显隐是 `DisplaySettingsConfig.ShowSeconds`。
- ⚠️ 时钟外面那层 `Viewbox` 不是装饰：字号能到 480，`MaxWidth` 由窗口在 `SizeChanged` 里写进
  `ClockMaxWidth`（作业还在时 45% 屏宽、只剩时钟 92%），`StretchDirection="DownOnly"` 保证放得下时一个像素都不动。
  ViewModel 拿不到窗口尺寸，没有这个上限 Viewbox 会按无限宽度量内容、永不缩小，时钟被裁成「21:4…」。
- **时钟与日期的左边缘要跟作业卡片对齐**（`DisplayBoardViewModel.ClockGutter`，值来自
  `DisplayBoardMetrics.ClockLeftGutter = 12`，在 `Corkboard.Core/Display/DisplayBoardMetrics.cs`）：作业板页面自己的内容带了一圈 12 的内边距
  （`BoardPage.axaml` 里 `Margin="12 0 12 12"` 那两处），全屏窗口却只给外层网格留了 56，
  不补这一截时钟就比作业往左戳出去一块（用户反馈过「左上角的时间日期和作业的组件左侧不对齐」）。
  ⚠️ 缩进只能加在**时钟那一格最外层容器的 `Margin`** 上（现在是包住 `Viewbox` 的那个 `StackPanel`，
  它顺带装着日期下面的考试倒计时），不能加进 `Viewbox` 里面那个 `StackPanel`——那里的边距会跟着
  Viewbox 一起缩放，字号一大就跑偏。`DisplayBoardMetrics.ClockLeftGutter` 与 `BoardPage.axaml` 那两个 12 是同一个约定，改一处要一起改。
- **顶栏与作业区之间固定留 `RowSpacing="28"`**（外层那个两行网格上）：日期那行本来就贴着第一排卡片，
  不留间隔看着「挤在一起」（用户反馈过「日期距离作业又太近了」）。
  ⚠️ 别改成给日期加下边距——日期在 `Viewbox` 里，那边距会跟着字号一起被缩放。
- **日期那行可选历法**（`DisplaySettingsConfig.DateCalendar`，`DisplayDateCalendar`：`Gregorian = 0` 默认 /
  `Lunar = 1` / `GregorianAndLunar = 2`，**数值冻结、只在末尾追加**）：拼装仍在 `DisplayClockFormatter.FormatDate`
  里做，农历串由 `Core/Display/LunarDateFormatter`（`ChineseLunisolarCalendar`）给，两段之间是「空格 · 空格」。
  ⚠️ 农历日历靠 ICU，`InvariantGlobalization` 或平台不支持时 `LunarDateFormatter.Format` 返回空串，
  **必须退回公历串**，不能让时钟那行崩掉或空着。设置页那一项在「钟面怎么写」卡里、`IsEnabled` 绑 `ShowDate`；
  日期字号预览浮窗也要读同一个 `DateCalendar`，否则下拉框选了「公历 + 农历」而预览只有公历。
- **考试倒计时写在日期下面**（`DisplaySettingsConfig.ShowExamCountdown`，默认开；文案如
  「贵州省贵阳市2027届高三年级第二次统考 · 还有 19 天」）：
  它是时钟那一格最外层 `StackPanel` 里的第二行 `TextBlock`（`Text` 绑 `DisplayBoardViewModel.ExamCountdownText`、
  `IsVisible` 绑 `ShowExamCountdown`、字号绑 `ExamCountdownFontSize`、`MaxWidth` 同样绑 `ClockMaxWidth`、
  `TextTrimming="CharacterEllipsis"`）——**在 `Viewbox` 外面**，所以时钟字号变到 480 它也不会跟着放大。
  倒的是**这一大场考试**、抬头是**看板大标题**，不是某门科目（用户原话：「我想要的是显示考试的名称，就是大标题」）。
  取数一律复用考试域的纯函数，不要在 VM 里自己算天数：
  `ExamStatusPolicy.FindFirst(IExamService.Exams)`（全部考试里开考最早的那一场，**不能换成 `FindNext`**：
  那一个会把第二门科目当成「下一场」）→ 若 `now >= 那一场.StartAt` 就整行不画 →
  `ExamTimeFormatter.FormatBoardCountdown(IExamService.ActiveBoard.Title, first.StartAt - now)`
  （后半句是 `FormatCountdown`，与考试看板报日子的精度同一套）。
  ⚠️ **一开考就整行不画、考完也不会再出现**：考试期间要盯的是「还剩几分钟」，那是全屏考试看板的活；
  这里只剩一句「已开始」，挂着反而占地方。
  设置页那个字号预览读的是同一套判断（`DisplaySettingsPageViewModel.SampleExamCountdownText`），改规则时两边一起改。
  刷新有三条路：每秒的 `RefreshNow()`（最后一小时按分钟/秒走）、`IExamService.Changed`（改排期、改标题、换看板都发）、
  组件挂载时的 `RefreshStatic()`；`Attach`/`Detach` 里成对增删订阅。
  ⚠️ **别用 `ShowClock` 去门控它**：用户可以把时钟关掉只留倒计时，那样会变成「有内容却永远不显示」。
  字号是**独立设置项**（`ExamCountdownFontSize` / `ResolvedExamCountdownFontSize`，`DisplayTextStyle.ExamCountdownFontSize`
  夹 14–96，坏了退回默认 22），设置页那一行在「显示什么」卡里挨着时钟那两项，滑块在字号卡最后一项。
- **「一言与天气」页的换城市抽屉挂在设置壳上**（`DisplayWidgetsSettingsPage`）：搜索框 / 结果列表 / 常去城市
  全在 `Corkboard.Core/Controls/DrawerHost` 里——页面只有一颗「换城市」按钮，点它由 code-behind 把
  `<UserControl.Resources>` 里那份 `<Grid x:Key="CityPickerDrawer">` 连同 `DataContext` 一起交给
  `SettingsView.Current?.OpenDrawer(...)`（与「关于」页的贡献人员名单同一个宿主），挑定城市后由 VM 的
  `CityPicked` 事件收回、Esc / 点遮罩走宿主自己的路。**不要在页面里自绘一层遮罩 + 卡片**
  （用户原话「不要自创这个抽屉」）：页面自绘的盖不住左侧导航与标题栏，也拿不到宿主的滑出动画。
- **设置页用的是「设置卡 + 设置项行」这套组件**（用户明确要求，照「作业板」设置页）：外层一张
  `fa:FASettingsExpander`（Header 名称、Description 一句话简介），设置项多的一整段收在卡里、**默认收起**，
  里面一行一项用 `fa:FASettingsExpanderItem`（Content 名称、Description 简介、Footer 控件）；
  只有一项的卡直接把控件放 `FASettingsExpander.Footer`，FluentAvalonia 就不画折叠箭头。
  ⚠️ **别退回手写的 `Border.setting-group` / `Border.setting-row` / `TextBlock.row-title|row-description`**
  （做过一版，用户否掉了：「单独设置框换成组件里的设置项、全部换掉」），也不要改 Core 的全局设置卡样式。
  ⚠️ **多行要交给 `FASettingsExpander` 自己的 items 宿主**：它继承自 `HeaderedItemsControl`，行是它生成的
  容器。数据驱动的行写成 `<fa:FASettingsExpander … ItemsSource="{Binding Items}">` +
  `<fa:FASettingsExpander.ItemTemplate>`（模板根就是 `fa:FASettingsExpanderItem`），
  **别在外面再套一层 `ItemsControl`**——那样行不算它的容器，卡片给行准备的底色不生效，行会退回控件默认底色、
  比卡片亮一块（用户反馈过「这个设置项的底色不对，我感觉就是没复用，或者说是用错了」）。
  现有两处这么用：`ExamFontSettingsPage.axaml`（`FontSizeGroups` 的 `Items`）、`DisplaySettingsPage.axaml`（`FontItems`）。
  页面自己的 `<UserControl.Styles>` 只留这几样：数值文本 `TextBlock.slider-value`、星期小方块 `ToggleButton.day-chip`
  与一段时段的容器 `Border.slot-row`。字号/光标/轮播停顿用滑块。
  （原来的 `Border.mode-switch` + `ToggleButton.mode-tab` 那套分段按钮已随「时钟位置」设置项一起删掉，
  这一页现在没有分段按钮。）
  ⚠️ **设置卡不能套设置卡**：`FASettingsExpander` 的内容里再出现一张 `FASettingsExpander`
  （直接放，或者放进它内容里的 `UserControl`——`BoardBackgroundEditor` 本身就是一叠卡片），
  **展开外层那张卡时整个程序会崩**：FA 的展开动画（`FAExpanderExt+ExpanderInfo.RunExpandDownUpAnimation`）
  在内容还没挂到可视树时就去量它，里层卡的内层 `Expander` 这时才应用模板，
  `HandleExpanderTemplateApplied` 里的 `ElementComposition.GetElementVisual(...)` 拿到 `null` →
  `System.NullReferenceException`，栈顶就是这一帧（用户报过，2026-11 修）。
  一页里真要分块就用 `<TextBlock Classes="section-title" />` 分区标题，别用卡片包一层；
  「考试看板 → 显示」那张「背景」卡就是这么改成「分区标题 + `bg:BoardBackgroundEditor`」的，
  「看板模式 → 背景」页本来就不套卡。
- **滑块走「草稿 + 松手提交」**（`DisplaySliderItem`，照 `ExamFontSizeItem` 那套）：`Draft` 是 `decimal?` 草稿，
  拖动只改草稿，指针抬起 / 键盘微调才 `Commit()` 写配置——`MainConfigHandler` 每变一次属性就落一次盘，
  直接绑配置等于拖一路写几十遍 `settings.json`。指针事件挂在**页面**这一层（`PointerPressed/Released/CaptureLost`
  都三种路由 + `handledEventsToo`），滑块内部的 Thumb / 轨道会吃掉一部分事件，挂滑块上会「拖到一半浮窗不关、值也没提交」。
- **拖动预览浮窗**：`Popup IsOpen="{Binding IsPreviewVisible}" PlacementTarget="{Binding #SizeSlider}" Placement="Top"
  VerticalOffset="-8" IsLightDismissEnabled="False" IsHitTestVisible="False"`——`IsHitTestVisible=False` 必须，
  否则浮窗吃掉指针、拖到一半就断。预览字号封顶 `DisplaySliderItem.PreviewMaxFontSize = 96`，超了显示
  `Settings_Display_PreviewScaled` 那一句。整秒数（光标）没有字形可看，`showsPreview: false` 不弹。
  预览那句示例文本是**传进来的 `Func<string>`**（`DisplaySliderItem.RefreshSample`）——制式 / 秒 / 日期格式一变，
  三颗滑块上的示例都要跟着重算，构造时传死字符串就会停在旧格式。
- ⚠️ **`DisplaySliderItem` 的夹取不许借用 `ExamBoardTextStyle.Clamp`**：那是字号的 8–120，拿来夹光标秒数会把默认的
  3 秒抬成 8 秒（默认值当场被改写）。夹取一律走本项自己的 `Minimum`/`Maximum`（字号的两档上限在构造时就传进去了）。
- **时段是「一段一段」的，带着星期**（`DisplaySettingsConfig.Slots`：`List<DisplaySlot>`，上限 `DisplaySlot.MaxCount = 8`）：
  每段有 `Enabled` / `Days`（`DisplayDayFlags` 位标志，周一 = `1 << 0`，`Weekdays` / `Weekend` / `EveryDay` 是预设）/
  进出时刻。判定全在 `DisplaySchedulePolicy` 的**新重载**（`IsActive(slot, now)` / `FindActiveSlot` /
  `ShouldEnter(bool, now, slots)` / `ShouldExit` / `ResolveActiveEnd` / `TimeUntilExit` / `NextStart`，未来 8 天内找下一段），
  老的四时刻重载保留给单元测试与老调用。**星期按「这一段开始那一天」算**：周五 22:00–06:00 那一段，周六凌晨五点半还在里面；
  空位标志按「每天」兜底（`DisplayDayFlagsExtensions.Normalize`），否则用户清空勾选就永远弹不出来。
  ⚠️ **`Weekdays` / `Weekend` / `EveryDay` 只是枚举常量与单元测试的输入，设置页不给按钮**：页面里只有七颗单日 chip
  （`DisplayDayChoice` + `SetDay`），「每天 / 工作日 / 周末」那三颗 `EveryDayCommand` / `WeekdaysCommand` / `WeekendCommand`
  已按用户要求删除（「不要有预设的什么工作日那些东西」），**不要再加回来**。
- ⚠️ **集合元素不会被 `ConfigHandlerBase` 递归订阅**（见「设置怎么写」），改时段后必须显式
  `MainConfigHandler.Save()`——设置页的 `OnSlotChanged` 就是干这个的。
- **老配置只有那四个数（`EnterHour/EnterMinute/ExitHour/ExitMinute`）**：`DisplaySettingsConfig.Normalize()` 在
  列表为空时用它们造出第一段，再把第一段写回这四个字段（别处还在读），所以升级上来的用户时段不丢。这四个键**冻结**。
- **到点自动进出**：`Services/Display/DisplayAutoShowHostedService`（照 `ExamAutoShowHostedService`：15 秒一拍、
  UI 线程 Tick、失败只记日志）吃 `DisplaySchedulePolicy.ShouldEnter/ShouldExit`（左闭右开，`enter > exit` 视为跨夜，
  `enter == exit` 永远进不去）。**只关「自动打开的那一次」**（`_autoShown` 记账），用户自己开的窗不收。
  ⚠️ **用户自己关掉之后不许再弹回来**：Tick 发现 `_autoShown && !App.IsDisplayBoardWindowOpen` 就先
  `App.SuppressDisplayUntil(ResolveActiveEnd(...) ?? now + 15min)` 再清账——只清 `_autoShown` 的话下一拍
  `ShouldEnter` 立刻又为真（这就是老版本「长按退出 15 秒后又自己跳出来」的根因）。
- **抑制与延长都记在 `App` 的静态态上**（`IsDisplayAutoShown` / `DisplaySuppressedUntil` / `DisplaySuppressedDate` /
  `DisplayExtendedUntil` + `IsDisplaySuppressed(now)` / `SuppressDisplayUntil` / `SuppressDisplayForToday` /
  `ExtendDisplaySession` / `ClearDisplaySuppression`）：看门狗、托盘、设置页状态卡三处读同一份，
  **不要各自记一套字段**（那样会出现「托盘说今天不弹了、设置页还在倒计时」）。`App.DisplayNow` 取
  `NtpTimeService.NowLocal`，全项目时间来源不许换回 `DateTime.Now`。
- ⚠️ **设置页那张状态卡只说「现在」，不预告「今天几点自动打开」**（`DisplaySettingsPageViewModel.RefreshStatus`）：
  它只有 `StatusIsOpen` / `StatusTitle`（现在正开着 / 现在没开）、一个空白屏警告、一行「今天不再自动打开」按钮
  （`ShowSuppressToday` 看 `AutoShow`、`CanSuppressToday` 看是否已抑制、`SuppressTodayText` 抑制后换成一句已生效的说明）。
  以前还有一行 `StatusSchedule`，用 `DisplaySchedulePolicy.NextStart` 预告「今天 {0} 自动打开」——可那个方法向未来扫 8 天，
  当天没有时段时也会说「今天 19:00 自动打开」（用户反馈的就是这个），已按用户要求整行删除。
  **别再把 `NextStart` 搬回状态卡**：要预告就得先说清「今天是哪天」，否则不如不说。
- **`corkboard://display` 的链接调用**：`Core/Display/DisplayBoardUri` 解析 `corkboard://display`、`?exit=1`、
  `?from=19:00&to=21:30`（`from`/`to` 必须成对，非法串 `TryParse` 返回 `false` 不抛异常；老主机 `study` 也认）；
  协议注册在平台层 `IUrlSchemeRegistrar`（Windows 写 `HKCU\Software\Classes\corkboard`、Linux 写 xdg desktop 项、
  macOS 报不支持→整张卡隐藏），开关落在 `BasicSettingsConfig.UrlProtocol`，服务端在 `App.EnsureUrlProtocolIntegration()`。
  设置页那张卡的语言是**「允许用链接调用」**，不能写成「用浏览器打开」——它只是注册协议，不是真去开浏览器。
  ⚠️ **这张卡不在「看板模式」页，在「设置 → 基础设置 → 启动」那一段**（用户要求：它是个系统集成项，
  不是看板模式的排版设置）。卡片展开后**一行一个地址**（`corkboard://display` / `corkboard://display?exit=1`），
  每行紧跟一颗复制按钮（`BasicSettingsPage.OnCopyUrlClick` 写剪贴板 + `UrlProtocolEntry` 负责「已复制」反馈）；
  文案键跟着所属设置组走：这张卡的标题与简介是 `Settings_Basic_UrlProtocol` / `Settings_Basic_UrlProtocolDescription`
  （它在基础设置页里），复制按钮是 `Settings_Basic_UrlCopy` / `Settings_Basic_UrlCopied`；
  卡片里那颗「打开全屏」按钮用 `Settings_Display_Open`（看板模式页原先那张「立即使用」卡已经下架，
  `Settings_Display_Open` / `Settings_Display_Close` 两条键只剩这张卡还在用，别当死键删掉）。
- **单实例转发**：`Services/Desktop/SingleInstanceGate`（命名 Mutex + 命名管道，名字取 `Utils.DataRoot` 的 SHA-256 前 16 位）
  在 `Corkboard.Desktop/Program.cs` 里、`PrepareDesktopDataRoot()` 之后调用；第二个实例把命令行转给第一个、自己退出。
  ⚠️ 因此**冒烟测试前必须先 `Get-Process Corkboard.Desktop | Stop-Process`**，否则新进程会把 `--open-display`
  转发给老实例、自己 `exit 0`，看起来像「启动即退出」。
- **弹层要画在全屏窗口里**：`PageOverlayService` 是多宿主（`PageOverlayHost` + `CreateHost/RegisterHost/ReleaseHost`，
  `ShellHostPriority = 0`、`FullScreenHostPriority = 10`，优先级大的赢）。全屏里点「编辑作业」如果还画在主窗口上，
  用户眼前（全屏）什么都不发生。
- **自动保存的渲染器也改成了多槽**（`BoardAutoSaveService.RegisterRenderer(owner, renderer)`）：全屏窗口里嵌的第二块
  `BoardPage` 会跟主窗口那块抢同一个单槽，Unloaded 时还会把主窗口那份抹成 `null`。取**最早注册**的那一份。
- **内嵌的那块 `BoardPage` 靠宿主属性改行为**（`BoardPage` 上的三个 `*Host` 属性转发到 VM，**别在 VM 里读全局配置**，
  主窗口那块板子不该跟着变）：`IsReadOnlyHost`（= `!Editable`，卡片上的编辑/删除按钮跟着收起来）、
  `ShowDueTodayOnlyHost`（`DisplayBoardScope.DueToday`，只留「今天到期 + 已经过了截止日期」的作业，回放期间不滤）、
  `EmptyHintOverrideHost`（空板时换成看板模式自己的那句文案）。新增宿主差异**照这个形状加转发属性**。
- **防止一块空白屏**：`DisplaySettingsConfig.EverythingHidden`（`!ShowClock && !ShowBoard && !ShowExamCountdown`，
  时钟、考试倒计时与作业全关掉）——窗口居中画一句 `Display_EmptyState`，设置页顶部那颗 `WarningNothingVisible`
  也一起亮。**这几个开关不许能关到屏幕上什么都没有**（`Display_EmptyState` 与 `Settings_Display_WarningNothingVisible`
  两句文案要跟着列出所有开关，别只说时钟和作业）。
- **只读时要让人知道**：作业区右下角那格没东西可点时常驻一枚 `LockClosedFilled` 徽标（`ToolTip` 是
  `Display_LockedTip`）——老版本关掉「允许编辑」后点作业毫无反应，用户以为卡了。
- **「布置作业」浮标在屏幕右下角、跟长按退出同一条水平线**：`DisplayBoardWindow.axaml` 根网格里两颗绝对定位的
  角落控件——左下角 64×64 的 `Border.exit-hint`（长按 2 秒退出，`Margin="24,0,0,16"`），
  右下角 56×56 `Classes="accent"` 的加号（`HorizontalAlignment="Right"` / `VerticalAlignment="Bottom"` /
  `Margin="0,0,24,20"`、`IsVisible` 绑 `DisplayBoardViewModel.ShowAssignButton = ShowBoard && Settings.AllowAssign`、
  `ToolTip` 复用 `Board_AddNote`、`Click="OnAddAssignmentClick"`）。
  **纵向对齐靠内边距配平**：退出是 64 高 + 底边距 16（中心离底 48），加号是 56 高 + 底边距 20（中心也是 48），
  改任一颗的尺寸要把另一边一起调；别改成「一个横向容器靠两端」——那样退出按钮会被拽离左下角。
  ⚠️ **别把它挪回作业区 `Grid` 里**（做过一版，会跟内嵌 `BoardPage` 的滚动区抢指针）：
  它属于窗口角落，挂在窗口根网格上，作业区怎么变宽变窄都不该带着它跑。
  作业区右下角只留给小锁（`ShowLockIcon = ShowBoard && !Settings.Editable && !Settings.AllowAssign`，
  只有「既不能布置也不能编辑」才是整屏只读、才挂锁，补一句「为什么点不动」）。
  以前看板模式只能改已经有的作业（卡片上悬停出来的编辑按钮），想新布置一条得退出全屏、
  回去点主窗口标题栏那颗「布置作业」，正在展示这块屏时根本做不到——这颗浮标就是补这个缺口。
  `DisplayBoardWindow.OnAddAssignmentClick` 走 `_overlay.Show(new HomeworkAssignmentForm())`——
  **不另写表单**，与主窗口那颗是同一个控件；弹层宿主就在这扇窗上（`CreateHost(this, FullScreenHostPriority)`），
  表单不会跑到被全屏挡住的主窗口去。
  ⚠️ 底部居中那条一言（`QuoteBar`）的左右内边距是 96：左边要躲开 `24 + 64 = 88` 的退出按钮、
  右边要躲开 `24 + 56 = 80` 的加号，两颗尺寸一变这里要跟着算。
- **「布置作业」与「编辑作业」是两个独立开关**（设置 → 看板模式 → 显示内容 → 显示什么 卡）：
  `DisplaySettingsConfig.AllowAssign`（JSON 键 `allow_assign`，默认 `true`，界面文案「允许布置作业」）管**新增**，
  `DisplaySettingsConfig.Editable`（`editable`，界面文案「允许编辑作业」）管**改/删已有的**，界面上先布置后编辑。
  以前只有 `Editable` 一个开关兼管两件事，想「屏幕上能看能改、但不许学生自己加」做不到——
  **别再让它们互相决定**（`AllowAssign` 不要回退成 `Editable` 的别名，`IsReadOnlyHost` 仍只看 `Editable`）。
  ⚠️ 只看今天到期（`DisplayBoardScope.DueToday`）时新布置的作业可能不在当前视野里（截止日期不是今天），
  那是这一档本来的过滤语义，别为了「布置完立刻看见」把它取消或临时放开。
  ⚠️ 已经有弹层开着时再点一下直接返回，别叠第二张表单（遮罩会越叠越暗、两张表单各存各的）。
- **超一屏自动轮播**：窗口 code-behind 里的 `_carouselTimer`（间隔 = `AutoScrollPauseSeconds`，3–60 秒，
  `DisplaySettingsConfig.MinAutoScrollPauseSeconds` / `MaxAutoScrollPauseSeconds` 夹取）拿
  `FindControl<BoardPage>("EmbeddedBoard")?.NotesScroller`（`BoardPage` 暴露的 `ScrollViewer`），
  ⚠️ **`NotesScroller` / `GetSectionAnchors()` 里取控件必须走 `this.FindControl<T>("名字")`**，不能用
  `x:Name` 生成的字段：`BoardPage` 自己写了 `InitializeComponent()`（`AvaloniaXamlLoader.Load(this)`），
  生成器那条赋值语句不会跑，字段恒为 `null`。踩过一次——字段为 `null` 时 `NotesScroller` 恒空、
  `GetSectionAnchors()` 恒返回空表，`OnCarouselTick` 每拍都在第二道 `return` 上安静退出，
  表现正是用户报的「轮播功能没有」。
  **落点由 Core 的 `DisplayCarousel.NextOffset` 算**（`Corkboard.Core/Display/DisplayCarousel.cs`，
  纯算式 + 单测 `Corkboard.Core.Tests/DisplayCarouselTests.cs`，**别在 code-behind 里再写一遍**）：
  一屏放得下（`extent <= viewport + BottomSlack`）就一动不动；否则按「当前偏移 + 视口 × 0.9」翻一页，
  **能在这一屏里对齐到某张区块卡片的顶边就对齐过去**（边界来自 `BoardPage.GetSectionAnchors()`，
  量的是可见那套排布里 `Border.note` 的顶边），保证每页最上面那张卡片是完整的、不会被从中间切开；
  再往前就贴底（最后那截内容一定露得出来），贴在底上时下一拍回顶部。
  ⚠️ **落点永远不超过「当前偏移 + 一屏」**，越过去就意味着两页之间漏掉一条内容带。
  ⚠️ **暂停只看「指针动没动」，不看悬停**（`DisplayCarousel.PauseAfterActivity`，10 秒）：指针移动 /
  按下会记一次时间戳（窗口上 Tunnel + `handledEventsToo` 的 `PointerMoved` / `PointerPressed`），
  10 秒内不翻页；弹层打开、作业区关掉时也不翻。**别再退回 `PointerEntered` / `PointerExited` 的悬停暂停**——
  作业区几乎占满整块屏幕，鼠标一停在板子上就永远不恢复，用户看到的就是「轮播功能没有」。
  **滚的是内嵌页面那个 ScrollViewer，不要另造一个**。
- **作业区底部要给一言留边界**（`DisplayBoardWindow.UpdateBoardBottomInset`）：一言画在窗口最底下、
  盖在作业区上面，不给它让地方的话翻到最后一页卡片会钻到那行字底下（用户原话「它可能到一言的部分去了，
  它应该要有边界吧」）。窗口量出 `QuoteBar` 的 `Bounds.Height` + 它自己的下边距，再加
  `DisplayBoardMetrics.BoardBottomGap = 8`，写成 `BoardHost`（作业区那个 `Grid`）的 `Margin.Bottom`；
  一言关掉时边界归零。⚠️ 高度读控件自己、不抄 XAML 里的数字；赋值前必须比较，否则挂在布局属性变化上会自激。
- **入口四处**：作业板标题栏那颗「看板模式」按钮（`MainView.axaml` → `OpenDisplayModeButton_OnClick` →
  `App.ShowDisplayBoardWindow()`，**默认显示**，可在「设置 → 作业板 → 常规 → 顶部按钮」关掉——配置是
  `BoardSettingsConfig.ShowTitleBarDisplay`，界面只看 `MainViewModel.ShowTitleBarDisplay`）、
  托盘菜单的「看板模式」子菜单（打开 / 延长 30 分钟 / 关掉 / 今天不再自动打开）、
  「设置 → 基础设置 → 启动」那张「允许用链接调用」卡里的「打开全屏」、`corkboard://display`；
  冒烟开关是 `--open-display`（直接铺全屏）与 `--open-display-settings`
  （直接落到设置页，`AppConsts.SettingsDisplayPageId`，老名字 `--open-study*` 也认）。
  ⚠️ **看板模式设置页里那张「立即使用」卡（打开全屏 / 关闭全屏）已按用户要求删掉**，别再搬回来——
  日常入口就是标题栏那颗按钮与托盘，设置页只留状态与排版设置（`DisplaySettingsPageViewModel` 里的
  `OpenCommand` / `CloseFullScreenCommand` 也一起删了）。
- **托盘菜单支持子菜单了**：`TrayMenuItem` 多了 `Children`（`IsSubmenu` / `IsSeparator` 跟着改），
  `TrayMenuWindow.CreateMenuEntry` 递归建 `MenuItem`，`TaskBarIconService.AddNativeItems` 递归建 `NativeMenuItem`
  （子菜单走 `nativeItem.Menu`）——**自绘菜单与原生菜单两条路都要走到**，漏一条就只剩展平的一串。
  「看板模式」那一组的条目**随状态变**：开着才有「延长 30 分钟」与「关掉看板模式」、不在抑制期里才有
  「今天不再自动打开」（判据是 `App.IsDisplayBoardWindowOpen` 与 `App.IsDisplaySuppressed()`，弹出那一刻读一次）。
  所以 `App.ShowDisplayBoardWindow` / `CloseDisplayBoardWindow` / 窗口 `Closed` 回调 / `SuppressDisplayUntil` /
  `SuppressDisplayForToday` / `ClearDisplaySuppression` 都要调 `App.NotifyDisplayBoardMenuChanged()`
  （内部就是 `IAppHost.TryGetService<TaskBarIconService>()?.NotifyBoardVisibilityChanged()`）；
  漏一处就会出现「已经关掉了，托盘里还写着关掉看板模式」。**别在托盘那边另存一份状态**。


## 作业板功能（2026-10 新增）

- **手写**：`Note.ContentKind` 追加 `Handwriting = 2`，笔迹在内存里是 `Note.Handwriting` 字符串，
  落盘时写在作业专有文件的 `ink=` 块里，**不是 JSON**——
  `Corkboard.Core/Handwriting/HandwritingCodec` 编的是专有文本格式（首行 `CORKBOARD-INK/2` 魔数，随后
  `aspect=`/`ref=` 元数据，每笔一行 `#AARRGGBB|笔宽|橡皮|x,y,笔锋倍率;…`）；旧版本存成 JSON 的数据由
  `TryDecode` 自动兼容（`{` 开头走旧解析），解码永不抛异常。写内容一律走 `Note.SetHandwriting`；
  **参考宽度 `ref`**：坐标相对它归一化，拖大拖小内容框不改它，所以笔迹的绝对位置与粗细不动
  （`HandwritingCanvas` 有笔迹时 `OnSizeChanged` 只重绘、不改 aspect；笔迹超出参考区时
  `NormalizeExtents` 等比扩大参考宽度并把所有坐标同倍收回，绝对位置仍然不变）。
  **笔锋**：每个点带 `WidthScale`（0.4–1.5，画布落点收在 0.72–1.3），画布按指针时间戳算书写速度
  （慢写 1.25 倍、快写 0.75 倍），再做指数平滑 + 每点最大变化 0.12 的**自动矫正**——
  快甩不该细成头发丝，慢写也不该糊成一块；`HandwritingRenderer` 据此画粗细渐变的多边形，
  两端按弧长收锋成**尖**（不补圆帽——用户明确要「锋利的」），最细处留 0.35 DIP，
  收锋长度 = 1.6 倍笔宽、最多占全长 22%（太长粗笔会像拖把）。
  ⚠️ **几何画法别退回去**：粗笔把中心线推成「整条带子」的多边形会自交，出现毛刺/缺口/「毛笔分叉」脏边；
  现在的画法是**一段一个梯形 + 每个顶点一个圆盘**（圆角连接与端头，分段数按半径 10–48），
  全部塞进同一个 `StreamGeometry` 并用 `FillRule.NonZero` 填充——梯形永远凸、不自交，边缘才是光滑的。
  编辑用 `Corkboard.Core/Controls/HandwritingCanvas`（实现 `ICustomHitTest`，鼠标左键 / 触摸 / 手写笔都能画；
  多色、粗细、橡皮粗细、橡皮、清除、撤销一笔），只读展示用 `HandwritingPreview`（卡片、导出走同一份渲染器）。
  ⚠️ **只读展示必须按比例缩放笔宽**：预览把整页缩到卡片宽度上（`area.Width / ReferenceWidth`，
  见 `HandwritingDocument.ResolveRenderScale`），**笔宽要乘同一个比例**——只缩坐标不缩笔宽的话，
  661 DIP 宽的手写放到 300 DIP 宽的卡片上时笔画相对字会粗一倍、字与字的间隙被墨吃掉
  （用户反馈「写的时候间隔正常，卡片上挤成一团」）。编辑画布是按参考尺寸 1:1 画的，
  比例正好是 1，所以这条只影响预览 / 导出（`HandwritingRenderer.Render` 里统一算，
  别再在预览控件里另写一套）。
  **画布视口可以平移/缩放**（写更多内容）：**触控双指**手势（两指中点位移 = 平移、间距变化 = 缩放，
  锚点算式 `offset = anchorDoc − centroid/zoom`，所以手指下那一点始终跟手；第二根手指落下时会把
  已经画了一半的那笔整个撤掉，双指结束后剩下那根手指不再落笔，直到全部抬起——`_touchPoints` /
  `_touchGestureActive` / `_suppressTouchDraw`；橡皮擦状态同样适用）；
  ⚠️ **单指触控的移动必须继续走落笔分支**：`OnPointerMoved` 里处理 `_touchPoints` 的那个分支只在
  双指手势（`_touchGestureActive || _touchPoints.Count >= 2`）时 `return`——以前单指也直接 return，
  表现是「手指按下去拖动只落一个点」。同理 `OnPointerReleased` / `OnPointerCaptureLost` 的触摸分支
  收完手指簿记后要调 `EndGesture()`：不收尾 `_pointerDown` 一直挂着，「撤销一笔」「清除」因为
  「画到一半」被忽略（用户反馈点了没反应），这一笔也不会 `SyncDataFromDocument`，保存出来是空笔迹。
  桌面端右键 / 中键拖动平移、
  `Ctrl + 滚轮` 以指针为锚点缩放（0.5–4 倍），
  `Render` 里 `context.PushTransform(Matrix.CreateScale * Matrix.CreateTranslation)`、输入走 `ToDocument` 反变换，
  `ClampViewOffset` 把视口夹在参考区域附近；视口只是控件状态，**不落盘**（打开时 1:1）。
  **默认笔宽是 6**（`HandwritingStyle.DefaultPenWidth`；历史默认值 4 与 10 由 `NormalizeBoardExtras` 一律迁到 6，
  用户手动选过这两个值的也会被换成 6——那是「没调过」的旧默认值）。
  ⚠️ **编辑手写作业必须落在「手写」页签**：`HomeworkAssignmentFormViewModel.LoadForEdit` 不只看
  `ContentKind == Handwriting`，也接受「笔迹非空且正文为空」的历史数据（kind 被旧版本洗成 Html 的），
  载入末尾再 `NotifyModeProperties()` 一次；`Confirm` 里文本态但正文为空、笔迹还在时按手写保存。
  解码侧（`NoteFileFormat.TryDecode`）对 kind=Html + 正文空 + ink 非空的数据也按手写读。
  ⚠️ **存储层还有一道兜底**：`NoteStore.SaveAll` 写盘前若内存里该作业「空正文 + 空笔迹」、
  磁盘上却有笔迹，就拒绝覆盖（`PreserveInkInsteadOfEmptyOverwrite`，日志警告）——
  用户真踩过「编辑后保存把整段手写抹掉、重启后只剩白卡片」，丢的内容不可恢复。
  ⚠️ 表单里画布的 `Data` 是画布用 `SetCurrentValue` 内部写的，TwoWay 绑定不会回传源，所以
  `HomeworkAssignmentForm.axaml.cs` 的 `RefreshHandwritingState()` **必须**把 `_canvas.Data` 显式写回
  `ViewModel.HandwritingData`——漏了它，保存出来的作业没有笔迹（「手写内容关掉再打开就没了」就是这么来的）。
- **标题栏右上角的按钮可以在设置里逐颗隐藏**（「设置 → 作业板 → 常规 → 顶部按钮」卡）：配置是
  `BoardSettingsConfig.ShowTitleBarSettings/Sort/Assign/Export/Replay/Dock/Display`（全部默认 `true`），
  界面只看 `MainViewModel.ShowTitleBar*` 这七个**计算属性**——配置开关只决定「允不允许显示」，
  各自还要叠原有条件：布置作业在回放中不显示（`!_replay.IsActive`）、回放入口没作业时不显示
  （`_replay.CanEnterReplay`）、收纳要求 `DockEnabled` 也开着；「看板模式」那颗（`ShowTitleBarDisplay`）
  与「设置」同档，只读态也照旧可用（它只开另一扇窗，不动板子）。`MainViewModel` 因此多了个
  `BoardReplayService` 构造参数，并在 `OnReplayChanged` 里补通知。
  ⚠️ **标题栏那颗锁不进这组开关**：解锁态下它是把窗口锁回去的唯一入口，藏掉就再也锁不回来，
  它跟「设置」「导出」「收纳」一样在解锁态照旧可用（都只读不写，遮罩也盖不到自绘标题栏）；
  ⚠️ 别把这些计算属性挂到 `MainView` 上——`UserControl` 的 `OnPropertyChanged(string)` 是 Avalonia 属性
  通知（参数是 `AvaloniaPropertyChangedEventArgs`），不是 `INotifyPropertyChanged`，绑了也不会刷新。
- **字号统一只在「作业板 → 常规 → 字号」一张卡里改**（科目标题 / 作业条小字 / 分类小标题 / 标签胶囊）；
  「科目与分类」页**不再放「分类标题字号」**（那是重复入口，用户要求去掉），该页 VM 的
  `SectionFontSizeDraft` / `CommitSectionFontSize` / `CommitAll` 已删除。
- **「科目与分类」设置页里的科目是「输入框 + 添加」一个一个加**（`BoardSectionsSettingsPage` 的「科目」卡）：
  不再用逗号分隔的长文本框（用户明确要求）。**`HomeworkConfig.Subjects` 默认是空表**（不再预置
  「语文 / 数学 / 英语」）：新用户第一次打开板子时没有科目，科目表完全由用户建；旧数据里的科目仍由
  `HomeworkSectionCatalog.ImportLegacy` 接管，不受影响。科目**胶囊横排、多了自动换行**（`ItemsControl` 的
  `ItemsPanel` 用 `WrapPanel Orientation="Horizontal"`）——语文、数学这种短名字不用一行一个竖着排
  （用户要求）；胶囊里是名字 + 一颗小叉删科目（`OnDeleteSubjectClick`，先
  `FAContentDialog` 确认，`RemoveSubject` 会连带删掉该科目下的分类定义，已布置的作业不动）；
  最后一行是「科目名称 + 添加」输入框与按钮（`AddSubject`，重名给提示、回车也能加，落盘走
  `IBoardService.Save()` → `Changed` → 列表重建）。资源键 `Settings_Board_Subjects_NewPlaceholder/Add/
  Duplicate/Empty/Delete/DeleteConfirmTitle/DeleteConfirmMessage`。`SubjectsDraft` / `CommitSubjects` 已删除，
  `CommitAll()` 只剩字号提交。
- **「科目与分类」设置页的二级分类是「左边选科目、右边编辑」**（`BoardSectionsSettingsPage`）：二级分类那张卡的
  内容区左列是科目 `ListBox`（显示科目名 + `CategoryCountText`），右列只放选中那个科目的分类行
  （改名 / 删除）与新建行——**标题栏里不要再放科目下拉框**：左边既然能选，右边再来一颗就是重复
  （用户明确要求去掉）。两栏都绑 `ViewModel.SelectedSubjectItem`；`ReloadSubjectItems()` 重建后按 `_selectedSubjectName`
  把选中项找回来（否则每次增删分类都会跳回第一个科目）。右半边用 `ContentControl` 绑
  `SelectedSubjectItem`，里面的按钮事件照旧从 `sender.DataContext` 拿那一项。
  资源键：`Settings_Board_Sections_CategoryCount`（`{0} 个分类`，写在
  `BoardSectionsSubjectItem.CategoryCountText`）。
  行距是**统一**的 8 DIP：容器 `Spacing="8"`、行内不再加 `Margin`、新建行与列表行同一档；
  输入框 `MinHeight="32"` + `VerticalContentAlignment="Center"`（**不要**用自定义 `Padding` 撑高，
  那样看起来像「一行字上面又套了个空框」，用户反馈过）；删除按钮 `Padding="6"`。
  曾经试过 10 DIP（太散）与 6 DIP + 大 Padding（太高、不统一），都别退回去。
  「科目」与「二级分类」两张卡**默认展开**（`IsExpanded="True"`），进页面直接就看到内容与**第一个科目**
  （VM 构造期 `ReloadSubjectItems()` 已经把 `SelectedSubjectItem` 落到第一项）。
- **二级分类**：「科目 → 分类」两级。`HomeworkConfig.Sections`（`HomeworkSection{Subject,Name}`，顺序即展示顺序）
  + `Note.Section`（空 = 未分类），`HomeworkSectionCatalog` 负责增删改，展示由 `NoteGrouping.GroupWithSections`
  两层分组；分类小标题字号是 `BoardSettingsConfig.SectionFontSize`。
- **作业卡片上的编辑 / 删除按钮是「悬停或点一下才出来」的浮层**（`BoardNoteCardItem` + `BoardPage.axaml`）：
  鼠标停在作业上就露出来（`PointerEntered/Exited` → `IsPointerOver`），触摸设备点这条作业任意处
  `Tapped`（`OnNoteTapped`）切换 `IsActionsVisible`、再点一下收起；
  模板绑的是 `ShowActions = CanModify && (IsActionsVisible || IsPointerOver)`。两颗按钮画在**同一格网格的上层**
  （`Border` + `HorizontalAlignment="Right"` / `VerticalAlignment="Top"`），
  **不能给它们单独开一列**——那样会吃掉内容宽度，作业内容会被挤窄（用户明确要求）。
  ⚠️ **模板根那个 `Grid` 必须写 `Background="Transparent"`**：没有底色的 `Grid` 不参与命中测试，
  点空白处与悬停的事件被外层卡片底色的 `Border` 接走（只向上冒泡、不会往子级传），
  表现就是「按钮永远不出现、点卡片也没反应」——已经踩过一次。
  `BoardNoteCardItem` 因此从 `record` 改成了 `sealed partial class : ObservableObject`（要能通知显隐）；
  点编辑 / 删除时先把按钮收回（`IsActionsVisible = false`）。回放中、以及窗口解锁期间 `CanModify=false`，
  按钮永远不冒出来。
  ⚠️ **点出来的按钮最多停 30 秒**（`ActionsVisibleDuration`，`DispatcherTimer` 到点把
  `IsActionsVisible` 收回）：用户嫌「一直显示很难受」。悬停态不走这个计时器，
  鼠标离开就收；计时器在 `OnIsActionsVisibleChanged` 里起停，别让它自己跑着不停。
- **科目区块是一张按主题配色的卡片**（`BoardPage.axaml` 的 `Border.note`）：底色 / 描边走页面资源里的
  `ResourceDictionary.ThemeDictionaries`（`BoardNoteCardBackground` / `BoardNoteCardBorder`，浅色白卡、
  深色比页面亮一档的深灰卡），**不要**退回 `Transparent` 底 + Fluent 半透明卡刷——浅色页面是浅灰 Mica、
  深色页面接近纯黑，同一个半透明色两边都跟背景糊在一起（用户反馈「区块分区不明显」）。
  两种排布（**多列** / 区块）**统一都是 1px 描边 + `CornerRadius=8` + `Padding=12`**；多列排布**不要**再覆写成「只剩一条底线」。
- **快捷标签**：`BoardSettingsConfig.QuickTags`（≤ 16 个，`MaxQuickTags`；设置 → 作业板 → 快捷标签里配，
  列表是**胶囊横排、多了自动换行**，和科目胶囊同一套做法，胶囊里带一颗小叉删标签）。布置作业时点选的是
  **结构化字段** `Note.Tags`（`Note.NormalizeTags` 去空白、忽略大小写去重），**不往正文里插字**；
  作业板卡片上用 `Border.note-tag` 画成胶囊（`BoardNoteCardItem.Tags` / `HasTags`），手写作业一样能贴，
  **位置在这条作业自己的内容下面**（不是卡片顶上）。⚠️ 库的 `MeasureContentHeight` 末尾固定 `+40.0`，
  卡片一旦只读就必须在 `RichTextBlock.MeasureOverride` 里扣掉（`MeasuredHeightSlack`），否则标签与内容
  之间会凭空多出一大段空白。
- **作业板文字字号**（设置 → 作业板 → 字号一张卡里四行）：科目标题 `SubjectFontSize`、
  作业条小字 `NoteMetaFontSize`、二级分类小标题 `SectionFontSize`（与「科目与分类」页写同一个配置项）、
  快捷标签胶囊 `NoteTagFontSize`（新增，8–32，默认 12；卡片模板绑 `BoardNoteCardItem.NoteTagFontSize`）。
  夹取一律走 `Corkboard.Core/Board/BoardTextStyle`（`ClampTagFontSize` 等），页面与卡片不要各写一组数字。
- **「界面字号」已按用户要求整体删除**（设置 → 外观里那颗滑杆下架，`AppearanceSettingsConfig.UiFontSize` /
  `DefaultUiFontSize` / `ClampUiFontSize` / `AppFontSize` / `ControlContentThemeFontSize` 与 VM 里那套
  草稿 + 防抖一起删掉）：它只带动窗口与对话框的默认字号，页面里绝大多数文字本来就有自己的字号，
  用户反馈「没啥用」。字号现在**只有「作业板 → 内容与样式 → 字号」这一处入口**（见上一条）。
  `StylesBase.axaml` 的 `:is(Window)` 与 `fa|FAContentDialog` 改成把 `FontSize` 写死 `14`
  （等于删之前的默认值；不写会掉回 Avalonia 基线的 12）。**不要再加回来。**
- **收纳的位置选择是「下拉样按钮 + Flyout」**（`DockSettingsPage`）：展开器右侧只放一颗像 ComboBox 的
  `Button`（`AnchorPickerButton`，显示 `ViewModel.AnchorText` + `ChevronDownFilled`），点开才在 `Flyout` 里
  展开九宫格圆点图（常驻太占面积，用户要求改掉）；九个位置文案是资源键
  `Settings_Board_Dock_Anchor_TopLeft` … `_BottomRight`，`AnchorText` 在 `RefreshAnchorSelection()` 里发通知。
  点完圆点由 code-behind 的 `OnAnchorDotClick` 收起浮层。
  ⚠️ **这里必须用 `Popup`，不能用 `Button.Flyout`**：Flyout 的内容在独立弹窗里、`DataContext`
  不会跟着按钮走，九宫格圆点的 `Command="{Binding SelectCommand}"` 会静默失效
  （用户反馈「点圆点没反应、下拉框永远显示右下」）。现在的写法是 `Panel` 里并列
  `Button`（`OnAnchorPickerClick` 切换 `Popup.IsOpen`）+ `Popup`（`PlacementTarget` 绑按钮、
  `IsLightDismissEnabled`），`Popup` 声明在同一棵可视树里、`DataContext` 正常继承。
- **收纳**：标题栏按钮把主窗口收成屏幕角落的一颗浮钮（`BoardDockService` / `BoardDockWindow`），位置用九宫格
  `DockAnchor` + `DockOffsetX/Y`（离选中那条边的距离，正数朝屏幕内侧）+ `DockScalePercent`（60–200）；
  `BoardSettingsConfig.DockEnabled` 控制入口显不显示。
- **「数据维护」里的两颗入口都是设置卡，不许裸放按钮**：`settings.board.automation` 页尾那张分区下，
  「清空作业板」与「立即清理」各是一张 `fa:FASettingsExpander`（Header 名称、Description 一句话说明、Footer 按钮）。
  早先「立即清理」是一颗光秃秃的 `Button` 直接摆在卡片外面，用户反馈「都不知道这个按钮是做什么用的」——
  设置页里凡是「点一下就有副作用」的入口都要有一张卡承载名称与简介，别让用户靠猜。
- **清空作业板 = 把作业从当前页面收起来**：设置 → 作业板 → 自动化与数据 → 数据维护，确认后直接 `IBoardService.ClearAll()`。
  备份功能（`BoardBackupService`、清理页那张「备份清单」卡）已按用户要求整套删除，**不要再加回来**。
  ⚠️ `ClearAll` 只给现在看得见的作业打 `CleanedAt` 标记（内部走 `CleanMany`）、落盘一次，作业仍留在
  `IBoardService.Notes` 里、文件照旧写在原来的日期文件中，科目/分类定义保留。用户明确要求「清空不是全部清空」，
  **不要退回 `_notes.Clear()`**——真删之后除了历史日志里那一份快照，就再也找不回来。
  - `IBoardService.RestoreMany(ids)` 只把「集合里还存在的作业」清掉 `CleanedAt`、放回板子（落盘一次）。
  - 「还原到此刻」走 `RestoreSnapshot(IReadOnlyList<Note>)`（见下面的「时空回放」条目），能把单独删掉的
    作业按历史快照整条放回来。
  - 改这条要同时改 `Corkboard.Core.Tests/BoardServiceTests.cs` 与 `BoardServiceHistoryTests.cs` 的用例。
- **导出（图片 / 表格）**：作业板标题栏按钮 → `BoardPage.ExportAsync()` → 先弹**自绘模态框**
  `Corkboard/Views/Export/ExportDialog.axaml`（`PageOverlayService`，不用系统窗口）选「导出图片 / 导出表格」，
  **范围固定全部作业**（勾选列表已按用户要求删掉）；图片格式在左下多一颗「复制图片到剪贴板」。
  对话框默认格式取 `ExportSettings.DefaultFormat`，**只要点了某个格式就记成默认**（`ExportDialogViewModel.BuildResult`），
  「以后直接导出这个格式」勾上才把 `AskForFormat` 关掉。弹层宽度按宿主窗口 58% 夹在 720–900、高度至少窗口七成、
  标题栏可拖动、内容区自己滚。文件名只留日期时间（`ExportFileNameBuilder.Build`，`2026-10-18_14-30-05.png` / 同名 `.xlsx`），**不带板名**。
  - **导出时必须把界面元素藏干净**：`ExportAsync` 进来先置 `BoardPageViewModel.IsExporting = true`（`finally` 复位），
    所有列表项的 `CanModify`（编辑 / 删除按钮）与 `CanReorder`（科目标题左侧拖动按钮）都看这个开关，值变了会重建列表。
    ⚠️ 导出图片渲染的就是屏幕上这块板子，漏了这一层就会把悬停才出现的按钮、拖动把手一起画进图片。
  - **PNG**：`BoardExportService.RenderContentBitmapAsync`（**async**）/ `SaveImageAsync`——按内容自然高度以 96 DPI 渲染，
    透明底先铺主题底色（**不导桌面背景**），画布左右各留 `HorizontalPadding = 24` DIP 底色边。
    ⚠️ 这个方法以前是同步的、用 `InvokeAsync(...).GetAwaiter().GetResult()` 在 UI 线程上等 Render 优先级任务，
    在按钮点击里调用必然死锁（用户点「复制图片」整个软件卡死就是这么来的），**不要再改回同步阻塞**。
  - **剪贴板**：Windows 上走 `Corkboard/Services/Ui/Export/WindowsClipboardImage.cs` 的 `CF_DIB` 写入
    （BGRA 像素直接 `SetClipboardData`）；Avalonia 的 `SetBitmapAsync` 在 Windows 是 OLE 延迟渲染，返回时数据还没进剪贴板，
    位图随后被释放 → 粘出来是空的（用户报过「没有成功复制图片」）。只有非 Windows 才回退 Avalonia 剪贴板。
  - **表格（.xlsx）**：`BoardExportService.ExportTableAsync` → `HomeworkTableBuilder.Build` 排表 → `XlsxTableWriter.Write` 写文件。
    **零新增 NuGet**：xlsx 就是 zip + 一组 XML，用 `System.IO.Compression` 手写 OOXML（6 个条目、字符串一律 inline string、
    不引 sharedStrings）。PDF 那一整套（`PdfOptions` / `PdfLayout` / `PdfDocumentWriter` / `BoardPdfRenderSession` /
    `ExportPreviewWindow`）已按用户要求**整体删除**，不要再往回加。
    - **样式定稿（改之前先看单测 `ExportTableTests`）**：8 列 `序号 | 科目 | 分类 | 截止日期 | 状态 | 内容 | 标签 | 创建时间`；
      行 1 板名（合并 A1:H1、16pt 粗、行高 28）；行 2 摘要（`Export_Table_Summary`，合并、10pt 灰 `FF6B7280`）；
      行 3 空行高 6；行 4 表头（粗体白字 + 深蓝底 `FF2F6FEB`、水平垂直居中、行高 22、四周细边框）；
      **冻结 `ySplit=4`**；作业行交替底色 白 / `FFFAFBFC`（只按作业行计数，组标题行不占位）、细边框 `FFE1E3E8`、
      垂直顶端对齐、`内容` 列自动换行其余不换行；组标题行合并 A:H、`FFF2F3F5` 浅灰底、粗体左对齐、行高 20；
      列宽 6/12/14/12/11/60/18/18；正文 Calibri 11pt `FF1F2328`。
    - **数字格式**：截止日期与创建时间是**真日期单元格**（数值 serial + 数字格式 `yyyy-mm-dd` / `yyyy-mm-dd hh:mm`，
      纪元 `1899-12-30`），不是拼好的字符串——用户在 Excel 里要能排序筛选。
    - **状态列判据与界面完全一致**（`HomeworkDueDateFormatter.DaysLeft`）：`due == today` 是「今天截止」、
      `today + 1` 是「明天截止」、`due < today` 才算「已过期 N 天」（红粗 `FFD92D20`）；已清理写 `Export_Table_Cleaned`。
    - **XML 转义 + 非法控制字符必须剔掉**（`XlsxTableWriter.Escape`）：留一个裸控制字符 Excel 就判文件损坏、要求「修复」。
  - **长图没有第二套卡片外观**：`RenderContentBitmapAsync` 直接渲染页面上那块 `BoardContent`，
    所以卡片就是用户看到的那个 `BoardNoteTemplate`，**别为导出另做模板**。
  - **导出设置页**：`settings.board.export`（`Corkboard/Views/SettingsPages/Export/ExportSettingsPage.axaml`，
    挂在「作业板」分组里），配置类是 `Corkboard.Core/Models/SubConfigs/Export/ExportSettingsConfig.cs`
    （`AskForFormat` 每次询问、`DefaultFormat`、`TableGroupBySubject` / `TableIncludeCleaned` / `TableSortMode`）；
    `ExportSortMode` 的 `FollowBoard` = 跟作业板当前排序，其余是固定排序（只改导出、不动板子）。
    旧的 `pdf_*` 键已随 PDF 一起删掉，留在 settings.json 里也不会被认、下一次落盘就清掉。
  - 依赖：`Corkboard.csproj` 显式引用 `SkiaSharp`（`WindowsClipboardImage` 用它把 PNG 解成 BGRA），已记进 `THIRD-PARTY-NOTICES.md`。
- **自动保存为图片**（设置 → 作业板 → 自动化与数据 → 自动保存，**默认关**）：板子改完等 5 秒防抖，
  把整块板面照「导出图片」的样子存成一张 PNG。纯逻辑在 `Corkboard.Core/Board/AutoSave/`
  （`BoardAutoSaveMode`：`Daily = 0` / `EveryChange = 1`；`BoardAutoSavePolicy` 只管文件名、保留期与统计，
  **不碰磁盘**），落盘与调度在 `Corkboard/Services/Board/BoardAutoSaveService.cs`（单例）。
  - **两种保存时机**：`Daily` 文件名只带日期（`board-yyyy-MM-dd.png`），当天反复覆盖、一天只留最后一份；
    `EveryChange` 文件名带时分秒（`board-yyyy-MM-dd_HH-mm-ss.png`），每次改动各留一张。
    保留天数（`RetentionDays`，默认 7，夹在 1–365）到点删旧的，两种模式同一套剪枝：
    **只认 `board-*.png`**（`BoardAutoSavePolicy.TryParseDate`），用户在同一个目录里放的别的图一张都不动。
    `Daily` 模式额外做一次「同一天只留最新那张」，手改过文件名留下的同日后缀也收掉
    （写入时间打平时优先留「只带日期」那个，否则刚存的正主会被当成旧的删掉）。
  - **默认目录是软件自己的数据目录**：`BoardAutoSavePolicy.ResolveDefaultFolder()` =
    `Utils.DataRoot/autosave`，**惰性创建**（`Folder` 配成空串就是这个默认值，别在启动时就把目录建出来）。
    设置页只让用户从文件夹选择器挑或一键恢复默认，**不给手输**——手输一个打错的路径，表现是
    「开了自动保存却什么都没存」，而失败只记日志。
  - **触发有三条**：`IBoardService.Changed` → 5 秒防抖（`Schedule()`）；10 分钟一次的巡检只在 `Daily`
    模式下补「今天还没有图」；设置页的「立即保存一张」走 `SaveNowAsync(manual: true)`——**不看开关**，
    关着也能手动存一张留底。
  - ⚠️ **`SaveNowAsync` 一次只让一张走完**（`_isSaving`）：防抖、巡检、手动三条路会撞在一起，
    而渲染要在 UI 线程上临时把卡片按钮收起来再放回去，两张叠着会互相提前还原状态。
  - ⚠️ **渲染器由页面注册**（`BoardPage` 的 `Loaded` 里 `RegisterRenderer`、`Unloaded` 里
    `RegisterRenderer(null)`）：只有页面拿得到 `BoardContent` 那块控件。渲染前把
    `BoardPageViewModel.IsExporting` 置真（`finally` 复位）——否则悬停才出现的编辑 / 删除按钮和拖动把手
    会被一起画进图里（与手动导出同一条规矩）。页面不在可视树里 / 板子上没作业 / 正在回放时渲染器返回
    `null`，服务就当这一轮跳过，**不报错、也不覆盖当天已有的那张图**。
  - 写文件走「临时文件 + `File.Move(overwrite: true)`」原子替换；失败只在状态行留一句
    `Settings_Board_AutoSave_Failed` 并记日志，不弹框（这是后台行为，打断用户写作业更糟）。
  - 配置在 `BoardAutoSaveSettingsConfig`（`MainConfigModel.BoardAutoSave`，JSON 键 `board_auto_save`：
    `enabled` / `mode` / `folder` / `retention_days`），`NormalizeLoaded` 里调 `Normalize()` 夹一次
    枚举与保留天数。⚠️ 枚举按**数值**落盘（配置管道没有字符串枚举转换器），所以 `BoardAutoSaveMode`
    只能往后追加成员，不能改已有数值。
    ⚠️ `HasCustomFolder` 是纯推导值（由 `folder` 现算），**必须带 `[JsonIgnore]`**：不标就会在
    settings.json 里多一个改不动、还可能跟 `folder` 对不上的字段。
  - 单测在 `Corkboard.Core.Tests/BoardAutoSaveTests.cs`：文件名按模式生成、`TryParseDate` 认不出
    别人的文件名、保留期剪枝的边界（当天算在窗口内）、`Daily` 同一天收成一张、夹取与统计，
    以及 `has_custom_folder` **不进 JSON**。
- **时空回放 = 按历史日志重放那一刻的板面**（不是「现存作业按创建时间累计」）：停靠点由
  `INoteHistoryStore.LoadAll()` 的事件经 `BoardHistory.BuildStops` 生成（同一本地秒的事件全部应用完
  才记 `NoteCount`），`BoardHistory.StateAt` 把事件重放到指定秒、返回那一刻 `CleanedAt is null` 的作业。
  所以**被删掉的作业、被改之前的旧内容**都能在回放里看到——用户明确要的是「那一天编辑作业的历史」，
  不是「包含之前的全部」。控制条里依次是「日期 + 时刻」选择（日历浮层）、「还原到此刻」、退出回放。
  「还原到此刻」= `BoardReplayService.RestoreCurrentSnapshot()` → `IBoardService.RestoreSnapshot(快照)`：
  快照里有、板子上没有的按快照新建（保留 Id 与 `CreatedAt`，`Order` 排到末尾），板子上有但快照里没有的
  收起来，内容不同的整条覆盖；成功就 `Exit()` 回实时视图。
  改这条要同时改 `Corkboard.Core.Tests/BoardHistoryTests.cs`、`BoardServiceHistoryTests.cs`、
  `NoteHistoryStoreTests.cs`。
- **主窗口缩放**：四边四角都能拉（`MainView` 的 `ResizeBandThickness = 8`）；顶部与左上角压在 `FAAppWindow`
  自己的标题栏带上，命中缩放热区后**必须 `e.Handled = true`**，否则库的标题栏处理器会把这一下当成拖窗口接走。
  ⚠️ **缩放命中判定一律用窗口坐标**：`HitTestResizeSides(e.GetPosition(window), window.Bounds.Size)`。
  用 `MainView` 自己的坐标 + `Bounds.Size` 会在置底到桌面时差一截（窗口成了桌面子窗口后 `MainView`
  不贴窗口四条边），表现正是「置底后顶部/左边拉不动」——已经踩过一次。所以窗口级兜底
  `OnWindowEdgeGuard`（`SetDragHost` 里 `handledEventsToo: true` 挂的隧道兜底）、`TryStartWindowResize`、
  `UpdateResizeCursor` 三处**必须同一套坐标**：只改兜底那一处，就变成「兜底能命中、进来后 `TryStartWindowResize`
  又判不中」而直接返回 false。屏幕绝对位移走 `window.PointToScreen(e.GetPosition(window))`，
  不要拿 `Window.Position` 加偏移乘 `RenderScaling` 自己拼（子窗口坐标不等于屏幕坐标）。
  `TryStartWindowResize` 里要挡掉 `_isResizingWindow`，`OnPointerPressedForDrag` 开头要挡掉 `e.Handled`。
