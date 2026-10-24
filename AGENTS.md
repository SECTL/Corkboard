# PROJECT KNOWLEDGE BASE

<!--
维护契约：
- 保持这份文件“耐久”：写稳定约定，不写某台机器上的临时观察。
- 改动项目结构、目标框架、本地化布局、工作流、导航/DI 规则或模块边界后，
  必须在同一次改动里更新本文件（以及受影响的嵌套 AGENTS.md）。
- 根目录规则对整个仓库生效；子目录 AGENTS.md 补充子树规则。
- 约定冲突时，`docs/project_rules.md` 是最终依据。
-->

**Last Update:** 2026-10-25（**修订**：**「作业板」设置组从八页重组为六页**——常规（名称／排布／拖动排序／区块宽度（只在「区块」排布时显示）／界面入口（原「顶部按钮」六项 + 右下角布置作业浮标合成一张）／删除前确认）→ 内容与样式（settings.board.content：内容默认样式、五个字号、快速颜色，外加搬进来的四张手写卡）→ 科目与分类（settings.board.sections：科目、二级分类，外加搬进来的快捷标签列表）→ 自动化与数据（settings.board.automation：清理、历史与回放、数据维护）→ 窗口与收纳（settings.board.window：从基础设置搬来的主窗口不透明度／置底到桌面／窗口圆角／长按拖动窗口／点击穿透，外加四张收纳卡）→ 导出；原来那五页（手写／快捷标签／清理／历史与回放／收纳）的 .axaml 删掉、**ViewModel 全部保留当子 VM**（父页 VM 注入它们，Handwriting.／Tags.／Cleanup.／History.／Dock. 前缀绑定）；手写那张「默认颜色」原来错用 Settings_Board_DefaultColor（与内容默认颜色撞键），改用 Settings_Board_Handwriting_DefaultColor；**导出删掉 PDF、换成 xlsx 表格**——ExportFormat 的 Pdf = 1 改名成 Table = 1（数值不变，老配置里写 1 的读出来就是表格），PdfOptions／PdfLayout／PdfDocumentWriter／BoardPdfRenderSession／ExportPreviewWindow 整体删除，换成 Core 里的 ExportTable + HomeworkTableBuilder + XlsxTableWriter（手写 OOXML、零新增依赖）与 ExportSortMode／ExportNoteSorter／RichTextPlainText；ExportSettingsConfig 的 pdf_* 全换成 table_group_by_subject／table_include_cleaned／table_sort_mode；**窗口/任务栏图标改成「PNG 母图 + 运行期拼多尺寸 ICO」**——`Window.Icon` 交给 `AppIcon.LoadWindowIcon()`（`Corkboard.Core/Imaging/IconRaster` 面积平均缩帧 + `IcoFile` 拼 ICO），任务栏不再把 1254 像素母图硬缩（实测 `WM_GETICON` 从 836×836 变成 36×36 / 24×24，144 DPI 1:1 不缩放，边缘锯齿消失）；「编辑看板」窗口补上同一个图标；自动清理的默认时刻从 4:00 改成每天 8:00（`HomeworkCleanupDefaults.DefaultHour`；已有配置里明确写过的时刻不动）；在线状态与使用量统计（`OnlineStatusService` / `PlatformUsageReportService`、隐私三态、`data/config/usage-stats.json`、关于页在线人数）；导出对话框放大、范围固定全部作业、PDF 加续标题与列数自动加档、导出时隐藏卡片按钮与拖动把手、复制图片改走 Windows CF_DIB；**导出支持图片与 PDF**——标题栏导出按钮先弹自绘对话框选格式与范围（全部/按科目/勾选），PDF 真分页排版（A4/A5/Letter、列数、边距、排序、科目另起页、页眉页脚）+ 书签目录、可先预览、图片可复制到剪贴板，文件名只留 `yyyy-MM-dd_HH-mm-ss`；新增「设置 → 作业板 → 导出」设置页（`ExportSettingsConfig`）；窗口圆角优先走 DWM 系统圆角（分层窗口与置底子窗口才退回区域裁剪，边缘不再是 1 位掩码的锯齿）；导出图片左右各留 24 DIP 边距；设置新增「拖动排序」开关（默认开，关掉后科目区块上不出现拖动按钮）；科目表默认不再预置语文/数学/英语；手写卡片预览把**笔宽**按「渲染宽度 ÷ 参考宽度」跟着整页一起缩放（`HandwritingDocument.ResolveRenderScale` / `HandwritingRenderer.Render`，编辑画布算出来是 1 所以不受影响），修「写的时候字距正常、卡片上笔迹挤成一团」——笔位缩了而笔宽没缩，笔画相对字就粗一倍；清空作业板改成只把作业从当前页面收起来（数据留在原文件、时空回放里点「还原到此刻」能整份放回来；历史按日追加到 `data/history/board/`）、基础设置新增「窗口圆角」开关（Windows 用 `SetWindowRgn` 裁区域、置底也生效）、主窗口四边缩放的命中判定统一改成窗口坐标（修「置底到桌面后四周拉不动」）、作业卡片点出来的编辑/删除按钮最多停 30 秒自动收回、布置作业表单取消整卡拖动、主窗口四边（含顶部与四角）都能缩放、手写支持鼠标与触控、快捷标签改 `Note.Tags` 胶囊显示、导出图片按自然高度渲染；**作业板新增八项功能**：手写、二级分类、快捷标签、界面字号、收纳浮钮、清空作业板（只收起来、回放里能还原）、导出图片、按历史日志重放的时空回放；设置侧边栏把「作业板」收成一个可展开分组（七个子页，含「历史与回放」），基础设置不再放任何作业板专属项；**作业板域代码按四类归目录**：`Corkboard.Core/Notes`（便签通用）、`RichText`（富文本通用）、`Board`（板子框架）、`Homework`（作业专属），App 层视图/VM/服务同步分目录；`BoardNote`→`Note`、`BoardCleanup*`→`HomeworkCleanup*`、`BoardAssignmentForm`→`HomeworkAssignmentForm`、Legacy 三件套→`LegacyNote*`；PageInfo、JSON 键、资源键、数据路径与云端文件名全部未变；主界面去掉侧边导航栏，只承载默认主页面；设置界面保留导航栏、顶部「收纳」与返回按钮（返回箭头改由 Core 样式模板画），设置项改为平铺、不再分组；设置页改设置卡；应用字体换成随包分发的 MiSans；作业板：**取消作业名与完成勾选**、**同科目合并成一个区块**、作业内容多行可拖宽拖高（布置作业卡片有最小宽度／最大宽度／最大高度，上限按**看得见的宿主**算：壳内弹层取窗口内容区、只占其 70%，置底形态退回屏幕工作区，超了卡片内部滚动）、**弹层卡片整张都是拖动面**（按住卡片上输入控件以外的地方都能整张拖走，不再有抓手图标；位移夹在宿主里，卡片尺寸一变就重夹，置底形态下改拖独立窗口）、自绘页面级弹层、纯文字便签、三种排布（**排布切换在主页面顶部已取消，改到「设置 → 作业板」下拉框里选**；成排布局里每个区块各自定高，不跟着同行最高的那块变长）、排序与删除确认、名称/科目可配（**作业类型与字段模板已删除**：便签上只有内容、科目、截止日期三样，旧数据里的 `types`/`type_id`/`values` 静默忽略）；**作业内容是所见即所得富文本**（第三方 `AvaloniaRichEditor`，由 Core 的 `RichTextBlock` 承载，落盘成 HTML 片段；旧的 Markdown 渲染管线与相关单测已整体删除，旧作业由 `LegacyNoteContentConverter` 一次性迁移），可以给选中的字/段单独改颜色与字号，默认字号与默认颜色在「设置 → 作业板 → 内容默认样式」里配；**格式工具栏常驻在编辑区正上方**（库自带的 `RichEditorToolbar`，`Target` 在 code-behind 里指向编辑控件，文案由 Core 的 `RichTextLocalization` 跟着界面语言切），加粗/斜体/下划线/删除线、文字颜色、高亮、列表、缩进、行距、对齐、字号/字体/段落样式与插入表格图片都在上面，点一下直接对当前选区生效；**作业内容只有一个框**：就是一个始终可编辑的富文本编辑器，没有「原文态/效果态」切换；**作业数据移出 `data/config/`**，按创建日期归档到 `data/board/<年>/<月>/<日>/notes.json`；**时空回放**放在主界面标题栏右上角，连点两次才进入、超时自动退回，没作业时不显示入口；Debug 构建在壳的左下角加版本水印，照上游 SecRandom-C 的 `DevelopmentBuildAdorner`；外观设置补齐主题模式（跟随系统 / 浅色 / 深色）与主题色（跟随系统 / 自定义）；**基础设置新增主窗口不透明度（滑杆，落到原生窗口）与点击穿透**，主界面标题栏右上角「布置作业」左侧加设置按钮（打开设置窗口）；**基础设置新增「右下角布置作业按钮」**，打开后主窗口右下角浮一颗圆角加号（`MainView` 里叠在内容之上的一颗按钮，弹层遮罩盖得住它，回放/点击穿透时随其它入口一起隐藏，点一下开出布置作业表单）；**设置页「关于」页复刻上游 SecRandom-C**，改应用信息／作者／版本信息三张卡，外部链接经新服务 `IExternalLauncher`（`Corkboard/Services/Desktop/`）交给系统默认程序打开）；**作业板新增「自动清理过期作业」**（设置 → 作业板：开关（默认开启）+ 每天/每周某天某时刻，默认 8:00，用 `TimePicker`），到点只给**过了截止日期**的作业打上 `CleanedAt` 标记、从主页面拿掉（数据原地不动、不搬目录、不删文件，回放里照旧看得到），**判定只看截止日期**（没填截止日期的作业永远不会被自动清掉，到了截止日期当天就算过期）；卡头的「立即清理」不看时刻、点一下当场把到了截止日期的清掉；**作业新增可选「截止日期」**（`Note.DueDate`，布置作业表单里用 Avalonia 自带的 `CalendarDatePicker`），卡片上按「今天 / 明天 / yyyy-MM-dd / 过期 N 天」显示，过期标红）；**设置新增「账号」页**（登录 SECTL 组织账号：OAuth 2.0 PKCE 跳系统浏览器 + loopback 回调，头像缺失时显示昵称首字（`Corkboard.Core.Helpers.AvatarInitialResolver`），凭据失效提示「重新登录」，退出先弹确认框再清本机凭据，页面订阅 `ISectlAuthService.StateChanged` 并在 `Unloaded` 里 `Detach`）；**设置新增「云同步」页**（把作业板与设置各备份成**一个文件**传到思拓创联云存储：`corkboard-board.zip.b64`（压缩包的 base64 文本，内带 `manifest.json`）与 `corkboard-settings.json`；刻意**只做备份 + 恢复、不做双向合并**——作业板改动（布置 / 修改 / 删除作业）后 5s 防抖自动上传、设置改动后 45s，两次改动挨在一起时按更早的那个截止时间算；**本机自上次同步以来没有改动时才自动拉取**，本机有改动就只提示「云端有更新的备份」由用户点恢复；默认关闭、登录后才能开；窗口大小位置与开机自启这类本机专属项按前缀排除（`CloudSyncExclusions`），设置改动是否算「本机有改动」看内容摘要（`LastSettingsHash`）而不是文件时间，否则拖一下窗口就会永远不敢自动拉；页面上显示云端空间剩余与本平台占用，界面文案里的组织名统一用「思拓创联」；云上传的 multipart 字段名要带引号（.NET 的 `ContentDispositionHeaderValue` 默认把 `name`/`filename` 写成不带引号的形式、还会多带 `filename*=utf-8''`，服务端会回 `400 invalid_request / No file data received`）；**云存储会把上传的字节按 UTF-8 解码一遍**（非法字节变 U+FFFD：实测 256 字节的二进制传上去服务端存成 512 字节、下载回来 sha256 不一致），所以作业板压缩包先 base64 成纯 ASCII 再传（`CloudBackupPayload`，云端名 `corkboard-board.zip.b64`），恢复时解回字节再解压；纯文本的设置文件不受影响；云端同名旧件一次全删、恢复时按 `updated_at` 取最新的一份（曾因留下同名旧件而取到旧数据）；设置窗口最小尺寸抬到 1000x720（`MainWindow.SettingsWindowMinWidth/MinHeight`，屏幕工作区比这还小时按屏幕收，否则设置卡会被挤成一条）；**作业板备份已改成分片上传**（`CloudBackupPackage`：压缩包按 3 MiB 切片、每片单独传，最后传一份 `corkboard-board-<备份标识>-manifest.json`，清单里记着每片的序号/长度/SHA-256 与整包长度/摘要；恢复只认「清单 + 清单里声明的全部分片」这一种完整形态，缺片就提示重新备份，拼完还要再对一次整包 SHA-256；每次上传成功后按清单名删掉上一代分片与早年的整包文件，中途断掉的上传只留下没人认领的分片；上传前拿用量接口的剩余空间做保底 `EnsureQuotaForUpload`，不够就直接回「云端空间不够了」；**这条链路上真正卡请求体大小的是 Appwrite `functions/executions` 的 `body` 参数上限 10 MiB，写死在 app 代码里（`/usr/src/code/src/Appwrite/Platform/Modules/Functions/Http/Executions/Create.php:85` 的 `new Text(10485760, 0)`），环境变量改不动；api-proxy 的 `MAX_BODY_BYTES` 已从 5 MiB 对齐到 10 MiB**（服务器 `/opt/api-proxy/server.js:13`，超了回干净的 413 `payload_too_large`，客户端自己按 6 MiB 预算、3 MiB 一片留余量）；**云同步页多了「清空云端」**（在「备份与恢复」卡片里，`FAContentDialog` 确认后走 `ICloudSyncService.ClearCloudAsync`／`CloudSyncService.ClearCloudAsync`：只删自己这一套文件名（`CloudBackupFiles.IsManagedFile`／`AllManagedFiles`——平台上别人的文件不碰，中断留下的孤儿分片一起清），删完清簿记（`LastSyncedAt`／`LastSettingsHash` 归零、远端快照作废）并重查用量，所以界面回到「还没同步过」、占用归零、按钮自己变灰；删之前先掐掉排着的自动上传，否则几秒后那份备份又自己传回去了；个别文件删不掉会记警告并报错让用户再点一次，不假装全清干净了）；**云端被别的设备改动或清空，本机自己会发现**（云同步页每 60 秒后台查一遍用量与文件清单：后台查询不进「同步中」态、失败只记日志不写错误提示，否则每分钟闪一次禁用态和一条红字；发现云端版本比上次同步新、且本机自上次同步没有改动时按 `CloudSyncPolicy.ShouldAutoPull` 自动拉取，**同一个云端版本只试一次**（`_autoPulledRemote`）——原先的 `_autoPullChecked` 是「一次进程只自动拉一次」，那样轮询永远不会拉；发现本机记着同步成功过、云端却一份备份都不剩（`CloudSyncPolicy.WasClearedRemotely`）就把本机簿记一起清掉，并在页面上提示「云端的备份已经没了，点「立即备份」重新传一份」）；**「上次同步」时间由服务现读配置**（`ICloudSyncService.LastSyncedAt`）：从云端恢复设置会把配置对象整个换掉（`ConfigHandlerBase.Reload`），页面 ViewModel 手里那份旧引用停在恢复前的时间上，会出现「云端备份已经没了」和「上次同步 12:15」同时挂着）；**两边都改过时按并集融合**（`Corkboard.Core/Homework/HomeworkNoteMerge.cs`：同 `Note.Id` 取 `UpdatedAt` 更新的那份整条替换；**Id 不同、但「科目 + 截止日期 + 内容 + 创建那天」都一样**的也当成同一条、只留更新的那份——作业本来就没有名字，两边各建过一次同一份作业时不能留成两张一模一样的卡片，条数记在 `RemovedDuplicates` 里；同一侧自己带重复 Id（历史数据里一个 Id 落在两个日期文件里）也只留更新的一份；合完**就地写回**本机日期文件（`NoteStore.SaveAll` + `CopyBoardFiles(staging, BoardDirectory, overwrite: false)`），**不能整体换目录**——那样会把不在并集管辖范围里的 `board.json`（科目表与手动排序）一起抹掉，`Reload` 随即退回默认科目；最后把并集传回云端收敛，日志带「云端补进来／更新／合并重复」条数）；**「只同步最近多久的作业」**（`cloud.sync_days`，`0` = 全部，下拉预设 全部／30／7／1 天，配置里若是别的天数就按大小插进列表、免得界面显示的和服务实际用的不一致；`CloudSyncPolicy.UploadCutoff`）：上传时 `PrepareBoardUploadDirectory` 只把窗口内（按 `CreatedAt`）的作业写进临时目录再打包，窗口外的老作业只留本机、不传也不删；恢复时 `RestoreBoardWithinWindow` 先把窗口外的本机作业和云端那份并集再写回，**不能整体替换**，否则窗口外的历史会被抹掉）；**考试看板（与作业板并列的第二块板子）**：设置侧边栏「作业板」分组下方新增「考试看板」分组（考试安排 / 看板显示 / 时间同步三页），一块看板 = 一个文件（`data/exam/boards/<Id>.json` + `data/exam/boards.json` 索引，自带标题 / 副标题 / 副标题颜色 / 考试安排，老的单份 `exams.json` 与 settings 里的全局标题一次性迁进第一块看板），看板编辑走独立窗口 `ExamBoardEditorWindow`（设置页只当看板列表：编辑 / 设为当前 / 删除），全屏看板 `ExamBoardWindow` 三档状态（未开始 / 即将开始 / 进行中）按可配阈值判定、左下角长按 2 秒退出（图标进度圈蓝→红），NTP 校时（`Core/Time/`，默认 8 个国内源，后台每 30 分钟同步一次，`NtpTimeService.NowLocal` 是应用内唯一时间来源））

