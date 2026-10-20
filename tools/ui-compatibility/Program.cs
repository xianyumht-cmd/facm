using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using FACM.League;
using ReaLTaiizor.Controls;

namespace GGman.UiCompatibility
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var watch = Stopwatch.StartNew();
            try {
                using (var form = new RepairPreview()) {
                    if (args.Length == 0) { Application.Run(form); return 0; }
                    form.Show();
                    Application.DoEvents();
                    if (args.Length == 2 && args[0] == "--capture") {
                        Directory.CreateDirectory(args[1]);
                        foreach (var name in new[] { "dark-wide", "light-wide", "dark-narrow" }) {
                            form.ClientSize = name.EndsWith("narrow") ? new Size(500, 760) : new Size(980, 760);
                            form.SetPalette(name.StartsWith("light"));
                            Application.DoEvents();
                            form.View.ValidateLayout();
                            using (var bitmap = new Bitmap(form.ClientSize.Width, form.ClientSize.Height)) {
                                form.View.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                                bitmap.Save(Path.Combine(args[1], name + ".png"), ImageFormat.Png);
                            }
                        }
                        return 0;
                    }
                    if (args.Length != 2 || (args[0] != "--smoke" && args[0] != "--smoke-merged"))
                        throw new ArgumentException("Expected --smoke, --smoke-merged or --capture and an output path.");
                    var merged = args[0] == "--smoke-merged";
                    var assembly = typeof(PoisonButton).Assembly;
                    if (merged && (assembly != typeof(Program).Assembly || assembly.GetReferencedAssemblies().Any(a => a.Name == "ReaLTaiizor")))
                        throw new InvalidOperationException("Controls still depend on an external library.");
                    var original = form.View.Actions.Select(a => a.Text).ToArray();
                    for (var i = 0; i < 20; i++) form.SetPalette(i % 2 == 0);
                    if (!original.SequenceEqual(form.View.Actions.Select(a => a.Text))) throw new InvalidOperationException("Theme changed action text.");
                    foreach (var width in new[] { 980, 720, 500 }) {
                        form.ClientSize = new Size(width, 760);
                        Application.DoEvents();
                        form.View.ValidateLayout();
                    }
                    VerifyKeyboardNavigation(form);
                    var process = Process.GetCurrentProcess();
                    process.Refresh();
                    File.WriteAllText(args[1], new JavaScriptSerializer().Serialize(new {
                        status = "PASS: shared repair view construction, themes, responsive layout and programmatic keyboard navigation",
                        visualAcceptance = "INCONCLUSIVE: native Win10 and independent visual review required",
                        os = Environment.OSVersion.VersionString, clr = Environment.Version.ToString(),
                        assembly = assembly.FullName, mergedExecutable = merged,
                        resources = assembly.GetManifestResourceNames(),
                        references = assembly.GetReferencedAssemblies().Select(a => a.FullName).ToArray(),
                        dpi = form.DeviceDpi, constructionAndSmokeMilliseconds = watch.Elapsed.TotalMilliseconds,
                        privateBytes = process.PrivateMemorySize64, workingSetBytes = process.WorkingSet64, handles = process.HandleCount
                    }));
                }
                return 0;
            } catch (Exception error) {
                if (args.Length == 2 && args[0] != "--capture") File.WriteAllText(args[1], error.ToString());
                else if (args.Length == 0) MessageBox.Show(error.Message);
                else Console.Error.WriteLine(error);
                return 1;
            }
        }
        private static void VerifyKeyboardNavigation(RepairPreview form)
        {
            form.ClientSize = new Size(500, 420);
            Application.DoEvents();
            var view = form.View;
            var viewport = (ScrollableControl)view.Controls[0];
            var clicks = 0;
            view.ActionRequested += index => clicks++;
            view.Actions[0].Focus();
            for (var i = 1; i < view.Actions.Length; i++) {
                if (!form.SelectNextControl(view.Actions[i - 1], true, true, true, false))
                    throw new InvalidOperationException("Repair keyboard traversal stopped early.");
                Application.DoEvents();
                if (!view.Actions[i].Focused)
                    throw new InvalidOperationException("Repair keyboard order changed.");
                var visible = viewport.RectangleToScreen(viewport.ClientRectangle);
                var action = view.Actions[i].RectangleToScreen(view.Actions[i].ClientRectangle);
                if (!visible.Contains(action))
                    throw new InvalidOperationException("Focused repair action is outside the viewport.");
            }
            for (var i = view.Actions.Length - 2; i >= 0; i--) {
                form.SelectNextControl(view.Actions[i + 1], false, true, true, false);
                if (!view.Actions[i].Focused) throw new InvalidOperationException("Reverse repair keyboard order changed.");
            }
            var dispatch = typeof(GameRepairView).GetMethod("ProcessCmdKey", BindingFlags.Instance | BindingFlags.NonPublic);
            viewport.AutoScrollPosition = Point.Empty;
            var down = new object[] { new Message(), Keys.PageDown };
            if (!(bool)dispatch.Invoke(view, down) || viewport.AutoScrollPosition.Y >= 0)
                throw new InvalidOperationException("PageDown did not scroll repair content.");
            var up = new object[] { new Message(), Keys.PageUp };
            if (!(bool)dispatch.Invoke(view, up) || viewport.AutoScrollPosition.Y != 0)
                throw new InvalidOperationException("PageUp did not restore repair scroll position.");
            if (clicks != 0) throw new InvalidOperationException("Keyboard navigation invoked a repair action.");
        }

    }

    internal sealed class RepairPreview : Form
    {
        internal readonly GameRepairView View;
        private bool light;
        internal RepairPreview()
        {
            Text = "GGman · 游戏修复预览（离线，F6 切换明暗）";
            ClientSize = new Size(980, 760);
            MinimumSize = new Size(440, 420);
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96, 96);
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;
            View = new GameRepairView();
            View.ActionRequested += index => View.SetStatus("离线预览 · 未执行游戏操作", light ? Color.FromArgb(30, 50, 75) : Color.FromArgb(220, 231, 245));
            Controls.Add(View);
            SetPalette(false);
        }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.F6) { SetPalette(!light); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
        internal void SetPalette(bool value)
        {
            light = value;
            View.ApplyPalette(light,
                light ? Color.FromArgb(239, 243, 248) : Color.FromArgb(17, 22, 30),
                light ? Color.White : Color.FromArgb(29, 37, 48),
                light ? Color.FromArgb(20, 32, 49) : Color.FromArgb(240, 244, 250),
                light ? Color.FromArgb(75, 91, 112) : Color.FromArgb(179, 193, 210),
                light ? Color.FromArgb(35, 91, 179) : Color.FromArgb(135, 185, 255),
                light ? Color.FromArgb(166, 36, 52) : Color.FromArgb(255, 161, 168));
        }
    }
}
