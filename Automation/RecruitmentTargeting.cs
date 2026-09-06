using System;
using System.Collections.Generic;

namespace BozjaBuddyReborn.Automation;

internal enum RecruitmentAction { Register, Commence }
internal readonly record struct RecruitmentEvent(ushort Id, string Name, byte State);
internal readonly record struct LabelledButton(nint Button, string Text, string EventName);

/// <summary>Matches the requested event to its own row, never to list position.</summary>
internal static class RecruitmentTargeting
{
    internal static LabelledButton? Find(IReadOnlyList<RecruitmentEvent> events,
        IReadOnlyList<LabelledButton> buttons, ushort eventId, RecruitmentAction action)
    {
        if (eventId == 0) return null;
        RecruitmentEvent? target = null;
        foreach (var entry in events)
        {
            if (entry.Id != eventId) continue;
            if (target != null) return null;
            target = entry;
        }
        if (target is not { } selected || string.IsNullOrWhiteSpace(selected.Name)
            || selected.State != (action == RecruitmentAction.Register ? 1 : 2)) return null;

        var name = selected.Name.Trim();
        foreach (var entry in events)
            if (entry.Id != eventId && string.Equals(entry.Name.Trim(), name, StringComparison.Ordinal))
                return null;

        LabelledButton? result = null;
        foreach (var button in buttons)
        {
            if (button.Button == 0 || !string.Equals(button.EventName.Trim(), name, StringComparison.Ordinal)
                || !Matches(button.Text, action)) continue;
            if (result != null) return null;
            result = button;
        }
        return result;
    }

    private static bool Matches(string text, RecruitmentAction action)
    {
        // Exact labels only: "deploy now" must never be treated as "deploy" (Register).
        var label = text.Trim().ToLowerInvariant();
        return action == RecruitmentAction.Register
            ? label is "参加希望" or "register" or "request deployment" or "deploy"
            : label is "戦闘突入" or "commence" or "enter" or "join" or "deploy now" or "proceed" or "begin" or "start";
    }
}
