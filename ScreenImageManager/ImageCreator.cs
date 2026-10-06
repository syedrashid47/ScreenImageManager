using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;

namespace ScreenImageManager
{
    public class CreateResult
    {
        public string OutputPath;
        public int Width;
        public int Height;
        public int DeletedFiles;
        public bool WeatherShown;
        public string WeatherMessage;
    }

    public static class ImageCreator
    {
        // ------------------------------------------------------------------
        //  Native helpers (real pixel size of the primary screen, even when Windows display scaling is on)
        // ------------------------------------------------------------------
        private const int DESKTOPVERTRES = 117;
        private const int DESKTOPHORZRES = 118;

        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("gdi32.dll")] private static extern int GetDeviceCaps(IntPtr hdc, int index);
        [DllImport("user32.dll")] private static extern int GetSystemMetrics(int nIndex);

        private static void GetPrimaryScreenSize(out int width, out int height)
        {
            width = 0;
            height = 0;
            IntPtr hdc = GetDC(IntPtr.Zero);
            if (hdc != IntPtr.Zero)
            {
                try
                {
                    width = GetDeviceCaps(hdc, DESKTOPHORZRES);
                    height = GetDeviceCaps(hdc, DESKTOPVERTRES);
                }
                finally
                {
                    ReleaseDC(IntPtr.Zero, hdc);
                }
            }
            if (width <= 0 || height <= 0)
            {
                width = GetSystemMetrics(0);
                height = GetSystemMetrics(1);
            }
            if (width <= 0 || height <= 0)
                throw new InvalidOperationException("Could not determine the screen size.");
        }

        // ------------------------------------------------------------------
        //  Main entry point
        // ------------------------------------------------------------------
        public static CreateResult Create(AppSettings s)
        {
            if (!IsSafeSubFolder(s.OutputSubFolder))
                throw new InvalidOperationException("The output sub folder setting is not a valid folder name.");

            string appFolder = Path.GetFullPath(SettingsStore.AppFolder);
            string folder = Path.GetFullPath(SettingsStore.GetOutputFolder(s));
            string prefix = appFolder.TrimEnd('\\') + "\\";
            if (!folder.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The output folder must be inside the application folder.");

            var result = new CreateResult();

            int width, height;
            GetPrimaryScreenSize(out width, out height);
            result.Width = width;
            result.Height = height;

            Directory.CreateDirectory(folder);
            long unix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            string outputPath = Path.Combine(folder, unix + "_" + s.OutputFileName);
            result.OutputPath = outputPath;

            // Remove ALL files from the output folder first (sub folders are left alone)
            foreach (string file in Directory.EnumerateFiles(folder))
            {
                try
                {
                    File.Delete(file);
                    result.DeletedFiles++;
                }
                catch (Exception)
                {
                    // locked file: skip it
                }
            }

            // Weather (null if disabled or if the call fails; the image is still created)
            WeatherData weather = null;
            if (s.WeatherEnabled)
            {
                string error;
                weather = WeatherApi.Get(s, out error);
                result.WeatherShown = weather != null;
                result.WeatherMessage = weather != null ? "Weather panel shown." : "Weather not available: " + error;
            }
            else
            {
                result.WeatherMessage = "Weather is turned off.";
            }

            using (var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb))
            {
                bmp.SetResolution(96, 96);
                using (var g = Graphics.FromImage(bmp))
                using (var painter = new Painter(g, width, height, s, weather))
                {
                    painter.Paint();
                }
                bmp.Save(outputPath, ImageFormat.Png);
            }

            return result;
        }

        /// <summary>The output folder is emptied on every run, so it must be a plain single folder name.</summary>
        public static bool IsSafeSubFolder(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            if (name == "." || name == "..") return false;
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return false;
            if (name.EndsWith(".") || name.EndsWith(" ")) return false;
            return true;
        }

