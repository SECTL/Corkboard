# 第三方组件与来源说明

## 许可

Corkboard 以 GNU GPLv3 发布（见 [LICENSE](../LICENSE)），再发布的衍生作品必须保持同一许可。

## 直接依赖

| 组件 | 版本 | 许可 | 用途 |
| --- | --- | --- | --- |
| Avalonia | 12.1.1 | MIT | 跨平台 UI 框架 |
| Avalonia.Desktop | 12.1.1 | MIT | 桌面后端 |
| FluentAvaloniaUI | 3.0.2 | MIT | Fluent 风格控件与窗口外壳 |
| CommunityToolkit.Mvvm | 8.4.2 | MIT | `ObservableObject` / `[ObservableProperty]` / `[RelayCommand]` |
| DynamicData | 9.4.33 | MIT | 集合批量操作扩展 |
| AvaloniaRichEditor | 1.2.1 | MIT | 作业内容的所见即所得富文本编辑器（由 `Corkboard.Core/Controls/RichTextBlock` 承载） |
| HtmlAgilityPack | 1.12.4 | MIT | HTML 片段解析与默认样式注入（`BoardHtmlStyling`、旧作业迁移） |
| Markdig | 1.1.2 | BSD-2-Clause | 只在旧作业内容迁移时解析 Markdown（`LegacyBoardContentConverter`，迁移路径删掉后可移除） |
| Microsoft.Extensions.Hosting / DependencyInjection / Http / Logging | 10.0.x | MIT | 宿主、DI、HTTP 客户端、日志 |
| ClassIsland.Shared.IPC / dotnetCampus.Ipc | 2.1.0.1 / 2.0.0-alpha410 | MIT | ClassIsland 进程间通知 |
| xunit.v3 / xunit.runner.visualstudio / Microsoft.NET.Test.Sdk | 3.2.2 / 3.1.5 / 18.8.1 | Apache-2.0 / MIT | 测试 |

## 随包分发的资源

| 资源 | 来源 | 许可 |
| --- | --- | --- |
| `Corkboard/Assets/Fonts/MiSans/MiSans-*.ttf`（Thin / ExtraLight / Light / Normal / Regular / Medium / Demibold / Semibold / Bold / Heavy） | MiSans，小米科技有限责任公司（Xiaomi HyperOS 系统字体） | 《MiSans 字体知识产权许可协议》：免费商用；允许嵌入软件分发，但必须注明使用了 MiSans 字体，且不得对字体改编或二次开发、不得单独再分发或售卖字体文件 |
| `Corkboard/Assets/Fonts/FluentSystemIcons-Resizable.ttf` | Microsoft Fluent System Icons | MIT |
| `Corkboard/Assets/FluentSystemIcons-Resizable.json` | 同上（图标名 → 码位映射，供将来的源生成器使用） | MIT |

图标字体与图标映射随包分发，用途仅限界面图标。

应用界面字体是随包分发的 MiSans（10 个字重，合计约 78 MB），默认值见
`GlobalConstants.DefaultFontFamily`；用户可在「设置 → 个性化 → 外观」里换成系统字体。
许可原文见 <https://hyperos.mi.com/font/zh/download/> 页面上的《MiSans 字体知识产权许可协议》。
注意：该协议不允许改编字体，也不允许把字体文件单独再分发或售卖，所以
`Corkboard/Assets/Fonts/MiSans/` 下的 TTF 只能作为本应用的一部分一起分发，不要单独对外提供下载。
