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

                if (AnimalPetCatalog.All.Count < 14)
                    throw new InvalidOperationException("Pet catalog is unexpectedly small.");
                if (AnimalPetCatalog.Get("vpet").Runtime != AnimalPetRuntime.VPetCore)
                    throw new InvalidOperationException("VPet Core runtime is missing from the pet catalog.");

                ValidateVisibleCatalog();
                ValidatePickerPresentation();
                ValidateHighDetailGreenFly();
                ValidateRealBeeGate();
                ValidateFlyingProfilesAndHeading();
                ValidateFlyingPolishProfiles();

                var signatures = new HashSet<int>();
                var spriteCount = 0;
                using (var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(55)))
                {
                    foreach (var pet in AnimalPetCatalog.All)
                    {
                        if (pet.Runtime != AnimalPetRuntime.Sprite) continue;
                        spriteCount++;
                        if (!string.Equals(pet.AssetLicense, "CC0", StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException("Non-CC0 asset entered the Sprite catalog: " + pet.Id);
                        if (pet.FrameCount < 2)
                            throw new InvalidOperationException("Sprite pet is not truly animated: " + pet.Id);

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

                            if (FlyingPetProfiles.IsManaged(pet))
                            {
                                var source = SpritePetAssetService.GetFrameRectangle(pet, sheet, 0, 0);
                                if (source.Width < 96 || source.Height < 96)
                                    throw new InvalidOperationException("Managed flying source is below the high-detail floor: " + pet.Id);
                                if (pet.DirectionalRows || pet.PixelArt)
                                    throw new InvalidOperationException("Managed flying pet regressed to directional/pixel-art rendering: " + pet.Id);
                            }
                        }
                    }
                }

                if (spriteCount < 13)
                    throw new InvalidOperationException("Sprite compatibility catalog dropped unexpectedly.");
                if (signatures.Count < spriteCount * 2 - 2)
                    throw new InvalidOperationException("Animated sprite renders are not visually distinct enough.");

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
                    throw new InvalidOperationException("Legacy eight-direction mapping is not changing with movement direction.");

                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 8;
            }
        }

        private static void ValidateVisibleCatalog()
        {
            if (!string.Equals(AnimalPetCatalog.DefaultPetId, "greenfly", StringComparison.Ordinal))
                throw new InvalidOperationException("Default pet fallback is no longer the accepted lightweight flying baseline.");
            if (AnimalPetCatalog.Visible.Count != 7)
                throw new InvalidOperationException("Desktop-pet picker must expose six managed flying pets plus VPet Core.");

            var managedCount = 0;
            var vpetCount = 0;
            foreach (var pet in AnimalPetCatalog.Visible)
            {
                if (pet.Runtime == AnimalPetRuntime.VPetCore)
                {
                    vpetCount++;
                    continue;
                }
                if (!FlyingPetProfiles.IsManaged(pet))
                    throw new InvalidOperationException("Legacy/non-managed Sprite leaked back into the primary picker: " + pet.Id);
                managedCount++;
            }
            if (managedCount != 6 || vpetCount != 1)
                throw new InvalidOperationException("Primary picker composition changed unexpectedly.");

            if (!AnimalPetCatalog.Contains("spider") || !AnimalPetCatalog.Contains("cat") || !AnimalPetCatalog.Contains("dog"))
                throw new InvalidOperationException("Legacy pet IDs must remain resolvable for existing settings.ini files.");
        }

        private static void ValidatePickerPresentation()
        {
            var summaries = new HashSet<string>(StringComparer.Ordinal);
            var behaviors = new HashSet<string>(StringComparer.Ordinal);
            foreach (var pet in AnimalPetCatalog.Visible)
            {
                var summary = AnimalPetPickerForm.SummaryForSmokeTest(pet);
                var behavior = AnimalPetPickerForm.BehaviorForSmokeTest(pet);
                var badge = AnimalPetPickerForm.RuntimeBadgeForSmokeTest(pet);
                if (string.IsNullOrWhiteSpace(summary) || string.IsNullOrWhiteSpace(behavior) || string.IsNullOrWhiteSpace(badge))
                    throw new InvalidOperationException("Pet picker presentation is incomplete: " + pet.Id);
                if (summary.IndexOf("Runtime", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    summary.IndexOf("CC0", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    behavior.IndexOf("Runtime", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    behavior.IndexOf("CC0", StringComparison.OrdinalIgnoreCase) >= 0)
                    throw new InvalidOperationException("Implementation/licensing jargon leaked into primary picker copy: " + pet.Id);
                summaries.Add(summary);
                behaviors.Add(behavior);

                if (pet.Runtime == AnimalPetRuntime.VPetCore)
                {
                    if (badge.IndexOf("高精度", StringComparison.Ordinal) < 0)
                        throw new InvalidOperationException("VPet picker badge lost the high-detail distinction.");
                }
                else if (badge.IndexOf("轻量", StringComparison.Ordinal) < 0 || badge.IndexOf("自主飞行", StringComparison.Ordinal) < 0)
                {
                    throw new InvalidOperationException("Managed flying picker badge lost its lightweight autonomous distinction: " + pet.Id);
                }
            }

            if (summaries.Count != AnimalPetCatalog.Visible.Count || behaviors.Count != AnimalPetCatalog.Visible.Count)
                throw new InvalidOperationException("Pet picker personalities are no longer visually distinct in copy.");
        }

        private static void ValidateHighDetailGreenFly()
        {
            var fly = AnimalPetCatalog.Get("greenfly");
            if (fly.Runtime != AnimalPetRuntime.Sprite || fly.Motion != AnimalMotionStyle.Fly)
                throw new InvalidOperationException("Green fly no longer uses the lightweight Sprite/Fly runtime.");
            if (!string.Equals(fly.SpriteUrl, SpritePetAssetService.BuiltInGreenFlyUrl, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Green fly is no longer using the accepted built-in high-detail asset.");
            if (fly.SpriteColumns != SpritePetAssetService.BuiltInGreenFlyFrameCount || fly.SpriteRows != 1 ||
                fly.FrameCount != SpritePetAssetService.BuiltInGreenFlyFrameCount)
                throw new InvalidOperationException("Green fly high-detail sprite grid changed unexpectedly.");
            if (fly.PixelArt)
                throw new InvalidOperationException("Green fly high-detail sprite must not use nearest-neighbor pixel-art scaling.");
            if (Math.Abs(fly.Speed - 1.36f) > 0.001f || Math.Abs(fly.VisualScale - 0.56f) > 0.001f)
                throw new InvalidOperationException("Green fly accepted movement/size profile changed while refactoring the runtime.");
            if (!string.Equals(fly.FlyingProfileId, FlyingPetProfiles.GreenFly, StringComparison.Ordinal))
                throw new InvalidOperationException("Green fly is not attached to the managed flight profile.");

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

                using (var right = SpritePetWindow.RenderFlyingForSmokeTest(fly, sheet, 0, 0f))
                using (var down = SpritePetWindow.RenderFlyingForSmokeTest(fly, sheet, 0, 90f))
                {
                    if (Signature(right) == Signature(down))
                        throw new InvalidOperationException("Managed flight heading rotation is not affecting the rendered body.");
                }
            }
        }

        private static void ValidateRealBeeGate()
        {
            var realBee = AnimalPetCatalog.Get("real-bee");
            if (realBee.Runtime != AnimalPetRuntime.Sprite || realBee.Motion != AnimalMotionStyle.Fly || !realBee.ShowInPicker)
                throw new InvalidOperationException("Real Bee Gate 1 is not exposed as a managed flying pet.");
            if (!string.Equals(realBee.SpriteUrl, SpritePetAssetService.BuiltInRealBeeUrl, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Real Bee Gate 1 lost its embedded photo-real asset route.");
            if (realBee.SpriteColumns != SpritePetAssetService.BuiltInRealBeeFrameCount || realBee.SpriteRows != 1 ||
                realBee.FrameCount != SpritePetAssetService.BuiltInRealBeeFrameCount)
                throw new InvalidOperationException("Real Bee Gate 1 sprite grid changed unexpectedly.");
            if (realBee.PixelArt || realBee.DirectionalRows)
                throw new InvalidOperationException("Real Bee Gate 1 must use high-quality natural-pose rendering.");
            if (Math.Abs(realBee.Speed - 1.00f) > 0.001f || Math.Abs(realBee.VisualScale - 0.55f) > 0.001f)
                throw new InvalidOperationException("Real Bee Gate 1 visual size or inherited speed changed unexpectedly.");
            if (!string.Equals(realBee.FlyingProfileId, FlyingPetProfiles.Bee, StringComparison.Ordinal))
                throw new InvalidOperationException("Real Bee Gate 1 must reuse the accepted bee trajectory while pose rendering evolves independently.");
            if (!SpritePetWindow.UsesNaturalFlightPose(realBee))
                throw new InvalidOperationException("Real Bee Gate 1 lost its natural flight-pose presentation route.");

            AssertNear(SpritePetWindow.NaturalFlightPitchForVector(100f, 0f), 0f, "real-bee level pitch");
            var steepDown = SpritePetWindow.NaturalFlightPitchForVector(0f, 100f);
            var steepUp = SpritePetWindow.NaturalFlightPitchForVector(0f, -100f);
            if (steepDown < 25f || steepDown > 32.01f || steepUp > -25f || steepUp < -32.01f)
                throw new InvalidOperationException("Real Bee natural pitch is no longer bounded near horizontal flight attitude.");
            if (SpritePetWindow.NaturalFlightFacingRightForVector(-100f, 8f, true))
                throw new InvalidOperationException("Real Bee strong leftward travel no longer mirrors the body left.");
            if (!SpritePetWindow.NaturalFlightFacingRightForVector(100f, -8f, false))
                throw new InvalidOperationException("Real Bee strong rightward travel no longer mirrors the body right.");
            if (!SpritePetWindow.NaturalFlightFacingRightForVector(2f, 100f, true) ||
                SpritePetWindow.NaturalFlightFacingRightForVector(2f, 100f, false))
                throw new InvalidOperationException("Real Bee near-vertical travel no longer preserves facing and may chatter between mirrored states.");

            using (var sheet = SpritePetAssetService.CreateBuiltInRealBeeSheetForSmokeTest())
            {
                var expectedWidth = SpritePetAssetService.BuiltInRealBeeFrameSize * SpritePetAssetService.BuiltInRealBeeFrameCount;
                if (sheet.Width != expectedWidth || sheet.Height != SpritePetAssetService.BuiltInRealBeeFrameSize)
                    throw new InvalidOperationException("Real Bee Gate 1 embedded sheet dimensions are invalid.");

                using (var right = SpritePetWindow.RenderNaturalFlyingForSmokeTest(realBee, sheet, 0, 0f, true))
                using (var left = SpritePetWindow.RenderNaturalFlyingForSmokeTest(realBee, sheet, 0, 0f, false))
                using (var climb = SpritePetWindow.RenderNaturalFlyingForSmokeTest(realBee, sheet, 0, -32f, true))
                using (var descend = SpritePetWindow.RenderNaturalFlyingForSmokeTest(realBee, sheet, 0, 32f, true))
                {
                    ValidateTransparentRender(realBee, right);
                    ValidateTransparentRender(realBee, left);
                    ValidateTransparentRender(realBee, climb);
                    ValidateTransparentRender(realBee, descend);
                    if (Signature(right) == Signature(left) || Signature(right) == Signature(climb) || Signature(climb) == Signature(descend))
                        throw new InvalidOperationException("Real Bee natural facing/pitch poses are not visually distinct.");
                }
            }
        }

        private static void ValidateFlyingProfilesAndHeading()
        {
            var green = FlyingPetProfiles.Get(FlyingPetProfiles.GreenFly);
            if (green == null)
                throw new InvalidOperationException("Green fly motion profile is missing.");
            if (Math.Abs(green.MinBaseSpeed - 82f) > 0.001f || Math.Abs(green.MaxBaseSpeed - 140f) > 0.001f ||
                Math.Abs(green.MoveMinSeconds - 0.55) > 0.0001 || Math.Abs(green.MoveMaxSeconds - 1.80) > 0.0001 ||
                Math.Abs(green.IdleChance - 0.02) > 0.0001 || Math.Abs(green.VelocityResponse - 7.5f) > 0.001f ||
                Math.Abs(green.JitterXAmplitude - 10f) > 0.001f || Math.Abs(green.JitterYAmplitude - 8f) > 0.001f ||
                Math.Abs(green.JitterXFrequency - 17f) > 0.001f || Math.Abs(green.JitterYFrequency - 13f) > 0.001f)
                throw new InvalidOperationException("Green fly trajectory baseline changed during Flying Runtime refactor.");

            if (FlyingPetProfiles.Get(FlyingPetProfiles.Bee) == null ||
                FlyingPetProfiles.Get(FlyingPetProfiles.Dragonfly) == null ||
                FlyingPetProfiles.Get(FlyingPetProfiles.Butterfly) == null ||
                FlyingPetProfiles.Get(FlyingPetProfiles.Moth) == null)
                throw new InvalidOperationException("One or more managed flying profiles are missing.");

            AssertNear(SpritePetWindow.HeadingDegreesForVector(1f, 0f), 0f, "right heading");
            AssertNear(SpritePetWindow.HeadingDegreesForVector(0f, 1f), 90f, "down heading");
            AssertNear(SpritePetWindow.HeadingDegreesForVector(-1f, 0f), 180f, "left heading");
            AssertNear(SpritePetWindow.HeadingDegreesForVector(0f, -1f), 270f, "up heading");
            AssertNear(SpritePetWindow.ShortestAngleDelta(350f, 10f), 20f, "wrap-positive turn");
            AssertNear(SpritePetWindow.ShortestAngleDelta(10f, 350f), -20f, "wrap-negative turn");
        }

        private static void ValidateFlyingPolishProfiles()
        {
            var bee = FlyingPetProfiles.Get(FlyingPetProfiles.Bee);
            var dragonfly = FlyingPetProfiles.Get(FlyingPetProfiles.Dragonfly);
            var butterfly = FlyingPetProfiles.Get(FlyingPetProfiles.Butterfly);
            var moth = FlyingPetProfiles.Get(FlyingPetProfiles.Moth);

            if (bee.IdleChance < 0.15 || bee.VelocityResponse >= 5.0f)
                throw new InvalidOperationException("Bee profile lost its hover/gentle-response character.");
            if (dragonfly.MinBaseSpeed < 110f || dragonfly.MaxBaseSpeed < 190f ||
                dragonfly.JitterXAmplitude > 1.0f || dragonfly.JitterYAmplitude > 1.0f ||
                dragonfly.MoveMinSeconds < 2.0)
                throw new InvalidOperationException("Dragonfly profile no longer reads as long straight high-speed dashes.");
            if (butterfly.MaxBaseSpeed > 42f || butterfly.HeadingResponse > 2.8f ||
                butterfly.JitterYAmplitude < 12f || butterfly.MoveMinSeconds < 2.5)
                throw new InvalidOperationException("Butterfly profile lost its slow floating arc character.");
            if (moth.MoveMaxSeconds > 1.7 || moth.HeadingResponse < 8f ||
                Math.Abs(moth.JitterXFrequency - moth.JitterYFrequency) > 0.001f ||
                Math.Abs(moth.JitterXAmplitude - moth.JitterYAmplitude) > 0.001f)
                throw new InvalidOperationException("Moth profile lost its short looping wander character.");

            using (var beeSheet = BuiltInFlyingPetArtService.TryCreate(BuiltInFlyingPetArtService.BeeUrl))
            using (var dragonflySheet = BuiltInFlyingPetArtService.TryCreate(BuiltInFlyingPetArtService.DragonflyUrl))
            using (var butterflySheet = BuiltInFlyingPetArtService.TryCreate(BuiltInFlyingPetArtService.ButterflyUrl))
            using (var mothSheet = BuiltInFlyingPetArtService.TryCreate(BuiltInFlyingPetArtService.MothUrl))
            {
                if (beeSheet == null || beeSheet.Height != BuiltInFlyingPetArtService.BeeFrameSize)
                    throw new InvalidOperationException("Bee polished source size is invalid.");
                if (dragonflySheet == null || dragonflySheet.Height != BuiltInFlyingPetArtService.DragonflyFrameSize)
                    throw new InvalidOperationException("Dragonfly polished source size is invalid.");
                if (butterflySheet == null || butterflySheet.Height != BuiltInFlyingPetArtService.ButterflyFrameSize)
                    throw new InvalidOperationException("Butterfly polished source size is invalid.");
                if (mothSheet == null || mothSheet.Height != BuiltInFlyingPetArtService.MothFrameSize)
                    throw new InvalidOperationException("Moth polished source size is invalid.");
            }
        }

        private static void AssertNear(float actual, float expected, string label)
        {
            if (Math.Abs(actual - expected) > 0.01f)
                throw new InvalidOperationException(label + " mismatch: " + actual + " != " + expected);
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
