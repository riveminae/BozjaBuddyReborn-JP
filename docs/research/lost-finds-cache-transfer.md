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

### 再現可能な手動採取手順

これは Test build だけで行う観測であり、プラグインに転送を実行させない。

1. ボズヤまたはザトゥノル内で、Cache と Holster を開く。対象のLost Actionを1種類だけ選び、操作前に Cache/ Holster の両方の個数を記録する。
2. Advanced の `LogMycItemBoxCallbacks` をONにする。通常ログに他の操作を混ぜない。
3. **Cache → Holster**: 選んだ同一rowを、ゲームのUIを手で使ってちょうど1個移す。probeの同じ `id` の event 行と correlation 行を保存する。
4. Holster の個数が1増え、Cache の個数が1減ったことをゲームUIでも確認する。
5. **Holster → Cache**: 同じrowを、ゲームのUIを手で使ってちょうど1個戻す。同様に event/correlation 行と画面上の前後個数を保存する。
6. `LogMycItemBoxCallbacks` をOFFにする。採取物にはキャラクター名等を含めず、必要な行だけを開発者へ渡す。

各方向で確定に必要な証拠は、(a) event行の addon/type/eventParam/atkParam/data、(b) 同じ `id` の **before/after** Cache/Holster row-count、(c) `snapshotStable=true` かつ `ambiguous=false` の安定した読取、(d) 手操作の数量とUI上の一致、である。さらに、方向（Cache→Holster または Holster→Cache）、対象row、数量、そして **slot値そのもの、またはこの操作にslotが存在しないことの明示的な証拠** を、別の手操作・UI観測で**独立に検証**しなければならない。複数rowが同時に変化した採取、複数eventを同一操作へ対応付けられない採取、`ambiguous=true` の採取、または方向/row/数量/slotが曖昧な採取は根拠として採用しない。

`snapshotStable` は連続するframework tickで読取値が安定したことだけを表す。before/after snapshotとdeltaは相関用であり、操作がその変化を引き起こした因果証明ではない。したがって `deltaMatchesExpected=unknown` と `acknowledgement=unconfirmed` は、callbackの戻り値でもサーバー承認でもない。これらが残る限り、row/count/slot引数または正規の承認経路は未確定である。

手動操作で得た署名は**再生を許可しない**。両方向の row/count/slot 引数と、正規のserver acknowledgementを独立した根拠で確定し、レビューした専用executorを別packetで追加するまで、callbackの呼出し・引数推測・server-backed countの書換えは禁止する。

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

将来のexecutorでは、転送前snapshotと転送後 `AgentMycItemBox.ItemBoxData` を比較して、対象rowのCache/Holster countが期待deltaになったことを確認する。この期待deltaは必要条件だが、server acknowledgementでも因果証明でもなく、それだけで次操作へ進んではならない。独立に検証済みの正規server acknowledgementも確立・観測してから次操作へ進む。

タイムアウト/不一致時はtransaction failureとして扱う。

## 残る不可避な確認

公開コードだけでは転送callback自体が見つからなかったため、この一点だけは最終的にゲームクライアントが発火する実callbackを取得する必要がある可能性が高い。

ただし、それまでは他のv1.1機能を止めない。Initializeについてもtarget planning、preflight、snapshot、rollback plan、UI、補給判定までは先行実装する。
