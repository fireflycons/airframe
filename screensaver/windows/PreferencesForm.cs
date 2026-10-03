using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Web_Page_Screensaver
{
    public partial class PreferencesForm : Form 
    {
        private PreferencesManager prefsManager = new PreferencesManager();
        private List<PrefsByScreenUserControl> screenUserControls;
        private bool isThemeEventRegistered = false;

        public PreferencesForm()
        {
            InitializeComponent();
            try
            {
                Icon = ModernAppIcon.CreateAppIcon(32);
            }
            catch { }
            RemoveExtraTabPages();
            screenTabControl.TabPages[0].Text = "Main Display";
            screenUserControls = new List<PrefsByScreenUserControl>() { prefsByScreenUserControl1 };
            LoadValuesForTab(0);

            // Enable/disable the position combo box according to whether the clock HUD overlay is checked
            cbClockOverlay.CheckedChanged += (s, e) =>
            {
                cmbClockPosition.Enabled = cbClockOverlay.Checked;
            };
        }

        private void PreferencesForm_Load(object sender, EventArgs e)
        {
            cbCloseOnActivity.Checked = prefsManager.CloseOnActivity;
            cbMuteAudio.Checked = prefsManager.MuteAudio;
            cbInPrivate.Checked = prefsManager.InPrivate;
            cbClockOverlay.Checked = prefsManager.ShowClockOverlay;
            cmbClockPosition.Enabled = cbClockOverlay.Checked;

            // 1. Detect the Windows system theme (dark/light) and register for live change events
            RegisterThemeEvents();
            bool isLight = ThemeManager.CheckWindowsLightTheme();
            ThemeManager.IsLightTheme = isLight;
            ApplyTheme(isLight);

            if (Screen.AllScreens.Length <= 1)
            {
                spanScreensButton.Enabled = false;
                mirrorScreensButton.Enabled = false;
                separateScreensButton.Enabled = false;
            }
            else
            {
                SetMultiScreenButtonFromMode();
                ArrangeScreenTabs();
            }

            ApplyTexts();
        }

        private void RegisterThemeEvents()
        {
            if (!isThemeEventRegistered)
            {
                SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
                isThemeEventRegistered = true;
            }
        }

        public void CleanupThemeEvents()
        {
            if (isThemeEventRegistered)
            {
                SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
                isThemeEventRegistered = false;
            }
        }

        private void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            // When a Windows theme (light/dark) change is detected, switch immediately on the UI thread
            if (e.Category == UserPreferenceCategory.General || e.Category == UserPreferenceCategory.Color)
            {
                if (IsHandleCreated && !IsDisposed)
                {
                    try
                    {
                        BeginInvoke((MethodInvoker)(() =>
                        {
                            bool isLight = ThemeManager.CheckWindowsLightTheme();
                            ThemeManager.IsLightTheme = isLight;
                            ApplyTheme(isLight);
                        }));
                    }
                    catch { }
                }
            }
        }

        /// <summary>
        /// Applies dark mode / light mode theme
        /// </summary>
        public void ApplyTheme(bool isLight)
        {
            var colors = ThemeManager.Colors;

            // 1. Apply the theme to the form and the DWM window title bar
            BackColor = colors.Background;
            ForeColor = colors.TextPrimary;
            ThemeManager.SetFormTitleBarTheme(Handle, isLight);

            // 2. Header label
            lblTitle.ForeColor = colors.TextPrimary;
            lblSubtitle.ForeColor = colors.TextSecondary;

            // 3. Multi-monitor card
            multiScreenCard.BackColor = colors.CardBackground;
            multiScreenCard.BorderColor = colors.CardBorder;

            bool isSingleScreen = Screen.AllScreens.Length <= 1;
            Color multiScreenTextColor = isSingleScreen ? colors.TextMuted : colors.TextPrimary;
            lblMultiScreen.ForeColor = multiScreenTextColor;
            spanScreensButton.ForeColor = multiScreenTextColor;
            mirrorScreensButton.ForeColor = multiScreenTextColor;
            separateScreensButton.ForeColor = multiScreenTextColor;

            // 4. Tab control and each tab page
            screenTabControl.BackColor = colors.Background;
            foreach (TabPage tab in screenTabControl.TabPages)
            {
                tab.BackColor = colors.CardBackground;
            }

            // 5. Bottom panel and controls
            cbCloseOnActivity.ForeColor = colors.TextPrimary;
            cbMuteAudio.ForeColor = colors.TextPrimary;
            cbInPrivate.ForeColor = colors.TextPrimary;
            cbClockOverlay.ForeColor = colors.TextPrimary;
            cmbClockPosition.BackColor = colors.InputBackground;
            cmbClockPosition.ForeColor = colors.TextPrimary;

            btnExport.Invalidate();
            btnImport.Invalidate();

            // 6. Propagate the theme to child screen controls
            if (screenUserControls != null)
            {
                foreach (var ctrl in screenUserControls)
                {
                    ctrl.ApplyTheme(isLight);
                }
            }

            // Redraw everything
            Invalidate(true);
        }

        public void ApplyTexts()
        {
            Text = "AirFrame Screensaver Settings";
            lblTitle.Text = "AirFrame Screensaver";
            lblSubtitle.Text = "Display websites and live dashboards with Microsoft WebView2";
            lblMultiScreen.Text = "Multi-Monitor Mode:";

            btnExport.Text = "Export";
            btnImport.Text = "Import";

            spanScreensButton.Text = "Span (All)";
            mirrorScreensButton.Text = "Mirror (Clone)";
            separateScreensButton.Text = "Separate (Each its own list)";

            screenModeTooltip.SetToolTip(spanScreensButton, "Spread a single screen across all monitors");
            screenModeTooltip.SetToolTip(mirrorScreensButton, "Same websites shown on all monitors");
            screenModeTooltip.SetToolTip(separateScreensButton, "Configure individual URL list for each screen");

            cbCloseOnActivity.Text = "Exit on mouse move";
            cbMuteAudio.Text = "Mute Audio";
            cbInPrivate.Text = "InPrivate Browsing";
            cbClockOverlay.Text = "Clock HUD Overlay";

            // Refresh the clock HUD corner position combo box items (preserving the selection)
            int selectedClockIndex = cmbClockPosition.SelectedIndex >= 0
                ? cmbClockPosition.SelectedIndex
                : (int)prefsManager.ClockPositionPref;

            cmbClockPosition.Items.Clear();
            cmbClockPosition.Items.AddRange(new object[] {
                "Bottom-Right (Default)",
                "Bottom-Left",
                "Top-Right",
                "Top-Left"
            });
            cmbClockPosition.SelectedIndex = (selectedClockIndex >= 0 && selectedClockIndex < cmbClockPosition.Items.Count)
                ? selectedClockIndex
                : 0;

            cancelButton.Text = "Cancel";
            okButton.Text = "Save & Apply";

            // Dynamically align top/bottom controls to the text widths (to avoid overlaps)
            AdjustResponsiveLayout();

            // Update tab names
            UpdateTabTitles();

            // Apply the texts to child screen controls
            if (screenUserControls != null)
            {
                foreach (var ctrl in screenUserControls)
                {
                    ctrl.ApplyTexts();
                }
            }
        }

        /// <summary>
        /// Dynamically aligns the multi-monitor radio buttons and bottom option controls to the text widths,
        /// to prevent overlapping and clipped text.
        /// </summary>
        public void AdjustResponsiveLayout()
        {
            // 1. Dynamically align the multi-monitor label and radio buttons
            lblMultiScreen.AutoSize = true;
            spanScreensButton.AutoSize = true;
            mirrorScreensButton.AutoSize = true;
            separateScreensButton.AutoSize = true;

            int multiStartX = lblMultiScreen.Right + 14;
            spanScreensButton.Location = new Point(multiStartX, 12);
            mirrorScreensButton.Location = new Point(spanScreensButton.Right + 14, 12);
            separateScreensButton.Location = new Point(mirrorScreensButton.Right + 14, 12);

            // 2. Dynamically align the bottom panel controls (optimises spacing between columns 1 and 2 and the buttons)
            cbCloseOnActivity.AutoSize = true;
            cbInPrivate.AutoSize = true;
            cbMuteAudio.AutoSize = true;
            cbClockOverlay.AutoSize = true;

            cbCloseOnActivity.Location = new Point(4, 10);
            cbInPrivate.Location = new Point(4, 42);

            int col2Left = Math.Max(cbCloseOnActivity.Right, cbInPrivate.Right) + 24;
            cbMuteAudio.Location = new Point(col2Left, 10);
            cbClockOverlay.Location = new Point(col2Left, 42);

            // Clock position combo box: 180px wide with a safe margin so the English text is fully visible
            cmbClockPosition.Size = new Size(180, 23);
            cmbClockPosition.Location = new Point(cbClockOverlay.Right + 8, 39);

            // 3. Align the controls on the right of the header (GitHub button right margin set to 8px to match the bottom buttons)
            if (headerPanel.Width >= 500)
            {
                int headerRight = headerPanel.Width - 8;
                btnGithub.Location = new Point(headerRight - btnGithub.Width, 11);
            }

            // 4. Align the OK/Cancel buttons on the right and the Backup/Restore buttons just above them (right edges aligned at 8px)
            int panelW = bottomPanel.ClientSize.Width > 0 ? bottomPanel.ClientSize.Width : 812;
            int bottomRight = panelW - 8;
            okButton.Location = new Point(bottomRight - okButton.Width, 38);
            cancelButton.Location = new Point(okButton.Left - 8 - cancelButton.Width, 38);

            // Place Backup/Restore just above Cancel/Save
            btnExport.Location = new Point(cancelButton.Left, 6);
            btnExport.Size = new Size(cancelButton.Width, 28);
            btnImport.Location = new Point(okButton.Left, 6);
            btnImport.Size = new Size(okButton.Width, 28);
        }

        /// <summary>
        /// For automatic build-time screenshots, injects a safe example URL instead of a personal URL and tidies the screen.
        /// </summary>
        public void PrepareForScreenshot(string exampleUrl = "https://example.com/screensaver")
        {
            if (prefsByScreenUserControl1 != null && prefsByScreenUserControl1.lvUrls != null)
            {
                prefsByScreenUserControl1.lvUrls.Items.Clear();
                prefsByScreenUserControl1.lvUrls.Items.Add(exampleUrl);
            }
            cbClockOverlay.Checked = true;
            cmbClockPosition.Enabled = true;
            cmbClockPosition.SelectedIndex = 0;
            ApplyTexts();
        }

        private void LoadValuesForTab(int screenNum)
        {
            if (screenNum < screenUserControls.Count)
            {
                var currentPrefsUserControl = screenUserControls[screenNum];
                loadUrlsForTabToControl(screenNum, currentPrefsUserControl);
                currentPrefsUserControl.nudRotationInterval.Value = Math.Max(1, prefsManager.GetRotationIntervalByScreen(screenNum));
                currentPrefsUserControl.cbRandomize.Checked = prefsManager.GetRandomizeFlagByScreen(screenNum);
                currentPrefsUserControl.cmbUrlMode.SelectedIndex = (int)prefsManager.GetUrlModeByScreen(screenNum);

                // Apply the zoom factor
                int zoom = prefsManager.GetZoomFactorByScreen(screenNum);
                string zoomStr = $"{zoom}%";
                int idx = currentPrefsUserControl.cmbZoom.Items.IndexOf(zoomStr);
                currentPrefsUserControl.cmbZoom.SelectedIndex = idx >= 0 ? idx : 1;

                currentPrefsUserControl.ApplyTexts();
            }
        }

        private void UpdateTabTitles()
        {
            switch (prefsManager.MultiScreenMode)
            {
                case PreferencesManager.MultiScreenModeItem.Span:
                    if (screenTabControl.TabPages.Count > 0)
                        screenTabControl.TabPages[0].Text = "Composite Display";
                    break;

                case PreferencesManager.MultiScreenModeItem.Mirror:
                    if (screenTabControl.TabPages.Count > 0)
                        screenTabControl.TabPages[0].Text = "All Displays (Mirrored)";
                    break;

                case PreferencesManager.MultiScreenModeItem.Separate:
                    for (int i = 0; i < screenTabControl.TabPages.Count; i++)
                    {
                        string primaryIndicator = (i < Screen.AllScreens.Length && Screen.AllScreens[i].Primary)
                            ? " (Primary)"
                            : string.Empty;
                        screenTabControl.TabPages[i].Text = string.Format("Screen {0}{1}", i + 1, primaryIndicator);
                    }
                    break;
            }
        }

        private void ArrangeScreenTabs()
        {
            switch (prefsManager.MultiScreenMode)
            {
                case PreferencesManager.MultiScreenModeItem.Span:
                    RemoveExtraTabPages();
                    screenUserControls = new List<PrefsByScreenUserControl>() { prefsByScreenUserControl1 };
                    LoadValuesForTab(0);
                    break;

                case PreferencesManager.MultiScreenModeItem.Mirror:
                    RemoveExtraTabPages();
                    screenUserControls = new List<PrefsByScreenUserControl>() { prefsByScreenUserControl1 };
                    LoadValuesForTab(0);
                    break;

                case PreferencesManager.MultiScreenModeItem.Separate:
                    for (int i = 0; i < Screen.AllScreens.Length; i++)
                    {
                        TabPage tabPage = null; 

                        if (i >= screenTabControl.TabPages.Count)
                        {
                            tabPage = new TabPage();
                            tabPage.BackColor = ThemeManager.Colors.CardBackground;
                            tabPage.Padding = new Padding(12);
                            screenTabControl.TabPages.Add(tabPage);

                            if (i > 0)
                            {
                                var prefsByScreenUserControl = new PrefsByScreenUserControl
                                {
                                    Name = string.Format("prefsByScreenUserControl{0}", i + 1),
                                    Dock = DockStyle.Fill,
                                    BackColor = ThemeManager.Colors.CardBackground,
                                    Font = new Font("Segoe UI", 9f)
                                };
                                prefsByScreenUserControl.lvUrls.ContextMenuStrip =
                                    prefsByScreenUserControl1.lvUrls.ContextMenuStrip;
                                prefsByScreenUserControl.ApplyTheme(ThemeManager.IsLightTheme);
                                screenUserControls.Add(prefsByScreenUserControl);
                                tabPage.Controls.Add(prefsByScreenUserControl);
                            }
                        }
                        else if (screenTabControl.TabPages.Count == 1)
                        {
                            tabPage = screenTabControl.TabPages[0];
                            screenUserControls = new List<PrefsByScreenUserControl>() { prefsByScreenUserControl1 };
                        }

                        LoadValuesForTab(i);
                    }
                    break;
            }

            UpdateTabTitles();
        }

        private void RemoveExtraTabPages()
        {
            while (screenTabControl.TabPages.Count > 1)
            {
                screenTabControl.TabPages.RemoveAt(screenTabControl.TabPages.Count - 1);
            }
        }

        private void SetMultiScreenButtonFromMode()
        {
            switch (prefsManager.MultiScreenMode)
            {
                case PreferencesManager.MultiScreenModeItem.Span:
                    spanScreensButton.Checked = true;
                    break;
                case PreferencesManager.MultiScreenModeItem.Mirror:
                    mirrorScreensButton.Checked = true;
                    break;
                case PreferencesManager.MultiScreenModeItem.Separate:
                    separateScreensButton.Checked = true;
                    break;
            }
        }

        private void setMultiScreenModeFromButtonState()
        {
            if (spanScreensButton.Checked)
            {
                prefsManager.MultiScreenMode = PreferencesManager.MultiScreenModeItem.Span;
            }
            else if (mirrorScreensButton.Checked)
            {
                prefsManager.MultiScreenMode = PreferencesManager.MultiScreenModeItem.Mirror;
            }
            else
            {
                prefsManager.MultiScreenMode = PreferencesManager.MultiScreenModeItem.Separate;
            }

            prefsManager.ResetEffectiveScreensList();
        }

        private void readBackValuesFromUI()
        {
            try
            {
                prefsManager.CloseOnActivity = cbCloseOnActivity.Checked;
                prefsManager.MuteAudio = cbMuteAudio.Checked;
                prefsManager.InPrivate = cbInPrivate.Checked;
                prefsManager.ShowClockOverlay = cbClockOverlay.Checked;
                if (cmbClockPosition.SelectedIndex >= 0)
                {
                    prefsManager.ClockPositionPref = (PreferencesManager.ClockPosition)cmbClockPosition.SelectedIndex;
                }

                for (var i = 0; i < screenUserControls.Count; i++)
                {
                    var currentPrefsUserControl = screenUserControls[i];
                    List<string> urls = (from ListViewItem lvUrlsItem in currentPrefsUserControl.lvUrls.Items
                        select lvUrlsItem.Text).ToList();
                    prefsManager.SetUrlsForScreen(i, urls);
                    prefsManager.SetRotationIntervalForScreen(i,
                        (int) currentPrefsUserControl.nudRotationInterval.Value);
                    prefsManager.SetRandomizeFlagForScreen(i, currentPrefsUserControl.cbRandomize.Checked);
                    if (currentPrefsUserControl.cmbUrlMode.SelectedIndex >= 0)
                    {
                        prefsManager.SetUrlModeForScreen(i,
                            (PreferencesManager.UrlDisplayMode)currentPrefsUserControl.cmbUrlMode.SelectedIndex);
                    }

                    if (currentPrefsUserControl.cmbZoom.SelectedItem != null)
                    {
                        string zoomStr = currentPrefsUserControl.cmbZoom.SelectedItem.ToString().TrimEnd('%');
                        if (int.TryParse(zoomStr, out int z))
                        {
                            prefsManager.SetZoomFactorForScreen(i, z);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }

        private void loadUrlsForTabToControl(int screenNum, PrefsByScreenUserControl currentPrefsUserControl)
        {
            currentPrefsUserControl.lvUrls.Items.Clear();

            var urls = prefsManager.GetUrlsByScreen(screenNum);

            foreach (var url in urls)
            {
                currentPrefsUserControl.lvUrls.Items.Add(url);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            CleanupThemeEvents();

            if (DialogResult == DialogResult.OK)
            {
                try
                {
                    readBackValuesFromUI();
                    prefsManager.SavePreferences();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[PreferencesForm.OnClosed] Error saving settings: {ex.Message}");
                }
            }

            base.OnClosed(e);
        }

        private void btnExport_Click(object sender, EventArgs e)
        {
            readBackValuesFromUI();
            ConfigExportImport.ExportToFile(prefsManager, this);
        }

        private void btnImport_Click(object sender, EventArgs e)
        {
            if (ConfigExportImport.ImportFromFile(prefsManager, this))
            {
                cbCloseOnActivity.Checked = prefsManager.CloseOnActivity;
                cbMuteAudio.Checked = prefsManager.MuteAudio;
                cbInPrivate.Checked = prefsManager.InPrivate;
                cbClockOverlay.Checked = prefsManager.ShowClockOverlay;
                cmbClockPosition.Enabled = cbClockOverlay.Checked;
                cmbClockPosition.SelectedIndex = (int)prefsManager.ClockPositionPref;
                SetMultiScreenButtonFromMode();
                ArrangeScreenTabs();
                ApplyTexts();
            }
        }

        private void btnGithub_Click(object sender, EventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start("https://github.com/fireflycons/airframe");
            }
            catch { }
        }

        private void okButton_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.OK;
            Close();
        }

        private void cancelButton_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }

        private void anyMultiScreenModeButton_Click(object sender, EventArgs e)
        {
            readBackValuesFromUI();
            setMultiScreenModeFromButtonState();
            ArrangeScreenTabs();
        }
    }
}
