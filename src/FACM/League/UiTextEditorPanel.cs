using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FACM.Online;
using FACM.Services;
using FACM.Theming;

namespace FACM.League
{
    internal sealed class UiTextEditorPanel : UserControl
    {
        private sealed class Entry
        {
            internal string Key;
            internal string Current;
            internal string Original;
            internal bool IsRule;
            public override string ToString()
            {
                var value = string.IsNullOrEmpty(Current) ? Key : Current.Replace("\r", " ").Replace("\n", " ");
                if (value.Length > 34) value = value.Substring(0, 34) + "…";
                return value + "  ·  " + Key;
            }
        }

        private readonly UiTextCatalog _ui;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly FacmActionButton _expandButton;
        private readonly TextBox _search;
        private readonly ComboBox _mode;
        private readonly ListBox _items;
        private readonly TextBox _source;
        private readonly Label _default;
        private readonly TextBox _value;
        private readonly Label _preview;
        private readonly Label _status;
        private readonly FacmActionButton _save, _reset, _newRule, _upload, _restore;
        private UiTextProfile _draft;
        private bool _expanded;
        private bool _busy;
        private bool _loading;
        private Entry _renderedEntry;
        private string _appliedSearch = string.Empty;
        private int _appliedMode;
        private Func<DialogResult> _confirmLeaveForSmokeTest;

        internal event EventHandler ExpandedHeightChanged;

        internal UiTextEditorPanel(UiTextCatalog ui)
        {
            _ui = ui ?? UiTextCatalog.Load();
            _active = new WeakReference<UiTextEditorPanel>(this);
            BackColor = FacmDesignSystem.Canvas;
            ForeColor = FacmDesignSystem.Text;
            Font = new Font(FacmThemeRuntime.Current.FontName, 9F);
            Height = 48;

            _expandButton = Action(UiTextKeys.UiTextEditorExpand);
            _expandButton.Tone = FacmButtonTone.Secondary;
            _expandButton.Click += delegate { SetExpanded(!_expanded); };
            Controls.Add(new Label
            {
                Text = _ui.Get(UiTextKeys.UiTextEditorTitle),
                Location = new Point(16, 10),
                Size = new Size(225, 30),
                Font = new Font(Font.FontFamily, 12F, FontStyle.Bold),
                ForeColor = FacmDesignSystem.Text,
                BackColor = Color.Transparent
            });
            Controls.Add(_expandButton);

            _search = new TextBox();
            _search.TextChanged += delegate { Filter(); };
            Controls.Add(_search);

            _mode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            _mode.Items.Add(_ui.Get(UiTextKeys.UiTextEditorTextMode));
            _mode.Items.Add(_ui.Get(UiTextKeys.UiTextEditorReplaceMode));
            _mode.SelectedIndexChanged += delegate { Filter(); };
            _mode.SelectedIndex = 0;
            Controls.Add(_mode);

            _items = new ListBox { IntegralHeight = false };
            _items.SelectedIndexChanged += delegate { RenderSelection(); };
            Controls.Add(_items);

            _source = new TextBox();
            Controls.Add(_source);
            _default = CreateLabel();
            Controls.Add(_default);
            _value = new TextBox
            {
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                AcceptsReturn = true,
                MaxLength = 1800
            };
            _value.TextChanged += delegate
            {
                if (!_loading) _preview.Text = _value.Text;
            };
            Controls.Add(_value);

            _preview = CreateLabel();
            _preview.AutoEllipsis = true;
            Controls.Add(_preview);

            _save = Action(UiTextKeys.UiTextEditorSave);
            _reset = Action(UiTextKeys.UiTextEditorRestore);
            _newRule = Action(UiTextKeys.UiTextEditorAddRule);
            _upload = Action(UiTextKeys.UiTextEditorCloudUpload);
            _restore = Action(UiTextKeys.UiTextEditorCloudRestore);
            _save.Tone = FacmButtonTone.Primary;
            _upload.Tone = FacmButtonTone.Primary;
            _save.Click += delegate { SaveLocal(); };
            _reset.Click += delegate { ResetSelected(); };
            _newRule.Click += delegate
            {
                if (!TryResolvePendingEdit()) return;
                if (_mode.SelectedIndex != 1) _mode.SelectedIndex = 1;
                _items.ClearSelected();
                _source.Text = string.Empty;
                _value.Text = string.Empty;
                _source.Focus();
            };
            _upload.Click += async delegate { await UploadAsync(); };
            _restore.Click += async delegate { await RestoreAsync(); };
            Controls.Add(_save);
            Controls.Add(_reset);
            Controls.Add(_newRule);
            Controls.Add(_upload);
            Controls.Add(_restore);

            _status = CreateLabel();
            _status.Text = _ui.Get(UiTextKeys.UiTextEditorReady);
            Controls.Add(_status);
            _search.Visible = _mode.Visible = _items.Visible = _source.Visible =
                _default.Visible = _value.Visible = _preview.Visible = false;
            foreach (var control in new Control[] { _save, _reset, _newRule, _upload, _restore, _status })
                control.Visible = false;

            SizeChanged += delegate { Arrange(); };
            Disposed += delegate { _lifetime.Cancel(); _lifetime.Dispose(); };
            Arrange();
        }

