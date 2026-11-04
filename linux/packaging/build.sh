#!/usr/bin/env bash
# Сборка Старт-пака для Linux: одна программа (self-contained) → AppImage, .deb, .rpm и SHA256SUMS.txt в linux/out.
# Запускается на Ubuntu (так делает GitHub Actions). Нужны: dotnet 8, dpkg-deb, rpmbuild, curl.
set -euo pipefail
cd "$(dirname "$0")/.."

VER=$(grep -oP '(?<=<Version>)[^<]+' StartPack.Linux.csproj)
OUT=out
rm -rf "$OUT"
mkdir -p "$OUT"

echo "== публикация $VER"
dotnet publish StartPack.Linux.csproj -c Release -r linux-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o "$OUT/publish"
BIN="$OUT/publish/startpack"
test -x "$BIN"

DESKTOP=packaging/startpack.desktop
ICON=../assets/app-256.png

echo "== AppImage"
APPDIR="$OUT/AppDir"
mkdir -p "$APPDIR/usr/bin"
cp "$BIN" "$APPDIR/usr/bin/startpack"
cp "$DESKTOP" "$APPDIR/startpack.desktop"
cp "$ICON" "$APPDIR/startpack.png"
cat > "$APPDIR/AppRun" <<'EOF'
#!/bin/sh
HERE="$(dirname "$(readlink -f "$0")")"
exec "$HERE/usr/bin/startpack" "$@"
EOF
chmod +x "$APPDIR/AppRun"
TOOL="$OUT/appimagetool"
curl -fsSL -o "$TOOL" https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage
chmod +x "$TOOL"
ARCH=x86_64 "$TOOL" --appimage-extract-and-run --no-appstream "$APPDIR" "$OUT/StartPack-x86_64.AppImage"
rm -f "$TOOL"

stage() {   # общий набор файлов для .deb и .rpm
  local root="$1"
  mkdir -p "$root/opt/startpack" "$root/usr/bin" "$root/usr/share/applications" "$root/usr/share/icons/hicolor/256x256/apps"
  cp "$BIN" "$root/opt/startpack/startpack"
  ln -sf /opt/startpack/startpack "$root/usr/bin/startpack"
  cp "$DESKTOP" "$root/usr/share/applications/startpack.desktop"
  cp "$ICON" "$root/usr/share/icons/hicolor/256x256/apps/startpack.png"
}

echo "== .deb"
DEB="$OUT/deb"
stage "$DEB"
mkdir -p "$DEB/DEBIAN"
cat > "$DEB/DEBIAN/control" <<EOF
Package: startpack
Version: $VER
Section: net
Priority: optional
Architecture: amd64
Maintainer: hTTT9 <239459885+hTTT9@users.noreply.github.com>
Depends: libc6, libfontconfig1, libx11-6, libice6, libsm6
Recommends: policykit-1 | pkexec, nftables, git, curl
Homepage: https://github.com/hTTT9/StartPack
Description: Старт-пак — zapret, прокси, VPN, Telegram и Discord одной кнопкой
 Запускает нужные программы одной кнопкой: zapret (обход блокировок Discord и YouTube),
 TG WS Proxy, Cloudflare WARP, Happ, Telegram, Discord и Spotify.
EOF
dpkg-deb --build --root-owner-group "$DEB" "$OUT/startpack_${VER}_amd64.deb"

echo "== .rpm"
RPMTOP="$(pwd)/$OUT/rpmbuild"
mkdir -p "$RPMTOP"/{BUILD,RPMS,SOURCES,SPECS,SRPMS,BUILDROOT}
STAGE="$(pwd)/$OUT/rpmstage"
stage "$STAGE"
cat > "$RPMTOP/SPECS/startpack.spec" <<EOF
Name:           startpack
Version:        $VER
Release:        1
Summary:        Старт-пак — zapret, прокси, VPN, Telegram и Discord одной кнопкой
License:        GPL-3.0-or-later
URL:            https://github.com/hTTT9/StartPack
BuildArch:      x86_64
AutoReqProv:    no
Requires:       fontconfig, libX11, libICE, libSM
Recommends:     polkit, nftables, git, curl
%global __os_install_post %{nil}
%global debug_package %{nil}

%description
Запускает нужные программы одной кнопкой: zapret (обход блокировок Discord и YouTube),
TG WS Proxy, Cloudflare WARP, Happ, Telegram, Discord и Spotify.

%install
mkdir -p %{buildroot}
cp -a $STAGE/. %{buildroot}/

%files
/opt/startpack/startpack
/usr/bin/startpack
/usr/share/applications/startpack.desktop
/usr/share/icons/hicolor/256x256/apps/startpack.png
EOF
rpmbuild --define "_topdir $RPMTOP" -bb "$RPMTOP/SPECS/startpack.spec"
cp "$RPMTOP"/RPMS/x86_64/startpack-*.rpm "$OUT/startpack-${VER}-1.x86_64.rpm"

echo "== контрольные суммы"
( cd "$OUT" && sha256sum StartPack-x86_64.AppImage "startpack_${VER}_amd64.deb" "startpack-${VER}-1.x86_64.rpm" > SHA256SUMS.txt && cat SHA256SUMS.txt )
rm -rf "$DEB" "$RPMTOP" "$STAGE" "$APPDIR"
echo "готово: $OUT"
