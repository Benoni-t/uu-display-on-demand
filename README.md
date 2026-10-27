# UU 虚拟显卡按需控制 · UU Display On Demand

**中文** | [English](README.en.md)

一个用于 Windows 和网易 UU远程的轻量工具：远程画面传输时开启 UU 虚拟显卡，结束约 20 秒后关闭，让普通远程、超级屏和本地双屏使用可以共存。

起因是在一台双显卡笔记本上，接入第二块物理屏幕后出现整机卡顿。对照测试中停用 UU 的后台/虚拟显示组件后恢复流畅。随后验证：只停用虚拟显卡时普通远程仍能连接，但超级屏不可用，因此采用按需切换。

## 行为

- 每 2 秒读取 Windows GPU 编码计数器，按完整路径核验 `GameViewerServer.exe`；健康空闲状态不调用 NVIDIA NVML。
- 出现 UU 编码活动时才用 NVML 确认会话；确认连接后至少间隔 5 秒复查，静止画面不会仅因 GPU 使用率下降而断开。
- 检测到 UU 画面编码会话时开启虚拟显卡；短暂重连有 20 秒缓冲。
- 没有相关编码会话约 20 秒后停用虚拟显卡。
- 检测失败时保持虚拟显卡的当前状态，不把错误当成断开。
- 保留 UU远程、TeamViewer 的服务和自启；仅操作名称完全匹配的 `GameViewer Virtual Display Adapter`。

这是针对 **UU 使用 NVIDIA 硬件编码** 的方案，不是通用的远程连接检测器。使用 Intel / AMD / 软件编码、文件传输会话或没有画面编码的连接，不能据此可靠判断。若更换编码方式或更新 UU，请重新验证。它不修复驱动本身，也不保证解决所有双屏卡顿。

## 环境要求

- Windows 10/11 x64、Windows PowerShell 5.1 和系统 .NET Framework。
- 已安装并运行 UU远程及其虚拟显示驱动。
- 支持 NVML 活动编码会话查询的 NVIDIA GPU / 驱动；`nvml.dll` 位于系统目录。
- 可用的 Windows `GPU Engine` 性能计数器。
- 以管理员权限安装。启动任务使用当前用户的交互式登录和最高权限，不保存密码。

## 构建与安装

克隆或下载本仓库，在仓库目录用 PowerShell 执行：

```powershell
.\scripts\Build.ps1
```

然后打开**管理员 PowerShell**，在仓库目录执行：

```powershell
.\dist\Install.ps1
```

如果 UU 安装在其他位置，可明确指定服务器程序：

```powershell
.\dist\Install.ps1 -ServerExecutable 'D:\Apps\GameViewer\bin\GameViewerServer.exe'
```

构建不下载依赖，也不需要安装额外的 .NET SDK。安装器自动识别唯一的 UU 虚拟显卡实例，并将实例 ID 和程序路径写入本机的 `.exe.config`。如果已有同名任务或安装目录，会停止并提示，不覆盖已有安装。

安装目录：`%ProgramData%\Codex\UUDisplayOnDemand`。登录启动任务：`UU-VirtualDisplay-OnDemand`。配置和程序仅允许管理员及 SYSTEM 写入，当前用户可以读取。状态保存在 `status.json`，有限大小的事件日志保存在 `events.log`。

已有本工具的安装时，在管理员 PowerShell 运行 `.\dist\Update.ps1`。更新器会核验已有任务，备份旧程序和配置，保留登录任务；启动检查失败时回滚。可使用 `-OutputDirectory .\build\v0.2.0` 将新构建放入其他空目录。

## 验证与停止

1. 先用另一台设备普通远程连接本机，确认状态文件中 `RemoteActive` 和 `VirtualDisplayEnabled` 为 `true`，再启用超级屏。
2. 断开连接并等待约 30 秒，确认虚拟显卡关闭、本地双屏流畅。
3. 核实 UU 的正常远程连接仍可用。

停止自动切换并重新启用虚拟显卡：运行构建目录中的 `Stop-Automation.cmd`，接受 Windows 管理员提示。它会停止并删除本工具的启动任务，保留安装目录供检查，UU 原有服务和自启设置不变。

该停用不使用跨重启持久标志；控制器登录后再次管理设备。启动任务是在用户登录时运行，不是开机前的系统服务。

## 已验证范围

原始实现于 2026-10-26 在 Windows 11、Intel i7-11800H / UHD Graphics、RTX 3050 Laptop GPU、UU 4.42.1 上现场验证：平板连接自动开启虚拟显卡，超级屏可用，断开后自动关闭且本地双屏恢复流畅。仓库版将设备 ID 和程序路径改为安装时配置，构建已检查；尚未在其他硬件上做现场测试。

**v0.2.0 修正**：后续本地对照发现，暂停旧版每 2 秒一次的 NVML 轮询后，Zotero 的间歇卡顿明显减少。因此新版使用 Windows 计数器作为空闲门控，并在断开后释放 NVML。2026-10-27 在同一台机器部署后，用户确认新版运行时 Zotero 的偶发卡顿消失；已观察到远程编码会话自动开启虚拟显卡。检测状态机的离线测试覆盖空闲零查询、静止远程画面、断开、查询错误和重连节流；具体硬件仍应按上面的步骤验证。

## 源码与数据

- `src/Native.cs`：NVML 会话查询、进程路径核验和 Windows 设备接口。
- `src/EncoderActivity.cs`、`src/OnDemandDetector.cs`：Windows 计数器门控和会话状态机。
- `src/Program.cs`：后台控制、断开缓冲、状态记录和停止入口。
- `scripts/`：构建、测试、安装、更新和停止工具；离线测试命令为 `.\scripts\Test.ps1`。
- [架构与限制 / Architecture and limits](docs/architecture.md)。

程序不读取远程验证码、账户凭据或画面内容，不发送遥测。不附带网易或 NVIDIA 的驱动/二进制；这是独立的第三方工具。
