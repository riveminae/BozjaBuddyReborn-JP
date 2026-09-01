using System;
using System.Collections.Generic;
using BozjaBuddyReborn.Game;
using Dalamud.Game.ClientState.Conditions;
using ECommons.Automation.UIInput;
using EventType = ECommons.Automation.UIInput.EventType;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;

using BozjaBuddyReborn;

namespace BozjaBuddyReborn.Automation;

/// <summary>Where a sign-up attempt has got to. See <see cref="SignUpRunner"/>.</summary>
public enum SignUpPhase : byte
{
    Idle = 0,
    Opening = 1,
    Registering = 2,
    AwaitingSelection = 3,
    Commencing = 4,
    Done = 5,
}

/// <summary>
/// Signs this box up for a Critical Engagement through the Resistance Recruitment window.
/// Registration is remote; Commence is pressed only after selection. An optional commence gate
/// lets the controller hold the second click while survival supply is critically empty without
/// delaying the initial registration or the lottery.
/// </summary>
public sealed class SignUpRunner
{
    private const AgentId RecruitmentAgent = AgentId.MycBattleAreaInfo;
    private const long OpenTimeoutMs = 8_000;
    private const long SelectionTimeoutMs = 200_000;
    private const long CommenceTimeoutMs = 20_000;
    private const long StepMs = 250;
    private const long AttemptTimeoutMs = 300_000;
    private const long ClickSettleMs = 1500;
    private const long WindowSettleMs = 600;
    private const long LapsedConfirmMs = 3000;
    private const int MaxReopens = 8;

    private readonly Func<bool> _canCommence;

    private long _startedMs;
    private long _phaseSinceMs;
    private long _lastStepMs;
    private bool _showRequested;
    private int _clicks;
    private int _reopens;
    private long _clickSettleUntilMs;
    private long _lapsedSinceMs;
    private long _readySinceMs;
    private ushort _targetEventId;
    private ushort _preferredEventId;
    private string _loggedButtons = string.Empty;
    private bool _commenceHeldForSupply;

    public SignUpRunner(Func<bool>? canCommence = null)
    {
        // Failing open is intentional for callers that do not wire supply evaluation (for example
        // an operator-only sign-up action). The production Plugin wires a strict critical-supply
        // predicate. Exceptions inside that predicate are also handled fail-open below because an
        // unavailable inventory read is not evidence that the character has zero recovery.
        _canCommence = canCommence ?? (() => true);
    }

    public bool Active { get; private set; }
    public SignUpPhase Phase { get; private set; } = SignUpPhase.Idle;
    public string Status { get; private set; } = string.Empty;
    public IReadOnlyList<string> LastButtons { get; private set; } = [];

    public void Begin(ushort preferredEventId = 0)
    {
        Active = true;
        Phase = SignUpPhase.Opening;
        _startedMs = Environment.TickCount64;
        _phaseSinceMs = _startedMs;
        _lastStepMs = 0;
        _showRequested = false;
        _clicks = 0;
        _reopens = 0;
        _clickSettleUntilMs = 0;
        _lapsedSinceMs = 0;
        _readySinceMs = 0;
        _targetEventId = 0;
        _preferredEventId = preferredEventId;
        _loggedButtons = string.Empty;
        _commenceHeldForSupply = false;
        LastButtons = [];
        Status = Loc.T("Opening the Resistance Recruitment window.", "ボズヤファインダーを開いています。");
        Svc.Log.Information($"[BozjaBuddyReborn] Sign-up: begin (preferred CE #{_preferredEventId}).");
    }

    public void Cancel(string reason = "Sign-up cancelled.")
    {
        if (Active)
            Svc.Log.Information($"[BozjaBuddyReborn] Sign-up: end - {reason}");

        Active = false;
        Phase = SignUpPhase.Idle;
        Status = reason;
    }

    private void Advance(SignUpPhase phase, string status)
    {
        Phase = phase;
        _phaseSinceMs = Environment.TickCount64;
        _showRequested = false;
        Status = status;
        Svc.Log.Information($"[BozjaBuddyReborn] Sign-up: -> {phase}: {status}");
    }

    private long PhaseAgeMs => Environment.TickCount64 - _phaseSinceMs;

