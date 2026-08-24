# 更新日志（Changelog）

本项目遵循 [语义化版本 SemVer](https://semver.org/lang/zh-CN/)。所有值得注意的变更记录于此。

## [1.8.0] - 2026-08-24

### 新增
- 统一日志系统：分级日志（Debug/Info/Warn/Error）、按入口分文件（app.log / contextmenu.log / install.log）、启动时自动把上一次日志归档为 `app-时间戳.zip`
- 本地崩溃报告器：捕获未处理异常，收集软件/系统/插件信息与最近日志，可选生成内存转储（仅保留最新一份）；崩溃报告窗口左右分栏显示崩溃报告与运行日志，支持复制崩溃报告 / 复制日志 / 打开日志目录
- 日志查看器：只读查看当前日志、刷新、打开日志目录
- 设置新增「高级」选项卡：日志级别、崩溃转储开关、崩溃转储等级（Normal / WithDataSegments / FullMemory）、崩溃测试按钮、查看日志、清理日志
- 设置保存时检测「需重启生效」的设置项（语言、日志级别、转储开关/等级），若有改动则询问是否立即重启
- 开发版主界面警告横幅（pre-release 版本运行时显示）
- 新增 QMC 解密插件：支持 QQ 音乐 `.qmc0`/`.qmc3`/`.qmcflac`/`.qmcogg` 老格式解密，解密后可自动转码为其它音频格式
- KGM/QMC 解密插件标记「未经测试」，相关格式在界面显示警告

### 修复
- 崩溃报告的系统信息 OS 版本错误：改用 `RtlGetVersion` 获取真实版本，并按 Build 号区分 Win10/Win11（≥22000 为 Win11）
- 崩溃测试按钮点了不崩溃：`Task.Run` 的异常会被 .NET 当「未观察的 Task 异常」静默吞掉，改用原始 `Thread` 使其真正触发 `AppDomain.UnhandledException`

### 变更
- 核心转换流程、插件加载、更新检查、扩展中心增加日志埋点

## [1.7.6] - 2026-08-24

### 修复
- 安装后重启 explorer 只打开「此电脑」窗口而非完整 shell：根因是 32 位安装器被 WOW64 重定向到 `SysWOW64\explorer.exe`（32 位 stub），改用完整路径 `{win}\explorer.exe` 直接启动 64 位 explorer，并加任务栏窗口（Shell_TrayWnd）检测与自动重试兜底

### 新增
- 右键菜单注册改为安装可选项（默认勾选、标注推荐），用户可在安装时关闭

## [1.7.5] - 2026-08-24

### 修复
- 安装时插件 DLL 仍被 explorer 占用：`KillExplorer` 提前到第一处写 explorer 锁定文件之前，并重排复制顺序（exe/大文件在前、锁定的 DLL 压到最尾段），把结束 explorer 的时间窗口压到最小
- 安装后重启 explorer 可能只拉起一个"半加载"的损坏进程，或新开一个"此电脑"窗口并残留进程：改为先 `taskkill` 清掉所有 explorer、再显式拉起完整 shell，不依赖系统自动恢复

### 新增
- 自动更新改为静默安装：下载完成后跳过安装向导、直接升级（`/SILENT` 装回原目录），装完自动启动新版本
- 应用启动时自动清理 `%TEMP%` 里遗留的 `UniversalConvert-Setup-*.exe` 安装包
- 卸载时可选一并删除用户配置、日志与已安装的扩展
- 安装完成页新增「立即运行 UniversalConvert」选项

## [1.7.4] - 2026-08-23

### 修复
- 安装时显式关闭正在运行的 UniversalConvert（先优雅关闭、再强制结束兜底），修复此前忽略运行实例导致文件被占用、安装不完整
- 重启 explorer 改用单一 `ShellExecute`，修复此前可能新开一个资源管理器窗口的问题

## [1.7.3] - 2026-08-23

### 新增
- 扩展中心：浏览扩展仓库、在线安装/更新/卸载扩展（含兼容性提示与下载进度）
- 插件可随包分发工具（`tools\` 目录自动定位）、插件目录支持子目录
- 新建公开扩展仓库 `universal-convert-extensions`，内置 Pandoc 文档转换插件

### 变更
- AssemblyVersion 稳定化为 1.0.0.0，保证外部插件二进制兼容

## [1.7.2] - 2026-08-23

### 修复
- 安装时不再忽略正在运行的 UniversalConvert：恢复「关闭程序」提示（仅排除 explorer），避免文件占用导致安装不完整

## [1.7.1] - 2026-08-23

### 修复
- 安装时 explorer 重启失败：被占用的 DLL 改为最后替换、重启改用 `SW_SHOWNORMAL` 并加降权回退

### 变更
- Release 附带 Core SDK（`UniversalConvert.Core.SDK.zip`），供扩展仓库编译插件引用

## [1.7.0] - 2026-08-23

### 新增
- 插件包格式（manifest.json + zip）与用户级插件目录（`%AppData%\UniversalConvert\plugins`，与内置目录合并加载、同 Id 用户优先），为扩展仓库与在线安装打地基

## [1.6.0] - 2026-08-23

### 新增
- 音频播放器显示采样率、实时码率（VBR 动态变化）、位深、声道（通过随包的 ffprobe 读取）

## [1.5.10] - 2026-08-23

### 修复
- 安装流程改为：先复制普通文件 → 结束 explorer → 写右键菜单 DLL → 注册 → 重启 explorer，不再依赖重启电脑、也不在开始时暴力结束 explorer

## [1.5.9] - 2026-08-23

### 新增
- 音频播放器新增音量滑块、进度滑块（可拖动/点击定位）、时长与剩余时间显示

### 修复
- 安装升级时 explorer 不再被列入「需要关闭的程序」（占用文件改为重启后替换）

## [1.5.8] - 2026-08-23

### 修复
- 移除安装时「强制结束 explorer」的步骤（会导致桌面/任务栏异常），改为安装完成提示
- 音频播放：通过窗口 X 按钮关闭时停止后台播放

## [1.5.7] - 2026-08-23

### 新增
- 简易音频播放：主界面双击音频文件可播放；批量转换窗口双击已完成项（播放转换后的文件）

## [1.5.6] - 2026-08-23

### 改进
- 右键菜单项添加应用图标，并改为「使用 UniversalConvert 转换为」「使用 UniversalConvert 打开」

## [1.5.5] - 2026-08-23

### 新增
- 批量转换改为并行处理，并新增「处理线程数」设置（默认 2，1/2/4/8 可选）

### 修复
- 修复切换语言不生效：安装包未部署语言卫星程序集（`en\...\resources.dll`）

## [1.5.4] - 2026-08-23

### 新增
- 设置改为选项卡布局，新增「常规」选项卡
- 「语言」设置：跟随系统 / 中文 / English（重启后生效）
- 设置标签本地化（`@资源键` 机制，随界面语言切换）

## [1.5.3] - 2026-08-23

### 改进
- 更新下载改为 8 线程分段下载，大幅提升下载速度（服务器不支持分段时自动回退单线程）

## [1.5.2] - 2026-08-23

### 修复
- 修复右键菜单不显示：安装时补充 `regasm /codebase` 注册 COM 服务器（CLSID）。此前 SharpShell 的 `RegisterServer` 只写「关联」，导致 explorer 找到菜单项却找不到实现

## [1.5.1] - 2026-08-23

### 修复
- 修复「查看更新内容」在中文系统上乱码（更新检查强制使用 UTF-8 解码响应）

## [1.5.0] - 2026-08-23

### 新增
- 扩展管理器：展示插件及其版本，按插件声明的 Min/Max 应用版本显示兼容性警告，并展示加载错误
- 插件版本元数据：IConverterPlugin 增加 Version / MinAppVersion / MaxAppVersion
- 关于页面（版本、第三方组件声明、项目主页）
- 更新内容改为应用内弹窗显示

### 变更
- GitHub Release 正文改用 CHANGELOG 对应版本内容（不再用自动生成的 commit 摘要）

## [1.4.0] - 2026-08-23

### 新增
- 开发版/正式版双通道：开发版 tag 为 `vX.Y.Z-dev.N`（prerelease），正式版为 `vX.Y.Z`，应用按渠道检查更新
- 通用设置系统：设置项 schema + 通用设置界面 + 持久化
- 插件设置 API：`IPluginContext.GetSetting` 读取，`ISettingsContributor` 声明插件自定义设置
- 「更新渠道」设置项（自动 / 仅正式版 / 包含开发版）

## [1.3.1] - 2026-08-23

### 变更
- 自动更新：点击下载改为真正下载并显示进度条，新增「查看更新内容」按钮

## [1.3.0] - 2026-08-23

### 新增
- 酷狗音乐 KGM/KGMA 解密（移植自 ghtz08/kugou-kgm-decoder，反 996 许可证），可转码为其它音频格式

## [1.2.3] - 2026-08-23

### 新增
- 图片格式转换：jpg/jpeg/png/bmp/webp/tiff/tif 互转（基于 FFmpeg）

## [1.2.2] - 2026-08-23

### 新增
- 应用图标：exe、窗口标题栏与安装包图标

## [1.2.1] - 2026-08-23

### 变更
- 自定义表单：参数项改为「文字靠左、控件靠右」对齐，枚举下拉改为可编辑（可输入或选择）
- 码率增加「原始」选项，新增「采样率」选项，并扩充码率/分辨率/帧率/编码等可选项

## [1.2.0] - 2026-08-23

### 新增
- 批量转换：主界面改为文件列表，可拖入/多选多个文件批量转换为共同支持的目标格式，逐文件状态（完成/失败/跳过）与错误汇总，支持复制报错

## [1.1.2] - 2026-08-23

### 变更
- 右键菜单增加诊断日志（`%AppData%\UniversalConvert\contextmenu.log`），用于排查菜单不显示问题

## [1.1.1] - 2026-08-23

### 变更
- NCM 插件改为「先解密成原格式，再按需 FFmpeg 转码」，`.ncm` 现可转换为全部音频格式（mp3/flac/wav/aac/ogg/m4a/opus），不再只是解密成原格式

## [1.1.0] - 2026-08-23

### 新增
- NCM 格式解密插件（纯 C#，网易云 `.ncm` → 原始 mp3/flac，移植自 hkylin/ncmdumpGUI，MIT）
- 错误解析器：归类常见转换错误（未知编码器/文件不存在/权限不足/磁盘满/输入损坏），失败时给出友好说明与修复建议
- 转换失败弹窗新增「重试」与「复制报错」按钮，可展开查看详细报错
- 主界面管理员运行警告横幅（提示管理员身份会禁用资源管理器拖放）
- 自动更新检查（GitHub Releases），发现新版本时顶部提示下载
- 安装程序中英双语可切换（默认中文）；WPF 应用界面国际化（resx，跟随系统语言）
- 扩展开发技术文档（`docs/扩展开发指南.md`）

### 修复
- 右键菜单注册后不生效：安装完成页新增「重启资源管理器」选项（默认勾选），安装器增加日志输出（`%AppData%\UniversalConvert\install.log`）
- Inno Setup 中文语言文件缺失导致打包失败：内置 `ChineseSimplified.isl`
- FFmpeg 打包进安装包（构建时下载 GPL 版，随包自带免配置）

## [1.0.0] - 2026-08-23

### 新增
- 项目骨架：壳 + 插件架构（Core 抽象层 + 插件 DLL 动态加载）
- FFmpeg 插件：音视频互转，含参数 schema 与命名预设
- WPF 主程序：主界面 + 无界面 `--convert` 模式 + 动态参数表单
- SharpShell 动态右键菜单（按文件扩展名生成「转换为」级联菜单）
- 安装/注册器（注册/卸载右键菜单）
- CI 自动编译 + Inno Setup 打包 + 打 tag 自动发 GitHub Release

[1.8.0]: https://github.com/114514901/universal-convert/releases/tag/v1.8.0
[1.7.6]: https://github.com/114514901/universal-convert/releases/tag/v1.7.6
[1.7.5]: https://github.com/114514901/universal-convert/releases/tag/v1.7.5
[1.7.4]: https://github.com/114514901/universal-convert/releases/tag/v1.7.4
[1.7.3]: https://github.com/114514901/universal-convert/releases/tag/v1.7.3
[1.7.2]: https://github.com/114514901/universal-convert/releases/tag/v1.7.2
[1.7.1]: https://github.com/114514901/universal-convert/releases/tag/v1.7.1
[1.7.0]: https://github.com/114514901/universal-convert/releases/tag/v1.7.0
[1.6.0]: https://github.com/114514901/universal-convert/releases/tag/v1.6.0
[1.5.10]: https://github.com/114514901/universal-convert/releases/tag/v1.5.10
[1.5.9]: https://github.com/114514901/universal-convert/releases/tag/v1.5.9
[1.5.8]: https://github.com/114514901/universal-convert/releases/tag/v1.5.8
[1.5.7]: https://github.com/114514901/universal-convert/releases/tag/v1.5.7
[1.5.6]: https://github.com/114514901/universal-convert/releases/tag/v1.5.6
[1.5.5]: https://github.com/114514901/universal-convert/releases/tag/v1.5.5
[1.5.4]: https://github.com/114514901/universal-convert/releases/tag/v1.5.4
[1.5.3]: https://github.com/114514901/universal-convert/releases/tag/v1.5.3
[1.5.2]: https://github.com/114514901/universal-convert/releases/tag/v1.5.2
[1.5.1]: https://github.com/114514901/universal-convert/releases/tag/v1.5.1
[1.5.0]: https://github.com/114514901/universal-convert/releases/tag/v1.5.0
[1.4.0]: https://github.com/114514901/universal-convert/releases/tag/v1.4.0
[1.3.1]: https://github.com/114514901/universal-convert/releases/tag/v1.3.1
[1.3.0]: https://github.com/114514901/universal-convert/releases/tag/v1.3.0
[1.2.3]: https://github.com/114514901/universal-convert/releases/tag/v1.2.3
[1.2.2]: https://github.com/114514901/universal-convert/releases/tag/v1.2.2
[1.2.1]: https://github.com/114514901/universal-convert/releases/tag/v1.2.1
[1.2.0]: https://github.com/114514901/universal-convert/releases/tag/v1.2.0
[1.1.2]: https://github.com/114514901/universal-convert/releases/tag/v1.1.2
[1.1.1]: https://github.com/114514901/universal-convert/releases/tag/v1.1.1
[1.1.0]: https://github.com/114514901/universal-convert/releases/tag/v1.1.0
[1.0.0]: https://github.com/114514901/universal-convert/releases/tag/v1.0.0
