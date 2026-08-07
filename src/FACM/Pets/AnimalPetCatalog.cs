using System;
using System.Collections.Generic;
using System.Drawing;

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
        public Color Primary { get; set; }
        public Color Secondary { get; set; }
        public Color Accent { get; set; }
        public float Speed { get; set; }
    }

    internal static class AnimalPetCatalog
    {
        public const string DefaultPetId = "cat";

        private static readonly IReadOnlyList<AnimalPetDefinition> Pets = new List<AnimalPetDefinition>
        {
            Pet("cat", "猫咪", "会摇尾巴、四处散步的小猫。", AnimalMotionStyle.Walk, Color.FromArgb(238, 178, 92), Color.FromArgb(255, 224, 175), Color.FromArgb(133, 73, 38), 1.00f),
            Pet("shiba", "柴犬", "小步快走，偶尔停下来东张西望。", AnimalMotionStyle.Walk, Color.FromArgb(221, 135, 64), Color.FromArgb(255, 232, 192), Color.FromArgb(91, 54, 34), 1.05f),
            Pet("rabbit", "兔兔", "一跳一跳地在桌面里闲逛。", AnimalMotionStyle.Hop, Color.FromArgb(244, 244, 246), Color.FromArgb(255, 202, 216), Color.FromArgb(96, 101, 118), 1.10f),
            Pet("hamster", "仓鼠", "圆滚滚地慢慢乱跑，动作比较轻。", AnimalMotionStyle.Crawl, Color.FromArgb(221, 167, 105), Color.FromArgb(255, 230, 186), Color.FromArgb(109, 71, 46), 0.82f),
            Pet("fox", "狐狸", "大尾巴会摆动，走动速度偏快。", AnimalMotionStyle.Walk, Color.FromArgb(235, 107, 46), Color.FromArgb(255, 238, 212), Color.FromArgb(96, 53, 38), 1.16f),
            Pet("panda", "熊猫", "慢吞吞地晃来晃去。", AnimalMotionStyle.Waddle, Color.FromArgb(245, 246, 244), Color.FromArgb(45, 49, 53), Color.FromArgb(77, 80, 86), 0.72f),
            Pet("chick", "小鸡", "小碎步乱逛，偶尔扑一下翅膀。", AnimalMotionStyle.Walk, Color.FromArgb(255, 214, 66), Color.FromArgb(255, 239, 133), Color.FromArgb(231, 132, 36), 1.05f),
            Pet("penguin", "企鹅", "左右摇摆着走，动作比较憨。", AnimalMotionStyle.Waddle, Color.FromArgb(37, 47, 62), Color.FromArgb(247, 248, 244), Color.FromArgb(244, 157, 49), 0.78f),
            Pet("turtle", "乌龟", "贴着桌面慢慢爬，不容易跑远。", AnimalMotionStyle.Crawl, Color.FromArgb(91, 150, 91), Color.FromArgb(147, 190, 101), Color.FromArgb(61, 100, 68), 0.55f),
            Pet("butterfly", "蝴蝶", "会在屏幕里自由飞动，翅膀持续扇动。", AnimalMotionStyle.Fly, Color.FromArgb(124, 112, 236), Color.FromArgb(231, 147, 235), Color.FromArgb(73, 59, 133), 1.22f)
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
            Color primary,
            Color secondary,
            Color accent,
            float speed)
        {
            return new AnimalPetDefinition
            {
                Id = id,
                Name = name,
                Description = description,
                Motion = motion,
                Primary = primary,
                Secondary = secondary,
                Accent = accent,
                Speed = speed
            };
        }
    }
}
