using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace Mp4ToDvd
{
    [ComImport, Guid("2735413C-7F64-5B0F-8F00-5D77AFBE261E"), InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
    public interface DDiscFormat2DataEvents
    {
        [DispId(0x200)] void Update([In, MarshalAs(UnmanagedType.IDispatch)] object sender, [In, MarshalAs(UnmanagedType.IDispatch)] object progress);
    }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    public class BurnEventSink : DDiscFormat2DataEvents
    {
        readonly Engine engine;
        public BurnEventSink(Engine e) { engine = e; }
        public void Update(object sender, object progress) { engine.OnBurnUpdate(progress); }
    }

    public enum OutputMode { Burn, Iso, Folder }

    public class Job
    {
        public List<string> Files = new List<string>();
        public bool Dvd9;
        public bool Ntsc;
        public string Aspect = "auto";     // auto | 16:9 | 4:3
        public int VideoKbps = 0;          // 0 = auto (riempi il disco)
        public bool TwoPass;
        public string Label = "DVD_VIDEO";
        public string WorkDir;
        public OutputMode Mode = OutputMode.Burn;
        public string OutputPath;          // ISO file o cartella destinazione
        public int DriveIndex;
        public int ChapterMinutes = 5;
        public int BurnSpeedX = 0;         // 0 = massima, altrimenti 2,4,6,8,12,16
    }

    public class ProbeInfo
    {
        public string Path; public double Duration; public double Dar; public int W, H; public bool HasAudio;
    }

    public class Engine
    {
        public Action<string> Log = s => { };
        public Action<double, string> Progress = (p, s) => { };   // p: 0..1 (o -1 = indeterminato)
        public Func<string, bool> AskInsertDisc = msg => false;   // ritorna true per riprovare

        readonly string toolsDir;
        Process current;
        volatile bool cancelled;

        public Engine(string toolsDir) { this.toolsDir = toolsDir; }

        public string Tool(string name)
        {
            var p = Path.Combine(toolsDir, name);
            if (File.Exists(p)) return p;
            var alt = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name);
            if (File.Exists(alt)) return alt;
            return null;
        }

        public void Cancel()
        {
            cancelled = true;
            try { var c = current; if (c != null && !c.HasExited) c.Kill(); } catch { }
        }

        static string Q(string s) => "\"" + s.Replace("\"", "\\\"") + "\"";

        static string Hms(double sec)
        {
            var t = TimeSpan.FromSeconds(Math.Max(0, sec));
            return string.Format("{0}:{1:00}:{2:00}", (int)t.TotalHours, t.Minutes, t.Seconds);
        }

        // ------------------------------------------------------------ processi
        int Run(string exe, string args, Action<string> onOut, Action<string> onErr, IDictionary<string, string> env = null)
        {
            if (cancelled) throw new OperationCanceledException();
            var psi = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            };
            if (env != null) foreach (var kv in env) psi.EnvironmentVariables[kv.Key] = kv.Value;
            var p = new Process { StartInfo = psi };
            p.OutputDataReceived += (s, e) => { if (e.Data != null) onOut?.Invoke(e.Data); };
            p.ErrorDataReceived += (s, e) => { if (e.Data != null) onErr?.Invoke(e.Data); };
            current = p;
            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            p.WaitForExit();
            current = null;
            if (cancelled) throw new OperationCanceledException();
            return p.ExitCode;
        }

        string Capture(string exe, string args)
        {
            var sb = new StringBuilder();
            Run(exe, args, l => sb.AppendLine(l), null);
            return sb.ToString();
        }

        // ------------------------------------------------------------ probe
        public ProbeInfo Probe(string file)
        {
            var ffprobe = Tool("ffprobe.exe") ?? throw new Exception("ffprobe.exe non trovato nella cartella tools");
            var v = Capture(ffprobe, "-v error -select_streams v:0 -show_entries stream=width,height,sample_aspect_ratio -of csv=p=0 " + Q(file)).Trim();
            if (string.IsNullOrWhiteSpace(v)) throw new Exception("nessuna traccia video in " + Path.GetFileName(file));
            var parts = v.Split('\n')[0].Trim().Split(',');
            int w = int.Parse(parts[0]), h = int.Parse(parts[1]);
            double sar = 1.0;
            if (parts.Length > 2 && parts[2].Contains(":"))
            {
                var ab = parts[2].Split(':');
                int a, b;
                if (int.TryParse(ab[0], out a) && int.TryParse(ab[1], out b) && a > 0 && b > 0) sar = (double)a / b;
            }
            var d = Capture(ffprobe, "-v error -show_entries format=duration -of csv=p=0 " + Q(file)).Trim();
            double dur = double.Parse(d.Split('\n')[0].Trim(), CultureInfo.InvariantCulture);
            var a1 = Capture(ffprobe, "-v error -select_streams a -show_entries stream=index -of csv=p=0 " + Q(file)).Trim();
            return new ProbeInfo { Path = file, Duration = dur, Dar = w * sar / h, W = w, H = h, HasAudio = a1.Length > 0 };
        }

        // ------------------------------------------------------------ pipeline
        public string Execute(Job job)
        {
            cancelled = false;
            var ffmpeg = Tool("ffmpeg.exe") ?? throw new Exception("ffmpeg.exe non trovato nella cartella tools");
            var dvdauthor = Tool("dvdauthor.exe") ?? throw new Exception("dvdauthor.exe non trovato nella cartella tools");
            if (job.Files.Count == 0) throw new Exception("nessun file");

            string fmt = job.Ntsc ? "ntsc" : "pal";
            int W = 720, H = job.Ntsc ? 480 : 576;
            string fps = job.Ntsc ? "30000/1001" : "25";
            string gop = job.Ntsc ? "18" : "15";

            Progress(-1, "Analisi file...");
            var probes = new List<ProbeInfo>();
            double total = 0;
            foreach (var f in job.Files)
            {
                var p = Probe(f);
                probes.Add(p);
                total += p.Duration;
                Log(string.Format("{0}  {1}  {2}x{3}  DAR {4:0.00}  audio: {5}", Path.GetFileName(f), Hms(p.Duration), p.W, p.H, p.Dar, p.HasAudio ? "sì" : "no (aggiungo traccia muta)"));
            }

            bool wide = job.Aspect == "16:9" || (job.Aspect == "auto" && probes.Any(p => p.Dar > 1.5));
            double tdar = wide ? 16.0 / 9 : 4.0 / 3;
            string aspect = wide ? "16:9" : "4:3";
            string sar = wide ? (job.Ntsc ? "32/27" : "64/45") : (job.Ntsc ? "8/9" : "16/15");

            const int audioKbps = 192;
            double cap = (job.Dvd9 ? 8540000000.0 : 4700000000.0) * 0.96;
            int vkbps = job.VideoKbps;
            if (vkbps <= 0)
            {
                vkbps = (int)Math.Floor((cap * 8 / 1000 / total) / 1.04 - audioKbps);
                if (vkbps > 8000) vkbps = 8000;
                if (vkbps < 1500) { Log("[!] " + Hms(total) + " totali: troppo per questo disco a qualità decente, vado a " + Math.Max(vkbps, 1000) + " kbps (mosaico). Prova DVD9."); vkbps = Math.Max(vkbps, 1000); }
            }
            Log(string.Format("Totale {0} -> {1} {2}, {3}, video {4} kbps, audio AC3 {5} kbps{6}", Hms(total), fmt.ToUpper(), aspect, job.Dvd9 ? "DVD9" : "DVD5", vkbps, audioKbps, job.TwoPass ? ", 2 passate" : ""));

            var work = job.WorkDir;
            if (Directory.Exists(work)) Directory.Delete(work, true);
            Directory.CreateDirectory(work);
            string dvdDir = job.Mode == OutputMode.Folder ? job.OutputPath : Path.Combine(work, "DVD");
            if (Directory.Exists(dvdDir))
            {
                if (Directory.EnumerateFileSystemEntries(dvdDir).Any())
                    throw new Exception("La cartella di destinazione non è vuota: " + dvdDir);
                Directory.Delete(dvdDir);
            }

            // ---- codifica
            var vobs = new List<Tuple<string, string>>();
            double doneSec = 0;
            for (int n = 0; n < probes.Count; n++)
            {
                var p = probes[n];
                string outFile = Path.Combine(work, string.Format("t{0:00}.mpg", n + 1));
                int sw, sh;
                if (p.Dar >= tdar) { sw = W; sh = (int)Math.Round(H * tdar / p.Dar / 2) * 2; }
                else { sh = H; sw = (int)Math.Round(W * p.Dar / tdar / 2) * 2; }
                string vf = string.Format("scale={0}:{1}:flags=lanczos,pad={2}:{3}:(ow-iw)/2:(oh-ih)/2,setsar={4}", sw, sh, W, H, sar);

                var common = new StringBuilder();
                common.Append("-y -hide_banner -loglevel error -nostats -progress pipe:1 -stats_period 1 -i " + Q(p.Path));
                if (!p.HasAudio) common.Append(" -f lavfi -i anullsrc=channel_layout=stereo:sample_rate=48000 -shortest");
                string video = string.Format(" -target {0}-dvd -map 0:v:0 -vf {1} -aspect {2} -r {3} -g {4} -b:v {5}k -maxrate 8000k -bufsize 1835k -sn -threads 0",
                    fmt, Q(vf), aspect, fps, gop, vkbps);
                string audio = string.Format(" -map {0} -c:a ac3 -b:a {1}k -ar 48000 -ac 2", p.HasAudio ? "0:a:0" : "1:a:0", audioKbps);

                string name = Path.GetFileName(p.Path);
                double baseDone = doneSec;
                double weight = p.Duration / total;
                Action<string, double, double> progressParser = null;

                Func<string, int> encode = (args) =>
                {
                    var err = new StringBuilder();
                    int code = Run(ffmpeg, args, line =>
                    {
                        var m = Regex.Match(line, @"^out_time_us=(\d+)");
                        if (m.Success)
                        {
                            double t = long.Parse(m.Groups[1].Value) / 1e6;
                            progressParser(name, t, p.Duration);
                        }
                    }, line => { err.AppendLine(line); Log("ffmpeg: " + line); });
                    if (code != 0) throw new Exception("ffmpeg ha fallito su " + name + "\n" + err);
                    return code;
                };

                if (job.TwoPass)
                {
                    string log = Q(Path.Combine(work, "pass" + (n + 1)));
                    progressParser = (nm, t, d) => Progress((baseDone + t / 2) / total, string.Format("Passata 1/2 {0}/{1}: {2}  {3} / {4}", n + 1, probes.Count, nm, Hms(t), Hms(d)));
                    Log("Passata 1: " + name);
                    encode(common + video + " -an -pass 1 -passlogfile " + log + " -f null NUL");
                    progressParser = (nm, t, d) => Progress((baseDone + d / 2 + t / 2) / total, string.Format("Passata 2/2 {0}/{1}: {2}  {3} / {4}", n + 1, probes.Count, nm, Hms(t), Hms(d)));
                    Log("Passata 2: " + name);
                    encode(common + video + audio + " -pass 2 -passlogfile " + log + " -f dvd " + Q(outFile));
                }
                else
                {
                    progressParser = (nm, t, d) => Progress((baseDone + t) / total, string.Format("Codifica {0}/{1}: {2}  {3} / {4}", n + 1, probes.Count, nm, Hms(t), Hms(d)));
                    Log("Codifica: " + name);
                    encode(common + video + audio + " -f dvd " + Q(outFile));
                }
                doneSec += p.Duration;

                var ch = new List<string> { "0" };
                for (int t = job.ChapterMinutes * 60; t < p.Duration - 10; t += job.ChapterMinutes * 60) ch.Add(Hms(t));
                vobs.Add(Tuple.Create(outFile, string.Join(",", ch)));
            }

            // ---- authoring
            Progress(-1, "Authoring DVD (dvdauthor)...");
            var xml = new StringBuilder();
            xml.AppendLine("<dvdauthor dest=\"" + Esc(dvdDir) + "\">");
            xml.AppendLine("  <vmgm><fpc>jump title 1;</fpc></vmgm>");
            xml.AppendLine("  <titleset><titles>");
            xml.AppendLine("    <video format=\"" + fmt + "\" aspect=\"" + aspect + "\"" + (wide ? " widescreen=\"nopanscan\"" : "") + "/>");
            xml.AppendLine("    <audio format=\"ac3\" lang=\"it\"/>");
            xml.AppendLine("    <pgc>");
            foreach (var v in vobs) xml.AppendLine("      <vob file=\"" + Esc(v.Item1) + "\" chapters=\"" + v.Item2 + "\"/>");
            xml.AppendLine("      <post>exit;</post>");
            xml.AppendLine("    </pgc>");
            xml.AppendLine("  </titles></titleset>");
            xml.AppendLine("</dvdauthor>");
            string xmlPath = Path.Combine(work, "dvd.xml");
            File.WriteAllText(xmlPath, xml.ToString(), new UTF8Encoding(false));

            var daErr = new StringBuilder();
            int dc = Run(dvdauthor, "-o " + Q(dvdDir) + " -x " + Q(xmlPath),
                l => { }, l => { if (l.StartsWith("ERR") || l.StartsWith("WARN")) Log("dvdauthor: " + l); daErr.AppendLine(l); },
                new Dictionary<string, string> { { "VIDEO_FORMAT", fmt.ToUpper() } });
            if (dc != 0 || !File.Exists(Path.Combine(dvdDir, "VIDEO_TS", "VIDEO_TS.IFO")))
                throw new Exception("dvdauthor ha fallito:\n" + Tail(daErr.ToString(), 1500));
            Directory.CreateDirectory(Path.Combine(dvdDir, "AUDIO_TS"));
            foreach (var v in vobs) try { File.Delete(v.Item1); } catch { }

            double sizeGB = new DirectoryInfo(dvdDir).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length) / 1e9;
            double limit = job.Dvd9 ? 8.5 : 4.7;
            Log(string.Format("VIDEO_TS pronto: {0:0.00} GB (limite {1} GB)", sizeGB, limit));
            if (sizeGB > limit) throw new Exception(string.Format("Il DVD è {0:0.00} GB e supera i {1} GB del disco scelto. {2}", sizeGB, limit, job.Dvd9 ? "Riduci la qualità." : "Usa DVD9 o riduci la qualità."));

            // ---- output
            string label = new string(job.Label.ToUpperInvariant().Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_').ToArray());
            if (label.Length == 0) label = "DVD_VIDEO";
            if (label.Length > 32) label = label.Substring(0, 32);

            string result;
            switch (job.Mode)
            {
                case OutputMode.Folder:
                    result = "Cartella DVD pronta in " + dvdDir + "\nMasterizzala con tasto destro > Masterizza su disco (o ImgBurn > Build, UDF).";
                    break;
                case OutputMode.Iso:
                    Progress(-1, "Creo la ISO...");
                    WriteIso(dvdDir, job.OutputPath, label, job.Dvd9);
                    result = "ISO creata: " + job.OutputPath + "\nTasto destro > Masterizza immagine disco.";
                    break;
                default:
                    Burn(dvdDir, label, job.DriveIndex, job.BurnSpeedX);
                    result = "DVD masterizzato.";
                    break;
            }
            try { if (job.Mode != OutputMode.Folder) Directory.Delete(work, true); else { foreach (var f in Directory.GetFiles(work)) File.Delete(f); Directory.Delete(work); } } catch { }
            Progress(1, "Fatto");
            return result;
        }

        const double DVD1X = 692.5;   // settori/s a 1x DVD (1385 KiB/s)
        static string SpeedX(int sectorsPerSec) => (sectorsPerSec / DVD1X).ToString("0.#", CultureInfo.InvariantCulture);

        internal void OnBurnUpdate(object progress)
        {
            try
            {
                dynamic pr = progress;
                int action = pr.CurrentAction;
                long done = pr.SectorsWritten, total = pr.TotalSectors;
                switch (action)
                {
                    case 1: Progress(-1, "Calibrazione potenza laser..."); break;
                    case 2: Progress(-1, "Formattazione..."); break;
                    case 3: Progress(-1, "Inizializzo l'hardware..."); break;
                    case 4: Progress(-1, "Scrivo le informazioni iniziali..."); break;
                    case 5: Progress(-1, "Verifica..."); break;
                    case 6:
                        if (total > 0) Progress((double)done / total, string.Format("Scrivo il disco  {0:0} / {1:0} MB", done * 2048 / 1e6, total * 2048 / 1e6));
                        break;
                    case 7: Progress(-1, "Finalizzo il disco (chiusura sessione)..."); break;
                    case 8: Progress(-1, "Completamento..."); break;
                    case 9: Progress(-1, "Verifica..."); break;
                    default: Progress(-1, "Scrittura in corso..."); break;
                }
            }
            catch { }
        }

        static string Esc(string s) => s.Replace("&", "&amp;").Replace("\"", "&quot;").Replace("<", "&lt;");
        static string Tail(string s, int n) => s.Length <= n ? s : s.Substring(s.Length - n);

        // ------------------------------------------------------------ IMAPI2 (COM late-binding, niente interop)
        static dynamic Com(string progId)
        {
            var t = Type.GetTypeFromProgID(progId);
            if (t == null) throw new Exception("Componente " + progId + " non disponibile (IMAPI2)");
            return Activator.CreateInstance(t);
        }
        static object Item(object col, int i) => col.GetType().InvokeMember("Item", BindingFlags.GetProperty, null, col, new object[] { i });

        public static List<string> ListRecorders()
        {
            var list = new List<string>();
            try
            {
                dynamic dm = Com("IMAPI2.MsftDiscMaster2");
                int count = dm.Count;
                for (int i = 0; i < count; i++)
                {
                    try
                    {
                        dynamic rec = Com("IMAPI2.MsftDiscRecorder2");
                        rec.InitializeDiscRecorder((string)Item(dm, i));
                        object[] vols = rec.VolumePathNames;
                        string letter = vols != null && vols.Length > 0 ? (string)vols[0] : "?";
                        list.Add(letter + "  " + ((string)rec.VendorId).Trim() + " " + ((string)rec.ProductId).Trim());
                    }
                    catch { list.Add("Masterizzatore " + i); }
                }
            }
            catch { }
            return list;
        }

        dynamic BuildImage(string dir, string label, dynamic recorderOrNull, bool dvd9)
        {
            dynamic fsi = Com("IMAPI2FS.MsftFileSystemImage");
            if (recorderOrNull != null) fsi.ChooseImageDefaults(recorderOrNull);
            else fsi.FreeMediaBlocks = dvd9 ? 4173824 : 2295104;
            fsi.FileSystemsToCreate = 5;     // ISO9660 + UDF (UDF bridge)
            fsi.UDFRevision = 0x102;         // UDF 1.02 = DVD-Video
            fsi.VolumeName = label;
            fsi.Root.AddTree(dir, false);
            return fsi.CreateResultImage();
        }

        void WriteIso(string dir, string isoPath, string label, bool dvd9)
        {
            dynamic res = BuildImage(dir, label, null, dvd9);
            long totalBytes = (long)res.TotalBlocks * 2048L;
            IStream stream = (IStream)res.ImageStream;
            var buf = new byte[1 << 20];
            IntPtr pRead = Marshal.AllocHGlobal(4);
            try
            {
                using (var fs = new FileStream(isoPath, FileMode.Create, FileAccess.Write))
                {
                    long done = 0;
                    while (true)
                    {
                        stream.Read(buf, buf.Length, pRead);
                        int n = Marshal.ReadInt32(pRead);
                        if (n <= 0) break;
                        fs.Write(buf, 0, n);
                        done += n;
                        if (totalBytes > 0) Progress((double)done / totalBytes, string.Format("Scrivo ISO  {0:0} / {1:0} MB", done / 1e6, totalBytes / 1e6));
                        if (cancelled) throw new OperationCanceledException();
                    }
                }
            }
            finally { Marshal.FreeHGlobal(pRead); }
        }

        void Burn(string dir, string label, int driveIndex, int speedX)
        {
            Progress(-1, "Preparo la masterizzazione...");
            dynamic dm = Com("IMAPI2.MsftDiscMaster2");
            int count = dm.Count;
            if (count == 0) throw new Exception("Nessun masterizzatore trovato.");
            if (driveIndex < 0 || driveIndex >= count) driveIndex = 0;
            dynamic rec = Com("IMAPI2.MsftDiscRecorder2");
            rec.InitializeDiscRecorder((string)Item(dm, driveIndex));
            object[] vols = rec.VolumePathNames;
            string letter = vols != null && vols.Length > 0 ? (string)vols[0] : "il masterizzatore";

            dynamic fmt = Com("IMAPI2.MsftDiscFormat2Data");
            fmt.Recorder = rec;
            fmt.ClientName = "mp4todvd";
            fmt.ForceMediaToBeClosed = true;

            while (true)
            {
                bool ok = false;
                try { ok = fmt.IsCurrentMediaSupported(rec) && fmt.MediaPhysicallyBlank; } catch { ok = false; }
                if (ok) break;
                try { rec.EjectMedia(); } catch { }
                if (!AskInsertDisc("Inserisci un DVD VERGINE in " + letter + ", chiudi il cassetto e premi OK.")) throw new OperationCanceledException();
                Thread.Sleep(5000);
            }

            // velocità
            try
            {
                object[] speeds = fmt.SupportedWriteSpeeds;
                var list = speeds.Select(o => Convert.ToInt32(o)).OrderBy(x => x).ToList();
                Log("Velocità supportate con questo disco: " + string.Join(", ", list.Select(x => SpeedX(x) + "x")));
                if (speedX > 0 && list.Count > 0)
                {
                    int want = (int)Math.Round(speedX * DVD1X);
                    int pick = list.Where(x => x <= want + 50).DefaultIfEmpty(list[0]).Max();
                    fmt.SetWriteSpeed(pick, false);
                    Log("Velocità impostata: " + SpeedX(pick) + "x");
                }
                else Log("Velocità: massima");
            }
            catch (Exception ex) { Log("Velocità non impostabile (" + ex.Message + "), uso la massima"); }

            Log("Masterizzo su " + letter + "...");
            Progress(-1, "Preparo l'immagine...");
            dynamic res = BuildImage(dir, label, rec, false);

            BurnEventSink sink = null; IConnectionPoint cp = null; int cookie = 0;
            try
            {
                var cpc = (IConnectionPointContainer)fmt;
                var iid = typeof(DDiscFormat2DataEvents).GUID;
                cpc.FindConnectionPoint(ref iid, out cp);
                sink = new BurnEventSink(this);
                cp.Advise(sink, out cookie);
            }
            catch { cp = null; Progress(-1, "Scrittura del disco in corso, non toccare il PC..."); }

            try { fmt.Write(res.ImageStream); }
            finally { if (cp != null) { try { cp.Unadvise(cookie); } catch { } } }
            Log("Scrittura completata.");
            try { rec.EjectMedia(); } catch { }
        }
    }
}
