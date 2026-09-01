from __future__ import annotations
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


def patch(path: str, old: str, new: str, marker: str | None = None) -> None:
    p = ROOT / path
    text = p.read_text(encoding="utf-8-sig")
    marker = marker or new
    if marker in text:
        print(f"{path}: debug world overlay already applied")
        return
    if old not in text:
        raise RuntimeError(f"anchor missing in {path}: {old[:180]!r}")
    p.write_text(text.replace(old, new, 1), encoding="utf-8")
    print(f"{path}: debug world overlay patched")


patch(
    "Configuration.cs",
    """    /// <summary>Log each previously unseen field-rank raw icon pair once in test diagnostics.</summary>\n    public bool EnemyRankDiagnostics = true;\n\n""",
    """    /// <summary>Log each previously unseen field-rank raw icon pair once in test diagnostics.</summary>\n    public bool EnemyRankDiagnostics = true;\n\n    /// <summary>TEST/diagnostics only: draw route and dangerous-enemy geometry in the world.</summary>\n    public bool DebugWorldOverlay;\n\n""",
    "public bool DebugWorldOverlay;",
)

patch(
    "Automation/FieldTravelRouter.cs",
    """    public string RouteDescription { get; private set; } = \"直接移動\";\n\n    public bool IsRoutingTo(Vector3 destination) =>\n""",
    """    public string RouteDescription { get; private set; } = \"直接移動\";\n\n    // Read-only diagnostic snapshot. These are world coordinates only; exposing them cannot\n    // mutate route state, and keeps the overlay out of the planner's decision logic.\n    public Vector3 DebugGoal => _goal;\n    public Vector3? DebugDeparture => _departure?.Position;\n    public Vector3? DebugInbound => _inbound?.Position;\n    public uint DebugDeparturePlaceNameId => _departure?.PlaceNameId ?? 0;\n    public uint DebugInboundPlaceNameId => _inbound?.PlaceNameId ?? 0;\n\n    public bool IsRoutingTo(Vector3 destination) =>\n""",
    "public Vector3 DebugGoal => _goal;",
)

patch(
    "Automation/Movement.cs",
    """    public string RouteDescription => _fieldRouter.RouteDescription;\n    public bool LifestreamAvailable => _fieldRouter.LifestreamAvailable;\n    public bool YieldingToManualMovement => _manualYield.ShouldYield();\n\n""",
    """    public string RouteDescription => _fieldRouter.RouteDescription;\n    public bool LifestreamAvailable => _fieldRouter.LifestreamAvailable;\n    public bool YieldingToManualMovement => _manualYield.ShouldYield();\n    public Vector3 DebugRouteGoal => _fieldRouter.DebugGoal;\n    public Vector3? DebugRouteDeparture => _fieldRouter.DebugDeparture;\n    public Vector3? DebugRouteInbound => _fieldRouter.DebugInbound;\n    public uint DebugRouteDeparturePlaceNameId => _fieldRouter.DebugDeparturePlaceNameId;\n    public uint DebugRouteInboundPlaceNameId => _fieldRouter.DebugInboundPlaceNameId;\n\n""",
    "public Vector3 DebugRouteGoal =>",
)

patch(
    "Windows/ConfigWindow.cs",
    """        ImGui.TextColored(Grey,\n            \"ザトゥノルの高低差などで地上経路が詰まった場合、実移動がこの時間発生しなければ\\n\" +\n            \"現在経路を破棄して目的地をnavmeshへ再スナップし、\\n\" +\n            \"新しい経路を作成します。\");\n\n        ImGui.Separator();\n        DrawAggroAvoidance();\n""",
    """        ImGui.TextColored(Grey,\n            \"ザトゥノルの高低差などで地上経路が詰まった場合、実移動がこの時間発生しなければ\\n\" +\n            \"現在経路を破棄して目的地をnavmeshへ再スナップし、\\n\" +\n            \"新しい経路を作成します。\");\n\n        ImGui.Spacing();\n        var debugOverlay = _config.DebugWorldOverlay;\n        if (ImGui.Checkbox(\"テスト用: 経路・危険敵をworld上に表示する\", ref debugOverlay))\n        {\n            _config.DebugWorldOverlay = debugOverlay;\n            Save();\n        }\n        if (ImGui.IsItemHovered())\n            ImGui.SetTooltip(\"目的地、選択Aethernet経路、IV/V/★/判定不能敵の感知範囲を描画します。通常はOFFにしてください。\");\n\n        ImGui.Separator();\n        DrawAggroAvoidance();\n""",
    "テスト用: 経路・危険敵をworld上に表示する",
)

