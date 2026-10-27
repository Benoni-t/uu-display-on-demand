[CmdletBinding()]
param([string]$ServerExecutable,[string]$ResultPath)
$ErrorActionPreference='Stop'
$taskName='UU-VirtualDisplay-OnDemand'
$installDir=Join-Path $env:ProgramData 'Codex\UUDisplayOnDemand'
$runtimeNames=@('UUDisplayOnDemand.exe','Native.cs','EncoderActivity.cs','OnDemandDetector.cs','Program.cs','README.md','README.en.md')
$configName='UUDisplayOnDemand.exe.config'
$backup=$null;$changed=$false;$wasRunning=$false;$existing=@{}
function Save-Result($value) {if($ResultPath) {$value|ConvertTo-Json -Depth 4|Set-Content -LiteralPath $ResultPath -Encoding UTF8}}
try {
 $admin=([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
 if(-not $admin) {throw 'Run this updater from an administrator PowerShell window.'}
 $marker=Get-Content -LiteralPath (Join-Path $installDir 'installation.json') -Raw|ConvertFrom-Json
 if($marker.Product -ne 'UUDisplayOnDemand') {throw 'Unrecognized installation. Nothing replaced.'}
 $task=Get-ScheduledTask -TaskName $taskName
 $targetExe=Join-Path $installDir 'UUDisplayOnDemand.exe'
 if(@($task.Actions).Count -ne 1 -or $task.Actions[0].Execute -ne $targetExe -or $task.Actions[0].Arguments) {throw 'The existing task does not match this installation.'}
 foreach($name in $runtimeNames) {if(-not (Test-Path -LiteralPath (Join-Path $PSScriptRoot $name) -PathType Leaf)) {throw ('Incomplete build: '+$name)}}
 $expected=((Get-Content -LiteralPath (Join-Path $PSScriptRoot 'SHA256SUMS.txt') -Raw).Trim() -split '\s+')[0]
 if((Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'UUDisplayOnDemand.exe')).Hash -ne $expected) {throw 'Build checksum mismatch.'}
 $configPath=Join-Path $installDir $configName
 if(Test-Path -LiteralPath $configPath) {
  $config=[xml](Get-Content -LiteralPath $configPath -Raw)
  $deviceId=($config.configuration.appSettings.add|Where-Object {$_.key -eq 'DeviceInstanceId'}).value
  if(-not $ServerExecutable) {$ServerExecutable=($config.configuration.appSettings.add|Where-Object {$_.key -eq 'ServerPath'}).value}
 } else {
  $devices=@(Get-PnpDevice -Class Display|Where-Object {$_.FriendlyName -eq 'GameViewer Virtual Display Adapter'})
  if($devices.Count -ne 1) {throw 'Expected exactly one UU virtual adapter.'}
  $deviceId=$devices[0].InstanceId
  $config=[xml]'<?xml version="1.0" encoding="utf-8"?><configuration><startup><supportedRuntime version="v4.0" sku=".NETFramework,Version=v4.0" /></startup><appSettings><add key="DeviceInstanceId" value="" /><add key="ServerPath" value="" /></appSettings></configuration>'
 }
 if(-not $ServerExecutable) {$ServerExecutable=Join-Path $env:ProgramFiles 'Netease\GameViewer\bin\GameViewerServer.exe'}
 $ServerExecutable=(Resolve-Path -LiteralPath $ServerExecutable).Path
 Add-Type -Path (Join-Path $PSScriptRoot 'Native.cs')
 Add-Type -Path (Join-Path $PSScriptRoot 'EncoderActivity.cs')
 [UUDisplayOnDemand.Native]::Configure($deviceId,$ServerExecutable)
 $null=[UUDisplayOnDemand.Native]::DeviceEnabled()
 if([UUDisplayOnDemand.Native]::GetServerProcessIds().Count -lt 1) {throw 'Start UU before updating.'}
 $counter=[UUDisplayOnDemand.GpuEncoderActivity]::new()
 try {Start-Sleep -Seconds 2;if(-not $counter.Read([UUDisplayOnDemand.Native]::GetServerProcessIds()).Ready) {throw 'Windows GPU counters are not ready.'}} finally {$counter.Dispose()}
 $backup=Join-Path $installDir ('backups\'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'-'+[guid]::NewGuid().ToString('N').Substring(0,8))
 New-Item -ItemType Directory -Path $backup -Force|Out-Null
 foreach($name in @($runtimeNames)+@($configName)) {
  $path=Join-Path $installDir $name;$existing[$name]=Test-Path -LiteralPath $path
  if($existing[$name]) {Copy-Item -LiteralPath $path -Destination (Join-Path $backup $name)}
 }
 $wasRunning=$task.State -eq 'Running'
 Stop-ScheduledTask -TaskName $taskName
 for($attempt=0;$attempt -lt 30;$attempt++) {
  $running=@(Get-Process -Name UUDisplayOnDemand -ErrorAction SilentlyContinue|Where-Object {$_.Path -eq $targetExe})
  if($running.Count -eq 0) {break};Start-Sleep -Milliseconds 500
 }
 if($running.Count -gt 0) {throw 'The existing helper did not stop. No files replaced.'}
 $changed=$true
 foreach($name in $runtimeNames) {Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $installDir $name) -Force}
 ($config.configuration.appSettings.add|Where-Object {$_.key -eq 'DeviceInstanceId'}).SetAttribute('value',$deviceId)
 ($config.configuration.appSettings.add|Where-Object {$_.key -eq 'ServerPath'}).SetAttribute('value',$ServerExecutable)
 $config.Save($configPath)
 Start-ScheduledTask -TaskName $taskName
 $started=$false
 for($attempt=0;$attempt -lt 30;$attempt++) {
  Start-Sleep -Seconds 1
  try {
   $state=Get-Content -LiteralPath (Join-Path $installDir 'status.json') -Raw|ConvertFrom-Json
   if($state.Version -eq '0.2.0' -and (Get-Date $state.TimeUtc) -gt (Get-Date).AddSeconds(-15) -and $state.Health -eq 'OK') {$started=$true;break}
  } catch { }
 }
 if(-not $started) {throw 'Updated helper did not report a healthy state. Rolling back.'}
 Save-Result ([pscustomobject]@{Success=$true;Version='0.2.0';Backup=$backup;State=$state})
 Write-Output ('Updated to v0.2.0. Previous runtime: '+$backup)
} catch {
 $failure=$_.Exception.Message
 if($changed) {
  try {
   Stop-ScheduledTask -TaskName $taskName
   Start-Sleep -Seconds 1
   foreach($name in @($runtimeNames)+@($configName)) {
    $target=Join-Path $installDir $name
    if($existing[$name]) {Copy-Item -LiteralPath (Join-Path $backup $name) -Destination $target -Force}
    elseif(Test-Path -LiteralPath $target) {Remove-Item -LiteralPath $target -Force}
   }
   if($wasRunning) {Start-ScheduledTask -TaskName $taskName}
  } catch {$failure+=' Rollback error: '+$_.Exception.Message}
 }
 Save-Result ([pscustomobject]@{Success=$false;Error=$failure;Backup=$backup})
 throw $failure
}
