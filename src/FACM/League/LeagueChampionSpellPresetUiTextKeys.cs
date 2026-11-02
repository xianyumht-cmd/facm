using System;
using System.Collections.Generic;

namespace FACM.League
{
    internal static class LeagueChampionSpellPresetUiTextKeys
    {
        public const string Toggle = "LeagueChampionSpellPresetToggle";
        public const string Save = "LeagueChampionSpellPresetSave";
        public const string Remove = "LeagueChampionSpellPresetRemove";
        public const string Saved = "LeagueChampionSpellPresetSaved";
        public const string Removed = "LeagueChampionSpellPresetRemoved";
        public const string Unavailable = "LeagueChampionSpellPresetUnavailable";
        public const string SaveFailed = "LeagueChampionSpellPresetSaveFailed";
        public const string SavedRunes = "LeagueChampionSpellPresetSavedRunes";
        public const string SavedSpells = "LeagueChampionSpellPresetSavedSpells";
        public const string EditRunes = "LeagueChampionSpellPresetEditRunes";
        public const string RuneCatalogUnavailable = "LeagueChampionSpellPresetRuneCatalogUnavailable";
        public const string EditorTitle = "LeagueChampionSpellPresetEditorTitle";
        public const string PrimaryStyle = "LeagueChampionSpellPresetPrimaryStyle";
        public const string SecondaryStyle = "LeagueChampionSpellPresetSecondaryStyle";
        public const string Keystone = "LeagueChampionSpellPresetKeystone";
        public const string PrimaryRow1 = "LeagueChampionSpellPresetPrimaryRow1";
        public const string PrimaryRow2 = "LeagueChampionSpellPresetPrimaryRow2";
        public const string PrimaryRow3 = "LeagueChampionSpellPresetPrimaryRow3";
        public const string SecondaryRow1 = "LeagueChampionSpellPresetSecondaryRow1";
        public const string SecondaryRow2 = "LeagueChampionSpellPresetSecondaryRow2";
        public const string StatRow1 = "LeagueChampionSpellPresetStatRow1";
        public const string StatRow2 = "LeagueChampionSpellPresetStatRow2";
        public const string StatRow3 = "LeagueChampionSpellPresetStatRow3";
        public const string EditorHint = "LeagueChampionSpellPresetEditorHint";
        public const string EditorSave = "LeagueChampionSpellPresetEditorSave";
        public const string EditorCancel = "LeagueChampionSpellPresetEditorCancel";
        public const string ManagerTitle = "LeagueChampionSpellPresetManagerTitle";
        public const string ManagerChampion = "LeagueChampionSpellPresetManagerChampion";
        public const string ManagerGroup = "LeagueChampionSpellPresetManagerGroup";
        public const string ManagerDelete = "LeagueChampionSpellPresetManagerDelete";
        public const string ManagerDeleteConfirm = "LeagueChampionSpellPresetManagerDeleteConfirm";
        public const string ManagerClosed = "LeagueChampionSpellPresetManagerClosed";
        public const string ManagerEdit = "LeagueChampionSpellPresetManagerEdit";
        public const string ManagerRunes = "LeagueChampionSpellPresetManagerRunes";
        public const string ManagerSpells = "LeagueChampionSpellPresetManagerSpells";
        public const string ManagerEditSpells = "LeagueChampionSpellPresetManagerEditSpells";
        public const string ManagerDeleteSpells = "LeagueChampionSpellPresetManagerDeleteSpells";
        public const string ManagerDeleteSpellsConfirm = "LeagueChampionSpellPresetManagerDeleteSpellsConfirm";
        public const string ManagerImportAkari = "LeagueChampionSpellPresetManagerImportAkari";
        public const string ManagerExportAkari = "LeagueChampionSpellPresetManagerExportAkari";
        public const string ManagerImportConfirm = "LeagueChampionSpellPresetManagerImportConfirm";
        public const string ManagerImportSuccess = "LeagueChampionSpellPresetManagerImportSuccess";
        public const string ManagerExportSuccess = "LeagueChampionSpellPresetManagerExportSuccess";
        public const string ManagerTransferFailed = "LeagueChampionSpellPresetManagerTransferFailed";
        public const string SpellEditorTitle = "LeagueChampionSpellPresetSpellEditorTitle";
        public const string SpellSlot1 = "LeagueChampionSpellPresetSpellSlot1";
        public const string SpellSlot2 = "LeagueChampionSpellPresetSpellSlot2";
        public const string SpellEditorSave = "LeagueChampionSpellPresetSpellEditorSave";

