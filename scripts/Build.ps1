[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$repoRoot=Split-Path -Parent $PSScriptRoot
$dist=Join-Path $repoRoot 'dist'
New-Item -ItemType Directory -Path $dist -Force|Out-Null
$exe=Join-Path $dist 'UUDisplayOnDemand.exe'
if(Test-Path -LiteralPath $exe) {throw 'dist/UUDisplayOnDemand.exe already exists. Move the previous build before rebuilding.'}
# Build against Windows .NET Framework so the result needs no extra runtime.
$frameworkShell=Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
function Quote-PS([string]$value) {return "'"+$value.Replace("'","''")+"'"}
$native=Quote-PS (Join-Path $repoRoot 'src\Native.cs')
$program=Quote-PS (Join-Path $repoRoot 'src\Program.cs')
$output=Quote-PS $exe
$build="Add-Type -Path @($native,$program) -OutputAssembly $output -OutputType WindowsApplication -ReferencedAssemblies @('System.dll','System.Core.dll','System.Configuration.dll') -ErrorAction Stop"
& $frameworkShell -NoProfile -Command $build
if($LASTEXITCODE -ne 0) {throw 'C# compilation failed.'}
foreach($name in @('Install.ps1','Stop-Automation.cmd')) {Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $dist $name)}
Copy-Item -LiteralPath (Join-Path $repoRoot 'src\Native.cs') -Destination (Join-Path $dist 'Native.cs')
foreach($name in @('README.md','README.en.md')) {Copy-Item -LiteralPath (Join-Path $repoRoot $name) -Destination (Join-Path $dist $name)}
Get-FileHash -LiteralPath $exe -Algorithm SHA256|ForEach-Object {"$($_.Hash.ToLowerInvariant())  UUDisplayOnDemand.exe"}|Set-Content -LiteralPath (Join-Path $dist 'SHA256SUMS.txt') -Encoding ASCII
Write-Output ('Build ready: '+$dist)
