using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Mp4ToDvd
{
    public class MainForm : Form
    {
        static readonly string[] VideoExt = { ".mp4", ".mkv", ".avi", ".mov", ".m4v", ".wmv", ".mpg", ".mpeg", ".ts", ".m2ts", ".webm", ".flv", ".3gp", ".vob" };

        ListBox lstFiles;
        Button btnAdd, btnRemove, btnUp, btnDown, btnStart, btnCancel;
        RadioButton rbDvd5, rbDvd9, rbPal, rbNtsc, rbAspAuto, rbAsp169, rbAsp43, rbBurn, rbIso, rbFolder;
        ComboBox cbQuality, cbDrive, cbChapters, cbSpeed, cbSource;
        CheckBox chkTwoPass;
        TextBox txtLabel, txtIso, txtFolder, txtWork, txtLog;
        Button btnIso, btnFolder, btnWork;
        ProgressBar prg;
        Label lblStatus, lblEstimate;

        Engine engine;
        bool running;

        public MainForm()
        {
            Text = "mp4todvd";
            Font = new Font("Segoe UI", 9f);
            AutoScaleMode = AutoScaleMode.Dpi;
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(700, 640);
            Size = new Size(720, 700);
            AllowDrop = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            DragEnter += (s, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; };
            DragDrop += (s, e) => AddPaths((string[])e.Data.GetData(DataFormats.FileDrop));

            engine = new Engine(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools"));
            Build();
            LoadSettings();
            CheckTools();
            FormClosing += (s, e) =>
            {
                if (running && MessageBox.Show(this, "Conversione in corso: vuoi davvero uscire?", "mp4todvd", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) { e.Cancel = true; return; }
                engine.Cancel();
                SaveSettings();
            };
        }

        // ------------------------------------------------------------ impostazioni (%APPDATA%\mp4todvd\settings.ini)
        static string SettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "mp4todvd", "settings.ini");

        void SaveSettings()
        {
            try
            {
                var b = Rectangle.Empty;
                b = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
                var kv = new Dictionary<string, string>
                {
                    ["dvd9"] = rbDvd9.Checked ? "1" : "0",
                    ["ntsc"] = rbNtsc.Checked ? "1" : "0",
                    ["source"] = cbSource.SelectedIndex.ToString(),
                    ["aspect"] = rbAsp169.Checked ? "169" : rbAsp43.Checked ? "43" : "auto",
                    ["quality"] = cbQuality.SelectedIndex.ToString(),
                    ["twopass"] = chkTwoPass.Checked ? "1" : "0",
                    ["chapters"] = cbChapters.SelectedIndex.ToString(),
                    ["mode"] = rbIso.Checked ? "iso" : rbFolder.Checked ? "folder" : "burn",
                    ["drive"] = cbDrive.SelectedIndex.ToString(),
                    ["speed"] = cbSpeed.SelectedIndex.ToString(),
                    ["work"] = txtWork.Text,
                    ["win.x"] = b.X.ToString(), ["win.y"] = b.Y.ToString(), ["win.w"] = b.Width.ToString(), ["win.h"] = b.Height.ToString(),
                    ["win.max"] = WindowState == FormWindowState.Maximized ? "1" : "0",
                };
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
                File.WriteAllLines(SettingsPath, kv.Select(x => x.Key + "=" + x.Value));
            }
            catch { }
        }

        void LoadSettings()
        {
            try
            {
                if (!File.Exists(SettingsPath)) return;
                var kv = new Dictionary<string, string>();
                foreach (var line in File.ReadAllLines(SettingsPath))
                {
                    int i = line.IndexOf('=');
                    if (i > 0) kv[line.Substring(0, i)] = line.Substring(i + 1);
                }
                Func<string, int, int> I = (k, d) => { int v; return kv.ContainsKey(k) && int.TryParse(kv[k], out v) ? v : d; };
                Func<string, string, string> S = (k, d) => kv.ContainsKey(k) ? kv[k] : d;
                Action<ComboBox, int> Sel = (cb, v) => { if (v >= 0 && v < cb.Items.Count) cb.SelectedIndex = v; };

                rbDvd9.Checked = I("dvd9", 0) == 1; rbDvd5.Checked = !rbDvd9.Checked;
                rbNtsc.Checked = I("ntsc", 0) == 1; rbPal.Checked = !rbNtsc.Checked;
                Sel(cbSource, I("source", 4));
                var a = S("aspect", "auto"); rbAsp169.Checked = a == "169"; rbAsp43.Checked = a == "43"; rbAspAuto.Checked = !(rbAsp169.Checked || rbAsp43.Checked);
                Sel(cbQuality, I("quality", 0));
                chkTwoPass.Checked = I("twopass", 0) == 1;
                Sel(cbChapters, I("chapters", 0));
                var mode = S("mode", "burn"); rbIso.Checked = mode == "iso"; rbFolder.Checked = mode == "folder"; rbBurn.Checked = !(rbIso.Checked || rbFolder.Checked);
                Sel(cbDrive, I("drive", 0));
                Sel(cbSpeed, I("speed", 0));
                var w = S("work", ""); if (!string.IsNullOrWhiteSpace(w)) txtWork.Text = w;

                int x = I("win.x", int.MinValue), y = I("win.y", int.MinValue), ww = I("win.w", 0), wh = I("win.h", 0);
                if (ww >= MinimumSize.Width && wh >= MinimumSize.Height)
                {
                    var r = new Rectangle(x, y, ww, wh);
                    if (x != int.MinValue && Screen.AllScreens.Any(sc => sc.WorkingArea.IntersectsWith(r)))
                    { StartPosition = FormStartPosition.Manual; Bounds = r; }
                    else Size = new Size(ww, wh);
                }
                if (I("win.max", 0) == 1) WindowState = FormWindowState.Maximized;
            }
            catch { }
        }

        // ------------------------------------------------------------ layout
        void Build()
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10), ColumnCount = 1, RowCount = 4 };
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
            Controls.Add(root);

            // --- file
            var gFiles = new GroupBox { Text = "Video (trascina qui i file, in ordine di riproduzione)", Dock = DockStyle.Fill };
            var tf = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(6) };
            tf.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            tf.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            lstFiles = new ListBox { Dock = DockStyle.Fill, SelectionMode = SelectionMode.MultiExtended, IntegralHeight = false, HorizontalScrollbar = true };
            var fb = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
            btnAdd = new Button { Text = "Aggiungi...", Width = 100 };
            btnRemove = new Button { Text = "Rimuovi", Width = 100 };
            btnUp = new Button { Text = "▲ Su", Width = 100 };
            btnDown = new Button { Text = "▼ Giù", Width = 100 };
            btnAdd.Click += (s, e) => { using (var d = new OpenFileDialog { Multiselect = true, Filter = "Video|" + string.Join(";", VideoExt.Select(x => "*" + x)) + "|Tutti i file|*.*" }) if (d.ShowDialog(this) == DialogResult.OK) AddPaths(d.FileNames); };
            btnRemove.Click += (s, e) => { foreach (var i in lstFiles.SelectedIndices.Cast<int>().OrderByDescending(i => i).ToList()) lstFiles.Items.RemoveAt(i); UpdateEstimate(); };
            btnUp.Click += (s, e) => MoveItems(-1);
            btnDown.Click += (s, e) => MoveItems(1);
            fb.Controls.AddRange(new Control[] { btnAdd, btnRemove, btnUp, btnDown });
            tf.Controls.Add(lstFiles, 0, 0); tf.Controls.Add(fb, 1, 0);
            gFiles.Controls.Add(tf);
            root.Controls.Add(gFiles, 0, 0);

            // --- opzioni
            var opts = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 4, Margin = new Padding(0, 8, 0, 0) };
            for (int i = 0; i < 4; i++) opts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));

            rbDvd5 = new RadioButton { Text = "DVD5  (4.7 GB)", Checked = true, AutoSize = true };
            rbDvd9 = new RadioButton { Text = "DVD9  (8.5 GB, dual layer)", AutoSize = true };
            opts.Controls.Add(Group("Disco", rbDvd5, rbDvd9), 0, 0);

            rbPal = new RadioButton { Text = "PAL  720×576  (Italia)", Checked = true, AutoSize = true };
            rbNtsc = new RadioButton { Text = "NTSC  720×480", AutoSize = true };
            cbSource = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 230 };
            cbSource.Items.AddRange(new object[] { "Video normale (telefono, PC)", "VHS da OBS: ritaglia le bande (4:3)", "VHS nativa 720x576: interlacciato", "VHS: deinterlaccia (yadif)", "VHS da OBS: lascia com'è (16:9 con bande)" });
            cbSource.SelectedIndex = 4;   // default: VHS da OBS com'è
            opts.Controls.Add(Group("Formato", rbPal, rbNtsc, cbSource), 1, 0);

            rbAspAuto = new RadioButton { Text = "Automatico", Checked = true, AutoSize = true };
            rbAsp169 = new RadioButton { Text = "16:9", AutoSize = true };
            rbAsp43 = new RadioButton { Text = "4:3", AutoSize = true };
            opts.Controls.Add(Group("Aspetto", rbAspAuto, rbAsp169, rbAsp43), 2, 0);

            cbQuality = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
            cbQuality.Items.AddRange(new object[] { "Auto (riempi il disco)", "Massima  8000 kbps", "Alta  6000 kbps", "Media  4500 kbps", "Bassa  3000 kbps" });
            cbQuality.SelectedIndex = 0;
            chkTwoPass = new CheckBox { Text = "Due passate (più lento, meglio)", AutoSize = true };
            cbChapters = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
            cbChapters.Items.AddRange(new object[] { "Capitolo ogni 5 min", "Capitolo ogni 10 min", "Capitolo ogni 15 min", "Nessun capitolo" });
            cbChapters.SelectedIndex = 0;
            opts.Controls.Add(Group("Qualità", cbQuality, chkTwoPass, cbChapters), 3, 0);
            foreach (var rb in new[] { rbDvd5, rbDvd9, rbPal, rbNtsc }) rb.CheckedChanged += (s, e) => UpdateEstimate();
            cbQuality.SelectedIndexChanged += (s, e) => UpdateEstimate();
            root.Controls.Add(opts, 0, 1);

            // --- output
            var gOut = new GroupBox { Text = "Uscita", Dock = DockStyle.Top, AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
            var to = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3, Padding = new Padding(6) };
            to.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            to.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            to.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            rbBurn = new RadioButton { Text = "Masterizza su", Checked = true, AutoSize = true, Anchor = AnchorStyles.Left };
            cbDrive = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
            var btnRefresh = new Button { Text = "Aggiorna", AutoSize = true };
            btnRefresh.Click += (s, e) => LoadDrives();
            var drivePanel = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3, Margin = new Padding(0) };
            drivePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            drivePanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            drivePanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            cbSpeed = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
            cbSpeed.Items.AddRange(new object[] { "Velocità max", "2x (più sicuro)", "4x", "6x", "8x", "12x", "16x" });
            cbSpeed.SelectedIndex = 0;
            drivePanel.Controls.Add(cbDrive, 0, 0);
            drivePanel.Controls.Add(new Label { Text = "a", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(6, 6, 6, 0) }, 1, 0);
            drivePanel.Controls.Add(cbSpeed, 2, 0);
            to.Controls.Add(rbBurn, 0, 0); to.Controls.Add(drivePanel, 1, 0); to.Controls.Add(btnRefresh, 2, 0);

            rbIso = new RadioButton { Text = "Crea file ISO", AutoSize = true, Anchor = AnchorStyles.Left };
            txtIso = new TextBox { Dock = DockStyle.Fill };
            btnIso = new Button { Text = "Sfoglia...", AutoSize = true };
            btnIso.Click += (s, e) => { using (var d = new SaveFileDialog { Filter = "Immagine ISO|*.iso", FileName = txtIso.Text }) if (d.ShowDialog(this) == DialogResult.OK) txtIso.Text = d.FileName; };
            to.Controls.Add(rbIso, 0, 1); to.Controls.Add(txtIso, 1, 1); to.Controls.Add(btnIso, 2, 1);

            rbFolder = new RadioButton { Text = "Solo cartella VIDEO_TS in", AutoSize = true, Anchor = AnchorStyles.Left };
            txtFolder = new TextBox { Dock = DockStyle.Fill };
            btnFolder = new Button { Text = "Sfoglia...", AutoSize = true };
            btnFolder.Click += (s, e) => { using (var d = new FolderBrowserDialog()) if (d.ShowDialog(this) == DialogResult.OK) txtFolder.Text = d.SelectedPath; };
            to.Controls.Add(rbFolder, 0, 2); to.Controls.Add(txtFolder, 1, 2); to.Controls.Add(btnFolder, 2, 2);

            var lblLabel = new Label { Text = "Etichetta disco", AutoSize = true, Anchor = AnchorStyles.Left };
            txtLabel = new TextBox { Text = "DVD_VIDEO", Dock = DockStyle.Fill };
            to.Controls.Add(lblLabel, 0, 3); to.Controls.Add(txtLabel, 1, 3);

            var lblWork = new Label { Text = "Cartella di lavoro", AutoSize = true, Anchor = AnchorStyles.Left };
            txtWork = new TextBox { Text = Path.Combine(Path.GetTempPath(), "mp4todvd"), Dock = DockStyle.Fill };
            btnWork = new Button { Text = "Sfoglia...", AutoSize = true };
            btnWork.Click += (s, e) => { using (var d = new FolderBrowserDialog()) if (d.ShowDialog(this) == DialogResult.OK) txtWork.Text = Path.Combine(d.SelectedPath, "mp4todvd"); };
            to.Controls.Add(lblWork, 0, 4); to.Controls.Add(txtWork, 1, 4); to.Controls.Add(btnWork, 2, 4);

            EventHandler modeChanged = (s, e) =>
            {
                cbDrive.Enabled = cbSpeed.Enabled = rbBurn.Checked;
                txtIso.Enabled = btnIso.Enabled = rbIso.Checked;
                txtFolder.Enabled = btnFolder.Enabled = rbFolder.Checked;
            };
            rbBurn.CheckedChanged += modeChanged; rbIso.CheckedChanged += modeChanged; rbFolder.CheckedChanged += modeChanged;
            modeChanged(null, null);
            gOut.Controls.Add(to);
            root.Controls.Add(gOut, 0, 2);

            // --- avvio + log
            var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = new Padding(0, 8, 0, 0) };
            bottom.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            bottom.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            bottom.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            bottom.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var row = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3 };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            lblEstimate = new Label { Text = "", AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = Color.DimGray };
            btnStart = new Button { Text = "Avvia", Width = 120, Height = 34, Font = new Font("Segoe UI", 10f, FontStyle.Bold) };
            btnCancel = new Button { Text = "Annulla", Width = 100, Height = 34, Enabled = false };
            btnStart.Click += (s, e) => Start();
            btnCancel.Click += (s, e) => { engine.Cancel(); btnCancel.Enabled = false; };
            row.Controls.Add(lblEstimate, 0, 0); row.Controls.Add(btnStart, 1, 0); row.Controls.Add(btnCancel, 2, 0);
            bottom.Controls.Add(row, 0, 0);

            prg = new ProgressBar { Dock = DockStyle.Top, Height = 18, Margin = new Padding(0, 6, 0, 2) };
            bottom.Controls.Add(prg, 0, 1);
            lblStatus = new Label { Text = "Pronto.", AutoSize = true, Dock = DockStyle.Top };
            bottom.Controls.Add(lblStatus, 0, 2);
            txtLog = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Font = new Font("Consolas", 9f), BackColor = Color.White };
            bottom.Controls.Add(txtLog, 0, 3);
            root.Controls.Add(bottom, 0, 3);

            LoadDrives();
        }

        static GroupBox Group(string title, params Control[] items)
        {
            var g = new GroupBox { Text = title, Dock = DockStyle.Fill, AutoSize = true };
            var f = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, Padding = new Padding(4, 2, 4, 2) };
            f.Controls.AddRange(items);
            g.Controls.Add(f);
            return g;
        }

        // ------------------------------------------------------------ helpers
        public void AddPathsPublic(IEnumerable<string> paths) => AddPaths(paths);
        void AddPaths(IEnumerable<string> paths)
        {
            foreach (var p in paths)
            {
                if (Directory.Exists(p))
                    foreach (var f in Directory.GetFiles(p).Where(f => VideoExt.Contains(Path.GetExtension(f).ToLowerInvariant())).OrderBy(f => f))
                        if (!lstFiles.Items.Contains(f)) lstFiles.Items.Add(f);
                if (File.Exists(p) && !lstFiles.Items.Contains(p)) lstFiles.Items.Add(p);
            }
            if (lstFiles.Items.Count > 0)
            {
                var first = (string)lstFiles.Items[0];
                if (string.IsNullOrWhiteSpace(txtIso.Text)) txtIso.Text = Path.ChangeExtension(first, ".iso");
                if (string.IsNullOrWhiteSpace(txtFolder.Text)) txtFolder.Text = Path.Combine(Path.GetDirectoryName(first), Path.GetFileNameWithoutExtension(first) + "_DVD");
                if (txtLabel.Text == "DVD_VIDEO") txtLabel.Text = new string(Path.GetFileNameWithoutExtension(first).ToUpperInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '_').Take(32).ToArray());
            }
            UpdateEstimate();
        }

        void MoveItems(int dir)
        {
            var idx = lstFiles.SelectedIndices.Cast<int>().OrderBy(i => dir < 0 ? i : -i).ToList();
            foreach (var i in idx)
            {
                int j = i + dir;
                if (j < 0 || j >= lstFiles.Items.Count) return;
                var tmp = lstFiles.Items[i]; lstFiles.Items[i] = lstFiles.Items[j]; lstFiles.Items[j] = tmp;
            }
            lstFiles.ClearSelected();
            foreach (var i in idx) lstFiles.SetSelected(i + dir, true);
        }

        void LoadDrives()
        {
            cbDrive.Items.Clear();
            foreach (var d in Engine.ListRecorders()) cbDrive.Items.Add(d);
            if (cbDrive.Items.Count > 0) cbDrive.SelectedIndex = 0;
            else { cbDrive.Items.Add("(nessun masterizzatore)"); cbDrive.SelectedIndex = 0; if (rbBurn.Checked) rbIso.Checked = true; }
        }

        void CheckTools()
        {
            var missing = new[] { "ffmpeg.exe", "ffprobe.exe", "dvdauthor.exe" }.Where(t => engine.Tool(t) == null).ToList();
            if (missing.Count > 0)
            {
                AppendLog("[!] Mancano nella cartella tools: " + string.Join(", ", missing));
                btnStart.Enabled = false;
            }
            else AppendLog("Pronto. Aggiungi i video e premi Avvia.");
        }

        void UpdateEstimate()
        {
            int n = lstFiles.Items.Count;
            if (n == 0) { lblEstimate.Text = ""; return; }
            double cap = rbDvd9.Checked ? 8.5 : 4.7;
            lblEstimate.Text = n + " file — " + (rbDvd9.Checked ? "DVD9" : "DVD5") + " " + (rbNtsc.Checked ? "NTSC" : "PAL") + " — a qualità massima ci stanno ~" + (rbDvd9.Checked ? "2 h" : "1 h") + ", oltre il bitrate scende da solo";
        }

        void AppendLog(string s)
        {
            if (InvokeRequired) { BeginInvoke((Action)(() => AppendLog(s))); return; }
            txtLog.AppendText(s + Environment.NewLine);
        }

        void SetProgress(double p, string status)
        {
            if (InvokeRequired) { BeginInvoke((Action)(() => SetProgress(p, status))); return; }
            if (p < 0) { prg.Style = ProgressBarStyle.Marquee; }
            else { prg.Style = ProgressBarStyle.Continuous; prg.Value = (int)Math.Max(0, Math.Min(100, Math.Round(p * 100))); }
            lblStatus.Text = status + (p >= 0 && p < 1 ? string.Format("   ({0:0}%)", p * 100) : "");
            Text = running ? string.Format("mp4todvd — {0}", p < 0 ? status : string.Format("{0:0}%", p * 100)) : "mp4todvd";
        }

        void SetRunning(bool on)
        {
            running = on;
            foreach (Control c in Controls) SetEnabledDeep(c, !on);
            btnCancel.Enabled = on;
            txtLog.Enabled = true; prg.Enabled = true; lblStatus.Enabled = true;
            if (!on) { prg.Style = ProgressBarStyle.Continuous; Text = "mp4todvd"; }
        }
        void SetEnabledDeep(Control c, bool en)
        {
            if (c == txtLog || c == prg || c == lblStatus || c == btnCancel) return;
            if (c.HasChildren) foreach (Control k in c.Controls) SetEnabledDeep(k, en);
            if (c is Button || c is RadioButton || c is CheckBox || c is ComboBox || c is TextBox || c is ListBox) c.Enabled = en;
        }

        // ------------------------------------------------------------ run
        void Start()
        {
            if (lstFiles.Items.Count == 0) { MessageBox.Show(this, "Aggiungi almeno un video.", "mp4todvd", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            var job = new Job
            {
                Files = lstFiles.Items.Cast<string>().ToList(),
                Dvd9 = rbDvd9.Checked,
                Ntsc = rbNtsc.Checked,
                Aspect = rbAsp169.Checked ? "16:9" : rbAsp43.Checked ? "4:3" : "auto",
                VideoKbps = new[] { 0, 8000, 6000, 4500, 3000 }[cbQuality.SelectedIndex],
                TwoPass = chkTwoPass.Checked,
                Label = txtLabel.Text,
                WorkDir = txtWork.Text,
                ChapterMinutes = new[] { 5, 10, 15, 100000 }[cbChapters.SelectedIndex],
                DriveIndex = cbDrive.SelectedIndex,
                BurnSpeedX = new[] { 0, 2, 4, 6, 8, 12, 16 }[cbSpeed.SelectedIndex],
                Source = cbSource.SelectedIndex,
                Mode = rbIso.Checked ? OutputMode.Iso : rbFolder.Checked ? OutputMode.Folder : OutputMode.Burn,
                OutputPath = rbIso.Checked ? txtIso.Text : txtFolder.Text,
            };
            if (job.Mode == OutputMode.Iso && string.IsNullOrWhiteSpace(job.OutputPath)) { MessageBox.Show(this, "Indica dove salvare la ISO.", "mp4todvd"); return; }
            if (job.Mode == OutputMode.Folder && string.IsNullOrWhiteSpace(job.OutputPath)) { MessageBox.Show(this, "Indica la cartella di destinazione.", "mp4todvd"); return; }
            if (job.Mode == OutputMode.Burn && Engine.ListRecorders().Count == 0) { MessageBox.Show(this, "Nessun masterizzatore trovato: scegli ISO o cartella.", "mp4todvd"); return; }

            engine.Log = AppendLog;
            engine.Progress = SetProgress;
            engine.AskInsertDisc = msg =>
            {
                DialogResult r = DialogResult.Cancel;
                Invoke((Action)(() => r = MessageBox.Show(this, msg, "mp4todvd — inserisci il disco", MessageBoxButtons.OKCancel, MessageBoxIcon.Information)));
                return r == DialogResult.OK;
            };

            txtLog.Clear();
            SetRunning(true);
            SetProgress(-1, "Avvio...");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Task.Run(() =>
            {
                string result = null; Exception error = null;
                try { result = engine.Execute(job); }
                catch (OperationCanceledException) { }
                catch (Exception ex) { error = ex; }
                BeginInvoke((Action)(() =>
                {
                    SetRunning(false);
                    if (result != null)
                    {
                        AppendLog("[OK] " + result.Replace("\n", " "));
                        SetProgress(1, "Fatto in " + sw.Elapsed.ToString(@"h\:mm\:ss"));
                        MessageBox.Show(this, result, "mp4todvd — fatto", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    else if (error != null)
                    {
                        AppendLog("[ERRORE] " + error.Message);
                        SetProgress(0, "Errore");
                        MessageBox.Show(this, error.Message, "mp4todvd — errore", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                    else { AppendLog("Annullato."); SetProgress(0, "Annullato"); }
                }));
            });
        }
    }
}
