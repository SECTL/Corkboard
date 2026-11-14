# Corkboard.Core/ 子树约定

根目录 `AGENTS.md` 里的跨层规矩（技术栈、结构、域归类、设置怎么加、本地化、命令）在本子树同样成立，这里只补本子树自己的约定。

## 配置管道与自动保存

三类：`ConfigBase`（模型基类，`Corkboard.Shared`）、`ConfigServiceBase`（读写实现）、
`ConfigHandlerBase<T>`（自动保存句柄）。落盘路径 `data/config/settings.json`。

- **自动保存覆盖到子配置**：`ConfigHandlerBase` 递归订阅根对象与任意深度的 `INotifyPropertyChanged`
  子对象，所以 `General.Basic.*` 这类改动会自动写盘，加子配置不需要额外接线。
- **但集合只订阅集合本身，不订阅集合里的元素**：`ObservableCollection<T>` 的增删会自动落盘，
  改元素字段不会。需要可编辑元素时自己在 VM 里订阅元素属性并显式 `MainConfigHandler.Save()`
  （范例见 `BoardSettingsPageViewModel.SaveAll`）。
- **本机专属的设置项贴 `[LocalOnly]`，不要再手写云同步排除清单**：特性在
  `Corkboard.Core/Attributes/LocalOnlyAttribute.cs`——贴在子配置的**属性**上只排除这一项（路径形如
  `general.basic.autostart`），贴在**类**上整段都不参与云同步（`CloudSyncSettingsConfig` 就是这么标的）。
  排除路径由 `Corkboard.Core/Services/Cloud/LocalOnlyPaths.Build(typeof(MainConfigModel))` 反射收出来
  （属性名按 snake_case 转、`[JsonIgnore]` 的转发属性跳过、`[JsonPropertyName]` 优先），
  `CloudSyncExclusions.Prefixes` 直接用它算——**别往 `CloudSyncExclusions` 里再加路径字符串**。
  源生成属性上的写法是 `[property: LocalOnly]`（`[ObservableProperty]` 的字段必须显式 target 到 property，
  直接写 `[LocalOnly]` 会贴到字段上、反射收不到）。清单由
  `Corkboard.Core.Tests/CloudSyncExclusionsTests.Prefixes_MatchConfigurationAttributes` 钉死，
  新增本机专属项时把那份期望列表一起改掉。
- **业务数据不进 `settings.json`**：作业板数据全在 `data/board/`（与设置文件的 `data/config/` 分开），
  由 `BoardService` 在每次变更后落盘（`IBoardService.Subjects` 是「调用方直接改集合、
  改完调 `Save()`」的用法）。这样绕开了上一条的坑，也把「应用偏好」和「用户数据」分开。
  设置页负责编辑它们，但**存储位置不等于设置项**：编辑界面在设置里，数据仍在 `data/board/`。
- **一条作业一个专有文件**：`data/board/<年>/<月>/<日>/<作业 Id>.cbnote`（`NoteStore` + `NoteFileFormat`，
  扩展名 `.cbnote`、不是 JSON）：元数据、富文本内容（`text=` 块）、手写笔迹（`ink=` 块）全在同一个文件里；
  文件名就是作业 Id，改内容不换文件、删作业删文件。科目这类**定义**才写单份 `data/board/board.json`。
  作业从内存整份重写时，没有落笔的旧 `.cbnote` 会被删掉、空目录逐级回收。
  ⚠️ **旧版 `notes.json` 只用于读取兼容与迁移**：`LoadAll` 会把它一起读进来（同 Id 以新文件为准），
  下一次 `SaveAll` 把所有作业改写成 `.cbnote` 后删掉旧文件；**解析失败的旧文件原地保留**（留给用户排查），
  删之前会再解析一次，避免云同步里临时 new 出来的 `NoteStore` 误删。
  旧版挤在 `data/config/board.json` 里的数据由 `BoardService` 在归档为空时一次性搬迁（见 `LegacyNoteFile`）。