patch(
    "Plugin.cs",
    """using System;\nusing BozjaBuddyReborn.Automation;\n""",
    """using System;\nusing System.Collections.Generic;\nusing System.Numerics;\nusing BozjaBuddyReborn.Automation;\n""",
    "using System.Collections.Generic;",
)

patch(
    "Plugin.cs",
    """using Dalamud.Game.Command;\nusing Dalamud.Interface.Windowing;\n""",
    """using Dalamud.Game.Command;\nusing Dalamud.Bindings.ImGui;\nusing Dalamud.Interface.Windowing;\n""",
    "using Dalamud.Bindings.ImGui;",
)

patch(
    "Plugin.cs",
    """    private bool _multiboxStarted;\n\n    /// <summary>Last known character name, announced over the multibox pipe. See SyncMultiboxLink.</summary>\n""",
    """    private bool _multiboxStarted;\n    private long _debugOverlayScanMs;\n    private List<DangerZone> _debugOverlayDangerZones = [];\n\n    /// <summary>Last known character name, announced over the multibox pipe. See SyncMultiboxLink.</summary>\n""",
    "private List<DangerZone> _debugOverlayDangerZones",
)

patch(
    "Plugin.cs",
    """        pluginInterface.UiBuilder.Draw += _windows.Draw;\n        pluginInterface.UiBuilder.OpenMainUi += OpenMain;\n""",
    """        pluginInterface.UiBuilder.Draw += _windows.Draw;\n        pluginInterface.UiBuilder.Draw += DrawDebugWorldOverlay;\n        pluginInterface.UiBuilder.OpenMainUi += OpenMain;\n""",
    "pluginInterface.UiBuilder.Draw += DrawDebugWorldOverlay;",
)

