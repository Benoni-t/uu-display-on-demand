[CmdletBinding()]
param([string]$OutputDirectory)
$ErrorActionPreference='Stop'
$repoRoot=Split-Path -Parent $PSScriptRoot
$dist=if($OutputDirectory) {[IO.Path]::GetFullPath($OutputDirectory)} else {Join-Path $repoRoot 'dist'}
New-Item -ItemType Directory -Path $dist -Force|Out-Null
$exe=Join-Path $dist 'UUDisplayOnDemand.exe'
if(Test-Path -LiteralPath $exe) {throw 'The output executable already exists. Choose a new -OutputDirectory for this build.'}
# Build against Windows .NET Framework so the result needs no extra runtime.
$frameworkShell=Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
function Quote-PS([string]$value) {return "'"+$value.Replace("'","''")+"'"}
$sources=@('Native.cs','EncoderActivity.cs','OnDemandDetector.cs','Program.cs')
$inputs=($sources|ForEach-Object {Quote-PS (Join-Path $repoRoot ('src\'+$_))}) -join ','
$output=Quote-PS $exe
$build="Add-Type -Path @($inputs) -OutputAssembly $output -OutputType WindowsApplication -ReferencedAssemblies @('System.dll','System.Core.dll','System.Configuration.dll') -ErrorAction Stop"
& $frameworkShell -NoProfile -Command $build
if($LASTEXITCODE -ne 0) {throw 'C# compilation failed.'}
foreach($name in @('Install.ps1','Update.ps1','Stop-Automation.cmd')) {Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $dist $name)}
foreach($name in $sources) {Copy-Item -LiteralPath (Join-Path $repoRoot ('src\'+$name)) -Destination (Join-Path $dist $name)}
foreach($name in @('README.md','README.en.md')) {Copy-Item -LiteralPath (Join-Path $repoRoot $name) -Destination (Join-Path $dist $name)}
Get-FileHash -LiteralPath $exe -Algorithm SHA256|ForEach-Object {"$($_.Hash.ToLowerInvariant())  UUDisplayOnDemand.exe"}|Set-Content -LiteralPath (Join-Path $dist 'SHA256SUMS.txt') -Encoding ASCII
Write-Output ('Build ready: '+$dist)
