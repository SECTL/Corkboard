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
}