- **过期作业是「打标记清理」不是「删文件、搬目录」**：到点由 `HomeworkCleanupService` 给命中的作业写上
  `Note.CleanedAt`（`DateTimeOffset?`，`[JsonIgnore(WhenWritingNull)]`），**数据原地不动**——
  作业仍在 `IBoardService.Notes` 里、也仍写在它自己那个 `data/board/<年>/<月>/<日>/notes.json` 里，
  只是主页面不再显示（`BoardPageViewModel.RebuildNotes` 按 `Note.IsOnBoard` 过滤）。
  之所以不真删：回放要从历史日志重放出那一刻的板面，内存里的作业被摘掉会让「还原到此刻」少一份原件；
  用户要的就是「只把主页面上的清掉，文件本身还在」。
  ⚠️ **已清理的作业必须继续留在 `IBoardService.Notes` 里**：`SaveAll` 会删掉这一轮没落笔的 `.cbnote` 文件，
  把它们从集合里摘掉就等于删文件。
  ⚠️ **`data/board/_trash/` 是旧版遗留目录**（`NoteStore.EnumerateNoteFiles` 里按字面量 `_trash`
  排除）：它按 `Directory.EnumerateFiles(root, "notes.json", AllDirectories)` 扫，遗留回收目录里的
  `notes.json` 会被当成活作业读回界面，紧接着被 `SaveAll` 的「删掉这轮没写过的文件」连同目录一起删掉；
  旧版本已经归档的数据不能被这次改动动到。
- **批量清理走 `IBoardService.CleanMany(ids, cleanedAt)`**：只给 `CleanedAt is null` 的作业打标记、
  只落盘一次、集合变更只通知一次；**不要**循环调 `Update`（那会一条落盘一次、界面重建一次）。
- **清理规则只有一条：过了截止日期才算过期**（`HomeworkExpiryPolicy.IsExpired`，纯函数、`today` 从参数进来）。
  没填截止日期的作业不管建了多久都不会被清掉（曾经的「保留天数」已按用户要求删掉）；
  **截止当天不算，第二天才清**（`due < today`，用户明确要求「今天截止的作业今天不应该清理」）。
  这条判据与界面那行「今天截止 / 过期 N 天」**完全一致**（`HomeworkDueDateFormatter.IsOverdue` 也是 `due < today`），
  卡片写着「今天截止」的作业不会再被当天的清理顺手收走。
  ⚠️ 两条判据必须保持同一个方向（界面说已过期的，清理一定也认为过期），改一边要看另一边的用例。
  `DueDate` 是 `Note` 上的 `DateOnly?` 结构化字段（`[JsonIgnore(WhenWritingNull)]`，没填的不落盘），
  **不是**靠名字去认的自由文本字段——清理器要统一读取它，不能靠字段名去猜。
  ⚠️ `BoardService.Update` 是逐字段复制的，**给 `Note` 加字段必须同时加进 `Update`**；
  它按 `Id` 定位、`CreatedAt` 与 `Order` 不参与编辑（编辑今天这条作业不该动到以前的）。
