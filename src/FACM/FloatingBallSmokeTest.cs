using System;
using System.Drawing;
using System.Windows.Forms;
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

                using (var form = new MainForm(false))
                {
                    if (form.Width < 80 || form.Height < 80)
                        throw new InvalidOperationException("Built-in floating ball window is unexpectedly small.");
                    if (form.FormBorderStyle != FormBorderStyle.None || !form.TopMost)
                        throw new InvalidOperationException("Built-in floating ball window settings are invalid.");
                    if (!form.TransparencyKey.IsEmpty)
                        throw new InvalidOperationException("Built-in floating ball must not use color-key transparency.");
                    if (form.Region != null)
                        throw new InvalidOperationException("Built-in floating ball must use per-pixel alpha instead of a clipped Region.");

                    using (var bitmap = LayeredFloatingBall.RenderForSmokeTest(form.Width, 0.5f, 1.2f))
                    {
                        if (bitmap.GetPixel(0, 0).A > 3 ||
                            bitmap.GetPixel(bitmap.Width - 1, 0).A > 3 ||
                            bitmap.GetPixel(0, bitmap.Height - 1).A > 3 ||
                            bitmap.GetPixel(bitmap.Width - 1, bitmap.Height - 1).A > 3)
                            throw new InvalidOperationException("Built-in floating ball corners are not truly transparent.");

                        var visibleBluePixels = 0;
                        var nearBlackVisiblePixels = 0;
                        var translucentEdgePixels = 0;
                        for (var y = 0; y < bitmap.Height; y++)
                        {
                            for (var x = 0; x < bitmap.Width; x++)
                            {
                                var pixel = bitmap.GetPixel(x, y);
                                if (pixel.A > 90 && pixel.B > 90 && pixel.B > pixel.R + 15)
                                    visibleBluePixels++;
                                if (pixel.A > 90 && pixel.R < 24 && pixel.G < 24 && pixel.B < 24)
                                    nearBlackVisiblePixels++;
                                if (pixel.A > 8 && pixel.A < 180)
                                    translucentEdgePixels++;
                            }
                        }

                        if (visibleBluePixels < 1400)
                            throw new InvalidOperationException("Built-in floating ball did not produce enough visible blue content.");
                        if (nearBlackVisiblePixels > 8)
                            throw new InvalidOperationException("Built-in floating ball still contains a visible black fringe.");
                        if (translucentEdgePixels < 80)
                            throw new InvalidOperationException("Built-in floating ball does not have anti-aliased alpha edges.");
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
