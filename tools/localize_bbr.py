#!/usr/bin/env python3
from pathlib import Path
import sys

root = Path(sys.argv[1]).resolve()
p = root / 'plugins/BozjaBuddyReborn'


def rw(rel, replacements):
    f = p / rel
    s = f.read_text(encoding='utf-8-sig')
    for old, new in replacements:
        if old not in s:
            print(f'[WARN] {rel}: anchor not found: {old[:70]!r}')
            continue
        s = s.replace(old, new)
    f.write_text(s, encoding='utf-8', newline='\n')

# Portable build + fork version.
rw('BozjaBuddyReborn.csproj', [
    ('<Version>1.0.28.0</Version>', '<Version>1.0.28.1</Version>'),
    ('<AssemblyVersion>1.0.28.0</AssemblyVersion>', '<AssemblyVersion>1.0.28.1</AssemblyVersion>'),
    ('<FileVersion>1.0.28.0</FileVersion>', '<FileVersion>1.0.28.1</FileVersion>'),
    ('    <!-- ECommons supplies Svc, NeoTaskManager, EzThrottler and the chat/command helpers.\n'
     '         Points at the local clone the other plugins on this machine build against. -->\n'
     '    <ProjectReference Include="..\\..\\..\\ZodiacRedone\\ECommons\\ECommons\\ECommons.csproj" />',
     '    <!-- JP fork: public NuGet package so a clean checkout can build without the upstream author\'s local ECommons checkout. -->\n'
     '    <PackageReference Include="ECommons" Version="3.2.1.18" />'),
])

# Tiny EN/JA localization helper.
(p / 'Localization.cs').write_text(r'''using System;
using BozjaBuddyReborn.Automation;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;

namespace BozjaBuddyReborn;

public static class Loc
{
    public static bool Ja => string.Equals(Svc.PluginInterface.UiLanguage, "ja", StringComparison.OrdinalIgnoreCase);
    public static string T(string en, string ja) => Ja ? ja : en;

    public static string Controller(ControllerState s) => !Ja ? s.ToString() : s switch
    {
        ControllerState.Idle => "待機", ControllerState.Blocked => "停止中",
        ControllerState.Selecting => "行き先選択中", ControllerState.Travelling => "移動中",
        ControllerState.Holding => "待機位置", ControllerState.Engaged => "戦闘中", _ => s.ToString(),
    };

    public static string Phase(SignUpPhase s) => !Ja ? s.ToString() : s switch
    {
        SignUpPhase.Idle => "待機", SignUpPhase.Opening => "ボズヤファインダーを開いています",
        SignUpPhase.Registering => "参加申請中", SignUpPhase.AwaitingSelection => "抽選待ち",
        SignUpPhase.Commencing => "戦闘突入中", SignUpPhase.Done => "完了", _ => s.ToString(),
    };

    public static string CeState(DynamicEventState s) => !Ja ? s.ToString() : s switch
    {
        DynamicEventState.Inactive => "終了", DynamicEventState.Register => "参加募集中",
        DynamicEventState.Warmup => "開戦準備中", DynamicEventState.Battle => "戦闘中", _ => s.ToString(),
    };
}
''', encoding='utf-8', newline='\n')