- **只有一个触发时刻：今天的清理时刻**（`HomeworkCleanupSchedule.ShouldRun` / `DueSlotToday` 比「今天的
  设定时刻」与进程内的 `HomeworkCleanupService.LastRun`，每天/每周某天某时刻）。
  ⚠️ **没到今天的时刻之前一律不动手**：早先按「最近一个过去的时刻」算，凌晨启动会把昨天那个时刻当成
  错过的补清、当场清一遍，用户看到的就是「没到点就清理了」。应用没开着而错过今天的时刻的，
  下次启动只要已经过了今天的时刻就补上；按周清理时其它日子一整天都不动手。
  因此不需要状态文件，但 `LastRun` 只活进程内、启动后第一次判定会补清当天那一次。
  应用层由 `HomeworkCleanupHostedService`（`BackgroundService`，注册见
  `App.axaml.cs` 的 `AddHostedService`）每分钟看一眼是否到点——不睡到下一个时刻，是因为用户随时会改时刻
  或关开关；动手前先 `Dispatcher.UIThread.InvokeAsync` 回 UI 线程，打标记会重建界面上的列表。
  批量清理走 `IBoardService.CleanMany`（见上一条），**不要**循环调 `Update`（那会一条落盘一次、界面重建一次）。
  这个功能**默认开启**（`CleanupEnabled = true`，用户明确要的默认值）。
  ⚠️ **手动「立即清理」不看时刻、也不看开关**（`RunCleanupNow` 直接调 `HomeworkCleanupService.Run`）：
  点一下就该见效，把过了截止日期的作业当场清掉——曾经的问题正是「到点判定」把它挡住了。
  ⚠️ 改这里必须同时改 `Corkboard.Core.Tests/HomeworkCleanupTests.cs`：判定与界面文案一致（没期限的永不清、
  截止当天不清、第二天清）、没到今天的时刻前不动手、按周只在配置的那天动手、手动清不看时刻、
  已清理的作业留在原文件、不重复计数、旧 `_trash` 目录对存储层不可见，这些都有用例。
  设置页里那个时刻用 Avalonia 自带的 `TimePicker`（`SelectedTime` 是 `TimeSpan?`；FluentAvalonia 里
  **没有**时间类控件），走草稿 + `LostFocus`/离页提交，不要直接双向绑到配置。
  界面上截止日期用 Avalonia 自带的 `CalendarDatePicker`（`SelectedDate` 在 Avalonia 12 里是
  `DateTime?`、不是 `DateTimeOffset?`）配一个「清除」按钮——选择器自己没地方把已挑的日期退回空值，
  「不设截止日期」这个状态只能由外面这个按钮给。
  ⚠️ 它是 `TemplatedControl`（既不是 `Button` 也不是 `TextBox`），**必须加进
  `HomeworkAssignmentForm.axaml.cs` 的 `IsInteractiveSource` 排除表**，否则按在它身上会被当成「拖卡片」。
  卡片上由 `HomeworkDueDateFormatter.Describe` 说成「今天 / 明天 / yyyy-MM-dd / 过期 N 天」。
- 配置文件一律经配置服务（原子写入 + 统一 JSON 选项），**不要自己 `File.WriteAllText` 写配置**。
- **读盘后统一收拾子配置**：`MainConfigModel.NormalizeLoaded()` 由 `FileConfigService.LoadConfig` 在反序列化后调用，
  里面夹字号/宽度/收纳位置、并把**手写笔宽旧的默认值 4 与 10 迁移成 6**（`BoardSettingsConfig.NormalizeBoardExtras`）。
  ⚠️ 这类迁移**必须挂在这里**，不要塞进设置页——作业板在设置页打开前就会按脏值渲染、手写表单也会继续用旧默认笔宽
  （曾经 `NormalizeBoardExtras`/`NormalizeCleanup` 没有任何调用点，等于死代码）。
- **作业内容是所见即所得富文本（HTML 片段）**：`Note.Content` 存的就是第三方富文本控件
  （`AvaloniaRichEditor`）`ToHtml()` 的输出，颜色/字号是片段里的行内 `<span style="…">`；
  `Note.ContentKind`（`Markdown = 0` / `Html = 1`，枚举按数字落盘）标记这一段是哪种格式——
  **旧数据的 JSON 里没有这一项，读出来就是 0 = Markdown**，所以不需要额外的版本字段。
  写内容**一律走 `Note.SetHtmlContent(html)`**（同时写 `Content` 与 `ContentKind`，
  并清空遗留字段），不要只改 `Content`。界面侧由 Core 的可复用控件 `RichTextBlock` 承载
  （卡片上只读、表单里可编辑），**不要在页面里直接用第三方 `RichEditor`**。
  默认字号 / 默认颜色是**设置**（`settings.json` 的 `board_settings.default_content_*`）：
  `RichTextBlock` 在装载时把它们逐文本节点补成行内 `<span>`（`RichTextHtmlStyling.Apply`），
  回写时再按 style 声明逐条摘掉（`RichTextHtmlStyling.Strip`）——**不要把默认样式烘进落盘的 HTML**，
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
  所以 `RichTextHtmlStyling.Apply` 给空块垫一个带默认色的**零宽「颜色种子」**（`SeedChar`，普通文本是 `\u200B`）
  ——它**绝不允许落盘**，回写前必须 `RichTextHtmlStyling.StripSeeds`（编辑器的 `ToHtml()` 会把它写成 `&#8203;`，
  所以摘的时候要解实体再比）。新敲进来的字没有颜色可继承，由 `RichTextBlock` 在打字路径上补色
  （`SetForeground`，触发点是 `TextChanged` / `IsModifiedChanged` / `OnTextInput` 与一条 150ms 心跳——
  库的 `TextChanged` 要等下一次重绘才发，真机上还会整段不发，光靠它自会黑字 + 回写停摆）；
  取主题色**必须显式带上 `ThemeVariant`** 去 `TryFindResource`，否则拿到的是亮色字典里的近黑 `#E4000000`。
