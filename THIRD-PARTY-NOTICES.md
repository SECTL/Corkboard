# 第三方组件与来源说明

## 本项目来源

Corkboard 的工程框架（分层结构、DI 装配方式、配置/日志管道、导航页面注册机制、平台能力抽象、
本地化与主题约定）移植自同作者的 **SecRandom-C**（GNU GPLv3）。
按 GPLv3 的要求，Corkboard 同样以 GPLv3 发布（见 [LICENSE](../LICENSE)），
再发布的衍生作品必须保持同一许可。

## 直接依赖

| 组件 | 版本 | 许可 | 用途 |
| --- | --- | --- | --- |
| Avalonia | 12.1.1 | MIT | 跨平台 UI 框架 |
| Avalonia.Desktop / Avalonia.Fonts.Inter | 12.1.1 | MIT | 桌面后端与默认字体 |
| FluentAvaloniaUI | 3.0.2 | MIT | Fluent 风格控件与窗口外壳 |
| CommunityToolkit.Mvvm | 8.4.2 | MIT | `ObservableObject` / `[ObservableProperty]` / `[RelayCommand]` |
| DynamicData | 9.4.33 | MIT | 集合批量操作扩展 |
| Microsoft.Extensions.Hosting / DependencyInjection / Http / Logging | 10.0.x | MIT | 宿主、DI、HTTP 客户端、日志 |
| ClassIsland.Shared.IPC / dotnetCampus.Ipc | 2.1.0.1 / 2.0.0-alpha410 | MIT | ClassIsland 进程间通知 |
| xunit.v3 / xunit.runner.visualstudio / Microsoft.NET.Test.Sdk | 3.2.2 / 3.1.5 / 18.8.1 | Apache-2.0 / MIT | 测试 |

## 随包分发的资源

| 资源 | 来源 | 许可 |
| --- | --- | --- |
| `Corkboard/Assets/Fonts/FluentSystemIcons-Resizable.ttf` | Microsoft Fluent System Icons | MIT |
| `Corkboard/Assets/FluentSystemIcons-Resizable.json` | 同上（图标名 → 码位映射，供将来的源生成器使用） | MIT |

字体与图标映射从 SecRandom-C 一并带入，用途仅限界面图标。
