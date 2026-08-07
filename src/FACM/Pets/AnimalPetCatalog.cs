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

    internal sealed class AnimalPetDefinition
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public AnimalMotionStyle Motion { get; set; }

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
    }

    internal static class AnimalPetCatalog
    {
        public const string DefaultPetId = "cat";

        private static readonly IReadOnlyList<AnimalPetDefinition> Pets = new List<AnimalPetDefinition>
        {
            Pet(
                "cat", "猫咪", "真正 5 帧跑动动画，不再是一张图片平移。", AnimalMotionStyle.Walk,
                "https://opengameart.org/sites/default/files/cat_run.png", "cat_run.png",
                5, 1, 0, 5, 12f, false, true, 1.00f, 0.82f,
                "https://opengameart.org/content/pixel-cat-0", "alizard", "CC0"),

            Pet(
                "dog", "狗狗", "6 帧走路循环，身体和四肢会真正逐帧变化。", AnimalMotionStyle.Walk,
                "https://opengameart.org/sites/default/files/dog_medium.png", "dog_medium.png",
                6, 6, 1, 6, 11f, false, true, 1.03f, 0.86f,
                "https://opengameart.org/content/dog-3", "rmazanek / Shepardskin / Hellkipz", "CC0"),

            Pet(
                "spider", "蜘蛛", "8 个方向、13 帧步态；转向和八条腿爬行都是真动画。", AnimalMotionStyle.Crawl,
                "https://opengameart.org/sites/default/files/sprite_sheet_3.png", "iso_spider_8x13.png",
                13, 8, 0, 13, 15f, true, false, 0.88f, 0.86f,
                "https://opengameart.org/content/iso-spider-spritesheet", "KillGorack", "CC0"),

            Pet(
                "ant", "蚂蚁", "多方向行走序列，六条腿会持续交替迈步。", AnimalMotionStyle.Crawl,
                "https://opengameart.org/sites/default/files/walk_5.png", "walking_ant.png",
                8, 8, 0, 8, 14f, true, false, 0.82f, 0.72f,
                "https://opengameart.org/content/walking-ant-with-parts-and-rigged-spriter-file", "DudeMan", "CC0"),

            Pet(
                "greenfly", "绿苍蝇", "三帧高速扇翅，移动轨迹更碎、更像飞虫。", AnimalMotionStyle.Fly,
                "https://opengameart.org/sites/default/files/greenfly_spritesheet.png", "greenfly_spritesheet.png",
                3, 1, 0, 3, 22f, false, true, 1.36f, 0.56f,
                "https://opengameart.org/content/16x16-flies", "ARoachIFoundOnMyPillow", "CC0"),

            Pet(
                "greyfly", "灰苍蝇", "三帧高速扇翅，会快速改变方向并轻微抖动。", AnimalMotionStyle.Fly,
                "https://opengameart.org/sites/default/files/greyfly_spritesheet.png", "greyfly_spritesheet.png",
                3, 1, 0, 3, 22f, false, true, 1.38f, 0.56f,
                "https://opengameart.org/content/16x16-flies", "ARoachIFoundOnMyPillow", "CC0"),

            Pet(
                "wasp", "胡蜂", "双帧高速振翅，移动速度快，转向频繁。", AnimalMotionStyle.Fly,
                "https://opengameart.org/sites/default/files/spr_wasp_flying_strip_2.png", "wasp_flying.png",
                2, 1, 0, 2, 18f, false, true, 1.28f, 0.66f,
                "https://opengameart.org/content/flying-hornetwasp", "Nerveona", "CC0"),

            Pet(
                "bird", "小鸟", "使用完整动画表中的飞行行，翅膀逐帧拍动。", AnimalMotionStyle.Fly,
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
