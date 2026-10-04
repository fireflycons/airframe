using System;
using System.Drawing;
using System.Windows.Forms;

namespace Web_Page_Screensaver
{
    /// <summary>
    /// Modal About box crediting the screensaver this one was forked from
    /// </summary>
    public class AboutDialog : Form
    {
        private const string UpstreamUrl = "https://github.com/muro-dot/Webview2_WebPage_Screensaver";

        public AboutDialog()
        {
            var colors = ThemeManager.Colors;

            Text = "About AirFrame Screensaver";
            ClientSize = new Size(520, 300);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Font = new Font("Segoe UI", 9F);
            BackColor = colors.Background;
            ForeColor = colors.TextPrimary;

            try
            {
                Icon = ModernAppIcon.CreateAppIcon(32);
            }
            catch { }

            var lblTitle = new Label
            {
                Text = "AirFrame Screensaver",
                Font = new Font("Segoe UI", 14F, FontStyle.Bold),
                ForeColor = colors.TextPrimary,
                AutoSize = true,
                Location = new Point(24, 18)
            };

            var lblSubtitle = new Label
            {
                Text = "Shows the airframe web UI as a Windows screensaver",
                ForeColor = colors.TextSecondary,
                AutoSize = true,
                Location = new Point(26, 48)
            };

            var card = new ModernCard
            {
                BorderRadius = 8,
                Location = new Point(24, 80),
                Size = new Size(472, 156)
            };

            var lblCredit = new Label
            {
                Text = "This screensaver is a fork of WebView2 Web Page Screensaver by muro-dot, " +
                       "taken at commit 352827d. Many thanks to muro-dot for building and sharing it.\n\n" +
                       "All of the original's functionality remains: it still displays any web pages " +
                       "and dashboards, with its multi-monitor modes, URL rotation, zoom and clock overlay.",
                ForeColor = colors.TextPrimary,
                BackColor = Color.Transparent,
                AutoSize = true,
                MaximumSize = new Size(440, 0),
                Location = new Point(16, 14)
            };

            var lnkUpstream = new LinkLabel
            {
                Text = UpstreamUrl,
                LinkColor = colors.Accent,
                ActiveLinkColor = colors.AccentHover,
                VisitedLinkColor = colors.Accent,
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(16, 124)
            };
            lnkUpstream.LinkClicked += (s, e) =>
            {
                try
                {
                    System.Diagnostics.Process.Start(UpstreamUrl);
                }
                catch { }
            };

            card.Controls.Add(lblCredit);
            card.Controls.Add(lnkUpstream);

            var lblLicense = new Label
            {
                Text = "Original work © 2026 muro-dot, MIT License.",
                ForeColor = colors.TextSecondary,
                AutoSize = true,
                Location = new Point(26, 258)
            };

            var btnClose = new ModernButton
            {
                Text = "Close",
                Style = ModernButtonStyle.Primary,
                BorderRadius = 6,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Size = new Size(98, 32),
                Location = new Point(398, 250),
                DialogResult = DialogResult.OK,
                Cursor = Cursors.Hand
            };

            Controls.Add(lblTitle);
            Controls.Add(lblSubtitle);
            Controls.Add(card);
            Controls.Add(lblLicense);
            Controls.Add(btnClose);

            AcceptButton = btnClose;
            CancelButton = btnClose;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ThemeManager.SetFormTitleBarTheme(Handle, ThemeManager.IsLightTheme);
        }
    }
}
