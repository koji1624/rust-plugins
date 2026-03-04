# CLAUDE.md

このファイルは、リポジトリ内のコードを扱う際に Claude Code (claude.ai/code) へ提供するガイダンスです。

## 概要

Rustゲーム向けの [Carbon](https://carbonmod.gg) プラグイン集です。C# で記述します。Carbon は `.cs` ファイルをそのままホットリロードするフレームワークのため、このリポジトリにビルド手順はありません。

## デプロイ & ホットリロード

`plugins/` 内の `.cs` ファイルをサーバーの `carbon/plugins/` にコピーすると自動でロードされます。

稼働中のサーバーでプラグインをリロードするには（RCON またはサーバーコンソール）:
```
c.reload PluginName
```

## リポジトリ構成

- `plugins/` — C# プラグイン本体 (`.cs`)。1ファイル = 1プラグイン
- `configs/` — 各プラグインが実行時に生成・使用する JSON 設定ファイル
- `docs/` — プラグインのドキュメント

## Carbon プラグインの規約

- プラグインは Carbon が Roslyn で実行時コンパイルする単一の `.cs` ファイル
- プラグインクラスは `CarbonPlugin` を継承する（Oxide 互換にする場合は `RustPlugin`）
- フックは Carbon のフックシグネチャに合わせたメソッドとして実装する（例: `OnPlayerConnected`, `OnEntitySpawned`）
- 設定は型付きクラスを使った `LoadConfig()` / `SaveConfig()` パターンで管理する
- パーミッションは `Init()` 内で `permission.RegisterPermission(...)` を使って登録する
- クラスに付与する `[Info]` 属性でプラグイン名・バージョン・作者を設定する

## 確認済み API

**プレイヤー識別子**
- `player.userID` → `ulong`（Steam ID）
- `player.UserIDString` → `string`

**インベントリ**
- `AllItems()` は存在しない。3コンテナを直接参照する:
  ```csharp
  player.inventory.containerMain.itemList
  player.inventory.containerBelt.itemList
  player.inventory.containerWear.itemList
  ```
- イテレーション中にアイテムを削除する場合は先に `List<Item>` へコピーする

**CUI**
- `using Oxide.Game.Rust.Cui;` で `CuiHelper` / `CuiElementContainer` / `CuiPanel` / `CuiLabel` / `CuiButton` が使える
- `CuiHelper.AddUi(player, container)` でUI送信、`CuiHelper.DestroyUi(player, name)` で破棄

**フック**
- `Carbon.Hooks.Base` は Carbon 固有フック（29個）のみ
- `OnItemRepair` などの標準 Oxide フックは Oxide 互換レイヤーで処理される（Carbon.Hooks.Base には含まれない）
- `refs/Carbon.Hooks.Base/` で Carbon 固有フックを確認、標準フックは [uMod ドキュメント](https://umod.org/documentation/rust/hooks) を参照

## refs ディレクトリ

- `refs/Carbon.Common/` — CarbonPlugin基底クラス・CUI・Oxide互換ライブラリのソース
- `refs/Carbon.Hooks.Base/` — Carbon 固有フックの定義

## ナレッジ

- `docs/carbon-plugin-patterns.md` — Harmony パッチの使い方・よくある間違い・確認済みAPIパターン
- `docs/MEMORY.md` — 開発メモ・要点まとめ

## ドキュメント運用方針

- `ignore/` — 開発に使う元資料（DLL・ツール・外部リポジトリ等）。git管理外
- `docs/` — `ignore/` の資料から得られた知見をまとめたもの。git管理対象
