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

    // ── 考试看板：现在只有四页 ────────────────────────────────────────────────
    // 下面四个**旧页 Id** 已经不注册导航了（页面被并进别的页），只留着把旧书签 / 旧冒烟参数
    // 送到新位置：alias 表在 `Corkboard/Views/SettingsView.axaml.cs` 的 `ResolvePageId`。
    // ⚠️ 这四条随便删哪一条，`--open-exam-floating` 之类的旧参数就落不到任何地方（静默停在默认页）。

    /// <summary>「看板字号」页（已删，字号改到布局编辑窗里调）的旧 Id。</summary>
    public const string SettingsExamFontPageId = "settings.exam.font";

    /// <summary>「最小化与浮窗」页（已并进「看板显示」）的旧 Id。</summary>
    public const string SettingsExamFloatingPageId = "settings.exam.floating";

    /// <summary>「看板布局」页（已并进「看板外观」）的旧 Id。</summary>
    public const string SettingsExamLayoutPageId = "settings.exam.layout";

    /// <summary>「时间同步」页（已并进「放映与时间」）的旧 Id。</summary>
    public const string SettingsExamTimePageId = "settings.exam.time";

    /// <summary>「看板显示」页 Id（吸收原「最小化与浮窗」）。</summary>
    public const string SettingsExamDisplayPageId = "settings.exam.display";

    /// <summary>「看板外观」页 Id（吸收原「看板布局」，主题来源搬到了「看板显示 → 背景」）。</summary>
    public const string SettingsExamThemePageId = "settings.exam.theme";

    /// <summary>「放映与时间」页 Id（吸收原「时间同步」）。</summary>
    public const string SettingsExamAutoShowPageId = "settings.exam.autoplay";

    /// <summary>「看板模式」页 Id：只给 <c>--open-display-settings</c> 版式冒烟用。</summary>
    public const string SettingsDisplayPageId = "settings.display";

    /// <summary>「一言与天气」页 Id：只给 <c>--open-display-widgets</c> 版式冒烟用。</summary>
    public const string SettingsDisplayWidgetsPageId = "settings.display.widgets";

    /// <summary>「背景」页 Id：只给 <c>--open-display-background</c> 版式冒烟用。</summary>
    public const string SettingsDisplayBackgroundPageId = "settings.display.background";
}