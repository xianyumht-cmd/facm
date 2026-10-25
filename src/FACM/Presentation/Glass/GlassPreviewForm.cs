using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using FACM.Services;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace FACM.Presentation.Glass
{
    internal sealed class GlassPreviewForm : Form
    {
        private const string EmbeddedHtml = "FACM.Presentation.Glass.GGman-Glass-Prototype.html";
        private const string CompactEmbeddedHtml = "FACM.Presentation.Glass.GGman-Compact-Preview.html";
        private const int WmNcLButtonDown = 0x00A1;
        private const int HtCaption = 2;

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
        private readonly WebView2 _browser;
        private readonly bool _compact;
        private bool _compactDetail;
        private bool _initializing;

        internal GlassPreviewForm(bool compact = false)
        {
            _compact = compact;
            Text = "GGman"; // ui-text-contract: allow
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = compact ? new System.Drawing.Size(380, 285) : new System.Drawing.Size(1140, 740);
            MinimumSize = compact ? new System.Drawing.Size(320, 240) : new System.Drawing.Size(800, 540);
            ShowInTaskbar = true;
            FormBorderStyle = FormBorderStyle.None;
            BackColor = System.Drawing.Color.FromArgb(8, 18, 36);
            _browser = new WebView2 { Dock = DockStyle.Fill };
            Controls.Add(_browser);

            Shown += async delegate
            {
                if (_compact) ResizeCompactPreview();
                await InitializeBrowserAsync();
            };
            FormClosed += delegate
            {
                try
                {
                    if (_browser.CoreWebView2 != null)
                        _browser.CoreWebView2.Stop();
                }
                catch (Exception exception)
                {
                    AppLog.Error("Glass preview browser shutdown failed", exception);
                }
            };
        }

        private async Task InitializeBrowserAsync()
        {
            if (_initializing || IsDisposed) return;
            _initializing = true;
            try
            {
                var userData = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "FACM",
                    "GlassPreviewWebView2");

                var environment = await CoreWebView2Environment.CreateAsync(null, userData);
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
                core.WebMessageReceived += HandlePageMessage;

                // The accepted HTML is loaded verbatim. Only window-close requests cross into native code.
                await core.AddScriptToExecuteOnDocumentCreatedAsync(@"
                    (function () {
                        function closeHost() {
                            if (window.chrome && window.chrome.webview)
                                window.chrome.webview.postMessage('close');
                        }
                        document.addEventListener('click', function (event) {
                            var button = event.target.closest('[data-action=""hide""]');
                            if (button || event.target.id === 'stage' || event.target.closest('#hideWindow')) closeHost();
                        }, true);
                        document.addEventListener('mousedown', function (event) {
                            if (event.button === 0 && event.target.closest('.desktop-top')
                                && window.chrome && window.chrome.webview)
                                window.chrome.webview.postMessage('drag');
                        }, true);
                        document.addEventListener('keydown', function (event) {
                            if (event.key !== 'Escape') return;
                            if (document.querySelector('#modalScrim.open, #quickPop.open')) return;
                            closeHost();
                        });
                    })();");

                core.NavigateToString(ReadEmbeddedHtml(_compact));
            }
            catch (Exception exception)
            {
                AppLog.Error(
                    "Glass preview initialization failed; Is64BitProcess=" + Environment.Is64BitProcess,
                    exception);
                if (IsDisposed) return;
                var error = exception.GetType().Name + " (0x" + exception.HResult.ToString("X8") + ")";
                var guidance = exception is BadImageFormatException
                    ? "检测到组件架构不匹配，请使用修复后的 x64 预览包。" // ui-text-contract: allow
                    : "请查看程序目录 logs 文件夹内的 Glass preview initialization failed 日志。"; // ui-text-contract: allow
                MessageBox.Show(
                    "Glass 预览启动失败。\n" + error + "\n" + guidance, // ui-text-contract: allow
                    "GGman Glass Preview", // ui-text-contract: allow
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                Close();
            }
        }

        private void HandlePageMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            string message;
            try { message = e.TryGetWebMessageAsString(); }
            catch { return; }

            if (IsDisposed) return;
            if (message == "drag")
            {
                if (WindowState != FormWindowState.Normal) return;
                ReleaseCapture();
                SendMessage(Handle, WmNcLButtonDown, new IntPtr(HtCaption), IntPtr.Zero);
            }
            else if (message == "close")
            {
                Close();
            }
            else if (_compact && (message == "compact-detail" || message == "compact-home"))
            {
                _compactDetail = message == "compact-detail";
                ResizeCompactPreview();
            }
        }

        private void ResizeCompactPreview()
        {
            var preferredSize = _compactDetail
                ? new System.Drawing.Size(548, 478)
                : new System.Drawing.Size(380, 285);
            var bounds = Screen.FromControl(this).WorkingArea;
            // Keep the preview reachable on small/high-DPI work areas; detail content can scroll.
            var size = new System.Drawing.Size(
                Math.Min(preferredSize.Width, Math.Max(MinimumSize.Width, bounds.Width)),
                Math.Min(preferredSize.Height, Math.Max(MinimumSize.Height, bounds.Height)));
            var oldLocation = Location;
            ClientSize = size;
            Location = new System.Drawing.Point(
                Math.Max(bounds.Left, Math.Min(oldLocation.X, bounds.Right - Width)),
                Math.Max(bounds.Top, Math.Min(oldLocation.Y, bounds.Bottom - Height)));
        }

        private static string ReadEmbeddedHtml(bool compact)
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(compact ? CompactEmbeddedHtml : EmbeddedHtml))
            {
                if (stream == null) throw new InvalidOperationException("Glass preview asset missing.");
                using (var reader = new StreamReader(stream, new UTF8Encoding(false), true))
                    return reader.ReadToEnd();
            }
        }
    }
}
