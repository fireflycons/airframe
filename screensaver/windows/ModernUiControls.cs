using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Web_Page_Screensaver
{
    /// <summary>
    /// Modern theme colour palette interface and data model.
    /// Manages every UI colour token for dark and light (white) mode consistently.
    /// </summary>
    public class ThemePalette
    {
        public Color Background { get; set; }           // Main background of the whole form
        public Color CardBackground { get; set; }       // Card/panel background
        public Color CardBorder { get; set; }           // Card/panel border
        public Color InputBackground { get; set; }      // Text box/list background
        public Color InputBorder { get; set; }          // Text box/list border

        public Color TextPrimary { get; set; }          // Primary text
        public Color TextSecondary { get; set; }        // Secondary text
        public Color TextMuted { get; set; }            // Disabled text

        public Color Accent { get; set; }               // Brand accent (Blue-600)
        public Color AccentHover { get; set; }          // Accent hover
        public Color AccentPressed { get; set; }        // Accent pressed

        public Color SecondaryBtn { get; set; }         // Secondary button default
        public Color SecondaryBtnHover { get; set; }    // Secondary button hover
        public Color SecondaryBtnPressed { get; set; }  // Secondary button pressed
        public Color SecondaryBtnBorder { get; set; }   // Secondary button border

        public Color Danger { get; set; }               // Warning/delete default
        public Color DangerHover { get; set; }          // Warning/delete hover
        public Color DangerPressed { get; set; }        // Warning/delete pressed

        public Color TabIndicator { get; set; }         // Accent bar under the active tab
    }

    /// <summary>
    /// Detects the Windows system theme and switches automatically between dark and light mode
    /// </summary>
    public static class ThemeManager
    {
        // 1. Dark theme colours (Zinc/Slate Deep Dark)
        public static readonly ThemePalette Dark = new ThemePalette
        {
            Background = Color.FromArgb(20, 20, 23),
            CardBackground = Color.FromArgb(28, 28, 32),
            CardBorder = Color.FromArgb(46, 46, 54),
            InputBackground = Color.FromArgb(15, 15, 17),
            InputBorder = Color.FromArgb(52, 52, 60),

            TextPrimary = Color.FromArgb(244, 244, 245),
            TextSecondary = Color.FromArgb(161, 161, 170),
            TextMuted = Color.FromArgb(113, 113, 122),

            Accent = Color.FromArgb(41, 103, 195),             // Calm, refined slate sapphire (toned down from overly vivid)
            AccentHover = Color.FromArgb(56, 125, 225),        // Soft accent hover
            AccentPressed = Color.FromArgb(30, 80, 160),

            SecondaryBtn = Color.FromArgb(36, 36, 42),
            SecondaryBtnHover = Color.FromArgb(50, 50, 58),
            SecondaryBtnPressed = Color.FromArgb(28, 28, 33),
            SecondaryBtnBorder = Color.FromArgb(46, 46, 54),

            Danger = Color.FromArgb(185, 45, 45),              // Restrained crimson
            DangerHover = Color.FromArgb(215, 55, 55),         // Warning red on hover
            DangerPressed = Color.FromArgb(160, 30, 30),

            TabIndicator = Color.FromArgb(56, 125, 225)
        };

        // 2. Light theme colours (Soft Clean White & Modern Slate)
        public static readonly ThemePalette Light = new ThemePalette
        {
            Background = Color.FromArgb(245, 246, 250),        // Soft off-white background
            CardBackground = Color.FromArgb(255, 255, 255),    // Pure white card
            CardBorder = Color.FromArgb(226, 232, 240),        // Subtle light border
            InputBackground = Color.FromArgb(248, 250, 252),   // Clean input field background
            InputBorder = Color.FromArgb(203, 213, 225),       // Light input border

            TextPrimary = Color.FromArgb(15, 23, 42),          // Deep slate black (best legibility)
            TextSecondary = Color.FromArgb(71, 85, 105),       // Soft slate secondary text
            TextMuted = Color.FromArgb(148, 163, 184),         // Disabled text

            Accent = Color.FromArgb(30, 41, 59),               // Modern slate deep navy (a premium tone instead of primary blue)
            AccentHover = Color.FromArgb(51, 65, 85),          // Soft slate 700
            AccentPressed = Color.FromArgb(15, 23, 42),

            SecondaryBtn = Color.FromArgb(241, 245, 249),
            SecondaryBtnHover = Color.FromArgb(226, 232, 240),
            SecondaryBtnPressed = Color.FromArgb(203, 213, 225),
            SecondaryBtnBorder = Color.FromArgb(226, 232, 240),

            Danger = Color.FromArgb(210, 45, 45),              // Restrained soft red
            DangerHover = Color.FromArgb(225, 60, 60),
            DangerPressed = Color.FromArgb(180, 35, 35),

            TabIndicator = Color.FromArgb(30, 41, 59)
        };

        private static bool isLightTheme = false;

        public static bool IsLightTheme
        {
            get => isLightTheme;
            set
            {
                if (isLightTheme != value)
                {
                    isLightTheme = value;
                    ThemeChanged?.Invoke();
                }
            }
        }

        public static ThemePalette Colors => isLightTheme ? Light : Dark;

        public static event Action ThemeChanged;

        /// <summary>
        /// Reads the Windows 10/11 registry to check whether the system app theme is light mode.
        /// </summary>
        public static bool CheckWindowsLightTheme()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (key != null)
                    {
                        object value = key.GetValue("AppsUseLightTheme");
                        if (value is int intVal)
                        {
                            return intVal == 1;
                        }
                    }
                }
            }
            catch { }
            return false; // Default is dark
        }

        // Controls the dark/light theme of the Windows title bar through the DWM API
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        /// <summary>
        /// Changes the window's title bar (caption and close button) to match the current theme.
        /// </summary>
        public static void SetFormTitleBarTheme(IntPtr hWnd, bool isLight)
        {
            if (hWnd == IntPtr.Zero) return;

            try
            {
                int useDarkMode = isLight ? 0 : 1;
                // Windows 10 20H1 and later, and Windows 11 (Attr 20)
                int result = DwmSetWindowAttribute(hWnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDarkMode, sizeof(int));
                if (result != 0)
                {
                    // Older Windows 10 (Attr 19)
                    DwmSetWindowAttribute(hWnd, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref useDarkMode, sizeof(int));
                }
            }
            catch { }
        }
    }

    /// <summary>
    /// DarkColors proxy for full backward compatibility with existing code.
    /// Maps dynamically to ThemeManager.Colors so live theme changes are reflected transparently.
    /// </summary>
    public static class DarkColors
    {
        public static Color Background => ThemeManager.Colors.Background;
        public static Color CardBackground => ThemeManager.Colors.CardBackground;
        public static Color CardBorder => ThemeManager.Colors.CardBorder;
        public static Color InputBackground => ThemeManager.Colors.InputBackground;
        public static Color InputBorder => ThemeManager.Colors.InputBorder;

        public static Color TextPrimary => ThemeManager.Colors.TextPrimary;
        public static Color TextSecondary => ThemeManager.Colors.TextSecondary;
        public static Color TextMuted => ThemeManager.Colors.TextMuted;

        public static Color Accent => ThemeManager.Colors.Accent;
        public static Color AccentHover => ThemeManager.Colors.AccentHover;
        public static Color AccentPressed => ThemeManager.Colors.AccentPressed;

        public static Color SecondaryBtn => ThemeManager.Colors.SecondaryBtn;
        public static Color SecondaryBtnHover => ThemeManager.Colors.SecondaryBtnHover;
        public static Color SecondaryBtnPressed => ThemeManager.Colors.SecondaryBtnPressed;

        public static Color Danger => ThemeManager.Colors.Danger;
        public static Color DangerHover => ThemeManager.Colors.DangerHover;
        public static Color DangerPressed => ThemeManager.Colors.DangerPressed;
    }

    /// <summary>
    /// Modern-style flat rounded button control
    /// </summary>
    public enum ModernButtonStyle
    {
        Primary,
        Secondary,
        Danger,
        Ghost,
        Segment
    }

    public class ModernButton : Button
    {
        private ModernButtonStyle style = ModernButtonStyle.Secondary;
        private int borderRadius = 6;
        private bool isHovered = false;
        private bool isPressed = false;
        private bool isSelected = false;

        public ModernButtonStyle Style
        {
            get => style;
            set { style = value; Invalidate(); }
        }

        public int BorderRadius
        {
            get => borderRadius;
            set { borderRadius = value; Invalidate(); }
        }

        public bool IsSelected
        {
            get => isSelected;
            set { isSelected = value; Invalidate(); }
        }

        public ModernButton()
        {
            SetStyle(ControlStyles.UserPaint | 
                     ControlStyles.AllPaintingInWmPaint | 
                     ControlStyles.OptimizedDoubleBuffer | 
                     ControlStyles.ResizeRedraw | 
                     ControlStyles.SupportsTransparentBackColor, true);

            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            FlatAppearance.MouseDownBackColor = Color.Transparent;
            FlatAppearance.MouseOverBackColor = Color.Transparent;
            FlatAppearance.CheckedBackColor = Color.Transparent;
            UseVisualStyleBackColor = false;
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            Font = new Font("Segoe UI", 9f, FontStyle.Regular);
            Size = new Size(80, 30);

            ThemeManager.ThemeChanged += OnThemeChanged;
        }

        private void OnThemeChanged()
        {
            if (IsDisposed) return;
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ThemeManager.ThemeChanged -= OnThemeChanged;
            }
            base.Dispose(disposing);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            isHovered = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            isHovered = false;
            isPressed = false;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs mevent)
        {
            base.OnMouseDown(mevent);
            if (mevent.Button == MouseButtons.Left)
            {
                isPressed = true;
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs mevent)
        {
            base.OnMouseUp(mevent);
            isPressed = false;
            Invalidate();
        }

        protected override void OnPaintBackground(PaintEventArgs pevent)
        {
            // Suppress the default rectangular background
        }

        private Color GetSolidParentBackColor()
        {
            Control c = Parent;
            while (c != null && (c.BackColor == Color.Transparent || c.BackColor.A < 255))
            {
                c = c.Parent;
            }
            return c != null ? c.BackColor : ThemeManager.Colors.Background;
        }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            var g = pevent.Graphics;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            // 1. Fill with the parent control's background colour
            using (var parentBrush = new SolidBrush(GetSolidParentBackColor()))
            {
                g.FillRectangle(parentBrush, ClientRectangle);
            }

            var colors = ThemeManager.Colors;
            Color bgColor;
            Color textColor = colors.TextPrimary;
            Color borderColor = Color.Transparent;

            switch (style)
            {
                case ModernButtonStyle.Primary:
                    bgColor = isPressed ? colors.AccentPressed : (isHovered ? colors.AccentHover : colors.Accent);
                    textColor = Color.White;
                    break;

                case ModernButtonStyle.Danger:
                    if (isPressed)
                    {
                        bgColor = colors.DangerPressed;
                        textColor = Color.White;
                    }
                    else if (isHovered)
                    {
                        bgColor = colors.DangerHover;
                        textColor = Color.White;
                    }
                    else
                    {
                        // Normally a secondary surface that blends with its surroundings, with soft red text
                        bgColor = colors.SecondaryBtn;
                        textColor = ThemeManager.IsLightTheme ? Color.FromArgb(195, 40, 40) : Color.FromArgb(248, 113, 113);
                        borderColor = colors.SecondaryBtnBorder;
                    }
                    break;

                case ModernButtonStyle.Segment:
                    if (isSelected)
                    {
                        bgColor = colors.Accent;
                        textColor = Color.White;
                    }
                    else
                    {
                        bgColor = isHovered ? colors.SecondaryBtnHover : colors.SecondaryBtn;
                        textColor = isHovered ? colors.TextPrimary : colors.TextSecondary;
                        borderColor = colors.CardBorder;
                    }
                    break;

                case ModernButtonStyle.Ghost:
                    bgColor = isPressed ? colors.SecondaryBtnPressed : (isHovered ? colors.SecondaryBtnHover : Color.Transparent);
                    textColor = isHovered ? colors.TextPrimary : colors.TextSecondary;
                    borderColor = isHovered ? colors.CardBorder : Color.Transparent;
                    break;

                case ModernButtonStyle.Secondary:
                default:
                    bgColor = isPressed ? colors.SecondaryBtnPressed : (isHovered ? colors.SecondaryBtnHover : colors.SecondaryBtn);
                    borderColor = colors.CardBorder;
                    textColor = colors.TextPrimary;
                    break;
            }

            if (!Enabled)
            {
                bgColor = ThemeManager.IsLightTheme ? Color.FromArgb(241, 245, 249) : Color.FromArgb(28, 28, 33);
                textColor = colors.TextMuted;
                borderColor = Color.Transparent;
            }

            var rect = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            using (var path = GetRoundedRectangleF(rect, borderRadius))
            {
                using (var brush = new SolidBrush(bgColor))
                {
                    g.FillPath(brush, path);
                }

                if (borderColor != Color.Transparent)
                {
                    using (var pen = new Pen(borderColor, 1f))
                    {
                        g.DrawPath(pen, path);
                    }
                }
            }

            // Draw the text
            var textRect = new Rectangle(0, 0, Width, Height);
            TextRenderer.DrawText(g, Text, Font, textRect, textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        }

        public static GraphicsPath GetRoundedRectangleF(RectangleF rect, float radius)
        {
            var path = new GraphicsPath();
            float diameter = radius * 2f;

            if (radius <= 0f || diameter >= rect.Width || diameter >= rect.Height)
            {
                path.AddRectangle(rect);
                return path;
            }

            var arc = new RectangleF(rect.Location, new SizeF(diameter, diameter));

            // Top-left
            path.AddArc(arc, 180, 90);

            // Top-right
            arc.X = rect.Right - diameter;
            path.AddArc(arc, 270, 90);

            // Bottom-right
            arc.Y = rect.Bottom - diameter;
            path.AddArc(arc, 0, 90);

            // Bottom-left
            arc.X = rect.Left;
            path.AddArc(arc, 90, 90);

            path.CloseFigure();
            return path;
        }
    }

    /// <summary>
    /// Modern card-style container panel
    /// </summary>
    public class ModernCard : Panel
    {
        private int borderRadius = 8;
        public Color BorderColor { get; set; }

        public int BorderRadius
        {
            get => borderRadius;
            set { borderRadius = value; Invalidate(); }
        }

        public ModernCard()
        {
            BorderColor = ThemeManager.Colors.CardBorder;
            SetStyle(ControlStyles.UserPaint | 
                     ControlStyles.AllPaintingInWmPaint | 
                     ControlStyles.OptimizedDoubleBuffer | 
                     ControlStyles.ResizeRedraw | 
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = ThemeManager.Colors.CardBackground;
            Padding = new Padding(12);

            ThemeManager.ThemeChanged += OnThemeChanged;
        }

        private void OnThemeChanged()
        {
            if (IsDisposed) return;
            BorderColor = ThemeManager.Colors.CardBorder;
            BackColor = ThemeManager.Colors.CardBackground;
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ThemeManager.ThemeChanged -= OnThemeChanged;
            }
            base.Dispose(disposing);
        }

        protected override void OnPaintBackground(PaintEventArgs pevent)
        {
            // Suppress the default rectangular background
        }

        private Color GetSolidParentBackColor()
        {
            Control c = Parent;
            while (c != null && (c.BackColor == Color.Transparent || c.BackColor.A < 255))
            {
                c = c.Parent;
            }
            return c != null ? c.BackColor : ThemeManager.Colors.Background;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            using (var parentBrush = new SolidBrush(GetSolidParentBackColor()))
            {
                g.FillRectangle(parentBrush, ClientRectangle);
            }

            var rect = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            using (var path = ModernButton.GetRoundedRectangleF(rect, borderRadius))
            {
                using (var brush = new SolidBrush(BackColor))
                {
                    g.FillPath(brush, path);
                }

                if (BorderColor != Color.Transparent)
                {
                    using (var pen = new Pen(BorderColor, 1f))
                    {
                        g.DrawPath(pen, path);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Modern flat tab control
    /// </summary>
    public class ModernTabControl : TabControl
    {
        public ModernTabControl()
        {
            SetStyle(ControlStyles.UserPaint | 
                     ControlStyles.AllPaintingInWmPaint | 
                     ControlStyles.OptimizedDoubleBuffer | 
                     ControlStyles.ResizeRedraw, true);
            DrawMode = TabDrawMode.OwnerDrawFixed;
            SizeMode = TabSizeMode.Normal;
            ItemSize = new Size(140, 36);
            Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);

            ThemeManager.ThemeChanged += OnThemeChanged;
        }

        private void OnThemeChanged()
        {
            if (IsDisposed) return;
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ThemeManager.ThemeChanged -= OnThemeChanged;
            }
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            var colors = ThemeManager.Colors;

            // Clear the whole background
            using (var bgBrush = new SolidBrush(colors.Background))
            {
                g.FillRectangle(bgBrush, ClientRectangle);
            }

            // Line under the tab headers
            using (var linePen = new Pen(colors.CardBorder, 1f))
            {
                g.DrawLine(linePen, 0, ItemSize.Height + 2, Width, ItemSize.Height + 2);
            }

            for (int i = 0; i < TabCount; i++)
            {
                var tabRect = GetTabRect(i);
                bool isSelected = (SelectedIndex == i);

                // Selected tab background and active indicator
                if (isSelected)
                {
                    using (var tabBg = new SolidBrush(colors.CardBackground))
                    {
                        g.FillRectangle(tabBg, tabRect.X, tabRect.Y, tabRect.Width, tabRect.Height + 2);
                    }

                    // Thin top/left/right border
                    using (var borderPen = new Pen(colors.CardBorder, 1f))
                    {
                        g.DrawLine(borderPen, tabRect.X, tabRect.Y, tabRect.X, tabRect.Bottom + 1);
                        g.DrawLine(borderPen, tabRect.Right, tabRect.Y, tabRect.Right, tabRect.Bottom + 1);
                    }

                    // Bottom accent bar
                    using (var accentBrush = new SolidBrush(colors.TabIndicator))
                    {
                        g.FillRectangle(accentBrush, tabRect.X, tabRect.Bottom, tabRect.Width, 3);
                    }
                }

                Color textColor = isSelected ? colors.TextPrimary : colors.TextSecondary;
                var font = isSelected ? new Font(Font, FontStyle.Bold) : Font;

                TextRenderer.DrawText(g, TabPages[i].Text, font, tabRect, textColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            }
        }
    }

    /// <summary>
    /// Modern-style radio button (theme-aware)
    /// </summary>
    public class ModernRadioButton : RadioButton
    {
        private bool isHovered = false;

        public ModernRadioButton()
        {
            SetStyle(ControlStyles.UserPaint | 
                     ControlStyles.AllPaintingInWmPaint | 
                     ControlStyles.OptimizedDoubleBuffer | 
                     ControlStyles.ResizeRedraw | 
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            Font = new Font("Segoe UI", 9f);
            ForeColor = ThemeManager.Colors.TextPrimary;

            ThemeManager.ThemeChanged += OnThemeChanged;
        }

        private void OnThemeChanged()
        {
            if (IsDisposed) return;
            ForeColor = ThemeManager.Colors.TextPrimary;
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ThemeManager.ThemeChanged -= OnThemeChanged;
            }
            base.Dispose(disposing);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            isHovered = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            isHovered = false;
            Invalidate();
        }

        protected override void OnPaintBackground(PaintEventArgs pevent)
        {
            // Suppress the default rectangular background
        }

        private Color GetParentBackColor()
        {
            Control c = Parent;
            while (c != null && (c.BackColor == Color.Transparent || c.BackColor.A < 255))
            {
                c = c.Parent;
            }
            return c != null ? c.BackColor : ThemeManager.Colors.Background;
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            Size textSize = TextRenderer.MeasureText(Text, Font);
            return new Size(textSize.Width + 28, Math.Max(textSize.Height + 4, 24));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            var colors = ThemeManager.Colors;

            using (var parentBrush = new SolidBrush(GetParentBackColor()))
            {
                g.FillRectangle(parentBrush, ClientRectangle);
            }

            float circleSize = 16f;
            float cy = (Height - circleSize) / 2f;
            var circleRect = new RectangleF(2f, cy, circleSize, circleSize);

            // Circular background
            using (var bgBrush = new SolidBrush(colors.InputBackground))
            {
                g.FillEllipse(bgBrush, circleRect);
            }

            // Circular border
            Color circleBorder = Checked ? colors.Accent : (isHovered ? colors.AccentHover : colors.CardBorder);
            using (var pen = new Pen(circleBorder, 1.2f))
            {
                g.DrawEllipse(pen, circleRect);
            }

            // Inner dot when checked
            if (Checked)
            {
                float dotSize = 8f;
                float dxy = (circleSize - dotSize) / 2f;
                var dotRect = new RectangleF(circleRect.X + dxy, circleRect.Y + dxy, dotSize, dotSize);

                using (var dotBrush = new SolidBrush(colors.Accent))
                {
                    g.FillEllipse(dotBrush, dotRect);
                }
            }

            // Draw the text
            Color textColor = Enabled ? (isHovered ? colors.AccentHover : colors.TextPrimary) : colors.TextMuted;
            var textRect = new Rectangle((int)(circleSize + 8), 0, Width - (int)(circleSize + 8), Height);
            TextRenderer.DrawText(g, Text, Font, textRect, textColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        }
    }

    /// <summary>
    /// Modern-style check box (theme-aware)
    /// </summary>
    public class ModernCheckBox : CheckBox
    {
        private bool isHovered = false;

        public ModernCheckBox()
        {
            SetStyle(ControlStyles.UserPaint | 
                     ControlStyles.AllPaintingInWmPaint | 
                     ControlStyles.OptimizedDoubleBuffer | 
                     ControlStyles.ResizeRedraw | 
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            Font = new Font("Segoe UI", 9f);
            ForeColor = ThemeManager.Colors.TextPrimary;

            ThemeManager.ThemeChanged += OnThemeChanged;
        }

        private void OnThemeChanged()
        {
            if (IsDisposed) return;
            ForeColor = ThemeManager.Colors.TextPrimary;
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ThemeManager.ThemeChanged -= OnThemeChanged;
            }
            base.Dispose(disposing);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            isHovered = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            isHovered = false;
            Invalidate();
        }

        protected override void OnPaintBackground(PaintEventArgs pevent)
        {
            // Suppress the default rectangular background
        }

        private Color GetParentBackColor()
        {
            Control c = Parent;
            while (c != null && (c.BackColor == Color.Transparent || c.BackColor.A < 255))
            {
                c = c.Parent;
            }
            return c != null ? c.BackColor : ThemeManager.Colors.Background;
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            Size textSize = TextRenderer.MeasureText(Text, Font);
            return new Size(textSize.Width + 28, Math.Max(textSize.Height + 4, 24));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            var colors = ThemeManager.Colors;

            using (var parentBrush = new SolidBrush(GetParentBackColor()))
            {
                g.FillRectangle(parentBrush, ClientRectangle);
            }

            float boxSize = 16f;
            float by = (Height - boxSize) / 2f;
            var boxRect = new RectangleF(2f, by, boxSize, boxSize);
            float r = 4f;

            using (var path = ModernButton.GetRoundedRectangleF(boxRect, r))
            {
                if (Checked)
                {
                    // Checked: accent background with a white check mark
                    using (var fillBrush = new SolidBrush(colors.Accent))
                    {
                        g.FillPath(fillBrush, path);
                    }

                    using (var checkPen = new Pen(Color.White, 2.0f))
                    {
                        checkPen.StartCap = LineCap.Round;
                        checkPen.EndCap = LineCap.Round;
                        checkPen.LineJoin = LineJoin.Round;

                        var pt1 = new PointF(boxRect.X + 3.8f, boxRect.Y + 8.2f);
                        var pt2 = new PointF(boxRect.X + 6.8f, boxRect.Y + 11.5f);
                        var pt3 = new PointF(boxRect.X + 12.2f, boxRect.Y + 4.8f);

                        g.DrawLines(checkPen, new PointF[] { pt1, pt2, pt3 });
                    }
                }
                else
                {
                    // Unchecked: input field background with card border
                    using (var bgBrush = new SolidBrush(colors.InputBackground))
                    {
                        g.FillPath(bgBrush, path);
                    }

                    Color boxBorder = isHovered ? colors.AccentHover : colors.CardBorder;
                    using (var pen = new Pen(boxBorder, 1.2f))
                    {
                        g.DrawPath(pen, path);
                    }
                }
            }

            // Draw the text
            Color textColor = Enabled ? (isHovered ? colors.AccentHover : colors.TextPrimary) : colors.TextMuted;
            var textRect = new Rectangle((int)(boxSize + 8), 0, Width - (int)(boxSize + 8), Height);
            TextRenderer.DrawText(g, Text, Font, textRect, textColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        }
    }

    /// <summary>
    /// Generator for the modern web screensaver app logo and title bar icon.
    /// Renders a refined, modern Fluent-style icon combining a web globe, a monitor (screensaver) and starlight sparkles.
    /// </summary>
    public static class ModernAppIcon
    {
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool DestroyIcon(IntPtr handle);

        public static Icon CreateAppIcon(int size = 32)
        {
            using (var bmp = CreateAppBitmap(size))
            {
                IntPtr hIcon = bmp.GetHicon();
                Icon temp = Icon.FromHandle(hIcon);
                Icon cloned = (Icon)temp.Clone();
                DestroyIcon(hIcon);
                return cloned;
            }
        }

        public static Bitmap CreateAppBitmap(int size)
        {
            var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);

                float s = size / 256.0f;

                // 1. Rounded app tile base
                float margin = 8f * s;
                float w = size - (margin * 2);
                var tileRect = new RectangleF(margin, margin, w, w);
                float radius = 54f * s;

                using (var path = ModernButton.GetRoundedRectangleF(tileRect, radius))
                {
                    // Deep indigo and sapphire gradient
                    using (var brush = new LinearGradientBrush(
                        new PointF(margin, margin),
                        new PointF(size - margin, size - margin),
                        Color.FromArgb(41, 112, 226),
                        Color.FromArgb(15, 23, 42)))
                    {
                        g.FillPath(brush, path);
                    }

                    // Subtle outline
                    using (var borderPen = new Pen(Color.FromArgb(110, 255, 255, 255), Math.Max(1f, 3f * s)))
                    {
                        g.DrawPath(borderPen, path);
                    }
                }

                // 2. Browser window frame (dark glass look)
                float winX = 32f * s;
                float winY = 32f * s;
                float winW = 192f * s;
                float winH = 192f * s;
                var winRect = new RectangleF(winX, winY, winW, winH);

                using (var winPath = ModernButton.GetRoundedRectangleF(winRect, 30f * s))
                {
                    using (var winBg = new SolidBrush(Color.FromArgb(140, 10, 16, 32)))
                    {
                        g.FillPath(winBg, winPath);
                    }
                    using (var winPen = new Pen(Color.FromArgb(100, 255, 255, 255), Math.Max(1f, 2.5f * s)))
                    {
                        g.DrawPath(winPen, winPath);
                    }
                }

                // 3. Browser header bar and traffic-light dots
                float dotY = 52f * s;
                float dotSize = 11f * s;
                using (var d1 = new SolidBrush(Color.FromArgb(245, 108, 108)))
                using (var d2 = new SolidBrush(Color.FromArgb(230, 162, 60)))
                using (var d3 = new SolidBrush(Color.FromArgb(103, 194, 58)))
                {
                    g.FillEllipse(d1, 50f * s, dotY, dotSize, dotSize);
                    g.FillEllipse(d2, 70f * s, dotY, dotSize, dotSize);
                    g.FillEllipse(d3, 90f * s, dotY, dotSize, dotSize);
                }

                // Header divider
                using (var linePen = new Pen(Color.FromArgb(60, 255, 255, 255), Math.Max(1f, 2f * s)))
                {
                    g.DrawLine(linePen, winX + 10f * s, 76f * s, winX + winW - 10f * s, 76f * s);
                }

                // 4. Centre: a refined web globe
                float cx = 128f * s;
                float cy = 144f * s;
                float r = 46f * s;

                // Globe outer circle
                using (var globePen = new Pen(Color.FromArgb(250, 255, 255, 255), Math.Max(1.4f, 5f * s)))
                {
                    g.DrawEllipse(globePen, cx - r, cy - r, r * 2, r * 2);
                }

                // Globe meridians and parallels
                using (var gridPen = new Pen(Color.FromArgb(180, 180, 220, 255), Math.Max(1f, 3f * s)))
                {
                    g.DrawLine(gridPen, cx - r, cy, cx + r, cy);
                    g.DrawEllipse(gridPen, cx - (r * 0.5f), cy - r, r, r * 2);
                }

                // 5. Glowing starlight sparkles representing the screensaver
                float spX = 180f * s;
                float spY = 96f * s;
                float spSize = 16f * s;
                using (var spBrush = new SolidBrush(Color.FromArgb(255, 255, 255, 255)))
                {
                    var path = new GraphicsPath();
                    float inR = spSize * 0.32f;
                    path.AddLines(new PointF[] {
                        new PointF(spX, spY - spSize),
                        new PointF(spX + inR, spY - inR),
                        new PointF(spX + spSize, spY),
                        new PointF(spX + inR, spY + inR),
                        new PointF(spX, spY + spSize),
                        new PointF(spX - inR, spY + inR),
                        new PointF(spX - spSize, spY),
                        new PointF(spX - inR, spY - inR)
                    });
                    path.CloseFigure();
                    g.FillPath(spBrush, path);
                }
            }
            return bmp;
        }

        /// <summary>
        /// Saves as a standard Windows multi-resolution .ico file
        /// </summary>
        public static void SaveIco(string outputPath, int[] sizes)
        {
            using (var fs = new System.IO.FileStream(outputPath, System.IO.FileMode.Create, System.IO.FileAccess.Write))
            using (var bw = new System.IO.BinaryWriter(fs))
            {
                var pngDatas = new byte[sizes.Length][];
                for (int i = 0; i < sizes.Length; i++)
                {
                    using (var bmp = CreateAppBitmap(sizes[i]))
                    using (var ms = new System.IO.MemoryStream())
                    {
                        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                        pngDatas[i] = ms.ToArray();
                    }
                }

                // 1. ICONDIR (6 bytes)
                bw.Write((ushort)0);
                bw.Write((ushort)1);
                bw.Write((ushort)sizes.Length);

                // 2. ICONDIRENTRY (16 bytes per image)
                int offset = 6 + (sizes.Length * 16);
                for (int i = 0; i < sizes.Length; i++)
                {
                    int sz = sizes[i];
                    bw.Write((byte)(sz >= 256 ? 0 : sz));
                    bw.Write((byte)(sz >= 256 ? 0 : sz));
                    bw.Write((byte)0);
                    bw.Write((byte)0);
                    bw.Write((ushort)1);
                    bw.Write((ushort)32);
                    bw.Write((uint)pngDatas[i].Length);
                    bw.Write((uint)offset);
                    offset += pngDatas[i].Length;
                }

                // 3. PNG Image Data
                for (int i = 0; i < sizes.Length; i++)
                {
                    bw.Write(pngDatas[i]);
                }
            }
        }
    }
}
