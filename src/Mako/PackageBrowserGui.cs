using ImGuiNET;
using System.Numerics;

namespace Mako;

/// The graphical package browser behind `mko search` / `mko info` (default,
/// unless --term is passed). Plain C# tooling, not a .mko script — reuses
/// MakoUI's own standalone-window lifecycle (Init/Running/Begin/End/Dispose,
/// the same path examples/ui_demo.mko drives from script) but calls ImGui.NET
/// directly for layout, since MakoUI's per-widget wrappers don't cover
/// list/selection widgets this needs.
static class PackageBrowserGui
{
    public static void Run(string? initialQuery, string? selected = null)
    {
        var entries = PackageRegistry.All();
        string query = initialQuery ?? "";
        string current = selected ?? entries.FirstOrDefault(e => Matches(e, query))?.Name ?? "";

        var ui = new MakoUI();
        ui.Init("MAKO Package Browser", 760, 460);

        try
        {
            while (ui.Running())
            {
                ui.Begin();

                ImGui.SetNextWindowPos(Vector2.Zero);
                ImGui.SetNextWindowSize(new Vector2(760, 460));
                ImGui.Begin("##browser", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove);

                // ── Left: search + results ──────────────────────────────
                ImGui.BeginChild("##list", new Vector2(260, 0), ImGuiChildFlags.Border);
                ImGui.SetNextItemWidth(-1);
                ImGui.InputTextWithHint("##query", "Search packages...", ref query, 128);

                var matches = entries.Where(e => Matches(e, query)).ToList();
                ImGui.Spacing();
                foreach (var e in matches)
                {
                    var label = e.Status == "planned" ? $"{e.Name} (planned)" : e.Name;
                    if (ImGui.Selectable(label, e.Name == current))
                        current = e.Name;
                }
                if (matches.Count == 0)
                    ImGui.TextDisabled("No matches.");
                ImGui.EndChild();

                ImGui.SameLine();

                // ── Right: detail panel ─────────────────────────────────
                ImGui.BeginChild("##detail", new Vector2(0, 0), ImGuiChildFlags.Border);
                var sel = entries.FirstOrDefault(e => e.Name == current);
                if (sel == null)
                {
                    ImGui.TextDisabled("Select a package on the left.");
                }
                else
                {
                    ImGui.PushFont(ImGui.GetFont()); // no bold font available — headline via color instead
                    ImGui.TextColored(new Vector4(0.55f, 0.75f, 1f, 1f), sel.Name);
                    ImGui.PopFont();

                    if (sel.Status == "planned")
                        ImGui.TextColored(new Vector4(0.9f, 0.7f, 0.2f, 1f), "Planned — not yet available");
                    else
                        ImGui.TextColored(new Vector4(0.5f, 0.85f, 0.5f, 1f), "Available");

                    ImGui.Spacing();
                    ImGui.TextWrapped(sel.Description);
                    ImGui.Spacing();
                    ImGui.Separator();
                    ImGui.Spacing();

                    if (sel.Usage != null)
                    {
                        ImGui.Text("Usage:");
                        ImGui.SameLine();
                        var usage = sel.Usage;
                        ImGui.InputText("##usage", ref usage, 128, ImGuiInputTextFlags.ReadOnly);
                    }
                    if (sel.Source != null) ImGui.Text($"Source: {sel.Source}");
                    if (sel.Docs != null) ImGui.Text($"Docs: {sel.Docs}");
                    if (sel.Note != null) { ImGui.Spacing(); ImGui.TextWrapped(sel.Note); }
                }
                ImGui.EndChild();

                ImGui.End();
                ui.End();
            }
        }
        finally
        {
            ui.Dispose();
        }
    }

    private static bool Matches(RegistryEntry e, string query) =>
        string.IsNullOrWhiteSpace(query) ||
        e.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        e.Description.Contains(query, StringComparison.OrdinalIgnoreCase);
}
