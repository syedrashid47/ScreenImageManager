using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace ScreenImageManager
{
    internal static class Program
    {
        /// <summary>
        /// Normal start:       ScreenImageManager.exe        -> settings window
        /// Scheduler start:    ScreenImageManager.exe /run   -> no window, creates one image and exits
        /// </summary>
        [STAThread]
        private static int Main(string[] args)
        {
            bool silent = args.Any(a =>
                string.Equals(a, "/run", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a, "-run", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a, "--run", StringComparison.OrdinalIgnoreCase));

            if (silent) return RunSilently();

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Form1());
            return 0;
        }

        private static int RunSilently()
        {
            // Only one silent run at a time (in case a run takes longer than the schedule interval)
            bool createdNew;
            using (var mutex = new Mutex(true, @"Local\ScreenImageManager.Run", out createdNew))
            {
                if (!createdNew)
                {
                    Log("Skipped: the previous run is still in progress.");
                    return 0;
                }

                try
                {
                    AppSettings settings = SettingsStore.Load();
                    CreateResult r = ImageCreator.Create(settings);
                    Log("OK  " + r.Width + "x" + r.Height + "  " + Path.GetFileName(r.OutputPath) + "  |  " + r.WeatherMessage);
                    return 0;
                }
                catch (Exception ex)
                {
                    Log("ERROR: " + ex.Message);
                    return 1;
                }
                finally
                {
                    mutex.ReleaseMutex();
                }
            }
        }

        // There is no window in silent mode, so results are written to run.log in the app folder.
        private static void Log(string message)
        {
            try
            {
                string path = Path.Combine(SettingsStore.AppFolder, "run.log");

                // keep the log small: start over once it grows past ~200 KB
                var info = new FileInfo(path);
                if (info.Exists && info.Length > 200 * 1024) File.Delete(path);

                File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine);
            }
            catch (Exception)
            {
                // logging must never break a run
            }
        }
    }
}