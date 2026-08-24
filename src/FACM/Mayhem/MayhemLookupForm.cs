using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FACM.League;

namespace FACM.Mayhem
{
    internal sealed class MayhemLookupForm : Form
    {
        private readonly ILeagueClientApi _leagueClient;
        private readonly TextBox _query;
        private readonly Button _search;
        private readonly Button _cancel;
        private readonly Button _saveImage;
        private readonly Button _copyImage;
        private readonly ProgressBar _progress;
        private readonly Label _status;
        private readonly Panel _imageHost;
        private readonly PictureBox _resultImage;
        private readonly System.Windows.Forms.Timer _elapsedTimer;
        private CancellationTokenSource _queryCancellation;
        private DateTime _queryStartedAt;
        private string _stageText = MayhemUiCopy.Ready;
        private bool _busy;

        public MayhemLookupForm(ILeagueClientApi leagueClient)
        {
            _leagueClient = leagueClient ?? throw new ArgumentNullException(nameof(leagueClient));
            Text = MayhemUiCopy.WindowTitle;
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(920, 700);
            ClientSize = new Size(1120, 820);
            BackColor = Color.FromArgb(10, 15, 25);
            ForeColor = Color.FromArgb(240, 245, 255);
            Font = new Font("Microsoft YaHei UI", 9F);

            var title = new Label
            {
                Text = MayhemUiCopy.WindowTitle,
                Location = new Point(24, 16),
                AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 18F, FontStyle.Bold),
                ForeColor = Color.White
            };
            var hint = new Label
            {
                Text = MayhemUiCopy.PageHint,
                Location = new Point(26, 54),
                Size = new Size(980, 24),
                ForeColor = Color.FromArgb(150, 166, 196)
            };