    private bool HoldCommenceForCriticalSupply()
    {
        bool allowed;
        try { allowed = _canCommence(); }
        catch (Exception ex)
        {
            // Inventory evaluation failure is unknown, not confirmed zero-supply. Keep the CE
            // deadline rather than blocking on a diagnostic failure.
            Svc.Log.Warning($"[BozjaBuddyReborn] CE commence supply gate failed open: {ex.Message}");
            allowed = true;
        }

        if (allowed)
        {
            if (_commenceHeldForSupply)
                Svc.Log.Information("[BozjaBuddyReborn] Critical survival supply recovered; CE Commence released.");
            _commenceHeldForSupply = false;
            return false;
        }

        Status = "生存用の回復手段が完全に枯渇しているため「戦闘突入」を保留しています。補給でき次第すぐ突入します。";
        if (!_commenceHeldForSupply)
        {
            _commenceHeldForSupply = true;
            Svc.Log.Warning("[BozjaBuddyReborn] Holding CE Commence because survival supply is critically empty.");
            DiagnosticsRecorder.Warning(Status, ControllerState.Blocked);
        }
        return true;
    }

    /// <summary>Drive one tick. Framework thread only - it touches agents and addons.</summary>
    public unsafe void Tick()
    {
        if (!Active || !Svc.Framework.IsInFrameworkUpdateThread)
            return;

        if (Svc.Objects.LocalPlayer == null ||
            Svc.Condition[ConditionFlag.BetweenAreas] ||
            Svc.Condition[ConditionFlag.BetweenAreas51] ||
            Svc.Condition[ConditionFlag.OccupiedInCutSceneEvent])
            return;

        if (!FieldState.InFieldZone)
        {
            Cancel($"Not in a Bozja field zone (in {BozjaZones.Name(Svc.ClientState.TerritoryType)}).");
            return;
        }

        var now = Environment.TickCount64;
        if (now - _startedMs > AttemptTimeoutMs)
        {
            Cancel($"Sign-up gave up after {AttemptTimeoutMs / 1000}s in phase {Phase}. Buttons: {ButtonList()}.");
            return;
        }

        if (now - _lastStepMs < StepMs)
            return;
        _lastStepMs = now;

        try
        {
            if (AnswerConfirmation())
                return;
            Step();
        }
        catch (Exception ex)
        {
            Svc.Log.Error(ex, "[BozjaBuddyReborn] Sign-up failed.");
            Cancel($"Sign-up failed: {ex.Message}");
        }
    }

    private static readonly string[] ConfirmationPhrases =
        ["critical engagement", "deployment", "deploy", "register", "commence", "クリティカルエンゲージメント", "戦闘突入"];

    private unsafe bool AnswerConfirmation()
    {
        if (_clicks == 0)
            return false;

        foreach (var yesno in ECommons.UIHelpers.AddonFinder.YesNo)
        {
            string text;
            try { text = yesno.Text ?? string.Empty; }
            catch { continue; }

            if (text.Length == 0)
                continue;

            var lowered = text.ToLowerInvariant();
            var mine = false;
            foreach (var phrase in ConfirmationPhrases)
            {
                if (lowered.Contains(phrase, StringComparison.Ordinal))
                {
                    mine = true;
                    break;
                }
            }

            if (!mine)
            {
                Svc.Log.Information($"[BozjaBuddyReborn] Sign-up: leaving an unrelated prompt alone: \"{text}\".");
                continue;
            }

            Svc.Log.Information($"[BozjaBuddyReborn] Sign-up: confirming \"{text}\".");
            yesno.RespectDisabledButtons = true;
            yesno.Yes();
            return true;
        }

        return false;
    }

    private unsafe void Step()
    {
        var agentModule = AgentModule.Instance();
        if (agentModule == null)
            return;

        var agent = (AgentMycBattleAreaInfo*)agentModule->GetAgentByInternalId(RecruitmentAgent);
        if (agent == null)
        {
            Cancel("The Resistance Recruitment agent is unavailable on this box.");
            return;
        }

        var iface = (AgentInterface*)agent;
        if (!iface->IsAgentActive())
        {
            if (!_showRequested)
            {
                _showRequested = true;
                iface->Show();
                Status = Loc.T("Opening the Resistance Recruitment window.", "ボズヤファインダーを開いています。");
                Svc.Log.Information("[BozjaBuddyReborn] Sign-up: asked the agent to show the window.");
                return;
            }

            if (PhaseAgeMs > OpenTimeoutMs)
            {
                if (Phase is SignUpPhase.AwaitingSelection && _reopens < MaxReopens)
                {
                    _reopens++;
                    _showRequested = false;
                    _phaseSinceMs = Environment.TickCount64;
                    Svc.Log.Information(
                        $"[BozjaBuddyReborn] Sign-up: window closed while awaiting the draw - reopening ({_reopens}).");
                    return;
                }

                Cancel("The Resistance Recruitment window did not open.");
            }
            return;
        }

        var addonId = iface->GetAddonId();
        var atkManager = RaptureAtkUnitManager.Instance();
        var addon = addonId == 0 || atkManager == null
            ? null
            : atkManager->GetAddonById((ushort)addonId);

        if (addon == null || !addon->IsVisible || addon->UldManager.LoadedState != AtkLoadState.Loaded)
        {
            _readySinceMs = 0;
            if (PhaseAgeMs > OpenTimeoutMs)
                Cancel("The Resistance Recruitment window did not finish loading.");
            else
                Status = Loc.T("Waiting for the Resistance Recruitment window.", "ボズヤファインダーの表示を待っています。");
            return;
        }

        if (_readySinceMs == 0)
            _readySinceMs = Environment.TickCount64;

        if (Environment.TickCount64 - _readySinceMs < WindowSettleMs)
        {
            Status = "参加リストの表示を待っています。";
            return;
        }

        var buttons = CollectButtons(addon);
        LastButtons = Describe(buttons);
        LogButtonsIfChanged();

        switch (Phase)
        {
            case SignUpPhase.Opening:
                Advance(SignUpPhase.Registering, Loc.T("Looking for the Register button.", "「参加希望」ボタンを探しています。"));
                goto case SignUpPhase.Registering;
            case SignUpPhase.Registering:
                StepRegister(addon, buttons);
                break;
            case SignUpPhase.AwaitingSelection:
                StepAwaitSelection(addon, buttons);
                break;
            case SignUpPhase.Commencing:
                StepCommencing(buttons);
                break;
        }
    }

