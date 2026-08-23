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
; 版本号默认 1.6.0，CI 打 tag 时会用 /DMyAppVersion=<tag> 覆盖
#ifndef MyAppVersion
#define MyAppVersion "1.6.0"
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
; 不弹"需要关闭的程序"对话框——被占用的右键菜单 DLL 由 [Code] 里先结束 explorer 再写、写完重启
CloseApplications=no
RestartApplications=no

[Languages]
; 中文在前 = 默认语言；语言选择对话框在有多个语言时自动显示
; 中文语言文件随仓库打包（choco 的 innosetup 不带 Languages 目录），英文用编译器自带的 Default.isl
Name: "chinesesimplified"; MessagesFile: "languages\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
chinesesimplified.CreateDesktopIcon=创建桌面快捷方式
chinesesimplified.AdditionalTasks=附加任务：
chinesesimplified.RegisteringContextMenu=正在注册右键菜单...
english.CreateDesktopIcon=Create a desktop icon
english.AdditionalTasks=Additional tasks:
english.RegisteringContextMenu=Registering context menu...

[Files]
; exe 等非 explorer 占用的文件先正常复制
Source: "{#DistDir}\*.exe"; DestDir: "{app}"; Flags: ignoreversion
; 被 explorer 加载的 DLL 放到这里：BeforeInstall 里先结束 explorer，再写入，ssPostInstall 里重启
Source: "{#DistDir}\*.dll"; DestDir: "{app}"; Flags: ignoreversion; BeforeInstall: KillExplorer
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

[UninstallRun]
; 卸载时先反注册，再删文件（Inno 会先执行本段再删除文件）
Filename: "{app}\UniversalConvert.Installer.exe"; Parameters: "uninstall"; Flags: runhidden

[UninstallDelete]
; 清理用户后续手动放入的插件/工具
Type: filesandordirs; Name: "{app}\plugins"
Type: filesandordirs; Name: "{app}\tools"

[Code]
var
  ExplorerKilled: Boolean;

procedure KillExplorer();
var
  ResultCode: Integer;
begin
  if not ExplorerKilled then
  begin
    Exec('taskkill.exe', '/f /im explorer.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    ExplorerKilled := True;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  if CurStep = ssPostInstall then
  begin
    if ExplorerKilled then
      ExecAsOriginalUser('explorer.exe', '', '', SW_HIDE, ewNoWait, ResultCode);
  end;
end;
