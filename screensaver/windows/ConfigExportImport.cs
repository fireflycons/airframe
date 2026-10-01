using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace Web_Page_Screensaver
{
    /// <summary>
    /// Serialisable DTO holding the complete screensaver configuration
    /// </summary>
    public class ScreensaverConfigDto
    {
        public string Version { get; set; } = "1.0.6";
        public DateTime ExportedAt { get; set; } = DateTime.Now;
        public string MultiScreenMode { get; set; }
        public bool CloseOnActivity { get; set; }
        public bool MuteAudio { get; set; }
        public bool InPrivate { get; set; }
        public bool ShowClockOverlay { get; set; }
        public string ClockPosition { get; set; } = "BottomRight";
        public List<List<string>> UrlsByScreen { get; set; }
        public List<int> RotationIntervalsByScreen { get; set; }
        public List<bool> RandomizeFlagByScreen { get; set; }
        public List<int> ZoomFactorsByScreen { get; set; }
    }

    /// <summary>
    /// Class responsible for exporting and importing settings
    /// </summary>
    public static class ConfigExportImport
    {
        private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer();

        /// <summary>
        /// Saves the current settings to a JSON file chosen through a dialog.
        /// </summary>
        public static bool ExportToFile(PreferencesManager prefs, IWin32Window owner)
        {
            using (var sfd = new SaveFileDialog())
            {
                sfd.Title = "Export Settings to JSON";
                sfd.Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*";
                sfd.FileName = $"WebScreensaver_Backup_{DateTime.Now:yyyyMMdd}.json";

                if (sfd.ShowDialog(owner) == DialogResult.OK)
                {
                    try
                    {
                        var dto = new ScreensaverConfigDto
                        {
                            MultiScreenMode = prefs.MultiScreenMode.ToString(),
                            CloseOnActivity = prefs.CloseOnActivity,
                            MuteAudio = prefs.MuteAudio,
                            InPrivate = prefs.InPrivate,
                            ShowClockOverlay = prefs.ShowClockOverlay,
                            ClockPosition = prefs.ClockPositionPref.ToString(),
                            UrlsByScreen = prefs.GetAllUrlsByScreenDirect(),
                            RotationIntervalsByScreen = prefs.GetAllIntervalsDirect(),
                            RandomizeFlagByScreen = prefs.GetAllRandomizeDirect(),
                            ZoomFactorsByScreen = prefs.GetAllZoomFactorsDirect()
                        };

                        string json = Serializer.Serialize(dto);
                        File.WriteAllText(sfd.FileName, json, System.Text.Encoding.UTF8);

                        MessageBox.Show(
                            "Settings exported successfully.",
                            "Success",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                        return true;
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(
                            "Error exporting settings: " + ex.Message,
                            "Error",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Reads settings from a JSON file and applies them to PreferencesManager.
        /// </summary>
        public static bool ImportFromFile(PreferencesManager prefs, IWin32Window owner)
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.Title = "Import Settings from JSON";
                ofd.Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*";

                if (ofd.ShowDialog(owner) == DialogResult.OK)
                {
                    try
                    {
                        string json = File.ReadAllText(ofd.FileName, System.Text.Encoding.UTF8);
                        var dto = Serializer.Deserialize<ScreensaverConfigDto>(json);
                        if (dto == null)
                        {
                            throw new Exception("Invalid configuration file format.");
                        }

                        if (!string.IsNullOrEmpty(dto.MultiScreenMode))
                        {
                            if (Enum.TryParse(dto.MultiScreenMode, out PreferencesManager.MultiScreenModeItem mode))
                            {
                                prefs.MultiScreenMode = mode;
                            }
                        }

                        prefs.CloseOnActivity = dto.CloseOnActivity;
                        prefs.MuteAudio = dto.MuteAudio;
                        prefs.InPrivate = dto.InPrivate;
                        prefs.ShowClockOverlay = dto.ShowClockOverlay;

                        if (!string.IsNullOrEmpty(dto.ClockPosition))
                        {
                            if (Enum.TryParse(dto.ClockPosition, out PreferencesManager.ClockPosition cp))
                            {
                                prefs.ClockPositionPref = cp;
                            }
                        }

                        if (dto.UrlsByScreen != null) prefs.SetAllUrlsByScreenDirect(dto.UrlsByScreen);
                        if (dto.RotationIntervalsByScreen != null) prefs.SetAllIntervalsDirect(dto.RotationIntervalsByScreen);
                        if (dto.RandomizeFlagByScreen != null) prefs.SetAllRandomizeDirect(dto.RandomizeFlagByScreen);
                        if (dto.ZoomFactorsByScreen != null) prefs.SetAllZoomFactorsDirect(dto.ZoomFactorsByScreen);

                        prefs.SavePreferences();

                        MessageBox.Show(
                            "Settings imported and applied successfully.",
                            "Success",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                        return true;
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(
                            "Error importing settings: " + ex.Message,
                            "Error",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                    }
                }
            }
            return false;
        }
    }
}
