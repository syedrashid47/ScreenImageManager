using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;

namespace ScreenImageManager
{
    /// <summary>
    /// Creates, queries and removes the Windows scheduled task by calling the built-in schtasks.exe.
    /// The task runs only while the current user is logged on (so the screen size can be read) and hidden
    /// because the app is a Windows application started with /run.
    /// </summary>
    public static class TaskSchedulerHelper
    {
        public const string TaskName = "ScreenImageManager";

        /// <summary>True if the task exists; 'xml' receives its definition.</summary>
        public static bool Exists(out string xml)
        {
            string error;
            int code = Run("/Query /TN \"" + TaskName + "\" /XML", out xml, out error);
            return code == 0 && xml != null && xml.IndexOf("<Task", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>Creates the task, or replaces it if it already exists. Throws with the reason on failure.</summary>
        public static void CreateOrUpdate(int minutes, string exePath)
        {
            if (minutes < 1) minutes = 1;

            string xml = BuildTaskXml(minutes, exePath);
            string file = Path.Combine(Path.GetTempPath(), "ScreenImageManager_task_" + Guid.NewGuid().ToString("N") + ".xml");
            try
            {
                File.WriteAllText(file, xml, Encoding.Unicode); // schtasks expects UTF-16
                string output, error;
                int code = Run("/Create /TN \"" + TaskName + "\" /XML \"" + file + "\" /F", out output, out error);
                if (code != 0)
                    throw new InvalidOperationException(FirstNonEmpty(error, output, "schtasks failed (exit code " + code + ")."));
            }
            finally
            {
                try { File.Delete(file); } catch (Exception) { }
            }
        }

        public static void Remove()
        {
            string output, error;
            int code = Run("/Delete /TN \"" + TaskName + "\" /F", out output, out error);
            if (code != 0)
                throw new InvalidOperationException(FirstNonEmpty(error, output, "schtasks failed (exit code " + code + ")."));
        }

        /// <summary>Starts the task immediately (errors are ignored; it's only a convenience).</summary>
        public static void TryRunNow()
        {
            try
            {
                string output, error;
                Run("/Run /TN \"" + TaskName + "\"", out output, out error);
            }
            catch (Exception) { }
        }

        // ------------------------------------------------------------------
        //  Reading values back from the task definition (language independent)
        // ------------------------------------------------------------------
        public static int ParseIntervalMinutes(string xml)
        {
            if (string.IsNullOrEmpty(xml)) return 0;
            Match m = Regex.Match(xml,
                @"<Interval>P(?:(\d+)D)?(?:T(?:(\d+)H)?(?:(\d+)M)?(?:(\d+)S)?)?</Interval>");
            if (!m.Success) return 0;

            int days = m.Groups[1].Success ? int.Parse(m.Groups[1].Value) : 0;
            int hours = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 0;
            int mins = m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0;
            return days * 1440 + hours * 60 + mins;
        }

        public static string ParseCommand(string xml)
        {
            if (string.IsNullOrEmpty(xml)) return "";
            Match m = Regex.Match(xml, @"<Command>(.*?)</Command>", RegexOptions.Singleline);
            return m.Success ? WebUtility.HtmlDecode(m.Groups[1].Value).Trim() : "";
        }

        // ------------------------------------------------------------------
        //  Internals
        // ------------------------------------------------------------------
        private static string BuildTaskXml(int minutes, string exePath)
        {
            string user = SecurityElement.Escape(WindowsIdentity.GetCurrent().Name);
            string exe = SecurityElement.Escape(exePath);
            string workDir = SecurityElement.Escape(Path.GetDirectoryName(exePath));
            string start = DateTime.Now.ToString("yyyy-MM-dd'T'HH:mm:ss");

            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-16\"?>");
            sb.AppendLine("<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">");
            sb.AppendLine("  <RegistrationInfo>");
            sb.AppendLine("    <Description>Creates the screen image on a schedule (Screen Image Manager).</Description>");
            sb.AppendLine("  </RegistrationInfo>");
            sb.AppendLine("  <Triggers>");
            sb.AppendLine("    <TimeTrigger>");
            sb.AppendLine("      <Repetition>");
            sb.AppendLine("        <Interval>" + ToIsoDuration(minutes) + "</Interval>");
            sb.AppendLine("        <StopAtDurationEnd>false</StopAtDurationEnd>");
            sb.AppendLine("      </Repetition>");
            sb.AppendLine("      <StartBoundary>" + start + "</StartBoundary>");
            sb.AppendLine("      <Enabled>true</Enabled>");
            sb.AppendLine("    </TimeTrigger>");
            sb.AppendLine("  </Triggers>");
            sb.AppendLine("  <Principals>");
            sb.AppendLine("    <Principal id=\"Author\">");
            sb.AppendLine("      <UserId>" + user + "</UserId>");
            sb.AppendLine("      <LogonType>InteractiveToken</LogonType>");
            sb.AppendLine("      <RunLevel>LeastPrivilege</RunLevel>");
            sb.AppendLine("    </Principal>");
            sb.AppendLine("  </Principals>");
            sb.AppendLine("  <Settings>");
            sb.AppendLine("    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>");
            sb.AppendLine("    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>");
            sb.AppendLine("    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>");
            sb.AppendLine("    <AllowHardTerminate>true</AllowHardTerminate>");
            sb.AppendLine("    <StartWhenAvailable>true</StartWhenAvailable>");
            sb.AppendLine("    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>");
            sb.AppendLine("    <AllowStartOnDemand>true</AllowStartOnDemand>");
            sb.AppendLine("    <Enabled>true</Enabled>");
            sb.AppendLine("    <Hidden>false</Hidden>");
            sb.AppendLine("    <ExecutionTimeLimit>PT5M</ExecutionTimeLimit>");
            sb.AppendLine("    <Priority>7</Priority>");
            sb.AppendLine("  </Settings>");
            sb.AppendLine("  <Actions Context=\"Author\">");
            sb.AppendLine("    <Exec>");
            sb.AppendLine("      <Command>" + exe + "</Command>");
            sb.AppendLine("      <Arguments>/run</Arguments>");
            sb.AppendLine("      <WorkingDirectory>" + workDir + "</WorkingDirectory>");
            sb.AppendLine("    </Exec>");
            sb.AppendLine("  </Actions>");
            sb.AppendLine("</Task>");
            return sb.ToString();
        }

        private static string ToIsoDuration(int minutes)
        {
            int h = minutes / 60;
            int m = minutes % 60;
            if (h > 0 && m > 0) return "PT" + h + "H" + m + "M";
            if (h > 0) return "PT" + h + "H";
            return "PT" + m + "M";
        }

        private static string FirstNonEmpty(string a, string b, string fallback)
        {
            if (!string.IsNullOrWhiteSpace(a)) return a.Trim();
            if (!string.IsNullOrWhiteSpace(b)) return b.Trim();
            return fallback;
        }

        private static int Run(string arguments, out string stdout, out string stderr)
        {
            var psi = new ProcessStartInfo("schtasks.exe", arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using (Process p = Process.Start(psi))
            {
                stdout = p.StandardOutput.ReadToEnd();
                stderr = p.StandardError.ReadToEnd();
                p.WaitForExit(30000);
                return p.ExitCode;
            }
        }
    }
}