- **`Note.Formats` / `ContentFontSize` 只是读旧数据的遗留通道**（`LegacyTextFormatRange` 因此退化成旧数据 DTO）：
  旧作业由 `BoardService` 在装载时经 `LegacyNoteContentConverter` 一次性转成 HTML 并立刻落盘，
  只在 `ContentKind == Markdown` 时动作（天然幂等）；转完这两项为空，序列化时整项省略。
  **新代码不要再往里写东西**；等确认没有旧数据了，可以连同 `LegacyTextFormatRange` 与 `Markdig` 依赖一起删掉。
- 数据根只能通过 `Corkboard.Shared/Utils.cs` 访问，新增持久化目录走 `Utils.GetFilePath/GetDirectoryPath`。
- 枚举按数字落盘，因此只能在末尾增删枚举成员。

## 历史存储与时空回放

回放要能看到「那一刻板面上的样子」，包括**后来被删掉的作业**与**编辑之前的旧内容**，
所以作业改动必须**追加式**记下来，不能只靠「现存作业的 `CreatedAt`」倒推。

- **布局**（`Corkboard.Core/Board/History/`，数据根由 `Utils` 给）：
  `data/history/board/<yyyy-MM-dd>.jsonl` 每天一个追加文件，一行一条事件；
  快照按内容寻址存在 `data/history/board/blobs/<哈希前 2 位>/<sha256 小写>.br`（Brotli，.NET 自带）。
  ⚠️ **历史目录不能塞进 `data/board/`**：云同步按整个 board 目录打包上传，历史跟上去会把备份涨大。
  ⚠️ **同一份内容只存一个 blob**（同卡反复保存不会存一堆版本），**同一条作业在合并窗口内的连续修改合并成一条事件**
  （`NoteHistoryStore.ReplaceLast` 只重写自己刚写的那一行；别的作业插进来就不合并）。
- **事件与快照**：`NoteHistoryEntry(DateTimeOffset At, Guid NoteId, NoteHistoryAction Action, string? Snapshot)`，
  `Snapshot` 是 blob 哈希（`Clean` 不带内容）；`NoteSnapshot` 是整条作业的字段副本（含手写笔迹，
  不含 `Formats`——旧数据在启动时已经转成 HTML）；`Note -> 快照 -> Note` 的转换**必须新建对象、不共享引用**。
- **组件职责**：`INoteHistoryStore`/`NoteHistoryStore` 只管读写（追加、合并、存/取快照、`Prune`、`GetStatistics`）；
  `BoardHistory` 是纯逻辑（`BuildStops` 生成停靠点、`StateAt` 重放出某一秒的板面、`Apply` 处理五类动作）；
  `BoardHistoryPolicy` 放默认值与夹取（`ClampCoalesceMinutes`/`ClampRetentionDays`/`ClampMaxSizeMb`）。
  **不要在 ViewModel 或页面里另写一套重放逻辑**，也不要在 `BoardHistory` 里碰 IO。
- **写入点全在 `BoardService`**：`Add` → Create（时刻用 `note.CreatedAt`）、`Update` → Update（合并窗口内替换上一条）、
  `Remove` → **先存快照**再 Delete、`CleanMany` → Clean、`RestoreMany` → Restore、`RestoreSnapshot` → 每条各自 Create/Update/Clean/Restore。
  `BoardService` 的历史依赖是**可选参数** `INoteHistoryStore? history = null`（Core 测试仍可用三参构造）。
- **历史从本版开始**：第一次跑起来、历史为空时，`SeedHistory()` 给现存每条作业按 `CreatedAt` 补一条 Create 基线；
  之后不再播种（`LoadAll().Count > 0` 就跳过）。**以前删掉的作业救不回来**，这一点对用户说明过、不要再承诺。
