using System;
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
