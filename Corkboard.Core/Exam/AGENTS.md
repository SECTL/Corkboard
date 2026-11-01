# Corkboard.Core/Exam/ 子树约定

根目录 `AGENTS.md` 里的跨层规矩（技术栈、结构、域归类、设置怎么加、本地化、命令）在本子树同样成立，这里只补本子树自己的约定。

## 考试看板与 NTP 校时

考试看板是**与作业板并列的第二块板子**，设置侧边栏里排在「作业板」分组正下方
（`groupId = "settings.exam.group"`，子页依次是：考试安排 / 看板显示 / 看板字号 / 最小化与浮窗 / 时间同步 / 自动放映）。

### 一块看板 = 一个文件（多份看板可切换）

- 数据在 `data/exam/`：`boards.json` 是**索引**（`ExamBoardIndexConfig`：`BoardIds` + `ActiveBoardId`），
  每块看板一份 `data/exam/boards/<Id>.json`（`ExamBoardDocument : ConfigBase`，`ConfigFilePath` 由 `Id` 推导）。
  一块看板自带 `Title` / `Subtitle` / `SubtitleColor` / `Exams`，**不再有「全局标题」这回事**。
- 为什么用索引而不是扫目录：`ConfigServiceBase` 没有列目录接口，只有 `IsConfigExists/LoadConfig/SaveConfig/DeleteConfig`；
  索引顺带把下拉顺序固定成创建顺序。索引里有、文件不在的 Id 直接丢掉；一块都不剩时现造一块空的。
- `Id` 就是文件名，**定下后不许改**。`ExamService` 读盘时一律用索引里的 Id 回填，手改 JSON 删掉 `id` 也长不出第二个文件。
- `IExamService` 的 `Add/Update/Remove/Save` **都作用于当前看板**（`ActiveBoard`），切看板走 `SelectBoard`；
  每次变更把当前看板整份写回 + 写索引 + 发 `Changed`。最后一块看板删不掉（返回 `false`）——界面上永远要有一块可看的。
- 老数据迁移：一块看板都没有时，`ExamService.BuildDefaultDocument` 把老版本单份 `data/exam/exams.json`
  与 `settings.json` 里全局的 `ExamBoardSettingsConfig.Title/Subtitle/SubtitleColor` 搬进第一块看板，
  然后把全局那三个字段清空（它们从此只作为遗留字段存在）。**遗留 `exams.json` 不删**。
- 因此 `ExamBoardSettingsConfig` 现在只剩**显示偏好**：`UpcomingThresholdMinutes` / `ClockSide` /
  `HideExamListWhenUpcoming` / `ShowExamListInProgress`。标题副标题归看板文件，**不要再绑回这里**。

### 编辑走独立窗口，设置页只当列表

- 设置页「考试安排」只有一张看板列表：每行 = 标题 + 「当前」徽标 + 副标题 + 「共 N 场 / 下一场 …」+ 编辑 / 设为当前 / 删除。
- 真正的编辑在 `Corkboard/Views/ExamBoard/ExamBoardEditorWindow.axaml(.cs)` + `ViewModels/ExamBoard/ExamBoardEditorViewModel.cs`：
  上面一条是标题 / 副标题 / 副标题颜色，下面**左表右表单**（表单固定 360 宽、字段竖排）。
- ⚠️ **别再把它塞回设置页**：设置页 `page-container` 限宽 960 且纵向滚动，一张十来列的表与一份十来个字段的表单
  挤在一起只能互相压扁（用户反馈过「布局不太好」）。窗口能拉大，两栏各占一半才摆得开。
- 窗口入口只有一个：`App.ShowExamBoardEditorWindow(Guid? boardId)`——传 `null` 编辑当前那块；
  窗口已经开着就 `ReloadBoard()` 换成新的一块，不另开第二个。
- 关窗不弹确认：标题 / 副标题 / 每一场考试在**失焦或回车**时就写进看板文件了（颜色走 400ms 防抖），关掉不丢东西。
- `ExamBoardEditorViewModel` 是 transient、`IExamService` 是单例：窗口 `Closed` 里必须 `CommitAll()` + `Detach()`
  （收掉订阅与 15 秒计时器），漏了就是每开一次窗口多挂一份处理器。

### 快速布置考试（向导 + 模板）

从零录一场考试要几十条记录，所以有一条「向导」路线：**基本信息 → 考哪些科目 → 总览**，最后一次性写盘。
入口两个，都开同一个独立窗口 `Corkboard/Views/ExamBoard/ExamPlanWizardWindow.axaml(.cs)`：

- 设置 → 考试看板 → 考试安排 → 看板列表的「快速布置」= `ExamPlanWizardMode.CreateBoard`（新建一块看板，建完直接进编辑窗口）
- 看板编辑窗口标题行右侧的「快速布置」= `ExamPlanWizardMode.AppendToBoard`（往**当前**看板批量写）

窗口 1040×760、`FAAppWindow`，**三步**（`LastStep = 2`）内容各自一块 `IsVisible` 绑
`IsBasicStep` / `IsSubjectsStep` / `IsReviewStep`；套了模板时跳过第 2 步（`IsSubjectsStepVisible` 为假）。
底部固定「取消 / 上一步 / 下一步（末步是「完成」）」，**末步那颗按钮只挂 `Click`**（要写盘 + 开窗口 + 关自己，
光靠 Command 收不了尾），前几步才走 `NextCommand`；这颗按钮还绑了
`IsEnabled="{Binding !HasStepError}"`——这一步没走通就点不动，左边那行红字负责解释原因。

