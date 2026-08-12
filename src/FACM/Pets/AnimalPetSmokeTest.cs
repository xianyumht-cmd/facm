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

                if (AnimalPetCatalog.All.Count < 9)
                    throw new InvalidOperationException("Pet catalog is unexpectedly small.");
                if (AnimalPetCatalog.Get("vpet").Runtime != AnimalPetRuntime.VPetCore)
                    throw new InvalidOperationException("VPet Core runtime is missing from the pet catalog.");

                ValidateHighDetailGreenFly();

                var signatures = new HashSet<int>();
                var spriteCount = 0;
                using (var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(55)))
                {
                    foreach (var pet in AnimalPetCatalog.All)
                    {
                        if (pet.Runtime != AnimalPetRuntime.Sprite) continue;
                        spriteCount++;
                        if (!string.Equals(pet.AssetLicense, "CC0", StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException("Non-CC0 asset entered the legacy sprite fallback catalog: " + pet.Id);
                        if (pet.FrameCount < 2)
                            throw new InvalidOperationException("Legacy sprite fallback is not truly animated: " + pet.Id);

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

                if (spriteCount < 8)
                    throw new InvalidOperationException("Legacy sprite fallback count dropped unexpectedly.");
                if (signatures.Count < spriteCount * 2 - 2)
                    throw new InvalidOperationException("Animated fallback renders are not visually distinct enough.");

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

        private static void ValidateHighDetailGreenFly()
        {
            var fly = AnimalPetCatalog.Get("greenfly");
            if (fly.Runtime != AnimalPetRuntime.Sprite || fly.Motion != AnimalMotionStyle.Fly)
                throw new InvalidOperationException("Green fly no longer uses the lightweight Sprite/Fly runtime.");
            if (!string.Equals(fly.SpriteUrl, SpritePetAssetService.BuiltInGreenFlyUrl, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Green fly is no longer using the built-in high-detail asset.");
            if (fly.SpriteColumns != SpritePetAssetService.BuiltInGreenFlyFrameCount || fly.SpriteRows != 1 ||
                fly.FrameCount != SpritePetAssetService.BuiltInGreenFlyFrameCount)
                throw new InvalidOperationException("Green fly high-detail sprite grid changed unexpectedly.");
            if (fly.PixelArt)
                throw new InvalidOperationException("Green fly high-detail sprite must not use nearest-neighbor pixel-art scaling.");
            if (Math.Abs(fly.Speed - 1.36f) > 0.001f || Math.Abs(fly.VisualScale - 0.56f) > 0.001f)
                throw new InvalidOperationException("Green fly accepted movement/size profile changed while upgrading artwork.");

            using (var sheet = SpritePetAssetService.CreateBuiltInGreenFlySheetForSmokeTest())
            {
                var expectedWidth = SpritePetAssetService.BuiltInGreenFlyFrameSize * SpritePetAssetService.BuiltInGreenFlyFrameCount;
                if (sheet.Width != expectedWidth || sheet.Height != SpritePetAssetService.BuiltInGreenFlyFrameSize)
                    throw new InvalidOperationException("Green fly built-in sheet is not the expected 96px-per-frame source.");

                for (var frame = 0; frame < SpritePetAssetService.BuiltInGreenFlyFrameCount; frame++)
                {
                    var rectangle = SpritePetAssetService.GetFrameRectangle(fly, sheet, frame, 0);
                    if (rectangle.Width != SpritePetAssetService.BuiltInGreenFlyFrameSize ||
                        rectangle.Height != SpritePetAssetService.BuiltInGreenFlyFrameSize)
                        throw new InvalidOperationException("Green fly source frame was accidentally downscaled.");
                }
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
