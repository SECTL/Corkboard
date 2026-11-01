# Corkboard/ 子树约定

根目录 `AGENTS.md` 里的跨层规矩（技术栈、结构、域归类、设置怎么加、本地化、命令）在本子树同样成立，这里只补本子树自己的约定。

## 壳层与导航

- **主界面没有侧边导航栏**：`MainView` 里只有「标题栏 + 页面标题 + `FAFrame`」，
  内容区承载默认主页面（`AppConsts.DefaultMainPageId`）；其它主页面只能靠
  `MainView.SelectNavigationItemById(id)` 程序化切换，用户可见入口只有托盘菜单。
- **设置界面才有侧边导航栏**：`SettingsView` 里是 `FANavigationView`（`PaneDisplayMode="Left"`）
  + `MenuItemsSource` / `FooterMenuItemsSource`，内容区是 `FAFrame`。
  设置项是「顶层页 + 可展开分组」：顶层按注册顺序排「基础设置 → 外观」，之后是**两个分组**——
  **「作业板」**（`groupId = "settings.board.group"`，`App.axaml.cs` 里 `AddGroup(new PageGroupInfo(CR.Settings_Board_Group,
  "settings.board.group", FluentIcons.BoardFilled))` 后接一串带 `groupId` 的页面），点它才展开**六个**子页：常规（`settings.board`）／内容与样式（`settings.board.content`）／
  科目与分类（`settings.board.sections`）／自动化与数据（`settings.board.automation`）／
  窗口与收纳（`settings.board.window`）／导出（`settings.board.export`）；**紧接着是「考试看板」**
  （`groupId = "settings.exam.group"`，`FluentIcons.CalendarFilled`），子页是考试安排／看板显示／**看板字号**（`settings.exam.font`）／时间同步／自动放映五页。
  **作业板专属的设置项一律进作业板组、考试专属的进考试看板组，基础设置里不许再放**。
  底部的「更新」「关于」由 `PageInfo.Location = Bottom` 沉到页脚，与注册顺序无关。
  **「账号」「云同步」两页当前入口隐藏**（页面 `PageInfo` 上 `isHide: true`，`PageItemsExtensions.ToNavigationViewItems`
  会过滤掉它们；注册与键控 DI 保留，以后开放时去掉该参数即可），侧边栏看不到它们。
- **设置界面标题栏有返回按钮**：`BackButton` 挂 `Classes="nav-back"`，`IsVisible` 绑
  `ViewModel.CanGoBack`，返回栈是 `SettingsViewModel.NavigationHistory`。箭头由
  `Corkboard.Core/Styles/NavigationBackButton.axaml` 的模板画（沿上游 SecRandom-C 的写法），
  所以按钮上**不要**再写 `Content` 字形或 `FontFamily`。
- **顶部「收纳」**：只有设置界面标题栏里有 `PaneToggleButtonStyle` 按钮，
  点一下切换 `NavigationView.IsPaneOpen`，折叠 / 展开设置导航栏。
- **主界面标题栏右上角承载主页面动作**：「设置」「看板模式」「布置作业」「更多」与那颗锁都在这里（「排序」「导出」「时空回放」「收纳」收在「更多」的 `MenuFlyout` 里）。
  「设置」是主窗口里唯一的设置入口（`App.ShowSettingsWindow()`，另一个入口在托盘菜单），
  必须在「布置作业」左边、标 `ElementRole="User"`。
  ⚠️ **「看板模式」是紧挨「设置」的那颗按钮**（图标 `FluentIcons.FullScreenMaximizeFilled`，`ToolTip` 走
  `Menu_OpenDisplayBoard`，点击 `MainView.OpenDisplayModeButton_OnClick` → `App.ShowDisplayBoardWindow()`）：
  它是全屏看板模式的**日常入口**、**默认显示**，配置开关是 `BoardSettingsConfig.ShowTitleBarDisplay`
  （JSON 键 `show_title_bar_display`，默认 `true`），在「设置 → 作业板 → 常规 → 顶部按钮」卡里逐颗关；
  不要把它藏进「更多」菜单，也不要在设置页里另加一颗「打开全屏」——看板模式设置页原先那张「立即使用」卡
  已按用户要求删掉。
  作业板页面上只剩列表与一句回放提示；**「导出」在「更多」菜单里**（`MainView` 里那颗 `SaveFilled` 项），
  点击走 `MainView.ExportImageButton_OnClick` → `(NavigationFrame.Content as BoardPage).ExportAsync()`
  （导出仍由页面做：只有它拿得到内容容器 `BoardContent` 与 `BoardNoteTemplate`；`BoardExportService` 导的是板上的作业内容、不含桌面背景）；
  排布切换仍在设置里（「设置 → 作业板 → 常规 → 排布」下拉框，写回 `BoardSettingsConfig.LayoutMode`）。
  标题栏按下即拖动，所以这些按钮一律要标 `WindowDecorationProperties.ElementRole="User"`，
  并进 `MainView.IsInteractiveSource` 的排除表（`Button` / `ToggleButton` / `TextBox` / `ComboBox` / `Slider`），
  漏一个那个控件就点不动。主窗口是 `WindowDecorations.None`，没有系统按钮，所以右侧可以放东西
  （设置窗口相反，见下）。
