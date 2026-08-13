using System;
using System.Collections.Generic;

namespace FACM.Pets
{
    internal enum AnimalMotionStyle
    {
        Walk,
        Hop,
        Crawl,
        Fly,
        Waddle
    }

    internal enum AnimalPetRuntime
    {
        Sprite = 0,
        VPetCore = 1
    }

    internal sealed class AnimalPetDefinition
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public AnimalMotionStyle Motion { get; set; }
        public AnimalPetRuntime Runtime { get; set; }

        // Legacy artwork fields are retained so older fallback code still compiles.
        public string ArtworkUrl { get; set; }
        public string ArtworkFileName { get; set; }

        public string SpriteUrl { get; set; }
        public string SpriteFileName { get; set; }
        public int SpriteColumns { get; set; }
        public int SpriteRows { get; set; }
        public int AnimationRow { get; set; }
        public int FrameCount { get; set; }
        public float FramesPerSecond { get; set; }
        public bool DirectionalRows { get; set; }
        public bool PixelArt { get; set; }
        public string SourcePage { get; set; }
        public string AssetAuthor { get; set; }
        public string AssetLicense { get; set; }

        public float Speed { get; set; }
        public float VisualScale { get; set; }
        public string FlyingProfileId { get; set; }
        public bool ShowInPicker { get; set; } = true;
    }

    internal static class AnimalPetCatalog
    {
        // Invalid/old pet IDs now fail soft to the lightweight flying baseline. New installs still do not
        // start a pet until the user explicitly enables the desktop-pet mode.
        public const string DefaultPetId = "greenfly";

        private static readonly IReadOnlyList<AnimalPetDefinition> Pets = new List<AnimalPetDefinition>
        {
            FlyingPet(
                "greenfly", "绿苍蝇", "高速、小范围急转；沿用已实机验收的 FACM 飞行轨迹，升级为 360° 平滑朝向。",
                SpritePetAssetService.BuiltInGreenFlyUrl, "greenfly_hq_v1.generated",
                22f, 1.36f, 0.56f, FlyingPetProfiles.GreenFly),

            FlyingPet(
                "bee", "蜜蜂", "中速巡航，转向更圆滑，会有短暂停悬；使用统一 Flying Runtime。",
                BuiltInFlyingPetArtService.BeeUrl, "bee_hq_v1.generated",
                18f, 1.00f, 0.62f, FlyingPetProfiles.Bee),

            RealBeePet(),

            FlyingPet(
                "dragonfly", "蜻蜓", "长距离快速冲刺、急停和快速改向；使用统一 Flying Runtime。",
                BuiltInFlyingPetArtService.DragonflyUrl, "dragonfly_hq_v1.generated",
                24f, 1.00f, 0.72f, FlyingPetProfiles.Dragonfly),

            FlyingPet(
                "butterfly", "蝴蝶", "慢速大曲线飞行，带明显上下漂浮和低频大幅振翅。",
                BuiltInFlyingPetArtService.ButterflyUrl, "butterfly_hq_v1.generated",
                8f, 1.00f, 0.74f, FlyingPetProfiles.Butterfly),

            FlyingPet(
                "moth", "飞蛾", "短距离随机游走、频繁改向，轨迹比蝴蝶更紧凑。",
                BuiltInFlyingPetArtService.MothUrl, "moth_hq_v1.generated",
                11f, 1.00f, 0.68f, FlyingPetProfiles.Moth),

            VPetPet(),

            LegacyPet(
                "cat", "猫咪（兼容）", "旧版 5 帧 Sprite 跑动；已有配置继续可用，新选择器不再推荐。", AnimalMotionStyle.Walk,
                "https://opengameart.org/sites/default/files/cat_run.png", "cat_run.png",
                5, 1, 0, 5, 12f, false, true, 1.00f, 0.82f,
                "https://opengameart.org/content/pixel-cat-0", "alizard", "CC0"),

            LegacyPet(
                "dog", "狗狗（兼容）", "旧版 6 帧 Sprite 走路循环；已有配置继续可用，新选择器不再推荐。", AnimalMotionStyle.Walk,
                "https://opengameart.org/sites/default/files/dog_medium.png", "dog_medium.png",
                6, 6, 1, 6, 11f, false, true, 1.03f, 0.86f,
                "https://opengameart.org/content/dog-3", "rmazanek / Shepardskin / Hellkipz", "CC0"),

            LegacyPet(
                "spider", "蜘蛛（兼容）", "旧 8 方向、13 帧步态；已有配置继续可用，新选择器不再推荐。", AnimalMotionStyle.Crawl,
                "https://opengameart.org/sites/default/files/sprite_sheet_3.png", "iso_spider_8x13.png",
                13, 8, 0, 13, 15f, true, false, 0.88f, 0.86f,
                "https://opengameart.org/content/iso-spider-spritesheet", "KillGorack", "CC0"),

            LegacyPet(
                "ant", "蚂蚁（兼容）", "旧多方向 Sprite；已有配置继续可用，新选择器不再推荐。", AnimalMotionStyle.Crawl,
                "https://opengameart.org/sites/default/files/walk_5.png", "walking_ant.png",
                8, 8, 0, 8, 14f, true, false, 0.82f, 0.72f,
                "https://opengameart.org/content/walking-ant-with-parts-and-rigged-spriter-file", "DudeMan", "CC0"),

            LegacyPet(
                "greyfly", "灰苍蝇（兼容）", "旧 16px 三帧 Sprite；已有配置继续可用，新选择器不再推荐。", AnimalMotionStyle.Fly,
                "https://opengameart.org/sites/default/files/greyfly_spritesheet.png", "greyfly_spritesheet.png",
                3, 1, 0, 3, 22f, false, true, 1.38f, 0.56f,
                "https://opengameart.org/content/16x16-flies", "ARoachIFoundOnMyPillow", "CC0"),

            LegacyPet(
                "wasp", "胡蜂（兼容）", "旧双帧 Sprite；已有配置继续可用，新选择器不再推荐。", AnimalMotionStyle.Fly,
                "https://opengameart.org/sites/default/files/spr_wasp_flying_strip_2.png", "wasp_flying.png",
                2, 1, 0, 2, 18f, false, true, 1.28f, 0.66f,
                "https://opengameart.org/content/flying-hornetwasp", "Nerveona", "CC0"),

            LegacyPet(
                "bird", "小鸟（兼容）", "旧 Sprite 飞行行；已有配置继续可用，新选择器不再推荐。", AnimalMotionStyle.Fly,
                "https://opengameart.org/sites/default/files/bird_v001_blue_and_yellow.png", "bird_blue_yellow.png",
                11, 8, 6, 11, 15f, false, true, 1.18f, 0.70f,
                "https://opengameart.org/content/bird-2", "rmazanek", "CC0")
        };

        private static readonly IReadOnlyList<AnimalPetDefinition> PickerPets = BuildPickerPets();

        public static IReadOnlyList<AnimalPetDefinition> All
        {
            get { return Pets; }
        }

        public static IReadOnlyList<AnimalPetDefinition> Visible
        {
            get { return PickerPets; }
        }

        public static AnimalPetDefinition Get(string id)
        {
            foreach (var pet in Pets)
            {
                if (string.Equals(pet.Id, id, StringComparison.OrdinalIgnoreCase)) return pet;
            }
            foreach (var pet in Pets)
            {
                if (string.Equals(pet.Id, DefaultPetId, StringComparison.OrdinalIgnoreCase)) return pet;
            }
            return Pets[0];
        }

        public static bool Contains(string id)
        {
            foreach (var pet in Pets)
            {
                if (string.Equals(pet.Id, id, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static IReadOnlyList<AnimalPetDefinition> BuildPickerPets()
        {
            var visible = new List<AnimalPetDefinition>();
            foreach (var pet in Pets)
            {
                if (pet.ShowInPicker) visible.Add(pet);
            }
            return visible;
        }

        private static AnimalPetDefinition FlyingPet(
            string id,
            string name,
            string description,
            string spriteUrl,
            string spriteFileName,
            float fps,
            float speed,
            float visualScale,
            string profileId)
        {
            return new AnimalPetDefinition
            {
                Id = id,
                Name = name,
                Description = description,
                Motion = AnimalMotionStyle.Fly,
                Runtime = AnimalPetRuntime.Sprite,
                SpriteUrl = spriteUrl,
                SpriteFileName = spriteFileName,
                SpriteColumns = 4,
                SpriteRows = 1,
                AnimationRow = 0,
                FrameCount = 4,
                FramesPerSecond = fps,
                DirectionalRows = false,
                PixelArt = false,
                SourcePage = "https://github.com/xianyumht-cmd/facm/issues/45",
                AssetAuthor = "FACM project",
                AssetLicense = "CC0",
                Speed = speed,
                VisualScale = visualScale,
                FlyingProfileId = profileId,
                ShowInPicker = true,
                ArtworkUrl = spriteUrl,
                ArtworkFileName = spriteFileName
            };
        }

        private static AnimalPetDefinition RealBeePet()
        {
            return new AnimalPetDefinition
            {
                Id = "real-bee",
                Name = "真实蜜蜂",
                Description = "写真级真实蜜蜂：小尺寸、透明翅膀与自然转向，适合更接近实物的桌面风格。",
                Motion = AnimalMotionStyle.Fly,
                Runtime = AnimalPetRuntime.Sprite,
                SpriteUrl = SpritePetAssetService.BuiltInRealBeeUrl,
                SpriteFileName = "real_bee_gate1_v1.generated",
                SpriteColumns = SpritePetAssetService.BuiltInRealBeeFrameCount,
                SpriteRows = 1,
                AnimationRow = 0,
                FrameCount = SpritePetAssetService.BuiltInRealBeeFrameCount,
                FramesPerSecond = 18f,
                DirectionalRows = false,
                PixelArt = false,
                SourcePage = "https://github.com/xianyumht-cmd/facm/issues/68",
                AssetAuthor = "FACM project / OpenAI generated asset",
                AssetLicense = "CC0",
                Speed = 1.00f,
                VisualScale = 0.55f,
                // Gate 1 deliberately reuses the accepted bee trajectory. Visual quality is evaluated first.
                FlyingProfileId = FlyingPetProfiles.Bee,
                ShowInPicker = true,
                ArtworkUrl = SpritePetAssetService.BuiltInRealBeeUrl,
                ArtworkFileName = "real_bee_gate1_v1.generated"
            };
        }

        private static AnimalPetDefinition VPetPet()
        {
            return new AnimalPetDefinition
            {
                Id = "vpet",
                Name = "高精度桌宠 · VPet Core",
                Description = "高精度独立运行层：成熟 Idle / Move / Raised / Touch 状态。首次启用会按需缓存官方动作。",
                Motion = AnimalMotionStyle.Walk,
                Runtime = AnimalPetRuntime.VPetCore,
                SourcePage = "https://github.com/LorisYounger/VPet",
                AssetAuthor = "VUP-Simulator team / VPet",
                AssetLicense = "VPet 非商用动画授权",
                VisualScale = 1f,
                ShowInPicker = true
            };
        }

        private static AnimalPetDefinition LegacyPet(
            string id,
            string name,
            string description,
            AnimalMotionStyle motion,
            string spriteUrl,
            string spriteFileName,
            int columns,
            int rows,
            int animationRow,
            int frameCount,
            float fps,
            bool directionalRows,
            bool pixelArt,
            float speed,
            float visualScale,
            string sourcePage,
            string author,
            string license)
        {
            return new AnimalPetDefinition
            {
                Id = id,
                Name = name,
                Description = description,
                Motion = motion,
                Runtime = AnimalPetRuntime.Sprite,
                SpriteUrl = spriteUrl,
                SpriteFileName = spriteFileName,
                SpriteColumns = columns,
                SpriteRows = rows,
                AnimationRow = animationRow,
                FrameCount = frameCount,
                FramesPerSecond = fps,
                DirectionalRows = directionalRows,
                PixelArt = pixelArt,
                SourcePage = sourcePage,
                AssetAuthor = author,
                AssetLicense = license,
                Speed = speed,
                VisualScale = visualScale,
                ShowInPicker = false,
                ArtworkUrl = spriteUrl,
                ArtworkFileName = spriteFileName
            };
        }
    }
}
