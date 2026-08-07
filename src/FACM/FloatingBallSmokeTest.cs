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
                    if (form.TransparencyKey != Color.Magenta)
                        throw new InvalidOperationException("Built-in floating ball transparency key is invalid.");

                    form.CreateControl();
                    using (var bitmap = new Bitmap(form.Width, form.Height))
                    {
                        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                        var visiblePixels = 0;
                        for (var y = 0; y < bitmap.Height; y += 2)
                        {
                            for (var x = 0; x < bitmap.Width; x += 2)
                            {
                                var pixel = bitmap.GetPixel(x, y);
                                if (pixel.ToArgb() != Color.Magenta.ToArgb() && pixel.A > 0) visiblePixels++;
                            }
                        }
                        if (visiblePixels < 350)
                            throw new InvalidOperationException("Built-in floating ball did not produce a visible rendered frame.");
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
