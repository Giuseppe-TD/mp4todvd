# mp4todvd

App Windows (C# WinForms, .NET Framework 4.8 già in Windows 10/11) che converte video
(mp4/mkv/avi/...) in DVD-Video, con o senza menu, e lo masterizza / crea la ISO / lascia la VIDEO_TS.
Tutto autocontenuto: `mp4todvd.exe` + `tools\` (ffmpeg, ffprobe, dvdauthor e spumux cross-compilati qui).

Pensata per i riversamenti da VHS registrati con OBS: riconosce il 1080p con la VHS dentro,
deinterlaccia, e non deforma mai le proporzioni.

## Come si ottiene lo zip

1. Crea un repo GitHub, carica questi file mantenendo la struttura, push su `main`.
2. Actions → "build mp4todvd (Windows)" → Run workflow (~6 minuti).
3. Scarica l'artifact `mp4todvd-windows` (o fai un tag `v1.0` → lo zip finisce in Releases).
4. Scompatta, apri `mp4todvd.exe`.

## Struttura

    .github/workflows/build.yml      job 1 (ubuntu): cross-compila dvdauthor.exe e spumux.exe - job 2 (windows): compila l'app e impacchetta
    build/build-dvdauthor.sh         zlib, libpng, freetype, libxml2 statiche + dvdauthor 0.7.2 con mingw-w64
    build/dvdauthor-mingw.patch      poche righe per Windows (mkdir, fsync, langinfo, netinet)
    build/config.h                   config.h a mano, niente autotools
    app/mp4todvd.csproj              progetto
    app/MainForm.cs                  la GUI
    app/Engine.cs                    pipeline: ffprobe -> ffmpeg (MPEG-2 PS) -> spumux (menu) -> dvdauthor -> IMAPI2 (burn / ISO)
    app/Menu.cs                      template dei menu: sfondo, pulsanti, highlight
    app/PreviewForm.cs               anteprima fotogramma
    app/Program.cs, app.manifest     entry point, DPI aware
    app/LEGGIMI.txt                  istruzioni utente, finisce nello zip