    private bool Settling => Environment.TickCount64 < _clickSettleUntilMs;

    private unsafe void StepRegister(AtkUnitBase* addon, List<LabelledButton> buttons)
    {
        if (Settling)
        {
            Status = "ボタン表示の更新を待っています。";
            return;
        }

        if (Find(buttons, CommenceLabels) is { } commence)
        {
            if (HoldCommenceForCriticalSupply())
                return;
            if (Click(addon, commence, "Commence"))
                Advance(SignUpPhase.Commencing, Loc.T("Commencing - waiting to be deployed.", "「戦闘突入」を実行しました。転送を待っています。"));
            return;
        }

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
        }

        if (Find(buttons, RegisterLabels) is { } register)
        {
            var first = FirstRegisteringEventId();
            _targetEventId = _preferredEventId != 0 ? _preferredEventId : first;
            if (_preferredEventId != 0 && first != 0 && first != _preferredEventId)
                Svc.Log.Warning(
                    $"[BozjaBuddyReborn] Preferred CE #{_preferredEventId} differs from the first recruitment row #{first}; " +
                    "using the current button order for this test build. Capture callback/button diagnostics before tightening row targeting.");

            if (Click(addon, register, "Register"))
                Advance(SignUpPhase.AwaitingSelection, Loc.T("Registered - waiting for the draw.", "参加申請済み - 抽選結果を待っています。"));
            return;
        }

        if (PhaseAgeMs <= OpenTimeoutMs)
        {
            Status = Loc.T("Waiting for a Register button.", "「参加希望」ボタンを待っています。");
            return;
        }

