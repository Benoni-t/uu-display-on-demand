# UU Display On Demand · UU 虚拟显卡按需控制

[中文](README.md) | **English**

A small Windows utility for NetEase UU Remote. It enables the UU virtual display adapter while UU is encoding a remote picture, then disables it about 20 seconds after encoding ends, supporting ordinary remote access, Super Screen, and local dual-monitor use.

It originated from a dual-GPU laptop that became sluggish after connecting a second physical monitor. Disabling UU's background/virtual-display components restored smoothness in a comparison test. With only the virtual adapter disabled, ordinary remote access still worked, but Super Screen did not. The utility switches that adapter on demand.

## Behavior

- Reads Windows GPU encoder counters every 2 seconds and verifies `GameViewerServer.exe` by its full path. Healthy idle polls make no NVIDIA NVML calls.
- Uses NVML to confirm candidate UU encoding activity, then rechecks confirmed sessions at intervals of at least 5 seconds. A static remote picture is not treated as disconnected just because GPU activity falls.
- Enables the UU virtual adapter when a matching picture-encoding session appears.
- Waits about 20 seconds without a matching session before disabling the adapter, allowing short reconnects.
- On detection failure, preserves the adapter's current state rather than treating the error as a disconnect.
- Leaves UU Remote and TeamViewer services and startup settings alone. Only an adapter whose name exactly matches `GameViewer Virtual Display Adapter` can be changed.

This targets **UU using NVIDIA hardware encoding**. It is not a universal connection detector. Intel / AMD / software encoding, file transfers, and connections without picture encoding cannot reliably be inferred from this signal. Revalidate after changing the encoder or updating UU. This does not repair the underlying driver or promise to solve every multi-monitor slowdown.

## Requirements

- Windows 10/11 x64, Windows PowerShell 5.1, and the system .NET Framework.
- UU Remote and its virtual display driver installed, with UU running.
- An NVIDIA GPU / driver that supports the NVML active encoder session query; `nvml.dll` in the system directory.
- Working Windows `GPU Engine` performance counters.
- Administrator permission for installation. The task runs at the current user's interactive logon with highest privileges; no password is stored.

## Build and install

Clone or download this repository and run in PowerShell from its root:

```powershell
.\scripts\Build.ps1
```

Open an **administrator PowerShell** window in the repository and run:

```powershell
.\dist\Install.ps1
```

For a nonstandard UU installation, specify the server executable:

```powershell
.\dist\Install.ps1 -ServerExecutable 'D:\Apps\GameViewer\bin\GameViewerServer.exe'
```

The build downloads no dependencies and needs no additional .NET SDK. The installer discovers the single matching UU adapter and writes its instance ID and server path to a local `.exe.config`. It refuses to replace an existing task or installation directory.

Install directory: `%ProgramData%\Codex\UUDisplayOnDemand`. Logon task: `UU-VirtualDisplay-OnDemand`. Administrators and SYSTEM can write the protected program/configuration directory; the current user can read it. `status.json` records the current state, and `events.log` keeps a bounded event history.

For an existing installation, run `.\dist\Update.ps1` in administrator PowerShell. It validates the task, backs up the old runtime/configuration, preserves the logon task, and rolls back if the startup check fails. Use `-OutputDirectory .\build\v0.2.0` when building into a different empty directory.

## Verify and stop

1. Connect to this PC normally from another device. Check that `RemoteActive` and `VirtualDisplayEnabled` become `true`, then select Super Screen.
2. Disconnect and wait about 30 seconds. Confirm that the virtual adapter is disabled and local dual-monitor use is smooth.
3. Check that ordinary UU remote access still works.

To stop automation and enable the virtual adapter again, run `Stop-Automation.cmd` in the build output and accept the Windows administrator prompt. It stops and removes this utility's task, retains the installation folder for inspection, and leaves UU services and startup settings unchanged.

The device disable does not use a cross-reboot persistence flag. The controller manages it again after logon. This is a user-logon task, not a service that runs before login.

## Validation scope

The original implementation was tested on 2026-10-26 with Windows 11, an Intel i7-11800H / UHD Graphics, an RTX 3050 Laptop GPU, and UU 4.42.1. A tablet connection automatically enabled the adapter, Super Screen worked, and disconnecting automatically disabled the adapter and restored smooth local dual-monitor use. The repository version makes the adapter ID and server path installation-specific and has been build-checked; other hardware has not been field-tested.

**v0.2.0 correction:** A later local comparison found that pausing the original two-second NVML polling substantially reduced intermittent Zotero stutter. This version gates idle detection with Windows counters and releases NVML after a disconnect. After deployment on the same machine on 2026-10-27, the user reported that intermittent Zotero stutter disappeared with the new helper running; a remote encoding session was observed automatically enabling the adapter. Offline state-machine tests cover zero-query idle operation, static remote pictures, disconnects, query failures, and reconnect rate limiting. Revalidate on each target system using the steps above.

## Source and data

- `src/Native.cs`: NVML queries, process identity checks, and Windows device APIs.
- `src/EncoderActivity.cs` and `src/OnDemandDetector.cs`: Windows counter gate and session state machine.
- `src/Program.cs`: background control, disconnect grace period, state recording, and stop command.
- `scripts/`: build, test, install, update, and stop helpers. Run offline tests with `.\scripts\Test.ps1`.
- [Architecture and limits / 架构与限制](docs/architecture.md).

The utility does not read verification codes, credentials, or picture content, and sends no telemetry. No NetEase or NVIDIA drivers/binaries are bundled. This is an independent third-party utility.
