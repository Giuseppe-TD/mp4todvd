using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Linq;

namespace Mp4ToDvd
{
    public class MenuOptions
    {
        public bool Enabled;
        public int Template;               // indice in MenuBuilder.Templates
        public string Title = "";          // vuoto = etichetta disco
        public string BackgroundImage;     // solo per il template "Immagine personalizzata"
        public List<string> Labels;        // null = nomi file
    }

    // Disegna sfondo, highlight e select del menu. Coordinate dei pulsanti in spazio DVD (720xH).
    public static class MenuBuilder
    {
        public static readonly string[] Templates = { "Scuro elegante", "Chiaro", "Blu classico", "Vintage VHS", "Immagine personalizzata" };
        public const int MaxItems = 16;

        public class Result
        {
            public string BgPng, HlPng, SelPng;
            public List<Rectangle> Buttons = new List<Rectangle>();   // 720xH, indice 0 = "Riproduci tutto"
            public int DisplayW, DisplayH;
        }

        class Style
        {
            public Color Bg1, Bg2, Title, Item, Hl, Sel, Shadow;
            public string TitleFont = "Georgia", ItemFont = "Segoe UI";
            public bool TitleBold = true, Scanlines, Glow, Sweep;
        }

        static Style StyleFor(int t)
        {
            switch (t)
            {
                case 1: return new Style { Bg1 = Color.FromArgb(246, 246, 242), Bg2 = Color.FromArgb(214, 216, 222), Title = Color.FromArgb(34, 34, 38), Item = Color.FromArgb(52, 52, 58), Hl = Color.FromArgb(30, 110, 220), Sel = Color.FromArgb(20, 160, 90), Shadow = Color.FromArgb(0, 0, 0, 0), Sweep = true };
                case 2: return new Style { Bg1 = Color.FromArgb(9, 26, 68), Bg2 = Color.FromArgb(22, 62, 140), Title = Color.White, Item = Color.FromArgb(219, 228, 255), Hl = Color.FromArgb(255, 200, 40), Sel = Color.White, Shadow = Color.FromArgb(120, 0, 0, 0), Glow = true };
                case 3: return new Style { Bg1 = Color.FromArgb(56, 38, 24), Bg2 = Color.FromArgb(112, 76, 42), Title = Color.FromArgb(246, 232, 200), Item = Color.FromArgb(240, 224, 190), Hl = Color.FromArgb(255, 140, 60), Sel = Color.FromArgb(255, 240, 200), Shadow = Color.FromArgb(140, 0, 0, 0), Scanlines = true, TitleFont = "Georgia" };
                case 4: return new Style { Bg1 = Color.FromArgb(20, 20, 24), Bg2 = Color.FromArgb(20, 20, 24), Title = Color.White, Item = Color.FromArgb(240, 240, 240), Hl = Color.FromArgb(255, 122, 24), Sel = Color.White, Shadow = Color.FromArgb(170, 0, 0, 0) };
                default: return new Style { Bg1 = Color.FromArgb(18, 22, 30), Bg2 = Color.FromArgb(44, 50, 66), Title = Color.White, Item = Color.FromArgb(232, 232, 232), Hl = Color.FromArgb(255, 122, 24), Sel = Color.White, Shadow = Color.FromArgb(150, 0, 0, 0), Sweep = true };
            }
        }

        public static string CleanLabel(string file)
        {
            var s = Path.GetFileNameWithoutExtension(file).Replace('_', ' ').Replace('.', ' ').Trim();
            while (s.Contains("  ")) s = s.Replace("  ", " ");
            if (s.Length > 42) s = s.Substring(0, 40) + "…";
            return s.Length == 0 ? "Video" : s;
        }

        static int Even(double v) { int i = (int)Math.Round(v); return i - (i & 1); }

