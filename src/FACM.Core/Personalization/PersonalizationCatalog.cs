namespace FACM.Core.Personalization;

public sealed record FacmThemeDefinition(
    string Id,
    string Name,
    string Description,
    string Background,
    string BackgroundSecondary,
    string Surface,
    string SurfaceSecondary,
    string Border,
    string TextPrimary,
    string TextMuted,
    string Accent,
    string AccentSecondary,
    string Success,
    string Warning,
    bool IsLight,
    double CardRadius,
    double ControlRadius);

public static class FacmThemeCatalog
{
    public const string DefaultThemeId = "glass-blue";

    public static IReadOnlyList<FacmThemeDefinition> All { get; } =
    [
        Theme("glass-blue", "深海玻璃", "蓝紫玻璃、柔光圆角", "#070E22", "#0D193C", "#1C274E", "#253466", "#6784FF", "#F6F9FF", "#A2B2E0", "#3D69FF", "#7A48FF", "#50EFB4", "#FFBE5B", false, 22, 14),
        Theme("obsidian-gold", "曜石鎏金", "黑金金属、精致双线", "#090A0A", "#141310", "#181816", "#231F18", "#B18132", "#F8DEA1", "#B09B6F", "#CC9636", "#F8CD71", "#E2BA62", "#FF9243", false, 10, 4),
        Theme("neon-cyber", "霓虹赛博", "洋红青蓝、锐角 HUD", "#050712", "#13051F", "#180826", "#071F30", "#FF30B4", "#FAFAFF", "#B3B0DE", "#FF21A8", "#00E0FF", "#00F5CF", "#FFA43E", false, 8, 3),
        Theme("cloud-light", "云端浅色", "清爽白蓝、柔和卡片", "#F4F7FD", "#FFFFFF", "#FFFFFF", "#EBF2FF", "#D3DCEE", "#1A2743", "#667797", "#4E7EFF", "#4ECABE", "#27BA8B", "#EA8B39", true, 24, 12),
        Theme("brutalist-grid", "先锋构成", "黑白蓝红、粗框大字", "#0A0A0A", "#181818", "#0F0F0F", "#EFECE0", "#F5F2E8", "#F7F4EA", "#C3BFB3", "#2449DC", "#EC3A2B", "#66DC79", "#F44B31", false, 0, 0),
        Theme("holo-spectrum", "全息光谱", "全息渐变、晶体面板", "#051027", "#111944", "#162A58", "#261B5C", "#53DAFF", "#EFF9FF", "#8FB7DE", "#20B8FF", "#BC4CFF", "#47F5CA", "#FFB04C", false, 14, 8),
        Theme("mono-emerald", "墨绿极简", "克制黑灰、细线绿光", "#121619", "#181D20", "#1B2023", "#1F2629", "#3C484C", "#EBEFEF", "#919B9C", "#4DC28E", "#58E1AA", "#58E1AA", "#E0A75B", false, 16, 4),
        Theme("rgb-tactical", "RGB 战术", "电竞灯效、战术切角", "#040814", "#0C1123", "#0E162B", "#1C1137", "#4690FF", "#F6F9FF", "#9FB1D4", "#00B9FF", "#FF37CE", "#00EBBC", "#FF6680", false, 6, 2),
        Theme("aurora-night", "极光夜幕", "青紫极光、深夜氛围", "#040C21", "#081634", "#112144", "#13304D", "#2D89CE", "#F3F8FF", "#8EA5CA", "#11BEE0", "#8542FF", "#43EFB1", "#FFB854", false, 20, 13),
        Theme("sunset-synthwave", "落日合成波", "橙粉紫夜、复古未来", "#0C061F", "#1B0936", "#230C3B", "#1C1148", "#F343C5", "#FFF1FB", "#C097CF", "#E22ACE", "#FF852F", "#3CECB4", "#FF8B34", false, 12, 6)
    ];

    public static FacmThemeDefinition Get(string? id) =>
        All.FirstOrDefault(theme => string.Equals(theme.Id, id, StringComparison.OrdinalIgnoreCase)) ?? All[0];