- **右上角那颗锁就是「固定窗口」开关**（`BasicSettingsConfig.WindowFixed`，JSON 键 `window_fixed`
  **键名冻结**、**默认 `true`**；「设置 → 作业板 → 窗口与收纳」里**没有**对应卡片，入口只有那颗锁，
  那颗锁是 `ToggleSwitch`（`Classes="titlebar-switch"`，绑 `MainViewModel.IsWindowLocked`，必须列进 `MainView.IsInteractiveSource` 的排除表，漏了它就点不动）；
  锁着时窗口位置与大小都钉住——拖不动标题栏、四边没有缩放热区、光标也不变双向箭头，
  但板子照常编辑、标题栏动作齐全；解锁（`MainViewModel.IsWindowAdjusting` = `!WindowFixed`）后
  标题栏可拖、四边（含顶部与四角）可缩放，代价是内容区盖一层遮罩。
  ⚠️ **解锁态不能编辑、不能布置作业**，所以布置与编辑入口一概要收起：标题栏「布置作业」、
  「更多」里的「排序」与「时空回放」（`MainViewModel.ShowTitleBarAssign` / `ShowTitleBarSort` /
  `ShowTitleBarReplay` 都叠 `&& Config.Basic.WindowFixed`）、右下角布置作业浮标
  （`ShowCornerAssignButton` 同样叠）、作业条上的编辑/删除按钮
  （`BoardPageViewModel.CanModifyNotes` = `!IsReadOnly && !_replay.IsActive && Config.Basic.WindowFixed`）。
  **设置、导出、收纳与那颗锁照旧可用**——导出只读不写（`CanExportImage` 只剩 `HasNotes`），
  而且遮罩排在 `fa:FAFrame x:Name="NavigationFrame"` 之后、`Grid.RowSpan="2"` 盖的是内容区，
  自绘标题栏不在遮罩里，所以解锁时照样拖得动窗口、也点得到那颗锁。
  遮罩是 `Border.lock-mask` + `Border.lock-mask-card`（样式在 `Corkboard.Core/Styles/StylesBase.axaml`，
  在 `Corkboard/Views/MainView.axaml` 内容区 `Grid Row="1"` 里，`IsVisible` 绑 `IsWindowAdjusting`），
  卡片上是开锁图标、标题、说明、提示与一颗「锁定窗口」按钮（`MainView.LockMaskButton_OnClick`
  把 `WindowFixed` 置真），文案键 `Board_UnlockMaskTitle` / `Board_UnlockMaskDescription` /
  `Board_UnlockMaskHint` / `Board_UnlockMaskLock`。
  锁的字形与提示跟着状态走（`MainViewModel.LockWindowTip`：锁着时提示「解锁窗口（可移动、可改大小）」，
  否则「锁定窗口（位置与大小都钉住）」，资源键 `MainView_UnlockWindow` / `MainView_LockWindow`）。
  `MainView` 里判固定一律走 `IsWindowFixed`（`OnWindowEdgeGuard` / `OnPointerPressedForDrag` /
  `TryStartWindowResize` / `UpdateResizeCursor` 四处），固定时连边缘热区与双向箭头光标一起收掉，
  **不要在别处再判一遍配置**（那样会出现「锁着还拖得动」）。
  ⚠️ **光靠 `IsWindowFixed` 挡不住原生拖动**：主窗口 `TitleBar.ExtendsContentIntoTitleBar = true`，
  Avalonia 的 Win32 后端在 `WM_NCHITTEST` 时按命中元素取 `WindowDecorationProperties.ElementRole`，
  `TitleBar` 直接换算成 `HTCAPTION`（`WindowImpl.CustomCaptionProc.HitTestVisual`），
  **那一下由 Windows 自己拖窗口，托管处理器收不到**——曾经因此「锁着照样能把窗口拖走」。
  所以 `MainView.UpdateTitleBarChromeRole()` 跟着锁定态切换标题栏的 role：锁着切成
  `WindowDecorationsElementRole.User`（= `HTCLIENT`，走托管路径再被 `IsWindowFixed` 拒掉），
  解锁切回 `TitleBar`（保留双击最大化等系统行为）。**别把它固定成 `User`、也别删这段同步**
  （`MainView` 构造里订阅 `Config.Basic.PropertyChanged`，`Unloaded` 里退订）。
  ⚠️ **别再往这颗锁上加花样**：点击穿透、`window_lock_mode`（锁点下去做什么）、长按整窗拖动都已整体下架，
  想加回任何一档都先跟用户确认；老配置里那几个键反序列化时静默忽略，没有迁移代码，**也别为兼容加回来**。
