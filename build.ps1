# Builds dist\ClawdShelf.exe with the C# compiler that ships with Windows (.NET Framework 4.8).
# No SDK, no NuGet, no internet needed.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$fw = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$csc = Join-Path $fw 'csc.exe'
$wpf = Join-Path $fw 'WPF'
$build = Join-Path $root 'build'
$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force $build, $dist | Out-Null

function Invoke-Csc([string[]]$cscArgs) {
    $out = & $csc /nologo @cscArgs 2>&1 | Where-Object { $_ -notmatch 'warning CS1668' }
    $out | ForEach-Object { Write-Host $_ }
    if ($LASTEXITCODE -ne 0) { throw "csc failed" }
}

Write-Host '1/2 drawing the icon...'
Invoke-Csc @("/out:$build\MakeIcon.exe", '/r:System.Drawing.dll', "$root\tools\MakeIcon.cs", "$root\src\ClawdArt.cs")
& "$build\MakeIcon.exe" "$build\clawd.ico"

Write-Host '2/2 compiling ClawdShelf...'
$refs = @(
    "$wpf\PresentationCore.dll", "$wpf\PresentationFramework.dll", "$wpf\WindowsBase.dll", "$fw\System.Xaml.dll",
    'System.Drawing.dll', 'System.Windows.Forms.dll', 'System.Core.dll'
) | ForEach-Object { "/r:$_" }
$src = Get-ChildItem "$root\src\*.cs" | ForEach-Object { $_.FullName }
Invoke-Csc (@('/target:winexe', '/optimize+', '/platform:anycpu', '/nowarn:0168,0219',
    "/out:$dist\ClawdShelf.exe", "/win32icon:$build\clawd.ico", "/win32manifest:$root\src\app.manifest") + $refs + $src)

$size = [math]::Round((Get-Item "$dist\ClawdShelf.exe").Length / 1KB)
Write-Host "done -> dist\ClawdShelf.exe ($size KB)"