    public static bool Contains(string? id) =>
        All.Any(theme => string.Equals(theme.Id, id, StringComparison.OrdinalIgnoreCase));

    private static FacmThemeDefinition Theme(
        string id,
        string name,
        string description,
        string background,
        string backgroundSecondary,
        string surface,
        string surfaceSecondary,
        string border,
        string textPrimary,
        string textMuted,
        string accent,
        string accentSecondary,
        string success,
        string warning,
        bool isLight,
        double cardRadius,
        double controlRadius) =>
        new(id, name, description, background, backgroundSecondary, surface, surfaceSecondary, border,
            textPrimary, textMuted, accent, accentSecondary, success, warning, isLight, cardRadius, controlRadius);
}

public enum FacmPetRuntimeKind
{
    FlyingSprite,
    VPetCore,
    LegacyCompatibility
}

public sealed record FacmPetDefinition(
    string Id,
    string Name,
    string Description,
    FacmPetRuntimeKind Runtime,
    bool ShowInPicker);

public static class FacmPetCatalog
{
    public const string DefaultPetId = "greenfly";

    public static IReadOnlyList<FacmPetDefinition> All { get; } =
    [
        new("greenfly", "绿苍蝇", "高速、小范围急转的轻量飞行桌宠。", FacmPetRuntimeKind.FlyingSprite, true),
        new("bee", "蜜蜂", "中速巡航、短暂停悬。", FacmPetRuntimeKind.FlyingSprite, true),
        new("real-bee", "真实蜜蜂", "写真级蜜蜂外观，沿用轻量飞行轨迹。", FacmPetRuntimeKind.FlyingSprite, true),
        new("dragonfly", "蜻蜓", "快速冲刺、急停和快速改向。", FacmPetRuntimeKind.FlyingSprite, true),
        new("butterfly", "蝴蝶", "慢速大曲线飞行。", FacmPetRuntimeKind.FlyingSprite, true),
        new("moth", "飞蛾", "短距离随机游走。", FacmPetRuntimeKind.FlyingSprite, true),
        new("vpet", "高精度桌宠 · VPet Core", "独立 PetHost 运行的高精度桌宠。", FacmPetRuntimeKind.VPetCore, true),
        new("cat", "猫咪（兼容）", "旧版 Sprite 配置兼容。", FacmPetRuntimeKind.LegacyCompatibility, false),
        new("dog", "狗狗（兼容）", "旧版 Sprite 配置兼容。", FacmPetRuntimeKind.LegacyCompatibility, false),
        new("spider", "蜘蛛（兼容）", "旧版 Sprite 配置兼容。", FacmPetRuntimeKind.LegacyCompatibility, false),
        new("ant", "蚂蚁（兼容）", "旧版 Sprite 配置兼容。", FacmPetRuntimeKind.LegacyCompatibility, false),
        new("greyfly", "灰苍蝇（兼容）", "旧版 Sprite 配置兼容。", FacmPetRuntimeKind.LegacyCompatibility, false),
        new("wasp", "胡蜂（兼容）", "旧版 Sprite 配置兼容。", FacmPetRuntimeKind.LegacyCompatibility, false),
        new("bird", "小鸟（兼容）", "旧版 Sprite 配置兼容。", FacmPetRuntimeKind.LegacyCompatibility, false)
    ];

    public static IReadOnlyList<FacmPetDefinition> Visible { get; } = All.Where(pet => pet.ShowInPicker).ToArray();

    public static FacmPetDefinition Get(string? id) =>
        All.FirstOrDefault(pet => string.Equals(pet.Id, id, StringComparison.OrdinalIgnoreCase)) ??
        All.First(pet => string.Equals(pet.Id, DefaultPetId, StringComparison.Ordinal));

    public static bool Contains(string? id) =>
        All.Any(pet => string.Equals(pet.Id, id, StringComparison.OrdinalIgnoreCase));
}