# CE registration localization. JP labels are exact-match only to avoid accidental cancel/withdraw clicks.
rw('Automation/SignUpRunner.cs', [
    ('namespace BozjaBuddyReborn.Automation;', 'using BozjaBuddyReborn;\n\nnamespace BozjaBuddyReborn.Automation;'),
    ('["critical engagement", "deployment", "deploy", "register", "commence"]',
     '["critical engagement", "deployment", "deploy", "register", "commence", "クリティカルエンゲージメント", "戦闘突入"]'),
    ('private static readonly string[] RegisterLabels = ["register", "request deployment", "deploy"];',
     'private static readonly string[] RegisterLabels = ["register", "request deployment", "deploy", "参加希望"];'),
    ('["commence", "enter", "join", "deploy now", "proceed", "begin", "start"]',
     '["commence", "enter", "join", "deploy now", "proceed", "begin", "start", "戦闘突入"]'),
    ('            foreach (var label in labels)\n                if (text == label || text.StartsWith(label, StringComparison.Ordinal))\n                    return b;',
     '''            foreach (var label in labels)
            {
                var ascii = true;
                foreach (var ch in label)
                    if (ch > 0x7f) { ascii = false; break; }
                if (text == label || (ascii && text.StartsWith(label, StringComparison.Ordinal)))
                    return b;
            }'''),
    ('        // Withdraw means the button has already been pressed once: we are in the lottery.\n'
     '        if (Find(buttons, WithdrawLabels) is not null)\n'
     '        {\n'
     '            Advance(SignUpPhase.AwaitingSelection, "Already registered - waiting for the draw.");\n'
     '            return;\n'
     '        }',
     '''        // Language-independent registered-state check. This avoids guessing the JP Withdraw label.
        if (CriticalEngagements.RegisteredEventId is { } registeredEventId && registeredEventId != 0)
        {
            _targetEventId = registeredEventId;
            Advance(SignUpPhase.AwaitingSelection, Loc.T("Already registered - waiting for the draw.", "参加申請済み - 抽選結果を待っています。"));
            return;
        }
        if (Find(buttons, WithdrawLabels) is not null)
        {
            Advance(SignUpPhase.AwaitingSelection, Loc.T("Already registered - waiting for the draw.", "参加申請済み - 抽選結果を待っています。"));
            return;
        }'''),
    ('Status = "Opening the Resistance Recruitment window.";', 'Status = Loc.T("Opening the Resistance Recruitment window.", "ボズヤファインダーを開いています。");'),
    ('Status = "Waiting for the Resistance Recruitment window.";', 'Status = Loc.T("Waiting for the Resistance Recruitment window.", "ボズヤファインダーの表示を待っています。");'),
    ('Advance(SignUpPhase.Registering, "Looking for the Register button.");', 'Advance(SignUpPhase.Registering, Loc.T("Looking for the Register button.", "「参加希望」ボタンを探しています。"));'),
    ('Status = "Waiting for a Register button.";', 'Status = Loc.T("Waiting for a Register button.", "「参加希望」ボタンを待っています。");'),
    ('Advance(SignUpPhase.AwaitingSelection, "Registered - waiting for the draw.");', 'Advance(SignUpPhase.AwaitingSelection, Loc.T("Registered - waiting for the draw.", "参加申請済み - 抽選結果を待っています。"));'),
    ('Advance(SignUpPhase.Commencing, "Commencing - waiting to be deployed.");', 'Advance(SignUpPhase.Commencing, Loc.T("Commencing - waiting to be deployed.", "「戦闘突入」を実行しました。転送を待っています。"));'),
    ('Status = "Commencing - waiting to be deployed.";', 'Status = Loc.T("Commencing - waiting to be deployed.", "「戦闘突入」を実行しました。転送を待っています。");'),
])

# Main window: everyday controls and tables.
rw('Windows/MainWindow.cs', [
    ('namespace BozjaBuddyReborn.Windows;', 'using BozjaBuddyReborn;\n\nnamespace BozjaBuddyReborn.Windows;'),
    ('ImGui.BeginTabItem("Engagements")', 'ImGui.BeginTabItem(Loc.T("Engagements", "CE / スカーミッシュ"))'),
    ('ImGui.BeginTabItem("Multibox")', 'ImGui.BeginTabItem(Loc.T("Multibox", "マルチボックス"))'),
    ('ImGui.BeginTabItem("Dependencies")', 'ImGui.BeginTabItem(Loc.T("Dependencies", "依存関係"))'),
    ('ImGui.Button(running ? "Stop" : "Start", new Vector2(90, 0))', 'ImGui.Button(running ? Loc.T("Stop", "停止") : Loc.T("Start", "開始"), new Vector2(90, 0))'),
    ('ImGui.TextColored(stateColour, _controller.State.ToString());', 'ImGui.TextColored(stateColour, Loc.Controller(_controller.State));'),
    ('ImGui.TextUnformatted("Work zone:");', 'ImGui.TextUnformatted(Loc.T("Work zone:", "周回エリア:"));'),
    ('            "Anywhere",', '            Loc.T("Anywhere", "指定なし"),'),
    ('ImGui.TextUnformatted($"Resistance Rank {rank}");', 'ImGui.TextUnformatted(Loc.Ja ? $"レジスタンスランク {rank}" : $"Resistance Rank {rank}");'),
    ('ImGui.TableSetupColumn("Engagement",', 'ImGui.TableSetupColumn(Loc.T("Engagement", "クリティカルエンゲージメント"),'),
    ('ImGui.TableSetupColumn("State",', 'ImGui.TableSetupColumn(Loc.T("State", "状態"),'),
    ('ImGui.TableSetupColumn("Time",', 'ImGui.TableSetupColumn(Loc.T("Time", "残り時間"),'),
    ('ImGui.TableSetupColumn("Players",', 'ImGui.TableSetupColumn(Loc.T("Players", "人数"),'),
    ('ImGui.TableSetupColumn("Progress",', 'ImGui.TableSetupColumn(Loc.T("Progress", "進行度"),'),
    ('ImGui.TableSetupColumn("Skip",', 'ImGui.TableSetupColumn(Loc.T("Skip", "除外"),'),
    ('ImGui.TextColored(nameColour, ce.StateText);', 'ImGui.TextColored(nameColour, Loc.CeState(ce.State));'),
    ('ImGui.Checkbox("Coordinate with other game clients on this PC", ref enabled)', 'ImGui.Checkbox(Loc.T("Coordinate with other game clients on this PC", "このPC上の複数クライアントを連携する"), ref enabled)'),
    ('ImGui.Checkbox("This client is the host", ref isHost)', 'ImGui.Checkbox(Loc.T("This client is the host", "このクライアントをホストにする"), ref isHost)'),
    ('if (ImGui.Button("Start all"))', 'if (ImGui.Button(Loc.T("Start all", "全クライアント開始")))'),
    ('if (ImGui.Button("Stop all"))', 'if (ImGui.Button(Loc.T("Stop all", "全クライアント停止")))'),
    ('if (ImGui.Button("Open the multiboxer panel"))', 'if (ImGui.Button(Loc.T("Open the multiboxer panel", "マルチボックス操作画面を開く")))'),
    ('if (ImGui.Button("Open the group duty-action hotbar"))', 'if (ImGui.Button(Loc.T("Open the group duty-action hotbar", "グループDuty Actionバーを開く")))'),
])

