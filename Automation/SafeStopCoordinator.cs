using System;
using BozjaBuddyReborn.Game;
using BozjaBuddyReborn.Vendor.BOCCHI;
using ECommons.DalamudServices;

namespace BozjaBuddyReborn.Automation;

public readonly record struct SafeStopStatus(bool StopNow, string JapaneseStatus);

/// <summary>
/// Best-effort safe shutdown after a required dependency has exceeded its recovery window.
/// Combat is never interrupted by a Return cast. Once combat ends, Return is used when available,
/// its owned confirmation is accepted, and the run stops after reaching base camp. Failure to
/// perform the safe return fails closed by stopping rather than retrying forever.
/// </summary>
public sealed class SafeStopCoordinator
{
    private const long ReturnTimeoutMs = 25_000;

    private bool _returnPending;
    private bool _confirmed;
    private long _returnStartedMs;

    public SafeStopStatus Tick(bool inCombat)
    {
        if (inCombat)
            return new SafeStopStatus(false, "必須プラグインが復帰しません。戦闘終了後に安全停止します。");

        var me = Svc.Objects.LocalPlayer;
        if (me == null)
            return new SafeStopStatus(true, "プレイヤー状態を取得できないため停止します。");

        var camp = FieldAethernet.BaseCamp(Svc.ClientState.TerritoryType);
        if (camp is not { } baseCamp)
            return new SafeStopStatus(true, "拠点位置を取得できないためその場で停止します。");

        if (Movement.HorizontalDistance(me.Position, baseCamp.Position) <= 80f)
            return new SafeStopStatus(true, "拠点へ戻ったため停止します。");

        var now = Environment.TickCount64;
        if (_returnPending)
        {
            if (!_confirmed && GeneralActions.TryConfirmPendingReturn())
            {
                _confirmed = true;
                Svc.Log.Information("[BozjaBuddyReborn] Confirmed Return for required-dependency safe stop.");
            }

            if (now - _returnStartedMs > ReturnTimeoutMs)
                return new SafeStopStatus(true, "拠点へのデジョンが完了しないため停止します。");

            return new SafeStopStatus(false, "必須プラグインが復帰しません。拠点へデジョンしています。");
        }

        // This is an intentional shutdown traversal, not combat/survival automation. Return
        // cannot be relied on to start while mounted, so explicitly dismount before the safe-stop
        // cast just like FieldTravelRouter does. No Lost Action or combat action is fired here.
        if (Mount.IsMounted && !Mount.EnsureDismounted())
            return new SafeStopStatus(false, "安全停止のためマウントから降りています。");

        if (!GeneralActions.ReturnReady())
            return new SafeStopStatus(true, "必須プラグインが復帰せず、デジョンも使用できないため停止します。");

        if (!GeneralActions.CastReturn())
            return new SafeStopStatus(true, "安全停止用のデジョンを開始できないため停止します。");

        _returnPending = true;
        _confirmed = false;
        _returnStartedMs = now;
        Svc.Log.Information("[BozjaBuddyReborn] Started Return for required-dependency safe stop.");
        return new SafeStopStatus(false, "必須プラグインが復帰しません。拠点へデジョンしています。");
    }

    public void Reset()
    {
        _returnPending = false;
        _confirmed = false;
        _returnStartedMs = 0;
    }
}
