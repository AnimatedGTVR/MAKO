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
    private const string GithubLookupSentinel = "__github_lookup__";

    public static void Run(string? initialQuery, string? selected = null)
    {
        var entries = PackageRegistry.All();
        string query = initialQuery ?? "";
        string current = selected ?? PackageRegistry.Search(query).FirstOrDefault()?.Name ?? "";

        // Live GitHub mako.json lookup, triggered by clicking the synthetic
        // "Look up ... (GitHub)" entry that appears whenever the query looks
        // like "github:User/Repo" — set once fetched, kept around so the
        // detail panel can keep showing it even after the query changes.
        RegistryEntry? remoteEntry = null;
        string? remoteError = null;

        var ui = new MakoUI();
        ui.Init("MAKO Package Browser", 760, 460);

        try
        {
            while (ui.Running())
            {
                ui.Begin();

                // Fill whatever the real window size turns out to be each
                // frame, rather than a hardcoded size — the actual window
                // (subject to the OS/compositor/DPI scaling) doesn't
                // necessarily match the size passed to MakoUI.Init(),
                // and a mismatch there is exactly what leaves empty space
                // below a fixed-size panel.
                var displaySize = ImGui.GetIO().DisplaySize;
                ImGui.SetNextWindowPos(Vector2.Zero);
                ImGui.SetNextWindowSize(displaySize);
                ImGui.Begin("##browser", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove);

                // ── Left: search + results ──────────────────────────────
                ImGui.BeginChild("##list", new Vector2(260, 0), ImGuiChildFlags.Border);
                ImGui.SetNextItemWidth(-1);
                ImGui.InputTextWithHint("##query", "Search packages...", ref query, 128);

                var matches = PackageRegistry.Search(query).ToList();
                ImGui.Spacing();

                bool typedGithub = query.StartsWith("github:", StringComparison.OrdinalIgnoreCase);
                bool looksLikeGithub = typedGithub && GithubPackageLookup.TryParseSource(query, out _, out _);
                if (looksLikeGithub)
                {
                    var lookupLabel = $"Look up {query} (GitHub) ->";
                    if (ImGui.Selectable(lookupLabel, current == GithubLookupSentinel))
                    {
                        try
                        {
                            remoteEntry = GithubPackageLookup.Fetch(query);
                            remoteError = null;
                            current = remoteEntry.Name;
                        }
                        catch (MakoError ex)
                        {
                            remoteEntry = null;
                            remoteError = ex.RawMessage;
                            current = GithubLookupSentinel;
                        }
                        catch (Exception ex)
                        {
                            // Belt-and-suspenders: Fetch() is documented to
                            // never let anything but MakoError escape, but if
                            // that ever regresses, this is the difference
                            // between "visible error in the detail panel" and
                            // "the whole browser window silently closes."
                            remoteEntry = null;
                            remoteError = $"unexpected error: {ex.Message}";
                            current = GithubLookupSentinel;
                        }
                    }
                    ImGui.Separator();
                }
                else if (typedGithub)
                {
                    // Typed "github:" but not "github:User/Repo" — e.g. a
                    // colon where a slash belongs. Say so instead of just
                    // silently falling through to "No matches" with no clue
                    // why a github: query isn't offering a lookup.
                    ImGui.TextColored(new Vector4(0.9f, 0.7f, 0.2f, 1f), "Format: github:User/Repo");
                    ImGui.Separator();
                }

                foreach (var e in matches)
                {
                    var label = e.Status == "planned" ? $"{e.Name} (planned)" : e.Name;
                    if (ImGui.Selectable(label, e.Name == current))
                        current = e.Name;
                }
                if (matches.Count == 0 && !typedGithub)
                    ImGui.TextDisabled("No matches.");
                ImGui.EndChild();

                ImGui.SameLine();

                // ── Right: detail panel ─────────────────────────────────
                ImGui.BeginChild("##detail", new Vector2(0, 0), ImGuiChildFlags.Border);
                var sel = remoteEntry != null && remoteEntry.Name == current
                    ? remoteEntry
                    : entries.FirstOrDefault(e => e.Name == current);

                if (sel != null)
                {
                    DrawDetail(sel);
                }
                else if (current == GithubLookupSentinel && remoteError != null)
                {
                    ImGui.TextColored(new Vector4(0.9f, 0.4f, 0.4f, 1f), "Couldn't fetch that package:");
                    ImGui.Spacing();
                    ImGui.TextWrapped(remoteError);
                }
                else
                {
                    ImGui.TextDisabled("Select a package on the left.");
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

    /// Single-entry view for a package that isn't in the local registry —
    /// e.g. a live GitHub mako.json lookup. No search box/sidebar, since
    /// there's nothing local to browse alongside it; just the same detail
    /// panel, full width.
    public static void RunSingle(RegistryEntry entry)
    {
        var ui = new MakoUI();
        ui.Init($"MAKO Package Browser — {entry.Name}", 560, 400);

        try
        {
            while (ui.Running())
            {
                ui.Begin();

                var displaySize = ImGui.GetIO().DisplaySize;
                ImGui.SetNextWindowPos(Vector2.Zero);
                ImGui.SetNextWindowSize(displaySize);
                ImGui.Begin("##browser_single", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove);

                DrawDetail(entry);

                ImGui.End();
                ui.End();
            }
        }
        finally
        {
            ui.Dispose();
        }
    }

    private static void DrawDetail(RegistryEntry sel)
    {
        ImGui.TextColored(new Vector4(0.55f, 0.75f, 1f, 1f), sel.Name);

        if (sel.Status == "planned")
            ImGui.TextColored(new Vector4(0.9f, 0.7f, 0.2f, 1f), "Planned - not yet available");
        else
            ImGui.TextColored(new Vector4(0.5f, 0.85f, 0.5f, 1f), "Available");

        ImGui.Spacing();
        ImGui.TextWrapped(sel.Description);
        ImGui.Spacing();
        ImGui.Separator();

        if (!ImGui.BeginTabBar("##detail_tabs")) return;

        if (ImGui.BeginTabItem("Usage & Docs"))
        {
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
            if (sel.Usage == null && sel.Source == null && sel.Docs == null && sel.Note == null)
                ImGui.TextDisabled("Nothing to show yet.");
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Versions"))
        {
            ImGui.Spacing();
            if (sel.Versions == null || sel.Versions.Count == 0)
            {
                ImGui.TextDisabled("No other versions or variants.");
            }
            else
            {
                foreach (var v in sel.Versions)
                {
                    ImGui.TextColored(new Vector4(0.55f, 0.75f, 1f, 1f), v.Name);
                    if (v.Status == "planned")
                        ImGui.TextColored(new Vector4(0.9f, 0.7f, 0.2f, 1f), "Planned - not yet available");
                    ImGui.TextWrapped(v.Description);
                    if (v.Usage != null)
                    {
                        ImGui.Text("Usage:");
                        ImGui.SameLine();
                        var vUsage = v.Usage;
                        ImGui.InputText($"##usage_{v.Name}", ref vUsage, 128, ImGuiInputTextFlags.ReadOnly);
                    }
                    if (v.Note != null) ImGui.TextWrapped(v.Note);
                    ImGui.Spacing();
                    ImGui.Separator();
                    ImGui.Spacing();
                }
            }
            ImGui.EndTabItem();
        }

        ImGui.EndTabBar();
    }
}
