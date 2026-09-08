using System;
using System.Windows.Forms;

namespace Mp4ToDvd
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var f = new MainForm();
            if (args.Length > 0) f.Load += (s, e) => f.AddPathsPublic(args);
            Application.Run(f);
        }
    }
}