- **排布是纯逻辑，不在窗口里现写**：`Corkboard.Core/Exam/ExamSchedulePlanner.cs`。
  `ExamPlanEntry` 存的是 **`DurationMinutes`（时长）而不是 End**——对照表里就地改时长，收卷时刻跟着算；
  `End` 由 `static ResolveEnd(TimeOnly, int)` 给出（跨天夹 23:59）。`ExamPlanSubject` / `ExamPlanSlot` /
  `ExamTimeSlot` / `ExamDurationPresets` 也都是 Core 里的独立纯类型，各自有单测（`ExamPlanTests.cs`）。
- **时长预设**（`ExamDurationPresets.Resolve`，忽略大小写与首尾空格，认不出的退回 75）：
  语文 150；数学、英语 120；物理、化学、生物、政治（含「思想政治」「道德与法治」）、历史、地理 75；
  信息技术、通用技术、音乐 60。下拉里的快捷档是 30 / 60 / 75 / 120 / 150（`QuickMinutes`）。
- **科目表只有一份**：向导第 2 步的科目来自作业板的 `IBoardService.Subjects`，界面上写明「科目来自作业板」；
  作业板一个科目都没有时给「＋ 添加科目」，**在这里加的也直接写进 `IBoardService.Subjects` + `Save()`**，
  不要另开第二套科目表。合卷（理综/文综）由多门合成一行，显示名用 `ExamPlanSubject.CombineSeparator`（`" · "`）拼。
- ⚠️ **作息在界面上只有三个数**：`SlotsPerDay`（一天几场，1–6）/ `FirstStartDraft`（第一场几点开考）/
  `BreakDraft`（两场之间歇多久），由 `ExamPlanSlotPresets.ForCount(count, firstStart, breakMinutes)` 摊成
  `List<ExamPlanSlot>`（第 1 场 `Fixed`、其余全部 `AfterPrevious`）。
  **不要再做「一行一场开考时刻」的表格**——用户明确说过「一天几场这里就只用设置一下就行了」，
  而且作息页原来是单独一步，也按要求并进了「总览」。
  用户手改过这三个数就置 `_slotsTouchedByUser = true`；只有这一位为假时，首次进总览才按
  `ExamPlanSlotPresets.Recommend(PlanSubjects.Count)` 自动推荐（≤2 一场、≤4 两场、≤6 三场、≤8 四场、其余五场），
  否则「我明明调好了，回去改个科目又被冲掉」。
- **时段（上午/下午/晚上）由开考时刻推出**（`ExamTimeSlotPolicy.Resolve`：`<12:00` 上午、`12:00–18:00` 下午、
  `≥18:00` 晚上），**不落盘**，只是显示层分组。
- ⚠️ **改一场要带着后面一起动**（用户原话：「你上面还是 3 点钟，你下面 4 点钟不变成 5 点钟，你应该要从 4 点钟变成 5 点钟」）：
  对照表里改开考时刻 → `ExamSchedulePlanner.SyncBreak(entries, index)` 把这一场与上一场的间隔记进
  `ExamPlanEntry.BreakMinutes` → `Cascade(entries, index)` 把**同一天**后面每一场重算成「上一场收卷 + 自己的休息」；
  改时长 → 直接 `Cascade`。`Cascade` **换天就停**（第二天第一场的时刻是独立锚点）。
- **冲突只标红不拦截**（`ExamPlanEntry.HasConflict` / `ConflictReason`，文案 `Exam_Plan_Conflict_*`）：
  三类是「跨到第二天」「比上一场收卷还早」「与看板上已有的考试撞了」。最后一种只在追加模式下出现，
  靠 `ExamSchedulePlanner.PlanAfter(..., existing)` / `Refresh(entries, existing)` 传进去；
  **选了「覆盖」就不再传已有考试**（`ExistingExams` 在 `ReplaceExisting` 时返回空），否则会拿马上要被删掉的考试判撞。
- ⚠️ **对照表里就地改行时不要重建控件**：改完走 `ExamSchedulePlanner.Refresh` 重排，然后
  `ReviewRows.Move(current, target)` **挪行**——重建会把用户正握着的 `TimePicker` 抽走。
- **汇总表每行有「上移 / 下移 / 删除」三颗按钮，挪顺序 = 换「这门课排在第几天」**：
  `ExamPlanRowItem.CanMoveUp` / `CanMoveDown` 由 `RefreshReviewTexts()` 按行号置值（首行不能上移、末行不能下移），
  XAML 里三颗都走 `Click`（`OnMoveRowUpClick` / `OnMoveRowDownClick` / `OnRemoveRowClick`）。
  ⚠️ **挪动会整张重铺，不只是换行位置**：`MoveRow` 先在列表里把两场对调，再调
  `ExamSchedulePlanner.Relayout(entries, slotsPerDay, firstStart, breakMinutes)`——**列表顺序即考试顺序**，
  第 1 场落在第一天、同一天后面「接上一场收卷 + 休息」、排满 `slotsPerDay` 场换下一天，
  **它会覆盖用户手改过的 `Date` / `Start`**（这是「挪顺序」的固有代价，顺序变了原来的时刻就不成立）。
  只想动时刻、不重排时用 `Cascade`（见上一条），两条路别混。
- **排期那三个数与「作息预设」分两行、都靠左**（`HorizontalAlignment="Left"`，**不要**用 `*` 列撑满——
  撑满后三组被推到窗口两端，看着更散）：第 1 行一天几场 / 第一场 / 休息，第 2 行作息预设 + 删模板 +
  存为作息模板 +（`IsAppendMode` 时）写入方式。每个「标签 + 控件」各自包一层 `StackPanel`，
  标签才永远紧贴自己的控件，不会因相邻列宽变化被拉成参差不齐的一排。
