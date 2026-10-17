; Corkboard 的 Inno Setup 安装包脚本（照上游 SecRandom-C 的 Setup.iss 改写）。
; 由 .github/workflows/Publish.yml 的「Build Windows Setup」步骤调用：
;   ISCC.exe Setup.iss /DMyAppVersion=<tag> /DMyAppOutDir=<发布树> /F<输出名> /O<输出目录> /DBuildArch<Arch>
; Corkboard 没有 UIAccess 清单，所以上游的 UiAccessBuild 分支（admin 安装到 {autopf} + 递升权限）整段删掉了。

#ifndef MyAppName
#define MyAppName "Corkboard"
#endif
#ifndef MyAppVersion
#define MyAppVersion "APP_VERSION"
#endif
#define MyAppPublisher "SECTL Studio"
; 安装后直接运行应用本体（与上游一致：安装包装的是 windows-exe 形态的发布树，不走便携包的 Launcher）。
#define MyAppExeName "Corkboard.Desktop.exe"
#define MyAppURL "https://github.com/SECTL/Corkboard"
#ifndef MyAppOutDir
#define MyAppOutDir "APP_OUTDIR"
#endif

[Setup]
; NOTE: The value of AppId uniquely identifies this application. Do not use the same AppId value in installers for other applications.
; (To generate a new GUID, click Tools | Generate GUID inside the IDE.)
AppId={{9936DC9F-6104-4D20-BDC4-EACCCFDF472A}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
;AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppSupportURL={#MyAppURL}
DefaultDirName={localappdata}\SECTL\Corkboard
UninstallDisplayIcon={app}\{#MyAppExeName}
#ifdef BuildArchX86
ArchitecturesAllowed=x86compatible
#else
#ifdef BuildArchArm64
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#else
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#endif
#endif
DisableProgramGroupPage=yes
; LicenseFile=E:\Projects\Avalonia\Corkboard\LICENSE
; 普通用户目录安装，不需要管理员权限；允许用户在安装界面里改成「为所有用户安装」。
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=artifacts\release\setup
OutputBaseFilename=Corkboard-Setup
SetupIconFile=Corkboard\Assets\Icons\Corkboard.ico
SolidCompression=yes
WizardStyle=modern dynamic windows11
LanguageDetectionMethod=none

[Languages]
; 中文与日文语言文件不在 Inno 自带的 Languages 里，工作流会先把它们下载进 Inno 目录（照上游做法）。
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#MyAppOutDir}\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#MyAppOutDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; NOTE: Don't use "Flags: ignoreversion" on any shared system files

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
