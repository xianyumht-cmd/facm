using System;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace FACM.Services
{
    internal static class UiTextRuntime
    {
        private const string DialogClassName = "#32770";
        private static readonly ConditionalWeakTable<Control, TextState> ControlStates =
            new ConditionalWeakTable<Control, TextState>();
        private static readonly ConditionalWeakTable<ToolStripItem, TextState> ToolStripStates =
            new ConditionalWeakTable<ToolStripItem, TextState>();
        private static readonly ConditionalWeakTable<DataGridViewColumn, TextState> GridColumnStates =
            new ConditionalWeakTable<DataGridViewColumn, TextState>();
        private static readonly ConditionalWeakTable<ColumnHeader, TextState> ListColumnStates =
            new ConditionalWeakTable<ColumnHeader, TextState>();
        private static readonly ConditionalWeakTable<ListControl, FormatMarker> ListControlStates =
            new ConditionalWeakTable<ListControl, FormatMarker>();

        private static UiTextCatalog _catalog;
        private static Timer _timer;
        private static DateTime _lastWriteUtc;
        private static int _revision;
        private static bool _installed;

        public static void Install()
        {
            if (_installed) return;
            _installed = true;
            ReloadCatalog();

            _timer = new Timer { Interval = 180 };
            _timer.Tick += delegate
            {
                ReloadWhenChanged();
                ApplyToOpenForms();
                ApplyToNativeDialogs();
            };
            _timer.Start();
        }

        /// <summary>
        /// Resolve a stable UI Text Contract key to the current configured value.
        /// The catalog owns the default copy, so callers never need to duplicate user-visible fallback text.
        /// </summary>
        public static string Text(string key)
        {
            if (_catalog == null) ReloadCatalog();
            return _catalog == null ? string.Empty : _catalog.Get(key);
        }

        public static string Translate(string text)
        {
            if (_catalog == null) ReloadCatalog();
            return _catalog == null ? (text ?? string.Empty) : _catalog.Translate(text);
        }

        public static void Apply(ContextMenuStrip menu)
        {
            if (menu == null || menu.IsDisposed) return;
            ApplyToolStripItems(menu.Items);
        }

        private static void ReloadWhenChanged()
        {
            try
            {
                var path = UiTextCatalog.ConfigPath;
                if (!System.IO.File.Exists(path)) return;
                var writeTime = System.IO.File.GetLastWriteTimeUtc(path);
                if (writeTime <= _lastWriteUtc) return;
                ReloadCatalog();
            }
            catch
            {
            }
        }

        private static void ReloadCatalog()
        {
            try
            {
                _catalog = UiTextCatalog.Load();
                _revision++;
                _lastWriteUtc = System.IO.File.Exists(UiTextCatalog.ConfigPath)
                    ? System.IO.File.GetLastWriteTimeUtc(UiTextCatalog.ConfigPath)
                    : DateTime.MinValue;
            }
            catch (Exception exception)
            {
                AppLog.Error("Failed to reload UI text configuration", exception);
                if (_catalog == null) _catalog = UiTextCatalog.Load();
            }
        }

        private static void ApplyToOpenForms()
        {
            if (_catalog == null) return;
            foreach (Form form in Application.OpenForms)
            {
                if (form == null || form.IsDisposed) continue;
                ApplyControlTree(form);
                ApplyComponentTexts(form);
            }
        }

        private static void ApplyControlTree(Control control)
        {
            if (control == null || control.IsDisposed) return;

            if (ShouldTranslateControlText(control))
                ApplyManagedText(control, ControlStates, delegate { return control.Text; }, delegate(string value) { control.Text = value; });

            var listControl = control as ListControl;
            if (listControl != null) ApplyListFormatting(listControl);

            var toolStrip = control as ToolStrip;
            if (toolStrip != null) ApplyToolStripItems(toolStrip.Items);
            if (control.ContextMenuStrip != null) ApplyToolStripItems(control.ContextMenuStrip.Items);

            var grid = control as DataGridView;
            if (grid != null)
            {
                foreach (DataGridViewColumn column in grid.Columns)
                {
                    var captured = column;
                    ApplyManagedText(captured, GridColumnStates, delegate { return captured.HeaderText; }, delegate(string value) { captured.HeaderText = value; });
                }
            }

            var listView = control as ListView;
            if (listView != null)
            {
                foreach (ColumnHeader column in listView.Columns)
                {
                    var captured = column;
                    ApplyManagedText(captured, ListColumnStates, delegate { return captured.Text; }, delegate(string value) { captured.Text = value; });
                }
            }

            foreach (Control child in control.Controls) ApplyControlTree(child);
        }

        private static bool ShouldTranslateControlText(Control control)
        {
            if (control is TextBoxBase) return false;
            if (control is ComboBox) return false;
            if (control is ListBox) return false;
            if (control is NumericUpDown) return false;
            if (control is DateTimePicker) return false;

            return control is Form ||
                   control is Label ||
                   control is ButtonBase ||
                   control is GroupBox ||
                   control is TabPage ||
                   control is LinkLabel;
        }

        private static void ApplyListFormatting(ListControl list)
        {
            if (list == null || list.IsDisposed || string.IsNullOrWhiteSpace(list.DisplayMember)) return;
            FormatMarker marker;
            if (!ListControlStates.TryGetValue(list, out marker))
            {
                marker = new FormatMarker();
                ListControlStates.Add(list, marker);
                list.FormattingEnabled = true;
                list.Format += HandleListFormat;
            }
            if (marker.Revision != _revision)
            {
                marker.Revision = _revision;
                list.Refresh();
            }
        }

        private static void HandleListFormat(object sender, ListControlConvertEventArgs e)
        {
            if (_catalog == null || e == null) return;
            var list = sender as ListControl;
            if (list == null) return;

            string text = null;
            if (e.Value is string) text = (string)e.Value;
            if (text == null && e.ListItem != null && !string.IsNullOrWhiteSpace(list.DisplayMember))
            {
                try
                {
                    var property = TypeDescriptor.GetProperties(e.ListItem)[list.DisplayMember];
                    if (property != null) text = Convert.ToString(property.GetValue(e.ListItem));
                }
                catch
                {
                }
            }
            if (string.IsNullOrEmpty(text)) return;
            e.Value = _catalog.Translate(_catalog.Canonicalize(text));
        }

        private static void ApplyToolStripItems(ToolStripItemCollection items)
        {
            if (items == null) return;
            foreach (ToolStripItem item in items)
            {
                if (item == null || item.IsDisposed) continue;
                var captured = item;
                ApplyManagedText(captured, ToolStripStates, delegate { return captured.Text; }, delegate(string value) { captured.Text = value; });

                var dropDown = item as ToolStripDropDownItem;
                if (dropDown != null && dropDown.HasDropDownItems) ApplyToolStripItems(dropDown.DropDownItems);
            }
        }

        private static void ApplyComponentTexts(Form form)
        {
            try
            {
                var fields = form.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                foreach (var field in fields)
                {
                    if (typeof(NotifyIcon).IsAssignableFrom(field.FieldType))
                    {
                        var icon = field.GetValue(form) as NotifyIcon;
                        if (icon == null) continue;
                        var translated = _catalog.Translate(_catalog.Canonicalize(icon.Text ?? string.Empty));
                        if (!string.Equals(icon.Text, translated, StringComparison.Ordinal))
                        {
                            try { icon.Text = translated.Length <= 63 ? translated : translated.Substring(0, 63); } catch { }
                        }
                        if (icon.ContextMenuStrip != null) ApplyToolStripItems(icon.ContextMenuStrip.Items);
                        continue;
                    }

                    if (typeof(ToolTip).IsAssignableFrom(field.FieldType))
                    {
                        var toolTip = field.GetValue(form) as ToolTip;
                        if (toolTip != null) ApplyToolTips(form, toolTip);
                    }
                }
            }
            catch
            {
            }
        }

        private static void ApplyToolTips(Control root, ToolTip toolTip)
        {
            var current = toolTip.GetToolTip(root);
            if (!string.IsNullOrEmpty(current))
            {
                var translated = _catalog.Translate(_catalog.Canonicalize(current));
                if (!string.Equals(current, translated, StringComparison.Ordinal)) toolTip.SetToolTip(root, translated);
            }
            foreach (Control child in root.Controls) ApplyToolTips(child, toolTip);
        }

        private static void ApplyManagedText<T>(
            T target,
            ConditionalWeakTable<T, TextState> states,
            Func<string> getter,
            Action<string> setter)
            where T : class
        {
            var current = getter() ?? string.Empty;
            if (current.Length == 0) return;

            TextState state;
            if (!states.TryGetValue(target, out state))
            {
                state = new TextState
                {
                    Source = _catalog.Canonicalize(current),
                    LastApplied = current,
                    Revision = -1
                };
                states.Add(target, state);
            }
            else if (!string.Equals(current, state.LastApplied, StringComparison.Ordinal))
            {
                state.Source = _catalog.Canonicalize(current);
            }

            if (state.Revision == _revision && string.Equals(current, state.LastApplied, StringComparison.Ordinal)) return;

            var translated = _catalog.Translate(state.Source);
            if (!string.Equals(current, translated, StringComparison.Ordinal)) setter(translated);
            state.LastApplied = translated;
            state.Revision = _revision;
        }

        private static void ApplyToNativeDialogs()
        {
            try
            {
                EnumThreadWindows(GetCurrentThreadId(), delegate(IntPtr window, IntPtr lParam)
                {
                    if (!string.Equals(GetWindowClass(window), DialogClassName, StringComparison.Ordinal)) return true;
                    TranslateNativeWindow(window);
                    EnumChildWindows(window, delegate(IntPtr child, IntPtr childParam)
                    {
                        TranslateNativeWindow(child);
                        return true;
                    }, IntPtr.Zero);
                    return true;
                }, IntPtr.Zero);
            }
            catch
            {
            }
        }

        private static void TranslateNativeWindow(IntPtr handle)
        {
            var length = GetWindowTextLength(handle);
            if (length <= 0) return;
            var builder = new StringBuilder(length + 2);
            if (GetWindowText(handle, builder, builder.Capacity) <= 0) return;
            var current = builder.ToString();
            var translated = _catalog.Translate(_catalog.Canonicalize(current));
            if (!string.Equals(current, translated, StringComparison.Ordinal)) SetWindowText(handle, translated);
        }

        private static string GetWindowClass(IntPtr handle)
        {
            var builder = new StringBuilder(64);
            return GetClassName(handle, builder, builder.Capacity) > 0 ? builder.ToString() : string.Empty;
        }

        private sealed class TextState
        {
            public string Source;
            public string LastApplied;
            public int Revision;
        }

        private sealed class FormatMarker
        {
            public int Revision = -1;
        }

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumThreadWindows(uint dwThreadId, EnumWindowsProc lpfn, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool SetWindowText(IntPtr hWnd, string lpString);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();
    }
}
