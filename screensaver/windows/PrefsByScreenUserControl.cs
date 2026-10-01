using System;
using System.Drawing;
using System.Windows.Forms;

namespace Web_Page_Screensaver
{
    public partial class PrefsByScreenUserControl : UserControl
    {
        private ListViewItem newlyAddedItem = null;
        private string currentLanguage = "en";
        private Action themeChangeHandler;

        public PrefsByScreenUserControl()
        {
            InitializeComponent();
            ApplyModernStyles();

            themeChangeHandler = () => ApplyTheme(ThemeManager.IsLightTheme);
            ThemeManager.ThemeChanged += themeChangeHandler;

            ApplyTheme(ThemeManager.IsLightTheme);
            AdjustOptionsLayout();
        }

        public void ApplyTheme(bool isLight)
        {
            var colors = ThemeManager.Colors;

            BackColor = colors.CardBackground;

            // URL list area
            listCard.BackColor = colors.InputBackground;
            listCard.BorderColor = colors.CardBorder;
            lvUrls.BackColor = colors.InputBackground;
            lvUrls.ForeColor = colors.TextPrimary;

            // Bottom rotation interval options card
            optionsCard.BackColor = colors.CardBackground;
            optionsCard.BorderColor = colors.CardBorder;
            nudRotationInterval.BackColor = colors.InputBackground;
            nudRotationInterval.ForeColor = colors.TextPrimary;

            // Label and check box text colours
            lblRotation.ForeColor = colors.TextPrimary;
            lblSeconds.ForeColor = colors.TextSecondary;
            lblZoom.ForeColor = colors.TextPrimary;
            cmbZoom.BackColor = colors.InputBackground;
            cmbZoom.ForeColor = colors.TextPrimary;
            cbRandomize.ForeColor = colors.TextPrimary;

            // Redraw the controls
            listCard.Invalidate();
            optionsCard.Invalidate();
            btnAddUrl.Invalidate();
            btnUp.Invalidate();
            btnDown.Invalidate();
            btnEdit.Invalidate();
            btnPreview.Invalidate();
            btnDelete.Invalidate();
            cbRandomize.Invalidate();
        }

        private void ApplyModernStyles()
        {
            // Auto-size ListView column widths
            if (lvUrls.Columns.Count > 0)
            {
                lvUrls.Columns[0].Width = Math.Max(200, lvUrls.ClientSize.Width - 10);
            }

            lvUrls.Resize += (s, e) =>
            {
                if (lvUrls.Columns.Count > 0)
                {
                    lvUrls.Columns[0].Width = Math.Max(200, lvUrls.ClientSize.Width - 10);
                }
            };

            // Default combo box selection (100%)
            if (cmbZoom.SelectedIndex < 0)
            {
                cmbZoom.SelectedIndex = 1; // 100%
            }
        }

        public void ApplyLanguage(string lang)
        {
            currentLanguage = lang;
            bool isKo = (lang == "ko");

            btnAddUrl.Text = isKo ? "＋ URL 추가" : "＋ Add URL";
            btnUp.Text = isKo ? "▲ 위로" : "▲ Move Up";
            btnDown.Text = isKo ? "▼ 아래로" : "▼ Move Down";
            btnEdit.Text = isKo ? "✎ 수정" : "✎ Edit";
            btnPreview.Text = isKo ? "👁 미리보기" : "👁 Preview";
            btnDelete.Text = isKo ? "✕ 삭제" : "✕ Delete";

            lblRotation.Text = isKo ? "전환 주기:" : "Rotate every:";
            lblSeconds.Text = isKo ? "초" : "sec";
            lblZoom.Text = isKo ? "화면 배율:" : "Zoom:";
            cbRandomize.Text = isKo ? "무작위 순서 (Shuffle)" : "Shuffle order";

            AdjustOptionsLayout();

            urlButtonsTooltip.SetToolTip(btnUp, isKo ? "선택한 URL을 위로 이동합니다 (Alt+▲)" : "Move selected URL up (Alt+▲)");
            urlButtonsTooltip.SetToolTip(btnDown, isKo ? "선택한 URL을 아래로 이동합니다 (Alt+▼)" : "Move selected URL down (Alt+▼)");
            urlButtonsTooltip.SetToolTip(btnAddUrl, isKo ? "목록에 새 사이트 URL을 추가하고 인라인으로 편집합니다" : "Add a new URL and edit inline");
            urlButtonsTooltip.SetToolTip(btnEdit, isKo ? "선택한 URL을 목록에서 직접 수정합니다 (F2 / 더블클릭)" : "Edit selected URL directly in list (F2 / Double-click)");
            urlButtonsTooltip.SetToolTip(btnPreview, isKo ? "선택한 URL을 실시간 화면보호기 창으로 미리 봅니다" : "Preview selected URL in live screensaver window");
            urlButtonsTooltip.SetToolTip(btnDelete, isKo ? "선택한 URL을 삭제합니다 (Del)" : "Delete selected URLs (Del)");
        }

