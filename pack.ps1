# Собирает архив для раздачи: out\StartPack-<версия>.zip (программа + README.txt)
# и кладёт копию на рабочий стол «Старт-пак <версия>.zip» (старые копии оттуда убирает).
# Рядом — out\SHA256SUMS.txt (контрольные суммы exe и архива для выпуска на GitHub).
# -NoDesktop — без копии на рабочий стол (так собирает GitHub Actions).
param([switch]$NoDesktop)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$exe = Join-Path $root 'out\StartPack.exe'
if (-not (Test-Path $exe)) { throw 'Сначала соберите программу: build.ps1' }
$ver = (Get-Item $exe).VersionInfo.FileVersion -replace '\.0$', '' -replace '\.0$', ''

$stage = Join-Path $root 'out\pack'
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force $stage | Out-Null
Copy-Item $exe $stage
$readme = @"
Старт-пак $ver
================

Запуск нужных программ одной кнопкой: zapret, TG WS Proxy, Spotify, Telegram, Discord, Happ, INCY, Koala Clash.

Как установить
  1. Откройте StartPack.exe (можно прямо из архива).
  2. Нажмите «Установить и открыть». Права администратора не нужны.
  3. Если Windows пишет «Система Windows защитила ваш компьютер» —
     нажмите «Подробнее», затем «Выполнить в любом случае».

Как пользоваться
  • «Запустить всё» — запускает всё, что включено переключателем на карточке.
  • ▶ на карточке — запустить одну программу, ■ — остановить.
  • Нет программы — «Установить» на карточке. Не получилось — «С сайта»:
    откроется официальная страница, установите как обычно.
  • «Запуск с Windows» внизу справа — всё запустится само при входе в Windows.
  • VPN (Happ, INCY, Koala) выключены — включите переключатель у нужного.
  • Справка — кнопка «?» или F1.

Обновить — откройте StartPack.exe новой версии, нажмите «Обновить и открыть». Список сохранится.
Удалить — «Настройки» → «Удалить Старт-пак» или «Параметры Windows» → «Приложения».
Если что-то не так — «⋯» → «Журнал и отчёт» → «Скопировать отчёт» и пришлите текст.
"@
[IO.File]::WriteAllText((Join-Path $stage 'README.txt'), $readme, (New-Object Text.UTF8Encoding $true))

$zip = Join-Path $root "out\StartPack-$ver.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)
Remove-Item $stage -Recurse -Force

$sums = foreach ($f in @($exe, $zip)) { (Get-FileHash $f -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + (Split-Path $f -Leaf) }
[IO.File]::WriteAllText((Join-Path $root 'out\SHA256SUMS.txt'), ($sums -join "`n") + "`n", (New-Object Text.UTF8Encoding $false))
Write-Host "Архив: $zip"
$sums | ForEach-Object { Write-Host "SHA-256: $_" }

if (-not $NoDesktop) {
    $desk = [Environment]::GetFolderPath('Desktop')
    Get-ChildItem $desk -Filter 'Старт-пак *.zip' -ErrorAction SilentlyContinue | Remove-Item -Force
    $deskZip = Join-Path $desk "Старт-пак $ver.zip"
    Copy-Item $zip $deskZip -Force
    Write-Host "Копия на рабочем столе: $deskZip"
}