## 这是什么

Corkboard 是 GPLv3 的 C#/.NET 桌面应用：一块桌面作业板（便签）。业务规格见 `docs/board.md`，
工程规则见 `docs/project_rules.md`（checkout 未提供，规则以本文件为准）。
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
│   └── Services/               #   Ui/、Board/、Homework/、Time/、Cloud/、Auth/、Desktop/、Ipc/
├── Corkboard.Core/             # 核心：契约、配置/日志、领域服务、可复用控件与样式、Langs/Common
│   ├── Notes/                  #   Note、NoteStore、NoteDayFile、INoteStore、Legacy/（旧数据迁移）
│   ├── RichText/               #   RichTextStyle/Palette/HtmlStyling/ContentKind
│   ├── Board/                  #   板子框架：BoardService、NoteGrouping、排布排序枚举、回放
│   ├── Homework/               #   作业专属：HomeworkConfig、SectionCatalog、截止日期、清理、合并
│   ├── Exam/                   #   考试专属：ExamEntry、三档状态判定、时间格式化、ExamService
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
├── docs/                       # 工程规则/规格/待办（checkout 未提供）
├── Global.props                # 共享 MSBuild 策略（各工程 Import）
├── Directory.Build.props       # AvaloniaVersion 锚点
└── Platforms.props             # CorkboardPlatform 选择 + CORKBOARD_PLATFORM_* 宏
```

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
- **中英两套文案必须一一对齐**：`Corkboard.Core.Tests/LocalizationTests.cs` 会比对两份 resx 的键集合、
  拒绝空文案，缺一条就红；加键时中文、英文、`Resources.Designer.cs` 三处要一起加。
- **简介要像人说的话，不是标签**：一句话说清「这一项做什么、改了会怎样」，中文一条 10–21 个字
  （英文同量级的一句话），口语、具体、一条一件事。**别压成两三个词的标签**（「被别的窗口盖住」
  「跟着窗口变宽」这种读起来像字段名，用户看不懂），也别写成并列堆叠的功能清单；长短语由
  `FASettingsExpander` 的 `Description` 一行承载，放不下就先砍修饰、不要再加第二句。
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
  此时 `PageOverlayService.UseSeparateWindow` 为真，表单由 `PageOverlayWindow`（无边框、不进任务栏）承载。
  ⚠️ **这个窗口要「透明窗 + 圆角实体底座」**：`TransparencyLevelHint="Transparent"` + `Background="Transparent"`，
  否则无边框窗口的方形底会在卡片四角露出**黑色角**（用户反馈过）；同时**不能**让内容变成半透明——
  独立窗口背后没有页面可叠、桌面会透上来，所以窗口内容外面必须自己叠一层
  `Border`：`SolidBackgroundFillColorQuarternaryBrush`（实体色，不是 Fluent 那几个半透明 CardFill）+
  `CornerRadius="8"` + `ClipToBounds="True"`，不画描边（里面的 `Border.sheet` 自带）。标志只在**置底实际生效**时置真，并要结合请求方向判断（`Apply` 在关闭时也会把
  `DesktopBottom` 报成已应用）。详见 `docs/board.md`。
  ⚠️ **弹层与整窗拖动会打架**：壳的 `HoldToDragWindow` 长按会捕获指针，拖动结束**必须显式
  `Capture(null)`**——指针在窗口外抬起时收不到 `PointerReleased`，捕获留在壳上会让整窗点不动、
  输入框无法聚焦（表现就是「输入框打不了字」）；并且弹层打开时不参与长按拖动。
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
- **布置作业表单的卡片不能拖动**（`HomeworkAssignmentForm`）：位置由弹层布局居中决定，标题行不要放拖动光标、
  也不要「拖动移动卡片」提示；能调的只有右下角缩放手柄（尺寸上限见下一条）。
  曾经做过「整卡拖动」（`SheetDragLimits` + `RenderTransform` 位移、置底形态改拖 `PageOverlayWindow`），
  用户明确要求删掉：标题行与留白太容易误拖，触屏更甚。`SheetDragLimits` 纯逻辑与单测留在
  `Corkboard.Core/Controls/` 不动，但表单不再用它。
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
  （`groupId = "settings.exam.group"`，`FluentIcons.CalendarFilled`），子页是考试安排／看板显示／时间同步三页。
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
- **主界面标题栏右上角承载主页面动作**：「设置」「布置作业」与「时空回放」都在这里。
  「设置」是主窗口里唯一的设置入口（`App.ShowSettingsWindow()`，另一个入口在托盘菜单），
  必须在「布置作业」左边、标 `ElementRole="User"`。
  作业板页面上只剩列表与一句回放提示；**「导出」按钮在标题栏**（`MainView` 里、回放左边，图标 `SaveFilled`），
  点击走 `MainView.ExportImageButton_OnClick` → `(NavigationFrame.Content as BoardPage).ExportAsync()`
  （导出仍由页面做：只有它拿得到内容容器 `BoardContent` 与 `BoardNoteTemplate`；`BoardExportService` 导的是板上的作业内容、不含桌面背景）；
  排布切换仍在设置里（「设置 → 作业板 → 常规 → 排布」下拉框，写回 `BoardSettingsConfig.LayoutMode`）。
  标题栏按下即拖动，所以这些按钮一律要标 `WindowDecorationProperties.ElementRole="User"`，
  并进 `MainView.IsInteractiveSource` 的排除表（`Button` / `ToggleButton` / `TextBox` / `ComboBox` / `Slider`），
  漏一个那个控件就点不动。主窗口是 `WindowDecorations.None`，没有系统按钮，所以右侧可以放东西
  （设置窗口相反，见下）。
- **主窗口不透明度、点击穿透与圆角都是窗口能力**（「设置 → 作业板 → 窗口与收纳」里的卡片）：
  `BasicSettingsConfig.MainWindowOpacity`（0–1 的标量）、`ClickThrough` 与
  `MainWindowRoundedCorners`（开关）由 `MainWindow` 在 `Opened` 与应用中把请求转给
  `IWindowFeatureService`（不透明度走 `WindowFeatures.WindowOpacity` +
  `WindowFeatureRequest.Opacity` 这个标量参数；圆角走 `WindowFeatures.RoundedCorners`，
  设置页靠 `BoardWindowSettingsPageViewModel.IsWindowCornersSupported` 决定显隐）。
  - 平台不支持的能力**整张卡隐藏**（`SupportedFeatures` 里没有对应位）：Linux 两样都没有，
    macOS 与 Windows 两样都有；圆角只有 Windows 报支持（macOS 窗口自带圆角，Linux 不报）。
  - ⚠️ **圆角优先走 DWM，走不通才裁区域**：普通顶层窗口交给
    `DWMWA_CORNER_PREFERENCE`（`DwmSetWindowAttribute`）画系统圆角，边缘有抗锯齿、弧度平滑；
    但分层窗口（开了不透明度 / 点击穿透时共用的 `WS_EX_LAYERED`）DWM 不给圆角，
    置底到桌面后窗口又成了桌面子窗口，这两种情况退回 `CreateRoundRectRgn` + `SetWindowRgn`
    （`WindowsWindowFeatureService.TrySetRoundedCorners`，半径 12 DIP 按 `GetDpiForWindow` 换算，
    `enabled == false` 时两样都退：DWM 偏好交还默认 + `SetWindowRgn(hwnd, 0, true)`）。
    区域那条路是 1 位掩码、边缘必然带锯齿，所以别为了省事把它当唯一实现。
    `SetWindowRgn` 成功后那块区域**归系统所有**，不要再 `DeleteObject`，只有调用失败才回收。
    ⚠️ 尺寸、DPI、置底挂载每变一次都要重贴一次（DWM 会自己跟着，区域不会），否则圆角按旧尺寸裁、
    右下角会切到内容或露直角：`MainWindow.ApplyWindowCorners()` 在 `OnOpened`、`OnBasicSettingsChanged`、
    `ApplyPinToDesktop` 末尾、`ApplyClickThrough` / `ApplyWindowOpacity` 之后、`OnWindowPropertyChanged`
    的 `BoundsProperty` 分支、`OnWindowScalingChanged` 里都要调——分层状态一变就必须重算走哪条路。
  - Windows 上两样共用 `WS_EX_LAYERED`：**只设样式不调 `SetLayeredWindowAttributes`，
    窗口会整体不显示**，所以 `TrySetExtendedStyles` 与 `TrySetWindowOpacity` 都要按同一份
    alpha 补一次分层属性；不透明度下限在平台侧兜底（设置页滑杆下限更高，20%）。
  - 点击穿透开着时主窗口一个控件都点不动，所以**凡是「点了才生效」的入口都要一起隐藏**，
    不留点不动的控件误导用户：
    - 标题栏右上角整块隐藏（`MainView` 右侧动作区 `IsVisible` 绑
      `!ViewModel.Config.Basic.ClickThrough`）；设置只能从托盘菜单打开。
    - 作业条右侧的编辑/删除按钮跟着隐藏（`BoardPageViewModel.CanModifyNotes`
      = `!_replay.IsActive && !Config.Basic.ClickThrough`，经 `BoardNoteCardItem.CanModify` 绑到按钮的
      `IsVisible`；`ClickThrough` 变化要 `RebuildNotes()`）。回放期间同理。
    - 右下角布置作业浮标也要隐藏（`MainViewModel.ShowCornerAssignButton`
      = `Config.Basic.ShowCornerAssignButton && !Config.Basic.ClickThrough`），**别直接绑配置里的
      `ShowCornerAssignButton`**。
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
- **设置页用分区标题分块**：一页塞不下时不要新开导航页，用 `<TextBlock Classes="section-title" />`
  把设置按用途分成几段（样式见 `Core/Styles/StylesBase.axaml`）。页内首组不加分区标题；
  分区标题文字加在 resx 的 `Settings_Section_*`。
- ⚠️ **设置界面选中导航项前必须保证容器已实例化**（否则窗口卡死）：初始选中推迟到首次布局之后，
  分组内的页面先用 `ExpandGroupsContaining` 展开祖先分组并走一次 `UpdateLayout()`。
  原因见 `docs/project_rules.md` 第 3 节。

## 应用图标（窗口 / 任务栏）

- **母图只有一份 PNG**：`Corkboard/Assets/Icons/Corkboard.png`（1254×1254，随包分发）。应用内绘制用它
  （`AppIcon.LoadBitmap()`）；**不要再把它直接交给 `Window.Icon`**。
- **会出现在任务栏的窗口，图标一律走 `AppIcon.LoadWindowIcon()`**（`Corkboard/Services/Desktop/AppIcon.cs`，
  结果缓存）：它把母图在内存里按面积平均缩成 `IcoFile.DefaultFrameSizes`
  （16/20/24/28/30/32/36/40/42/48/54/60/64/72/96/128）里每一档，拼成一份真 ICO 再赋给 `Window.Icon`，
  窗口构造时设一次即可（现在有 `MainWindow` 与 `ExamBoardEditorWindow`）。
  `ShowInTaskbar="False"` 的窗口（`ExamBoardWindow`、`BoardDockWindow`、`PageOverlayWindow`、
  `TrayMenuWindow`、`ClickThroughLockWindow`）不用设。
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

## 配置管道与自动保存

三类：`ConfigBase`（模型基类，`Corkboard.Shared`）、`ConfigServiceBase`（读写实现）、
`ConfigHandlerBase<T>`（自动保存句柄）。落盘路径 `data/config/settings.json`。

- **自动保存覆盖到子配置**：`ConfigHandlerBase` 递归订阅根对象与任意深度的 `INotifyPropertyChanged`
  子对象，所以 `General.Basic.*` 这类改动会自动写盘，加子配置不需要额外接线。
- **但集合只订阅集合本身，不订阅集合里的元素**：`ObservableCollection<T>` 的增删会自动落盘，
  改元素字段不会。需要可编辑元素时自己在 VM 里订阅元素属性并显式 `MainConfigHandler.Save()`
  （范例见 `BoardSettingsPageViewModel.SaveAll`）。
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

## 考试看板与 NTP 校时

考试看板是**与作业板并列的第二块板子**，设置侧边栏里排在「作业板」分组正下方
（`groupId = "settings.exam.group"`，子页：考试安排 / 看板显示 / 时间同步）。

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
- **左下角长按 2 秒退出**：进度圈 + 蓝→红渐变全在 ViewModel 里（`BeginHold` / `CancelHold` / `HoldSweepAngle` / `HoldRingBrush`），
  窗口 code-behind 只负责捕获指针。⚠️ **窗口里不要自己写 `InitializeComponent()`**——那会顶掉 XAML 生成的同名方法，
  `x:Name` 字段恒为 null，按住一下就是空引用崩溃（这条踩过两次，`TrayMenuWindow` 一次、这里一次）。

### NTP 校时

- 核心在 `Corkboard.Core/Time/`：`NtpPacket` / `NtpTimestamp` / `NtpClient`（UDP 123，异常一律转 `NtpSyncResult.Failure` 不抛）/
  `NtpServerCatalog`（默认 8 个国内源：阿里云 ×2、腾讯云 ×2、`cn.pool.ntp.org`、`cn.ntp.org.cn`、交大、`time.edu.cn`）/ `NtpTimeService`。
- **应用内唯一时间来源是 `NtpTimeService.NowLocal` / `UtcNow`**（= 系统时钟 + 偏差）；关掉同步就把偏差归零退回系统时钟。
  考试看板与「考试安排」页都用它，不要各自 `DateTime.Now` 了事。
- 后台由 `Corkboard/Services/Time/NtpSyncHostedService`（`BackgroundService`）每 30 秒醒一次，按 `SyncIntervalMinutes`（默认 30）决定要不要真同步；
  失败**保留上次偏差**，不因为一次超时就把时钟跳回系统时间。
## 平台统计（在线状态 / 使用量）

对齐上游 SecRandom-C 的三个统计接口（与账号、云存储同一个 api-proxy，端点集中在
`Corkboard/Services/Auth/SectlAuthEndpoints.cs`）：`POST /api/stats/online`（在线状态上报，响应带
`online_count`）、`GET /api/stats/platform/{platformId}`（在线人数）、`POST /api/stats/usage/increment`（使用量增量）。

- **平台 ID 只有一个来源**：`Corkboard.Core/GlobalConstants.cs` 的 `PlatformId`
  （`6ac3d35400241eff9fbe`）。它就是 SECTL 平台记录的主键，**同时是 OAuth 客户端 ID**：
  `SectlAuthEndpoints.ClientId` 直接取这个常量，不要写第二份字面量；上游 SecRandom 也是
  ClientId == PlatformId。版本上报、在线状态、使用量都从这里取值。
  ⚠️ 原来的 `platform_29648fc4ac3ba07d` 是错的（`GET /api/stats/platform/...` 直接 400），
  已废弃；统计只认 `6ac3d35400241eff9fbe`。
- **在线状态**（`Corkboard/Services/Stats/OnlineStatusService.cs`，`BackgroundService`，注册成单例再挂托管）：
  载荷 `OnlineStatusPayload`（Core）只含 `platform_id` / `device_uuid`（`IDeviceIdProvider`，
  落在 `data/config/sectl-device.json`）/ `device_type`。
  ⚠️ `device_type` 是**服务端词表**：`windows-desktop` / `macos-desktop` / `linux-desktop` /
  `android` / `ios` / `unknown-desktop`，**不带架构**（写成 `windows-x64` 会 400，整条上报不生效）。
  每 2 分钟上报一次、10 秒超时，失败只记 debug；隐私设置是 `OnlineStatusMode.Off` 时**完全不动网络**。
  位置由服务端按访问地址统计，客户端**不发** IP/城市，也**从不**带 SECTL 账号 ID。
  - 响应有两种形态：上报（`POST /api/stats/online`）给顶层 `online_count`；查询
    （`GET /api/stats/platform/{id}`）把人数放在 `online.online_count` 里。
    `OnlineStatsResult.ResolveOnlineCount()` 两种都认，不要只读顶层的。
  - 在线人数缓存在服务里（`OnlineCount` / `HasOnlineCount`），「关于」页的「在线人数」行显示；
    `OnlineCountChanged` 事件驱动刷新，页面 `Unloaded` 里 `Detach()` 解开（页面是 transient 的）。
  - 隐私设置 `PrivacySettingsConfig.OnlineStatusMode` 挂在 `general.privacy`（默认 `Full`），
    界面在「基础设置 → 隐私」分区（正常上报 / 匿名上报 / 停止上报）。读盘后由
    `MainConfigModel.NormalizeLoaded` → `PrivacySettings.Normalize()` 夹取；从云端恢复设置会整个换掉配置对象，
    服务订阅 `MainConfigHandler.Reloaded`，在关闭上报时把缓存一并清掉（不留过期的在线人数）。
- **使用量**（`Corkboard/Services/Stats/PlatformUsageReportService.cs`，`BackgroundService`，同样单例）：
  载荷 `UsageIncrementPayload`（`platform_id` / `field_key` / `delta`；**JSON 名是 `field_key`，
  不是 `field`**），指标名见 `UsageMetricNames`：`daily_app_launch_count` / `daily_note_add_count` /
  `daily_export_count`。
  ⚠️ 服务端对每个平台有「已登记字段」白名单：没登记的 `field_key` 回
  `200 {"status":"ignored","reason":"field_not_registered"}`（不报错也不计数）。Corkboard 平台目前
  **还没登记这三个键**，需要在 SECTL 侧登记后统计才真正落库；键名改了就前功尽弃。
  - 计数用 `UsageMetricCounter`（Core，线程安全）；待上报增量落盘 `data/config/usage-stats.json`
    （`Utils.GetFilePath`，原子写入）。它**不是设置项**，不要并进 `settings.json`——设置会被云同步带走/互相覆盖。
  - 记录后 2 秒防抖上报，失败把增量放回队列、30 秒后重试；进程退出时文件里还有没上报的，下次启动接着传。
  - 埋点位置：服务启动（`RecordAppLaunch`）、布置作业成功
    （`HomeworkAssignmentFormViewModel.Confirm` 的 `_boardService.Add` 之后，编辑不算）、
    导出成功（`BoardPage.ExportAsync` 的图片保存 / 复制到剪贴板 / 表格三处，取消与失败不算）。
  - `PlatformUsageReportService` 由 DI 自动注入 `HomeworkAssignmentFormViewModel` 与 `BoardPage`；新增埋点照着接。
- **设置页**：`BasicSettingsPageViewModel` 的 `PrivacySettings` + `OnlineStatusOptions`
  （`OnlineStatusOption` 记录，与 `LanguageOption` 同一种做法），下拉写回
  `Config.General.PrivacySettings.OnlineStatusMode`；文案键 `Settings_Section_Privacy`、
  `Settings_Basic_OnlineStatus(_Description)`、`Settings_Basic_OnlineStatus_Full/Anonymous/Off`、
  `Settings_About_OnlineUsers`。
- 纯逻辑（载荷校验与字段名、设备类型推断、隐私三态、增量取用与放回）都有单测：
  `Corkboard.Core.Tests/OnlineStatsTests.cs`。

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
  `BoardSettingsConfig.ShowTitleBarSettings/Sort/Assign/Export/Replay/Dock`（全部默认 `true`），
  界面只看 `MainViewModel.ShowTitleBar*` 这六个**计算属性**——配置开关只决定「允不允许显示」，
  各自还要叠原有条件：布置作业在回放中不显示（`!_replay.IsActive`）、回放入口没作业时不显示
  （`_replay.CanEnterReplay`）、收纳要求 `DockEnabled` 也开着。`MainViewModel` 因此多了个
  `BoardReplayService` 构造参数，并在 `OnReplayChanged` 里补通知。
  ⚠️ **「锁定窗口」那颗不能进这组开关**：点击穿透状态下它是唯一的解锁入口，藏掉就再也点不回来；
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
  点编辑 / 删除时先把按钮收回（`IsActionsVisible = false`）。回放与点击穿透时 `CanModify=false`，
  按钮永远不冒出来。
  ⚠️ **点出来的按钮最多停 30 秒**（`ActionsVisibleDuration`，`DispatcherTimer` 到点把
  `IsActionsVisible` 收回）：用户嫌「一直显示很难受」。悬停态不走这个计时器，
  鼠标离开就收；计时器在 `OnIsActionsVisibleChanged` 里起停，别让它自己跑着不停。
- **科目区块是一张按主题配色的卡片**（`BoardPage.axaml` 的 `Border.note`）：底色 / 描边走页面资源里的
  `ResourceDictionary.ThemeDictionaries`（`BoardNoteCardBackground` / `BoardNoteCardBorder`，浅色白卡、
  深色比页面亮一档的深灰卡），**不要**退回 `Transparent` 底 + Fluent 半透明卡刷——浅色页面是浅灰 Mica、
  深色页面接近纯黑，同一个半透明色两边都跟背景糊在一起（用户反馈「区块分区不明显」）。
  两种排布（单列 / 区块）**统一都是 1px 描边 + `CornerRadius=8` + `Padding=12`**；单列排布**不要**再覆写成「只剩一条底线」。
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
- **界面字号**：`AppearanceSettingsConfig.UiFontSize`（10–24），`App.ApplyFontSettings` 写 `AppFontSize` +
  `ControlContentThemeFontSize`，`StylesBase.axaml` 的 `:is(Window)` 与 `fa|FAContentDialog` 各加一条 `FontSize`。
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
- **清空作业板 = 把作业从当前页面收起来**：设置 → 作业板 → 清理，确认后直接 `IBoardService.ClearAll()`。
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
  窗口置底到桌面后是桌面宿主的子窗口，原生标题拖动对它不生效。标题栏按下即拖；其余位置在
  `BasicSettingsConfig.HoldToDragWindow` 打开时长按判定后拖动（**整窗任意位置**，靠移动阈值让出）。
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
  「整窗点不动」。标题行标了 `WindowDragGesture.Suppress`，壳的整窗长按拖动会让出（两者判定不再撞车）。
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
  - 菜单里第一项「显示主窗口」走 `App.ShowMainWindow()`：先看 `BoardDockService.IsDocked` 决定是否
    `Expand()`，再走 `MainWindow.ShowFromDock()` 重放置底 / 点击穿透 / 不透明度（隐藏会摘掉这些平台样式，
    光 `Window.Show()` 补不回来）。**不要**在托盘那边另写一套「显示窗口」。
  菜单文案复用 Core 的 `Menu_*` 键；平台能力看 `PlatformCapabilities.SupportsTrayIcon`。
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
