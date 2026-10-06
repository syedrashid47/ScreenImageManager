using System;
using System.IO;
using System.Xml.Serialization;

namespace ScreenImageManager
{
    /// <summary>All configurable values. Public get/set properties so XmlSerializer can save them.</summary>
    public class AppSettings
    {
        // ---- Output (always a sub folder of the installed app) ----
        public string OutputSubFolder { get; set; } = "Images";
        public string OutputFileName { get; set; } = "screen.png";

        // ---- Schedule ----
        public int IntervalMinutes { get; set; } = 3;                // how often the scheduled task runs

        // ---- Look ----
        public string BackgroundColorHex { get; set; } = "#1F5569";
        public string Text { get; set; } = "";
        public string FontName { get; set; } = "Segoe UI";
        public float FontHeightPercent { get; set; } = 0.05f;      // fraction of image height
        public float RightMarginPercent { get; set; } = 0.02f;     // fraction of image width
        public float Scale { get; set; } = 1.0f;                   // master scale

        // ---- Timestamp ----
        public string TimestampFormat { get; set; } = "ddd, dd-MM-yyyy HH:mm:ss";
        public float TimestampSizeFactor { get; set; } = 0.5f;     // relative to main text
        public float TimestampGapPercent { get; set; } = 0.01f;    // fraction of image height

        // ---- Weather ----
        public bool WeatherEnabled { get; set; } = true;
        public string WeatherUrl { get; set; } = "https://XXXXXXXXX"; //put your own api url here,
        /*the results should be structured like this
        {"temperature":"38","feelsLike":"42","condition":"Sunny","conditionDescription":"Sunny","humidity":"12","pressure":"1012","windSpeed":"9","windDirection":"149","windFrom":"SSE","visibility":"10","uvIndex":"8","uvDescription":"Very High","airQualityIndex":"72","icon":"http://img-s-msn-com.akamaized.net/tenant/amp/entityid/AAehR3S.img","fajar":"04:32","dhuhr":"11:44","asr":"15:08","maghrib":"17:38","isha":"19:08","hijriDateString":"1448, 25 ,رَبيع الثاني","m":{"azimuth":284.43675148490405,"altitude":3.8262244257536726,"distance":374727.197170598,"illumination":19.234402256110545,"phase":127.97459098318842,"pmr":"Today 1:32 AM","nmr":"Tomorrow 2:34 AM","pms":"Yesterday 2:24 PM","nms":"Today 3:05 PM"}}
       */
        public double Latitude { get; set; } = 24.695686543297676;
        public double Longitude { get; set; } = 46.724166870117195;
        public double Altitude { get; set; } = 612;
        public int WeatherTimeoutSeconds { get; set; } = 15;

        // ---- Units ----
        public string TempUnit { get; set; } = "°C";
        public string WindUnit { get; set; } = " km/h";
        public string PressureUnit { get; set; } = " hPa";
        public string VisibilityUnit { get; set; } = " km";
    }

    /// <summary>Loads/saves settings.xml in the app folder and resolves the output folder.</summary>
    public static class SettingsStore
    {
        public static string AppFolder
        {
            get { return AppDomain.CurrentDomain.BaseDirectory; }
        }

        public static string SettingsPath
        {
            get { return Path.Combine(AppFolder, "settings.xml"); }
        }

        /// <summary>Full path of the folder where images are created: [app folder]\[OutputSubFolder]</summary>
        public static string GetOutputFolder(AppSettings settings)
        {
            return Path.Combine(AppFolder, settings.OutputSubFolder);
        }

        /// <summary>Returns saved settings, or defaults if the file is missing or unreadable.</summary>
        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var serializer = new XmlSerializer(typeof(AppSettings));
                    using (var stream = File.OpenRead(SettingsPath))
                    {
                        return (AppSettings)serializer.Deserialize(stream);
                    }
                }
            }
            catch
            {
                // Corrupt or unreadable file: fall back to defaults
            }
            return new AppSettings();
        }

        public static void Save(AppSettings settings)
        {
            var serializer = new XmlSerializer(typeof(AppSettings));
            using (var stream = File.Create(SettingsPath))
            {
                serializer.Serialize(stream, settings);
            }
        }
    }
}