- ⚠️ **汇总表列宽给足**：时间列 230、时长列 150（`TimePicker` 两格 + 分隔线实测要 ~200 DIP，
  `NumericUpDown` 还要在最右边画上下箭头），表头与数据行**必须同一份 `ColumnDefinitions`**
  （`"36,100,60,*,230,150,100,96"`），漏改一边就是「表头对齐、数据错位」。
  给窄了控件会按自己需要的最小宽度画出去、压到右边一列上（用户反馈过「重叠了」）。
- **`ApplyShape` 末尾必须补一次 `OnPlanShapeChanged()`**：它在 `_isSyncing = true` 里直接写
  `SlotsPerDay` / `FirstStartDraft` / `BreakDraft` 三个字段，那三个 `partial void On...Changed` 一律早退，
  只补 `OnPropertyChanged` 不够——从作息预设下拉挑一份之后表格不会跟着变
  （表现是「选了一天四场，下面的表还是原来那几场」）。
- **最后一步一场都没排出来时「完成」也要禁用**：`StepError` 里 `2 when ReviewRows.Count == 0` 给一句提示，
  按钮上那颗 `!HasStepError` 才会跟着灰掉。
- **追加模式下要问「覆盖还是追加」**：`ReplaceExisting` / `WriteModeIndex`（只在 `IsAppendMode` 时显示下拉），
  「覆盖」走 `IExamService.ReplaceAll(entries)`、「追加」走 `AddMany(entries)`。两者都**只落盘一次、只通知一次**，
  **不要**循环调 `Add` / `Remove`（那会一条落盘一次、界面重建一次）。
- **作息模板**（`Corkboard.Core/Exam/ExamSlotPreset.cs`，`data/exam/slot-presets.json`）：
  总览页右上「存为作息模板」，内置 5 份（一天一场…一天五场，`IsBuiltIn`、Id 固定、删不掉），
  用户存的同名忽略大小写就地覆盖。⚠️ **内置模板必须用 `ExamPlanSlotPresets.ForCount` 生成**——
  界面上作息只有三个数，内置模板得能由这三个数还原，否则用户挑了「一天两场」下拉却匹配不上、看着像没生效
  （原来那几份带 14:00 / 19:00 固定锚点的写法因此被换掉了）。`FindMatchingPreset` 按
  「场数 + 每场 `FixedStart` / `StartMode` / `BreakMinutes`」逐项比对。
  作息模板与考试模板分开存：前者脱离科目，只描述「一天怎么排」。

### 考试模板（跨届复用）

- 模板里**只存「第几天几点考什么、考多久」，不存绝对日期**（`ExamPlanTemplateEntry.DayOffset`）——
  套用时只问「这次第一天是哪天」，日期现算。存绝对日期的话模板过一个月就作废、跨月跨年还得手改
  （用户原话是「只用把开始日期和结束日期改一下」，那就干脆让日期根本不进模板）。
- 数据在 `data/exam/templates.json`（`ExamTemplateConfig` + `ExamTemplateService`）：
  **一个文件装全部模板**，不像看板那样一块一个文件——模板是小对象、份数少，一个文件省掉索引。
  模板与看板文件分开存：删掉一块看板不该把模板带走，反过来也一样。
- 「另存为模板」在编辑窗口底部（`ExamBoardEditorViewModel.SaveAsTemplate(name)` → `ExamPlanTemplate.FromExams`，
  日期换算成相对最早那天的 `DayOffset`）。**同名即覆盖**（`ExamTemplateService.Save` 按名字忽略大小写判重），
  同一块看板反复存不会越堆越多。
- 套用：向导第 1 步的「用模板」下拉（`ExamPlanWizardViewModel.SelectedTemplate`）——选了就**跳过科目与作息两步**
  （步骤条那两颗胶囊跟着藏），直接出对照表；模板里的标题 / 副标题 / 颜色会预填到第 1 步。
- 模板列表在设置 → 考试看板 → 考试安排 的最后一张卡，只显示名字与「共 N 场 · 跨 N 天」，
  **不显示任何具体日期**（模板本来就不锁在某一年，显示日期只会误导）。

### 表格对齐

- ⚠️ **表头 Grid 与数据行 Grid 必须用完全相同的 `ColumnDefinitions`**，操作列给**固定宽度**、表头补一个空列。
  踩过的坑：表头写 `130,*,180,190,Auto` 但只有 4 个子元素，行却有 5 个（第 5 列是编辑/删除按钮约 60px），
  表头那个 `Auto` 塌成 0，多出来的宽度全落进 `*` 的科目列，时间列与状态列整体右移（用户截图反馈「表格没对齐」）。
  当前两处（编辑窗口、全屏看板）都是 `140,*,150,150,72` 这种「固定列 + 一个 `*` + 固定操作列」。

### 全屏看板

- `Corkboard/Views/ExamBoard/ExamBoardWindow.axaml(.cs)` + `ViewModels/ExamBoard/ExamBoardViewModel.cs`：
  `WindowDecorations="None"` + `WindowState="FullScreen"` + `ShowInTaskbar="False"`，左上角标题 + 副标题，左侧时钟、右侧考试表。
- 三档状态由 `ExamStatusPolicy`（Core，纯函数）判定，左闭右开：`now >= End` → 已结束；`now >= Start` → 进行中；
  `Start - now <= 阈值` → 即将开始；否则未开始。阈值 `UpcomingThresholdMinutes` 默认 30，可 1–1440。
- 显示形态：未开始 = 时钟 + 全部考试；即将开始默认**只显示下一场**（`HideExamListWhenUpcoming` 可改回显示全部）；
  进行中**默认只显示时钟**（`ShowExamListInProgress` 可打开列表）。时钟与列表左右位置由 `ClockSide` 决定。
