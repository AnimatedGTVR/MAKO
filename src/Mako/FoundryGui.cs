using ImGuiNET;
using System.Collections.Concurrent;
using System.Numerics;

namespace Mako;

/// MakoUI-powered Foundry frontend. Layout uses ImGui.NET directly, matching
/// the existing package browser, while all build behavior stays in Foundry.
static class FoundryGui
{
    public static void Run(string projectPath)
    {
        var project = Foundry.LoadProject(projectPath);
        string selectedTarget = Foundry.Targets.Any(t => t.Id == project.DefaultTarget)
            ? project.DefaultTarget : "linux-x64";
        var logQueue = new ConcurrentQueue<string>();
        var logLines = new List<string> { "Foundry ready.", $"Project: {project.Root}" };
        Task<FoundryBuildResult>? buildTask = null;
        FoundryBuildResult? lastResult = null;

        var ui = new MakoUI();
        ui.Init("MAKO Foundry", 980, 650);
        ui.ThemeDark();
        try
        {
            while (ui.Running())
            {
                while (logQueue.TryDequeue(out string? line)) logLines.Add(line);
                if (buildTask is { IsCompleted: true })
                {
                    try { lastResult = buildTask.Result; }
                    catch (Exception ex) { lastResult = new(false, null, ex.Message); }
                    logLines.Add(lastResult.Success ? "SUCCESS" : $"FAILED: {lastResult.Message}");
                    buildTask = null;
                }

                ui.Begin();
                var display = ImGui.GetIO().DisplaySize;
                ImGui.SetNextWindowPos(Vector2.Zero);
                ImGui.SetNextWindowSize(display);
                ImGui.Begin("##foundry", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove);

                ImGui.TextColored(new Vector4(0.45f, 0.85f, 1f, 1f), "MAKO Foundry");
                ImGui.SameLine();
                ImGui.TextDisabled("Build games without exposing the toolchain.");
                ImGui.Separator();

                ImGui.BeginChild("##targets", new Vector2(245, 0), ImGuiChildFlags.Border);
                ImGui.Text("Targets");
                ImGui.Spacing();
                foreach (var target in Foundry.Targets)
                {
                    string suffix = target.Available ? "" : $" ({target.Status})";
                    if (ImGui.Selectable(target.Name + suffix, selectedTarget == target.Id))
                        selectedTarget = target.Id;
                }
                ImGui.EndChild();
                ImGui.SameLine();

                ImGui.BeginChild("##content", Vector2.Zero, ImGuiChildFlags.Border);
                var selected = Foundry.Targets.First(t => t.Id == selectedTarget);
                ImGui.TextColored(new Vector4(0.65f, 0.82f, 1f, 1f), selected.Name);
                ImGui.Text($"{selected.Platform} -> {selected.Artifact}");
                ImGui.TextWrapped(selected.Description);
                ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();

                bool building = buildTask != null;
                if (building) ImGui.BeginDisabled();
                string projectName = project.Name;
                ImGui.SetNextItemWidth(340);
                if (ImGui.InputText("Game name", ref projectName, 128)) project.Name = projectName;
                string projectVersion = project.Version;
                ImGui.SetNextItemWidth(180);
                if (ImGui.InputText("Version", ref projectVersion, 32)) project.Version = projectVersion;
                string entry = project.Entry;
                ImGui.SetNextItemWidth(340); ImGui.InputText("Entry", ref entry, 256, ImGuiInputTextFlags.ReadOnly);
                string projectOutput = project.Output;
                ImGui.SetNextItemWidth(340);
                if (ImGui.InputText("Output", ref projectOutput, 256)) project.Output = projectOutput;
                if (building) ImGui.EndDisabled();

                var errors = Foundry.Validate(project, selectedTarget);
                if (errors.Count > 0)
                {
                    ImGui.Spacing();
                    foreach (string error in errors)
                        ImGui.TextColored(new Vector4(1f, 0.55f, 0.35f, 1f), error);
                }

                ImGui.Spacing();
                if (building) ImGui.BeginDisabled();
                if (ImGui.Button("Save Settings"))
                {
                    project.DefaultTarget = selectedTarget;
                    try { Foundry.SaveProject(project); logLines.Add($"Saved {project.ManifestPath}"); }
                    catch (Exception ex) { logLines.Add($"Save failed: {ex.Message}"); }
                }
                ImGui.SameLine();
                if (!selected.Available || errors.Count > 0) ImGui.BeginDisabled();
                if (ImGui.Button(building ? "Building..." : "Build Game"))
                {
                    project.DefaultTarget = selectedTarget;
                    logLines.Add($"> build {selectedTarget}");
                    string target = selectedTarget;
                    buildTask = Task.Run(() => Foundry.Build(project, target, line => logQueue.Enqueue(line)));
                }
                if (!selected.Available || errors.Count > 0) ImGui.EndDisabled();
                if (building) ImGui.EndDisabled();

                if (lastResult?.Success == true && lastResult.ArtifactPath != null)
                {
                    ImGui.SameLine();
                    ImGui.TextColored(new Vector4(0.45f, 0.9f, 0.5f, 1f), lastResult.ArtifactPath);
                }

                ImGui.Spacing(); ImGui.Separator(); ImGui.Text("Build Log");
                ImGui.BeginChild("##build_log", new Vector2(0, 0), ImGuiChildFlags.Border,
                    ImGuiWindowFlags.HorizontalScrollbar);
                foreach (string line in logLines.TakeLast(250)) ImGui.TextUnformatted(line);
                if (building) ImGui.SetScrollHereY(1f);
                ImGui.EndChild();
                ImGui.EndChild();

                ImGui.End();
                ui.End();
            }
        }
        finally
        {
            if (buildTask != null)
                try { buildTask.Wait(); } catch { }
            ui.Dispose();
        }
    }
}