        Cancel(AnyRegistering()
            ? $"An engagement is recruiting but no Register button was found. Buttons on the window: {ButtonList()}."
            : "No Critical Engagement is currently recruiting.");
    }

    private unsafe void StepAwaitSelection(AtkUnitBase* addon, List<LabelledButton> buttons)
    {
        if (Settling)
        {
            Status = "ボタン表示の更新を待っています。";
            return;
        }

        if (Find(buttons, CommenceLabels) is { } commence)
        {
            if (HoldCommenceForCriticalSupply())
                return;
            if (Click(addon, commence, "Commence"))
                Advance(SignUpPhase.Commencing, Loc.T("Commencing - waiting to be deployed.", "「戦闘突入」を実行しました。転送を待っています。"));
            return;
        }

        if (PhaseAgeMs > SelectionTimeoutMs)
        {
            Cancel("Registered, but no Commence appeared - the lottery did not pick this box.");
            return;
        }

        if (Find(buttons, WithdrawLabels) is null && Find(buttons, RegisterLabels) is not null)
        {
            if (_lapsedSinceMs == 0)
                _lapsedSinceMs = Environment.TickCount64;

            if (Environment.TickCount64 - _lapsedSinceMs > LapsedConfirmMs)
            {
                _lapsedSinceMs = 0;
                Advance(SignUpPhase.Registering, "Registration lapsed - trying again.");
            }
            return;
        }

        _lapsedSinceMs = 0;
        Status = $"参加申請済み - 抽選結果を待っています（{PhaseAgeMs / 1000}秒）。";
    }

    private void StepCommencing(List<LabelledButton> buttons)
    {
        if (Find(buttons, CommenceLabels) is null)
        {
            var id = CriticalEngagements.RegisteredEventId;
            Cancel(id is { } joined
                ? $"Commenced - deploying to engagement #{joined}."
                : "Commenced.");
            Phase = SignUpPhase.Done;
            return;
        }

        if (CriticalEngagements.Current(null) is { IsRunning: true } running)
        {
            Cancel($"Deployed to engagement #{running.EventId} - it is under way.");
            Phase = SignUpPhase.Done;
            return;
        }

        if (PhaseAgeMs > CommenceTimeoutMs)
        {
            Cancel($"Commence was pressed but the button is still there. Buttons: {ButtonList()}.");
            return;
        }

        Status = Loc.T("Commencing - waiting to be deployed.", "「戦闘突入」を実行しました。転送を待っています。");
    }

    private static readonly string[] RegisterLabels = ["register", "request deployment", "deploy", "参加希望"];
    private static readonly string[] WithdrawLabels = ["withdraw", "cancel deployment", "cancel"];
    private static readonly string[] CommenceLabels =
        ["commence", "enter", "join", "deploy now", "proceed", "begin", "start", "戦闘突入"];

    private readonly record struct LabelledButton(nint Button, string Text);

    private static LabelledButton? Find(List<LabelledButton> buttons, string[] labels)
    {
        foreach (var b in buttons)
        {
            var text = b.Text.Trim().ToLowerInvariant();
            if (text.Length == 0)
                continue;

            foreach (var label in labels)
            {
                var ascii = true;
                foreach (var ch in label)
                    if (ch > 0x7f) { ascii = false; break; }
                if (text == label || (ascii && text.StartsWith(label, StringComparison.Ordinal)))
                    return b;
            }
        }
        return null;
    }

    private static unsafe bool Click(AtkUnitBase* addon, LabelledButton button, string semantic)
    {
        try
        {
            var node = (AtkComponentButton*)button.Button;
            if (node == null || !node->IsEnabled)
                return false;

            var res = node->AtkComponentBase.OwnerNode;
            if (res == null)
                return false;

            foreach (var evt in res->AtkEventManager.EventList)
            {
                if (evt.State.EventType != EventType.ButtonClick)
                    continue;

                var data = new AtkEventData();
                addon->ReceiveEvent(evt.State.EventType, (int)evt.Param, evt.AtkEvent, &data);
                Svc.Log.Information($"[BozjaBuddyReborn] Sign-up: clicked {semantic} button \"{button.Text}\".");
                return true;
            }
        }
        catch (Exception ex)
        {
            Svc.Log.Warning($"[BozjaBuddyReborn] Sign-up: {semantic} click failed: {ex.Message}");
        }
        return false;
    }

    private unsafe List<LabelledButton> CollectButtons(AtkUnitBase* addon)
    {
        var result = new List<LabelledButton>();
        if (addon->UldManager.NodeList == null)
            return result;

        for (var i = 0; i < addon->UldManager.NodeListCount; i++)
        {
            var node = addon->UldManager.NodeList[i];
            if (node == null || node->Type < NodeType.Component)
                continue;

            try
            {
                var componentNode = (AtkComponentNode*)node;
                var component = componentNode->Component;
                if (component == null || component->GetComponentType() != ComponentType.Button)
                    continue;

                var button = (AtkComponentButton*)component;
                var text = button->ButtonTextNode?.NodeText.ToString() ?? string.Empty;
                if (text.Length > 0)
                    result.Add(new LabelledButton((nint)button, text));
            }
            catch { }
        }
        return result;
    }

    private static IReadOnlyList<string> Describe(List<LabelledButton> buttons)
    {
        var result = new List<string>(buttons.Count);
        foreach (var b in buttons)
            result.Add(b.Text);
        return result;
    }

    private string ButtonList() => LastButtons.Count == 0 ? "none" : string.Join(", ", LastButtons);

    private void LogButtonsIfChanged()
    {
        var current = ButtonList();
        if (string.Equals(current, _loggedButtons, StringComparison.Ordinal))
            return;
        _loggedButtons = current;
        Svc.Log.Information($"[BozjaBuddyReborn] Sign-up: visible buttons = [{current}].");
    }

    private static bool AnyRegistering()
    {
        try
        {
            var catalog = CriticalEngagements.Read(null);
            foreach (var ce in catalog)
                if (ce.State == DynamicEventState.Register)
                    return true;
        }
        catch { }
        return false;
    }

    private static ushort FirstRegisteringEventId()
    {
        try
        {
            var catalog = CriticalEngagements.Read(null);
            ushort best = 0;
            foreach (var ce in catalog)
            {
                if (ce.State != DynamicEventState.Register)
                    continue;
                if (best == 0 || ce.EventId < best)
                    best = ce.EventId;
            }
            return best;
        }
        catch { return 0; }
    }
}
