using System.Text;

namespace FACM.Core.Mayhem;

public static class MayhemChampionAliases
{
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["寒冰"] = "ashe", ["艾希"] = "ashe",
        ["琴女"] = "sona", ["琴瑟仙女"] = "sona",
        ["火男"] = "brand", ["布兰德"] = "brand",
        ["vn"] = "vayne", ["薇恩"] = "vayne",
        ["女警"] = "caitlyn", ["凯特琳"] = "caitlyn",
        ["金克丝"] = "jinx", ["萝莉"] = "jinx",
        ["光辉"] = "lux", ["拉克丝"] = "lux",
        ["石头人"] = "malphite", ["墨菲特"] = "malphite",
        ["木木"] = "amumu", ["阿木木"] = "amumu",
        ["剑圣"] = "master-yi", ["易大师"] = "master-yi",
        ["亚索"] = "yasuo", ["永恩"] = "yone",
        ["诺手"] = "darius", ["德莱厄斯"] = "darius",
        ["盖伦"] = "garen",
        ["皇子"] = "jarvan-iv", ["嘉文"] = "jarvan-iv",
        ["盲僧"] = "lee-sin", ["瞎子"] = "lee-sin",
        ["德邦"] = "xin-zhao", ["赵信"] = "xin-zhao",
        ["妖姬"] = "leblanc", ["乐芙兰"] = "leblanc",
        ["发条"] = "orianna", ["奥莉安娜"] = "orianna",
        ["卡牌"] = "twisted-fate", ["崔斯特"] = "twisted-fate",
        ["男枪"] = "graves", ["格雷福斯"] = "graves",
        ["女枪"] = "miss-fortune", ["赏金"] = "miss-fortune",
        ["提莫"] = "teemo", ["小法"] = "veigar", ["维迦"] = "veigar",
        ["大头"] = "heimerdinger", ["大发明家"] = "heimerdinger",
        ["老鼠"] = "twitch", ["图奇"] = "twitch",
        ["狗头"] = "nasus", ["内瑟斯"] = "nasus",
        ["鳄鱼"] = "renekton", ["雷克顿"] = "renekton",
        ["龙王"] = "aurelion-sol", ["铸星龙王"] = "aurelion-sol",
        ["乌鸦"] = "swain", ["斯维因"] = "swain",
        ["蚂蚱"] = "malzahar", ["马尔扎哈"] = "malzahar",
        ["泽拉斯"] = "xerath", ["辛德拉"] = "syndra",
        ["狐狸"] = "ahri", ["阿狸"] = "ahri",
        ["小鱼人"] = "fizz",
        ["机器人"] = "blitzcrank", ["布里茨"] = "blitzcrank",
        ["锤石"] = "thresh", ["泰坦"] = "nautilus",
        ["女坦"] = "leona", ["曙光"] = "leona",
        ["牛头"] = "alistar", ["牛"] = "alistar",
        ["猫咪"] = "yuumi", ["悠米"] = "yuumi",
        ["萨勒芬妮"] = "seraphine", ["歌姬"] = "seraphine",
        ["蒙多"] = "dr-mundo", ["赛恩"] = "sion", ["塞恩"] = "sion",
        ["腕豪"] = "sett", ["瑟提"] = "sett",
        ["铁男"] = "mordekaiser", ["莫德凯撒"] = "mordekaiser",
        ["稻草人"] = "fiddlesticks", ["费德提克"] = "fiddlesticks",
        ["死歌"] = "karthus", ["卡尔萨斯"] = "karthus",
        ["乌迪尔"] = "udyr", ["奎桑提"] = "ksante",
        ["滑板鞋"] = "kalista", ["卡莉丝塔"] = "kalista",
        ["轮子妈"] = "sivir", ["希维尔"] = "sivir",
        ["维鲁斯"] = "varus", ["韦鲁斯"] = "varus",
        ["烬"] = "jhin", ["卢锡安"] = "lucian", ["卢仙"] = "lucian",
        ["ez"] = "ezreal", ["伊泽瑞尔"] = "ezreal",
        ["卡莎"] = "kaisa", ["霞"] = "xayah", ["洛"] = "rakan",
        ["豹女"] = "nidalee", ["奈德丽"] = "nidalee",
        ["蜘蛛"] = "elise", ["伊莉丝"] = "elise",
        ["螳螂"] = "khazix", ["卡兹克"] = "khazix",
        ["狮子狗"] = "rengar", ["雷恩加尔"] = "rengar",
        ["梦魇"] = "nocturne", ["魔腾"] = "nocturne",
        ["蔚"] = "vi", ["艾克"] = "ekko",
        ["琪亚娜"] = "qiyana", ["奇亚娜"] = "qiyana",
        ["沙皇"] = "azir", ["阿兹尔"] = "azir",
        ["冰鸟"] = "anivia", ["艾尼维亚"] = "anivia",
        ["冰女"] = "lissandra", ["丽桑卓"] = "lissandra",
        ["天使"] = "kayle", ["凯尔"] = "kayle",
        ["莫甘娜"] = "morgana",
        ["风女"] = "janna", ["迦娜"] = "janna",
        ["奶妈"] = "soraka", ["索拉卡"] = "soraka",
        ["娜美"] = "nami", ["璐璐"] = "lulu", ["露露"] = "lulu"
    };

    public static bool TryResolve(string? input, out string slug)
    {
        slug = string.Empty;
        if (string.IsNullOrWhiteSpace(input)) return false;

        var normalized = Normalize(input);
        if (Aliases.TryGetValue(normalized, out var mapped))
        {
            slug = mapped;
            return true;
        }

        if (!IsLikelySlug(input)) return false;
        slug = Slugify(input);
        return slug.Length > 0;
    }

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var builder = new StringBuilder();
        foreach (var ch in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch) || ch > 127) builder.Append(ch);
        }
        return builder.ToString();
    }

    public static string Slugify(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var text = value.Trim().ToLowerInvariant()
            .Replace("'", string.Empty, StringComparison.Ordinal)
            .Replace(".", string.Empty, StringComparison.Ordinal)
            .Replace("&", "and", StringComparison.Ordinal)
            .Replace(" ", "-", StringComparison.Ordinal)
            .Replace("_", "-", StringComparison.Ordinal);
        while (text.Contains("--", StringComparison.Ordinal))
            text = text.Replace("--", "-", StringComparison.Ordinal);
        return text.Trim('-');
    }

    private static bool IsLikelySlug(string input)
    {
        foreach (var ch in input)
            if (ch > 127) return false;
        return true;
    }
}