        public static Result Render(MenuOptions o, List<string> items, bool wide, int H, string dir)
        {
            var st = StyleFor(o.Template);
            int dispW = wide ? (int)Math.Round(H * 16.0 / 9) : (int)Math.Round(H * 4.0 / 3);
            int dispH = H;
            var r = new Result { DisplayW = dispW, DisplayH = dispH };
            Directory.CreateDirectory(dir);

            // voci del menu: "Riproduci tutto" + un pulsante per video (se più di uno)
            var labels = new List<string>();
            labels.Add(items.Count > 1 ? "Riproduci tutto" : "Riproduci");
            if (items.Count > 1) labels.AddRange(items.Take(MaxItems));

            string title = string.IsNullOrWhiteSpace(o.Title) ? "" : o.Title.Trim();
            int marginX = (int)(dispW * 0.085);
            int titleTop = (int)(dispH * 0.09);
            int listTop = title.Length > 0 ? (int)(dispH * 0.30) : (int)(dispH * 0.16);
            int listBottom = (int)(dispH * 0.92);
            int rows = labels.Count;
            int rowH = Math.Min(54, (listBottom - listTop) / rows);
            listTop += Math.Max(0, (listBottom - listTop - rows * rowH) / 2);   // blocco voci centrato nello spazio libero
            float itemSize = Math.Max(14f, Math.Min(28f, rowH * 0.50f));
            float titleSize = title.Length > 26 ? 36f : 46f;

            // ---- sfondo a risoluzione di visualizzazione (pixel quadrati), poi ridotto a 720xH
            var buttonRectsDisp = new List<Rectangle>();
            using (var bmp = new Bitmap(dispW, dispH, PixelFormat.Format24bppRgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;

                    if (o.Template == 4 && !string.IsNullOrWhiteSpace(o.BackgroundImage) && File.Exists(o.BackgroundImage))
                    {
                        using (var img = Image.FromFile(o.BackgroundImage))
                        {
                            // riempi tutto il frame mantenendo le proporzioni (ritaglio centrato)
                            double s = Math.Max((double)dispW / img.Width, (double)dispH / img.Height);
                            int w = (int)Math.Ceiling(img.Width * s), h = (int)Math.Ceiling(img.Height * s);
                            g.DrawImage(img, (dispW - w) / 2, (dispH - h) / 2, w, h);
                        }
                        // velatura scura a sinistra e in basso per leggere il testo
                        using (var br = new LinearGradientBrush(new Rectangle(0, 0, dispW, dispH), Color.FromArgb(190, 0, 0, 0), Color.FromArgb(40, 0, 0, 0), 0f))
                            g.FillRectangle(br, 0, 0, dispW, dispH);
                        using (var br = new LinearGradientBrush(new Rectangle(0, 0, dispW, dispH), Color.FromArgb(0, 0, 0, 0), Color.FromArgb(150, 0, 0, 0), 90f))
                            g.FillRectangle(br, 0, 0, dispW, dispH);
                    }
                    else
                    {
                        using (var br = new LinearGradientBrush(new Rectangle(0, 0, dispW, dispH), st.Bg1, st.Bg2, 60f))
                            g.FillRectangle(br, 0, 0, dispW, dispH);
                        if (st.Glow)
                        {
                            using (var path = new GraphicsPath())
                            {
                                path.AddEllipse(dispW * 0.45f, -dispH * 0.4f, dispW * 0.9f, dispH * 1.3f);
                                using (var pgb = new PathGradientBrush(path) { CenterColor = Color.FromArgb(70, 255, 255, 255), SurroundColors = new[] { Color.FromArgb(0, 255, 255, 255) } })
                                    g.FillPath(pgb, path);
                            }
                        }
                        if (st.Sweep)
                        {
                            using (var br = new LinearGradientBrush(new Point(0, 0), new Point(dispW, dispH), Color.FromArgb(0, 255, 255, 255), Color.FromArgb(28, 255, 255, 255)))
                                g.FillRectangle(br, 0, 0, dispW, dispH);
                        }
                        if (st.Scanlines)
                        {
                            using (var pen = new Pen(Color.FromArgb(28, 0, 0, 0), 1f))
                                for (int y = 0; y < dispH; y += 3) g.DrawLine(pen, 0, y, dispW, y);
                            using (var br = new SolidBrush(Color.FromArgb(60, 255, 200, 120)))
                                g.FillRectangle(br, 0, dispH - 14, dispW, 14);
                        }
                        // riga decorativa sotto il titolo
                        using (var pen = new Pen(Color.FromArgb(90, st.Item), 2f))
                            g.DrawLine(pen, marginX, listTop - rowH * 0.35f, dispW - marginX, listTop - rowH * 0.35f);
                    }

                    var fmt = new StringFormat(StringFormat.GenericTypographic) { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };

                    if (title.Length > 0)
                    {
                        // riduci il corpo finché il titolo entra nella larghezza utile
                        float maxW = dispW - 2 * marginX;
                        while (titleSize > 18f)
                        {
                            using (var ft = new Font(st.TitleFont, titleSize, st.TitleBold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel))
                                if (g.MeasureString(title, ft, 100000, fmt).Width <= maxW * 0.92f) break;
                            titleSize -= 2f;
                        }
                        using (var f = new Font(st.TitleFont, titleSize, st.TitleBold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel))
                        {
                            var rect = new RectangleF(marginX, titleTop, dispW - 2 * marginX, titleSize * 1.4f);
                            if (st.Shadow.A > 0) using (var sb = new SolidBrush(st.Shadow)) g.DrawString(title, f, sb, new RectangleF(rect.X + 3, rect.Y + 3, rect.Width, rect.Height), fmt);
                            using (var tb = new SolidBrush(st.Title)) g.DrawString(title, f, tb, rect, fmt);
                        }
                    }

                    {
                        float maxItemW = dispW - 2 * marginX - 22;
                        string longest = labels.OrderByDescending(l => l.Length).First();
                        while (itemSize > 12f)
                        {
                            using (var ft = new Font(st.ItemFont, itemSize, FontStyle.Regular, GraphicsUnit.Pixel))
                                if (g.MeasureString("00.  " + longest, ft, 100000, fmt).Width <= maxItemW * 0.95f) break;
                            itemSize -= 1f;
                        }
                    }
                    using (var f = new Font(st.ItemFont, itemSize, FontStyle.Regular, GraphicsUnit.Pixel))
                    {
                        int y = listTop;
                        for (int i = 0; i < labels.Count; i++)
                        {
                            string text = (i == 0 ? "▶  " : (i) + ".  ") + labels[i];
                            var size = g.MeasureString(text, f, dispW - 2 * marginX, fmt);
                            var rect = new RectangleF(marginX + 22, y + (rowH - size.Height) / 2, dispW - 2 * marginX - 22, size.Height);
                            if (st.Shadow.A > 0) using (var sb = new SolidBrush(st.Shadow)) g.DrawString(text, f, sb, new RectangleF(rect.X + 2, rect.Y + 2, rect.Width, rect.Height), fmt);
                            using (var tb = new SolidBrush(st.Item)) g.DrawString(text, f, tb, rect, fmt);
                            int bw = (int)Math.Min(dispW - 2 * marginX, Math.Max(size.Width + 40, dispW * 0.42));
                            buttonRectsDisp.Add(new Rectangle(marginX, y + 2, bw, rowH - 4));
                            y += rowH;
                        }
                    }
                }
                // riduzione a 720xH anamorfico
                using (var dvd = new Bitmap(720, H, PixelFormat.Format24bppRgb))
                {
                    using (var g = Graphics.FromImage(dvd))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.DrawImage(bmp, new Rectangle(0, 0, 720, H));   // overload con solo destinazione: scala sempre
                    }
                    r.BgPng = Path.Combine(dir, "menu_bg.png");
                    dvd.Save(r.BgPng, ImageFormat.Png);
                }
            }