        private static readonly Dictionary<string, string> Defaults = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { Toggle, "选人时自动恢复英雄配置（符文/技能，与 OP.GG 自动应用互斥）" },
            { Save, "保存当前配置" },
            { EditRunes, "编辑符文" },
            { RuneCatalogUnavailable, "当前客户端的符文资料不可用，未修改预设。" },
            { EditorTitle, "编辑英雄符文 · " },
            { PrimaryStyle, "主系" },
            { SecondaryStyle, "副系" },
            { Keystone, "基石符文" },
            { PrimaryRow1, "主系第一行" },
            { PrimaryRow2, "主系第二行" },
            { PrimaryRow3, "主系第三行" },
            { SecondaryRow1, "副系符文一" },
            { SecondaryRow2, "副系符文二" },
            { StatRow1, "属性碎片一" },
            { StatRow2, "属性碎片二" },
            { StatRow3, "属性碎片三" },
            { EditorHint, "从当前游戏客户端读取可用符文。副系必须选不同的两行；保存只修改本地预设，不立即写入游戏。" },
            { EditorSave, "保存符文" },
            { EditorCancel, "取消" },
            { ManagerTitle, "英雄符文预设管理" },
            { ManagerChampion, "英雄" },
            { ManagerGroup, "模式 / 位置" },
            { ManagerDelete, "删除符文预设" },
            { ManagerDeleteConfirm, "确认删除所选英雄和模式的符文预设？召唤师技能预设不受影响。" },
            { ManagerClosed, "符文预设管理已关闭。" },
            { ManagerEdit, "编辑 / 新建符文" },
            { ManagerRunes, "符文" },
            { ManagerSpells, "召唤师技能" },
            { ManagerEditSpells, "编辑技能" },
            { ManagerDeleteSpells, "删除技能" },
            { ManagerDeleteSpellsConfirm, "确认删除所选英雄和模式的召唤师技能预设？符文预设不受影响。" },
            { ManagerImportAkari, "导入 Akari" },
            { ManagerExportAkari, "导出 Akari" },
            { ManagerImportConfirm, "从剪贴板导入 Akari v1.5.1 英雄配置并替换对应类别的全部本地预设？符文 / 技能条目数：" },
            { ManagerImportSuccess, "Akari 英雄配置已导入本地预设。" },
            { ManagerExportSuccess, "符文和召唤师技能预设已复制为 Akari JSON。" },
            { ManagerTransferFailed, "配置数据无效、过大或剪贴板不可用；未执行导入。" },
            { SpellEditorTitle, "编辑召唤师技能 · " },
            { SpellSlot1, "召唤师技能一" },
            { SpellSlot2, "召唤师技能二" },
            { SpellEditorSave, "保存技能" },
            { Remove, "删除此预设" },
            { Saved, "已保存本英雄及模式的符文与技能预设。" },
            { SavedRunes, "已保存符文预设；技能读取不可用，旧技能预设未改动。" },
            { SavedSpells, "已保存技能预设；符文读取不可用，旧符文预设未改动。" },
            { Removed, "已删除本英雄及模式的预设。" },
            { Unavailable, "请进入选英雄界面，选择英雄后再保存。" },
            { SaveFailed, "当前配置读取失败，未修改预设。" }
        };

        public static bool TryGetDefault(string key, out string value)
        {
            return Defaults.TryGetValue(key ?? string.Empty, out value);
        }
    }
}
