# 架构与限制 · Architecture and limits

## 中文

空闲时每 2 秒读取 Windows PDH 的 `GPU Engine` 编码活动，匹配经过完整路径核验的 UU 服务器进程。只在发现候选活动或已有确认连接时调用 NVIDIA 的 `nvmlDeviceGetEncoderSessions`，至少间隔 5 秒；健康空闲时 NVML 查询数为零。断开后释放 NVML。GPU 使用率只作为门控，不作为连接或断开的最终依据，因此静止画面不会直接导致断开。主动控制另一台电脑通常是解码画面，不需要开启本机虚拟显卡。

设备通过 Windows Configuration Manager API 操作。每次改变状态之前都会核验设备名称，只允许 UU 虚拟显示适配器；不启停 Intel 或 NVIDIA 物理显卡。使用非持久停用，重启后设备默认可重新启用。

`RemoteActive` 的准确含义是“检测到匹配的 NVIDIA 编码会话”，不等同于所有远程会话。编码器切换、软件主动销毁编码会话、驱动或 UU 更新，都需要重新验证。检测错误或计数器预热时返回未知，保留设备当前状态并重置断开缓冲；接口成功返回零个会话时，无法区分 Intel / 软件编码和未连接状态。状态文件包含版本、`DetectorState` 和累计 `NvmlQueries`；健康空闲状态下后者应保持不变。

程序与配置安装到管理员保护的目录，登录启动任务不保存用户密码。日志只记录状态、时间、进程号和错误，不记录设备账户或画面。代码不修改 UU / TeamViewer 的服务、自启或访问权限。

## English

While idle, Windows PDH `GPU Engine` counters are sampled every 2 seconds for UU server processes verified by full executable path. NVIDIA `nvmlDeviceGetEncoderSessions` is called only for candidate encoding activity or confirmed sessions, at intervals of at least 5 seconds. Healthy idle operation makes zero NVML queries; NVML is released after a disconnect. Utilization is only a gate, not the final connection/disconnection signal, so a static picture does not directly end a session. Controlling another computer generally decodes pictures locally and does not require this PC's virtual adapter.

Device changes use the Windows Configuration Manager APIs. The adapter name is checked before each change, allowing only the UU virtual display adapter. Intel and NVIDIA physical GPUs are not toggled. Disabling is nonpersistent across reboots.

`RemoteActive` means “a matching NVIDIA encoding session was observed,” not “every kind of remote connection.” Revalidate if the encoder changes, the application destroys an encoder, or after driver / UU updates. Query errors and counter warm-up return unknown, preserve the current adapter state, and reset the disconnect grace period. A successful zero-session response cannot distinguish Intel / software encoding from an idle connection. The status file includes the version, `DetectorState`, and cumulative `NvmlQueries`; the latter should remain constant during healthy idle operation.

The executable and configuration live in an administrator-protected directory. The logon task stores no user password. Logs contain state, timestamps, process IDs, and errors, not account details or pictures. The utility does not modify UU / TeamViewer services, startup entries, or access permissions.

## 接口参考 · API references

- [NVIDIA NVML device queries](https://docs.nvidia.com/deploy/nvml-api/latest/api/group__nvmlDeviceQueries.html)
- [Microsoft PDH formatted counter arrays](https://learn.microsoft.com/en-us/windows/win32/api/pdh/nf-pdh-pdhgetformattedcounterarrayw)
- [Microsoft CM_Enable_DevNode](https://learn.microsoft.com/en-us/windows/win32/api/cfgmgr32/nf-cfgmgr32-cm_enable_devnode)
- [Microsoft CM_Disable_DevNode](https://learn.microsoft.com/en-us/windows/win32/api/cfgmgr32/nf-cfgmgr32-cm_disable_devnode)
