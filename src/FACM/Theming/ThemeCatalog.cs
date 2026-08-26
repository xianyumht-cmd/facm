using System;
using System.Collections.Generic;
using System.Drawing;

namespace FACM.Theming
{
    internal enum ThemeStyle
    {
        Glass,
        Luxury,
        Cyber,
        Soft,
        Brutalist,
        Holographic,
        Minimal,
        Rgb,
        Aurora,
        Synthwave
    }

    internal sealed class ThemeDefinition
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public ThemeStyle Style { get; set; }
        public Size WindowSize { get; set; }
        public Color Background { get; set; }
        public Color BackgroundSecondary { get; set; }
        public Color Surface { get; set; }
        public Color SurfaceSecondary { get; set; }
        public Color Border { get; set; }
        public Color TextPrimary { get; set; }
        public Color TextMuted { get; set; }
        public Color Accent { get; set; }
        public Color AccentSecondary { get; set; }
        public Color Success { get; set; }
        public Color Warning { get; set; }
        public bool IsLight { get; set; }
        public int Radius { get; set; }
        public int ButtonRadius { get; set; }
        public float BorderWidth { get; set; }
        public string FontName { get; set; }
        public FontStyle HeaderFontStyle { get; set; }

        public bool UsesAngularCorners
        {
            get
            {
                return Style == ThemeStyle.Cyber ||
                       Style == ThemeStyle.Brutalist ||
                       Style == ThemeStyle.Rgb ||
                       Style == ThemeStyle.Synthwave;
            }
        }
    }

    internal static class ThemeCatalog
    {
        public const string DefaultThemeId = "glass-blue";

        private static readonly IReadOnlyList<ThemeDefinition> Themes = new List<ThemeDefinition>
        {
            new ThemeDefinition
            {
                Id = "glass-blue",
                Name = "深海玻璃",
                Description = "蓝紫玻璃、柔光圆角",
                Style = ThemeStyle.Glass,
                WindowSize = new Size(390, 620),
                Background = Color.FromArgb(7, 14, 34),
                BackgroundSecondary = Color.FromArgb(13, 25, 60),
                Surface = Color.FromArgb(28, 39, 78),
                SurfaceSecondary = Color.FromArgb(37, 52, 102),
                Border = Color.FromArgb(103, 132, 255),
                TextPrimary = Color.FromArgb(246, 249, 255),
                TextMuted = Color.FromArgb(162, 178, 224),
                Accent = Color.FromArgb(61, 105, 255),
                AccentSecondary = Color.FromArgb(122, 72, 255),
                Success = Color.FromArgb(80, 239, 180),
                Warning = Color.FromArgb(255, 190, 91),
                Radius = 22,
                ButtonRadius = 14,
                BorderWidth = 1.2F,
                FontName = "Microsoft YaHei UI",
                HeaderFontStyle = FontStyle.Bold
            },
            new ThemeDefinition
            {
                Id = "obsidian-gold",
                Name = "曜石鎏金",
                Description = "黑金金属、精致双线",
                Style = ThemeStyle.Luxury,
                WindowSize = new Size(398, 630),
                Background = Color.FromArgb(9, 10, 10),
                BackgroundSecondary = Color.FromArgb(20, 19, 16),
                Surface = Color.FromArgb(24, 24, 22),
                SurfaceSecondary = Color.FromArgb(35, 31, 24),
                Border = Color.FromArgb(177, 129, 50),
                TextPrimary = Color.FromArgb(248, 222, 161),
                TextMuted = Color.FromArgb(176, 155, 111),
                Accent = Color.FromArgb(204, 150, 54),
                AccentSecondary = Color.FromArgb(248, 205, 113),
                Success = Color.FromArgb(226, 186, 98),
                Warning = Color.FromArgb(255, 146, 67),
                Radius = 10,
                ButtonRadius = 4,
                BorderWidth = 1.3F,
                FontName = "Microsoft YaHei UI",
                HeaderFontStyle = FontStyle.Bold
            },
            new ThemeDefinition
            {
                Id = "neon-cyber",
                Name = "霓虹赛博",
                Description = "洋红青蓝、锐角 HUD",
                Style = ThemeStyle.Cyber,
                WindowSize = new Size(408, 642),
                Background = Color.FromArgb(5, 7, 18),
                BackgroundSecondary = Color.FromArgb(19, 5, 31),
                Surface = Color.FromArgb(24, 8, 38),
                SurfaceSecondary = Color.FromArgb(7, 31, 48),
                Border = Color.FromArgb(255, 48, 180),
                TextPrimary = Color.FromArgb(250, 250, 255),
                TextMuted = Color.FromArgb(179, 176, 222),
                Accent = Color.FromArgb(255, 33, 168),
                AccentSecondary = Color.FromArgb(0, 224, 255),
                Success = Color.FromArgb(0, 245, 207),
                Warning = Color.FromArgb(255, 164, 62),
                Radius = 8,
                ButtonRadius = 3,
                BorderWidth = 1.7F,
                FontName = "Microsoft YaHei UI",
                HeaderFontStyle = FontStyle.Bold | FontStyle.Italic
            },
            new ThemeDefinition
            {
                Id = "cloud-light",
                Name = "云端浅色",
                Description = "清爽白蓝、柔和卡片",
                Style = ThemeStyle.Soft,
                WindowSize = new Size(394, 624),
                Background = Color.FromArgb(244, 247, 253),
                BackgroundSecondary = Color.FromArgb(255, 255, 255),
                Surface = Color.FromArgb(255, 255, 255),
                SurfaceSecondary = Color.FromArgb(235, 242, 255),
                Border = Color.FromArgb(211, 220, 238),
                TextPrimary = Color.FromArgb(26, 39, 67),
                TextMuted = Color.FromArgb(102, 119, 151),
                Accent = Color.FromArgb(78, 126, 255),
                AccentSecondary = Color.FromArgb(78, 202, 190),
                Success = Color.FromArgb(39, 186, 139),
                Warning = Color.FromArgb(234, 139, 57),
                IsLight = true,
                Radius = 24,
                ButtonRadius = 12,
                BorderWidth = 1F,
                FontName = "Microsoft YaHei UI",
                HeaderFontStyle = FontStyle.Bold
            },
            new ThemeDefinition
            {
                Id = "brutalist-grid",
                Name = "先锋构成",
                Description = "黑白蓝红、粗框大字",
                Style = ThemeStyle.Brutalist,
                WindowSize = new Size(430, 610),
                Background = Color.FromArgb(10, 10, 10),
                BackgroundSecondary = Color.FromArgb(24, 24, 24),
                Surface = Color.FromArgb(15, 15, 15),
                SurfaceSecondary = Color.FromArgb(239, 236, 224),
                Border = Color.FromArgb(245, 242, 232),
                TextPrimary = Color.FromArgb(247, 244, 234),
                TextMuted = Color.FromArgb(195, 191, 179),
                Accent = Color.FromArgb(36, 73, 220),
                AccentSecondary = Color.FromArgb(236, 58, 43),
                Success = Color.FromArgb(102, 220, 121),
                Warning = Color.FromArgb(244, 75, 49),
                Radius = 0,
                ButtonRadius = 0,
                BorderWidth = 2F,
                FontName = "Arial",
                HeaderFontStyle = FontStyle.Bold
            },
            new ThemeDefinition
            {
                Id = "holo-spectrum",
                Name = "全息光谱",
                Description = "全息渐变、晶体面板",
                Style = ThemeStyle.Holographic,
                WindowSize = new Size(402, 638),
                Background = Color.FromArgb(5, 16, 39),
                BackgroundSecondary = Color.FromArgb(17, 25, 68),
                Surface = Color.FromArgb(22, 42, 88),
                SurfaceSecondary = Color.FromArgb(38, 27, 92),
                Border = Color.FromArgb(83, 218, 255),
                TextPrimary = Color.FromArgb(239, 249, 255),
                TextMuted = Color.FromArgb(143, 183, 222),
                Accent = Color.FromArgb(32, 184, 255),
                AccentSecondary = Color.FromArgb(188, 76, 255),
                Success = Color.FromArgb(71, 245, 202),
                Warning = Color.FromArgb(255, 176, 76),
                Radius = 14,
                ButtonRadius = 8,
                BorderWidth = 1.5F,
                FontName = "Microsoft YaHei UI",
                HeaderFontStyle = FontStyle.Bold
            },
            new ThemeDefinition
            {
                Id = "mono-emerald",
                Name = "墨绿极简",
                Description = "克制黑灰、细线绿光",
                Style = ThemeStyle.Minimal,
                WindowSize = new Size(388, 616),
                Background = Color.FromArgb(18, 22, 25),
                BackgroundSecondary = Color.FromArgb(24, 29, 32),
                Surface = Color.FromArgb(27, 32, 35),
                SurfaceSecondary = Color.FromArgb(31, 38, 41),
                Border = Color.FromArgb(60, 72, 76),
                TextPrimary = Color.FromArgb(235, 239, 239),
                TextMuted = Color.FromArgb(145, 155, 156),
                Accent = Color.FromArgb(77, 194, 142),
                AccentSecondary = Color.FromArgb(88, 225, 170),
                Success = Color.FromArgb(88, 225, 170),
                Warning = Color.FromArgb(224, 167, 91),
                Radius = 16,
                ButtonRadius = 4,
                BorderWidth = 1F,
                FontName = "Microsoft YaHei UI",
                HeaderFontStyle = FontStyle.Regular
            },
            new ThemeDefinition
            {
                Id = "rgb-tactical",
                Name = "RGB 战术",
                Description = "电竞灯效、战术切角",
                Style = ThemeStyle.Rgb,
                WindowSize = new Size(412, 646),
                Background = Color.FromArgb(4, 8, 20),
                BackgroundSecondary = Color.FromArgb(12, 17, 35),
                Surface = Color.FromArgb(14, 22, 43),
                SurfaceSecondary = Color.FromArgb(28, 17, 55),
                Border = Color.FromArgb(70, 144, 255),
                TextPrimary = Color.FromArgb(246, 249, 255),
                TextMuted = Color.FromArgb(159, 177, 212),
                Accent = Color.FromArgb(0, 185, 255),
                AccentSecondary = Color.FromArgb(255, 55, 206),
                Success = Color.FromArgb(0, 235, 188),
                Warning = Color.FromArgb(255, 102, 128),
                Radius = 6,
                ButtonRadius = 2,
                BorderWidth = 1.8F,
                FontName = "Microsoft YaHei UI",
                HeaderFontStyle = FontStyle.Bold | FontStyle.Italic
            },
            new ThemeDefinition
            {
                Id = "aurora-night",
                Name = "极光夜幕",
                Description = "青紫极光、深夜氛围",
                Style = ThemeStyle.Aurora,
                WindowSize = new Size(398, 630),
                Background = Color.FromArgb(4, 12, 33),
                BackgroundSecondary = Color.FromArgb(8, 22, 52),
                Surface = Color.FromArgb(17, 33, 68),
                SurfaceSecondary = Color.FromArgb(19, 48, 77),
                Border = Color.FromArgb(45, 137, 206),
                TextPrimary = Color.FromArgb(243, 248, 255),
                TextMuted = Color.FromArgb(142, 165, 202),
                Accent = Color.FromArgb(17, 190, 224),
                AccentSecondary = Color.FromArgb(133, 66, 255),
                Success = Color.FromArgb(67, 239, 177),
                Warning = Color.FromArgb(255, 184, 84),
                Radius = 20,
                ButtonRadius = 13,
                BorderWidth = 1.2F,
                FontName = "Microsoft YaHei UI",
                HeaderFontStyle = FontStyle.Bold
            },
            new ThemeDefinition
            {
                Id = "sunset-synthwave",
                Name = "落日合成波",
                Description = "橙粉紫夜、复古未来",
                Style = ThemeStyle.Synthwave,
                WindowSize = new Size(404, 636),
                Background = Color.FromArgb(12, 6, 31),
                BackgroundSecondary = Color.FromArgb(27, 9, 54),
                Surface = Color.FromArgb(35, 12, 59),
                SurfaceSecondary = Color.FromArgb(28, 17, 72),
                Border = Color.FromArgb(243, 67, 197),
                TextPrimary = Color.FromArgb(255, 241, 251),
                TextMuted = Color.FromArgb(192, 151, 207),
                Accent = Color.FromArgb(226, 42, 206),
                AccentSecondary = Color.FromArgb(255, 133, 47),
                Success = Color.FromArgb(60, 236, 180),
                Warning = Color.FromArgb(255, 139, 52),
                Radius = 12,
                ButtonRadius = 6,
                BorderWidth = 1.5F,
                FontName = "Microsoft YaHei UI",
                HeaderFontStyle = FontStyle.Bold
            }
        }.AsReadOnly();

        public static IReadOnlyList<ThemeDefinition> All
        {
            get { return Themes; }
        }

        public static ThemeDefinition Get(string id)
        {
            foreach (var theme in Themes)
            {
                if (string.Equals(theme.Id, id, StringComparison.OrdinalIgnoreCase)) return theme;
            }
            return Themes[0];
        }
    }
}
