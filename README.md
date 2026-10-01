# CTL-472 快写连笔参数调节器

## 当前状态

2026-10-01：用户在 Xournal++ 中试写后反馈「目前暂时解决」。保留此版本作为后续维护基线，不继续提高参数。实际长期效果尚未验证。

本项目通过 Wacom 官方 PrefUtil.exe 备份、修改、恢复配置，扩展界面滑块之外的压力阈值。它不是独立驱动，也没有修改驱动二进制。

## 环境与有效参数

- Windows；Wacom CTL-472，USB VID_056A / PID_037A。
- Wacom 驱动版本：6.4.14-1。
- Xournal++：1.3.7，安装位置 `E:\SF\xou\bin\xournalpp.exe`。
- 压力满量程：2047。

| 参数 | 原始值 | 当前有效值 |
| --- | --- | --- |
| UpperPressureThreshold | 409 | 512（约 25%） |
| LowerPressureThreshold | 393 | 491（约 24%） |
| DoubleClickOnOff | true | false |
| PressureCurveControlPoint | 409 0 1739 266 2047 2047 | 保持原值 |
| WinUseInk | true | true |

当前参数已从驱动配置读回确认。压力阈值和双击识别同时发生变化，因此还不能单独归因于其中一个修改。阈值名称及本项目的按下/抬起解释需结合实际书写效果判断，保存成功不等于证明驱动内部全部输入路径都采用了这些参数。

## 使用与恢复

从 GitHub 首次下载时，不包含本机完整配置备份。请先安装官方驱动，在项目目录执行以下命令备份自己的配置，再打开工具；已有 `original.wacomprefs` 时不要覆盖它：

```powershell
if (-not (Test-Path .\original.wacomprefs)) {
    & 'C:\Program Files\Tablet\Wacom\PrefUtil.exe' /silent /backup (Join-Path (Get-Location) 'original.wacomprefs') | Out-Null
}
```

`known-good.json` 是参数记录，不是可直接导入的官方配置。`known-good.wacomprefs` 只在原电脑保存。

双击 `Open-Tuner.cmd` 打开界面。

- Press threshold：按下阈值百分比。
- Release threshold：抬起阈值百分比。
- Apply thresholds：备份当前完整配置，修改 CTL-472 的全局笔配置，关闭笔尖双击识别，导入并读回检查。导入会短暂重启驱动进程。
- Restore original settings：恢复 `original.wacomprefs`，即本次调整前的完整 Wacom 配置。此操作也会恢复备份时的其他 Wacom 设置。

要求 `0 <= 抬起阈值 < 按下阈值 <= 95%`。95% 是工具输入上限，不是推荐值或已验证的驱动支持上限。当前不要继续调大；过高可能丢失轻笔画。界面显示一位小数，再次应用可能发生整数舍入。

仅查看已保存参数，不修改配置：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\PenTuner.ps1 -Check
```

恢复本次用户确认有效的完整配置：

```powershell
& 'C:\Program Files\Tablet\Wacom\PrefUtil.exe' /silent /restore 'D:\WD\ChatGPT\maybe\pen-tuner\known-good.wacomprefs' | Out-Null
```

## 文件与版本保存

- `PenTuner.ps1`：调节器源代码。
- `Open-Tuner.cmd`：启动入口。
- `known-good.json`：本次有效参数及环境记录。
- `original.wacomprefs`：调整前的完整配置，必须保留。
- `known-good.wacomprefs`：用户反馈暂时解决后导出的完整配置，必须保留。
- `before-*.wacomprefs`、`applied-*.wacomprefs`：每次修改前和待导入的配置。
- `probe*.wacomprefs`、`verify.wacomprefs`：开发时的备份调用验证文件，保留作记录。

源代码用本地 Git 保存版本。完整配置可能包含本机路径和设备信息，仅保留在本机，不纳入 Git；迁移项目时需额外复制这些备份。

## 后续维护线索

原问题：快速写字时，用户明确抬笔，但两笔之间出现细连接线。用户反馈发生于 Xournal++，尚未完成其他软件的对照测试。

复发时先读取实际参数，确认驱动升级、控制面板调整或应用专属配置是否覆盖设置。随后记录 Xournal++ / 驱动版本，区分连笔和轻笔画丢失；每次只改一个变量。必要时采集压力和抬笔事件并做跨软件对照，再判断是否需要输入过滤或替代驱动。

实现限制：只支持本机这类配置结构下唯一的 CTL-472 全局笔配置；不会修改应用专属配置。原始 HID 报告、实际悬空压力、Windows Ink/Wintab 事件序列均未采集。GUI 目前为英文。官方工具失败会报错；读回值不符时尝试恢复本次修改前的备份。

开发中确认：PrefUtil 原生命令调用可用；最终采用 `/silent` 放在操作参数之前，避免恢复成功提示阻塞。备份导入及读回已验证，GUI 已启动；界面上的恢复按钮未做完整往返实测。

参考：

- [Wacom 配置工具命令行说明](https://developer-support.wacom.com/hc/en-us/articles/9354481821463-Run-the-Preferences-utility-from-the-command-line)
- [Wacom 高级笔设置：双击距离并非抬笔高度](https://101.wacom.com/UserHelp/en/AdvancedPenEraser_Con_Full.htm)
