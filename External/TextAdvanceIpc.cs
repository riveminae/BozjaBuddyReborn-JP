using System;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using ECommons.DalamudServices;

namespace BozjaBuddyReborn.External;

/// <summary>
/// Optional TextAdvance integration used by death recovery.
///
/// TextAdvance exposes read-only state IPC as TextAdvance.IsEnabled / IsPaused. It does not expose
/// a simple global enable/disable IPC; its documented command surface is /at e and /at d. Those
/// commands are deliberately contained in this wrapper so the controller cannot accidentally leave
/// a user's TextAdvance setting changed after a recovery attempt.
/// </summary>
public sealed class TextAdvanceIpc
{
    private const long CommandRetryMs = 1500;

    private readonly ICallGateSubscriber<bool>? _isEnabled;
    private readonly ICallGateSubscriber<bool>? _isPaused;

    private bool _snapshotTaken;
    private bool _wasEnabled;
    private bool _changedByUs;
    private long _lastEnableCommandMs;

    public TextAdvanceIpc(IDalamudPluginInterface plugin)
    {
        _isEnabled = Bind(() => plugin.GetIpcSubscriber<bool>("TextAdvance.IsEnabled"));
        _isPaused = Bind(() => plugin.GetIpcSubscriber<bool>("TextAdvance.IsPaused"));
    }

    private static T? Bind<T>(Func<T> resolve) where T : class
    {
        try { return resolve(); }
        catch { return null; }
    }

    public bool Available
    {
        get
        {
            try { return _isEnabled?.HasFunction == true; }
            catch { return false; }
        }
    }

    public bool Enabled
    {
        get
        {
            try { return _isEnabled?.HasFunction == true && _isEnabled.InvokeFunc(); }
            catch { return false; }
        }
    }

    public bool Paused
    {
        get
        {
            try { return _isPaused?.HasFunction == true && _isPaused.InvokeFunc(); }
            catch { return false; }
        }
    }

    /// <summary>
    /// Remember the user's state once and request TextAdvance ON when needed. The command is
    /// asynchronous; callers should poll <see cref="Enabled"/> rather than assuming this call
    /// completed the transition in the same framework tick.
    /// </summary>
    public bool EnsureTemporarilyEnabled()
    {
        if (!Available)
            return false;

        if (!_snapshotTaken)
        {
            _wasEnabled = Enabled;
            _snapshotTaken = true;
        }

        if (Enabled)
            return true;

        var now = Environment.TickCount64;
        if (_lastEnableCommandMs != 0 && now - _lastEnableCommandMs < CommandRetryMs)
            return true;

        try
        {
            _lastEnableCommandMs = now;
            Svc.Commands.ProcessCommand("/at e");
            _changedByUs = true;
            Svc.Log.Information("[BozjaBuddyReborn] Requested temporary TextAdvance enable for death recovery.");
            return true;
        }
        catch (Exception ex)
        {
            Svc.Log.Warning(ex, "[BozjaBuddyReborn] Failed to request temporary TextAdvance enable.");
            return false;
        }
    }

    /// <summary>Restore the state captured by <see cref="EnsureTemporarilyEnabled"/>.</summary>
    public void RestoreOriginalState()
    {
        if (!_snapshotTaken)
            return;

        try
        {
            if (_changedByUs && !_wasEnabled && Available && Enabled)
            {
                Svc.Commands.ProcessCommand("/at d");
                Svc.Log.Information("[BozjaBuddyReborn] Restored TextAdvance to its pre-recovery disabled state.");
            }
        }
        catch (Exception ex)
        {
            Svc.Log.Warning(ex, "[BozjaBuddyReborn] Failed to restore TextAdvance state after death recovery.");
        }
        finally
        {
            ResetSnapshot();
        }
    }

    public void ResetSnapshot()
    {
        _snapshotTaken = false;
        _wasEnabled = false;
        _changedByUs = false;
        _lastEnableCommandMs = 0;
    }
}