- **数字一律用等宽数字**（`Corkboard.Core/Services/Fonts/TabularFigures` 的 `tnum`，**不换字体家族**）：
  时钟、考试信息里的时间区间与倒计时（`InfoRemaining`）、表格的时间列在 XAML 里都绑
  `ExamBoardViewModel.NumericFontFeatures`，值由 `RefreshFontSizes()` 里
  `TabularFigures.ForNumbers(Config.Appearance.Font)` 给出——**只在界面字体是默认 MiSans 时给值**，
  用户自选了别的字体就当没设过（`null`），不替那个字体改数字宽度。
  倒计时最后一小时每秒一跳，比例数字会让居中那行左右抖；表格时间列靠它上下对齐。
- **报日子的说法只有一份**：(`ExamTimeFormatter.FormatCountdown`)，一天以上按天、不到一天按小时/分钟。
  看板模式那一行在它外面套一层抬头：**`ExamTimeFormatter.FormatBoardCountdown(title, remaining)`** =
  看板大标题 + 「 · 」+ `FormatCountdown`（标题空着只留后半句）——抬头是**这场考试的名字**
  （`IExamService.ActiveBoard.Title`），不是科目（用户原话：「我想要的是显示考试的名称，就是大标题」）。
  挑哪一场用 `ExamStatusPolicy.FindFirst`（整块看板里开考最早的那场；**不能换成 `FindNext`**，
  那一个会把第二门科目当成「下一场」）。看板模式日期下面那行、以及它设置页的滑块预览都调这两个方法，
  **不要在别的 VM 里再拼一遍天数**，两边的口径必须一致。
  以后再有「数字会跳 / 数字要跨行对齐」的文字照等宽数字那条绑，别单独换等宽字体家族。
- **左下角长按 2 秒退出**：进度圈 + 蓝→红渐变全在 ViewModel 里（`BeginHold` / `CancelHold` / `HoldSweepAngle` / `HoldRingBrush`），
  窗口 code-behind 只负责捕获指针。⚠️ **窗口里不要自己写 `InitializeComponent()`**——那会顶掉 XAML 生成的同名方法，
  `x:Name` 字段恒为 null，按住一下就是空引用崩溃（这条踩过两次，`TrayMenuWindow` 一次、这里一次）。
