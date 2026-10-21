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
        private const int WmNcLButtonDown = 0x00A1;
        private const int HtCaption = 2;

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
        private readonly WebView2 _browser;
        private bool _initializing;

        internal GlassPreviewForm()
        {
            Text = "GGman"; // ui-text-contract: allow
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new System.Drawing.Size(1140, 740);
            MinimumSize = new System.Drawing.Size(800, 540);
            ShowInTaskbar = true;
            FormBorderStyle = FormBorderStyle.None;
            BackColor = System.Drawing.Color.FromArgb(8, 18, 36);
            _browser = new WebView2 { Dock = DockStyle.Fill };
            Controls.Add(_browser);

            Shown += async delegate { await InitializeBrowserAsync(); };
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

                core.NavigateToString(ReadEmbeddedHtml());
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
        }

        private static string ReadEmbeddedHtml()
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(EmbeddedHtml))
            {
                if (stream == null) throw new InvalidOperationException("Glass preview asset missing.");
                using (var reader = new StreamReader(stream, new UTF8Encoding(false), true))
                    return reader.ReadToEnd();
            }
        }
    }
}