# Settings: primary controls. Detailed diagnostics stay English for upstream troubleshooting.
rw('Windows/ConfigWindow.cs', [
    ('namespace BozjaBuddyReborn.Windows;', 'using BozjaBuddyReborn;\n\nnamespace BozjaBuddyReborn.Windows;'),
    ('ImGui.BeginTabItem("Combat")', 'ImGui.BeginTabItem(Loc.T("Combat", "戦闘"))'),
    ('ImGui.BeginTabItem("Engagements")', 'ImGui.BeginTabItem(Loc.T("Engagements", "CE / スカーミッシュ"))'),
    ('ImGui.BeginTabItem("Movement")', 'ImGui.BeginTabItem(Loc.T("Movement", "移動"))'),
    ('ImGui.BeginTabItem("Zones")', 'ImGui.BeginTabItem(Loc.T("Zones", "エリア"))'),
    ('ImGui.BeginTabItem("Lost Actions")', 'ImGui.BeginTabItem(Loc.T("Lost Actions", "ロストアクション"))'),
    ('ImGui.Checkbox("BossMod: AoE avoidance", ref avoid)', 'ImGui.Checkbox(Loc.T("BossMod: AoE avoidance", "BossMod: AoE回避"), ref avoid)'),
    ('ImGui.Checkbox("Rotation Solver Reborn: rotation", ref rsr)', 'ImGui.Checkbox(Loc.T("Rotation Solver Reborn: rotation", "Rotation Solver Reborn: 戦闘ローテーション"), ref rsr)'),
    ('ImGui.Checkbox("Join Critical Engagements", ref ces)', 'ImGui.Checkbox(Loc.T("Join Critical Engagements", "クリティカルエンゲージメントに参加する"), ref ces)'),
    ('ImGui.Checkbox("Farm skirmish FATEs when no engagement is open", ref fates)', 'ImGui.Checkbox(Loc.T("Farm skirmish FATEs when no engagement is open", "CEがない間はスカーミッシュを周回する"), ref fates)'),
    ('ImGui.RadioButton("Keep running (never attack)", keepRunning)', 'ImGui.RadioButton(Loc.T("Keep running (never attack)", "そのまま走る（反撃しない）"), keepRunning)'),
    ('ImGui.RadioButton("Stop and fight back", !keepRunning)', 'ImGui.RadioButton(Loc.T("Stop and fight back", "停止して反撃する"), !keepRunning)'),
    ('ImGui.Checkbox("Enter duels (1v1)", ref duels)', 'ImGui.Checkbox(Loc.T("Enter duels (1v1)", "一騎打ちに参加する"), ref duels)'),
    ('ImGui.Checkbox("Enter large-scale battles", ref large)', 'ImGui.Checkbox(Loc.T("Enter large-scale battles", "大規模戦闘に参加する"), ref large)'),
    ('ImGui.Checkbox("Summon a mount for long travel", ref mount)', 'ImGui.Checkbox(Loc.T("Summon a mount for long travel", "長距離移動ではマウントを使用する"), ref mount)'),
    ('ImGui.Checkbox("Route around enemy aggro while travelling", ref avoid)', 'ImGui.Checkbox(Loc.T("Route around enemy aggro while travelling", "移動中に敵の感知範囲を迂回する"), ref avoid)'),
    ('ImGui.Checkbox("Wait at a staging point when nothing is up", ref idle)', 'ImGui.Checkbox(Loc.T("Wait at a staging point when nothing is up", "対象がないときは待機地点へ移動する"), ref idle)'),
    ('ImGui.TextColored(Grey, "Diagnostics");', 'ImGui.TextColored(Grey, Loc.T("Diagnostics", "診断"));'),
    ('ImGui.Checkbox("Automatically use Lost Actions in combat", ref auto)', 'ImGui.Checkbox(Loc.T("Automatically use Lost Actions in combat", "戦闘中にロストアクションを自動使用する"), ref auto)'),
    ('ImGui.TextUnformatted("Party support");', 'ImGui.TextUnformatted(Loc.T("Party support", "パーティ支援"));'),
])

