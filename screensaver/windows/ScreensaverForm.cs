using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Web.WebView2.Core;

namespace Web_Page_Screensaver
{
    public partial class ScreensaverForm : Form
    {
        private DateTime StartTime;
        private Timer timer;
        private Timer mouseMonitorTimer;
        private Point initialMousePos;
        private int currentSiteIndex = -1;
        private bool shuffleOrder;
        private List<string> urls;
        private PreferencesManager.UrlDisplayMode urlMode;
        // True once every URL has failed and the clock page is up; tells RetryPrimary there is something to recover from
        private bool showingFallbackClock;
        // Prevents a slow probe from overlapping the next timer tick's probe
        private bool probeInFlight;

        private PreferencesManager prefsManager = new PreferencesManager();
        private int screenNum;

        /// <summary>
        /// True while an airframe /screensaver page is on screen. Its controls are meant to be used, so only ESC exits
        /// (or mouse movement, if the user opted in); any other page exits on any input as usual.
        /// </summary>
        public bool IsInteractive { get; private set; }

        private WebView2 webView;

        [ThreadStatic]
        private static Random random;

        public ScreensaverForm(int? screenNumber = null)
        {
            // [Fix 1] Record the start time as soon as the form is created to prevent mouse timer malfunction (premature termination)
            StartTime = DateTime.Now;

            if (screenNumber == null) screenNum = prefsManager.EffectiveScreensList.FindIndex(s => s.IsPrimary);
            else screenNum = (int)screenNumber;

            InitializeComponent();
            try
            {
                Icon = ModernAppIcon.CreateAppIcon(32);
            }
            catch { }
            InitializeWebViewAsync();

            Cursor.Hide();

            initialMousePos = Cursor.Position;
            mouseMonitorTimer = new Timer();
            mouseMonitorTimer.Interval = 50;
            mouseMonitorTimer.Tick += MouseMonitorTimer_Tick;
            mouseMonitorTimer.Start();
        }

        private void MouseMonitorTimer_Tick(object sender, EventArgs e)
        {
            // Grant a grace period of 1.5 seconds after program execution so it doesn't terminate even if the mouse jitters
            if (StartTime.AddSeconds(1.5) > DateTime.Now)
            {
                initialMousePos = Cursor.Position;
                return;
            }

            Point currentPos = Cursor.Position;

            // React only when the X or Y coordinates move by more than 3 pixels
            if (Math.Abs(initialMousePos.X - currentPos.X) > 3 ||
                Math.Abs(initialMousePos.Y - currentPos.Y) > 3)
            {
                mouseMonitorTimer.Stop();
                HandleUserActivity();
            }
        }

        private string currentLoadedUrl = string.Empty;

        private async void InitializeWebViewAsync()
        {
            webView = new WebView2();
            webView.Dock = DockStyle.Fill;
            this.Controls.Add(webView);

            if (this.Controls.ContainsKey("closeButton"))
            {
                this.Controls["closeButton"].BringToFront();
            }

            // [Fix 2] Save WebView2 temporary data in a safe folder (LocalAppData) with no permission issues
            string userDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Airframe-Screensaver");
            
            // InPrivate mode and security options
            CoreWebView2EnvironmentOptions envOptions = null;
            if (prefsManager.InPrivate)
            {
                envOptions = new CoreWebView2EnvironmentOptions
                {
                    AdditionalBrowserArguments = "--inprivate"
                };
            }

            var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder, envOptions);
            await webView.EnsureCoreWebView2Async(env);

            // Apply automatic audio muting
            if (webView.CoreWebView2 != null)
            {
                webView.CoreWebView2.IsMuted = prefsManager.MuteAudio;

                // Handle web page load completion and error events
                webView.CoreWebView2.NavigationCompleted += (s, e) =>
                {
                    if (!e.IsSuccess && e.WebErrorStatus != CoreWebView2WebErrorStatus.OperationCanceled)
                    {
                        // In first-available mode a dead URL hands over to the next one; the clock is the last resort
                        if (urlMode == PreferencesManager.UrlDisplayMode.FirstAvailable && currentSiteIndex + 1 < Urls.Count)
                        {
                            currentSiteIndex++;
                            BrowseTo(ScreensaverUrlItem.Parse(Urls[currentSiteIndex]).Url);
                            return;
                        }

                        // On a network error or server outage, render an elegant modern digital clock fallback screen
                        showingFallbackClock = true;
                        IsInteractive = false;
                        string fallbackHtml = FallbackHtmlProvider.GetFallbackClockHtml(currentLoadedUrl);
                        webView.CoreWebView2.NavigateToString(fallbackHtml);
                    }
                    else if (e.IsSuccess)
                    {
                        IsInteractive = IsInteractiveUrl(webView.CoreWebView2.Source);

                        // 1. Apply the screen zoom factor
                        int zoomPercent = prefsManager.GetZoomFactorByScreen(screenNum);
                        webView.ZoomFactor = (zoomPercent > 0 ? zoomPercent : 100) / 100.0;

                        // 2. Inject the glassmorphism digital clock/date HUD overlay (in the user's chosen corner)
                        if (prefsManager.ShowClockOverlay)
                        {
                            webView.ExecuteScriptAsync(FallbackHtmlProvider.GetClockOverlayScript(prefsManager.ClockPositionPref));
                        }
                    }
                };
            }

            // ---------------------------------------------------------
            // [Modified part] Use events of the WebView2 control itself, not CoreWebView2.
            // 1. Detect general character and number keys
            // On an interactive page keys belong to the page; the global hook in Program handles ESC.
            webView.KeyDown += (sender, e) =>
            {
                if (IsInteractive) return;
                Application.Exit();
            };