- **主窗口置底时，主窗口之外那几颗浮钮也一起沉到桌面层**（`Corkboard/Services/Desktop/FloatingWindowLayer.cs`）：
  收纳展开按钮（`BoardDockService` 管的 `BoardDockWindow`）本来浮在所有窗口之上，
  板子沉到桌面后它再压在别人头上就很突兀
  （用户反馈过）。置底生效时 `FloatingWindowLayer.Apply(window, true)` **先退 `Topmost`、再走
  `WindowFeatures.DesktopBottom` 把窗口 `SetParent` 到 `Progman`/`WorkerW`**（与主窗口同一个桌面宿主、
  同一个层级）；置底没生效时先摘挂载恢复成顶层窗口，再还原成 `FloatingWindowLayer.Register(window)`
  记下的**该窗口自己的默认值**——浮钮的 XAML 默认值并不统一（它本来就没写 `Topmost`），
  **不能拿「置顶」当统一兜底**；所以窗口一创建就 `Register` 一次。
  ⚠️ **只退 `Topmost` 是不够的**（曾经这么做过，用户当场指出来「它不是置顶，是不能置底」）：
  那样窗口只是从「置顶」变回「普通顶层窗口」，仍然浮在桌面层之上，看起来还是「板子沉下去了、按钮没沉」。
  真置底必须改父窗口。
  ⚠️ **挂进桌面层之后不能再直接写 `Window.Position`**：子窗口的 `SetWindowPos` 坐标是相对宿主客户区的，
  主显示器上宿主原点正好在 (0,0)、写屏幕坐标「碰巧对」，副显示器（原点为负）或宿主不铺满整屏时就整块偏掉。
  所以浮钮定位一律走 `FloatingWindowLayer.Place(window, screenPoint)`，由平台
  （`IWindowFeatureService.TryPlaceAtScreenPoint` → Windows 上 `ScreenToClient` 换算）算一次；
  平台不支持时它自己退回写 `Position`。**`BoardDockService` 的 `PlaceAt*` 与 `ApplyDockWindowPosition`
  都改走它了，别在别处又写 `Position`。**
  主窗口是否置底一律取**实际生效值** `MainWindow.IsPinnedToDesktop`（不是配置值——平台可能报不支持或退化）；
  `BoardDockService` 因此额外订阅了 `Basic.PinToDesktop`（`OnBasicSettingsChanged`，**经
  `DispatcherPriority.Background` 延后一拍**再读 `IsPinnedToDesktop`：本服务在构造期订阅、主窗口在 `Loaded`
  订阅，同一份配置变化主窗口那条处理器排在后面，当拍读到的是旧值）并在 `Collapse` 末尾重放一次层级。
  独立窗口不会自己跟着主窗口变层级，这一环漏了就是「设置里置底了、按钮还浮在最上面」。
