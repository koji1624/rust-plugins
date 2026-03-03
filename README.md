# rust-plugins

Rust (ゲーム) 用のCarbonプラグイン集です。

## 環境
- フレームワーク: [Carbon](https://carbonmod.gg)
- 言語: C#

## フォルダ構成
- `plugins/` … プラグイン本体 (.csファイル)
- `configs/` … 各プラグインの設定ファイル
- `docs/`    … ドキュメント

## 使い方
1. `plugins/` 内の `.cs` をサーバーの `carbon/plugins/` にコピー
2. サーバーが自動でロード

## ホットリロード
c.reload PluginName