        private static Color ParseColor(string hex)
        {
            try
            {
                Color c = ColorTranslator.FromHtml((hex ?? "").Trim());
                if (!c.IsEmpty) return c;
            }
            catch (Exception) { }
            return ColorTranslator.FromHtml("#1F5569");
        }

        private static string V(string value, string unit)
        {
            return string.IsNullOrWhiteSpace(value) ? "\u2013" : value + unit;
        }

        // US AQI category + color
        private static string AqiInfo(int aqi, out Color color)
        {
            if (aqi <= 50) { color = Color.FromArgb(0, 200, 83); return "Good"; }
            if (aqi <= 100) { color = Color.FromArgb(255, 214, 0); return "Moderate"; }
            if (aqi <= 150) { color = Color.FromArgb(255, 145, 0); return "Sensitive groups"; }
            if (aqi <= 200) { color = Color.FromArgb(244, 67, 54); return "Unhealthy"; }
            if (aqi <= 300) { color = Color.FromArgb(171, 71, 188); return "Very unhealthy"; }
            color = Color.FromArgb(183, 28, 28);
            return "Hazardous";
        }

        private static GraphicsPath RoundedRect(RectangleF r, float radius)
        {
            float d = radius * 2;
            var p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        // ------------------------------------------------------------------
        //  Drawing
        // ------------------------------------------------------------------
        private sealed class Tile
        {
            public readonly string Label, Value, Sub;
            public readonly Color? Dot;
            public Tile(string label, string value, string sub, Color? dot)
            {
                Label = label; Value = value; Sub = sub; Dot = dot;
            }
        }

        private sealed class Painter : IDisposable
        {
            private readonly List<IDisposable> _own = new List<IDisposable>();
            private readonly Graphics g;
            private readonly int width, height;
            private readonly AppSettings s;
            private readonly WeatherData weather;
            private readonly float u;
            private readonly Color bg, textColor;
            private readonly string timestamp;

            private readonly Font font, smallFont, tempFont, condFont, subFont, labelFont, valueFont, tileSubFont, timeFont;
            private readonly Brush mainBrush, mutedBrush, tileBrush, tileHighlightBrush;
            private readonly Pen linePen;
            private readonly StringFormat right, left, center, rtl;

            private readonly float rightEdge, panelW, panelLeft, gapL, gapS;
            private readonly string[] prayerNames = { "Fajr", "Dhuhr", "Asr", "Maghrib", "Isha" };
            private readonly string[] prayerTimes;
            private readonly int nextPrayer;

            public Painter(Graphics g, int width, int height, AppSettings s, WeatherData weather)
            {
                this.g = g;
                this.width = width;
                this.height = height;
                this.s = s;
                this.weather = weather;

                bg = ParseColor(s.BackgroundColorHex);
                double luminance = (0.299 * bg.R + 0.587 * bg.G + 0.114 * bg.B) / 255.0;
                textColor = luminance > 0.5 ? Color.Black : Color.White;

                try { timestamp = DateTime.Now.ToString(s.TimestampFormat, CultureInfo.InvariantCulture); }
                catch (FormatException) { timestamp = DateTime.Now.ToString("ddd, dd-MM-yyyy", CultureInfo.InvariantCulture); }

                // everything is designed for a 1080px-high screen, then multiplied by the master scale
                u = height / 1080f * s.Scale;

                float mainPx = height * s.FontHeightPercent * s.Scale;
                font = Own(MakeFont(s.FontName, mainPx, FontStyle.Bold));
                smallFont = Own(MakeFont(s.FontName, mainPx * s.TimestampSizeFactor, FontStyle.Regular));
                tempFont = Own(MakeFont(s.FontName, 120 * u, FontStyle.Bold));
                condFont = Own(MakeFont(s.FontName, 34 * u, FontStyle.Bold));
                subFont = Own(MakeFont(s.FontName, 20 * u, FontStyle.Regular));
                labelFont = Own(MakeFont(s.FontName, 14 * u, FontStyle.Bold));
                valueFont = Own(MakeFont(s.FontName, 28 * u, FontStyle.Bold));
                tileSubFont = Own(MakeFont(s.FontName, 15 * u, FontStyle.Regular));
                timeFont = Own(MakeFont(s.FontName, 26 * u, FontStyle.Bold));

                mainBrush = Own(new SolidBrush(textColor));
                mutedBrush = Own(new SolidBrush(Color.FromArgb(175, textColor)));
                tileBrush = Own(new SolidBrush(Color.FromArgb(34, textColor)));
                tileHighlightBrush = Own(new SolidBrush(Color.FromArgb(85, textColor)));
                linePen = Own(new Pen(Color.FromArgb(70, textColor), Math.Max(1f, 1.5f * u)));

                right = Own(new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Near });
                left = Own(new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Near });
                center = Own(new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Near });
                rtl = Own(new StringFormat(StringFormatFlags.DirectionRightToLeft)
                {
                    Alignment = StringAlignment.Near,
                    LineAlignment = StringAlignment.Near
                });

