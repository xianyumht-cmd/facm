using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using FACM.League;
using FACM.Services;
using FACM.Theming;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace FACM.Presentation.Glass
{
    // Feature-gated transition host. One native OS window and one WebView2 process owner.
    // Existing native League pages remain embedded until their HTML counterparts are ready.
    internal sealed class UnifiedWorkspaceForm : Form
    {
        private const string HtmlResource = "FACM.Presentation.Glass.GGman-Unified-Workspace.html";
        private const int WmNcLButtonDown = 0x00A1;
        private const int HtCaption = 2;

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

        private readonly UnifiedWorkspaceNavigation _navigation = new UnifiedWorkspaceNavigation();
        private readonly MainForm _ownerBall;
        private readonly UiTextCatalog _ui;
        private readonly WebView2 _browser;
        private readonly Panel _content;
        private LeagueHubForm _hub;
        private bool _starting;
        private bool _ready;
        private bool _closing;
        internal event Action BrowserUnavailable;

        internal UnifiedWorkspaceForm(MainForm ownerBall, UiTextCatalog ui)
        {
            _ownerBall = ownerBall ?? throw new ArgumentNullException(nameof(ownerBall));
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
            Text = ui.AppName;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.None;
            BackColor = FacmDesignSystem.Canvas;
            ForeColor = FacmDesignSystem.Text;
            Font = new Font(FacmThemeRuntime.Current.FontName, 9F);
            TopMost = true;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = UnifiedWorkspaceNavigation.ResolveClientSize(Screen.PrimaryScreen.WorkingArea, false);

            _content = new Panel
            {
                Dock = DockStyle.Fill,
                Visible = false,
                BackColor = FacmDesignSystem.Canvas,
                Padding = Padding.Empty
            };
            _browser = new WebView2
            {
                Dock = DockStyle.Fill,
                DefaultBackgroundColor = Color.FromArgb(4, 12, 32)
            };
            Controls.Add(_content);
            Controls.Add(_browser);
            Shown += async delegate { await InitializeAsync(); };
            FormClosing += HandleClosing;
            FormClosed += delegate
            {
                try { if (_browser.CoreWebView2 != null) _browser.CoreWebView2.Stop(); }
                catch (Exception exception) { AppLog.Info("Unified workspace WebView2 shutdown: " + exception.Message); }
                if (_hub != null && !_hub.IsDisposed) _hub.Dispose();
                _browser.Dispose();
            };
        }

        internal bool HasNativeRoute { get { return _hub != null && !_hub.IsDisposed; } }

        private async Task InitializeAsync()
        {
            if (_starting || IsDisposed) return;
            _starting = true;
            try
            {
                var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "FACM", "UnifiedWorkspaceWebView2");
                var environment = await CoreWebView2Environment.CreateAsync(null, data);
                if (IsDisposed) return;
                await _browser.EnsureCoreWebView2Async(environment);
                if (IsDisposed || _browser.CoreWebView2 == null) return;

                var core = _browser.CoreWebView2;
                core.Settings.AreDevToolsEnabled = false;
                core.Settings.AreDefaultContextMenusEnabled = false;
                core.Settings.AreHostObjectsAllowed = false;
                core.Settings.IsStatusBarEnabled = false;
                core.Settings.IsWebMessageEnabled = true;
                core.Settings.IsZoomControlEnabled = false;
                core.Settings.AreBrowserAcceleratorKeysEnabled = false;
                core.NavigationStarting += delegate(object sender, CoreWebView2NavigationStartingEventArgs args)
                {
                    if (!string.Equals(args.Uri, "about:blank", StringComparison.OrdinalIgnoreCase))
                        args.Cancel = true;
                };
                core.NewWindowRequested += delegate(object sender, CoreWebView2NewWindowRequestedEventArgs args)
                {
                    args.Handled = true;
                };
                core.PermissionRequested += delegate(object sender, CoreWebView2PermissionRequestedEventArgs args)
                {
                    args.State = CoreWebView2PermissionState.Deny;
                };
                core.DownloadStarting += delegate(object sender, CoreWebView2DownloadStartingEventArgs args)
                {
                    args.Cancel = true;
                };
                core.WebMessageReceived += HandleWebMessage;
                core.NavigationCompleted += delegate
                {
                    if (IsDisposed) return;
                    _ready = true;
                    SyncWebNavigation();
                };
                core.NavigateToString(ReadEmbeddedHtml());
            }
            catch (Exception exception)
            {
                AppLog.Error("Unified WebView2 workspace initialization failed", exception);
                if (IsDisposed) return;
                _ready = false;
                if (BrowserUnavailable != null) BrowserUnavailable();
                Close();
            }
        }

        private void HandleWebMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            if (IsDisposed || _closing || !_ready) return;
            string message;
            try { message = e.TryGetWebMessageAsString(); }
            catch { return; }
            if (message == "close")
            {
                Close();
                return;
            }
            if (message == "drag")
            {
                if (WindowState != FormWindowState.Normal) return;
                ReleaseCapture();
                SendMessage(Handle, WmNcLButtonDown, new IntPtr(HtCaption), IntPtr.Zero);
                return;
            }
            if (message == "back")
            {
                NavigateBack();
                return;
            }
            if (message == "home")
            {
                ReturnHome();
                return;
            }
            if (message != null && message.StartsWith("open:", StringComparison.Ordinal))
                OpenRoute(message.Substring(5));
        }

        private void OpenRoute(string route)
        {
            if (!UnifiedWorkspaceNavigation.IsAllowed(route)) return;
            var created = false;
            if (_hub == null || _hub.IsDisposed)
            {
                _hub = LeagueHubUiBridge.CreateUnifiedEmbeddedForm(_ui);
                if (_hub == null) return;
                created = true;
                _hub.SetInitialView(route);
                _hub.UseUnifiedWorkspaceHost();
                _hub.TopLevel = false;
                _hub.FormBorderStyle = FormBorderStyle.None;
                _hub.Dock = DockStyle.Fill;
                _hub.StartPosition = FormStartPosition.Manual;
                _hub.ShowInTaskbar = false;
                _hub.TopMost = false;
                _hub.MinimumSize = Size.Empty;
                _hub.MaximumSize = Size.Empty;
                _content.Controls.Add(_hub);
            }
            else if (!_hub.NavigateInUnifiedWorkspace(route))
            {
                // Unsaved settings editor explicitly declined navigation.
                SyncWebNavigation();
                return;
            }

            _navigation.Open(route);
            _browser.Dock = DockStyle.Top;
            _browser.Height = 58;
            _content.Visible = true;
            ResizeForRoute(true);
            if (created) _hub.Show();
            else _hub.Show();
            SyncWebNavigation();
        }

        private void NavigateBack()
        {
            if (_navigation.IsHome) return;
            var route = _navigation.Current;
            if (!TryCloseCurrentHub()) return;
            _navigation.Back();
            // Recreate the page through the verified service factory if history points to one.
            if (_navigation.IsHome) ShowHome();
            else
            {
                var prior = _navigation.Current;
                _navigation.Back();
                OpenRoute(prior);
            }
        }

        private void ReturnHome()
        {
            if (!TryCloseCurrentHub()) return;
            _navigation.Home();
            ShowHome();
        }

        private bool TryCloseCurrentHub()
        {
            if (_hub == null || _hub.IsDisposed) return true;
            _hub.Close();
            if (!_hub.IsDisposed) return false;
            _content.Controls.Remove(_hub);
            _hub = null;
            return true;
        }

        private void ShowHome()
        {
            _content.Visible = false;
            _browser.Dock = DockStyle.Fill;
            ResizeForRoute(false);
            SyncWebNavigation();
        }

        private void ResizeForRoute(bool detail)
        {
            if (IsDisposed) return;
            var area = Screen.FromControl(this).WorkingArea;
            var size = UnifiedWorkspaceNavigation.ResolveClientSize(area, detail);
            var original = Location;
            MinimumSize = Size.Empty;
            ClientSize = size;
            Location = new Point(
                Math.Max(area.Left + 2, Math.Min(original.X, area.Right - Width - 2)),
                Math.Max(area.Top + 2, Math.Min(original.Y, area.Bottom - Height - 2)));
        }

        private void SyncWebNavigation()
        {
            if (!_ready || IsDisposed || _browser.CoreWebView2 == null) return;
            _browser.CoreWebView2.PostWebMessageAsString(
                _navigation.IsHome ? "home" : "detail:" + _navigation.Current);
        }

        private void HandleClosing(object sender, FormClosingEventArgs e)
        {
            if (_closing) return;
            if (!TryCloseCurrentHub())
            {
                e.Cancel = true;
                return;
            }
            _closing = true;
        }

        internal static string ReadEmbeddedHtml()
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(HtmlResource))
            {
                if (stream == null) throw new InvalidOperationException("Unified workspace HTML resource missing.");
                using (var reader = new StreamReader(stream, new UTF8Encoding(false), true))
                    return reader.ReadToEnd();
            }
        }

        internal static void ValidateForSmokeTest()
        {
            UnifiedWorkspaceNavigation.ValidateForSmokeTest();
            var html = ReadEmbeddedHtml();
            foreach (var view in LeagueHubNavigation.Views)
            {
                if (html.IndexOf("data-route=\"" + view.Id + "\"", StringComparison.Ordinal) < 0)
                    throw new InvalidOperationException("Unified workspace HTML lost a native route.");
            }
            if (!html.Contains("data-action=\"back\"") || !html.Contains("data-action=\"home\"") ||
                !html.Contains("data-action=\"close\""))
                throw new InvalidOperationException("Unified workspace HTML lost back/home/close navigation.");
        }
    }
}
