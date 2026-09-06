using System.Numerics;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;

namespace BozjaBuddyReborn.Game;

/// <summary>
/// One Critical Engagement as read from the live DynamicEventContainer, snapshotted into
/// managed memory so the UI and the controller can hold it across frames.
/// </summary>
public readonly record struct CeSnapshot(
    int Index,
    ushort EventId,
    DynamicEventState State,
    byte Progress,
    byte Participants,
    byte MaxParticipants,
    uint SecondsLeft,
    uint SecondsDuration,
    Vector3 Position,
    float Radius,
    string Name,
    bool IsDuel)
{
    /// <summary>
    /// Registration phase. The enabled recruitment UI button is authoritative for an actual
    /// Register press; reaching a world position does not enroll the player.
    /// </summary>
    public bool IsJoinable => State == DynamicEventState.Register;

    /// <summary>Warmup or battle - the engagement has started.</summary>
    public bool IsRunning => State is DynamicEventState.Warmup or DynamicEventState.Battle;

    /// <summary>Any non-Inactive state, i.e. the engagement exists on the field right now.</summary>
    public bool IsLive => State != DynamicEventState.Inactive;

    /// <summary>A usable world position was published for this engagement.</summary>
    public bool HasPosition => Position != Vector3.Zero;

    public string StateText => State switch
    {
        DynamicEventState.Inactive => "Inactive",
        DynamicEventState.Register => "Registering",
        DynamicEventState.Warmup => "Warmup",
        DynamicEventState.Battle => "In battle",
        _ => State.ToString(),
    };
}
