[CmdletBinding()]
param([string]$ServerExecutable=(Join-Path $env:ProgramFiles 'Netease\GameViewer\bin\GameViewerServer.exe'))
$ErrorActionPreference='Stop'
$taskName='UU-VirtualDisplay-OnDemand'
$installDir=Join-Path $env:ProgramData 'Codex\UUDisplayOnDemand'
$admin=([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if(-not $admin) {throw 'Run this installer from an administrator PowerShell window.'}
if(Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue) {throw 'The task already exists. Stop the existing installation first; nothing was replaced.'}
if(Test-Path -LiteralPath $installDir) {throw 'The install folder already exists. Review and move the previous installation before reinstalling.'}
$sourceExe=Join-Path $PSScriptRoot 'UUDisplayOnDemand.exe'
if(-not (Test-Path -LiteralPath $sourceExe)) {throw 'Run the installer from the generated dist folder after building.'}
$ServerExecutable=(Resolve-Path -LiteralPath $ServerExecutable).Path
$devices=@(Get-PnpDevice -Class Display|Where-Object {$_.FriendlyName -eq 'GameViewer Virtual Display Adapter'})
if($devices.Count -ne 1) {throw 'Expected exactly one GameViewer Virtual Display Adapter. No changes made.'}
$deviceId=$devices[0].InstanceId
Add-Type -Path (Join-Path $PSScriptRoot 'Native.cs')
[UUDisplayOnDemand.Native]::Configure($deviceId,$ServerExecutable)
$preflight=[UUDisplayOnDemand.Native]::Probe()
if($preflight.ServerProcessCount -lt 1) {throw 'Start UU remote and verify the GameViewerServer.exe path before installing.'}
$beforeEnabled=[UUDisplayOnDemand.Native]::DeviceEnabled()
New-Item -ItemType Directory -Path $installDir -Force|Out-Null
$ownerSid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$admins=[Security.Principal.SecurityIdentifier]::new('S-1-5-32-544')
$system=[Security.Principal.SecurityIdentifier]::new('S-1-5-18')
$user=[Security.Principal.SecurityIdentifier]::new($ownerSid)
$inherit=[Security.AccessControl.InheritanceFlags]'ContainerInherit, ObjectInherit'
$acl=[Security.AccessControl.DirectorySecurity]::new()
$acl.SetAccessRuleProtection($true,$false)
$acl.SetOwner($admins)
foreach($sid in @($admins,$system)) {$acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($sid,'FullControl',$inherit,'None','Allow'))}
$acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($user,'ReadAndExecute',$inherit,'None','Allow'))
Set-Acl -LiteralPath $installDir -AclObject $acl
foreach($name in @('UUDisplayOnDemand.exe','Native.cs','README.md','README.en.md')) {Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $installDir $name)}
$config=[xml]'<?xml version="1.0" encoding="utf-8"?><configuration><startup><supportedRuntime version="v4.0" sku=".NETFramework,Version=v4.0" /></startup><appSettings><add key="DeviceInstanceId" value="" /><add key="ServerPath" value="" /></appSettings></configuration>'
$config.configuration.appSettings.add[0].SetAttribute('value',$deviceId)
$config.configuration.appSettings.add[1].SetAttribute('value',$ServerExecutable)
$config.Save((Join-Path $installDir 'UUDisplayOnDemand.exe.config'))
[pscustomobject]@{Product='UUDisplayOnDemand';Created=(Get-Date).ToString('o');UserSid=$ownerSid;VirtualDisplayPreviouslyEnabled=$beforeEnabled}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $installDir 'installation.json') -Encoding UTF8
$action=New-ScheduledTaskAction -Execute (Join-Path $installDir 'UUDisplayOnDemand.exe') -WorkingDirectory $installDir
$trigger=New-ScheduledTaskTrigger -AtLogOn -User $ownerSid
$principal=New-ScheduledTaskPrincipal -UserId $ownerSid -LogonType Interactive -RunLevel Highest
$settings=New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1)
Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Description 'UU display on demand: enable the validated GameViewer virtual adapter for NVIDIA encoder sessions, disable 20 seconds after they end.'|Out-Null
Start-ScheduledTask -TaskName $taskName
Write-Output ('Installed: '+$installDir)
Write-Output ('State: '+(Join-Path $installDir 'status.json'))