- **主窗口不透明度与圆角都是窗口能力**（「设置 → 作业板 → 窗口与收纳」里的卡片）：
  `BasicSettingsConfig.MainWindowOpacity`（0–1 的标量）与 `MainWindowRoundedCorners`（开关）
  由 `MainWindow` 在 `Opened` 与应用中把请求转给
  `IWindowFeatureService`（不透明度走 `WindowFeatures.WindowOpacity` +
  `WindowFeatureRequest.Opacity` 这个标量参数；圆角走 `WindowFeatures.RoundedCorners`，
  设置页靠 `BoardWindowSettingsPageViewModel.IsWindowCornersSupported` 决定显隐）。
  - 平台不支持的能力**整张卡隐藏**（`SupportedFeatures` 里没有对应位）：Linux 两样都没有，
    macOS 与 Windows 两样都有；圆角只有 Windows 报支持（macOS 窗口自带圆角，Linux 不报）。
  - ⚠️ **圆角优先走 DWM，走不通才裁区域**：普通顶层窗口交给
    `DWMWA_CORNER_PREFERENCE`（`DwmSetWindowAttribute`）画系统圆角，边缘有抗锯齿、弧度平滑；
    但分层窗口（开了不透明度时用到的 `WS_EX_LAYERED`）DWM 不给圆角，
    置底到桌面后窗口又成了桌面子窗口，这两种情况退回 `CreateRoundRectRgn` + `SetWindowRgn`
    （`WindowsWindowFeatureService.TrySetRoundedCorners`，半径 12 DIP 按 `GetDpiForWindow` 换算，
    `enabled == false` 时两样都退：DWM 偏好交还默认 + `SetWindowRgn(hwnd, 0, true)`）。
    区域那条路是 1 位掩码、边缘必然带锯齿，所以别为了省事把它当唯一实现。
    `SetWindowRgn` 成功后那块区域**归系统所有**，不要再 `DeleteObject`，只有调用失败才回收。
    ⚠️ 尺寸、DPI、置底挂载每变一次都要重贴一次（DWM 会自己跟着，区域不会），否则圆角按旧尺寸裁、
    右下角会切到内容或露直角：`MainWindow.ApplyWindowCorners()` 在 `OnOpened`、`OnBasicSettingsChanged`、
    `ApplyPinToDesktop` 末尾、`ApplyWindowOpacity` 之后、`OnWindowPropertyChanged`
    的 `BoundsProperty` 分支、`OnWindowScalingChanged` 里都要调——分层状态一变就必须重算走哪条路。
  - Windows 上两样共用 `WS_EX_LAYERED`：**只设样式不调 `SetLayeredWindowAttributes`，
    窗口会整体不显示**，所以 `TrySetExtendedStyles` 与 `TrySetWindowOpacity` 都要按同一份
    alpha 补一次分层属性；不透明度下限在平台侧兜底（设置页滑杆下限更高，20%）。
  - ⚠️ **最小化会摘掉平台样式，还原时必须重放窗口能力**：Windows 在最小化时清除窗口的
    `WS_EX_LAYERED`，还会把置底窗口的 `WS_CHILD` 摘掉（父窗口变回 `0x0`），这些都不会自己回来。
    所以 `MainWindow` 在 `WindowState` 变成非最小化时重放 `ApplyPinToDesktop()` / `ApplyWindowOpacity()`
    （幂等）；`WindowsDesktopBottom.TryAttach` 也不能只信自己那张「已挂载」表，要用 `GetParent`
    核对实际父窗口，否则会跳过重挂。漏掉这一环的后果是：配置写着不透明度/置底，界面也这么显示，
    窗口实际已经全部失效。
  - 不透明度滑杆与取色器同一套做法：绑 VM 草稿 + 400ms 防抖，离开页面 `FlushOpacityDraft()`，
    **不要直接双向绑配置**。
  - ⚠️ **不透明度是整窗 alpha，弹层/对话框打开期间必须让位给 1**：`main_window_opacity` 由平台写进
    原生分层窗口，整窗一起乘算，壳内弹层（`Border.sheet`）与 `FAContentDialog` **自己也会变透明**，
    底下便签与桌面就透上来了（用户反馈「框的底是透明的」）。窗内画什么都不可能让那个面局部不透明，
    所以 `MainWindow.ApplyWindowOpacity()` 在「有弹层或对话框开着」时一律用 1，关闭后自动回配置值：
    弹层靠订阅 `PageOverlayService.IsOpen`（`UseSeparateWindow` 为真时弹层在独立窗口里，不用让位），
    对话框靠 `ModalOpacityScope.Begin(this)`（`using var` 包住 `ShowAsync`，三处对话框都写上了）。
    另外 `Border.sheet` 的底色必须用**实体**笔刷（`SolidBackgroundFillColorQuarternaryBrush`），
    Fluent 的 `CardBackgroundFillColorDefaultBrush` 是半透明色、底下的字会隐隐透上来。
