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
; 版本号默认 2.0.1，CI 打 tag 时会用 /DMyAppVersion=<tag> 覆盖
#ifndef MyAppVersion
#define MyAppVersion "2.0.1"
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
; 弹「需要关闭的程序」提示（如正在运行的 UniversalConvert），但排除 explorer——explorer 由 [Code] 的杀/重启流程处理
CloseApplications=yes
CloseApplicationsFilter=explorer.exe
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
chinesesimplified.AddContextMenu=添加到右键菜单（推荐）
chinesesimplified.RunAfterInstall=立即运行 UniversalConvert
chinesesimplified.AskDeleteUserData=是否同时删除配置、日志与已安装的扩展？
english.CreateDesktopIcon=Create a desktop icon
english.AdditionalTasks=Additional tasks:
english.RegisteringContextMenu=Registering context menu...
english.AddContextMenu=Add to context menu (recommended)
english.RunAfterInstall=Run UniversalConvert
english.AskDeleteUserData=Delete settings, logs and installed extensions as well?

[Files]
; 先复制 explorer 不会锁定的文件（exe、语言卫星程序集、tools 大文件）。
; tools\ffmpeg.exe 我们的代码只用 File.Exists 定位，从不加载进 explorer，正常安装不会被锁；
; 趁 explorer 活着先把 ~100MB 大文件拷完，把「结束 explorer」压缩到最小时间窗口。
Source: "{#DistDir}\*.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#DistDir}\en\*"; DestDir: "{app}\en"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#DistDir}\tools\*"; DestDir: "{app}\tools"; Flags: ignoreversion recursesubdirs createallsubdirs
; 下面这些 DLL 会被 explorer 锁定（右键菜单把它们加载进 explorer.exe：CoreHost 扫描 plugins 目录
; Assembly.LoadFrom 插件 DLL；根目录的 ContextMenu/SharpShell/Core/Newtonsoft 也被 explorer 加载）。
; 因此放到最后、紧挨在一起：写第一处前 KillExplorer 结束 explorer，写完由 ssPostInstall 统一重启。
; explorer 关闭时间只有这几秒的小文件拷贝，不含 tools 大文件。
; KillExplorer 内部有 ExplorerKilled 标志，只执行一次，后续条目的 BeforeInstall 是空操作。
Source: "{#DistDir}\plugins\*"; DestDir: "{app}\plugins"; Flags: ignoreversion recursesubdirs createallsubdirs; BeforeInstall: KillExplorer
Source: "{#DistDir}\*.dll"; DestDir: "{app}"; Flags: ignoreversion; BeforeInstall: KillExplorer

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalTasks}"; Flags: unchecked
Name: "runapp"; Description: "{cm:RunAfterInstall}"; GroupDescription: "{cm:AdditionalTasks}"; Flags: unchecked
Name: "contextmenu"; Description: "{cm:AddContextMenu}"; GroupDescription: "{cm:AdditionalTasks}"; Flags: checkedonce

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; 文件复制完后，调用我们的注册器写 HKLM 右键菜单（继承安装包的管理员权限）
Filename: "{app}\UniversalConvert.Installer.exe"; Parameters: "install"; \
    Flags: runhidden; StatusMsg: "{cm:RegisteringContextMenu}"; Tasks: contextmenu
; 手动安装：完成页勾选「立即运行」（postinstall + skipifsilent 使静默安装时不显示/不执行）
Filename: "{app}\UniversalConvert.App.exe"; Description: "{cm:RunAfterInstall}"; \
    Flags: nowait postinstall skipifsilent; Tasks: runapp
; 自动更新静默安装：装完立即启动新版本（Check: WizardSilent 保证只在静默时执行，不与上面重复启动）
Filename: "{app}\UniversalConvert.App.exe"; Flags: nowait; Tasks: runapp; Check: WizardSilent

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

procedure CloseRunningApp();
var
  ResultCode: Integer;
begin
  // 先优雅关闭（发 WM_CLOSE），再强制结束残留进程，避免 exe/dll 被占用导致安装不完整
  Exec('taskkill.exe', '/im UniversalConvert.App.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec('taskkill.exe', '/f /im UniversalConvert.App.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  // 复制文件前显式关闭正在运行的实例（CloseApplications 检测不可靠，这里兜底）
  CloseRunningApp;
  Result := '';
end;

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

procedure Sleep(dwMilliseconds: LongWord);
external 'Sleep@kernel32.dll stdcall';

function FindWindowW(lpClassName, lpWindowName: String): LongWord;
external 'FindWindowW@user32.dll stdcall';

procedure RestartExplorer();
var
  ResultCode: Integer;
  Attempt: Integer;
  i: Integer;
begin
  // 清掉可能残留的 explorer
  Exec('taskkill.exe', '/f /im explorer.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  // 根因：Inno 安装器是 32 位进程（CI 用 "Program Files (x86)" 的 ISCC 编译），
  // ShellExec('explorer.exe') 会被 WOW64 重定向到 SysWOW64\explorer.exe（32 位 stub），
  // stub 再带路径拉起 64 位 explorer → 只开"此电脑"窗口而非成为 shell。
  // 改用完整路径 {win}\explorer.exe 直接启动 64 位 explorer，使其成为 shell。
  for Attempt := 1 to 3 do
  begin
    ShellExec('open', ExpandConstant('{win}\explorer.exe'), '', '', SW_SHOWNORMAL, ewNoWait, ResultCode);

    // 等任务栏窗口（Shell_TrayWnd）出现，确认成为完整 shell；没出现就再杀再启（最多 3 次）。
    for i := 1 to 16 do
    begin
      if FindWindowW('Shell_TrayWnd', '') <> 0 then
        Exit;
      Sleep(500);
    end;

    Exec('taskkill.exe', '/f /im explorer.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Sleep(800);
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    if ExplorerKilled then
      RestartExplorer();
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  // 卸载完成后，询问是否一并删除用户数据（配置/日志/在线安装的扩展）。默认保留。
  if CurUninstallStep = usPostUninstall then
  begin
    if MsgBox(ExpandConstant('{cm:AskDeleteUserData}'), mbConfirmation, MB_YESNO) = IDYES then
    begin
      DelTree(ExpandConstant('{userappdata}\UniversalConvert'), True, True, True);
    end;
  end;
end;
