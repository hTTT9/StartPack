# Собирает out\StartPack.exe встроенным в Windows компилятором C# (.NET Framework 4.x).
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { throw "Не найден компилятор: $csc" }
& "$root\make_icon.ps1" | Out-Null
New-Item -ItemType Directory -Force "$root\out" | Out-Null

$refs = 'System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll', 'System.Web.Extensions.dll',
        'System.IO.Compression.dll', 'System.IO.Compression.FileSystem.dll', 'Microsoft.CSharp.dll', 'Microsoft.VisualBasic.dll', 'System.ServiceProcess.dll'
$cscArgs = @('/nologo', '/target:winexe', '/codepage:65001', '/optimize+', '/warn:4',
          "/out:$root\out\StartPack.exe", "/win32icon:$root\assets\app.ico", "/win32manifest:$root\src\app.manifest", "/resource:$root\assets\app-256.png,StartPack.app-256.png") +
        ($refs | ForEach-Object { "/r:$_" }) + (Get-ChildItem "$root\src\*.cs" | ForEach-Object FullName)
& $csc @cscArgs
if ($LASTEXITCODE -ne 0) { throw "Сборка не удалась (код $LASTEXITCODE)" }
Write-Host "Готово: $root\out\StartPack.exe"
