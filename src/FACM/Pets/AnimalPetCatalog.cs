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
    }

    internal static class AnimalPetCatalog
    {
        public const string DefaultPetId = "cat";

        private static readonly IReadOnlyList<AnimalPetDefinition> Pets = new List<AnimalPetDefinition>
        {
            VPetPet(),

            Pet(
                "cat", "猫咪", "轻量猫咪桌宠。", AnimalMotionStyle.Walk,
                "https://opengameart.org/sites/default/files/cat_run.png", "cat_run.png",
                5, 1, 0, 5, 12f, false, true, 1.00f, 0.82f,
                "https://opengameart.org/content/pixel-cat-0", "alizard", "CC0"),

            Pet(
                "dog", "狗狗", "轻量狗狗桌宠。", AnimalMotionStyle.Walk,
                "https://opengameart.org/sites/default/files/dog_medium.png", "dog_medium.png",
                6, 6, 1, 6, 11f, false, true, 1.03f, 0.86f,
                "https://opengameart.org/content/dog-3", "rmazanek / Shepardskin / Hellkipz", "CC0"),

            Pet(
                "spider", "蜘蛛", "支持多方向移动的蜘蛛桌宠。", AnimalMotionStyle.Crawl,
                "https://opengameart.org/sites/default/files/sprite_sheet_3.png", "iso_spider_8x13.png",
                13, 8, 0, 13, 15f, true, false, 0.88f, 0.86f,
                "https://opengameart.org/content/iso-spider-spritesheet", "KillGorack", "CC0"),

            Pet(
                "ant", "蚂蚁", "支持多方向移动的蚂蚁桌宠。", AnimalMotionStyle.Crawl,
                "https://opengameart.org/sites/default/files/walk_5.png", "walking_ant.png",
                8, 8, 0, 8, 14f, true, false, 0.82f, 0.72f,
                "https://opengameart.org/content/walking-ant-with-parts-and-rigged-spriter-file", "DudeMan", "CC0"),

            Pet(
                "greenfly", "绿苍蝇", "轻量飞行桌宠。", AnimalMotionStyle.Fly,
                "https://opengameart.org/sites/default/files/greenfly_spritesheet.png", "greenfly_spritesheet.png",
                3, 1, 0, 3, 22f, false, true, 1.36f, 0.56f,
                "https://opengameart.org/content/16x16-flies", "ARoachIFoundOnMyPillow", "CC0"),

            Pet(
                "greyfly", "灰苍蝇", "轻量飞行桌宠。", AnimalMotionStyle.Fly,
                "https://opengameart.org/sites/default/files/greyfly_spritesheet.png", "greyfly_spritesheet.png",
                3, 1, 0, 3, 22f, false, true, 1.38f, 0.56f,
                "https://opengameart.org/content/16x16-flies", "ARoachIFoundOnMyPillow", "CC0"),

            Pet(
                "wasp", "胡蜂", "轻量飞行桌宠。", AnimalMotionStyle.Fly,
                "https://opengameart.org/sites/default/files/spr_wasp_flying_strip_2.png", "wasp_flying.png",
                2, 1, 0, 2, 18f, false, true, 1.28f, 0.66f,
                "https://opengameart.org/content/flying-hornetwasp", "Nerveona", "CC0"),

            Pet(
                "bird", "小鸟", "轻量飞行桌宠。", AnimalMotionStyle.Fly,
                "https://opengameart.org/sites/default/files/bird_v001_blue_and_yellow.png", "bird_blue_yellow.png",
                11, 8, 6, 11, 15f, false, true, 1.18f, 0.70f,
                "https://opengameart.org/content/bird-2", "rmazanek", "CC0")
        };

        public static IReadOnlyList<AnimalPetDefinition> All
        {
            get { return Pets; }
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

        private static AnimalPetDefinition VPetPet()
        {
            return new AnimalPetDefinition
            {
                Id = "vpet",
                Name = "高精度桌宠",
                Description = "支持待机、移动、触摸和拖动。首次使用需要联网准备桌宠资源。",
                Motion = AnimalMotionStyle.Walk,
                Runtime = AnimalPetRuntime.VPetCore,
                SourcePage = "https://github.com/LorisYounger/VPet",
                AssetAuthor = "VUP-Simulator team / VPet",
                AssetLicense = "VPet 非商用动画授权",
                VisualScale = 1f
            };
        }

        private static AnimalPetDefinition Pet(
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
                ArtworkUrl = spriteUrl,
                ArtworkFileName = spriteFileName
            };
        }
    }
}
