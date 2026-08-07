using System;
using System.Collections.Generic;
using System.Drawing;

namespace FACM.Pets
{
    internal enum PetKind
    {
        Jelly,
        Cat,
        Fox,
        Robot,
        Ghost,
        Chick,
        Dragon,
        Star,
        PixelBot,
        CloudBunny
    }

    internal sealed class PetDefinition
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public PetKind Kind { get; set; }
        public Size Size { get; set; }
        public Color Primary { get; set; }
        public Color Secondary { get; set; }
        public Color Accent { get; set; }
        public Color Outline { get; set; }
        public bool Pixelated { get; set; }
    }

    internal static class PetCatalog
    {
        public const string DefaultPetId = "jelly-blue";

        private static readonly IReadOnlyList<PetDefinition> Pets = new List<PetDefinition>
        {
            new PetDefinition
            {
                Id = "jelly-blue",
                Name = "蓝莓啵啵",
                Description = "透明果冻感，轻微呼吸",
                Kind = PetKind.Jelly,
                Size = new Size(76, 70),
                Primary = Color.FromArgb(82, 150, 255),
                Secondary = Color.FromArgb(87, 83, 230),
                Accent = Color.FromArgb(181, 224, 255),
                Outline = Color.FromArgb(42, 75, 167)
            },
            new PetDefinition
            {
                Id = "cream-cat",
                Name = "奶油猫",
                Description = "圆脸猫咪，耳朵会抖",
                Kind = PetKind.Cat,
                Size = new Size(82, 76),
                Primary = Color.FromArgb(255, 225, 178),
                Secondary = Color.FromArgb(226, 166, 112),
                Accent = Color.FromArgb(255, 147, 169),
                Outline = Color.FromArgb(117, 78, 55)
            },
            new PetDefinition
            {
                Id = "sunset-fox",
                Name = "落日狐",
                Description = "橙红狐狸，尾巴轻摆",
                Kind = PetKind.Fox,
                Size = new Size(88, 78),
                Primary = Color.FromArgb(255, 137, 63),
                Secondary = Color.FromArgb(204, 64, 57),
                Accent = Color.FromArgb(255, 239, 195),
                Outline = Color.FromArgb(102, 43, 46)
            },
            new PetDefinition
            {
                Id = "mint-robot",
                Name = "薄荷机器人",
                Description = "机械面板，状态灯闪烁",
                Kind = PetKind.Robot,
                Size = new Size(80, 74),
                Primary = Color.FromArgb(72, 222, 189),
                Secondary = Color.FromArgb(44, 122, 159),
                Accent = Color.FromArgb(226, 255, 248),
                Outline = Color.FromArgb(30, 77, 91)
            },
            new PetDefinition
            {
                Id = "violet-ghost",
                Name = "紫雾幽灵",
                Description = "漂浮幽灵，柔和发光",
                Kind = PetKind.Ghost,
                Size = new Size(78, 80),
                Primary = Color.FromArgb(166, 106, 255),
                Secondary = Color.FromArgb(88, 58, 190),
                Accent = Color.FromArgb(240, 221, 255),
                Outline = Color.FromArgb(65, 43, 130)
            },
            new PetDefinition
            {
                Id = "lemon-chick",
                Name = "柠檬团子",
                Description = "黄色小鸟，眨眼点头",
                Kind = PetKind.Chick,
                Size = new Size(76, 72),
                Primary = Color.FromArgb(255, 216, 79),
                Secondary = Color.FromArgb(243, 159, 49),
                Accent = Color.FromArgb(255, 245, 206),
                Outline = Color.FromArgb(139, 89, 39)
            },
            new PetDefinition
            {
                Id = "ruby-dragon",
                Name = "赤焰幼龙",
                Description = "红色幼龙，角和翅膀",
                Kind = PetKind.Dragon,
                Size = new Size(92, 82),
                Primary = Color.FromArgb(239, 76, 83),
                Secondary = Color.FromArgb(141, 42, 74),
                Accent = Color.FromArgb(255, 194, 102),
                Outline = Color.FromArgb(82, 29, 55)
            },
            new PetDefinition
            {
                Id = "cosmic-star",
                Name = "星愿精灵",
                Description = "星形精灵，轨道光点",
                Kind = PetKind.Star,
                Size = new Size(80, 80),
                Primary = Color.FromArgb(113, 100, 255),
                Secondary = Color.FromArgb(244, 96, 209),
                Accent = Color.FromArgb(255, 238, 134),
                Outline = Color.FromArgb(52, 45, 132)
            },
            new PetDefinition
            {
                Id = "pixel-bot",
                Name = "像素机兵",
                Description = "复古像素风，硬边动画",
                Kind = PetKind.PixelBot,
                Size = new Size(72, 72),
                Primary = Color.FromArgb(54, 215, 247),
                Secondary = Color.FromArgb(50, 74, 158),
                Accent = Color.FromArgb(241, 255, 132),
                Outline = Color.FromArgb(20, 31, 70),
                Pixelated = true
            },
            new PetDefinition
            {
                Id = "cloud-bunny",
                Name = "云朵兔",
                Description = "柔白兔子，云朵脚垫",
                Kind = PetKind.CloudBunny,
                Size = new Size(84, 82),
                Primary = Color.FromArgb(239, 247, 255),
                Secondary = Color.FromArgb(164, 205, 255),
                Accent = Color.FromArgb(255, 160, 198),
                Outline = Color.FromArgb(92, 128, 177)
            }
        };

        public static IReadOnlyList<PetDefinition> All
        {
            get { return Pets; }
        }

        public static PetDefinition Get(string id)
        {
            foreach (var pet in Pets)
            {
                if (string.Equals(pet.Id, id, StringComparison.OrdinalIgnoreCase)) return pet;
            }
            return Pets[0];
        }
    }
}