                float margin = width * s.RightMarginPercent;
                rightEdge = width - margin;
                panelW = Math.Min(640 * u, width * 0.6f);
                panelLeft = rightEdge - panelW;
                gapL = 26 * u;
                gapS = 12 * u;

                prayerTimes = weather != null ? weather.PrayerTimes : new string[0];
                nextPrayer = weather != null ? weather.NextPrayerIndex(DateTime.Now.TimeOfDay) : -1;
            }

            private T Own<T>(T item) where T : IDisposable
            {
                _own.Add(item);
                return item;
            }

            private static Font MakeFont(string name, float pixelSize, FontStyle style)
            {
                return new Font(name, Math.Max(1f, pixelSize), style, GraphicsUnit.Pixel);
            }

            public void Dispose()
            {
                foreach (IDisposable d in _own) d.Dispose();
                _own.Clear();
            }

            public void Paint()
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.Clear(bg);

                // Measure first, then draw the whole column vertically centered on the right side
                float totalHeight = Render(0, false);
                float startY = Math.Max(0f, (height - totalHeight) / 2f);
                Render(startY, true);
            }

            // Lays out (and optionally draws) the right-hand column; returns its bottom y.
            private float Render(float top, bool draw)
            {
                float y = top;
                string text = s.Text ?? "";

                // ---- Title text + timestamp ----
                SizeF textSize = g.MeasureString(text, font, (int)panelW, right);
                if (draw) g.DrawString(text, font, mainBrush, new RectangleF(panelLeft, y, panelW, textSize.Height), right);
                y += textSize.Height + height * s.TimestampGapPercent * s.Scale;

                SizeF stampSize = g.MeasureString(timestamp, smallFont, (int)panelW, right);
                if (draw) g.DrawString(timestamp, smallFont, mainBrush, new RectangleF(panelLeft, y, panelW, stampSize.Height), right);
                y += stampSize.Height;

                WeatherData w = weather;
                if (w == null) return y;

                // ---- Divider ----
                y += gapL;
                if (draw) g.DrawLine(linePen, panelLeft, y, rightEdge, y);
                y += gapL;

                // ---- Temperature (right) + condition (left) ----
                float tempH = tempFont.GetHeight(g);
                if (draw) g.DrawString(V(w.Temperature, s.TempUnit), tempFont, mainBrush,
                    new RectangleF(panelLeft, y, panelW, tempH), right);

                var subLines = new List<string> { "Feels like " + V(w.FeelsLike, s.TempUnit) };
                if (!string.IsNullOrWhiteSpace(w.ConditionDescription) &&
                    !string.Equals(w.ConditionDescription, w.Condition, StringComparison.OrdinalIgnoreCase))
                    subLines.Add(w.ConditionDescription);

                float condH = condFont.GetHeight(g);
                float subH = subFont.GetHeight(g);
                float leftBlockH = condH + subLines.Count * subH;
                float ly = y + Math.Max(0f, (tempH - leftBlockH) / 2f);
                if (draw)
                {
                    g.DrawString(w.Condition, condFont, mainBrush, new RectangleF(panelLeft, ly, panelW * 0.55f, condH), left);
                    for (int k = 0; k < subLines.Count; k++)
                        g.DrawString(subLines[k], subFont, mutedBrush,
                            new RectangleF(panelLeft, ly + condH + k * subH, panelW * 0.55f, subH), left);
                }
                y += tempH + gapL;

                // ---- Detail tiles (3 x 2) ----
                string aqiLabel = "";
                Color? aqiColor = null;
                int aqi;
                if (int.TryParse(w.AirQualityIndex, out aqi))
                {
                    Color c;
                    aqiLabel = AqiInfo(aqi, out c);
                    aqiColor = c;
                }

                var tiles = new List<Tile>
                {
                    new Tile("Humidity", V(w.Humidity, "%"), "", null),
                    new Tile("Wind", V(w.WindSpeed, s.WindUnit),
                        string.IsNullOrWhiteSpace(w.WindFrom) ? "" : "from " + w.WindFrom, null),
                    new Tile("Pressure", V(w.Pressure, s.PressureUnit), "", null),
                    new Tile("Visibility", V(w.Visibility, s.VisibilityUnit), "", null),
                    new Tile("UV index", V(w.UvIndex, ""), w.UvDescription, null),
                    new Tile("Air quality", V(w.AirQualityIndex, ""), aqiLabel, aqiColor)
                };

                float tileW = (panelW - 2 * gapS) / 3f;
                float tileH = 96 * u;
                if (draw)
                {
                    for (int i = 0; i < tiles.Count; i++)
                    {
                        int col = i % 3, row = i / 3;
                        var r = new RectangleF(panelLeft + col * (tileW + gapS), y + row * (tileH + gapS), tileW, tileH);
                        DrawTile(r, tiles[i]);
                    }
                }
                y += 2 * tileH + gapS + gapL;

                // ---- Prayer times (5 tiles, next prayer highlighted) ----
                float prH = 78 * u;
                float prW = (panelW - 4 * gapS) / 5f;
                if (draw)
                {
                    for (int i = 0; i < 5; i++)
                    {
                        string time = i < prayerTimes.Length ? prayerTimes[i] : "";
                        var r = new RectangleF(panelLeft + i * (prW + gapS), y, prW, prH);
                        DrawPrayer(r, prayerNames[i], V(time, ""), i == nextPrayer);
                    }
                }
                y += prH + gapL * 0.8f;

                // ---- Footer: Hijri date + moon ----
                string hijri = w.HijriDisplay();
                if (hijri.Length > 0)
                {
                    SizeF hs = g.MeasureString(hijri, subFont, (int)panelW, rtl);
                    if (draw) g.DrawString(hijri, subFont, mainBrush, new RectangleF(panelLeft, y, panelW, hs.Height), rtl);
                    y += hs.Height + 2 * u;
                }

                var moonParts = new List<string>();
                if (w.MoonIllumination.HasValue)
                    moonParts.Add("Moon " + Math.Round(w.MoonIllumination.Value).ToString("0", CultureInfo.InvariantCulture) + "% lit");
                if (!string.IsNullOrWhiteSpace(w.MoonNextRise)) moonParts.Add("Rise " + w.MoonNextRise);
                if (!string.IsNullOrWhiteSpace(w.MoonNextSet)) moonParts.Add("Set " + w.MoonNextSet);
                if (moonParts.Count > 0)
                {
                    string moon = string.Join("  \u00B7  ", moonParts);
                    SizeF ms = g.MeasureString(moon, tileSubFont, (int)panelW, right);
                    if (draw) g.DrawString(moon, tileSubFont, mutedBrush, new RectangleF(panelLeft, y, panelW, ms.Height), right);
                    y += ms.Height;
                }

                return y;
            }

            private void DrawTile(RectangleF r, Tile t)
            {
                using (GraphicsPath path = RoundedRect(r, 14 * u))
                    g.FillPath(tileBrush, path);

                float pad = 14 * u;
                float x = r.X + pad;
                float w = r.Width - 2 * pad;
                float ty = r.Y + 10 * u;

                g.DrawString(t.Label.ToUpperInvariant(), labelFont, mutedBrush,
                    new RectangleF(x, ty, w, labelFont.GetHeight(g)), left);
                ty += labelFont.GetHeight(g) + 2 * u;

                g.DrawString(t.Value, valueFont, mainBrush, new RectangleF(x, ty, w, valueFont.GetHeight(g)), left);
                ty += valueFont.GetHeight(g);

                if (!string.IsNullOrWhiteSpace(t.Sub))
                    g.DrawString(t.Sub, tileSubFont, mutedBrush, new RectangleF(x, ty, w, tileSubFont.GetHeight(g)), left);

                if (t.Dot.HasValue)
                {
                    using (var b = new SolidBrush(t.Dot.Value))
                    {
                        float d = 12 * u;
                        g.FillEllipse(b, r.Right - pad - d, r.Y + 12 * u, d, d);
                    }
                }
            }

            private void DrawPrayer(RectangleF r, string name, string time, bool highlight)
            {
                using (GraphicsPath path = RoundedRect(r, 14 * u))
                    g.FillPath(highlight ? tileHighlightBrush : tileBrush, path);

                float ty = r.Y + 10 * u;
                float lh = labelFont.GetHeight(g);
                g.DrawString(name.ToUpperInvariant(), labelFont, highlight ? mainBrush : mutedBrush,
                    new RectangleF(r.X, ty, r.Width, lh), center);
                g.DrawString(time, timeFont, mainBrush,
                    new RectangleF(r.X, ty + lh + 2 * u, r.Width, timeFont.GetHeight(g)), center);
            }
        }
    }

    // ======================================================================
    //  Weather data + API call
    // ======================================================================
    public sealed class WeatherData
    {
        public string Temperature { get; set; } = "";
        public string FeelsLike { get; set; } = "";
        public string Condition { get; set; } = "";
        public string ConditionDescription { get; set; } = "";
        public string Humidity { get; set; } = "";
        public string Pressure { get; set; } = "";
        public string WindSpeed { get; set; } = "";
        public string WindFrom { get; set; } = "";
        public string Visibility { get; set; } = "";
        public string UvIndex { get; set; } = "";
        public string UvDescription { get; set; } = "";
        public string AirQualityIndex { get; set; } = "";
        public string Fajr { get; set; } = "";
        public string Dhuhr { get; set; } = "";
        public string Asr { get; set; } = "";
        public string Maghrib { get; set; } = "";
        public string Isha { get; set; } = "";
        public string HijriDateString { get; set; } = "";
        public double? MoonIllumination { get; set; }
        public string MoonNextRise { get; set; } = "";
        public string MoonNextSet { get; set; } = "";

        public string[] PrayerTimes
        {
            get { return new[] { Fajr, Dhuhr, Asr, Maghrib, Isha }; }
        }

        // Index of the next prayer after "now" (wraps to Fajr after Isha), or -1 if the times can't be parsed
        public int NextPrayerIndex(TimeSpan now)
        {
            string[] times = PrayerTimes;
            TimeSpan t;
            for (int i = 0; i < times.Length; i++)
            {
                if (TimeSpan.TryParse(times[i], CultureInfo.InvariantCulture, out t) && t > now) return i;
            }
            return TimeSpan.TryParse(times[0], CultureInfo.InvariantCulture, out t) ? 0 : -1;
        }

        // The API sends "year, day, month"; show it as "day month year"
        public string HijriDisplay()
        {
            if (string.IsNullOrWhiteSpace(HijriDateString)) return "";
            string[] p = HijriDateString.Split(',');
            if (p.Length == 3) return p[1].Trim() + " " + p[2].Trim() + " " + p[0].Trim();
            return HijriDateString.Trim();
        }
    }

    public static class WeatherApi
    {
        // Returns null (with a reason in 'error') on any failure: network, timeout, HTTP error, bad or empty JSON.
        public static WeatherData Get(AppSettings s, out string error)
        {
            error = null;
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

                string baseUrl = s.WeatherUrl.Trim();
                string separator = baseUrl.Contains("?") ? "&" : "?";
                string url = baseUrl + separator +
                    "latitude=" + s.Latitude.ToString("R", CultureInfo.InvariantCulture) +
                    "&longitude=" + s.Longitude.ToString("R", CultureInfo.InvariantCulture) +
                    "&altitude=" + s.Altitude.ToString("R", CultureInfo.InvariantCulture);

                int timeoutMs = Math.Max(1, s.WeatherTimeoutSeconds) * 1000;
                var request = (HttpWebRequest)WebRequest.Create(url);
                request.Timeout = timeoutMs;
                request.ReadWriteTimeout = timeoutMs;
                request.UserAgent = "ScreenImageManager";
                request.Accept = "application/json";

                string json;
                using (var response = (HttpWebResponse)request.GetResponse())
                using (Stream stream = response.GetResponseStream())
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    json = reader.ReadToEnd();
                }

                var serializer = new JavaScriptSerializer();
                var root = serializer.DeserializeObject(json) as Dictionary<string, object>;
                if (root == null)
                {
                    error = "unexpected response format";
                    return null;
                }

                if (string.IsNullOrWhiteSpace(Str(root, "temperature")) && string.IsNullOrWhiteSpace(Str(root, "condition")))
                {
                    error = "the API returned no usable data";
                    return null;
                }

                var data = new WeatherData
                {
                    Temperature = Str(root, "temperature"),
                    FeelsLike = Str(root, "feelsLike"),
                    Condition = Str(root, "condition"),
                    ConditionDescription = Str(root, "conditionDescription"),
                    Humidity = Str(root, "humidity"),
                    Pressure = Str(root, "pressure"),
                    WindSpeed = Str(root, "windSpeed"),
                    WindFrom = Str(root, "windFrom"),
                    Visibility = Str(root, "visibility"),
                    UvIndex = Str(root, "uvIndex"),
                    UvDescription = Str(root, "uvDescription"),
                    AirQualityIndex = Str(root, "airQualityIndex"),
                    Fajr = Str(root, "fajar"),
                    Dhuhr = Str(root, "dhuhr"),
                    Asr = Str(root, "asr"),
                    Maghrib = Str(root, "maghrib"),
                    Isha = Str(root, "isha"),
                    HijriDateString = Str(root, "hijriDateString")
                };

                object moonObj;
                if (root.TryGetValue("m", out moonObj))
                {
                    var moon = moonObj as Dictionary<string, object>;
                    if (moon != null)
                    {
                        data.MoonNextRise = Str(moon, "nmr");
                        data.MoonNextSet = Str(moon, "nms");
                        object il;
                        if (moon.TryGetValue("illumination", out il) && il != null)
                        {
                            try { data.MoonIllumination = Convert.ToDouble(il, CultureInfo.InvariantCulture); }
                            catch (Exception) { }
                        }
                    }
                }

                return data;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
        }

        // Reads a value as text whether the API sends it as a string ("36") or a number (36)
        private static string Str(Dictionary<string, object> d, string key)
        {
            object v;
            if (d != null && d.TryGetValue(key, out v) && v != null)
            {
                var str = v as string;
                if (str != null) return str;
                if (v is IConvertible) return Convert.ToString(v, CultureInfo.InvariantCulture);
            }
            return "";
        }
    }
}
