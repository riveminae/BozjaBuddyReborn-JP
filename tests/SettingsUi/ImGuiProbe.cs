// Test-only observer: every call is forwarded to the real native ImGui implementation.
// No widget return, ref argument or input is replaced. Rectangles locate native input targets.
global using ImGui = SettingsUiTests.ImGuiProbe;
using System.Numerics;
using System.Runtime.Versioning;
using Dalamud.Bindings.ImGui;
using Native = Dalamud.Bindings.ImGui.ImGui;
[assembly: SupportedOSPlatform("windows7.0")]

namespace SettingsUiTests;

public static class ImGuiProbe
{
    public readonly record struct Item(string Label, string Kind, Vector2 Min, Vector2 Max, bool Visible, int TabDepth);
    public static readonly List<Item> Items = [];
    private static int _tabDepth;
    private static void Observe(string label, string kind) => Items.Add(new(label, kind,
        Native.GetItemRectMin(), Native.GetItemRectMax(), Native.IsItemVisible(), _tabDepth));
    public static bool BeginTabBar(string id) { var result = Native.BeginTabBar(id); if (result) _tabDepth++; return result; }
    public static void EndTabBar() { Native.EndTabBar(); _tabDepth--; }
    public static bool BeginTabItem(string label) { var result = Native.BeginTabItem(label); Observe(label, "tab"); return result; }
    public static void EndTabItem() => Native.EndTabItem();
    public static bool Checkbox(string label, ref bool value) { var result = Native.Checkbox(label, ref value); Observe(label, "checkbox"); return result; }
    public static bool Button(string label) { var result = Native.Button(label); Observe(label, "button"); return result; }
    public static bool SmallButton(string label) { var result = Native.SmallButton(label); Observe(label, "button"); return result; }
    public static bool CollapsingHeader(string label) { var result = Native.CollapsingHeader(label); Observe(label, "header"); return result; }
    public static bool InputText(string label, ref string value, int maxLength) { var result = Native.InputText(label, ref value, maxLength); Observe(label, "input"); return result; }
    public static bool InputFloat2(string label, ref Vector2 value) { var result = Native.InputFloat2(label, ref value); Observe(label, "input"); return result; }
    public static bool SliderFloat(string label, ref float value, float min, float max, string format) { var result = Native.SliderFloat(label, ref value, min, max, format); Observe(label, "slider"); return result; }
    public static bool SliderInt(string label, ref int value, int min, int max) { var result = Native.SliderInt(label, ref value, min, max); Observe(label, "slider"); return result; }
    public static bool BeginCombo(string label, string preview) { var result = Native.BeginCombo(label, preview); Observe(label, "combo"); return result; }
    public static void EndCombo() => Native.EndCombo();
    public static bool Selectable(string label, bool selected) { var result = Native.Selectable(label, selected); Observe(label, "selectable"); return result; }
    public static void SetItemDefaultFocus() => Native.SetItemDefaultFocus();
    public static bool BeginChild(string id, Vector2 size, bool border) => Native.BeginChild(id, size, border);
    public static void EndChild() => Native.EndChild();
    public static bool BeginTable(string id, int count, ImGuiTableFlags flags) => Native.BeginTable(id, count, flags);
    public static void EndTable() => Native.EndTable();
    public static void TableSetupColumn(string label, ImGuiTableColumnFlags flags, float width = 0) => Native.TableSetupColumn(label, flags, width);
    public static void TableHeadersRow() => Native.TableHeadersRow();
    public static void TableNextRow() => Native.TableNextRow();
    public static bool TableNextColumn() => Native.TableNextColumn();
    public static void TextUnformatted(string text) { Native.TextUnformatted(text); Observe(text, "text"); }
    public static void TextColored(Vector4 color, string text) { Native.TextColored(color, text); Observe(text, "text"); }
    public static void SetTooltip(string text) => Native.SetTooltip(text);
    public static bool IsItemHovered() => Native.IsItemHovered();
    public static void SetNextItemWidth(float width) => Native.SetNextItemWidth(width);
    public static void SetNextItemOpen(bool open, ImGuiCond condition) => Native.SetNextItemOpen(open, condition);
    public static void SameLine() => Native.SameLine();
    public static void Separator() => Native.Separator();
    public static void Spacing() => Native.Spacing();
    public static void PushID(string id) => Native.PushID(id);
    public static void PopID() => Native.PopID();
    public static void ProgressBar(float fraction, Vector2 size, string overlay) => Native.ProgressBar(fraction, size, overlay);
}
