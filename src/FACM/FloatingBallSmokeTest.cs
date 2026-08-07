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
                        throw new InvalidOperationException("Built-in floating ball must not use a color-key transparency fringe.");
                    if (form.Region == null)
                        throw new InvalidOperationException("Built-in floating ball window shape is missing.");

                    form.CreateControl();
                    using (var bitmap = new Bitmap(form.Width, form.Height))
                    {
                        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                        var bluePixels = 0;
                        var magentaPixels = 0;
                        for (var y = 0; y < bitmap.Height; y += 2)
                        {
                            for (var x = 0; x < bitmap.Width; x += 2)
                            {
                                var pixel = bitmap.GetPixel(x, y);
                                if (pixel.B > 75 && pixel.B > pixel.R + 18 && pixel.B > pixel.G + 5) bluePixels++;
                                if (pixel.R > 170 && pixel.B > 170 && pixel.G < 95) magentaPixels++;
                            }
                        }
                        if (bluePixels < 220)
                            throw new InvalidOperationException("Built-in floating ball did not produce enough visible blue 3D content.");
                        if (magentaPixels > 8)
                            throw new InvalidOperationException("Built-in floating ball still contains a magenta fringe.");
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
