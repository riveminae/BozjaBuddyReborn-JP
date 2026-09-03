# Lost Finds Cache ↔ Holster 転送調査

更新: 2026-09-01

## 結論

2026-09-01時点の公開ソースから、Cache/Holsterの**在庫読取**は確定できたが、サーバーへ正規転送を要求する公開メソッド/IPC/既存Dalamud実装は特定できていない。

したがって v1.1 は以下を厳守する。

- `MycItem.Count` 等を直接書き換えない。
- 推測した `FireCallback` 引数を本番処理として送らない。
- Initialize/差分補給のpolicy/planner/state machineは先に実装してよい。
- 実際の転送effectだけは、正規callback/functionを根拠付きで確定するまで隔離する。

## 公開ソースで確定したこと

FFXIVClientStructs `AgentMycItemBox`:

- AgentId: `MycItemBox`
- `AgentMycItemBox.ItemBoxData`
- `MycItemBoxData._itemCaches`: 7カテゴリ
- `MycItemBoxData._itemHolsters`: 7カテゴリ
- `MycItemCategory._items`: 48 entries/category
- `MycItem.ActionId`
- `MycItem.Count`
- `MycItemBoxData.LastSelectedActionId`

これらは現在状態の観測に使用できる。

公開ClientStructsには、Cache→Holster / Holster→Cacheを実行するメンバ関数は定義されていない。

## 既存BozjaBuddy調査

`kaleidocli/BozjaBuddy` には以下が存在する。

- `UINode_MycItemBox`
- `ExtGui_MycItemBox`
- `ExtGui_MycItemBagTrade`

ただし確認した実装は主にLost Finds Cache/Trade addon上へのoverlay・loadout表示であり、サーバー転送を自動実行する既知のcallback実装は確認できなかった。

addon名として以下は公開ソースから確認できる。

- `MYCItemBox`
- `MYCItemBag`
- `MYCItemBagTrade`

## 現在branchの診断基盤

`Game/MycItemBoxCallbackProbe.cs` は、MYC系UIの実際のイベントを英語ログへ記録するための診断専用コードである。

目的は引数を推測することではなく、ゲーム自身が手動操作時に送った実値を記録すること。

このprobeが取得すべき最小ケース:

1. CacheからHolsterへ1個だけ移動
2. HolsterからCacheへ1個だけ返却
3. 可能なら数量指定で複数個移動

記録対象:

- addon名
- event type / param
- callback argument types/values
- 操作前後の `ActionId` / Cache count / Holster count

## 転送APIを確定した後の実装境界

`Game/HolsterInventory.cs` 相当へ以下を閉じ込める。

```text
Capture()
BeginWithdraw(rowId, count)
BeginReturn(rowId, count)
IsTransferSettled(before, expectedDelta)
```

上位の `InitializationCoordinator` / `SupplyManager` はcallback番号やaddon nodeを知らない設計にする。

## Server acknowledgement

成功判定はUI callbackの戻り値だけに依存しない。

転送前snapshotと転送後 `AgentMycItemBox.ItemBoxData` を比較して、対象rowのCache/Holster countが期待deltaになったことを確認してから次操作へ進む。

タイムアウト/不一致時はtransaction failureとして扱う。

## 残る不可避な確認

公開コードだけでは転送callback自体が見つからなかったため、この一点だけは最終的にゲームクライアントが発火する実callbackを取得する必要がある可能性が高い。

ただし、それまでは他のv1.1機能を止めない。Initializeについてもtarget planning、preflight、snapshot、rollback plan、UI、補給判定までは先行実装する。
