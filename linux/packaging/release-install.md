
## Как установить

**Ubuntu, Debian, Linux Mint** — скачайте `startpack_{ver}_amd64.deb` и откройте его двойным щелчком (откроется установка программ) или в терминале, в папке с файлом:

```
sudo apt install ./startpack_{ver}_amd64.deb
```

**Fedora и РЕД ОС** — скачайте `startpack-{ver}-1.x86_64.rpm` и в терминале, в папке с файлом:

```
sudo dnf install ./startpack-{ver}-1.x86_64.rpm
```

**Любой другой Linux** — скачайте `StartPack-x86_64.AppImage`, сделайте его исполняемым и запустите:

```
chmod +x StartPack-x86_64.AppImage
./StartPack-x86_64.AppImage
```

Если AppImage не запускается (в системе нет FUSE) — `./StartPack-x86_64.AppImage --appimage-extract-and-run`.

После установки «Старт-пак» появится в меню программ. Когда программа ставит или запускает zapret, система спросит пароль администратора — это нормально: zapret работает как системная служба.

**Удалить:** `sudo apt remove startpack` (Ubuntu, Debian) или `sudo dnf remove startpack` (Fedora, РЕД ОС), AppImage — просто удалить файл.

**Что-то не так?** Пришлите журнал программы — файл `~/.local/share/startpack/startpack.log` — и название системы с версией.
