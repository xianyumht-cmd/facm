using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace FACM.Pets
{
    internal static class AnimalPetSmokeTest
    {
        public static int Run()
        {
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                if (AnimalPetCatalog.All.Count < 8)
                    throw new InvalidOperationException("Built-in animal pet catalog is unexpectedly small.");

                var signatures = new HashSet<int>();
                foreach (var pet in AnimalPetCatalog.All)
                {
                    using (var bitmap = AnimalPetWindow.RenderForSmokeTest(pet, 1.17f, true))
                    {
                        if (bitmap.Width < 96 || bitmap.Height < 96)
                            throw new InvalidOperationException("Animal pet render is unexpectedly small: " + pet.Id);
                        if (bitmap.GetPixel(0, 0).A > 3 ||
                            bitmap.GetPixel(bitmap.Width - 1, 0).A > 3 ||
                            bitmap.GetPixel(0, bitmap.Height - 1).A > 3 ||
                            bitmap.GetPixel(bitmap.Width - 1, bitmap.Height - 1).A > 3)
                            throw new InvalidOperationException("Animal pet corners are not transparent: " + pet.Id);

                        var visible = 0;
                        var alphaEdges = 0;
                        var signature = 17;
                        for (var y = 0; y < bitmap.Height; y += 2)
                        {
                            for (var x = 0; x < bitmap.Width; x += 2)
                            {
                                var pixel = bitmap.GetPixel(x, y);
                                if (pixel.A > 80) visible++;
                                if (pixel.A > 5 && pixel.A < 220) alphaEdges++;
                                if (pixel.A > 100)
                                    signature = unchecked(signature * 31 + pixel.ToArgb());
                            }
                        }
                        if (visible < 300)
                            throw new InvalidOperationException("Animal pet render contains too little visible content: " + pet.Id);
                        if (alphaEdges < 20)
                            throw new InvalidOperationException("Animal pet render has no anti-aliased transparent edge: " + pet.Id);
                        signatures.Add(signature);
                    }
                }

                if (signatures.Count < AnimalPetCatalog.All.Count - 1)
                    throw new InvalidOperationException("Animal pet renders are not visually distinct enough.");

                using (var window = new AnimalPetWindow(AnimalPetCatalog.Get("cat")))
                {
                    window.ResetToPrimaryScreen();
                    var area = Screen.PrimaryScreen.WorkingArea;
                    var center = new Point(window.Left + window.Width / 2, window.Top + window.Height / 2);
                    if (!area.Contains(center))
                        throw new InvalidOperationException("Animal pet reset did not return the pet to the primary screen.");
                    window.Close();
                }
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 8;
            }
        }
    }
}