- ⚠️ **设置界面标题栏右侧放控件，必须先让出系统按钮那一带**：`FAAppWindow` 的最小化/最大化/关闭按钮
  画在标题栏右端、盖在页面内容**上面**，而本窗口 `ExtendsContentIntoTitleBar`，页面内容一直铺到标题栏底下，
  靠右的控件会被那三个按钮压住（表现为按钮和 × / 方块叠在一起）。这一带的宽度**不许写死常量**
  （随系统与缩放变），走平台能力 `IWindowFeatureService.GetSystemCaptionButtonWidth`
  （Windows 查 DWM 的系统按钮矩形，其它平台没有系统按钮时返回 0）；`SettingsView` 在 `Loaded` 里
  按它给右侧动作区（`TitleBarActionArea`）补右边距，「需要重启」提示因此正好落在最小化按钮左边，
  既不被吞掉、也不被推到最左边。以后往设置标题栏右侧加控件，放进 `TitleBarActionArea` 即可。
- **导航项来自注册表**：`PagesRegistryService` 的集合经 `PageItemsExtensions.ToNavigationViewItems`
  转成设置界面的导航项，按 `PageLocation.Top` / `Bottom` 分到上半区与底部。
  要把某一页沉到页脚（「关于」就是这么放的），在它的 `PageInfo` 上把 location 传 `PageLocation.Bottom`
  （特性参数是位置参数，第三个 `groupId` 传 `null`）；`SettingsView` 的 `FANavigationView` 已经绑好
  `FooterMenuItemsSource`，漏绑就会出现「注册了却看不见」。
  运行期不要增删导航集合，需要动态显隐就改 `IsVisible`。
- **页面实例走键控 DI**，`PageInfo.Id` 就是 DI 的 key；页面工厂在 `MainView` / `SettingsView` 里。
- **页面标题的归属分两种壳**：主界面壳渲染标题（`MainViewModel.PageTitle`，作业板名称可在设置里改），
  主页面自己**不要**再画一遍 `Classes="h1"`；设置壳**不渲染**标题，设置页必须自己把标题画在
  `page-container` 里（`Theme="{StaticResource TitleTextBlockStyle}"` + `Margin="0 4 0 0"`）——
  标题和设置卡同属一个限宽容器，才会跟着卡片一起滚动、左边缘也与卡片对齐。
  主界面页要隐藏标题就设 `PageInfo.HidePageTitle`（设置页用不到它）。
- **页标题只有一行、且不加粗**（用户明确要求的标准状态，照作业板页标题的样子）：
  `Theme="{StaticResource TitleTextBlockStyle}"` 负责字号/行高，`FontWeight="Normal"` 把
  `TitleTextBlockStyle` 自带的 SemiBold 压回常规（MiSans 的 SemiBold 会解析成 Bold，看着就是加粗），
  `Margin="0 4 0 0"` 收上边距——三个都写，缺 `FontWeight="Normal"` 就是那一页标题突然变粗。
  ⚠️ **标题下面不要再挂一句副标题**（灰字简介那一行）：页级说明放进卡片的 `Description`，
  或者干脆别写。以前「看板模式」那几页与「看板字号」页都在标题下面挂了一句灰字，
  用户指着说「标题上面为什么还有副标题、为什么加粗了」——已经全部删掉，别再加回来。
