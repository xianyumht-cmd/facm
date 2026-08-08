using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FACM.Services
{
    internal sealed class GameLocatorSearchDialog : Form
    {
        private readonly Func<CancellationToken, IProgress<int>, string> _worker;
        private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
        private readonly Label _status;
        private readonly Button _cancel;
        private string _result;
        private Exception _error;
        private bool _completed;

        private GameLocatorSearchDialog(
            string statusText,
            Func<CancellationToken, IProgress<int>, string> worker)
        {
            _worker = worker ?? throw new ArgumentNullException(nameof(worker));

            Text = "FACM 目录识别";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ControlBox = false;
            ShowInTaskbar = false;
            TopMost = true;
            ClientSize = new Size(390, 150);
            BackColor = Color.FromArgb(20, 26, 38);
            ForeColor = Color.White;
            Font = new Font("Microsoft YaHei UI", 9F);

            var title = new Label
            {
                Text = "正在识别游戏目录",
                Location = new Point(20, 18),
                Size = new Size(350, 28),
                Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold),
                ForeColor = Color.White
            };

            _status = new Label
            {
                Text = string.IsNullOrWhiteSpace(statusText) ? "正在搜索…" : statusText,
                Location = new Point(20, 52),
                Size = new Size(350, 40),
                AutoEllipsis = true,
                ForeColor = Color.FromArgb(174, 188, 212)
            };

            _cancel = new Button
            {
                Text = "取消识别",
                Location = new Point(266, 104),
                Size = new Size(104, 30),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(43, 54, 73),
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
            _cancel.FlatAppearance.BorderColor = Color.FromArgb(76, 93, 124);
            _cancel.Click += delegate
            {
                if (_completed || _cancellation.IsCancellationRequested) return;
                _cancel.Enabled = false;
                _status.Text = "正在取消目录识别…";
                _cancellation.Cancel();
            };

            Controls.Add(title);
            Controls.Add(_status);
            Controls.Add(_cancel);
            Shown += BeginSearch;
        }

        public static string Run(
            string statusText,
            Func<CancellationToken, IProgress<int>, string> worker)
        {
            using (var dialog = new GameLocatorSearchDialog(statusText, worker))
            {
                var owner = Form.ActiveForm;
                if (owner != null && !owner.IsDisposed)
                    dialog.ShowDialog(owner);
                else
                    dialog.ShowDialog();

                if (dialog._error != null) throw dialog._error;
                return dialog._result;
            }
        }

        private async void BeginSearch(object sender, EventArgs e)
        {
            var progress = new Progress<int>(count =>
            {
                if (IsDisposed || Disposing || _completed || _cancellation.IsCancellationRequested) return;
                _status.Text = "正在搜索目录… 已检查 " + count + " 个文件夹";
            });

            try
            {
                _result = await Task.Run(
                    () => _worker(_cancellation.Token, progress),
                    _cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                _error = new GameLocationSearchCancelledException();
            }
            catch (Exception exception)
            {
                _error = exception;
            }
            finally
            {
                _completed = true;
                if (!IsDisposed && !Disposing)
                {
                    DialogResult = DialogResult.OK;
                    Close();
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try { _cancellation.Cancel(); } catch { }
                _cancellation.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