- **最小化代替退出（考试场景，只在考试进行中）**：「设置 → 考试看板 → 最小化与浮窗」（`PageInfo("settings.exam.floating")`，
  `ExamFloatingSettingsPage` + `ExamFloatingSettingsPageViewModel`）里用下拉挑**什么时候**那颗按钮是「最小化」——
  `ExamBoardSettingsConfig.MinimizeMode`（`ExamBoardMinimizeMode`：`Off = 0` / `InProgress = 1`（默认）/
  `InProgressOrUpcoming = 2` / `Always = 3`，**按数字落盘、只能末尾追加**，`Normalize()` 里 `Enum.IsDefined` 兜底）。
  ⚠️ **默认不是「一直」而是 `InProgress`**：用户的原话是「应该是在考试进行中才是最小化，平常都是只有退出」——
  考完就没必要收起来了，那颗按钮那时就是退出键。原来那个布尔键 `minimize_instead_of_exit` 已就地换掉（没发过版，不留遗留字段）。
  - 判定在 `ExamBoardViewModel.ResolveCanMinimize(now)` / `HasExamInStatus(now, status)`：状态一律问 `ExamStatusPolicy.Resolve`
    （阈值就是「看板显示」里的 `UpcomingThresholdMinutes`），**别在这儿自己比时间**。结果落在私有字段 `_canMinimize`，
    由 `RefreshCorner(force: false)` 在**每秒**那一拍重算（`RefreshNow()` 里调）——所以开考 / 收卷那一刻图标自己翻面，
    值没变就不发通知；`RefreshStatic()` 与 `OnSettingsChanged` 传 `force: true`（手势变了但「能不能最小化」没变，也得刷）。
  - 左下角那颗按钮换图标、换语义（`CornerGlyph` / `CornerToolTip` / `CornerClickMinimizes` 三个现算属性，
    XAML 绑它们，**不要写死 `x:Static`**）。
  - 手势由 `ExamBoardMinimizeGesture`（`Click = 0` / `Hold = 1`，**按数字落盘、只能末尾追加**）决定：
    `Click` 时单击就收（`FinishCornerPress` 里 `CornerClickMinimizes` 为真 → `MinimizeRequested`），
    `Hold` 时走满 2 秒才收（`OnHoldTick` 里分叉）。**手势是 `Hold` 时单击什么都不做**——那颗按钮那时等于第二颗退出键，
    碰一下不该把看板收起来。`Off` 档 / 不在考试期时那颗按钮仍是「长按 2 秒退出」，退出**不做单击**（避免误触）。
  - 收起 / 回来 / 退出全归窗口自己：`ExamBoardWindow.IsMinimized` + `Minimize()` / `RestoreFromMinimize()`，
    **不另立单例服务**——「看板收起来了没有」本来就是这扇窗的状态。`App.IsExamBoardWindowOpen` 把 `IsMinimized` 也算成
    「看板还在」（自动放映看门狗在 `ExamAutoShowHostedService.Tick` 里拿它判用户关窗，漏了这一条最小化就会被当成关窗、
    结束放映、下一拍又弹一个回来），`App.ShowExamBoardWindow()` 会先 `RestoreFromMinimize()` 再 `ShowAndActivate`。
  - 屏幕上留下的那颗是 `Corkboard/Views/ExamBoard/ExamBoardFloatingWindow.axaml(.cs)`：**圆角矩形**（不是圆）、
    左边软件图标（`AppIcon.LoadBitmap()`，与窗口/托盘同一份母图）、右边一行字，**单击回来、按住 2 秒退出**，
    长按进度用底部一条细 `ProgressBar` 表示。文字 / 缩放（60%–200%）/ 九宫格位置全在 `ExamBoardSettingsConfig`
    （`FloatingButtonText` / `FloatingScalePercent` / `FloatingAnchor`，文字空串 = 用 `Settings_Exam_Floating_DefaultText`），
    尺寸与夹取读 `Corkboard.Core/Exam/ExamBoardFloatingStyle`（`Corkboard.Core.Tests/ExamBoardFloatingStyleTests.cs` 钉死），
    **不要在 XAML 或 code-behind 里另写一组数字**。位置复用作业板收纳那套 `DockAnchor`（默认 `BottomCenter` = 屏幕中央底部），
    定位一律走 `FloatingWindowLayer.Place`，窗口是 `SizeToContent`，所以每次 `SizeChanged` 都要重摆一次。
  - ⚠️ **「按住 2 秒退出」必须写在按钮上**：用户的原话是「很多用户不知道长按那个浮窗按钮能够退出」——
    所以按钮里**常驻**一行小字（`HintText` ← `CR.Exam_Board_FloatingHoldHint`，字号走
    `ExamBoardFloatingStyle.ResolveHintFontSize`，无字模式时跟标题一起藏），**不要**做成悬停气泡或几秒后消失的提示：
    触屏用户根本不会悬停，错过一次就再也不知道了。ToolTip（`CR.Exam_Board_FloatingToolTip`）只是补充，不是主渠道。
  - 最小化期间改浮窗的文字 / 大小 / 颜色 / 图标 / 缩放 / 位置要当场生效：订阅挂在**浮窗自己**身上
    （`ExamBoardFloatingWindow.Attach(settings)` / `Detach()`，跟着 `ExamBoardSettingsConfig.PropertyChanged` 重跑 `Apply`）——
    看板这时是藏起来的，别再绕「看板窗口收到配置变化再转给浮窗」那一层（多绕一层就多一个会断的地方）。
    看板窗口只在 `ShowFloatingWindow()` 里 `Attach` 一次；窗口关掉时浮窗必须一起 `Close()` + `Detach()`
    （否则屏幕上留一颗点不动的孤儿按钮，或者窗口没了还挂着一份配置订阅）。
  - **浮窗的外观也是可配置的**（用户要求「单独设文字颜色、图标能关、能自己传一张、还能只留图标」）：
    `ExamBoardSettingsConfig.FloatingTextColor`（默认白）/
    `ShowFloatingIcon`（默认开）/ `FloatingIconPath`（空 = 软件自带图标）/ `FloatingIconOnly`（无字模式），
    全部在 `ExamBoardFloatingWindow.Apply` 一处落到控件上（`CaptionText.Foreground`、`AppIconImage.Source`），
    **不要在 XAML 里写死文字颜色**（否则用户配的会被本地值盖掉）。
    ⚠️ **按钮底色不开放配置**：`RestoreButton` 走 XAML 上的 `Classes="accent"`、跟随主题强调色——
    用户说过「按钮底色的话，我不强求了，把那个设置项给删掉」，所以 `floating_background_color` 这个键连同设置卡一起下架了，
    `Apply` 里**不要**再给 `RestoreButton.Background` 赋值（会把主题色顶掉）。
    - 无字模式给图标放大一档：`ExamBoardFloatingStyle.ResolveIconOnlyIconSize`（`IconOnlyIconBoost = 1.8`）与 `ResolveIconOnlyPadding`；
      此时 `IconCaptionGrid.ColumnSpacing` **必须归零**——文字那一列虽然藏了，列间距还在，按钮会空出一条缝。
      关掉 `ShowFloatingIcon` 时 `Normalize()` 会把 `FloatingIconOnly` 一起打回 `false`（没有图标就没有「只有图标」这一档），
      设置页那颗开关同时 `IsEnabled="{Binding Settings.ShowFloatingIcon}"`。
    - 自定义图标只认本地图片路径：`ResolveIcon` 用 `new Bitmap(path)` 载入并缓存（同路径复用），
      载入失败**不清用户选的路径**、只让这一轮退回默认图标——文件被删 / 被移走时设置页还显示着它，用户才知道发生了什么。
      选文件走 `TopLevel.GetTopLevel(this)?.StorageProvider.OpenFilePickerAsync`（设置页 code-behind），整段包 `try/catch`：
      文件选择器在某些平台会抛，换图标失败不该把整个设置页带走。图片只在窗口 `Closed` 时 `Dispose`。
    - ⚠️ **别把指针处理器挂在 `Button` 上、也别指望 XAML 上的 `PointerPressed="..."`**：`Button` 在它的类处理器里
      就把 `PointerPressed` / `PointerReleased` 标成 `Handled` 了（还顺手 `Capture`），挂在元素上的实例处理器
      一个都收不到——症状就是「点浮窗毫无反应、长按也不退出」（用户报过两次，第一次误判成 `Capture(null)` 的顺序问题）。
      现在的做法：**单击交给 `Button.Click`**（`OnRestoreClick`），长按用 code-behind 里带 `handledEventsToo: true`
      的 `AddHandler(PointerPressedEvent/PointerReleasedEvent/PointerCaptureLostEvent, …, RoutingStrategies.Bubble, true)` 拿按下时刻；
      长按走满 2 秒时先置 `_exited`，把松手后紧跟的那次 `Click` 吃掉（否则「长按退出」会紧接一次「单击恢复」，看板又跳回来）。
      同理，`ExamBoardWindow` 左下角那颗是 `Border`（不是 Button），XAML 上的指针处理器照旧有效。
      从别的窗口堆叠里回来时还要 `Topmost` 抖一下（`true` → `false`）+ `Activate()`，并且先把 `WindowState` 摘到 `Normal`
      再设回 `FullScreen`——`Hide()` 期间状态一直是 `FullScreen`，不先摘一次「设成全屏」就是空操作，样式不会被重新应用。
    - 颜色 / 图标在设置页走**草稿 + 防抖**（`Corkboard/Helpers/DebounceTimer`，400ms 后才写配置）而不是双向绑配置：
      `ColorPicker` 在光谱里拖一下能连发几十次变化，直接绑等于拖一次写几十遍 `settings.json`；
      文字框与大小框仍是失焦提交（`CommitAll()`），离页时 `CommitAll()` 会 `FlushColorDrafts()` 把最后一笔补上。
    - **设置页按三张卡分组、卡里一行一项**（用户要求「搞个分类、用那种展开式的设置下拉框」）：
      「最小化」（①什么时候能最小化 = `MinimizeMode` 下拉、②手势 = `MinimizeGesture` 下拉，后者 `IsEnabled="{Binding IsMinimizeEnabled}"`）/
      「浮窗按钮」（文字 / 大小 / 位置）/「按钮外观」（文字颜色 / 图标开关 / 自定义图标 / 只显示图标），
      行写成 `fa:FASettingsExpander` 的**直接子元素** `fa:FASettingsExpanderItem`。**没有「最小化」总开关**——
      那一档由 `MinimizeMode` 的 `Off` 顶替，两处开关表达同一件事只会互相打架。
      ⚠️ 卡里**不许再套一张 `fa:FASettingsExpander`**：FA 的展开动画会在内容还没挂到可视树时去量里层卡，
      直接 `NullReferenceException`（见 `Corkboard/Views/AGENTS.md`），要分块就用分区标题，不是用卡片包卡片。