# Multibox / duty-action / relic windows: primary navigation and action buttons.
rw('Windows/MultiboxerWindow.cs', [
    ('namespace BozjaBuddyReborn.Windows;', 'using BozjaBuddyReborn;\n\nnamespace BozjaBuddyReborn.Windows;'),
    ('ImGui.BeginTabItem("Boxes")', 'ImGui.BeginTabItem(Loc.T("Boxes", "クライアント"))'),
    ('ImGui.BeginTabItem("Loadouts")', 'ImGui.BeginTabItem(Loc.T("Loadouts", "ロードアウト"))'),
    ('ImGui.BeginTabItem("Errands")', 'ImGui.BeginTabItem(Loc.T("Errands", "移動・操作"))'),
    ('ImGui.Button("Sign up ALL for the engagement", new Vector2(280, 0))', 'ImGui.Button(Loc.T("Sign up ALL for the engagement", "CEに全クライアントで参加申請"), new Vector2(280, 0))'),
    ('ImGui.Button("Sign up this box")', 'ImGui.Button(Loc.T("Sign up this box", "このクライアントだけ参加申請"))'),
    ('$"   phase: {_signUps.Phase}"', 'Loc.Ja ? $"   フェーズ: {Loc.Phase(_signUps.Phase)}" : $"   phase: {_signUps.Phase}"'),
    ('ImGui.Button("Start all")', 'ImGui.Button(Loc.T("Start all", "全クライアント開始"))'),
    ('ImGui.Button("Stop all")', 'ImGui.Button(Loc.T("Stop all", "全クライアント停止"))'),
    ('ImGui.SmallButton("Sign up")', 'ImGui.SmallButton(Loc.T("Sign up", "CE参加申請"))'),
    ('ImGui.SmallButton(_editing == i ? "Done" : "Edit")', 'ImGui.SmallButton(_editing == i ? Loc.T("Done", "完了") : Loc.T("Edit", "編集"))'),
    ('ImGui.SmallButton("Delete")', 'ImGui.SmallButton(Loc.T("Delete", "削除"))'),
    ('ImGui.Button("Add")', 'ImGui.Button(Loc.T("Add", "追加"))'),
])

rw('Windows/DutyActionWindow.cs', [
    ('namespace BozjaBuddyReborn.Windows;', 'using BozjaBuddyReborn;\n\nnamespace BozjaBuddyReborn.Windows;'),
    ('ImGui.TextColored(Yellow, "No duty actions right now.");', 'ImGui.TextColored(Yellow, Loc.T("No duty actions right now.", "現在使用できるDuty Actionはありません。"));'),
    ('peer.IsSelf ? $"{peer.Name} (you)" : peer.Name', 'peer.IsSelf ? (Loc.Ja ? $"{peer.Name}（自分）" : $"{peer.Name} (you)") : peer.Name'),
])

rw('Windows/RelicWindow.cs', [
    ('namespace BozjaBuddyReborn.Windows;', 'using BozjaBuddyReborn;\n\nnamespace BozjaBuddyReborn.Windows;'),
    ('ImGui.Checkbox("Show every stage", ref _showAllStages);', 'ImGui.Checkbox(Loc.T("Show every stage", "全段階を表示"), ref _showAllStages);'),
    ('ImGui.TableSetupColumn("Material",', 'ImGui.TableSetupColumn(Loc.T("Material", "素材"),'),
    ('ImGui.TableSetupColumn("Held",', 'ImGui.TableSetupColumn(Loc.T("Held", "所持数"),'),
    ('ImGui.TableSetupColumn("Farm",', 'ImGui.TableSetupColumn(Loc.T("Farm", "周回"),'),
    ('ImGui.TableSetupColumn("Where",', 'ImGui.TableSetupColumn(Loc.T("Where", "入手場所"),'),
])

# Manifest: keep InternalName so configuration/IPC identity remains compatible.
rw('BozjaBuddyReborn.json', [
    ('"Name": "Bozja Buddy Reborn"', '"Name": "Bozja Buddy Reborn JP"'),
    ('"Author": "OmegaJackie"', '"Author": "OmegaJackie / JP localization by riveminae"'),
    ('"Punchline": "Orchestrate Critical Engagements on the Bozjan Southern Front and Zadnor"',
     '"Punchline": "南方ボズヤ戦線 / ザトゥノルのCE・スカーミッシュ周回を自動化"'),
])

print('Bozja Buddy Reborn JP localization applied.')
