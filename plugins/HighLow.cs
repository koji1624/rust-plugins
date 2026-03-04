// ============================================================
//  HighLow.cs  — ハイ＆ロー プラグイン
//  Framework : Carbon (Rust Mod)
//  Version   : 1.0.0
//  Author    : AntiGravity
// ============================================================
//
//  【遊び方】
//  /highlow で開く → 賭け金を選んでスタート
//  現在のカード (1〜13) を見て「HIGH（高い）」か「LOW（低い）」を選択
//  正解すると獲得スクラップが増える！いつでも「換金」して引き上げOK
//  連続正解するほどボーナス倍率が上がる！
//
//  【コマンド】
//  /highlow          … UIを開く
//  /highlow admin reset … ゲームを強制リセット（管理者専用）
//
// ============================================================
using System;
using System.Collections.Generic;
using Carbon.Base;
using Oxide.Core;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Carbon.Plugins
{
    [Info("HighLow", "AntiGravity", "1.0.0")]
    [Description("ハイ＆ロー（カード賭けゲーム）プラグイン")]
    public class HighLow : CarbonPlugin
    {
        // ============================================================
        //  定数
        // ============================================================
        private const string CmdMain    = "highlow";
        private const string PermAdmin  = "highlow.admin";
        private const string UIMain     = "HighLow_Main";   // メインパネル

        private const string ItemScrap  = "scrap";

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
                if (PluginConfig == null)
                {
                    PluginConfig = new Configuration();
                    SaveConfig();
                    return;
                }
            }
            catch
            {
                LoadDefaultConfig();
                SaveConfig();
            }
            UpdateConfig();
        }

        private void UpdateConfig()
        {
            if (PluginConfig.Version >= Version) return;
            PrintWarning("Outdated configuration file detected. Updating...");
            PluginConfig.Version = Version;
            SaveConfig();
        }

        public class Configuration
        {
            [Newtonsoft.Json.JsonProperty(PropertyName = "Version (DO NOT CHANGE)", Order = int.MaxValue)]
            public VersionNumber Version = new VersionNumber(1, 0, 0);

            [Newtonsoft.Json.JsonProperty("最低賭け金（スクラップ）")]
            public int MinBet = 10;

            [Newtonsoft.Json.JsonProperty("最高賭け金（スクラップ）")]
            public int MaxBet = 5000;

            // 通常時の勝利倍率 （賭け金 × この値 が払い出し）
            [Newtonsoft.Json.JsonProperty("通常勝利倍率")]
            public float BaseMultiplier = 1.9f;

            // 連続正解ボーナス倍率テーブル（インデックス = 連続数-1）
            // 0番目：1連続目(通常)、1番目：2連続目、…
            [Newtonsoft.Json.JsonProperty("連続正解ボーナス倍率テーブル")]
            public float[] StreakMultipliers = { 1.9f, 2.0f, 2.5f, 3.0f };
        }

        // ============================================================
        //  列挙型
        // ============================================================

        /// <summary>プレイヤーの状態</summary>
        private enum HLPhase
        {
            Idle,           // 未参加 / 結果確認中
            WaitingChoice,  // HIGH or LOW を選ぶ待ち
        }

        // ============================================================
        //  セッションクラス（1プレイヤーにつき1つ）
        // ============================================================
        private class HLSession
        {
            public ulong  PlayerId     { get; set; }
            public string DisplayName  { get; set; }
            public int    InitialBet   { get; set; }   // 最初の賭け金
            public int    CurrentPot   { get; set; }   // 現在の獲得予定スクラップ
            public int    CurrentCard  { get; set; }   // 今見えているカード（1〜13）
            public int    Streak       { get; set; }   // 連続正解数
            public HLPhase Phase       { get; set; } = HLPhase.Idle;
            public string LastResult   { get; set; } = "";  // 直前の結果メッセージ
        }

        // ============================================================
        //  ゲーム状態
        // ============================================================
        private readonly Dictionary<ulong, HLSession> _sessions
            = new Dictionary<ulong, HLSession>();
        private readonly System.Random _rnd = new System.Random();

        // ============================================================
        //  Carbon ライフサイクル
        // ============================================================
        private void Init()
        {
            permission.RegisterPermission(PermAdmin, this);
            cmd.AddChatCommand(CmdMain, this, nameof(OnHighLowCommand));
        }

        private void Unload()
        {
            // 全プレイヤーのUIを閉じる
            foreach (var kv in _sessions)
            {
                var bp = BasePlayer.FindByID(kv.Key);
                if (bp != null) CloseUI(bp);
            }
            _sessions.Clear();
        }

        // ============================================================
        //  コマンド
        // ============================================================
        private void OnHighLowCommand(BasePlayer player, string command, string[] args)
        {
            // 管理者リセット
            if (args != null && args.Length >= 2
                && args[0].ToLower() == "admin"
                && args[1].ToLower() == "reset")
            {
                if (!permission.UserHasPermission(player.UserIDString, PermAdmin))
                {
                    SendReply(player, "[ハイ＆ロー] 管理者専用コマンドです。");
                    return;
                }
                CloseUI(player);
                if (_sessions.ContainsKey(player.userID))
                    _sessions.Remove(player.userID);
                SendReply(player, "[ハイ＆ロー] あなたのセッションをリセットしました。");
                return;
            }

            OpenMainUI(player);
        }

        // ============================================================
        //  ゲームロジック
        // ============================================================

        /// <summary>セッションを取得（なければ新規作成）</summary>
        private HLSession GetOrCreateSession(BasePlayer player)
        {
            if (!_sessions.TryGetValue(player.userID, out var s))
            {
                s = new HLSession
                {
                    PlayerId    = player.userID,
                    DisplayName = player.displayName,
                    Phase       = HLPhase.Idle
                };
                _sessions[player.userID] = s;
            }
            return s;
        }

        /// <summary>1〜13のカードをランダムに引く</summary>
        private int DrawCard() => _rnd.Next(1, 14);

        /// <summary>連続正解数に応じたオッズ倍率を返す</summary>
        private float GetOdds(int streak)
        {
            var table = PluginConfig.StreakMultipliers;
            int idx = Mathf.Clamp(streak - 1, 0, table.Length - 1);
            return table[idx];
        }

        /// <summary>カードの表示文字列</summary>
        private static string CardName(int card) => card switch
        {
            1  => "A",
            11 => "J",
            12 => "Q",
            13 => "K",
            _  => card.ToString()
        };

        /// <summary>賭け金を受け取ってゲーム開始</summary>
        private void StartGame(BasePlayer player, int bet)
        {
            var s = GetOrCreateSession(player);

            if (s.Phase == HLPhase.WaitingChoice)
            {
                SendReply(player, "[ハイ＆ロー] すでにゲーム中です。");
                return;
            }

            // 賭け金チェック
            bet = Mathf.Clamp(bet, PluginConfig.MinBet, PluginConfig.MaxBet);
            if (GetScrap(player) < bet)
            {
                SendReply(player, $"[ハイ＆ロー] スクラップが足りません（必要: {bet}）");
                OpenMainUI(player);
                return;
            }

            // スクラップを預かる
            TakeScrap(player, bet);

            // 最初のカードを引く
            s.InitialBet  = bet;
            s.CurrentPot  = bet;         // 最初は賭け金がそのままポットに
            s.CurrentCard = DrawCard();
            s.Streak      = 0;
            s.LastResult  = "";
            s.Phase       = HLPhase.WaitingChoice;

            OpenMainUI(player);
        }

        /// <summary>HIGH / LOW を選択した</summary>
        private void OnChoice(BasePlayer player, bool chooseHigh)
        {
            if (!_sessions.TryGetValue(player.userID, out var s)
                || s.Phase != HLPhase.WaitingChoice)
            {
                SendReply(player, "[ハイ＆ロー] ゲームを開始してから選んでください。");
                return;
            }

            int prevCard = s.CurrentCard;
            int nextCard = DrawCard();

            // 引き分け（同じ数字）→ ポットを返還してゲーム終了
            if (nextCard == prevCard)
            {
                int refund = s.CurrentPot;
                GiveScrap(player, refund);
                s.LastResult = $"🤝 引き分け！ カード {CardName(prevCard)} → {CardName(nextCard)}\n{refund} スクラップ返還しました";
                s.Phase = HLPhase.Idle;
                s.Streak = 0;
                OpenMainUI(player);
                return;
            }

            // 勝敗判定
            bool won = chooseHigh ? (nextCard > prevCard) : (nextCard < prevCard);
            string choiceStr = chooseHigh ? "HIGH ↑" : "LOW ↓";

            if (won)
            {
                s.Streak++;
                float odds = GetOdds(s.Streak);
                // 賭け金 × 連続正解オッズ をポットとする
                s.CurrentPot  = Mathf.RoundToInt(s.InitialBet * odds);
                s.CurrentCard = nextCard;
                s.LastResult  = $"✅ 正解！ {choiceStr} | {CardName(prevCard)} → {CardName(nextCard)} | 連続 {s.Streak} 回 | 獲得予定: {s.CurrentPot} スクラップ";
                // ゲームを続ける（WaitingChoiceのまま）
                OpenMainUI(player);
            }
            else
            {
                // 不正解 → 賭け金没収
                s.LastResult = $"❌ 不正解！ {choiceStr} | {CardName(prevCard)} → {CardName(nextCard)} | {s.InitialBet} スクラップを失いました";
                s.Phase  = HLPhase.Idle;
                s.Streak = 0;
                s.CurrentPot = 0;
                OpenMainUI(player);
            }
        }

        /// <summary>換金してゲーム終了</summary>
        private void CashOut(BasePlayer player)
        {
            if (!_sessions.TryGetValue(player.userID, out var s)
                || s.Phase != HLPhase.WaitingChoice)
            {
                SendReply(player, "[ハイ＆ロー] ゲーム中のみ換金できます。");
                return;
            }

            int payout = s.CurrentPot;
            GiveScrap(player, payout);
            s.LastResult = $"💰 換金！ {payout} スクラップを受け取りました！";
            s.Phase  = HLPhase.Idle;
            s.Streak = 0;
            OpenMainUI(player);
        }

        // ============================================================
        //  スクラップ操作
        // ============================================================

        private static ItemContainer[] GetContainers(BasePlayer player) => new[]
        {
            player.inventory.containerMain,
            player.inventory.containerBelt,
            player.inventory.containerWear,
        };

        private int GetScrap(BasePlayer player)
        {
            int total = 0;
            foreach (var container in GetContainers(player))
            {
                if (container?.itemList == null) continue;
                foreach (var item in container.itemList)
                    if (item.info.shortname == ItemScrap) total += item.amount;
            }
            return total;
        }

        private bool TakeScrap(BasePlayer player, int amount)
        {
            if (GetScrap(player) < amount) return false;

            // 先に対象アイテムを収集してから削除（イテレーション中の変更を避ける）
            var scrapItems = new List<Item>();
            foreach (var container in GetContainers(player))
            {
                if (container?.itemList == null) continue;
                foreach (var item in container.itemList)
                    if (item.info.shortname == ItemScrap) scrapItems.Add(item);
            }

            int remaining = amount;
            foreach (var item in scrapItems)
            {
                if (remaining <= 0) break;
                if (item.amount <= remaining)
                {
                    remaining -= item.amount;
                    item.RemoveFromWorld();
                    item.RemoveFromContainer();
                }
                else
                {
                    item.amount -= remaining;
                    item.MarkDirty();
                    remaining = 0;
                }
            }
            return true;
        }

        private void GiveScrap(BasePlayer player, int amount)
        {
            if (amount <= 0) return;
            var def = ItemManager.FindItemDefinition(ItemScrap);
            if (def == null) return;
            player.GiveItem(ItemManager.Create(def, amount));
        }

        private void CloseUI(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, UIMain);
        }

        // ============================================================
        //  UI描画
        // ============================================================

        private void OpenMainUI(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, UIMain);
            var s = GetOrCreateSession(player);

            // ---- カラーパレット ----
            const string ColBg       = "0.05 0.05 0.08 0.97";   // ほぼ黒
            const string ColPanel    = "0.10 0.10 0.15 1.00";   // ダークパネル
            const string ColAccent   = "0.12 0.65 0.95 1.00";   // シアンブルー
            const string ColGold     = "0.95 0.80 0.20 1.00";   // ゴールド
            const string ColGreen    = "0.20 0.85 0.45 1.00";   // 緑
            const string ColRed      = "0.90 0.25 0.25 1.00";   // 赤
            const string ColGray     = "0.35 0.35 0.40 1.00";   // グレー
            const string ColWhite    = "0.95 0.95 0.95 1.00";   // 白
            const string ColSubtext  = "0.60 0.60 0.65 1.00";   // サブテキスト
            const string ColCardBg   = "0.15 0.15 0.22 1.00";   // カード背景
            const string ColHighBtn  = "0.15 0.55 0.90 1.00";   // HIGHボタン
            const string ColLowBtn   = "0.85 0.30 0.15 1.00";   // LOWボタン
            const string ColCash     = "0.20 0.70 0.35 1.00";   // 換金ボタン
            const string ColClose    = "0.25 0.25 0.30 1.00";   // 閉じるボタン

            var cui = new CuiElementContainer();

            // === 背景オーバーレイ ===
            cui.Add(new CuiPanel
            {
                Image     = { Color = "0 0 0 0.6" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                CursorEnabled = true
            }, "Overlay", UIMain);

            // === メインウィンドウ（中央） ===
            const string winName = UIMain + "_win";
            cui.Add(new CuiPanel
            {
                Image         = { Color = ColBg },
                RectTransform = { AnchorMin = "0.30 0.18", AnchorMax = "0.70 0.82" }
            }, UIMain, winName);

            // --- タイトルバー ---
            cui.Add(new CuiPanel
            {
                Image         = { Color = ColPanel },
                RectTransform = { AnchorMin = "0 0.88", AnchorMax = "1 1" }
            }, winName, winName + "_title");

            // タイトル文字
            cui.Add(new CuiLabel
            {
                Text          = { Text = "🃏  ハイ＆ロー", FontSize = 18, Align = TextAnchor.MiddleCenter, Color = ColAccent },
                RectTransform = { AnchorMin = "0.05 0", AnchorMax = "0.85 1" }
            }, winName + "_title");

            // 閉じるボタン（×）
            cui.Add(new CuiButton
            {
                Button        = { Color = ColClose, Command = $"highlow.ui close" },
                RectTransform = { AnchorMin = "0.86 0.10", AnchorMax = "0.99 0.90" },
                Text          = { Text = "✕", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = ColWhite }
            }, winName + "_title");

            // --- 情報エリア: 所持スクラップ ---
            int currentScrap = GetScrap(player);
            cui.Add(new CuiLabel
            {
                Text          = { Text = $"💰 所持スクラップ: {currentScrap}", FontSize = 12, Align = TextAnchor.MiddleRight, Color = ColGold },
                RectTransform = { AnchorMin = "0.02 0.82", AnchorMax = "0.98 0.88" }
            }, winName);

            // ============================================================
            //  ゲーム開始前（Idle）
            // ============================================================
            if (s.Phase == HLPhase.Idle)
            {
                // 直前の結果メッセージ
                if (!string.IsNullOrEmpty(s.LastResult))
                {
                    cui.Add(new CuiLabel
                    {
                        Text          = { Text = s.LastResult, FontSize = 11, Align = TextAnchor.MiddleCenter, Color = ColWhite },
                        RectTransform = { AnchorMin = "0.04 0.66", AnchorMax = "0.96 0.82" }
                    }, winName);
                }
                else
                {
                    // 説明文
                    cui.Add(new CuiLabel
                    {
                        Text          = { Text = "次のカードが高い？低い？を当てよう\n正解するほどボーナス倍率が上がる！", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = ColSubtext },
                        RectTransform = { AnchorMin = "0.04 0.66", AnchorMax = "0.96 0.82" }
                    }, winName);
                }

                // 賭け金ラベル
                cui.Add(new CuiLabel
                {
                    Text          = { Text = "賭け金を選択", FontSize = 13, Align = TextAnchor.MiddleCenter, Color = ColWhite },
                    RectTransform = { AnchorMin = "0 0.58", AnchorMax = "1 0.66" }
                }, winName);

                // 賭け金ボタン（5種類）
                int[] betAmounts = { 10, 50, 100, 500, 1000 };
                float btnW = 0.17f;
                float gap  = 0.015f;
                float startX = 0.03f;
                for (int i = 0; i < betAmounts.Length; i++)
                {
                    float xMin = startX + i * (btnW + gap);
                    float xMax = xMin + btnW;
                    int bet = betAmounts[i];
                    string btnCol = bet <= currentScrap ? ColAccent : ColGray;
                    cui.Add(new CuiButton
                    {
                        Button        = { Color = btnCol, Command = $"highlow.ui start {bet}" },
                        RectTransform = { AnchorMin = $"{xMin:F3} 0.50", AnchorMax = $"{xMax:F3} 0.58" },
                        Text          = { Text = $"{bet}", FontSize = 11, Align = TextAnchor.MiddleCenter, Color = ColWhite }
                    }, winName);
                }

                // 最大賭け金ボタン
                cui.Add(new CuiButton
                {
                    Button        = { Color = ColGold, Command = $"highlow.ui start {PluginConfig.MaxBet}" },
                    RectTransform = { AnchorMin = "0.03 0.41", AnchorMax = "0.97 0.50" },
                    Text          = { Text = $"MAX BET ({PluginConfig.MaxBet})", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = "0.05 0.05 0.05 1" }
                }, winName);

                // オッズ表示
                cui.Add(new CuiLabel
                {
                    Text          = { Text = "▶ 通常: x1.9  |  2連: x2.0  |  3連: x2.5  |  4連+: x3.0", FontSize = 10, Align = TextAnchor.MiddleCenter, Color = ColSubtext },
                    RectTransform = { AnchorMin = "0 0.34", AnchorMax = "1 0.41" }
                }, winName);
            }
            // ============================================================
            //  ゲーム中（WaitingChoice）
            // ============================================================
            else
            {
                // 直前の結果
                if (!string.IsNullOrEmpty(s.LastResult))
                {
                    cui.Add(new CuiLabel
                    {
                        Text          = { Text = s.LastResult, FontSize = 10, Align = TextAnchor.MiddleCenter, Color = ColGreen },
                        RectTransform = { AnchorMin = "0.02 0.72", AnchorMax = "0.98 0.82" }
                    }, winName);
                }

                // ---- カード表示エリア ----
                cui.Add(new CuiPanel
                {
                    Image         = { Color = ColCardBg },
                    RectTransform = { AnchorMin = "0.20 0.52", AnchorMax = "0.80 0.72" }
                }, winName, winName + "_card");

                // カード番号
                cui.Add(new CuiLabel
                {
                    Text          = { Text = CardName(s.CurrentCard), FontSize = 28, Align = TextAnchor.MiddleCenter, Color = ColWhite },
                    RectTransform = { AnchorMin = "0 0.40", AnchorMax = "1 1" }
                }, winName + "_card");

                // カード数字（小）
                cui.Add(new CuiLabel
                {
                    Text          = { Text = $"({s.CurrentCard})", FontSize = 10, Align = TextAnchor.MiddleCenter, Color = ColSubtext },
                    RectTransform = { AnchorMin = "0 0.10", AnchorMax = "1 0.40" }
                }, winName + "_card");

                // 現在の統計
                float currentOdds = GetOdds(s.Streak + 1);
                cui.Add(new CuiLabel
                {
                    Text          = { Text = $"🔥 連続: {s.Streak}回  |  次のオッズ: x{currentOdds:F1}  |  獲得予定: {s.CurrentPot} スクラップ", FontSize = 11, Align = TextAnchor.MiddleCenter, Color = ColGold },
                    RectTransform = { AnchorMin = "0 0.44", AnchorMax = "1 0.52" }
                }, winName);

                // ---- HIGH ボタン ----
                cui.Add(new CuiButton
                {
                    Button        = { Color = ColHighBtn, Command = "highlow.ui high" },
                    RectTransform = { AnchorMin = "0.03 0.29", AnchorMax = "0.47 0.43" },
                    Text          = { Text = "HIGH ↑\n高い", FontSize = 13, Align = TextAnchor.MiddleCenter, Color = ColWhite }
                }, winName);

                // ---- LOW ボタン ----
                cui.Add(new CuiButton
                {
                    Button        = { Color = ColLowBtn, Command = "highlow.ui low" },
                    RectTransform = { AnchorMin = "0.53 0.29", AnchorMax = "0.97 0.43" },
                    Text          = { Text = "LOW ↓\n低い", FontSize = 13, Align = TextAnchor.MiddleCenter, Color = ColWhite }
                }, winName);

                // ---- 換金ボタン（1連続以上のときのみ有効） ----
                if (s.Streak >= 1)
                {
                    cui.Add(new CuiButton
                    {
                        Button        = { Color = ColCash, Command = "highlow.ui cashout" },
                        RectTransform = { AnchorMin = "0.15 0.19", AnchorMax = "0.85 0.28" },
                        Text          = { Text = $"💰 換金する  ({s.CurrentPot} スクラップ)", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = ColWhite }
                    }, winName);
                }
                else
                {
                    // まだ1回も正解していない → 灰色で無効表示
                    cui.Add(new CuiLabel
                    {
                        Text          = { Text = "正解すると換金ボタンが出現！", FontSize = 11, Align = TextAnchor.MiddleCenter, Color = ColSubtext },
                        RectTransform = { AnchorMin = "0.05 0.19", AnchorMax = "0.95 0.28" }
                    }, winName);
                }

                // 賭け金表示
                cui.Add(new CuiLabel
                {
                    Text          = { Text = $"賭け金: {s.InitialBet} スクラップ", FontSize = 11, Align = TextAnchor.MiddleCenter, Color = ColSubtext },
                    RectTransform = { AnchorMin = "0 0.12", AnchorMax = "1 0.19" }
                }, winName);
            }

            // === フッター ===
            cui.Add(new CuiLabel
            {
                Text          = { Text = "1〜13 のカード  |  同じ数字 → 引き分け（返金）", FontSize = 9, Align = TextAnchor.MiddleCenter, Color = ColSubtext },
                RectTransform = { AnchorMin = "0 0.02", AnchorMax = "1 0.10" }
            }, winName);

            CuiHelper.AddUi(player, cui);
        }

        // ============================================================
        //  コンソールコマンド（UIボタンアクション）
        // ============================================================
        [ConsoleCommand("highlow.ui")]
        private void ConsoleCmd_HighLowUI(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            string action = arg.GetString(0).ToLower();

            switch (action)
            {
                case "close":
                    CloseUI(player);
                    break;

                case "start":
                    int bet = arg.GetInt(1, PluginConfig.MinBet);
                    StartGame(player, bet);
                    break;

                case "high":
                    OnChoice(player, chooseHigh: true);
                    break;

                case "low":
                    OnChoice(player, chooseHigh: false);
                    break;

                case "cashout":
                    CashOut(player);
                    break;
            }
        }
    }
}