### 自动放映（到点自己全屏）

- **参不参加由每块看板自己决定**：`ExamBoardDocument.AutoShowEnabled`（JSON 键 `auto_show_enabled`，**默认 `false`**）。
  开关在「设置 → 考试看板 → 考试安排」的看板列表每行最前面（`ExamBoardItem.AutoShowEnabled` → `IExamService.SetAutoShow`）。
  ⚠️ `SetAutoShow` **不换当前看板、也不发 `Changed`**：开关正被按着，列表一重建就把控件抽走了。
- **全局参数在「设置 → 考试看板 → 自动放映」**（`PageInfo("settings.exam.autoplay")`，配置 `ExamAutoShowSettingsConfig`
  挂在 `Config.ExamAutoShow`）：`LeadMinutes`（提前量，默认 10，夹 1–120）、`AlsoShowAfterStart`（**默认 `true`**：
  软件中途启动、已经开考了也补弹一次）、`CloseDelayMinutes`（最后一场考完再放多久，默认 10，`0` = 收卷就收）。
  数值走草稿 + 失焦提交，**不要双向绑配置**。
- **规则全在 `Corkboard.Core/Exam/ExamAutoShowPolicy`**（纯函数，`Corkboard.Core.Tests/ExamAutoShowTests.cs` 钉死）：
  放映期 = `[第一场开考 − 提前量, 最后一场收卷 + 延迟)`；`FindTrigger` 挑这一拍该弹的那一场，`IsFinished` 判该不该收，
  `FindNextTrigger` 只给设置页状态卡用。边界**左闭右开**，与 `ExamStatusPolicy` 同一口径——
  **别处不许再算一套「离考试还有几分钟」**。放映范围由考试本身决定，没考试的看板没有放映期，
  所以不会「今天把开关打开、明天就开始放」。
- **看门狗 `Corkboard/Services/Exam/ExamAutoShowHostedService`**（`BackgroundService`，15 秒一拍，`BuildHost` 里
  `AddHostedService`）：时间一律 `NtpTimeService.NowLocal`，判定与开窗都在 UI 线程（`Dispatcher.UIThread.InvokeAsync`）。
  弹之前若参演的看板不是当前那块，先 `IExamService.SelectBoard`——全屏看板显示的永远是「当前看板」。
- **同一场只弹一次**（`_shownExamIds` 兜着）：用户长按退出之后不该被弹回来。`_sessionBoardId` 记着「哪一块是自动弹出来的」，
  **只有它归看门狗收**——用户自己开的看板不能被当成放映结果关掉。最后一场考完（延迟到了）才
  `App.CloseExamBoardWindow()` 并清空记录。
- `App.axaml.cs` 为此补了两个静态成员：`IsExamBoardWindowOpen` 与 `CloseExamBoardWindow()`
  （后者**先清字段再关窗**，看门狗下一拍就要读到「已关」）。设置页的「立即放映」只是直接调 `App.ShowExamBoardWindow()`，
  **不动当前看板**。
- **当前看板自己也跟着换**（`ExamAutoShowSettingsConfig.AutoSwitchBoard`，JSON `auto_switch_board`，**默认开**；
  开关在「自动放映」页「当前看板跟着换」那张卡）：全屏看板显示的永远是 `IExamService.ActiveBoard`，
  不换的话手动开看板时看到的还是上次手选的那块。规则在 `Corkboard.Core/Exam/ExamBoardSwitchPolicy`
  （`Corkboard.Core.Tests/ExamBoardSwitchTests.cs` 钉死）：**正在考的那块 > 今天接下来要考的那块 > 谁都不切**
  （返回 `null` 表示保持现状，**不**切回什么默认看板）；**只看今天**——考试前一天不切、考完不切回去；
  多块同时满足取开考最早的。这一步**不看 `AutoShowEnabled`**（没参加自动放映的看板照样会被切），
  而且**只换当前看板、不弹窗**：弹不弹仍由每块看板自己的开关决定。
  ⚠️ **用户手动换过看板就让位到下一场开考**（`_manualOverrideUntil` + `ExamBoardSwitchPolicy.FindNextStart`）：
  设置页点「设为当前」、打开编辑窗口都算，刚挑的那块不能被十几秒后的自动切换改回去；一场都没排就一直让位。
  自己切的都走 `SwitchTo()`（顺手更新 `_lastActiveBoardId`），免得被当成用户手动切换。