        /// <summary>
        /// Dynamically aligns the horizontal positions of the rotation interval, seconds and screen zoom labels and their controls as label lengths change by language (Korean/English and font size),
        /// so text is never clipped or overlaps the controls.
        /// </summary>
        public void AdjustOptionsLayout()
        {
            lblRotation.Location = new Point(12, 13);
            nudRotationInterval.Location = new Point(lblRotation.Right + 8, 9);
            lblSeconds.Location = new Point(nudRotationInterval.Right + 6, 13);
            lblZoom.Location = new Point(lblSeconds.Right + 22, 13);
            cmbZoom.Location = new Point(lblZoom.Right + 8, 10);
        }

        #region URL add / edit / delete (with inline editing)

        /// <summary>
        /// Adds a new URL entry to the end of the list and enters inline edit mode immediately.
        /// </summary>
        private void btnAddUrl_Click(object sender, EventArgs e)
        {
            var item = new ListViewItem("https://");
            lvUrls.Items.Add(item);

            // Focus and select
            foreach (ListViewItem old in lvUrls.SelectedItems)
            {
                old.Selected = false;
            }
            item.Selected = true;
            item.Focused = true;
            item.EnsureVisible();

            newlyAddedItem = item;

            // Start inline editing safely via a UI thread dispatch
            BeginInvoke((MethodInvoker)(() =>
            {
                if (item != null && item.ListView != null && !item.ListView.IsDisposed)
                {
                    item.BeginEdit();
                }
            }));
        }

        /// <summary>
        /// Starts inline editing of the selected item.
        /// </summary>
        private void btnEdit_Click(object sender, EventArgs e)
        {
            StartEditSelected();
        }

        private void lvUrls_DoubleClick(object sender, EventArgs e)
        {
            StartEditSelected();
        }

        private void StartEditSelected()
        {
            if (lvUrls.SelectedItems.Count > 0)
            {
                var item = lvUrls.SelectedItems[0];
                BeginInvoke((MethodInvoker)(() =>
                {
                    if (item != null && item.ListView != null && !item.ListView.IsDisposed)
                    {
                        item.BeginEdit();
                    }
                }));
            }
        }

        /// <summary>
        /// Handles completion or cancellation of an inline edit (fixes up the protocol and cleans up empty values)
        /// </summary>
        private void lvUrls_AfterLabelEdit(object sender, LabelEditEventArgs e)
        {
            var item = lvUrls.Items[e.Item];
            string editedText = e.Label;

            // The user cancelled the edit or entered nothing
            if (editedText == null)
            {
                // Automatically remove a newly added row that was cancelled or left as the default template
                if (newlyAddedItem == item && (string.IsNullOrWhiteSpace(item.Text) || item.Text == "https://"))
                {
                    BeginInvoke((MethodInvoker)(() =>
                    {
                        item.Remove();
                    }));
                }
                newlyAddedItem = null;
                return;
            }

            editedText = editedText.Trim();

            // An empty value was entered
            if (string.IsNullOrEmpty(editedText) || editedText == "https://" || editedText == "http://")
            {
                e.CancelEdit = true;
                if (newlyAddedItem == item)
                {
                    BeginInvoke((MethodInvoker)(() =>
                    {
                        item.Remove();
                    }));
                }
                newlyAddedItem = null;
                return;
            }

            // Automatically add the protocol (https://)
            if (!editedText.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !editedText.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
                !editedText.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                editedText = "https://" + editedText;
            }

            e.CancelEdit = true; // Assign the corrected text directly instead of the framework's default assignment
            item.Text = editedText;
            newlyAddedItem = null;
        }