            // ---- highlight/select: 720xH, un solo colore pieno + trasparente, niente antialias (spumux vuole max 4 colori)
            double sx = 720.0 / dispW;
            foreach (var rd in buttonRectsDisp)
            {
                int x0 = Even(rd.X * sx), x1 = Even((rd.X + rd.Width) * sx), y0 = Even(rd.Y), y1 = Even(rd.Y + rd.Height);
                r.Buttons.Add(new Rectangle(x0, y0, x1 - x0, y1 - y0));
            }
            r.HlPng = Path.Combine(dir, "menu_hl.png");
            r.SelPng = Path.Combine(dir, "menu_sel.png");
            DrawOverlay(r.HlPng, r.Buttons, st.Hl, H);
            DrawOverlay(r.SelPng, r.Buttons, st.Sel, H);
            return r;
        }

        // marcatore a sinistra + sottolineatura: non copre il testo, si vede bene sul TV
        static void DrawOverlay(string path, List<Rectangle> buttons, Color color, int H)
        {
            using (var bmp = new Bitmap(720, H, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = SmoothingMode.None;
                    g.PixelOffsetMode = PixelOffsetMode.None;
                    using (var br = new SolidBrush(Color.FromArgb(255, color)))
                        foreach (var b in buttons)
                        {
                            g.FillRectangle(br, b.X + 2, b.Y + 6, 8, b.Height - 12);                   // barra a sinistra
                            g.FillRectangle(br, b.X + 16, b.Y + b.Height - 6, b.Width - 20, 4);       // sottolineatura
                        }
                }
                bmp.Save(path, ImageFormat.Png);
            }
        }

        // immagine di anteprima (pixel quadrati) con il primo pulsante evidenziato
        public static Bitmap Compose(Result r, int highlightIndex)
        {
            var bg = Image.FromFile(r.BgPng);
            var outBmp = new Bitmap(r.DisplayW, r.DisplayH, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(outBmp))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(bg, 0, 0, r.DisplayW, r.DisplayH);
                using (var hl = Image.FromFile(r.HlPng))
                {
                    if (highlightIndex >= 0 && highlightIndex < r.Buttons.Count)
                    {
                        var b = r.Buttons[highlightIndex];
                        double sx = (double)r.DisplayW / 720;
                        // ritaglio via clipping + overload che scala: evita differenze tra implementazioni di DrawImage
                        g.SetClip(new Rectangle((int)(b.X * sx) - 1, b.Y - 1, (int)(b.Width * sx) + 2, b.Height + 2));
                        g.DrawImage(hl, new Rectangle(0, 0, r.DisplayW, r.DisplayH));
                        g.ResetClip();
                    }
                }
            }
            bg.Dispose();
            return outBmp;
        }
    }
}