### 考试文件的导出与导入

- **一份文件只装考试本身**：`.cbexam`，内容是 JSON，外壳是 `Corkboard.Core/Exam/ExamEntryFile`
  （`format = "corkboard-exam"`、`version`、`exported_at`、`board_title`、`exams[]`）。
  单场导出就是只有一条的清单，导入一次能吃下多场——**别为「单场 / 多场」做两套格式**。
- **格式与容错都在 Core**（`ExamEntryFile`，`Corkboard.Core.Tests/ExamEntryFileTests.cs` 钉死）：
  `Create` 排好序并 `Clone`；`TryParse` 除了标准外壳还认**裸数组**与**单场裸对象**（别人拿记事本拼一份也能导进来）；
  `ResolveImportableExams` 逐条过 `ExamEntry.Normalize` 并丢掉空科目。
  读盘用 `ConfigServiceBase.JsonOptions`（snake_case + 缩进），与配置文件同一套，人可以直接改。
- **App 层只管对话框与字节**：`Corkboard/Services/Exam/ExamFileService`（`BuildHost` 里 `AddSingleton`），
  约定与作业板导出一致——用户取消返回 `false` / `null`，其余失败抛 `InvalidOperationException` 由调用方提示；
  写盘**不带 BOM**（文件是给人看的）。
- **入口四处，都能「在外面」直接点到，不用先进编辑窗口**（用户反馈过「为什么要点进去才能导出」）：
  ① 设置页「考试安排」**每块看板行**上那颗 `ArrowExportFilled`——导**那一行**的看板（行内按钮的
  `DataContext` 就是那行的 `ExamBoardItem`，`OnExportBoardClick` 按 `sender` 认行）；
  ② 同一页看板卡页脚、与「导入考试」并排的那颗——导**当前那块**看板；
  ③ 「编辑看板」窗口每行第 5 列的 `ArrowExportFilled`——导**那一场**；
  ④ 导入只有一处：看板卡页脚 `OpenFilled`，落点是**当前那块**看板。
  ⚠️ 导出那颗**别用 `SaveFilled`**：软盘图标读起来像「保存」，用户根本找不到导出入口（反馈过）。
  ⚠️ 行内导出按钮与页脚导出按钮**共用同一个 `OnExportBoardClick`**，别再写第二个处理器。
  导入走 `ExamScheduleSettingsPageViewModel.ImportExams` → `IExamService.AddMany`：**追加到当前那块看板**、
  只落盘一次、Id 撞了自动换新，**不替换**原有排期。
- ⚠️ 导出/导入按钮要加在考试表的**第 5 列**，表头与数据行的 `ColumnDefinitions` 必须同步改（`140,*,150,150,104`），
  否则 `*` 列宽不同、后面几列整体错位。

### 看板字号（考试看板上每一处文字的大小都可调）

- **一处文字一个字号，全在 `ExamBoardSettingsConfig` 上**（`title_font_size` / `subtitle_font_size` /
  `clock_font_size` / `clock_alone_font_size` / `clock_date_font_size` / `info_caption_font_size` /
  `info_subject_font_size` / `info_time_font_size` / `info_remaining_font_size` / `info_paper_font_size` /
  `table_header_font_size` / `table_font_size` / `table_subject_font_size` / `empty_font_size` /
  `empty_hint_font_size` / `exit_icon_size`），默认值与上下限**只在 `Corkboard.Core/Exam/ExamBoardTextStyle`**
  （一般字 8–120、时钟那两档 8–480），读盘后 `Normalize()` 一律过一遍 `Clamp` / `ClampClock`。
- ⚠️ **看板窗口 XAML 里不许写死 `FontSize`**：`ExamBoardWindow.axaml` 的每一处文字都绑 `ExamBoardViewModel` 上
  对应的那个属性（`RefreshFontSizes()` 从配置搬过来、夹一次）；写死的那个用户就改不动。
  Style 里的 `Setter` 优先级高于本地值，所以 `Border.exit-hint TextBlock.icon` 那条样式**只留前景色**，
  字号挂在 `TextBlock` 自己的 `FontSize="{Binding ExitIconSize}"` 上。
- **设置页是单开的「看板字号」页**（`PageInfo("settings.exam.font")`，`ExamFontSettingsPage(.axaml|.cs)` +
  `ExamFontSettingsPageViewModel`），外观是**四张按区域分组的可折叠卡**：标题与副标题 / 时钟 /
  时钟下面的考试信息 / 考试表格与其他（含空状态与退出图标）。分组在
  `ExamFontSettingsPageViewModel.BuildGroups()` 里按 `ExamFontSizeItem.Id` 归拢，
  **默认全部收起**（用户明确要求：一页看到四个组名，点开才见具体字号）。
  ⚠️ **卡就是 `fa:FASettingsExpander`（Header 组名、Description 组简介），卡里每一行是
  `fa:FASettingsExpanderItem`（Content 名称、Description 简介、Footer 放滑块）**——与「作业板」设置页同一套组件。
  不写 `IsExpanded`（默认收起）、不写 `Margin`（页面栈的 `page-container` 间距管卡间距）。
  **别退回手写的 `Border.setting-group` / `Border.setting-row` 行**：那版被用户指着说过
  「单独设置框换成组件里的设置项、不要用之前那种，全部换掉」；也别在卡里再嵌一层卡。
- **一项字号 = `ExamFontSizeItem` 一行**（`Id` / 名称 / 简介 / 默认值 / 是否时钟 / 读写两个委托），
  页面遍历 `FontSizeGroups` 画，**不给每项单写一遍 XAML**；加一项新文字只要在 `BuildItems()` 加一行、
  在 `BuildGroups()` 归到某组、再加三个资源键（名称 / 简介 / …），`Resources.Designer.cs` 同步。
