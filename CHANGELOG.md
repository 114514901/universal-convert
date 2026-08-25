# 更新日志（Changelog）

本项目遵循 [语义化版本 SemVer](https://semver.org/lang/zh-CN/)。所有值得注意的变更记录于此。

## [2.1.0-dev.2] - 2026-08-25

### 修复
- 扩展更新/卸载重启后未生效：重启时新进程可能在旧进程完全退出前就尝试替换插件目录，DLL 句柄未释放导致失败且无提示——现在暂存时记录旧进程 PID，启动时先等旧进程退出（最多 10 秒）再应用；单个失败不再阻塞其它
- 暂存内容应用失败时不再静默：启动后提示「有扩展更新/卸载未能应用（文件被占用）」，下次启动会继续重试

## [2.1.0-dev.1] - 2026-08-25

### 新增
- 扩展更新/安装进度弹窗：列表逐行显示每个扩展的下载进度与状态（等待/下载中/解压/完成/失败），多个扩展并行更新；插件管理器批量与单插件更新、扩展中心安装统一使用

### 修复
- 扩展更新选「是」后无进度无结果：下载请求改用手动跟随重定向（.NET Framework 自动重定向会丢失 Range 头，导致分段下载错乱/卡死），并加 60 秒超时；失败原因现在逐行显示在进度弹窗中，不再静默

## [2.0.2] - 2026-08-25

### 新增
- 扩展管理与在线分发：扩展中心可安装/更新/卸载非内置扩展（免管理员，装到 %AppData%）；插件管理器支持卸载用户扩展、检查扩展更新（只检查用户扩展、并行比对，发现更新后弹窗确认）
- 扩展更新/卸载统一「直接生效 or 暂存重启」：已加载插件 DLL 被进程锁定时自动暂存，重启后应用（含启动重试）；需要重启的操作（含首次安装）完成后提示「是否立即重启」
- 同一「输入→输出」方向被多个扩展注册时，转换前询问用户选择用哪个，可勾选「不再提醒」记住选择；设置「高级」可清除格式选择记忆；记住的插件被卸载后自动回退重新询问
- 插件可选接口 `IPreviewProvider`：扩展可声明支持某些扩展名的文件预览（如 MIDI 合成），主程序预览窗口按「提供者优先」策略先渲染再播放
- 插件加载时强制校验 `MinAppVersion`：应用版本低于插件要求时跳过加载，并在扩展管理器显示友好提示
- 音频预览支持 MIDI（.mid/.midi）：由 MIDI 扩展（FluidSynth + GeneralUser GS 音色库）渲染成 wav 后播放

### 修复
- 扩展更新/卸载在重启后未生效：旧进程可能仍持有插件 DLL 句柄导致启动时应用失败——现在启动应用带重试；卸载/更新只捕获 IOException 导致权限类异常静默失败——改为统一暂存待重启并记录日志
- 扩展中心新装扩展后未询问重启（新扩展需重启才会被应用加载）：现在安装完成后同样提示「是否立即重启」
- 检查扩展更新弹窗选「否」后状态栏仍停留「正在检查扩展更新…」：改为提示「已跳过更新」，单插件检查同样处理
- 设置「更新」选项卡检查到新版本但下载更新按钮不出现：release 刚创建、安装包尚未上传完成时资产列表为空——按固定命名规则构造下载地址兜底

### 改进
- 主界面默认输出文件夹从「音乐」改为「文档」
- 主界面/右键菜单对同一方向的多注册条目去重显示
- `SemVersion` 移入 Core，供插件加载器做版本比较
- 重写《扩展开发指南》：补充扩展中心/在线安装与更新卸载/重启暂存、FormatResolver 格式选择记忆、`@资源键` 本地化、IPreviewProvider、MinAppVersion 加载校验等

## [2.0.1] - 2026-08-25

### 新增
- 全新现代 UI：引入 ModernWpf（Fluent 设计），主界面/设置/关于窗口卡片式重排，支持主题色个性化
- 主界面文件列表三列（文件 / 转换参数 / 格式），支持每个文件单独自定义参数、右键菜单与快捷键
- 主界面新增「输出位置」输入框（默认音乐文件夹），批量转换输出到指定目录
- 看护进程（watchdog）：后台心跳 + IsHungAppWindow 双通道检测主程序卡死（约 15 秒阈值），命中后结束进程并弹出卡死报告；崩溃报告窗口新增「重启」按钮
- 设置新增「更新」选项卡（检查更新 / 查看更新内容 / 下载更新）与「高级」选项卡（日志、崩溃转储、崩溃测试、测试卡死）
- 关于窗口新增「支持的格式」列表：从格式注册表动态汇总，标注来源插件，内置与用户扩展自动收录
- FFmpeg 插件新增 HEIC/HEIF 与 AVIF 图片格式支持（与 jpg/png/webp 等互转）
- 处理线程数设置新增「自动」选项：按逻辑核心数 × 75%（四舍五入）计算，低于 4 核的设备用 1 线程
- 音频预览支持 Opus 格式：系统解码失败时自动用随包 ffmpeg 转码播放

### 修复
- mp4→gif 转换失败（音频流被错误映射进 GIF）：GIF 输出现在只取视频流
- 自定义表单「默认」预设改用插件声明的 DefaultValue（如 gif 帧率默认 10fps），与右键菜单行为一致
- 自定义参数保存后重新打开表单回填上次保存的参数并恢复预设选择
- 自定义参数表单的参数名与「原始」选项本地化（@资源键 + Strings.L）
- 崩溃报告窗口摘要、卡死报告标签本地化；报告模式遵循用户选择的语言
- 设置「更新」手动检查到新版本后显示「查看更新内容」与「下载更新」按钮
- 音频播放完毕后再拖动进度条：同步播放状态与按钮（进度条恢复走动）
- 启动不再被更新检查卡住：改为后台异步检查，网络/代理问题不影响界面加载

### 变更
- 视频编码（videoCodec）默认值为「原始」（不重编码）

## [1.8.2] - 2026-08-24

### 新增
- 添加 GPL-3.0 许可证（`LICENSE`），关于页面显示许可证信息

## [1.8.1] - 2026-08-24

### 修复
- 扩展管理器未显示「未经测试」状态：KGM/QMC 插件现在在扩展管理器里显示「未经测试」而非「正常」

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

[2.0.2-dev.6]: https://github.com/114514901/universal-convert/releases/tag/v2.0.2-dev.6
[2.0.2-dev.5]: https://github.com/114514901/universal-convert/releases/tag/v2.0.2-dev.5
[2.0.2-dev.4]: https://github.com/114514901/universal-convert/releases/tag/v2.0.2-dev.4
[2.0.2-dev.3]: https://github.com/114514901/universal-convert/releases/tag/v2.0.2-dev.3
[2.0.2-dev.2]: https://github.com/114514901/universal-convert/releases/tag/v2.0.2-dev.2
[2.0.2-dev.1]: https://github.com/114514901/universal-convert/releases/tag/v2.0.2-dev.1
[2.0.1]: https://github.com/114514901/universal-convert/releases/tag/v2.0.1
[1.8.2]: https://github.com/114514901/universal-convert/releases/tag/v1.8.2
[1.8.1]: https://github.com/114514901/universal-convert/releases/tag/v1.8.1
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