        private Label CreateLabel()
        {
            return new Label
            {
                ForeColor = FacmDesignSystem.TextMuted,
                BackColor = Color.Transparent,
                AutoEllipsis = true
            };
        }

        private FacmActionButton Action(string key)
        {
            return new FacmActionButton
            {
                Text = _ui.Get(key),
                Tone = FacmButtonTone.Secondary
            };
        }

        private void SetExpanded(bool value)
        {
            if (_expanded == value) return;
            if (!value && !TryResolvePendingEdit()) return;
            _expanded = value;
            Height = value ? 492 : 48;
            _expandButton.Text = _ui.Get(value ? UiTextKeys.UiTextEditorCollapse : UiTextKeys.UiTextEditorExpand);
            foreach (var control in new Control[]
            { _search,_mode,_items,_source,_default,_value,_preview,_save,_reset,_newRule,_status })
                control.Visible = value;
            if (value)
            {
                RefreshEntries();
                RefreshAccountActions();
            }
            Arrange();
            ExpandedHeightChanged?.Invoke(this, EventArgs.Empty);
        }

        internal void RefreshAccountActions()
        {
            var enabled = !_busy && GgmanAccountSession.Current != null;
            _upload.Enabled = enabled;
            _restore.Enabled = enabled;
        }

        private void Arrange()
        {
            var width = Math.Max(420, ClientSize.Width);
            _expandButton.SetBounds(width - 135, 8, 119, 30);
            if (!_expanded) return;
            _search.SetBounds(16, 61, width - 196, 27);
            _mode.SetBounds(width - 166, 61, 150, 27);
            var listWidth = Math.Max(160, (width - 48) * 2 / 5);
            _items.SetBounds(16, 101, listWidth, 290);
            var left = _items.Right + 14;
            var rightWidth = width - left - 16;
            _source.SetBounds(left, 101, rightWidth, 27);
            _default.SetBounds(left, 141, rightWidth, 74);
            _value.SetBounds(left, 226, rightWidth, 126);
            _preview.SetBounds(left, 362, rightWidth, 34);

            var unit = (width - 64) / 3;
            _save.SetBounds(16, 402, unit, 34);
            _reset.SetBounds(26 + unit, 402, unit, 34);
            _newRule.SetBounds(36 + unit * 2, 402, unit, 34);
            _status.SetBounds(16, 444, width - 32, 40);
        }

        private void RefreshEntries()
        {
            if (_busy) return;
            _draft = UiTextCustomizationStore.Capture();
            Filter();
        }

