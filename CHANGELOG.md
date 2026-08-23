# 更新日志（Changelog）

本项目遵循 [语义化版本 SemVer](https://semver.org/lang/zh-CN/)。所有值得注意的变更记录于此。

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

[1.2.1]: https://github.com/114514901/universal-convert/releases/tag/v1.2.1
[1.2.0]: https://github.com/114514901/universal-convert/releases/tag/v1.2.0
[1.1.2]: https://github.com/114514901/universal-convert/releases/tag/v1.1.2
[1.1.1]: https://github.com/114514901/universal-convert/releases/tag/v1.1.1
[1.1.0]: https://github.com/114514901/universal-convert/releases/tag/v1.1.0
[1.0.0]: https://github.com/114514901/universal-convert/releases/tag/v1.0.0