- **三个设置项**（「设置 → 作业板 → 历史与回放」）：保存粒度 `HistoryCoalesceMinutes`（1–10，默认 3）、
  保留天数 `HistoryRetentionDays`（20–999，默认 90）、容量上限 `HistoryMaxSizeMb`（20–2000，默认 200）。
  读盘后由 `MainConfigModel.NormalizeLoaded()` → `BoardSettingsConfig.NormalizeHistory()` 夹取；
  运行期由 `BoardHistoryHostedService` 把它们同步进 `INoteHistoryStore`（`CoalesceWindow`/`RetentionDays`/`MaxBytes`），
  启动时与每小时各 `Prune()` 一次。
- **`Prune` 的顺序**：先按保留天数删整天的日志 → 再按容量上限从最老删到只剩一天（今天那份永远留着）→
  最后删没有事件引用的 blob。返回删掉的天数，日志重、IO 异常只记 warning。
- **恢复与还原别混**：`RestoreMany(ids)` 只把**集合里还在**的作业放回板子；
  「还原到此刻」用 `RestoreSnapshot(IReadOnlyList<Note>)`——快照里有、板子上没有的按快照新建（保留 Id 与
  `CreatedAt`、`Order` 排到末尾），板子上有、快照里没有的收起来，内容不同的整条覆盖。单测见
  `BoardHistoryTests.cs`、`BoardServiceHistoryTests.cs`、`NoteHistoryStoreTests.cs`。

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
  **要加新图标**：从 `Corkboard/Assets/FluentSystemIcons-Resizable.json` 按 `ic_fluent_<name>_20_filled` 查十进制码位、
  转成 `\uXXXX` 加进 `FluentIcons.cs`（例：`ArrowMinimizeFilled = "\uE10E"` 来自 57614，用在收纳按钮与「收纳」设置页；
  「科目与分类」设置页图标改成了 `ArrowSortFilled`）。
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

## 平台统计（在线状态 / 版本 / 使用量）

三类上报**全部走 SECTL 的聚合入口** `POST /api/stats/report`（与账号、云存储同一个 api-proxy）。
三处代码分工固定：

| 位置 | 负责 |
| --- | --- |
| `Corkboard/Services/Auth/SectlAuthEndpoints.cs` | 端点常量：`StatsReportUrl`、`OnlineStatsUrl(platformId)`（查询仍走 `GET /api/stats/platform/{id}`，聚合入口没有查询版本） |
| `Corkboard.Core/Services/Stats/StatsReportEvent.cs` + `StatsReportBatch.cs` | 报文形状、工厂与校验、`ResolveKind()`、批量请求体与响应解析（纯逻辑，可单测） |
| `Corkboard/Services/Stats/SectlStatsReporter.cs`（`ISectlStatsReporter`） | 唯一发请求的地方：超时、批量发送、`results[i]` 逐条判定、只记 debug |

权威接口文档在 `https://sectl.cn/api/docs/index` + `/api/docs/content?path=…`（需 doc token）；
离线副本可以抓进 `docs/sectl/`（`docs/` 在 `.gitignore` 里，属于本机方便、不进仓库）。

- **聚合入口按「哪些键在」判定上报类型**，顺序命中即止、没有 type 字段：
  `field_key`+`value` → `version`/`app_version` → `metrics`/`increments`/`usage`/`delta`/`metric_key` → 兜底 `online`。
  所以**一条事件只能有一种形状**：在线心跳不许带 `version`/`metrics`，版本上报不许带 `metrics`，
  否则数据会**静默落进别的统计口径**（`StatsReportEvent.ResolveKind()` 复刻了这张表，判不出来是 `Unknown`）。
  判不出来服务端回 `400 invalid_request`。
- **一类上报 = 一条事件**：`StatsReportEvent(platform_id, device_uuid?, device_type?, version?, metrics?)`，
  空字段不序列化（`JsonIgnoreCondition.WhenWritingNull`），于是每个事件的报文与旧端点逐字段一致。
  构造只走三个工厂：`CreateOnline` / `CreateVersion` / `CreateUsage`（校验、归一化、设备类型推断都在 Core），
  **不要在服务层自己拼 JSON**，也不要绕过工厂直接 new（会跳过 UUID / 版本号 / delta 范围校验）。
