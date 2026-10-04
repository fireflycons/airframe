using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace Web_Page_Screensaver
{
    /// <summary>
    /// Generator class that captures the latest PreferencesForm UI, at build time or for development,
    /// in both dark and light mode, and saves the images to the assets folder.
    /// It always injects a safe official example URL (https://example.com/screensaver) instead of a personal URL.
    /// </summary>
    public static class AssetScreenshotGenerator
    {
        public static void GenerateAssets(string outputDirectory = null)
        {
            if (string.IsNullOrEmpty(outputDirectory))
            {
                outputDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets");
            }

            if (!Directory.Exists(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            // Support high-DPI displays
            Application.EnableVisualStyles();

            using (var form = new PreferencesForm())
            {
                form.StartPosition = FormStartPosition.CenterScreen;
                form.ClientSize = new Size(860, 632);
                form.TopMost = true;
                form.Show();

                // 1. Inject a safe example URL instead of personal settings
                form.PrepareForScreenshot("https://example.com/screensaver");
                Application.DoEvents();
                Thread.Sleep(200);

                // 2. Capture the dark theme (shown by default on GitHub)
                ThemeManager.IsLightTheme = false;
                form.ApplyTheme(false);
                form.Refresh();
                Application.DoEvents();
                Thread.Sleep(200);

                CaptureFormWindow(form, Path.Combine(outputDirectory, "screenshot_dark.png"));
                CaptureFormWindow(form, Path.Combine(outputDirectory, "screenshot.png"));
                CaptureFormWindow(form, Path.Combine(outputDirectory, "screenshot_en.png"));

                // 3. Capture the light theme
                ThemeManager.IsLightTheme = true;
                form.ApplyTheme(true);
                form.Refresh();
                Application.DoEvents();
                Thread.Sleep(200);

                CaptureFormWindow(form, Path.Combine(outputDirectory, "screenshot_light.png"));

                // Clean up
                form.DialogResult = DialogResult.Cancel;
                form.Close();
            }
        }

        private static void CaptureFormWindow(Form form, string savePath)
        {
            try
            {
                // Use DrawToBitmap so capture works reliably even in non-interactive build/console environments
                Rectangle rect = new Rectangle(0, 0, form.Width, form.Height);
                using (var bitmap = new Bitmap(rect.Width, rect.Height, PixelFormat.Format32bppArgb))
                {
                    form.DrawToBitmap(bitmap, rect);
                    bitmap.Save(savePath, ImageFormat.Png);
                    Console.WriteLine($"[AssetScreenshotGenerator] Captured:{Path.GetFileName(savePath)}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AssetScreenshotGenerator] Capture failed ({savePath}): {ex.Message}");
            }
        }
    }
}