            // 2. Detect special keys like arrow keys, Tab, Esc, etc., that might be missed by general KeyDown (safety measure)
            webView.PreviewKeyDown += (sender, e) =>
            {
                if (IsInteractive) return;
                Application.Exit();
            };
            // ---------------------------------------------------------

            StartScreensaverLogic();
        }

        public List<string> Urls
        {
            get
            {
                if (urls == null)
                {
                    urls = prefsManager.GetUrlsByScreen(screenNum);
                }
                return urls;
            }
        }

        private void ScreensaverForm_Load(object sender, EventArgs e)
        {
            // StartTime is handled in the constructor, so leave this empty.
        }

        private void StartScreensaverLogic()
        {
            urlMode = prefsManager.GetUrlModeByScreen(screenNum);

            if (Urls.Any() && urlMode == PreferencesManager.UrlDisplayMode.FirstAvailable)
            {
                // The timer is needed even for a single URL, so a page that was down at start-up is picked up once it recovers.
                // Per-URL display times are about rotation, so they don't apply here.
                timer = new Timer();
                timer.Interval = Math.Max(1, prefsManager.GetRotationIntervalByScreen(screenNum)) * 1000;
                timer.Tick += (s, ee) => RetryPrimary();
                timer.Start();

                currentSiteIndex = 0;
                BrowseTo(ScreensaverUrlItem.Parse(Urls[0]).Url);
            }
            else if (Urls.Any())
            {
                if (Urls.Count > 1)
                {
                    shuffleOrder = prefsManager.GetRandomizeFlagByScreen(screenNum);
                    if (shuffleOrder)
                    {
                        random = new Random();
                        int n = urls.Count;
                        while (n > 1)
                        {
                            n--;
                            int k = random.Next(n + 1);
                            var value = urls[k];
                            urls[k] = urls[n];
                            urls[n] = value;
                        }
                    }

                    timer = new Timer();
                    timer.Interval = prefsManager.GetRotationIntervalByScreen(screenNum) * 1000;
                    timer.Tick += (s, ee) => RotateSite();
                    timer.Start();
                }

                RotateSite();
            }
            else
            {
                webView.Visible = false;
                if (this.Controls.ContainsKey("closeButton"))
                {
                    this.Controls["closeButton"].Visible = false;
                }
            }
        }

        private void BrowseTo(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                webView.Visible = false;
            }
            else
            {
                webView.Visible = true;
                currentLoadedUrl = url;
                showingFallbackClock = false;
                try
                {
                    if (webView.CoreWebView2 != null)
                        webView.CoreWebView2.Navigate(url);
                }
                catch { }
            }
        }

        private void RotateSite()
        {
            currentSiteIndex++;
            if (currentSiteIndex >= Urls.Count) currentSiteIndex = 0;

            string rawUrl = Urls[currentSiteIndex];
            var parsed = ScreensaverUrlItem.Parse(rawUrl);

            // Dynamically update the timer with the per-URL interval (seconds)
            if (timer != null)
            {
                int intervalSec = parsed.CustomInterval.HasValue 
                    ? parsed.CustomInterval.Value 
                    : prefsManager.GetRotationIntervalByScreen(screenNum);
                timer.Interval = Math.Max(1, intervalSec) * 1000;
            }

            BrowseTo(parsed.Url);
        }

        /// <summary>
        /// First-available mode: while a fallback (later URL or the clock) is on screen, return to the first URL once it responds.
        /// The URL is probed out of band first, so the WebView2 error page doesn't flash every interval while it's still down.
        /// </summary>
        private async void RetryPrimary()
        {
            if ((currentSiteIndex == 0 && !showingFallbackClock) || probeInFlight) return;

            string primaryUrl = ScreensaverUrlItem.Parse(Urls[0]).Url;
            probeInFlight = true;
            try
            {
                bool reachable = await Task.Run(() => IsReachable(primaryUrl));
                if (reachable && !IsDisposed)
                {
                    currentSiteIndex = 0;
                    BrowseTo(primaryUrl);
                }
            }
            finally
            {
                probeInFlight = false;
            }
        }

        private static bool IsReachable(string url)
        {
            try
            {
                var uri = new Uri(url);
                if (uri.IsFile) return File.Exists(uri.LocalPath);
                if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return true;

                var request = (HttpWebRequest)WebRequest.Create(uri);
                request.Method = "GET";
                request.Timeout = 5000;
                request.AllowAutoRedirect = true;
                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    return (int)response.StatusCode < 400;
                }
            }
            catch
            {
                // Connection refused, timeout, 4xx/5xx (thrown as WebException) or a malformed URL: all mean "not yet"
                return false;
            }
        }

        private static bool IsInteractiveUrl(string url)
        {
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri)) return false;
            return uri.AbsolutePath.TrimEnd('/').EndsWith("/screensaver", StringComparison.OrdinalIgnoreCase);
        }

        private void HandleUserActivity()
        {
            if (prefsManager.CloseOnActivity)
            {
                Close();
            }
            else if (IsInteractive)
            {
                // No close button: ESC is the way out, and the page header says so
                Cursor.Show();
            }
            else
            {
                if (this.Controls.ContainsKey("closeButton"))
                {
                    this.Controls["closeButton"].Visible = true;
                }
                Cursor.Show();
            }
        }

        private void closeButton_Click(object sender, EventArgs e)
        {
            Close();
        }
    }
}