- **一律走批量形态**（`StatsReportBatch` → `{"events":[…]}`），即使只有一条事件：批量最外层固定回 **207**
  （里面全成功也是 207），逐条结果在 `results[i] = {matched,status,body}`，`results[i]` ↔ `events[i]` 一一对应，
  单条失败不打断后面。⚠️ **`207` 但没有可用的 `results[]` 时不能当成功**（`SectlStatsReporter` 把 status 留空按失败处理），
  否则根本没落库的事件会被当成已上报、再也不会重试。空数组本地就挡住（服务端回 `400 events must be a non-empty array`）。
- 上报是**尽力而为的旁路**：除调用方自己取消（按「没成功」返回、增量留在队列）外 `ReportAsync` **不抛异常**；
  单次超时 10 秒，失败只记 debug，绝不影响业务与启动。聚合入口限流 **600 次/分钟**（四个旧端点里最严），
  超限 `429 rate_limited` 带 `retry_after_ms`；现在一次刷写只发一个请求，远低于额度。
- **平台 ID 只有一个来源**：`Corkboard.Core/GlobalConstants.cs` 的 `PlatformId`
  （`6ac3d35400241eff9fbe`）。它就是 SECTL 平台记录的主键，**同时是 OAuth 客户端 ID**：
  `SectlAuthEndpoints.ClientId` 直接取这个常量，不要写第二份字面量；上游 SecRandom 也是
  ClientId == PlatformId。版本上报、在线状态、使用量都从这里取值。
  ⚠️ 原来的 `platform_29648fc4ac3ba07d` 是错的（`GET /api/stats/platform/...` 直接 400），
  已废弃；统计只认 `6ac3d35400241eff9fbe`。
- **在线状态**（`Corkboard/Services/Stats/OnlineStatusService.cs`，`BackgroundService`，注册成单例再挂托管）：
  每条心跳是 `StatsReportEvent.CreateOnline(...)`，只含 `platform_id` / `device_uuid`（`IDeviceIdProvider`，
  落在 `data/config/sectl-device.json`）/ `device_type`。
  ⚠️ `device_type` 是**服务端词表**：`windows-desktop` / `macos-desktop` / `linux-desktop` /
  `android` / `ios` / `unknown-desktop`（另有 `*-web`、`windows-tablet`、`windows-mobile` 等变体），
  **不带架构**（写成 `windows-x64` 会 400，整条上报不生效；实测服务端 400 文案里会把合法词表列全）。
  每 2 分钟上报一次、10 秒超时，失败只记 debug；隐私设置是 `OnlineStatusMode.Off` 时**完全不动网络**。
  位置由服务端按访问地址统计，客户端**不发** IP/城市，也**从不**带 SECTL 账号 ID。
  - 人数的两种响应形态：心跳的 `207` 逐条结果里 `body.online_count`（顶层）；查询
    （`GET /api/stats/platform/{id}`）把人数放在 `online.online_count` 里。
    `StatsReportResult.ResolveOnlineCount()` 与 `OnlineStatsResult.ResolveOnlineCount()` 各认一路，不要只读顶层的。
  - 在线人数缓存在服务里（`OnlineCount` / `HasOnlineCount`），「关于」页的「在线人数」行显示；
    `OnlineCountChanged` 事件驱动刷新，页面 `Unloaded` 里 `Detach()` 解开（页面是 transient 的）。
  - 隐私设置 `PrivacySettingsConfig.OnlineStatusMode` 挂在 `general.privacy`（默认 `Full`），
    界面在「基础设置 → 隐私」分区（正常上报 / 匿名上报 / 停止上报）。读盘后由
    `MainConfigModel.NormalizeLoaded` → `PrivacySettings.Normalize()` 夹取；从云端恢复设置会整个换掉配置对象，
    服务订阅 `MainConfigHandler.Reloaded`，在关闭上报时把缓存一并清掉（不留过期的在线人数）。
