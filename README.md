# BatteryPilot / 续航助手

BatteryPilot 是一个面向 Windows 11 笔记本的本地电量监测与可恢复电源调度工具。它显示整机放电功率、CPU/内存/I/O 活跃进程和续航趋势，并在用户主动开启后复制当前电源方案，只修改副本中的电池设置。

## 安装

从 [GitHub Releases](https://github.com/NiuVector/BatteryPilot/releases/latest) 下载 `BatteryPilot-Setup-*-x64.exe`。安装器采用每用户安装，不要求管理员权限；开始菜单快捷方式默认创建，桌面快捷方式可选。

## 适配范围

- 正式支持：Windows 11 22H2 或更新版本、x64 处理器、.NET Framework 4.8/4.8.1。
- 可用功能取决于设备：电池 WMI 传感器、CPPC/EPP 电源设置、固件和 OEM 驱动。
- EPP 不可用时：继续使用设备支持的关屏策略，并在设备页明确显示能力。
- 电池传感器缺少字段时：界面显示不可用，不影响进程监测。
- Acrylic 不可用时：自动回退为实色背景。
- 当前不提供：32 位、ARM64 原生版本、macOS、Linux。

## 安全恢复

应用创建恢复记录 `%LOCALAPPDATA%\BatteryPilot\recovery.txt`。正常退出会恢复原电源方案。安装器升级和卸载还会调用：

```text
BatteryPilot.exe --restore-and-exit
```

该命令无界面执行恢复，成功返回 0；程序仍在运行时返回 2；恢复失败返回 1，并把详情保存为 `restore-error.txt`。

## 构建

在 Windows 11 上运行：

```powershell
.\build.ps1
```

也可以使用 Visual Studio 或带 .NET Framework 4.8 Developer Pack 的 MSBuild 构建 `BatteryPilot.csproj`。构建 Setup 需要 Inno Setup 6：

```powershell
.\build-release.ps1
```

生成文件位于 `dist\BatteryPilot-Setup-3.1.0-x64.exe`。

## 数据与隐私

所有采样与设置保存在本机。应用没有联网、账号、遥测或数据上传功能。导出的 CSV 可能包含进程名称，分享前请自行检查。

## 许可证

MIT，见 `LICENSE`。