        /// <summary>
        /// Keyboard shortcuts (F2: edit, Del: delete, Alt+Up/Down: move)
        /// </summary>
        private void lvUrls_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F2)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                StartEditSelected();
            }
            else if (e.KeyCode == Keys.Delete)
            {
                e.Handled = true;
                DeleteAllSelectedUrls_Click(sender, e);
            }
            else if (e.Alt && e.KeyCode == Keys.Up)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                MoveAllSelectedUrlsUp_Click(sender, e);
            }
            else if (e.Alt && e.KeyCode == Keys.Down)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                MoveAllSelectedUrlsDown_Click(sender, e);
            }
            else if (e.KeyCode == Keys.Insert)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                btnAddUrl_Click(sender, e);
            }
        }

        private void DeleteAllSelectedUrls_Click(object sender, EventArgs e)
        {
            for (int i = lvUrls.Items.Count - 1; i >= 0; i--)
            {
                if (lvUrls.Items[i].Selected)
                {
                    lvUrls.Items[i].Remove();
                }
            }
        }

        #endregion

        #region Reordering logic

        private void MoveAllSelectedUrlsDown_Click(object sender, EventArgs e)
        {
            bool gapFound = false;

            for (int i = lvUrls.Items.Count - 1; i >= 0; i--)
            {
                if (lvUrls.Items[i].Selected)
                {
                    if (gapFound)
                    {
                        Swap(lvUrls.Items, i, i + 1);
                    }
                }
                else
                {
                    gapFound = true;
                }
            }

            lvUrls.Select();
        }

        private void MoveAllSelectedUrlsUp_Click(object sender, EventArgs e)
        {
            bool gapFound = false;

            for (int i = 0; i < lvUrls.Items.Count; i++)
            {
                if (lvUrls.Items[i].Selected)
                {
                    if (gapFound)
                    {
                        Swap(lvUrls.Items, i, i - 1);
                    }
                }
                else
                {
                    gapFound = true;
                }
            }

            lvUrls.Select();
        }

        private static void Swap(ListView.ListViewItemCollection itemsList, int indexA, int indexB)
        {
            var a = Math.Min(itemsList.Count - 1, Math.Max(0, indexA));
            var b = Math.Min(itemsList.Count - 1, Math.Max(0, indexB));
            if (a != b)
            {
                var itemA = (ListViewItem)itemsList[a].Clone();
                bool itemASelected = itemsList[a].Selected;
                var itemB = (ListViewItem)itemsList[b].Clone();
                bool itemBSelected = itemsList[b].Selected;
                itemsList[a] = itemB;
                itemsList[a].Selected = itemBSelected;
                itemsList[b] = itemA;
                itemsList[b].Selected = itemASelected;
            }
        }

        #endregion

        #region Live preview

        /// <summary>
        /// Immediately previews the selected URL in the list in an actual screensaver modal window.
        /// </summary>
        private void btnPreview_Click(object sender, EventArgs e)
        {
            if (lvUrls.SelectedItems.Count > 0)
            {
                string rawUrl = lvUrls.SelectedItems[0].Text;
                var parsed = ScreensaverUrlItem.Parse(rawUrl);
                if (!string.IsNullOrWhiteSpace(parsed.Url))
                {
                    double zoom = 1.0;
                    if (cmbZoom.SelectedItem != null)
                    {
                        string zoomStr = cmbZoom.SelectedItem.ToString().TrimEnd('%');
                        if (double.TryParse(zoomStr, out double z)) zoom = z / 100.0;
                    }

                    using (var dlg = new PreviewDialog(parsed.Url, zoom, true, currentLanguage))
                    {
                        dlg.ShowDialog(this.FindForm());
                    }
                }
            }
            else
            {
                bool isKo = (currentLanguage == "ko");
                MessageBox.Show(
                    isKo ? "미리 볼 URL을 목록에서 먼저 선택해 주세요." : "Please select a URL from the list to preview.",
                    isKo ? "안내" : "Notice",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }

        #endregion
    }
}
