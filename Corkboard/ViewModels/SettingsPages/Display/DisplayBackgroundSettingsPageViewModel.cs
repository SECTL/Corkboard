using Corkboard.Core.Services.Config;

namespace Corkboard.ViewModels.SettingsPages.Display;

/// <summary>
///     「设置 → 看板模式 → 背景」页的 ViewModel：这一页没有自己的状态，
///     编辑控件直接绑 <c>Config.Display</c> 上的背景字段，配置 handler 负责落盘。
///     <para>留成独立类型是为了与其它设置页一致（都由键控 DI 按页面取一个 transient 实例）。</para>
/// </summary>
public class DisplayBackgroundSettingsPageViewModel(MainConfigHandler configHandler) : ViewModelBase(configHandler);
