# Memory

## Carbon プラグイン開発

詳細: `carbon-plugin-patterns.md`

### 重要: Harmony パッチの適用方法
Carbon プラグインでは `[HarmonyPatch]` を書くだけでは**適用されない**。
`Init()` / `Unload()` で手動管理が必須:

```csharp
private static PluginClass _instance;
private HarmonyLib.Harmony _harmony;

private void Init()
{
    _instance = this;
    _harmony = new HarmonyLib.Harmony(Name);
    _harmony.PatchAll(GetType().Assembly);
}

private void Unload()
{
    _harmony?.UnpatchAll(Name);
    _instance = null;
}
```

### 確認済み API
- `player.userID` (ulong) / `player.UserIDString` (string) ← `STEAMID` は存在しない
- `AllItems()` は存在しない → `containerMain` / `containerBelt` / `containerWear` を直接使う
- `using Oxide.Game.Rust.Cui;` で `CuiHelper` / `CuiElementContainer` が使える

### refs ディレクトリ
- `refs/Carbon.Common/` — CarbonPlugin 基底・CUI・Oxide互換ライブラリ
- `refs/Carbon.Hooks.Base/` — Carbon 固有フック定義（29個のみ、OnItemRepair 等は含まない）
- Carbon 固有以外の標準フックは Oxide 互換レイヤー経由だが未実装のものもある