- **「关于」页＝粘滞页头 + 四张卡 + 右侧抽屉**（`Corkboard/Views/SettingsPages/About/`，
  整体照上游 SecRandom-C 的那一页抄过来，但内容全是 Corkboard 自己的）：
  - 页头走 `Corkboard.Core/Controls/StickyScrollViewer`：往下滚时页头整体上移并按三次方淡出，
    滚到只剩 `CollapsedHeight="58"` 那一截就钉住不动，正文从它底下穿过去。模板在
    `Corkboard.Core/Styles/StickyScrollViewer.axaml`（放 Core 的 `Styles/` 才会被
    `<AvaloniaResource Include="Styles\**" />` 打进包）。它内部写的是 `e.NameScope.Find<T>(...)`，
    **不要**照上游用 `GetTemplateDescendants`（那是上游自己的扩展，我们没有）。
  - 页头的海报（Banner）**先没放图**：`Corkboard/Assets/Banners/` 里只有一份 `README.md` 约定
    `corkboard-banner-cn.png` / `-ja.png` / `-en.png` 三个文件名，`AboutSettingsPageViewModel.LoadBanner()`
    按界面语言拼 `avares://Corkboard/Assets/Banners/corkboard-banner-{后缀}.png`、
    用 `AssetLoader.Exists` 先探再加载，缺图时 `HasBanner=false`、页头退化成只有标题。
    **以后加图只往这个目录按名字丢，代码不用改**；新图要补 `THIRD-PARTY-NOTICES.md` 的资源表。
  - 四张卡：应用信息（图标 `AppLogo` 等价物 `/Assets/Icons/Corkboard.png`；常用链接四项＝
    GitHub 仓库 / 问题反馈 / QQ 群 / 组织主页）、作者（图标 `/Assets/Icons/AuthorAionflux.png`，
    组织项图标 `/Assets/Icons/SECTLLogo.png`，两张图沿用上游同一份素材）、鸣谢（在线人数 +
    贡献人员）、版本信息。链接一律走 `IExternalLauncher`，别直接 `Process.Start`。
  - 「贡献人员」拉 `https://api.github.com/repos/SECTL/Corkboard/contributors?per_page=30`，
    模型与加载在 `Corkboard/ViewModels/SettingsPages/GitHubContributor.cs`（头像先读进 `MemoryStream`
    再解码——GitHub 头像是重定向到 CDN 的流，直接喂 `Bitmap` 偶发「流不支持定位」）。
    名单装在**设置壳**的右侧抽屉里（`Corkboard.Core/Controls/DrawerHost`，模板在
    `Corkboard.Core/Styles/DrawerHost.axaml`）：页面调 `SettingsView.Current?.OpenDrawer(content)` /
    `CloseDrawer()`，`SettingsViewModel.IsDrawerOpen` / `DrawerContent` 两向绑定。
    **抽屉必须挂在壳那一层**，画在页面里既盖不住左侧导航、也盖不住标题栏。抽屉支持 Esc、
    点遮罩、点关闭按钮三种收法（Esc 与点遮罩都只是把 `IsDrawerOpen` 置假，模板负责滑出去）。
- **托盘的「软件名 + 图标」首项直接进「关于」页**：`TaskBarIconService.CreateMenuItems()` 的第一项是
  `new(GlobalConstants.AppName, () => App.ShowSettingsWindow(AppConsts.SettingsAboutPageId), ShowApplicationIcon: true)`，
  后面才是分隔线与「显示主窗口 / 考试看板 / 打开设置 / 重启 / 退出」。
  `TrayMenuItem` 只带一个 `ShowApplicationIcon` 布尔、**不带位图**，图标由两份映射各取各的：
  原生菜单 `NativeMenuItem.Icon` 收 `Bitmap`（`AppIcon.LoadBitmap()`），左键自绘菜单的
  `MenuItem.Icon` 收控件（包一层 16×16 `Image`）。`App.ShowSettingsWindow(string? pageId)` 负责落地页；
  它经 `SettingsView.NavigateToPage` 走，窗口还没跑完首次导航时后者只记 `_pendingPageId`、
  交给 `OnLoadedForInitialNavigation` 一起导航（否则两条 Background 消息顺序没谱，
  「打开关于」会被「回到默认页」盖掉）。
- **设置页用分区标题分块**：一页塞不下时不要新开导航页，用 `<TextBlock Classes="section-title" />`
  把设置按用途分成几段（样式见 `Core/Styles/StylesBase.axaml`）。页内首组不加分区标题；
  分区标题文字加在 resx 的 `Settings_Section_*`。
- ⚠️ **设置界面选中导航项前必须保证容器已实例化**（否则窗口卡死）：初始选中推迟到首次布局之后，
  分组内的页面先用 `ExpandGroupsContaining` 展开祖先分组并走一次 `UpdateLayout()`。
  容器没实例化时设选中项，`FANavigationView` 会卡在选中项与模板的循环里出不来。

## 应用图标（窗口 / 任务栏）

- **母图只有一份 PNG**：`Corkboard/Assets/Icons/Corkboard.png`（1254×1254，随包分发）。应用内绘制用它
  （`AppIcon.LoadBitmap()`）；**不要再把它直接交给 `Window.Icon`**。
- **会出现在任务栏的窗口，图标一律走 `AppIcon.LoadWindowIcon()`**（`Corkboard/Services/Desktop/AppIcon.cs`，
  结果缓存）：它把母图在内存里按面积平均缩成 `IcoFile.DefaultFrameSizes`
  （16/20/24/28/30/32/36/40/42/48/54/60/64/72/96/128）里每一档，拼成一份真 ICO 再赋给 `Window.Icon`，
  窗口构造时设一次即可（现在有 `MainWindow` 与 `ExamBoardEditorWindow`）。
  `ShowInTaskbar="False"` 的窗口（`ExamBoardWindow`、`BoardDockWindow`、`PageOverlayWindow`、
  `TrayMenuWindow`）不用设。