- **版本使用人数**（`Corkboard/Services/PlatformVersionReportService.cs`）：启动一次
  `StatsReportEvent.CreateVersion(GlobalConstants.PlatformId, 版本号, 设备标识)`，仍 `Task.Run` 丢线程池、失败只记日志。
  版本号 1–64 字符、首字符字母或数字、其余只允许字母数字与 `. _ + - ( )` 和空格（`IsSupportedVersion`，
  文档规则）；设备标识必须是标准 UUID 并统一小写。原来的 `VersionUsageReportPayload` 已删，别再引。
- **使用量**（`Corkboard/Services/Stats/PlatformUsageReportService.cs`，`BackgroundService`，同样单例）：
  每条是 `StatsReportEvent.CreateUsage(platformId, 指标名, 增量)`，报文是**用法容器**
  `{"platform_id":"…","metrics":{"daily_note_add_count":3}}`（旧端点的 `field_key` / `delta` 单条形态已经不用了）；
  指标名见 `UsageMetricNames`：`daily_app_launch_count` / `daily_note_add_count` / `daily_export_count`。
  ⚠️ 服务端对每个平台有「已登记字段」白名单：没登记的指标名回 `200 {"status":"ignored","reason":"field_not_registered"}`
  （不报错也不计数）。Corkboard 平台目前**还没登记这三个键**，需要在 SECTL 侧登记后统计才真正落库；键名改了就前功尽弃。
  `period` 一律不传：档位由 `daily_*` 前缀兜底到服务端当天（只有明确写 `total`/`all` 才进总档），传了只是多一处能写错的地方。
  - 计数用 `UsageMetricCounter`（Core，线程安全）；待上报增量落盘 `data/config/usage-stats.json`
    （`Utils.GetFilePath`，原子写入）。它**不是设置项**，不要并进 `settings.json`——设置会被云同步带走/互相覆盖。
  - 记录后 2 秒防抖刷写，**一次刷写只发一个请求**（所有待上报指标装进同一个 `events[]`，旧端点是一项指标一个往返），
    按 `results[i]` 只把没落库的那几条放回队列、30 秒后重试；成功的别再重发（会被重复计数）。
    单条增量上限 `StatsReportEvent.MaxDelta`（=10000，服务端规则），**超出的部分拆到下一批**（`carried`）而不是丢掉。
  - 进程退出时文件里还有没上报的，下次启动接着传。
  - 埋点位置：服务启动（`RecordAppLaunch`）、布置作业成功
    （`HomeworkAssignmentFormViewModel.Confirm` 的 `_boardService.Add` 之后，编辑不算）、
    导出成功（`BoardPage.ExportAsync` 的图片保存 / 复制到剪贴板 / 表格三处，取消与失败不算）。
  - `PlatformUsageReportService` 由 DI 自动注入 `HomeworkAssignmentFormViewModel` 与 `BoardPage`；新增埋点照着接，
    `Record` / `RecordAppLaunch` 的签名别改（页面直接调）。
- **DI**：`SectlStatsReporter` 注册成 `ISectlStatsReporter` 单例，在线状态、版本、使用量三个服务共用它
  （超时与限流口径只在一处定义）。`OnlineStatusService` / `PlatformUsageReportService` 各自「先单例再
  `AddHostedService(sp => sp.GetRequiredService<…>())`」，页面读缓存才不会拿到第二个实例。
- **设置页**：`BasicSettingsPageViewModel` 的 `PrivacySettings` + `OnlineStatusOptions`
  （`OnlineStatusOption` 记录，与 `LanguageOption` 同一种做法），下拉写回
  `Config.General.PrivacySettings.OnlineStatusMode`；文案键 `Settings_Section_Privacy`、
  `Settings_Basic_OnlineStatus(_Description)`、`Settings_Basic_OnlineStatus_Full/Anonymous/Off`、
  `Settings_About_OnlineUsers`。
- 纯逻辑（事件形状与字段名、类型判定、批量请求体与 `207` 解析、设备类型推断、隐私三态、增量取用与放回）
  都有单测：`Corkboard.Core.Tests/StatsReportEventTests.cs` 与 `Corkboard.Core.Tests/OnlineStatsTests.cs`。
