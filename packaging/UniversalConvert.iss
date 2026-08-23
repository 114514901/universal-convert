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
#define MyAppVersion "1.0.0"
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

[Files]
Source: "{#DistDir}\*.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#DistDir}\*.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#DistDir}\plugins\*"; DestDir: "{app}\plugins"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#DistDir}\tools\*"; DestDir: "{app}\tools"; Flags: ignoreversion recursesubdirs createallsubdirs

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加任务："; Flags: unchecked

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; 文件复制完后，调用我们的注册器写 HKLM 右键菜单（继承安装包的管理员权限）
Filename: "{app}\UniversalConvert.Installer.exe"; Parameters: "install"; \
    Flags: runhidden; StatusMsg: "正在注册右键菜单..."

[UninstallRun]
; 卸载时先反注册，再删文件（Inno 会先执行本段再删除文件）
Filename: "{app}\UniversalConvert.Installer.exe"; Parameters: "uninstall"; Flags: runhidden

[UninstallDelete]
; 清理用户后续手动放入的插件/工具
Type: filesandordirs; Name: "{app}\plugins"
Type: filesandordirs; Name: "{app}\tools"
