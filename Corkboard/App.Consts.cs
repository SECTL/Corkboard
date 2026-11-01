namespace Corkboard;

/// <summary>应用级常量。全局可见的常量放 <c>Corkboard.Core.GlobalConstants</c>。</summary>
public static class AppConsts
{
    /// <summary>主窗口标识，窗口尺寸记忆与窗口查找共用。</summary>
    public const string MainWindowScope = "main";

    /// <summary>设置窗口标识。</summary>
    public const string SettingsWindowScope = "settings";

    /// <summary>主界面默认页面 Id。</summary>
    public const string DefaultMainPageId = "main.board";

    /// <summary>设置界面默认页面 Id。</summary>
    public const string DefaultSettingsPageId = "settings.general.basic";

    /// <summary>「关于」页 Id：托盘菜单顶上那项直接跳到它。</summary>
    public const string SettingsAboutPageId = "settings.about";

    /// <summary>「看板字号」页 Id：只给 <c>--open-exam-font</c> 版式冒烟用。</summary>
    public const string SettingsExamFontPageId = "settings.exam.font";

    /// <summary>「最小化与浮窗」页 Id：只给 <c>--open-exam-floating</c> 版式冒烟用。</summary>
    public const string SettingsExamFloatingPageId = "settings.exam.floating";

    /// <summary>「看板模式」页 Id：只给 <c>--open-display-settings</c> 版式冒烟用。</summary>
    public const string SettingsDisplayPageId = "settings.display";

    /// <summary>「一言与天气」页 Id：只给 <c>--open-display-widgets</c> 版式冒烟用。</summary>
    public const string SettingsDisplayWidgetsPageId = "settings.display.widgets";

    /// <summary>「背景」页 Id：只给 <c>--open-display-background</c> 版式冒烟用。</summary>
    public const string SettingsDisplayBackgroundPageId = "settings.display.background";
}
