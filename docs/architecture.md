# 架构与限制 · Architecture and limits

## 中文

连接状态使用 NVIDIA 的 `nvmlDeviceGetEncoderSessions`，匹配经过完整路径核验的 UU 服务器进程，而不是根据 GPU 使用率或用户是否移动鼠标判断。对本机来说，主动控制另一台电脑通常是解码画面，不需要开启本机虚拟显卡；本方案主要跟踪本机被控制时的画面编码。

设备通过 Windows Configuration Manager API 操作。每次改变状态之前都会核验设备名称，只允许 UU 虚拟显示适配器；不启停 Intel 或 NVIDIA 物理显卡。使用非持久停用，重启后设备默认可重新启用。

`RemoteActive` 的准确含义是“检测到匹配的 NVIDIA 编码会话”，不等同于所有远程会话。编码器切换、静止画面时软件主动销毁编码会话、驱动或 UU 更新，都需要重新验证。检测接口报错会尝试开启虚拟显卡，但接口成功返回零个会话时，无法区分 Intel / 软件编码和未连接状态。

程序与配置安装到管理员保护的目录，登录启动任务不保存用户密码。日志只记录状态、时间、进程号和错误，不记录设备账户或画面。代码不修改 UU / TeamViewer 的服务、自启或访问权限。

## English

The signal comes from NVIDIA `nvmlDeviceGetEncoderSessions`, matched to a UU server process verified by its full executable path. It does not infer a session from GPU utilization or mouse activity. Controlling another computer generally decodes pictures locally and does not require this PC's virtual adapter; the primary target is this PC being controlled and encoding its screen.

Device changes use the Windows Configuration Manager APIs. The adapter name is checked before each change, allowing only the UU virtual display adapter. Intel and NVIDIA physical GPUs are not toggled. Disabling is nonpersistent across reboots.

`RemoteActive` means “a matching NVIDIA encoding session was observed,” not “every kind of remote connection.” Revalidate if the encoder changes, if the application destroys an encoder on static pictures, or after driver / UU updates. A query error attempts to enable the adapter, but a successful zero-session response cannot distinguish Intel / software encoding from an idle connection.

The executable and configuration live in an administrator-protected directory. The logon task stores no user password. Logs contain state, timestamps, process IDs, and errors, not account details or pictures. The utility does not modify UU / TeamViewer services, startup entries, or access permissions.

## 接口参考 · API references

- [NVIDIA NVML device queries](https://docs.nvidia.com/deploy/nvml-api/latest/api/group__nvmlDeviceQueries.html)
- [Microsoft CM_Enable_DevNode](https://learn.microsoft.com/en-us/windows/win32/api/cfgmgr32/nf-cfgmgr32-cm_enable_devnode)
- [Microsoft CM_Disable_DevNode](https://learn.microsoft.com/en-us/windows/win32/api/cfgmgr32/nf-cfgmgr32-cm_disable_devnode)