- ⚠️ **别给 `Window.Icon` 传 `Bitmap` / `WindowIcon(Bitmap)`**：Avalonia 只有在拿到**真正的 ICO 字节**时才会
  按 DPI 用 `CreateIconFromResourceEx` 从容器里挑最接近的一帧；传位图它只按位图原始尺寸造**一个** HICON，
  任务栏再把这个巨型 HICON 硬缩到 36 像素——这就是「任务栏图标糊成一团」的根因
  （144 DPI 下 `WM_GETICON` 回来 836×836，正是 1254÷1.5）。
- ⚠️ **托盘图标同理**：`TrayIconImpl` 也是按 DPI 从图标里取帧（`LoadSmallIcon(taskBarScaling)`），
  以后给托盘设图标也要喂 ICO，不要喂位图。
- 帧尺寸为什么这么密：Windows 按 `16 × 缩放`（小图标）与 `24 × 缩放`（大图标，Win10+）请求，
  Avalonia 找不到「≥ 请求」的帧时还会按 `32 × 缩放` 再挑一次；100%～300% 就落在
  16/20/24/28/30/32/36/40/42/48/54/60/64/72 上，所以这些尺寸全备上，任务栏才能拿到恰好那么大的一帧。
- 为什么自己写 `IconRaster.ScaleSquare` 而不用 `Bitmap.CreateScaledBitmap`：1254→24 是缩 50 倍，
  双三次只取 4×4 邻域、等于在一千多个像素里**抽点**，细线抽没、边缘一圈锯齿；面积平均把目标像素盖住的
  整块源像素全平均掉。输入输出都是**预乘 BGRA**（透明像素 RGB 为 0，直接平均才不会把「透明的黑」
  混到白边上），写进 ICO 前由 `IcoFile` 反预乘、并给全透明像素在 AND 掩码里置 1。
- `Corkboard.Desktop.csproj` 的 `ApplicationIcon`（`Corkboard.ico`，exe 文件图标 / 资源管理器用）不参与运行期；
  换图标时母版 PNG 与这份 ICO 要一起换。

## 首次运行引导（OOBE）与「启用作业板」开关

两块东西，一个决定「第一次打开看到什么」，一个决定「作业板还算不算存在」。

### 引导：三步走

- **判定与收尾在 Core**：`Corkboard.Core/Services/FirstRun/FirstRunOobeService.cs`（`AddCoreRuntimeServices`
  里 `AddSingleton`）。`Requirement` 只有三种：`FullSetup`（`GuideCompleted` 还是假 → 整段引导）/
  `LegalOnly`（引导走完、但两个协议版本号里有一个落后 → 只补协议那一步）/ `None`（什么都不弹）。
  协议版本号是 `FirstRunOobeService.CurrentEulaVersion` / `CurrentPrivacyPolicyVersion`，
  **协议正文有实质变化才 +1**，改文案不用动它——判据写在服务里、不在界面里，就是为了改文案时不会忘。
- **`Complete()` 的写入顺序不能改**：`ConfigHandlerBase` 是「写一个属性落一次盘」，没有批量写，
  所以那三个键天然写成三次。因此**`GuideCompleted` 必须最后写**：中途崩掉时盘上仍是「引导没走完」，
  下次启动重新引导；反过来先写它就会留下「算走完了、协议却没确认」的中间态，下次只弹协议页。
  末尾那次显式的 `MainConfigHandler.Save()` 只是兜底。（单测 `FirstRunOobeTests` 钉着这条顺序。）
- **窗口在 App 层**：`Corkboard/Views/FirstRunOobeWindow.axaml(.cs)` + `ViewModels/FirstRunOobeViewModel.cs`。
  `OnFrameworkInitializationCompleted` 里 `BuildHost` 之后、建主窗口之前判一次：
  要引导就只 `ShowOobeWindow()`、**主窗口一个都不建**（引导里能关掉作业板，那之后主窗口不该出现；
  而且引导要抢在任何窗口之前，否则会先闪一下主界面）。引导窗口**不交给生命周期**
  （不设 `desktop.MainWindow`），进程靠 `ShutdownMode.OnExplicitShutdown` 常驻。
- **点 × 的两种含义**（`FirstRunOobeWindow.exitOnCancel`）：首次启动传真——`FAContentDialog` 确认后
  直接 `App.StopAsync()`（半套配置留在后台没有意义，下次启动重来）；从设置页「重新运行首次设置」
  点进来传假——只关这个窗口，不退程序。文案是两套键（`Oobe_Exit*` / `Oobe_ExitWindow*`）。
