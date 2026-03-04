// ============================================================
//  NoRepair.cs  — 修理禁止プラグイン
//  Framework : Carbon (Rust Mod)
//  Version   : 1.2.0
//  Author    : AntiGravity
// ============================================================
//
//  【概要】
//  修理台でのツール・武器の修理を禁止する
//
//  【設定】
//  configs/NoRepair.json で対象カテゴリを変更可能
//
// ============================================================
using Carbon.Base;
using HarmonyLib;

namespace Carbon.Plugins
{
    [Info("NoRepair", "AntiGravity", "1.2.0")]
    [Description("ツール・武器の修理を禁止する")]
    public class NoRepair : CarbonPlugin
    {
        // ============================================================
        //  設定クラス
        // ============================================================
        public Configuration PluginConfig;

        protected override void LoadDefaultConfig() => PluginConfig = new Configuration();
        protected override void SaveConfig() => Config.WriteObject(PluginConfig, true);

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                PluginConfig = Config.ReadObject<Configuration>();
                if (PluginConfig == null) { LoadDefaultConfig(); SaveConfig(); }
            }
            catch { LoadDefaultConfig(); SaveConfig(); }
        }

        public class Configuration
        {
            [Newtonsoft.Json.JsonProperty("武器の修理を禁止")]
            public bool BlockWeapons = true;

            [Newtonsoft.Json.JsonProperty("ツールの修理を禁止")]
            public bool BlockTools = true;

            [Newtonsoft.Json.JsonProperty("防具の修理を禁止")]
            public bool BlockAttire = false;

            [Newtonsoft.Json.JsonProperty("修理禁止時にメッセージを表示")]
            public bool ShowMessage = true;

            [Newtonsoft.Json.JsonProperty("メッセージ内容")]
            public string Message = "このアイテムは修理できません。";
        }

        // ============================================================
        //  静的参照（Harmonyパッチから参照するため）
        // ============================================================
        private static NoRepair _instance;
        private HarmonyLib.Harmony _harmony;

        private void Init()
        {
            _instance = this;

            // Carbon プラグインでは手動で PatchAll を呼ぶ必要がある
            _harmony = new HarmonyLib.Harmony(Name);
            _harmony.PatchAll(GetType().Assembly);
        }

        private void Unload()
        {
            _harmony?.UnpatchAll(Name);
            _instance = null;
        }

        // ============================================================
        //  Harmony パッチ
        //  RepairItem(RPCMessage) → RepairAnItem(Item, BasePlayer, ...) の流れを
        //  RepairAnItem で直接インターセプトする
        // ============================================================
        [HarmonyPatch(typeof(RepairBench), "RepairAnItem")]
        private static class PatchRepairAnItem
        {
            // RepairAnItem(Item itemToRepair, BasePlayer player, BaseEntity repairBenchEntity,
            //              float maxConditionLostOnRepair, bool mustKnowBlueprint)
            private static bool Prefix(Item itemToRepair, BasePlayer player)
            {
                if (_instance == null) return true;

                var cat = itemToRepair?.info?.category;
                if (cat == null) return true;

                var cfg = _instance.PluginConfig;

                bool blocked =
                    (cfg.BlockWeapons && cat == ItemCategory.Weapon) ||
                    (cfg.BlockTools   && cat == ItemCategory.Tool)   ||
                    (cfg.BlockAttire  && cat == ItemCategory.Attire);

                if (!blocked) return true;

                if (cfg.ShowMessage && player != null)
                    _instance.SendReply(player, cfg.Message);

                return false;
            }
        }
    }
}
