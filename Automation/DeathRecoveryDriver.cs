using System;
using BozjaBuddyReborn.External;
using BozjaBuddyReborn.Vendor.BOCCHI;
using ECommons.DalamudServices;

namespace BozjaBuddyReborn.Automation;

public enum DeathRecoveryState : byte
{
    Alive = 0,
    WaitingForRaise = 1,
    WaitingForCeEnd = 2,
    RequestingReturn = 3,
    RecoveryUnavailable = 4,
}

public readonly record struct DeathRecoveryStatus(
    DeathRecoveryState State,
    string JapaneseStatus,
    bool Fatal)
{
    public bool HandlingDeath => State != DeathRecoveryState.Alive;
}

/// <summary>
/// Timed death-recovery policy agreed for v1.1.
///
/// TextAdvance is enabled only while dead so it can accept a genuine raise / Return confirmation,
/// and the pre-death state is restored immediately after revival.  Return itself is the same
/// General Action 8 used by BOCCHI.  Critical Engagement deaths never cast Return while the CE is
/// still live; skirmish deaths wait 30s, travel/idle deaths 10s.
/// </summary>
public sealed class DeathRecoveryDriver(TextAdvanceIpc textAdvance)
{
    private const long SkirmishRaiseWaitMs = 30_000;
    private const long TravelRaiseWaitMs = 10_000;
    private const long ReturnRetryMs = 10_000;

    private readonly TextAdvanceIpc _textAdvance = textAdvance;
    private long _deadSinceMs;
    private long _lastReturnAttemptMs;
    private bool _wasDead;

    public DeathRecoveryStatus Tick(bool dead, bool criticalEngagementLive, bool diedDuringSkirmish)
    {
        if (!dead)
        {
            if (_wasDead)
            {
                _textAdvance.RestoreOriginalState();
                Svc.Log.Information("[BozjaBuddyReborn] Death recovery completed; character is alive again.");
            }

            ResetInternal();
            return new DeathRecoveryStatus(DeathRecoveryState.Alive, string.Empty, false);
        }

        var now = Environment.TickCount64;
        if (!_wasDead)
        {
            _wasDead = true;
            _deadSinceMs = now;
            _lastReturnAttemptMs = 0;
            Svc.Log.Information("[BozjaBuddyReborn] Character died; starting timed death recovery.");
        }

        // The agreed unattended-recovery contract requires TextAdvance. Do not silently click a
        // generic confirmation ourselves when the optional plugin is absent; that would weaken the
        // exact-dialog safety boundary chosen for v1.1.
        if (!_textAdvance.Available || !_textAdvance.EnsureTemporarilyEnabled())
        {
            return new DeathRecoveryStatus(
                DeathRecoveryState.RecoveryUnavailable,
                "死亡復旧に必要なTextAdvanceが利用できません。",
                true);
        }

        if (criticalEngagementLive)
        {
            return new DeathRecoveryStatus(
                DeathRecoveryState.WaitingForCeEnd,
                "CE中に戦闘不能です。蘇生を待っています（CE終了まではキャンプへ戻りません）。",
                false);
        }

        var waitMs = diedDuringSkirmish ? SkirmishRaiseWaitMs : TravelRaiseWaitMs;
        var elapsed = Math.Max(0, now - _deadSinceMs);
        if (elapsed < waitMs)
        {
            var remaining = Math.Ceiling((waitMs - elapsed) / 1000d);
            return new DeathRecoveryStatus(
                DeathRecoveryState.WaitingForRaise,
                $"戦闘不能です。蘇生を待っています（残り{remaining:F0}秒）。",
                false);
        }

        // After the raise window, cast Return and leave confirmation to temporarily enabled
        // TextAdvance. If Return is still cooling down, keep waiting rather than inventing a
        // direct UI callback. A refused/expired confirmation is retried at a bounded cadence.
        if (_lastReturnAttemptMs != 0 && now - _lastReturnAttemptMs < ReturnRetryMs)
        {
            var retryIn = Math.Ceiling((ReturnRetryMs - (now - _lastReturnAttemptMs)) / 1000d);
            return new DeathRecoveryStatus(
                DeathRecoveryState.RequestingReturn,
                $"キャンプへの帰還確認を待っています（再試行まで{retryIn:F0}秒）。",
                false);
        }

        if (!GeneralActions.ReturnReady())
        {
            return new DeathRecoveryStatus(
                DeathRecoveryState.RequestingReturn,
                "デジョンの再使用待ちです。蘇生は引き続き受諾します。",
                false);
        }

        _lastReturnAttemptMs = now;
        if (GeneralActions.CastReturn())
        {
            Svc.Log.Information("[BozjaBuddyReborn] Cast Return for timed death recovery; TextAdvance will handle confirmation.");
            return new DeathRecoveryStatus(
                DeathRecoveryState.RequestingReturn,
                "キャンプへ戻るためデジョンを実行しました。",
                false);
        }

        Svc.Log.Warning("[BozjaBuddyReborn] Return was ready but the game refused the death-recovery cast; retrying later.");
        return new DeathRecoveryStatus(
            DeathRecoveryState.RequestingReturn,
            "キャンプへの帰還を再試行しています。",
            false);
    }

    public void CancelAndRestore()
    {
        if (_wasDead)
            _textAdvance.RestoreOriginalState();
        ResetInternal();
    }

    private void ResetInternal()
    {
        _deadSinceMs = 0;
        _lastReturnAttemptMs = 0;
        _wasDead = false;
    }
}
