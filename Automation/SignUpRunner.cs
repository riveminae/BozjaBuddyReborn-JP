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

    private unsafe bool Click(AtkUnitBase* addon, LabelledButton target, string what)
    {
        if (addon == null)
            return false;

        var button = (AtkComponentButton*)target.Button;
        if (button == null)
            return false;

        var ownerNode = button->AtkComponentBase.OwnerNode;
        if (ownerNode == null)
        {
            Svc.Log.Warning($"[BozjaBuddyReborn] Sign-up: \"{target.Text}\" has no owner node; not clicking.");
            return false;
        }

        // Use the event the game attached to the button. A half-built list row can have a visible
        // button with no event yet; refusing that frame is safer than inventing a callback.
        var evt = ownerNode->AtkResNode.AtkEventManager.Event;
        if (evt == null)
        {
            Svc.Log.Warning($"[BozjaBuddyReborn] Sign-up: \"{target.Text}\" has no attached event yet; not clicking.");
            return false;
        }

        // Prefer a click event in the bounded chain. API15 exposes AtkEventType here; the
        // ECommons UIInput EventType alias is only used at the final invocation boundary.
        var chosen = evt;
        var node = evt;
        for (var i = 0; i < 16 && node != null; i++)
        {
            var t = node->State.EventType;
            if (t is AtkEventType.MouseClick or AtkEventType.ButtonClick)
            {
                chosen = node;
                break;
            }
            node = node->NextEvent;
        }

        // Register -> Withdraw -> Commence is one physical button whose label changes after the
        // click. Do not permit a second click until the UI has caught up or Register can turn into
        // an accidental Withdraw. _clicks also scopes confirmation handling to prompts we caused.
        _clicks++;
        _clickSettleUntilMs = Environment.TickCount64 + ClickSettleMs;
        Svc.Log.Information(
            $"[BozjaBuddyReborn] Sign-up: clicking \"{target.Text}\" as {what} " +
            $"(click {_clicks}, event type {chosen->State.EventType}, param {chosen->Param}).");

        // MYCBattleAreaInfo dereferences the input-data path; the convenience ReceiveEvent call
        // with a null fourth argument has previously crashed the client. Recreate a real component
        // click with concrete event/input data instead.
        using var eventData = EventData.ForNormalTarget(ownerNode, addon);
        using var inputData = InputData.Empty();
        ClickHelper.InvokeReceiveEvent(
            &addon->AtkEventListener,
            (EventType)chosen->State.EventType,
            chosen->Param,
            eventData,
            inputData);
        return true;
    }

    private static unsafe List<LabelledButton> CollectButtons(AtkUnitBase* addon)
    {
        var found = new List<LabelledButton>();
        if (addon == null)
            return found;

        var mgr = &addon->UldManager;
        Walk(mgr, found, 0);
        return found;

        static void Walk(AtkUldManager* mgr, List<LabelledButton> found, int depth)
        {
            if (mgr == null || depth > 6 || found.Count > 64)
                return;
            if (mgr->LoadedState != AtkLoadState.Loaded || mgr->NodeList == null)
                return;

            var count = mgr->NodeListCount;
            for (var i = 0; i < count; i++)
            {
                var node = mgr->NodeList[i];
                if (node == null || !node->IsVisible())
                    continue;
                if ((ushort)node->Type < 1000)
                    continue;

                var component = ((AtkComponentNode*)node)->Component;
                if (component == null)
                    continue;

                if (component->GetComponentType() == ComponentType.Button)
                {
                    var button = (AtkComponentButton*)component;
                    if (button->IsEnabled)
                        found.Add(new LabelledButton((nint)button, ReadText(button)));
                }

                Walk(&component->UldManager, found, depth + 1);
            }
        }

        static string ReadText(AtkComponentButton* button)
        {
            try
            {
                var textNode = button->ButtonTextNode;
                return textNode == null ? string.Empty : textNode->NodeText.ToString();
            }
            catch
            {
                return string.Empty;
            }
        }
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

    private static bool AnyRegistering() => FirstRegisteringEventId() != 0;

    private static ushort FirstRegisteringEventId()
    {
        try
        {
            ushort best = 0;
            foreach (var ce in CriticalEngagements.Read(null))
            {
                if (!ce.IsJoinable)
                    continue;
                if (best == 0 || ce.EventId < best)
                    best = ce.EventId;
            }
            return best;
        }
        catch { return 0; }
    }
}
