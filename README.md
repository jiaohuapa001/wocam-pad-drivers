# 本仓库由codex维护
# 轻笔 / WacomLite 0.3

CTL-472 轻量控制面板，提供独立预设、配置恢复与笔事件诊断。需要 Windows 10/11 x64、.NET Framework 4.x 和兼容的官方 Wacom 驱动。

## v0.3.0 更新

2026-10-03 发布准备：从早期 PowerShell 调节器升级为轻笔原生 Windows 程序。

- 独立 EXE 与笔尖图标，中文界面，不再通过控制台启动。
- 并列显示编辑、已保存和驱动参数；支持预设迁移、应用前备份、失败回退及恢复有效预设。
- 新增试写画布、压力曲线、落笔/抬笔计数及 CSV/PNG 导出。
- 项目精简为程序、src、data、构建脚本和一份说明；本程序数据保存在 EXE 旁的 data。

下载本版本的 Windows x64 ZIP 后解压，双击 WacomLite.exe。仍需安装兼容的官方 Wacom 驱动，本版本仅适配 CTL-472。更新已有安装时保留 data。

配置逻辑与模拟回退已测试；真实数位笔快速抬笔、跨电脑和未来驱动兼容仍需实测。发布包不包含个人配置、本机备份或官方驱动二进制。

## 使用与目录

双击根目录 **WacomLite.exe**，带独立图标，无命令行窗口，关闭后不驻留。

| 文件或目录 | 用途 |
| --- | --- |
| WacomLite.exe | 唯一启动入口，本地构建 |
| README.md | 使用与维护说明 |
| Build.ps1 | 构建、打包 |
| src/ | C# 源码、测试、清单及默认预设 |
| data/ | 本程序所有数据，仅本地保存 |

另有隐藏的 `.git` 与 `.gitignore`。程序固定使用自身目录内的 `data`，不向 AppData 存放自己的数据；目录不可读写时明确报错。官方驱动的既有配置仍由官方工具管理。

## 设置与保护

表格显示编辑值、已保存/内置预设和驱动当前值；蓝色表示编辑值不同，黄色表示驱动偏离预设。

- **仅保存预设**只保存 JSON，不修改驱动。
- **保存并应用**先备份本机完整配置，再应用五项管理参数并读回检查；失败尝试回退。
- **导入预设**只载入界面，应用后才修改驱动。**导出预设**不包含本机路径和完整驱动配置。
- **确认试写有效**记录你实际试写满意的参数。**恢复有效预设**恢复该记录；尚无确认记录时采用最初快写预设。
- **撤销上次应用**恢复应用前的完整本机配置，包括其他官方设置；恢复前也会备份。

启动和运行期间约每 10 秒检查差异，不自动覆盖设置。配置存在不代表数位板当前在线。

数据位置：

- `data/preferred.json`：已保存预设。
- `data/verified.json`：最近一次成功应用并读回的预设。
- `data/confirmed.json`：用户确认有效的预设。
- `data/backups/`：本机回退文件。`initial/` 内保留最初的 `original.wacomprefs` 和 `known-good.wacomprefs`。
- `data/presets/`、`data/logs/`：导出预设、事件和笔迹的默认目录。
- `data/build/`：构建中间文件及便携发布包。
- `data/archive/`：旧文件和合并快照。

预设及导出目录在对应操作后生成。

## 试写与诊断

第二页显示笔迹、压力曲线、落笔/抬笔计数，支持 CSV 与 PNG 导出。输入来自 Windows WM_POINTER/GetPointerPenInfo/GetPointerPenInfoHistory，不是原始 USB/HID 数据，也不代表 Xournal++ 内部事件。

Windows 压力为 0–1024，CSV 的 -1 表示无压力数据。鼠标可试画但没有笔压，计数包含笔和鼠标。抬笔、取消、离开画布、捕获改变或窗口失活时结束笔画，不添加猜测性的压力过滤。

读取失败或历史回退意味着记录可能不完整。达到 50000 条事件后停止录制，请导出并清空后继续。关闭前自行导出需要保留的笔迹和事件。

## 迁移与升级

换电脑继续使用 CTL-472 时，先安装官方驱动并初始化，再携带 EXE 和独立 JSON 预设，导入后应用；也可将 preferred.json、confirmed.json 放到新程序的 data 中。

完整 .wacomprefs 备份只供原电脑回退，不作为跨电脑预设。升级工具时保留 data，覆盖原位置的 EXE。官方驱动升级后可检查并重新应用预设，未来驱动配置结构变化仍需适配。

## 有效参数与验证记录

2026-10-01，用户反馈 Xournal++ 快写细连线暂时解决。环境为 CTL-472（056A:037A）、Wacom 6.4.14-1、Xournal++ 1.3.7。

| 参数 | 调整前 | 当前有效值 |
| --- | --- | --- |
| 按下阈值 | 409 | 512 |
| 抬起阈值 | 393 | 491 |
| 满量程 | 2047 | 2047 |
| 笔尖双击 | 开 | 关 |
| Windows Ink | 开 | 开 |
| 压力曲线 | 409 0 1739 266 2047 2047 | 保持原值 |

25.012% / 23.986% 是整数阈值换算，过高可能丢轻笔画。阈值与双击识别同时修改，尚不能单独归因。

仅适配 CTL-472 全局配置，保留映射、侧键和应用专属配置。官方工具有引号路径限制，调用采用简单临时文件名和必要的 8.3 目录别名；别名不可用时需放在无空格的可写目录。

配置校验、换算、模拟应用和撤销回退、笔画分段、事件导出与上限测试已通过；真实官方备份接口、配置读回与界面已检查。真实笔压/抬笔、跨电脑迁移和未来驱动兼容仍需实测；新 EXE 的驱动写入路径主要完成模拟验证。

## 构建与维护

```powershell
# EXE 生成在根目录；发布包放到 data/build
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Build.ps1 -Package
# 可选原生测试，测试产物在 data 内
Start-Process .\WacomLite.exe -ArgumentList '--selftest', "$PWD\data\selftest.txt" -Wait
# 只读核对当前驱动
Start-Process .\WacomLite.exe -ArgumentList '--check', "$PWD\data\check.txt" -Wait
```

默认参数只维护 `src/fast-writing.json`，构建时嵌入 EXE，无需程序旁的重复副本。发布包仅包含 EXE 和本说明，不带个人配置、日志和备份。

旧脚本、启动器、重复说明和产物移至 `data/archive/legacy`，已跟踪的旧源码也可从 Git 历史找回。整理前构建产物和根目录备份合并至 `data/archive/previous-layout-20261001.zip`，逐文件核对过 SHA-256。自动审批阻止了删除旧文件，因此归档原件也保留在本地。

复发时先读回参数，再每次只调整一个变量，跨软件比较并保留事件记录。

参考：[官方配置工具](https://developer-support.wacom.com/hc/en-us/articles/9354481821463-Run-the-Preferences-utility-from-the-command-line)、[高级笔设置](https://101.wacom.com/UserHelp/en/AdvancedPenEraser_Con_Full.htm)、[Windows 笔事件](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-pointer_pen_info)。