- **改语言即重启**：引导里的语言下拉一变就 `App.Restart()`（`LanguageChangeRequested` 事件）。
  此时 `GuideCompleted` 还是假，重启后同一个窗口以新语言从第一步重开，没有代价。
- **六步布局**（照上游 SecRandom-C 的 `FirstRunOobeWindow` 抄的结构）：欢迎+语言 / 隐私政策与协议 /
  启用作业板 / 主题 / 启动 / 完成汇总。**每一步就是一个 `ScrollViewer`，用 `IsVisible="{Binding IsXxxStep}"`
  切换**（不用 `Carousel`：我们要按 `OobeRequirement` 动态裁掉步骤，`LegalOnly` 时就只有「协议」一步）。
  步骤表由 `FirstRunOobeViewModel.BuildSteps(requirement)` 现算，`StepProgress` 跟着它走。
- **`CanGoNext` 只在协议那一步拦人**：两个 `CheckBox`（隐私政策 / 用户协议）都勾上才能继续；
  其它步骤随便翻。「下一步 / 完成」按钮 `IsEnabled` 绑 `CanGoNext`，完成时走 `TryFinish()`
  （先 `FlushThemeColorDraft()` 再 `FirstRunOobeService.Complete()`）。
- **主题改动要立刻生效**：`SelectedTheme` / `SelectedThemeColorMode` / `ThemeColorDraft` 写进配置后
  调 `App.RefreshAppearanceSettings()`（它内部是 `ApplyThemeSettings` + `ApplyFontSettings`），
  否则用户选了深色还是得重启才看得见。主题色是草稿 + 400ms 防抖（与设置页同一套做法）。
- **开机自启当场落系统**：`Autostart` 走 `IAutostartService.TrySetEnabled`，失败要回滚配置并把
  `ErrorMessage` 显示在底栏（`Oobe_AutostartFailed`）；平台不支持时整行隐藏（`IsAutostartSupported`）。
- **命令行冒烟开关**：`--open-oobe` 直接铺开引导（`App.StartHostAsync` 末尾，与 `--open-settings` /
  `--open-about` / `--open-exam-board` 同一个位置）。**它不改任何配置**，只是把窗口亮出来对版式。
- **设置页里没有「重新运行首次设置」入口**（原来放在「设置 → 基础设置」底部，用户明确说「没什么用」，
  已删掉）：引导只在首次启动 / `--open-oobe` 时出来。`App.ShowOobeWindow(exitOnCancel:)` 那个参数还留着，
  但当前只有默认值这一条路（首启点 × 直接退出），**不要再往设置页加回去**。

### 「启用作业板」开关（`board_enabled`，默认开）

- **配置在 `BoardSettingsConfig.BoardEnabled`**（不是 `BasicSettingsConfig`：它管的是作业板这个域）。
  三处入口写同一个键：引导第 3 步、设置 → 作业板 → 常规的第一张卡。
- **一处监听、两处生效**（`App.WatchBoardEnabled()`，在 `StartHostAsync` 里挂）：
  关掉 → `_mainWindow?.HideForDock()`（连同收纳浮钮一起收，**窗口不销毁**——
  来回拨开关不该每次重建视图与 ViewModel）；打开 → `ShowMainWindow()`，
  但**「启动时显示主窗口」关着的人不会被硬拽出窗口**（`ApplyBoardEnabled` 里那道
  `ShowStartupWindow` 判断）。引导窗口开着时整段跳过（用户此刻是在选，不是已经在用）。
- ⚠️ **云端恢复设置会把配置对象整个换掉**，所以 `App` 与 `TaskBarIconService` 都要跟着
  `ConfigHandlerBase.Reloaded` 换订阅（`OnConfigReloaded*`），只订阅一次 `Data.BoardSettings` 的版本
  在恢复之后会静默失效。
- **启动时干脆不建主窗口**：`BoardEnabled == false` 时 `OnFrameworkInitializationCompleted` 连
  `CreateMainWindow` 都不调（托盘里也没有「显示主窗口」入口，建出来只会白跑一遍页面装配）。
- **托盘跟着那个键重建**：`TaskBarIconService.CreateMenuItems(bool boardEnabled)` 在关掉时不列
  「显示主窗口」；菜单是启动时拍下的快照，所以 `RefreshMenu()` 要在开关变化与 `Reloaded` 时重设
  `trayIcon.Menu` 并收掉已经弹出的左键菜单。左键自绘菜单每次弹都现算（`CurrentMenuItems`）。
