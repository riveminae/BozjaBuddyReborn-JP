from __future__ import annotations
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
P = ROOT / "Automation/SignUpRunner.cs"
text = P.read_text(encoding="utf-8-sig")

SAFE_MARKER = "using var eventData = EventData.ForNormalTarget(ownerNode, addon);"
if SAFE_MARKER in text and "if (ce.IsJoinable)" in text and "static void Walk(AtkUldManager* mgr" in text:
    print("Automation/SignUpRunner.cs: API15-safe recruitment handling already applied")
    raise SystemExit(0)

# Replace the whole click method. The previous direct rewrite regressed to fields removed from the
# pinned API15 ClientStructs (AtkEventManager.EventList / EventType.ButtonClick) and, more
# importantly, bypassed the four-argument ReceiveEvent path that had already been hardened after a
# real client crash. Keep the proven component-event invocation from the last green baseline.
start = text.find("    private unsafe bool Click(AtkUnitBase* addon, LabelledButton")
if start < 0:
    start = text.find("    private static unsafe bool Click(AtkUnitBase* addon, LabelledButton")
end = text.find("    private ", start + 20)
if start < 0 or end < 0:
    raise RuntimeError("SignUpRunner Click method bounds not found")

safe_click = r'''    private unsafe bool Click(AtkUnitBase* addon, LabelledButton target, string what)
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

'''
text = text[:start] + safe_click + text[end:]

# Replace CollectButtons with the recursive API15-safe component walk. The simplified rewrite used
# null-conditional syntax on a native pointer and only inspected top-level nodes, missing row
# buttons living in nested component managers.
start = text.find("    private static unsafe List<LabelledButton> CollectButtons(AtkUnitBase* addon)")
end = text.find("    private static IReadOnlyList<string> Describe", start)
if end < 0:
    end = text.find("    private static List<string> Describe", start)
if start < 0 or end < 0:
    raise RuntimeError("SignUpRunner CollectButtons method bounds not found")

safe_collect = r'''    private static unsafe List<LabelledButton> CollectButtons(AtkUnitBase* addon)
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

'''
text = text[:start] + safe_collect + text[end:]

# The simplified rewrite referenced DynamicEventState without importing the FFXIVClientStructs
# enum. More importantly the established CE snapshot already exposes the exact semantic needed.
old_any = r'''    private static bool AnyRegistering()
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
'''
new_any = r'''    private static bool AnyRegistering() => FirstRegisteringEventId() != 0;

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
'''
if old_any not in text:
    raise RuntimeError("SignUpRunner registration helper anchor not found")
text = text.replace(old_any, new_any, 1)

P.write_text(text, encoding="utf-8")
print("Automation/SignUpRunner.cs: restored API15-safe recruitment handling")