- **字号用滑块调，不设精确输入框**（用户明确要求「把输入框改成滑块」+「不留精确输入」）：
  `Slider` 的 `Minimum` / `Maximum` 直接读 `ExamFontSizeItem.Minimum` / `Maximum`（下限 8、一般上限 120、
  时钟那两档 480），`TickFrequency="1"` + `IsSnapToTickEnabled` 保证只出整数；当前值显示在滑块右边那一行
  （`ValueText`＝「108 号」/「108 pt」，单位走 `Settings_Exam_Font_Unit`）。
  ⚠️ **没有 `NumericUpDown`，也别再加回去**：`Slider` 只吃 `double`，草稿仍是 `decimal?`，
  两者由 `ExamFontSizeItem.SliderValue` 这层适配（取整 + 夹取）转接。
- **拖动期间浮窗只放预览，不放数值**（用户：「浮窗上面字号去掉，就在滑块右边就行」）：`Popup` 挂在滑块上
  （`PlacementTarget="{Binding #SizeSlider}"`、`Placement="Top"`、`IsLightDismissEnabled="False"`），
  内容是「按当前字号渲染的一句预览」——预览文字就用这一项自己的名字（`SampleText`＝`Name`，
  所以 16 项不用各写一句示例），退出图标那一项画字形（`SampleGlyph` = `FluentIcons.ArrowExitFilled`）。
  ⚠️ **预览字号封顶 96**（`ExamFontSizeItem.PreviewMaxFontSize`）：时钟能到 480，照实数渲染会把浮窗撑出屏幕；
  超过这一档在浮窗里补一句 `Settings_Exam_Font_PreviewScaled`（「预览最多放到 96，数值照实」）。
  ⚠️ **`Popup` 必须 `IsHitTestVisible="False"`**：它挂在滑块上方，能吃掉指针的话拖到一半就断了。
- **拖动状态由页面置位、不是绑 `IsPointerOver`**（`ExamFontSettingsPage.axaml.cs`）：`PointerPressed` 把
  `ExamFontSizeItem.IsDragging` 置真（浮窗随之打开）、`PointerReleased` / `PointerCaptureLost` 收回并 `CommitAll()`。
  处理器挂在**页面**这一层、`Tunnel | Bubble` 全写、`handledEventsToo: true`——滑块的指针事件有一部分被
  Thumb / 轨道自己吃掉，逐个滑块去挂要跟着模板复用走，容易漏挂或多挂。
  键盘调（方向键）不经过指针，所以另外挂了 `RangeBase.ValueChangedEvent`：不在拖的时候值一变就提交。
- **数值走草稿 + 松手提交**（`decimal? Draft`，松手 / 离开页面 `CommitAll()`）：配置没有防抖，直接双向绑等于
  拖一路写几十遍盘。
- 「恢复默认字号」一把把十六项写回默认值（不做二次确认）；
  那句 `Settings_Exam_Font_ResetHint` 就写在这颗按钮旁边。
  ⚠️ **它不套 `FASettingsExpander`**：一行说明 + 一颗按钮，套上去白给一个折叠箭头、
  点开里面什么都没有（用户明确说过「没必要搞展开设置框」），直接用 `Border Classes="card"`
  加一张 `Grid ColumnDefinitions="*,Auto"`。
- 页面标题也不套卡：顶部一段 `TextBlock` + 一行简介（`Settings_Exam_Font_PageTitle` /
  `PageDescription`），与「看板显示」「自动放映」等考试设置页一致。
- 版式冒烟开关 `--open-exam-font`（`AppConsts.SettingsExamFontPageId`）直接落到这一页，
  与 `--open-settings` / `--open-about` / `--open-exam-board` / `--open-oobe` 都在
  `App.StartHostAsync` 末尾那一段。

### NTP 校时

- 核心在 `Corkboard.Core/Time/`：`NtpPacket` / `NtpTimestamp` / `NtpClient`（UDP 123，异常一律转 `NtpSyncResult.Failure` 不抛）/
  `NtpServerCatalog`（默认 8 个国内源：阿里云 ×2、腾讯云 ×2、`cn.pool.ntp.org`、`cn.ntp.org.cn`、交大、`time.edu.cn`）/ `NtpTimeService`。
- **应用内唯一时间来源是 `NtpTimeService.NowLocal` / `UtcNow`**（= 系统时钟 + 偏差）；关掉同步就把偏差归零退回系统时钟。
  考试看板与「考试安排」页都用它，不要各自 `DateTime.Now` 了事。
- 后台由 `Corkboard/Services/Time/NtpSyncHostedService`（`BackgroundService`）每 30 秒醒一次，按 `SyncIntervalMinutes`（默认 30）决定要不要真同步；
  失败**保留上次偏差**，不因为一次超时就把时钟跳回系统时间。
- ⚠️ **`NtpTimeService.StatusChanged` 一律在 UI 线程上发**（`RaiseStatusChanged` 里 `Dispatcher.UIThread.CheckAccess()` 判断，不在就 `Post`）：
  `SyncNowAsync` 全程 `ConfigureAwait(false)`（不能在 UI 线程上等网络），`finally` 里那一发**落在线程池线程上**；
  订阅者拿到事件就改绑定属性、调 `NotifyCanExecuteChanged`，跨线程直接抛「The calling thread cannot access this object...」，
  而且是在 `finally` 里抛的，会顺着 `AsyncRelayCommand` 冒到 Dispatcher 变成**未处理崩溃**（「立即同步」点一下就可能崩，
  用户报过一次）。线程口径由服务保证，**不要再让每个订阅者自己判一次**；新加的事件型服务照这个写（对照 `CloudSyncService.RunOnUiThread`）。
