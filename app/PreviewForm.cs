using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Mp4ToDvd
{
    // Anteprima di un fotogramma così come uscirà sul TV: stessa catena di filtri della conversione.
    public class PreviewForm : Form
    {
        readonly Engine engine;
        readonly Job job;
        readonly string file;
        readonly string pngPath;

        PictureBox pic;
        TrackBar bar;
        Label lblTime, lblInfo, lblSize;
        Button btnRefresh, btnClose;
        double duration = 0;
        bool busy;
        int pending = -1;

        public PreviewForm(Engine engine, Job job, string file)
        {
            this.engine = engine; this.job = job; this.file = file;
            pngPath = Path.Combine(Path.GetTempPath(), "mp4todvd_preview_" + Guid.NewGuid().ToString("N") + ".png");

            Text = "Anteprima — " + Path.GetFileName(file);
            Font = new Font("Segoe UI", 9f);
            AutoScaleMode = AutoScaleMode.Dpi;
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(640, 480);
            Size = new Size(1080, 720);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(8) };
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(root);

            // il "televisore": sfondo nero, immagine in Zoom = proporzioni esatte
            pic = new PictureBox { Dock = DockStyle.Fill, BackColor = Color.Black, SizeMode = PictureBoxSizeMode.Zoom, BorderStyle = BorderStyle.FixedSingle };
            root.Controls.Add(pic, 0, 0);

            var row = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3 };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            lblTime = new Label { Text = "0:00:00", AutoSize = true, Anchor = AnchorStyles.Left, Width = 70 };
            bar = new TrackBar { Dock = DockStyle.Fill, Minimum = 0, Maximum = 1000, TickStyle = TickStyle.None, Value = 300 };
            btnRefresh = new Button { Text = "Aggiorna", AutoSize = true };
            row.Controls.Add(lblTime, 0, 0); row.Controls.Add(bar, 1, 0); row.Controls.Add(btnRefresh, 2, 0);
            root.Controls.Add(row, 0, 1);

            lblInfo = new Label { Text = "Carico...", AutoSize = true, Dock = DockStyle.Top, ForeColor = Color.DimGray };
            root.Controls.Add(lblInfo, 0, 2);

            var bottom = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            lblSize = new Label { Text = "Trascina il cursore per cambiare punto del video. L'immagine è mostrata con le proporzioni del TV.", AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = Color.DimGray };
            btnClose = new Button { Text = "Chiudi", AutoSize = true };
            btnClose.Click += (s, e) => Close();
            bottom.Controls.Add(lblSize, 0, 0); bottom.Controls.Add(btnClose, 1, 0);
            root.Controls.Add(bottom, 0, 3);

            bar.ValueChanged += (s, e) => { UpdateTimeLabel(); };
            bar.MouseUp += (s, e) => Render();
            bar.KeyUp += (s, e) => Render();
            btnRefresh.Click += (s, e) => Render();
            Shown += (s, e) => Render();
            FormClosed += (s, e) => { try { pic.Image?.Dispose(); File.Delete(pngPath); } catch { } };
        }

        void UpdateTimeLabel()
        {
            double t = duration * bar.Value / 1000.0;
            var ts = TimeSpan.FromSeconds(t);
            lblTime.Text = string.Format("{0}:{1:00}:{2:00}", (int)ts.TotalHours, ts.Minutes, ts.Seconds);
        }

        void Render()
        {
            if (busy) { pending = bar.Value; return; }
            busy = true;
            int v = bar.Value;
            double at = duration > 0 ? duration * v / 1000.0 : 60;   // prima volta: 1 minuto dentro
            lblInfo.Text = "Genero l'anteprima...";
            btnRefresh.Enabled = false;
            UseWaitCursor = true;
            Task.Run(() =>
            {
                Engine.PreviewResult r = null; Exception err = null;
                try { r = engine.Preview(job, file, at, pngPath); }
                catch (Exception ex) { err = ex; }
                BeginInvoke((Action)(() =>
                {
                    busy = false; UseWaitCursor = false; btnRefresh.Enabled = true;
                    if (r != null)
                    {
                        duration = r.Duration;
                        UpdateTimeLabel();
                        try
                        {
                            Image img;
                            using (var fs = new FileStream(pngPath, FileMode.Open, FileAccess.Read)) img = Image.FromStream(fs);
                            var old = pic.Image; pic.Image = img; old?.Dispose();
                        }
                        catch (Exception ex) { lblInfo.Text = "Errore immagine: " + ex.Message; return; }
                        lblInfo.Text = r.Info + string.Format("  —  visualizzato come {0}x{1}", r.DisplayW, r.DisplayH);
                    }
                    else lblInfo.Text = "Errore: " + (err != null ? err.Message.Split('\n')[0] : "?");
                    if (pending >= 0 && pending != v) { pending = -1; Render(); }
                    pending = -1;
                }));
            });
        }
    }
}
