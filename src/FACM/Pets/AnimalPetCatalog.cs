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
        public string ArtworkUrl { get; set; }
        public string ArtworkFileName { get; set; }
        public float Speed { get; set; }
        public float VisualScale { get; set; }
    }

    internal static class AnimalPetCatalog
    {
        public const string DefaultPetId = "cat";
        private const string NotoBase = "https://raw.githubusercontent.com/googlefonts/noto-emoji/main/png/128/";

        private static readonly IReadOnlyList<AnimalPetDefinition> Pets = new List<AnimalPetDefinition>
        {
            Pet("cat", "猫咪", "会在桌面里散步、停顿和转身。", AnimalMotionStyle.Walk, "emoji_u1f408.png", 1.00f, 0.92f),
            Pet("shiba", "狗狗", "小步快走，偶尔停下来东张西望。", AnimalMotionStyle.Walk, "emoji_u1f415.png", 1.04f, 0.92f),
            Pet("rabbit", "兔兔", "一跳一跳地在桌面里闲逛。", AnimalMotionStyle.Hop, "emoji_u1f407.png", 1.08f, 0.94f),
            Pet("hamster", "仓鼠", "圆滚滚地慢慢乱跑，动作比较轻。", AnimalMotionStyle.Crawl, "emoji_u1f439.png", 0.80f, 0.80f),
            Pet("fox", "狐狸", "动作轻快，走动速度偏快。", AnimalMotionStyle.Walk, "emoji_u1f98a.png", 1.14f, 0.82f),
            Pet("panda", "熊猫", "慢吞吞地晃来晃去。", AnimalMotionStyle.Waddle, "emoji_u1f43c.png", 0.72f, 0.82f),
            Pet("chick", "小鸡", "小碎步乱逛，偶尔轻轻跳一下。", AnimalMotionStyle.Walk, "emoji_u1f425.png", 1.04f, 0.86f),
            Pet("penguin", "企鹅", "左右摇摆着走，动作比较憨。", AnimalMotionStyle.Waddle, "emoji_u1f427.png", 0.78f, 0.86f),
            Pet("turtle", "乌龟", "贴着桌面慢慢爬，不容易跑远。", AnimalMotionStyle.Crawl, "emoji_u1f422.png", 0.54f, 0.88f),
            Pet("butterfly", "蝴蝶", "会在屏幕里自由飞动，轨迹更轻快。", AnimalMotionStyle.Fly, "emoji_u1f98b.png", 1.20f, 0.80f)
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
            string artworkFileName,
            float speed,
            float visualScale)
        {
            return new AnimalPetDefinition
            {
                Id = id,
                Name = name,
                Description = description,
                Motion = motion,
                ArtworkFileName = artworkFileName,
                ArtworkUrl = NotoBase + artworkFileName,
                Speed = speed,
                VisualScale = visualScale
            };
        }
    }
}
