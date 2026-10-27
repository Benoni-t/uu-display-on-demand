[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$repoRoot=Split-Path -Parent $PSScriptRoot
$testDir=Join-Path $repoRoot ('build\tests-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testDir -Force|Out-Null
function Quote-PS([string]$value) {return "'"+$value.Replace("'","''")+"'"}
$inputs=@('src\Native.cs','src\EncoderActivity.cs','src\OnDemandDetector.cs','tests\DetectorTests.cs')|ForEach-Object {Quote-PS (Join-Path $repoRoot $_)}
$exe=Join-Path $testDir 'DetectorTests.exe'
$command='Add-Type -Path @('+($inputs -join ',')+') -OutputAssembly '+(Quote-PS $exe)+" -OutputType ConsoleApplication -ReferencedAssemblies @('System.dll','System.Core.dll') -ErrorAction Stop"
& (Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe') -NoProfile -Command $command
if($LASTEXITCODE -ne 0) {throw 'Test compilation failed.'}
& $exe
if($LASTEXITCODE -ne 0) {throw 'Detector tests failed.'}
