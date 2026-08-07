using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
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
                    throw new InvalidOperationException("Animated pet catalog is unexpectedly small.");

                var signatures = new HashSet<int>();
                using (var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(55)))
                {
                    foreach (var pet in AnimalPetCatalog.All)
                    {
                        if (!string.Equals(pet.AssetLicense, "CC0", StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException("Non-CC0 pet asset entered the built-in catalog: " + pet.Id);
                        if (pet.FrameCount < 2)
                            throw new InvalidOperationException("Built-in pet is not truly animated: " + pet.Id);

                        using (var sheet = SpritePetAssetService.LoadAsync(pet, cancellation.Token).GetAwaiter().GetResult())
                        {
                            if (sheet == null)
                                throw new InvalidOperationException("Animated sprite sheet could not be loaded: " + pet.Id);
                            if (sheet.Width % Math.Max(1, pet.SpriteColumns) != 0 || sheet.Height % Math.Max(1, pet.SpriteRows) != 0)
                                throw new InvalidOperationException("Sprite sheet grid is invalid: " + pet.Id);

                            using (var frame0 = SpritePetWindow.RenderForSmokeTest(pet, sheet, 0, 0, true))
                            using (var frame1 = SpritePetWindow.RenderForSmokeTest(pet, sheet, 1, 0, true))
                            {
                                ValidateTransparentRender(pet, frame0);
                                ValidateTransparentRender(pet, frame1);
                                var first = Signature(frame0);
                                var second = Signature(frame1);
                                if (first == second)
                                    throw new InvalidOperationException("Pet animation frames render identically: " + pet.Id);
                                signatures.Add(first);
                                signatures.Add(second);
                            }

                            if (pet.DirectionalRows)
                            {
                                using (var direction0 = SpritePetWindow.RenderForSmokeTest(pet, sheet, 2, 0, true))
                                using (var direction1 = SpritePetWindow.RenderForSmokeTest(pet, sheet, 2, 1, true))
                                {
                                    if (Signature(direction0) == Signature(direction1))
                                        throw new InvalidOperationException("Directional pet rows render identically: " + pet.Id);
                                }
                            }
                        }
                    }
                }

                if (signatures.Count < AnimalPetCatalog.All.Count * 2 - 2)
                    throw new InvalidOperationException("Animated pet renders are not visually distinct enough.");

                using (var window = new SpritePetWindow(AnimalPetCatalog.Get("spider")))
                {
                    window.ResetToPrimaryScreen();
                    var area = Screen.PrimaryScreen.WorkingArea;
                    var center = new Point(window.Left + window.Width / 2, window.Top + window.Height / 2);
                    if (!area.Contains(center))
                        throw new InvalidOperationException("Pet reset did not return the pet to the primary screen.");
                    window.Close();
                }

                if (SpritePetWindow.DirectionRowForVector(1f, 0f) == SpritePetWindow.DirectionRowForVector(0f, 1f))
                    throw new InvalidOperationException("Eight-direction pet mapping is not changing with movement direction.");

                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 8;
            }
        }

        private static void ValidateTransparentRender(AnimalPetDefinition pet, Bitmap bitmap)
        {
            if (bitmap == null || bitmap.Width < 120 || bitmap.Height < 120)
                throw new InvalidOperationException("Pet render is unexpectedly small: " + pet.Id);
            if (bitmap.GetPixel(0, 0).A > 3 ||
                bitmap.GetPixel(bitmap.Width - 1, 0).A > 3 ||
                bitmap.GetPixel(0, bitmap.Height - 1).A > 3 ||
                bitmap.GetPixel(bitmap.Width - 1, bitmap.Height - 1).A > 3)
                throw new InvalidOperationException("Pet render corners are not transparent: " + pet.Id);

            var visible = 0;
            for (var y = 0; y < bitmap.Height; y += 2)
            {
                for (var x = 0; x < bitmap.Width; x += 2)
                {
                    if (bitmap.GetPixel(x, y).A > 70) visible++;
                }
            }
            if (visible < 110)
                throw new InvalidOperationException("Pet render contains too little visible content: " + pet.Id);
        }

        private static int Signature(Bitmap bitmap)
        {
            var signature = 17;
            for (var y = 0; y < bitmap.Height; y += 3)
            {
                for (var x = 0; x < bitmap.Width; x += 3)
                {
                    var pixel = bitmap.GetPixel(x, y);
                    if (pixel.A > 20)
                        signature = unchecked(signature * 31 + pixel.ToArgb());
                }
            }
            return signature;
        }
    }
}