patch(
    "Plugin.cs",
    """    private void OpenMain() => _mainWindow.IsOpen = true;\n    private void OpenConfig() => _configWindow.IsOpen = true;\n\n""",
    """    private void OpenMain() => _mainWindow.IsOpen = true;\n    private void OpenConfig() => _configWindow.IsOpen = true;\n\n    /// <summary>TEST-only world-space diagnostics; never affects movement or selection.</summary>\n    private void DrawDebugWorldOverlay()\n    {\n        if (!_config.DebugWorldOverlay || !FieldState.InFieldZone)\n            return;\n\n        var me = Svc.Objects.LocalPlayer;\n        if (me == null)\n            return;\n\n        var now = Environment.TickCount64;\n        if (now - _debugOverlayScanMs >= 500)\n        {\n            _debugOverlayScanMs = now;\n            _debugOverlayDangerZones = _aggroAvoidance.Scan(140f);\n        }\n\n        var draw = ImGui.GetForegroundDrawList();\n        var routeColour = ImGui.GetColorU32(new Vector4(0.25f, 0.80f, 1.00f, 0.90f));\n        var teleportColour = ImGui.GetColorU32(new Vector4(0.75f, 0.45f, 1.00f, 0.90f));\n        var goalColour = ImGui.GetColorU32(new Vector4(0.30f, 1.00f, 0.45f, 0.95f));\n        var dangerColour = ImGui.GetColorU32(new Vector4(1.00f, 0.25f, 0.20f, 0.80f));\n        var marginColour = ImGui.GetColorU32(new Vector4(1.00f, 0.75f, 0.20f, 0.55f));\n\n        var goal = _movement.DebugRouteGoal;\n        if (goal == Vector3.Zero && _controller.CurrentObjective.IsSet)\n            goal = _controller.CurrentObjective.Position;\n\n        if (_movement.DebugRouteDeparture is { } departure)\n        {\n            DrawWorldLine(me.Position, departure, routeColour, 2.5f);\n            DrawWorldLabel(departure, $\"出発Aethernet #{_movement.DebugRouteDeparturePlaceNameId}\", routeColour);\n\n            if (_movement.DebugRouteInbound is { } inbound)\n            {\n                DrawWorldLine(departure, inbound, teleportColour, 2.0f);\n                DrawWorldLabel(inbound, $\"到着Aethernet #{_movement.DebugRouteInboundPlaceNameId}\", teleportColour);\n                if (goal != Vector3.Zero)\n                    DrawWorldLine(inbound, goal, routeColour, 2.5f);\n            }\n        }\n        else if (goal != Vector3.Zero)\n        {\n            DrawWorldLine(me.Position, goal, routeColour, 2.5f);\n        }\n\n        if (goal != Vector3.Zero)\n            DrawWorldLabel(goal, $\"目的地 / {_movement.TravelMode}\", goalColour);\n\n        foreach (var zone in _debugOverlayDangerZones)\n            DrawDangerZone(zone, dangerColour, marginColour);\n\n        return;\n\n        void DrawDangerZone(DangerZone zone, uint danger, uint margin)\n        {\n            DrawWorldCircle(zone.Position, zone.ProximityRadius, danger, 2.0f);\n\n            // Sight cone: radius arc plus the two radial edges.\n            const int ArcSegments = 20;\n            var previous = Vector3.Zero;\n            for (var i = 0; i <= ArcSegments; i++)\n            {\n                var t = i / (float)ArcSegments;\n                var angle = zone.Rotation - zone.ConeHalfAngleRad + t * zone.ConeHalfAngleRad * 2f;\n                var point = zone.Position + new Vector3(MathF.Sin(angle), 0f, MathF.Cos(angle)) * zone.SightRadius;\n                if (i > 0)\n                    DrawWorldLine(previous, point, danger, 1.8f);\n                previous = point;\n            }\n            var left = zone.Position + new Vector3(\n                MathF.Sin(zone.Rotation - zone.ConeHalfAngleRad), 0f,\n                MathF.Cos(zone.Rotation - zone.ConeHalfAngleRad)) * zone.SightRadius;\n            var right = zone.Position + new Vector3(\n                MathF.Sin(zone.Rotation + zone.ConeHalfAngleRad), 0f,\n                MathF.Cos(zone.Rotation + zone.ConeHalfAngleRad)) * zone.SightRadius;\n            DrawWorldLine(zone.Position, left, danger, 1.8f);\n            DrawWorldLine(zone.Position, right, danger, 1.8f);\n\n            var extra = _config.DangerClearance\n                        + (zone.Strength == FieldEnemyStrength.Star ? _config.DangerStarExtraClearance : 0f);\n            DrawWorldCircle(zone.Position, zone.OuterRadius + extra, margin, 1.2f);\n\n            var rank = zone.Strength switch\n            {\n                FieldEnemyStrength.Star => \"★\",\n                FieldEnemyStrength.Unknown => \"?\",\n                _ => ((byte)zone.Strength).ToString(),\n            };\n            DrawWorldLabel(zone.Position, $\"[{rank}] {zone.Name}\", danger);\n        }\n\n        void DrawWorldCircle(Vector3 center, float radius, uint colour, float thickness)\n        {\n            const int Segments = 32;\n            var previous = center + new Vector3(radius, 0f, 0f);\n            for (var i = 1; i <= Segments; i++)\n            {\n                var angle = MathF.Tau * i / Segments;\n                var current = center + new Vector3(MathF.Cos(angle) * radius, 0f, MathF.Sin(angle) * radius);\n                DrawWorldLine(previous, current, colour, thickness);\n                previous = current;\n            }\n        }\n\n        void DrawWorldLine(Vector3 a, Vector3 b, uint colour, float thickness)\n        {\n            if (Svc.GameGui.WorldToScreen(a, out var sa) && Svc.GameGui.WorldToScreen(b, out var sb))\n                draw.AddLine(sa, sb, colour, thickness);\n        }\n\n        void DrawWorldLabel(Vector3 world, string text, uint colour)\n        {\n            var raised = world + new Vector3(0f, 2.5f, 0f);\n            if (Svc.GameGui.WorldToScreen(raised, out var screen))\n                draw.AddText(screen, colour, text);\n        }\n    }\n\n""",
    "private void DrawDebugWorldOverlay()",
)

patch(
    "Plugin.cs",
    """        Svc.PluginInterface.UiBuilder.Draw -= _windows.Draw;\n        Svc.PluginInterface.UiBuilder.OpenMainUi -= OpenMain;\n""",
    """        Svc.PluginInterface.UiBuilder.Draw -= _windows.Draw;\n        Svc.PluginInterface.UiBuilder.Draw -= DrawDebugWorldOverlay;\n        Svc.PluginInterface.UiBuilder.OpenMainUi -= OpenMain;\n""",
    "Svc.PluginInterface.UiBuilder.Draw -= DrawDebugWorldOverlay;",
)
