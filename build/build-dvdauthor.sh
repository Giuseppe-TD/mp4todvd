#!/usr/bin/env bash
# Cross-compila dvdauthor.exe (win64, statico) con mingw-w64. Output: build/out/dvdauthor.exe
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
W="${TMPDIR:-/tmp}/dvdauthor-build"; OUT="$HERE/out"; PFX="$W/prefix"
rm -rf "$W" "$OUT"; mkdir -p "$W" "$OUT" "$PFX"
cd "$W"

echo "== libxml2 (statico, minimale)"
curl -fsSL -o libxml2.tar.xz https://download.gnome.org/sources/libxml2/2.12/libxml2-2.12.9.tar.xz
tar xJf libxml2.tar.xz
( cd libxml2-2.12.9
  ./configure --host=x86_64-w64-mingw32 --prefix="$PFX" --disable-shared --enable-static \
    --without-python --without-zlib --without-lzma --without-iconv --without-http --without-ftp >/dev/null
  make -j"$(nproc)" >/dev/null
  make install >/dev/null )

echo "== dvdauthor 0.7.2"
curl -fsSL -o dvdauthor.tar.gz https://deb.debian.org/debian/pool/main/d/dvdauthor/dvdauthor_0.7.2.orig.tar.gz
tar xzf dvdauthor.tar.gz
cd dvdauthor-0.7.2
patch -p0 < "$HERE/dvdauthor-mingw.patch"
cp "$HERE/config.h" src/config.h
cd src
flex -s -B -Cem -o dvdvml.c -Pdvdvm dvdvml.l
bison -o dvdvmy.c -d -p dvdvm dvdvmy.y
x86_64-w64-mingw32-gcc -O2 -I. -I"$PFX/include/libxml2" -DLIBXML_STATIC \
  -static -o "$OUT/dvdauthor.exe" \
  dvdauthor.c dvdcompile.c dvdvml.c dvdvmy.c dvdifo.c dvdvob.c dvdpgc.c dvdcli.c readxml.c conffile.c compat.c \
  -L"$PFX/lib" -lxml2 -lws2_32
x86_64-w64-mingw32-strip "$OUT/dvdauthor.exe"
ls -la "$OUT/dvdauthor.exe"
echo "== OK"
