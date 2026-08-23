; UniversalConvert 安装脚本（Inno Setup 6）
;
; 使用方法：
;   1. 编译解决方案，把所有输出整理到 dist\ 目录：
;        dist\
;          UniversalConvert.App.exe
;          UniversalConvert.Core.dll
;          UniversalConvert.ContextMenu.dll
;          UniversalConvert.Installer.exe
;          SharpShell.dll
;          Newtonsoft.Json.dll
;          plugins\UniversalConvert.Plugin.FFmpeg.dll
;          tools\ffmpeg.exe        <- LGPL essentials 构建
;          tools\ffprobe.exe       <- 可选
;   2. 用 Inno Setup 打开并编译本脚本，输出 Setup 到 ..\output

#define MyAppName "UniversalConvert"
; 版本号默认 1.5.5，CI 打 tag 时会用 /DMyAppVersion=<tag> 覆盖
#ifndef MyAppVersion
#define MyAppVersion "1.5.5"
#endif
#define MyAppPublisher "UniversalConvert"
#define MyAppExeName "UniversalConvert.App.exe"
#define DistDir "..\dist"

[Setup]
AppId=UniversalConvert
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=..\output
OutputBaseFilename=UniversalConvert-Setup
Compression=lzma
SolidCompression=yes
WizardStyle=modern
; 只支持 64 位（SharpShell 按 OS64Bit 注册，ffmpeg 用 64 位构建）
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64
; 注册右键菜单需写 HKLM，安装包必须提权
PrivilegesRequired=admin

[Languages]
; 中文在前 = 默认语言；语言选择对话框在有多个语言时自动显示
; 中文语言文件随仓库打包（choco 的 innosetup 不带 Languages 目录），英文用编译器自带的 Default.isl
Name: "chinesesimplified"; MessagesFile: "languages\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
chinesesimplified.CreateDesktopIcon=创建桌面快捷方式
chinesesimplified.AdditionalTasks=附加任务：
chinesesimplified.RegisteringContextMenu=正在注册右键菜单...
chinesesimplified.RestartExplorer=重启资源管理器以生效（推荐）
english.CreateDesktopIcon=Create a desktop icon
english.AdditionalTasks=Additional tasks:
english.RegisteringContextMenu=Registering context menu...
english.RestartExplorer=Restart Explorer to apply changes (recommended)

[Files]
Source: "{#DistDir}\*.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#DistDir}\*.dll"; DestDir: "{app}"; Flags: ignoreversion
; 语言卫星程序集（en\...\resources.dll）
Source: "{#DistDir}\en\*"; DestDir: "{app}\en"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#DistDir}\plugins\*"; DestDir: "{app}\plugins"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#DistDir}\tools\*"; DestDir: "{app}\tools"; Flags: ignoreversion recursesubdirs createallsubdirs

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalTasks}"; Flags: unchecked

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; 文件复制完后，调用我们的注册器写 HKLM 右键菜单（继承安装包的管理员权限）
Filename: "{app}\UniversalConvert.Installer.exe"; Parameters: "install"; \
    Flags: runhidden; StatusMsg: "{cm:RegisteringContextMenu}"
; 完成页提供"重启资源管理器"复选框（默认勾选），重启后右键菜单立即生效
Filename: "{cmd}"; Parameters: "/c taskkill /f /im explorer.exe & start explorer.exe"; \
    Description: "{cm:RestartExplorer}"; Flags: postinstall runhidden

[UninstallRun]
; 卸载时先反注册，再删文件（Inno 会先执行本段再删除文件）
Filename: "{app}\UniversalConvert.Installer.exe"; Parameters: "uninstall"; Flags: runhidden

[UninstallDelete]
; 清理用户后续手动放入的插件/工具
Type: filesandordirs; Name: "{app}\plugins"
Type: filesandordirs; Name: "{app}\tools"
