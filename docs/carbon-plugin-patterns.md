# Carbon プラグイン開発パターン

## Harmony パッチ

Carbon プラグインでは `[HarmonyPatch]` を書くだけでは適用されない。
`Init()` で `PatchAll`、`Unload()` で `UnpatchAll` を必ず呼ぶ。

```csharp
private static MyPlugin _instance;
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

パッチクラスから設定やログにアクセスするために `_instance` 静的参照を使う。

## パッチ対象の探し方

1. リフレクションで `DeclaredOnly` を使って対象クラス固有のメソッドを列挙する
2. メソッドの `DeclaringType` を確認して正しい型をパッチする
3. RPC メソッドは `OnRpcMessage` 経由で呼ばれ、メソッド名は文字列で照合される

```csharp
// DeclaringType の確認例
var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
var m = typeof(TargetClass).GetMethod("MethodName", flags);
Puts($"DeclaringType = {m?.DeclaringType?.FullName}");
```

## RepairBench の修理フロー（確認済み）

```
クライアント RPC
  → RepairBench.RepairItem(RPCMessage msg)   // RPC ハンドラ
    → RepairBench.RepairAnItem(Item, BasePlayer, BaseEntity, float, bool)  // static、ここをパッチ
      → item.DoRepair(maxConditionLost)
```

- `DoRepair` は `BaseCombatEntity` で定義（ベンチ自体の修理用、アイテム修理ではない）
- アイテム修理をブロックするには `RepairAnItem` をパッチする

## よくある間違い

| 間違い | 正解 |
|---|---|
| `player.STEAMID` | `player.userID` |
| `player.inventory.AllItems()` | 3コンテナを直接列挙 |
| `[HarmonyPatch]` だけ書く | `harmony.PatchAll()` を手動で呼ぶ |
| 継承元メソッドを子クラスでパッチ | `DeclaringType` を確認して正しい型をパッチ |

## インベントリ列挙パターン

```csharp
private static ItemContainer[] GetContainers(BasePlayer player) => new[]
{
    player.inventory.containerMain,
    player.inventory.containerBelt,
    player.inventory.containerWear,
};
```

イテレーション中にアイテムを削除する場合は先に `List<Item>` へコピーする。
