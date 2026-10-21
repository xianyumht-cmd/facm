using System;
using System.IO;
using System.Reflection;
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
        private readonly WebView2 _browser;
        private bool _initializing;

        internal GlassPreviewForm()
        {
            Text = UiTextCatalog.Load().AppName;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new System.Drawing.Size(1140, 740);
            MinimumSize = new System.Drawing.Size(800, 540);
            ShowInTaskbar = true;
            FormBorderStyle = FormBorderStyle.Sizable;
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
                            if (button || event.target.id === 'stage') closeHost();
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
                AppLog.Error("Glass preview initialization failed", exception);
                if (IsDisposed) return;
                MessageBox.Show(
                    "Glass 预览启动失败。请检查 WebView2 Runtime；原有 GGman 功能不会受到影响。", // ui-text-contract: allow
                    "GGman Glass Preview",
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

            if (message != "close" || IsDisposed) return;
            if (InvokeRequired)
                BeginInvoke(new Action(Close));
            else
                Close();
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
