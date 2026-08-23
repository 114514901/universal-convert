# 更新日志（Changelog）

本项目遵循 [语义化版本 SemVer](https://semver.org/lang/zh-CN/)。所有值得注意的变更记录于此。

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
