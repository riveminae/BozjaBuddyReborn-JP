from __future__ import annotations
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
P = ROOT / "Windows/ConfigWindow.cs"
text = P.read_text(encoding="utf-8-sig")

replacements = [
    (
        '''        ImGui.TextColored(Yellow,\n            "Off by default, and worth reading before you turn it on.\\n" +\n            "\\n" +\n            "The two kinds of holster entry do not cost the same. ITEMS - every Essence, the potion,\\n" +\n            "ether and medi kits, Dynamis Dice, Reraiser, Lodestone, Light Curtain, Resistance Elixir\\n" +\n            "- are consumed the moment the box above uses them, and always have been. ACTIONS - Lost\\n" +\n            "Cure, Lost Font of Power, the Banners, and so on - are only LOADED into a duty slot by\\n" +\n            "that same call, and until this build nothing pressed the slot, so ticking the box above\\n" +\n            "has never spent an action charge however long it was left on.\\n" +\n            "\\n" +\n            "This switch is the missing press. Turning it on means every action you tick below is\\n" +\n            "actually fired, roughly once per cooldown window, for as long as you are in combat in an\\n" +\n            "engagement - so the toggle above starts costing farmed charges it did not cost before.\\n" +\n            "Leaving it off keeps that exactly as it was: items are used, actions are left alone.\\n" +\n            "\\n" +\n            "It drives duty slot 1 when it has to load, so a loadout parked there can be replaced. An\\n" +\n            "action already sitting in either slot is pressed where it stands and nothing is moved.");''',
        '''        ImGui.TextColored(Yellow,\n            "初期値はOFFです。有効にする前に以下を確認してください。\\n" +\n            "\\n" +\n            "Holsterには消費方法が異なる2種類があります。アイテム型（Essence、各種Potion/Ether/\\n" +\n            "Medi Kit、Dynamis Dice、Reraiser、Lodestone、Light Curtain、Resistance Elixir等）は、\\n" +\n            "Holsterから使用した時点でそのまま消費されます。アクション型（Lost Cure、\\n" +\n            "Lost Font of Power、各種Banner等）は、HolsterからDuty Action枠へロードしただけでは\\n" +\n            "チャージを消費しません。\\n" +\n            "\\n" +\n            "このスイッチをONにすると、下で選択したアクション型もDuty Actionとして実際に発動し、\\n" +\n            "戦闘中は設定した使用間隔ごとにチャージを消費します。OFFのままなら従来どおり、\\n" +\n            "アイテム型だけを使用し、アクション型は発動しません。\\n" +\n            "\\n" +\n            "ロードが必要な場合はDuty Action 1を使用するため、そこに置いていたロードアウトが\\n" +\n            "置き換わることがあります。既にどちらかの枠にあるアクションはその枠から発動します。");''',
    ),
    (
        '''            "自動使用を許可する項目を選択します。「item」はHolsterから直接消費される種類です。\\n" +''',
        '''            "自動使用を許可する項目を選択します。「アイテム」はHolsterから直接消費される種類です。\\n" +''',
    ),
    ('var kind = entry.IsItem ? ", item" : string.Empty;', 'var kind = entry.IsItem ? ", アイテム" : string.Empty;'),
    (
        '''        ImGui.TextColored(Grey,\n            "Someone with no buff at all is always served first. This is the second pass: top up the\\n" +\n            "most-expired member once they drop below this much of the total. Lost Bravery runs 600s,\\n" +\n            "so 20% is the last two minutes. The totals are read out of the game's own tooltip text -\\n" +\n            "an action whose duration is not in the data is never topped up, only given to people who\\n" +\n            "have nothing.");''',
        '''        ImGui.TextColored(Grey,\n            "バフが付いていないメンバーを最優先します。その後、残り効果時間がこの割合を下回った\\n" +\n            "メンバーを、残り時間が短い順に更新します。Lost Braveryは600秒なので、20%なら\\n" +\n            "残り2分未満が更新対象です。総効果時間はゲーム内Tooltipから取得します。\\n" +\n            "効果時間を取得できないアクションは自動更新せず、未付与のメンバーにだけ使用します。");''',
    ),
    (
        '''        ImGui.TextColored(Grey,\n            "Healing goes to the LOWEST-HP member first, re-decided before every cast. This floor is\\n" +\n            "what stops \\"lowest\\" meaning \\"whoever is at 99%\\" - there is always a lowest.");''',
        '''        ImGui.TextColored(Grey,\n            "回復は毎回の使用直前に判定し、HP割合が最も低いメンバーを優先します。\\n" +\n            "この閾値を設けることで、全員がほぼ満タンのときに不要な回復を消費しません。");''',
    ),
    (
        '''        ImGui.TextColored(Grey,\n            "Slot 2 by default, because auto-use drives slot 1 and two things reloading one slot\\n" +\n            "underneath each other would spend the whole fight fighting. An action already sitting in\\n" +\n            "either slot is used where it is, so this only matters when something has to be loaded.");''',
        '''        ImGui.TextColored(Grey,\n            "初期値はDuty Action 2です。通常の自動使用がDuty Action 1を使うため、同じ枠を\\n" +\n            "互いにロードし直して競合するのを避けます。既にどちらかの枠にあるアクションは\\n" +\n            "その枠から使用するため、この設定は新しくロードする場合だけ影響します。");''',
    ),
]

changed = 0
for old, new in replacements:
    if new in text:
        continue
    if old not in text:
        raise RuntimeError(f"ConfigWindow localization anchor missing: {old[:120]!r}")
    text = text.replace(old, new, 1)
    changed += 1

P.write_text(text, encoding="utf-8")
print(f"Windows/ConfigWindow.cs: translated {changed} remaining Lost Action settings group(s)")
