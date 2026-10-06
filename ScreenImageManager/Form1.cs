using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ScreenImageManager
{
    public partial class Form1 : Form
    {
        // ---- controls ----
        private TabControl _tabs;
        private TextBox txtSubFolder, txtFileName, txtBackground, txtText;
        private TextBox txtTimestampFormat, txtWeatherUrl, txtLatitude, txtLongitude;
        private TextBox txtTempUnit, txtWindUnit, txtPressureUnit, txtVisibilityUnit;
        private ComboBox cmbFont;
        private Panel pnlSwatch;
        private Button btnPickColor, btnCreate;
        private NumericUpDown numFontHeight, numRightMargin, numScale, numTsSize, numTsGap, numAltitude, numTimeout, numInterval;
        private CheckBox chkWeather;
        private Label lblOutputPath, lblTsPreview, lblTaskPath, lblTaskStatus, lblLastRun;
        private Control[] _weatherControls;

        public Form1()
        {
            InitializeComponent();
            BuildUi();
            LoadToUi(SettingsStore.Load());
        }

        // =====================================================================
        //  UI construction (all in code, so there is nothing to drag in the designer)
        // =====================================================================
        private void BuildUi()
        {
            SuspendLayout();
            Text = "Screen Image Manager - Settings";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(660, 560);
            MinimumSize = new Size(600, 520);
            Font = new Font("Segoe UI", 9f);

            _tabs = new TabControl { Dock = DockStyle.Fill };
            var tabLook = NewTab("Output and Look");
            var tabTs = NewTab("Timestamp");
            var tabWeather = NewTab("Weather");
            var tabUnits = NewTab("Units");

            // ---------- Tab 1: Output and Look ----------
            var t = NewTable(tabLook);

            txtSubFolder = new TextBox();
            AddRow(t, "Output sub folder:", txtSubFolder, true);
            txtFileName = new TextBox();
            AddRow(t, "Output file name:", txtFileName, true);

            lblOutputPath = NewNote("", SystemColors.GrayText);
            AddFull(t, lblOutputPath);
            AddFull(t, NewNote("Warning: every file in this folder is deleted each time an image is created.", Color.Firebrick));

            txtBackground = new TextBox { Width = 100 };
            pnlSwatch = new Panel { Width = 32, Height = 22, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(8, 3, 8, 3) };
            btnPickColor = new Button { Text = "Pick...", AutoSize = true };
            var colorRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
            colorRow.Controls.AddRange(new Control[] { txtBackground, pnlSwatch, btnPickColor });
            AddRow(t, "Background color:", colorRow, false);

            txtText = new TextBox { Multiline = true, Height = 70, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical };
            AddRow(t, "Text:", txtText, true);

            cmbFont = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDown,
                Width = 240,
                AutoCompleteMode = AutoCompleteMode.SuggestAppend,
                AutoCompleteSource = AutoCompleteSource.ListItems
            };
            using (var fonts = new InstalledFontCollection())
            {
                foreach (FontFamily f in fonts.Families) cmbFont.Items.Add(f.Name);
            }
            AddRow(t, "Font:", cmbFont, false);

            numFontHeight = NewNum(0.01m, 0.30m, 0.005m, 3);
            AddRow(t, "Font size (fraction of height):", numFontHeight, false);
            numRightMargin = NewNum(0m, 0.50m, 0.005m, 3);
            AddRow(t, "Right margin (fraction of width):", numRightMargin, false);
            numScale = NewNum(0.2m, 4.0m, 0.05m, 2);
            AddRow(t, "Master scale (1.0 = normal):", numScale, false);

            // ---------- Tab 2: Timestamp ----------
            t = NewTable(tabTs);
            txtTimestampFormat = new TextBox();
            AddRow(t, "Timestamp format:", txtTimestampFormat, true);
            lblTsPreview = NewNote("", SystemColors.ControlText);
            AddFull(t, lblTsPreview);
            AddFull(t, NewNote("Examples:  ddd, dd-MM-yyyy   |   ddd, dd-MM-yyyy HH:mm   |   yyyy-MM-dd HH:mm:ss", SystemColors.GrayText));
            numTsSize = NewNum(0.2m, 1.5m, 0.05m, 2);
            AddRow(t, "Size relative to text:", numTsSize, false);
            numTsGap = NewNum(0m, 0.10m, 0.005m, 3);
            AddRow(t, "Gap above (fraction of height):", numTsGap, false);

            // ---------- Tab 3: Weather ----------
            t = NewTable(tabWeather);
            chkWeather = new CheckBox { Text = "Show weather panel", AutoSize = true, Margin = new Padding(3, 6, 3, 6) };
            AddFull(t, chkWeather);
            txtWeatherUrl = new TextBox();
            AddRow(t, "API URL:", txtWeatherUrl, true);
            txtLatitude = new TextBox { Width = 160 };
            AddRow(t, "Latitude:", txtLatitude, false);
            txtLongitude = new TextBox { Width = 160 };
            AddRow(t, "Longitude:", txtLongitude, false);
            numAltitude = NewNum(-500m, 9000m, 1m, 0);
            AddRow(t, "Altitude (meters):", numAltitude, false);
            numTimeout = NewNum(1m, 120m, 1m, 0);
            AddRow(t, "Timeout (seconds):", numTimeout, false);
            _weatherControls = new Control[] { txtWeatherUrl, txtLatitude, txtLongitude, numAltitude, numTimeout };

            // ---------- Tab 4: Units ----------
            t = NewTable(tabUnits);
            txtTempUnit = new TextBox { Width = 120 };
            AddRow(t, "Temperature:", txtTempUnit, false);
            txtWindUnit = new TextBox { Width = 120 };
            AddRow(t, "Wind speed:", txtWindUnit, false);
            txtPressureUnit = new TextBox { Width = 120 };
            AddRow(t, "Pressure:", txtPressureUnit, false);
            txtVisibilityUnit = new TextBox { Width = 120 };
            AddRow(t, "Visibility:", txtVisibilityUnit, false);
            AddFull(t, NewNote("Shown after each value. Include a leading space if you want one, e.g. \" km/h\".", SystemColors.GrayText));

            // ---------- Tab 5: Schedule ----------
            var tabSchedule = NewTab("Schedule");
            t = NewTable(tabSchedule);
            numInterval = NewNum(1m, 720m, 1m, 0);
            AddRow(t, "Run every (minutes):", numInterval, false);
            AddFull(t, NewNote("Windows Task Scheduler starts this app with /run at this interval (hidden, no window). " +
                "Every run reads the saved settings, so only a changed interval needs the button below.", SystemColors.GrayText));
            lblTaskPath = NewNote("", SystemColors.GrayText);
            AddFull(t, lblTaskPath);

            var btnTaskCreate = new Button { Text = "Create / update task", AutoSize = true, Padding = new Padding(6, 2, 6, 2) };
            var btnTaskRemove = new Button { Text = "Remove task", AutoSize = true, Padding = new Padding(6, 2, 6, 2) };
            var btnTaskRefresh = new Button { Text = "Refresh status", AutoSize = true, Padding = new Padding(6, 2, 6, 2) };
            var taskButtons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(3, 10, 3, 4) };
            taskButtons.Controls.AddRange(new Control[] { btnTaskCreate, btnTaskRemove, btnTaskRefresh });
            AddFull(t, taskButtons);

            lblTaskStatus = NewNote("", SystemColors.ControlText);
            AddFull(t, lblTaskStatus);
            lblLastRun = NewNote("", SystemColors.GrayText);
            AddFull(t, lblLastRun);

            btnTaskCreate.Click += delegate { CreateOrUpdateTask(); };
            btnTaskRemove.Click += delegate { RemoveTask(); };
            btnTaskRefresh.Click += delegate { RefreshTaskStatus(); };

            _tabs.TabPages.AddRange(new TabPage[] { tabLook, tabTs, tabWeather, tabUnits, tabSchedule });

            // ---------- Bottom buttons ----------
            var bottom = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 52,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(10, 10, 10, 6)
            };
            var btnClose = NewButton("Close");
            var btnSave = NewButton("Save");
            var btnReset = NewButton("Reset to defaults");
            btnCreate = NewButton("Create image now");
            bottom.Controls.AddRange(new Control[] { btnClose, btnSave, btnReset, btnCreate });

            // Fill control first, then the bottom bar
            Controls.Add(_tabs);
            Controls.Add(bottom);

            // ---------- Events ----------
            btnSave.Click += delegate { SaveSettings(); };
            btnClose.Click += delegate { Close(); };
            btnReset.Click += delegate { ResetToDefaults(); };
            btnCreate.Click += delegate { CreateImageNow(); };
            btnPickColor.Click += delegate { PickColor(); };
            txtSubFolder.TextChanged += delegate { UpdateOutputPath(); };
            txtTimestampFormat.TextChanged += delegate { UpdateTimestampPreview(); };
            txtBackground.TextChanged += delegate { UpdateSwatch(); };
            chkWeather.CheckedChanged += delegate { UpdateWeatherEnabled(); };

            ResumeLayout(true);
        }

        private static TabPage NewTab(string title)
        {
            return new TabPage(title) { UseVisualStyleBackColor = true, AutoScroll = true };
        }

        private static TableLayoutPanel NewTable(TabPage page)
        {
            var t = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 2,
                Padding = new Padding(10)
            };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            page.Controls.Add(t);
            return t;
        }

        private static void AddRow(TableLayoutPanel t, string label, Control c, bool stretch)
        {
            bool tall = c.Height > 40;
            var lbl = new Label
            {
                Text = label,
                AutoSize = true,
                Anchor = tall ? (AnchorStyles.Left | AnchorStyles.Top) : AnchorStyles.Left,
                Margin = new Padding(3, tall ? 8 : 3, 14, 3)
            };
            c.Anchor = stretch ? (AnchorStyles.Left | AnchorStyles.Right) : AnchorStyles.Left;
            c.Margin = new Padding(3, 4, 3, 4);
            t.Controls.Add(lbl);
            t.Controls.Add(c);
        }

        private static void AddFull(TableLayoutPanel t, Control c)
        {
            t.Controls.Add(c);
            t.SetColumnSpan(c, 2);
        }

        private static Label NewNote(string text, Color color)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                MaximumSize = new Size(560, 0),
                ForeColor = color,
                Margin = new Padding(3, 4, 3, 4)
            };
        }

        private static NumericUpDown NewNum(decimal min, decimal max, decimal increment, int decimals)
        {
            return new NumericUpDown
            {
                Minimum = min,
                Maximum = max,
                Increment = increment,
                DecimalPlaces = decimals,
                Width = 90
            };
        }

        private static Button NewButton(string text)
        {
            return new Button { Text = text, Width = 120, Height = 30 };
        }

        // =====================================================================
        //  Settings -> UI
        // =====================================================================
        private void LoadToUi(AppSettings s)
        {
            txtSubFolder.Text = s.OutputSubFolder;
            txtFileName.Text = s.OutputFileName;
            txtBackground.Text = s.BackgroundColorHex;
            txtText.Text = (s.Text ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n");
            cmbFont.Text = s.FontName;
            SetNum(numFontHeight, s.FontHeightPercent);
            SetNum(numRightMargin, s.RightMarginPercent);
            SetNum(numScale, s.Scale);

            txtTimestampFormat.Text = s.TimestampFormat;
            SetNum(numTsSize, s.TimestampSizeFactor);
            SetNum(numTsGap, s.TimestampGapPercent);

            chkWeather.Checked = s.WeatherEnabled;
            txtWeatherUrl.Text = s.WeatherUrl;
            txtLatitude.Text = s.Latitude.ToString("R", CultureInfo.InvariantCulture);
            txtLongitude.Text = s.Longitude.ToString("R", CultureInfo.InvariantCulture);
            SetNum(numAltitude, s.Altitude);
            SetNum(numTimeout, s.WeatherTimeoutSeconds);

            txtTempUnit.Text = s.TempUnit;
            txtWindUnit.Text = s.WindUnit;
            txtPressureUnit.Text = s.PressureUnit;
            txtVisibilityUnit.Text = s.VisibilityUnit;

            SetNum(numInterval, s.IntervalMinutes);

            UpdateOutputPath();
            UpdateTimestampPreview();
            UpdateSwatch();
            UpdateWeatherEnabled();
            RefreshTaskStatus();
        }

        private static void SetNum(NumericUpDown n, double value)
        {
            decimal d = (decimal)value;
            if (d < n.Minimum) d = n.Minimum;
            if (d > n.Maximum) d = n.Maximum;
            n.Value = d;
        }

        // =====================================================================
        //  Live helpers
        // =====================================================================
        private void UpdateOutputPath()
        {
            try
            {
                lblOutputPath.Text = "Images are created in:  " +
                    Path.Combine(SettingsStore.AppFolder, txtSubFolder.Text.Trim());
            }
            catch (ArgumentException)
            {
                lblOutputPath.Text = "Images are created in:  (invalid folder name)";
            }
        }

        private void UpdateTimestampPreview()
        {
            try
            {
                lblTsPreview.Text = "Preview:  " + DateTime.Now.ToString(txtTimestampFormat.Text, CultureInfo.InvariantCulture);
            }
            catch (FormatException)
            {
                lblTsPreview.Text = "Preview:  (invalid format)";
            }
        }

        private void UpdateSwatch()
        {
            Color c;
            pnlSwatch.BackColor = TryParseColor(txtBackground.Text, out c) ? c : Color.Transparent;
        }

        private void UpdateWeatherEnabled()
        {
            foreach (Control c in _weatherControls) c.Enabled = chkWeather.Checked;
        }

        private void PickColor()
        {
            using (var dlg = new ColorDialog { FullOpen = true })
            {
                Color current;
                if (TryParseColor(txtBackground.Text, out current)) dlg.Color = current;
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    Color c = dlg.Color;
                    txtBackground.Text = string.Format("#{0:X2}{1:X2}{2:X2}", c.R, c.G, c.B);
                }
            }
        }

        private static bool TryParseColor(string text, out Color color)
        {
            color = Color.Empty;
            try
            {
                if (string.IsNullOrWhiteSpace(text)) return false;
                color = ColorTranslator.FromHtml(text.Trim());
                return !color.IsEmpty;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // =====================================================================
        //  Buttons
        // =====================================================================
        private void ResetToDefaults()
        {
            var answer = MessageBox.Show(this,
                "Reset all fields to the default values?\r\n(Nothing is saved until you click Save.)",
                "Reset to defaults", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer == DialogResult.Yes) LoadToUi(new AppSettings());
        }

        private void SaveSettings()
        {
            AppSettings s = ReadFromUi();
            if (s == null) return;

            try
            {
                SettingsStore.Save(s);
                MessageBox.Show(this, "Settings saved to:\r\n" + SettingsStore.SettingsPath,
                    "Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    "Could not save the settings:\r\n" + ex.Message +
                    "\r\n\r\nIf the app is installed under Program Files, install it in a folder you can write to, " +
                    "such as C:\\Apps\\ScreenImageManager.",
                    "Save failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // Saves the current fields, then creates the image using exactly what was saved.
        private async void CreateImageNow()
        {
            AppSettings s = ReadFromUi();
            if (s == null) return;

            try
            {
                SettingsStore.Save(s);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not save the settings:\r\n" + ex.Message,
                    "Save failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            btnCreate.Enabled = false;
            Cursor = Cursors.WaitCursor;
            try
            {
                CreateResult r = await Task.Run(() => ImageCreator.Create(s));

                MessageBox.Show(this,
                    "Image created (" + r.Width + " x " + r.Height + "):\r\n" + r.OutputPath +
                    "\r\n\r\n" + r.WeatherMessage,
                    "Done", MessageBoxButtons.OK, MessageBoxIcon.Information);

                try
                {
                    Process.Start("explorer.exe", "/select,\"" + r.OutputPath + "\"");
                }
                catch (Exception)
                {
                    // opening Explorer is just a convenience
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not create the image:\r\n" + ex.Message,
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Cursor = Cursors.Default;
                btnCreate.Enabled = true;
            }
        }

        // =====================================================================
        //  UI -> Settings (with validation). Returns null if something is invalid.
        // =====================================================================
        private AppSettings ReadFromUi()
        {
            string sub = txtSubFolder.Text.Trim();
            if (!IsValidFolderName(sub))
                return Fail("The output sub folder must be a single folder name, with no \\ or / and not \".\" or \"..\".", txtSubFolder);

            string file = txtFileName.Text.Trim();
            if (file.Length == 0 || file.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                return Fail("The output file name is empty or contains invalid characters.", txtFileName);

            Color bg;
            if (!TryParseColor(txtBackground.Text, out bg))
                return Fail("The background color is not valid. Use a hex value such as #1F5569.", txtBackground);

            if (string.IsNullOrWhiteSpace(cmbFont.Text))
                return Fail("Please enter a font name.", cmbFont);

            try { DateTime.Now.ToString(txtTimestampFormat.Text, CultureInfo.InvariantCulture); }
            catch (FormatException) { return Fail("The timestamp format is not valid.", txtTimestampFormat); }
            if (string.IsNullOrWhiteSpace(txtTimestampFormat.Text))
                return Fail("The timestamp format cannot be empty.", txtTimestampFormat);

            Uri uri;
            if (!Uri.TryCreate(txtWeatherUrl.Text.Trim(), UriKind.Absolute, out uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                return Fail("The weather API URL must start with http:// or https://", txtWeatherUrl);

            double lat, lon;
            if (!double.TryParse(txtLatitude.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out lat) ||
                lat < -90 || lat > 90)
                return Fail("Latitude must be a number between -90 and 90 (use a dot as the decimal separator).", txtLatitude);
            if (!double.TryParse(txtLongitude.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out lon) ||
                lon < -180 || lon > 180)
                return Fail("Longitude must be a number between -180 and 180 (use a dot as the decimal separator).", txtLongitude);

            return new AppSettings
            {
                OutputSubFolder = sub,
                OutputFileName = file,
                BackgroundColorHex = txtBackground.Text.Trim(),
                Text = txtText.Text,
                FontName = cmbFont.Text.Trim(),
                FontHeightPercent = (float)numFontHeight.Value,
                RightMarginPercent = (float)numRightMargin.Value,
                Scale = (float)numScale.Value,

                TimestampFormat = txtTimestampFormat.Text,
                TimestampSizeFactor = (float)numTsSize.Value,
                TimestampGapPercent = (float)numTsGap.Value,

                WeatherEnabled = chkWeather.Checked,
                WeatherUrl = txtWeatherUrl.Text.Trim(),
                Latitude = lat,
                Longitude = lon,
                Altitude = (double)numAltitude.Value,
                WeatherTimeoutSeconds = (int)numTimeout.Value,

                TempUnit = txtTempUnit.Text,
                WindUnit = txtWindUnit.Text,
                PressureUnit = txtPressureUnit.Text,
                VisibilityUnit = txtVisibilityUnit.Text,

                IntervalMinutes = (int)numInterval.Value
            };
        }

        // =====================================================================
        //  Scheduled task
        // =====================================================================
        private void RefreshTaskStatus()
        {
            lblTaskPath.Text = "The task will run:  \"" + Application.ExecutablePath + "\" /run";

            string xml;
            if (TaskSchedulerHelper.Exists(out xml))
            {
                int minutes = TaskSchedulerHelper.ParseIntervalMinutes(xml);
                string command = TaskSchedulerHelper.ParseCommand(xml);

                string text = "Task status:  INSTALLED, runs every " +
                    (minutes > 0 ? minutes + " minute(s)" : "(unknown interval)");
                if (command.Length > 0) text += "\r\nProgram:  " + command;
                if (command.Length > 0 &&
                    !string.Equals(command, Application.ExecutablePath, StringComparison.OrdinalIgnoreCase))
                    text += "\r\nNote: the task points to a different copy of the app. " +
                            "Click \"Create / update task\" to point it to this one.";

                lblTaskStatus.Text = text;
                lblTaskStatus.ForeColor = Color.ForestGreen;
            }
            else
            {
                lblTaskStatus.Text = "Task status:  not installed";
                lblTaskStatus.ForeColor = Color.Firebrick;
            }

            lblLastRun.Text = "Last run (from run.log):  " + ReadLastLogLine();
        }

        private static string ReadLastLogLine()
        {
            try
            {
                string path = Path.Combine(SettingsStore.AppFolder, "run.log");
                if (!File.Exists(path)) return "(no runs yet)";
                string[] lines = File.ReadAllLines(path);
                for (int i = lines.Length - 1; i >= 0; i--)
                    if (!string.IsNullOrWhiteSpace(lines[i])) return lines[i].Trim();
            }
            catch (Exception)
            {
            }
            return "(no runs yet)";
        }

        private void CreateOrUpdateTask()
        {
            AppSettings s = ReadFromUi();
            if (s == null) return;

            try
            {
                SettingsStore.Save(s);
                TaskSchedulerHelper.CreateOrUpdate(s.IntervalMinutes, Application.ExecutablePath);
                TaskSchedulerHelper.TryRunNow();
                RefreshTaskStatus();

                MessageBox.Show(this,
                    "The scheduled task is set up.\r\n\r\nIt runs every " + s.IntervalMinutes + " minute(s) while you are logged on, " +
                    "and has just been started once.\r\n\r\nSettings were saved too.",
                    "Task ready", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not create the scheduled task:\r\n" + ex.Message,
                    "Task failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void RemoveTask()
        {
            string xml;
            if (!TaskSchedulerHelper.Exists(out xml))
            {
                MessageBox.Show(this, "There is no scheduled task to remove.", "Nothing to do",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                RefreshTaskStatus();
                return;
            }

            var answer = MessageBox.Show(this, "Remove the scheduled task? Images will no longer be created automatically.",
                "Remove task", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer != DialogResult.Yes) return;

            try
            {
                TaskSchedulerHelper.Remove();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not remove the task:\r\n" + ex.Message,
                    "Remove failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            RefreshTaskStatus();
        }

        // Safety: the output folder is emptied on every run, so it must be a plain sub folder name.
        private static bool IsValidFolderName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            if (name == "." || name == "..") return false;
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return false; // also rejects \ / :
            if (name.EndsWith(".") || name.EndsWith(" ")) return false;
            return true;
        }

        private AppSettings Fail(string message, Control field)
        {
            Control p = field;
            while (p != null && !(p is TabPage)) p = p.Parent;
            if (p != null) _tabs.SelectedTab = (TabPage)p;

            MessageBox.Show(this, message, "Please check this setting", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            field.Focus();
            return null;
        }
    }
}