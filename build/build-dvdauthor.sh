#!/usr/bin/env bash
# Cross-compila dvdauthor.exe e spumux.exe (win64, statici) con mingw-w64. Output: build/out/
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
W="${TMPDIR:-/tmp}/dvdauthor-build"; OUT="$HERE/out"; PFX="$W/prefix"
rm -rf "$W" "$OUT"; mkdir -p "$W" "$OUT" "$PFX"
cd "$W"
H=x86_64-w64-mingw32

fetch() { # fetch <out> <url...>  (prova più mirror)
  local out="$1"; shift
  for u in "$@"; do curl -fsSL --retry 3 -o "$out" "$u" && return 0; done
  echo "download fallito: $out" >&2; return 1
}

echo "== zlib"
fetch zlib.tar.gz https://zlib.net/fossils/zlib-1.3.1.tar.gz https://github.com/madler/zlib/releases/download/v1.3.1/zlib-1.3.1.tar.gz
tar xzf zlib.tar.gz
( cd zlib-1.3.1 && CC=$H-gcc AR=$H-ar RANLIB=$H-ranlib ./configure --static --prefix="$PFX" >/dev/null && make -j"$(nproc)" >/dev/null && make install >/dev/null )

echo "== libpng"
fetch libpng.tar.gz https://download.sourceforge.net/libpng/libpng-1.6.43.tar.gz https://deb.debian.org/debian/pool/main/libp/libpng1.6/libpng1.6_1.6.43.orig.tar.gz
tar xzf libpng.tar.gz
( cd libpng-1.6.43 && ./configure --host=$H --prefix="$PFX" --disable-shared --enable-static CPPFLAGS="-I$PFX/include" LDFLAGS="-L$PFX/lib" >/dev/null && make -j"$(nproc)" >/dev/null && make install >/dev/null )

echo "== freetype"
fetch ft.tar.gz https://downloads.sourceforge.net/project/freetype/freetype2/2.13.2/freetype-2.13.2.tar.gz https://download.savannah.gnu.org/releases/freetype/freetype-2.13.2.tar.gz
tar xzf ft.tar.gz
( cd freetype-2.13.2 && ./configure --host=$H --prefix="$PFX" --disable-shared --enable-static --with-zlib=yes --with-png=yes --with-harfbuzz=no --with-brotli=no --with-bzip2=no \
    PKG_CONFIG_PATH="$PFX/lib/pkgconfig" ZLIB_CFLAGS="-I$PFX/include" ZLIB_LIBS="-L$PFX/lib -lz" LIBPNG_CFLAGS="-I$PFX/include" LIBPNG_LIBS="-L$PFX/lib -lpng16 -lz" >/dev/null \
  && make -j"$(nproc)" >/dev/null && make install >/dev/null )

echo "== libxml2 (statico, minimale)"
fetch libxml2.tar.xz https://download.gnome.org/sources/libxml2/2.12/libxml2-2.12.9.tar.xz https://deb.debian.org/debian/pool/main/libx/libxml2/libxml2_2.12.7+dfsg.orig.tar.xz
tar xJf libxml2.tar.xz
( cd libxml2-2.12.* && ./configure --host=$H --prefix="$PFX" --disable-shared --enable-static \
    --without-python --without-zlib --without-lzma --without-iconv --without-http --without-ftp >/dev/null && make -j"$(nproc)" >/dev/null && make install >/dev/null )

echo "== dvdauthor 0.7.2"
fetch dvdauthor.tar.gz https://deb.debian.org/debian/pool/main/d/dvdauthor/dvdauthor_0.7.2.orig.tar.gz
tar xzf dvdauthor.tar.gz
cd dvdauthor-0.7.2
patch -p0 < "$HERE/dvdauthor-mingw.patch"
cp "$HERE/config.h" src/config.h
cd src
flex -s -B -Cem -o dvdvml.c -Pdvdvm dvdvml.l
bison -o dvdvmy.c -d -p dvdvm dvdvmy.y
CFLAGS="-O2 -I. -I$PFX/include/libxml2 -I$PFX/include -I$PFX/include/freetype2 -DLIBXML_STATIC -static"
$H-gcc $CFLAGS -o "$OUT/dvdauthor.exe" \
  dvdauthor.c dvdcompile.c dvdvml.c dvdvmy.c dvdifo.c dvdvob.c dvdpgc.c dvdcli.c readxml.c conffile.c compat.c \
  -L"$PFX/lib" -lxml2 -lws2_32
$H-gcc $CFLAGS -o "$OUT/spumux.exe" \
  subgen.c subgen-parse-xml.c readxml.c subgen-encode.c subgen-image.c conffile.c compat.c subrender.c subreader.c subfont.c \
  -L"$PFX/lib" -lxml2 -lfreetype -lpng16 -lz -lws2_32 -lm
$H-strip "$OUT/dvdauthor.exe" "$OUT/spumux.exe"
ls -la "$OUT"
echo "== OK"