            _query = new TextBox
            {
                Location = new Point(24, 90),
                Size = new Size(510, 36),
                Font = new Font("Microsoft YaHei UI", 11F),
                BackColor = Color.FromArgb(28, 37, 56),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
            _query.KeyDown += QueryKeyDown;

            _search = CreateButton(MayhemUiCopy.Search, new Rectangle(546, 88, 100, 40), Color.FromArgb(69, 112, 255));
            _search.Click += async delegate { await SearchAsync(); };
            _cancel = CreateButton(MayhemUiCopy.Cancel, new Rectangle(656, 88, 92, 40), Color.FromArgb(53, 62, 82));
            _cancel.Enabled = false;
            _cancel.Click += delegate { CancelCurrentQuery(); };
            _saveImage = CreateButton(MayhemUiCopy.SaveImage, new Rectangle(778, 88, 108, 40), Color.FromArgb(43, 126, 102));
            _saveImage.Enabled = false;
            _saveImage.Click += SaveImage;
            _copyImage = CreateButton(MayhemUiCopy.CopyImage, new Rectangle(896, 88, 108, 40), Color.FromArgb(73, 83, 112));
            _copyImage.Enabled = false;
            _copyImage.Click += CopyImage;

            _progress = new ProgressBar
            {
                Location = new Point(24, 139),
                Size = new Size(1080, 5),
                Style = ProgressBarStyle.Blocks,
                Minimum = 0,
                Maximum = 100,
                Value = 0,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            _status = new Label
            {
                Text = _stageText,
                Location = new Point(24, 151),
                Size = new Size(1080, 26),
                ForeColor = Color.FromArgb(99, 205, 166),
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            _imageHost = new Panel
            {
                Location = new Point(24, 184),
                Size = new Size(1080, 612),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                AutoScroll = true,
                BackColor = Color.FromArgb(15, 22, 35),
                BorderStyle = BorderStyle.FixedSingle
            };
            _resultImage = new PictureBox
            {
                Location = new Point(12, 12),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.FromArgb(15, 22, 35)
            };
            _imageHost.Controls.Add(_resultImage);
            _imageHost.Resize += delegate { ResizePreview(); };
            _resultImage.Image = CreateEmptyCard();
            ResizePreview();

            Controls.Add(title);
            Controls.Add(hint);
            Controls.Add(_query);
            Controls.Add(_search);
            Controls.Add(_cancel);
            Controls.Add(_saveImage);
            Controls.Add(_copyImage);
            Controls.Add(_progress);
            Controls.Add(_status);
            Controls.Add(_imageHost);

            _elapsedTimer = new System.Windows.Forms.Timer { Interval = 250 };
            _elapsedTimer.Tick += UpdateElapsed;
            AcceptButton = _search;
            FormClosing += delegate { CancelCurrentQuery(); };
            FormClosed += delegate
            {
                _elapsedTimer.Stop();
                _elapsedTimer.Dispose();
                DisposeCancellation();
                var image = _resultImage.Image;
                _resultImage.Image = null;
                if (image != null) image.Dispose();
            };
        }

        private async Task SearchAsync()
        {
            if (_busy) return;
            var text = _query.Text.Trim();
            if (text.Length == 0)
            {
                MessageBox.Show(MayhemUiCopy.EnterChampion, "FACM", MessageBoxButtons.OK, MessageBoxIcon.Information);
                _query.Focus();
                return;
            }

            DisposeCancellation();
            _queryCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(13));
            _queryStartedAt = DateTime.UtcNow;
            _stageText = MayhemUiCopy.ReadingHero;
            SetBusy(true);
            var progress = new Progress<string>(message =>
            {
                _stageText = CleanProgressText(message);
                UpdateStatusText();
            });

            try
            {
                var token = _queryCancellation.Token;
                var result = await OpggMayhemService.QueryAsync(text, progress, token);
                if (IsDisposed) return;
                if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
                {
                    _stageText = CleanErrorText(result.ErrorMessage);
                    _status.ForeColor = Color.FromArgb(255, 155, 120);
                    UpdateStatusText(false);
                    return;
                }

                _stageText = MayhemUiCopy.OrganizingAssets;
                UpdateStatusText();
                await RiotGameDataService.EnrichAsync(result, _leagueClient, token);

                _stageText = MayhemUiCopy.ReadingAugments;
                UpdateStatusText();
                await MayhemRankedAugmentService.EnrichAsync(result, token);
                SanitizeResult(result);

                _stageText = MayhemUiCopy.Rendering;
                UpdateStatusText();
                var image = await MayhemCardRenderer.RenderAsync(result, _leagueClient, token);
                if (IsDisposed)
                {
                    image.Dispose();
                    return;
                }
                SetResultImage(image);
                _saveImage.Enabled = true;
                _copyImage.Enabled = true;
                _stageText = MayhemUiCopy.Completed + " · " + DescribeAugmentSource(result);
                _status.ForeColor = Color.FromArgb(99, 205, 166);
                UpdateStatusText(false);
            }
            catch (OperationCanceledException)
            {
                if (!IsDisposed)
                {
                    var elapsed = DateTime.UtcNow - _queryStartedAt;
                    _stageText = elapsed.TotalSeconds >= 12.5 ? MayhemUiCopy.Timeout : MayhemUiCopy.QueryCanceled;
                    _status.ForeColor = elapsed.TotalSeconds >= 12.5 ? Color.FromArgb(255, 155, 120) : Color.FromArgb(170, 180, 200);
                    UpdateStatusText(false);
                }
            }
            catch (Exception exception)
            {
                Services.AppLog.Error("Mayhem card rendering failed", exception);
                if (!IsDisposed)
                {
                    _stageText = MayhemUiCopy.Failed;
                    _status.ForeColor = Color.FromArgb(255, 155, 120);
                    UpdateStatusText(false);
                }
            }
            finally
            {
                if (!IsDisposed) SetBusy(false);
            }
        }

        private void SetResultImage(Bitmap bitmap)
        {
            var old = _resultImage.Image;
            _resultImage.Image = bitmap;
            _imageHost.AutoScrollPosition = Point.Empty;
            ResizePreview();
            if (old != null) old.Dispose();
        }

        private void ResizePreview()
        {
            if (_imageHost == null || _resultImage == null) return;
            var availableWidth = Math.Max(420, _imageHost.ClientSize.Width - 42);
            var image = _resultImage.Image;
            var ratio = image != null && image.Width > 0
                ? image.Height / (double)image.Width
                : MayhemCardRenderer.CardHeight / (double)MayhemCardRenderer.CardWidth;
            var height = Math.Max(360, (int)Math.Round(availableWidth * ratio));
            _resultImage.Size = new Size(availableWidth, height);
        }

        private void SaveImage(object sender, EventArgs e)
        {
            if (_resultImage.Image == null) return;
            using (var dialog = new SaveFileDialog
            {
                Filter = MayhemUiCopy.SaveFilter,
                DefaultExt = "png",
                AddExtension = true,
                FileName = "FACM-" + MayhemUiCopy.SavePrefix + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".png"
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    _resultImage.Image.Save(dialog.FileName, ImageFormat.Png);
                    _stageText = MayhemUiCopy.Saved;
                    _status.ForeColor = Color.FromArgb(99, 205, 166);
                    UpdateStatusText(false);
                }
                catch (Exception exception)
                {
                    Services.AppLog.Error("Save Mayhem card failed", exception);
                    MessageBox.Show(MayhemUiCopy.SaveFailed, "FACM", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void CopyImage(object sender, EventArgs e)
        {
            if (_resultImage.Image == null) return;
            try
            {
                using (var clone = new Bitmap(_resultImage.Image)) Clipboard.SetImage(clone);
                _stageText = MayhemUiCopy.Copied;
                _status.ForeColor = Color.FromArgb(99, 205, 166);
                UpdateStatusText(false);
            }
            catch (Exception exception)
            {
                Services.AppLog.Error("Copy Mayhem card failed", exception);
                MessageBox.Show(MayhemUiCopy.CopyFailed, "FACM", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static void SanitizeResult(MayhemChampionResult result)
        {
            if (result == null) return;
            if (LooksLikeTechnicalFallback(result.BalanceSummary)) result.BalanceSummary = null;
            if (LooksLikeTechnicalFallback(result.SkillOrder)) result.SkillOrder = null;
            result.CoreItems = result.CoreItems.Where(value => !LooksLikeTechnicalFallback(value)).Take(5).ToList();
            result.Augments = result.Augments.Where(value => !LooksLikeTechnicalFallback(value)).Take(5).ToList();
            result.AugmentRows = result.AugmentRows
                .Where(value => value != null && !LooksLikeTechnicalFallback(value.Name))
                .Take(12)
                .ToList();
            while (result.CoreItemIconUrls.Count > result.CoreItems.Count) result.CoreItemIconUrls.RemoveAt(result.CoreItemIconUrls.Count - 1);
            while (result.AugmentIconUrls.Count > result.Augments.Count) result.AugmentIconUrls.RemoveAt(result.AugmentIconUrls.Count - 1);
        }

        private static string DescribeAugmentSource(MayhemChampionResult result)
        {
            if (result == null || result.AugmentRows.Count == 0) return MayhemUiCopy.BasicGenerated;
            if (string.Equals(result.AugmentSourceRoute, "fresh-cache", StringComparison.OrdinalIgnoreCase)) return MayhemUiCopy.FreshCache;
            if (string.Equals(result.AugmentSourceRoute, "stale-cache", StringComparison.OrdinalIgnoreCase) || result.AugmentSourceStale) return MayhemUiCopy.StaleCache;
            return MayhemUiCopy.LatestAugments;
        }

        private static bool LooksLikeTechnicalFallback(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            var text = value.ToLowerInvariant();
            return text.Contains(MayhemUiCopy.TriggerDataSource) ||
                   text.Contains(MayhemUiCopy.TriggerUnparsed) ||
                   text.Contains(MayhemUiCopy.TriggerOpggPage) ||
                   text.Contains(MayhemUiCopy.TriggerPageNoData);
        }

        private static string CleanProgressText(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return MayhemUiCopy.Search;
            if (message.Contains(MayhemUiCopy.TriggerParallel)) return MayhemUiCopy.ReadingLatest;
            if (message.Contains(MayhemUiCopy.TriggerParseHero)) return MayhemUiCopy.Organizing;
            if (message.Contains(MayhemUiCopy.TriggerCache)) return MayhemUiCopy.ReadingCache;
            return message.Replace("OP.GG", MayhemUiCopy.ExternalGuide).Replace(MayhemUiCopy.TriggerDataSource, MayhemUiCopy.DataWord);
        }

        private static string CleanErrorText(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return MayhemUiCopy.Failed;
            if (message.Contains(MayhemUiCopy.TriggerSeconds)) return MayhemUiCopy.TimeoutShort;
            if (message.Contains(MayhemUiCopy.TriggerDataSource)) return MayhemUiCopy.NoData;
            return message;
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            _search.Enabled = !busy;
            _query.Enabled = !busy;
            _cancel.Enabled = busy;
            _search.Text = busy ? MayhemUiCopy.Searching : MayhemUiCopy.Search;
            _progress.Style = busy ? ProgressBarStyle.Marquee : ProgressBarStyle.Blocks;
            _progress.MarqueeAnimationSpeed = busy ? 24 : 0;
            if (!busy) _progress.Value = 0;
            if (busy)
            {
                _status.ForeColor = Color.FromArgb(112, 165, 255);
                _elapsedTimer.Start();
            }
            else
            {
                _elapsedTimer.Stop();
                DisposeCancellation();
            }
            UpdateStatusText(busy);
        }

        private void CancelCurrentQuery()
        {
            if (_queryCancellation == null || _queryCancellation.IsCancellationRequested) return;
            _stageText = MayhemUiCopy.Canceling;
            _queryCancellation.Cancel();
            _cancel.Enabled = false;
            UpdateStatusText();
        }

        private void DisposeCancellation()
        {
            if (_queryCancellation == null) return;
            _queryCancellation.Dispose();
            _queryCancellation = null;
        }

        private void UpdateElapsed(object sender, EventArgs e) { UpdateStatusText(); }

        private void UpdateStatusText(bool includeElapsed = true)
        {
            if (IsDisposed) return;
            if (includeElapsed && _busy)
            {
                var elapsed = DateTime.UtcNow - _queryStartedAt;
                _status.Text = _stageText + "  ·  " + elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s";
            }
            else _status.Text = _stageText;
        }

        private void QueryKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            if (!_busy) _ = SearchAsync();
        }

        private static Button CreateButton(string text, Rectangle bounds, Color background)
        {
            var button = new Button
            {
                Text = text,
                Location = bounds.Location,
                Size = bounds.Size,
                FlatStyle = FlatStyle.Flat,
                BackColor = background,
                ForeColor = Color.White,
                Cursor = Cursors.Hand,
                Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold)
            };
            button.FlatAppearance.BorderColor = Color.FromArgb(100, 128, 174);
            return button;
        }

        private static Bitmap CreateEmptyCard()
        {
            var result = new MayhemChampionResult
            {
                ChampionName = MayhemUiCopy.EmptyCard,
                Patch = "—",
                Tier = "—",
                BalanceSummary = MayhemUiCopy.EmptyBalance
            };
            return MayhemCardRenderer.RenderForSmokeTest(result);
        }
    }
}
