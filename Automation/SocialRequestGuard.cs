using System;
using System.Collections.Generic;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Plugin.Services;
using ECommons;
using ECommons.DalamudServices;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace BozjaBuddyReborn.Automation;

/// <summary>
/// Rejects incoming social requests only while the runner is active.
///
/// Party invites use a strong discriminator borrowed from AutoFATEGrind: AgentPartyInvite's
/// ConfirmAddonId must match the SelectYesno that opened. Other request kinds do not currently
/// expose an equally convenient agent id, so the fallback requires BOTH a social-object keyword
/// and an incoming-request keyword in the actual SelectYesno prompt. Generic Yes/No dialogs are
/// never rejected on the strength of the addon name alone.
/// </summary>
public sealed unsafe class SocialRequestGuard : IDisposable
{
    private const string SelectYesno = "SelectYesno";

    private static readonly string[] SocialSubjects =
    [
        "friend", "フレンド",
        "linkshell", "リンクシェル",
        "cross-world linkshell", "cross world linkshell", "クロスワールドリンクシェル",
        "trade", "トレード", "取引",
        "alliance", "アライアンス",
    ];

    private static readonly string[] IncomingRequestWords =
    [
        "invited you", "invitation", "request from", "has requested", "would like to",
        "誘われ", "誘い", "招待", "申請", "申し込まれ", "申し込み", "加入依頼",
    ];

    private readonly Configuration _config;
    private readonly Func<bool> _running;

    private bool _pending;
    private uint _pendingAddonId;
    private string _pendingKind = string.Empty;

    public string LastDeclined { get; private set; } = string.Empty;

    public SocialRequestGuard(Configuration config, Func<bool> running)
    {
        _config = config;
        _running = running;
        Svc.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, SelectYesno, OnSetup);
        Svc.Framework.Update += OnUpdate;
    }

    public void Dispose()
    {
        Svc.Framework.Update -= OnUpdate;
        Svc.AddonLifecycle.UnregisterListener(AddonEvent.PostSetup, SelectYesno, OnSetup);
    }

    private void OnSetup(AddonEvent type, AddonArgs args)
    {
        if (!_config.RejectSocialRequestsWhileRunning || !_running() || _pending)
            return;

        var addon = (AtkUnitBase*)args.Addon.Address;
        if (addon == null)
            return;

        if (IsPartyInvite(addon))
        {
            Queue(addon->Id, "パーティ招待");
            return;
        }

        try
        {
            var master = new AddonMaster.SelectYesno((nint)addon);
            var kind = ClassifyPrompt(master.Text);
            if (kind.Length != 0)
                Queue(addon->Id, kind);
        }
        catch (Exception ex)
        {
            Svc.Log.Debug($"[BozjaBuddyReborn] Social request prompt classification failed: {ex.Message}");
        }
    }

    private void OnUpdate(IFramework _)
    {
        if (!_pending)
            return;

        if (!_config.RejectSocialRequestsWhileRunning || !_running())
        {
            Clear();
            return;
        }

        if (!GenericHelpers.TryGetAddonByName<AtkUnitBase>(SelectYesno, out var addon)
            || !GenericHelpers.IsAddonReady(addon)
            || addon->Id != _pendingAddonId)
        {
            Clear();
            return;
        }

        // Revalidate the same dialog immediately before touching it. For party invitations this
        // remains agent-backed; for other social requests the prompt must still classify.
        var party = IsPartyInvite(addon);
        string kind;
        try
        {
            kind = party ? "パーティ招待" : ClassifyPrompt(new AddonMaster.SelectYesno((nint)addon).Text);
        }
        catch
        {
            Clear();
            return;
        }

        if (kind.Length == 0)
        {
            Clear();
            return;
        }

        try
        {
            var master = new AddonMaster.SelectYesno((nint)addon)
            {
                RespectDisabledButtons = true,
                RespectHoldButtons = true,
            };
            master.No();
            LastDeclined = kind;
            Svc.Log.Information($"[BozjaBuddyReborn] Declined incoming social request: {kind}.");
        }
        catch (Exception ex)
        {
            Svc.Log.Warning(ex, "[BozjaBuddyReborn] Failed to decline identified social request.");
        }
        finally
        {
            Clear();
        }
    }

    private void Queue(uint addonId, string kind)
    {
        _pending = true;
        _pendingAddonId = addonId;
        _pendingKind = kind;
        Svc.Log.Debug($"[BozjaBuddyReborn] Identified incoming social request ({kind}); queued decline.");
    }

    private static bool IsPartyInvite(AtkUnitBase* addon)
    {
        try
        {
            var agent = AgentPartyInvite.Instance();
            return agent != null
                   && agent->ConfirmAddonId != 0
                   && agent->ConfirmAddonId == addon->Id;
        }
        catch
        {
            return false;
        }
    }

    private static string ClassifyPrompt(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            return string.Empty;

        var normalized = prompt.Replace('\n', ' ').Trim();
        var lower = normalized.ToLowerInvariant();

        string? subject = null;
        foreach (var candidate in SocialSubjects)
        {
            if (!lower.Contains(candidate.ToLowerInvariant(), StringComparison.Ordinal))
                continue;
            subject = candidate;
            break;
        }
        if (subject == null)
            return string.Empty;

        var incoming = false;
        foreach (var candidate in IncomingRequestWords)
        {
            if (!lower.Contains(candidate.ToLowerInvariant(), StringComparison.Ordinal))
                continue;
            incoming = true;
            break;
        }
        if (!incoming)
            return string.Empty;

        if (subject.Contains("friend", StringComparison.OrdinalIgnoreCase) || subject.Contains("フレンド", StringComparison.Ordinal))
            return "フレンド申請";
        if (subject.Contains("linkshell", StringComparison.OrdinalIgnoreCase) || subject.Contains("リンクシェル", StringComparison.Ordinal))
            return "リンクシェル招待";
        if (subject.Contains("trade", StringComparison.OrdinalIgnoreCase) || subject is "トレード" or "取引")
            return "トレード要求";
        if (subject.Contains("alliance", StringComparison.OrdinalIgnoreCase) || subject == "アライアンス")
            return "アライアンス招待";
        return "ソーシャル要求";
    }

    private void Clear()
    {
        _pending = false;
        _pendingAddonId = 0;
        _pendingKind = string.Empty;
    }
}
