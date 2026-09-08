# mp4todvd

App Windows (C# WinForms, .NET Framework 4.8 già in Windows 10/11) che converte video
(mp4/mkv/avi/...) in DVD-Video senza menu e lo masterizza / crea la ISO / lascia la VIDEO_TS.
Tutto autocontenuto: `mp4todvd.exe` + `tools\` (ffmpeg, ffprobe, dvdauthor cross-compilato qui).

## Come si ottiene lo zip

1. Crea un repo GitHub, carica questi file mantenendo la struttura, push su `main`.
2. Actions → "build mp4todvd (Windows)" → Run workflow (~4 minuti).
3. Scarica l'artifact `mp4todvd-windows` (o fai un tag `v1.0` → lo zip finisce in Releases).
4. Scompatta, apri `mp4todvd.exe`.

## Struttura

    .github/workflows/build.yml      job 1 (ubuntu): cross-compila dvdauthor.exe - job 2 (windows): compila l'app e impacchetta
    build/build-dvdauthor.sh         dvdauthor 0.7.2 con mingw-w64, statico, solo libxml2
    build/dvdauthor-mingw.patch      3 righe per Windows (mkdir, fsync, langinfo)
    build/config.h                   config.h a mano, niente autotools
    app/mp4todvd.csproj              progetto
    app/MainForm.cs                  la GUI
    app/Engine.cs                    pipeline: ffprobe -> ffmpeg (MPEG-2 PS) -> dvdauthor -> IMAPI2 (burn / ISO)
    app/Program.cs, app.manifest     entry point, DPI aware
    app/LEGGIMI.txt                  istruzioni utente, finisce nello zip