        private void Filter()
        {
            if (_loading || _items == null || _mode == null || _draft == null) return;
            if (!TryResolvePendingEdit())
            {
                _loading = true;
                try
                {
                    _search.Text = _appliedSearch;
                    _mode.SelectedIndex = _appliedMode;
                }
                finally { _loading = false; }
                return;
            }
            _appliedSearch = _search.Text;
            _appliedMode = _mode.SelectedIndex;
            var mode = _mode.SelectedIndex == 1;
            var query = _search.Text.Trim();
            var entries = new List<Entry>();
            if (mode)
            {
                foreach (var rule in _draft.Replace)
                    entries.Add(new Entry { Key = rule.Key, Current = rule.Value,
                        Original = rule.Key, IsRule = true });
            }
            else
            {
                foreach (var entry in UiTextCatalog.DefaultEntries)
                {
                    string current;
                    if (!_draft.Text.TryGetValue(entry.Key, out current)) current = entry.Value;
                    entries.Add(new Entry { Key = entry.Key, Current = current, Original = entry.Value });
                }
            }

            var previous = (_items.SelectedItem as Entry)?.Key;
            _loading = true;
            try
            {
                _items.BeginUpdate();
                _items.Items.Clear();
                foreach (var entry in entries)
                {
                    if (query.Length != 0 && entry.Key.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0 &&
                        entry.Current.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0 &&
                        entry.Original.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                    _items.Items.Add(entry);
                }
                _items.EndUpdate();
                for (var i = 0; i < _items.Items.Count; i++)
                    if (string.Equals(((_items.Items[i] as Entry)?.Key), previous,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        _items.SelectedIndex = i;
                        break;
                    }
            }
            finally { _loading = false; }
            RenderSelection();
        }

        private void RenderSelection()
        {
            if (_loading || _draft == null) return;
            var next = _items.SelectedItem as Entry;
            if (!ReferenceEquals(next, _renderedEntry) && !TryResolvePendingEdit())
            {
                _loading = true;
                try { _items.SelectedItem = _renderedEntry; }
                finally { _loading = false; }
                return;
            }
            _loading = true;
            try
            {
                var entry = next;
                _renderedEntry = entry;
                var advanced = _mode.SelectedIndex == 1;
                _source.Visible = _expanded && advanced;
                _newRule.Visible = _expanded && advanced;
                _source.Text = advanced ? entry?.Key ?? "" : "";
                _source.ReadOnly = entry != null;
                _default.Text = entry == null ? _ui.Get(UiTextKeys.UiTextEditorSelectEntry)
                    : _ui.Get(UiTextKeys.UiTextEditorDefault) + "： " + entry.Original;
                _value.Text = entry?.Current ?? "";
                _preview.Text = _value.Text;
            }
            finally { _loading = false; }
        }

        private bool SaveLocal(bool refresh = true)
        {
            if (_busy) return false;
            try
            {
                var entry = _renderedEntry;
                if (_mode.SelectedIndex == 1)
                {
                    var source = _source.Text.Trim();
                    if (source.Length == 0) throw new InvalidOperationException(
                        _ui.Get(UiTextKeys.UiTextEditorSelectEntry));
                    _draft.Replace[source] = _value.Text;
                }
                else
                {
                    if (entry == null) throw new InvalidOperationException(
                        _ui.Get(UiTextKeys.UiTextEditorSelectEntry));
                    UiTextCustomizationStore.ValidateText(_value.Text, entry.Original);
                    if (string.Equals(_value.Text, entry.Original, StringComparison.Ordinal))
                        _draft.Text.Remove(entry.Key);
                    else
                        _draft.Text[entry.Key] = _value.Text;
                }
                UiTextCustomizationStore.Apply(_draft);
                if (entry != null)
                    entry.Current = _value.Text;
                else
                    ClearNewRuleFields();
                GgmanAutoSyncService.Current?.NotifyUiTextSaved();
                _status.Text = _ui.Get(UiTextKeys.UiTextEditorSaved);
                if (refresh) RefreshEntries();
                return true;
            }
            catch (Exception error)
            {
                _status.Text = string.Format(_ui.Get(UiTextKeys.UiTextEditorError), error.Message);
                return false;
            }
        }

        private void ClearNewRuleFields()
        {
            _loading = true;
            try
            {
                _source.Text = string.Empty;
                _value.Text = string.Empty;
                _preview.Text = string.Empty;
            }
            finally { _loading = false; }
        }

        internal bool TryResolvePendingEdit()
        {
            if (IsDisposed) return true;
            if (_busy) return false;
            if (!HasPendingChanges) return true;

            var decision = _confirmLeaveForSmokeTest != null ? _confirmLeaveForSmokeTest() :
                MessageBox.Show(this, _ui.Get(UiTextKeys.UiTextEditorLeavePrompt),
                    _ui.Get(UiTextKeys.UiTextEditorTitle), MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Warning, MessageBoxDefaultButton.Button3);
            if (decision == DialogResult.Yes) return SaveLocal(false);
            if (decision != DialogResult.No) return false;

            _loading = true;
            try
            {
                _source.Text = _renderedEntry != null && _renderedEntry.IsRule
                    ? _renderedEntry.Key : string.Empty;
                _value.Text = _renderedEntry == null ? string.Empty : _renderedEntry.Current;
                _preview.Text = _value.Text;
            }
            finally { _loading = false; }
            return true;
        }

        internal static void ValidateDraftNavigationForSmokeTest()
        {
            using (var editor = new UiTextEditorPanel(null))
            {
                editor.SetExpanded(true);
                if (editor._items.Items.Count < 2)
                    throw new InvalidOperationException("UI text editor needs at least two text entries for navigation smoke.");

                editor._items.SelectedIndex = 0;
                var original = editor._items.SelectedItem;
                var draft = editor._value.Text + " draft";
                editor._value.Text = draft;
                editor._confirmLeaveForSmokeTest = delegate { return DialogResult.Cancel; };

                editor._items.SelectedIndex = 1;
                if (!ReferenceEquals(editor._items.SelectedItem, original) || editor._value.Text != draft)
                    throw new InvalidOperationException("Canceling an edit transition discarded the current draft.");

                editor._search.Text = draft;
                if (editor._search.Text != string.Empty || editor._value.Text != draft)
                    throw new InvalidOperationException("Canceling a search failed to preserve the visible edit.");

                editor.SetExpanded(false);
                if (!editor._expanded || editor._value.Text != draft)
                    throw new InvalidOperationException("Canceling collapse lost the current draft.");

                editor._confirmLeaveForSmokeTest = delegate { return DialogResult.No; };
                editor._items.SelectedIndex = 1;
                if (editor._items.SelectedIndex != 1 || editor.HasPendingChanges)
                    throw new InvalidOperationException("Discard did not allow the requested editor selection.");
            }
        }

        private void ResetSelected()
        {
            if (_busy || !TryResolvePendingEdit()) return;
            var entry = _renderedEntry;
            if (entry == null) return;
            try
            {
                if (entry.IsRule) _draft.Replace.Remove(entry.Key);
                else _draft.Text.Remove(entry.Key);
                UiTextCustomizationStore.Apply(_draft);
                _renderedEntry = null;
                ClearNewRuleFields();
                GgmanAutoSyncService.Current?.NotifyUiTextSaved();
                _status.Text = _ui.Get(UiTextKeys.UiTextEditorSaved);
                RefreshEntries();
            }
            catch (Exception error)
            {
                _status.Text = string.Format(_ui.Get(UiTextKeys.UiTextEditorError), error.Message);
            }
        }

        private static WeakReference<UiTextEditorPanel> _active;
        internal static bool HasPendingVisibleChanges
        {
            get
            {
                UiTextEditorPanel editor;
                return _active != null && _active.TryGetTarget(out editor) &&
                    !editor.IsDisposed && editor.HasPendingChanges;
            }
        }

        internal bool HasPendingChanges
        {
            get { return _expanded && !IsCurrentEditSaved(); }
        }

        internal void ReloadAfterExternalRestore()
        {
            if (_expanded && !IsDisposed) RefreshEntries();
        }

        private bool IsCurrentEditSaved()
        {
            var selected = _renderedEntry;
            if (selected != null)
                return string.Equals(_value.Text, selected.Current, StringComparison.Ordinal);
            return string.IsNullOrWhiteSpace(_source.Text) &&
                string.IsNullOrEmpty(_value.Text);
        }

        private async Task UploadAsync()
        {
            if (_busy || IsDisposed) return;
            if (!IsCurrentEditSaved())
            {
                _status.Text = _ui.Get(UiTextKeys.UiTextEditorSaveBeforeCloud);
                return;
            }
            var account = GgmanAccountSession.Current;
            if (account == null) { _status.Text = _ui.Get(UiTextKeys.UiTextEditorNoLogin); return; }
            SetBusy(true);
            try
            {
                var current = UiTextCustomizationStore.Capture();
                using (var client = new GgmanUiTextCloudClient())
                {
                    var remote = await client.GetAsync(account, _lifetime.Token);
                    if (IsDisposed || _lifetime.IsCancellationRequested) return;
                    RequireSameAccount(account);
                    var prompt = string.Format(_ui.Get(UiTextKeys.UiTextEditorCloudConfirm),
                        remote == null ? 0 : remote.Version, current.Text.Count, current.Replace.Count);
                    if (MessageBox.Show(this, prompt, _ui.Get(UiTextKeys.UiTextEditorTitle),
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2)
                        != DialogResult.Yes) return;
                    RequireSameAccount(account);
                    var version = await client.SetAsync(account, current,
                        remote == null ? 0 : remote.Version, _lifetime.Token);
                    if (!IsDisposed) _status.Text = string.Format(
                        _ui.Get(UiTextKeys.UiTextEditorUploaded), version);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { if (!IsDisposed) ShowError(error); }
            finally { if (!IsDisposed) SetBusy(false); }
        }

        private async Task RestoreAsync()
        {
            if (_busy || IsDisposed) return;
            if (!IsCurrentEditSaved())
            {
                _status.Text = _ui.Get(UiTextKeys.UiTextEditorSaveBeforeCloud);
                return;
            }
            var account = GgmanAccountSession.Current;
            if (account == null) { _status.Text = _ui.Get(UiTextKeys.UiTextEditorNoLogin); return; }
            SetBusy(true);
            try
            {
                using (var client = new GgmanUiTextCloudClient())
                {
                    var remote = await client.GetAsync(account, _lifetime.Token);
                    if (IsDisposed || _lifetime.IsCancellationRequested) return;
                    RequireSameAccount(account);
                    if (remote == null)
                    {
                        _status.Text = _ui.Get(UiTextKeys.UiTextEditorCloudMissing);
                        return;
                    }
                    var prompt = string.Format(_ui.Get(UiTextKeys.UiTextEditorCloudRestoreConfirm),
                        remote.Version, remote.UpdatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                        remote.Profile.Text.Count, remote.Profile.Replace.Count);
                    if (MessageBox.Show(this, prompt, _ui.Get(UiTextKeys.UiTextEditorTitle),
                        MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2)
                        != DialogResult.Yes) return;
                    RequireSameAccount(account);
                    UiTextCustomizationStore.Apply(remote.Profile);
                    if (!IsDisposed)
                    {
                        _status.Text = _ui.Get(UiTextKeys.UiTextEditorRestored);
                        RefreshEntries();
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { if (!IsDisposed) ShowError(error); }
            finally { if (!IsDisposed) SetBusy(false); }
        }

        private static void RequireSameAccount(GgmanAccountIdentity expected)
        {
            var current = GgmanAccountSession.Current;
            if (current == null || current.UserId != expected.UserId ||
                current.SessionKey != expected.SessionKey)
                throw new InvalidOperationException("GGman 登录状态已更改，文字同步已取消。");
        }

        private void ShowError(Exception exception)
        {
            _status.Text = string.Format(_ui.Get(UiTextKeys.UiTextEditorError), exception.Message);
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            _save.Enabled = _reset.Enabled = _newRule.Enabled = !busy;
            _search.Enabled = _mode.Enabled = _items.Enabled = _value.Enabled = _source.Enabled = !busy;
            RefreshAccountActions();
        }
    }
}
