using System;
using System.Drawing;
using System.Windows.Forms;
using FACM.AppHost.Modules;
using FACM.Services;

namespace FACM
{
    internal static class FloatingBallSmokeTest
    {
        public static int Run()
        {
            try
            {
                RuntimePaths.Initialize();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                var leagueClient = new LeagueClientModule();
                using (var form = new MainForm(
                    new AppSettings(),
                    UiTextCatalog.Load(),
                    new ToolsModule(),
                    new OnlineModule(),
                    new PetsModule(),
                    new MayhemModule(leagueClient),
                    new CleanupModule(),
                    false))
                {
                    if (form.Width < 52 || form.Height < 52)
                        throw new InvalidOperationException("Built-in FACM shell window is unexpectedly small.");
                    if (form.FormBorderStyle != FormBorderStyle.None || !form.TopMost)
                        throw new InvalidOperationException("Built-in FACM shell window settings are invalid.");
                    if (!form.TransparencyKey.IsEmpty)
                        throw new InvalidOperationException("Built-in FACM shell must not use color-key transparency.");
                    if (form.Region != null)
                        throw new InvalidOperationException("Built-in FACM shell must use per-pixel alpha instead of a clipped Region.");

                    using (var bitmap = LayeredFloatingBall.RenderForSmokeTest(form.Width, 0.5f, 1.2f))
                    {
                        if (bitmap.GetPixel(0, 0).A > 3 ||
                            bitmap.GetPixel(bitmap.Width - 1, 0).A > 3 ||
                            bitmap.GetPixel(0, bitmap.Height - 1).A > 3 ||
                            bitmap.GetPixel(bitmap.Width - 1, bitmap.Height - 1).A > 3)
                            throw new InvalidOperationException("Built-in FACM shell corners are not truly transparent.");

                        var visibleBodyPixels = 0;
                        var translucentEdgePixels = 0;
                        var accentPixels = 0;
                        for (var y = 0; y < bitmap.Height; y++)
                        {
                            for (var x = 0; x < bitmap.Width; x++)
                            {
                                var pixel = bitmap.GetPixel(x, y);
                                if (pixel.A > 150) visibleBodyPixels++;
                                if (pixel.A > 8 && pixel.A < 180) translucentEdgePixels++;
                                if (pixel.A > 120 && pixel.B > pixel.R + 18) accentPixels++;
                            }
                        }

                        if (visibleBodyPixels < 1350)
                            throw new InvalidOperationException("Built-in FACM shell did not produce enough visible body content.");
                        if (translucentEdgePixels < 55)
                            throw new InvalidOperationException("Built-in FACM shell does not have anti-aliased alpha edges.");
                        if (accentPixels < 10)
                            throw new InvalidOperationException("Built-in FACM shell did not preserve a restrained theme accent.");
                    }
                    form.Close();
                }
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 6;
            }
        }
    }
}
