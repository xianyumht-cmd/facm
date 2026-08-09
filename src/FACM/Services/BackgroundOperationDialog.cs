using System;
using System.ComponentModel;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FACM.Services
{
    internal sealed class BackgroundOperationDialog : Form
    {
        private readonly Func<object> _worker;
        private object _result;
        private Exception _error;
        private bool _completed;

        private BackgroundOperationDialog(string titleText, string statusText, Func<object> worker)
        {
            _worker = worker ?? throw new ArgumentNullException(nameof(worker));
            Text = string.IsNullOrWhiteSpace(titleText) ? "FACM" : titleText;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ControlBox = false;
            ShowInTaskbar = false;
            TopMost = true;
            ClientSize = new Size(390, 132);
            BackColor = Color.FromArgb(20, 26, 38);
            ForeColor = Color.White;
            Font = new Font("Microsoft YaHei UI", 9F);

            var title = new Label
            {
                Text = Text,
                Location = new Point(20, 18),
                Size = new Size(350, 28),
                Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold),
                ForeColor = Color.White
            };
            var status = new Label
            {
                Text = string.IsNullOrWhiteSpace(statusText) ? "正在处理，请稍候…" : statusText,
                Location = new Point(20, 50),
                Size = new Size(350, 26),
                AutoEllipsis = true,
                ForeColor = Color.FromArgb(174, 188, 212)
            };
            var progress = new ProgressBar
            {
                Location = new Point(20, 88),
                Size = new Size(350, 18),
                Style = ProgressBarStyle.Marquee,
                MarqueeAnimationSpeed = 28
            };

            Controls.Add(title);
            Controls.Add(status);
            Controls.Add(progress);
            Shown += BeginOperation;
        }

        public static T Run<T>(string titleText, string statusText, Func<T> worker)
        {
            if (worker == null) throw new ArgumentNullException(nameof(worker));
            using (var dialog = new BackgroundOperationDialog(titleText, statusText, delegate { return worker(); }))
            {
                var owner = Form.ActiveForm;
                if (owner != null && !owner.IsDisposed)
                    dialog.ShowDialog(owner);
                else
                    dialog.ShowDialog();

                if (dialog._error != null) throw dialog._error;
                return dialog._result == null ? default(T) : (T)dialog._result;
            }
        }

        private async void BeginOperation(object sender, EventArgs e)
        {
            try
            {
                _result = await Task.Run(_worker);
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

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_completed)
            {
                e.Cancel = true;
                return;
            }
            base.OnFormClosing(e);
        }
    }
}
