# Ставит Старт-пак в %LOCALAPPDATA%\Programs\StartPack, кладёт ярлыки на рабочий стол и в меню «Пуск».
# Список программ (StartPack.json) и журнал при переустановке не трогаются — новая версия сама дополнит список.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$dest = Join-Path $env:LOCALAPPDATA 'Programs\StartPack'
New-Item -ItemType Directory -Force $dest | Out-Null

# Открытый Старт-пак закрываем штатно, иначе exe не заменить. Версия 3 живёт в трее — её закрываем полностью.
$running = Get-Process StartPack -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$dest*" }
$wasRunning = [bool]$running
# сначала вежливо: /exit (понимают версии с 3.1), потом — как раньше
if ($running) { Start-Process "$root\out\StartPack.exe" -ArgumentList '/exit' -Wait; foreach ($p in $running) { [void]$p.WaitForExit(5000) } }
$running = Get-Process StartPack -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$dest*" }
foreach ($p in $running) {
    if ($p.MainWindowHandle -ne [IntPtr]::Zero) { [void]$p.CloseMainWindow() }
    if (-not $p.WaitForExit(6000)) { Stop-Process -Id $p.Id -Force; $p.WaitForExit(5000) | Out-Null }
}

Copy-Item "$root\out\StartPack.exe" $dest -Force
Remove-Item "$dest\source" -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force "$dest\source\src", "$dest\source\assets" | Out-Null
Copy-Item "$root\src\*" "$dest\source\src" -Force
Copy-Item "$root\assets\*" "$dest\source\assets" -Force
Copy-Item "$root\build.ps1", "$root\make_icon.ps1", "$root\install.ps1", "$root\pack.ps1", "$root\CHANGELOG.md" "$dest\source" -Force

# Значок для ярлыков — отдельным файлом с новым именем: Windows кэширует значки по пути,
# и с прежним путём рабочий стол продолжал бы показывать старую картинку.
Get-ChildItem $dest -Filter 'StartPack-*.ico' | Remove-Item -Force
$ico = Join-Path $dest ("StartPack-" + (Get-Date -Format 'yyyyMMddHHmmss') + ".ico")
Copy-Item "$root\assets\app.ico" $ico

Add-Type -Namespace StartPackInstall -Name Shell -MemberDefinition @'
[DllImport("shell32.dll", CharSet = CharSet.Unicode)]
public static extern void SHChangeNotify(int eventId, uint flags, string item1, System.IntPtr item2);
'@

$sh = New-Object -ComObject WScript.Shell
function New-Lnk($path, $arguments, $desc) {
    if (Test-Path $path) { Remove-Item $path -Force }
    $sc = $sh.CreateShortcut($path)
    $sc.TargetPath = "$dest\StartPack.exe"
    $sc.Arguments = $arguments
    $sc.WorkingDirectory = $dest
    $sc.IconLocation = "$ico,0"
    $sc.Description = $desc
    $sc.Save()
    [StartPackInstall.Shell]::SHChangeNotify(0x2000, 0x0005, $path, [IntPtr]::Zero)   # SHCNE_UPDATEITEM: «этот ярлык изменился»
}

$desktop = [Environment]::GetFolderPath('Desktop')
$lnk = Join-Path $desktop 'Старт-пак.lnk'
New-Lnk $lnk '' 'Запустить zapret, прокси, Spotify, Telegram и Discord одним кликом'
$startMenu = Join-Path ([Environment]::GetFolderPath('Programs')) 'Старт-пак.lnk'
New-Lnk $startMenu '' 'Запустить zapret, прокси, Spotify, Telegram и Discord одним кликом'

# Ярлыки, созданные из настроек Старт-пака, — обновить им значок тоже.
$runAll = Join-Path $desktop 'Старт-пак — запустить всё.lnk'
if (Test-Path $runAll) { New-Lnk $runAll '/autorun' 'Запустить всё из Старт-пака одним двойным щелчком' }
$startup = Join-Path ([Environment]::GetFolderPath('Startup')) 'Старт-пак.lnk'
if (Test-Path $startup) { New-Lnk $startup '/autorun' 'Старт-пак: запустить всё' }

# Попросить Windows перечитать значки.
[StartPackInstall.Shell]::SHChangeNotify(0x08000000, 0, $null, [IntPtr]::Zero)   # SHCNE_ASSOCCHANGED
try { Start-Process -FilePath "$env:WINDIR\System32\ie4uinit.exe" -ArgumentList '-show' -WindowStyle Hidden -Wait } catch {}

# Если Старт-пак был запущен — вернуть его в трей (без окна и без запуска программ).
if ($wasRunning) { Start-Process "$dest\StartPack.exe" -ArgumentList '/tray' }

Write-Host "Установлено: $dest"
Write-Host "Ярлыки: $lnk; $startMenu"
