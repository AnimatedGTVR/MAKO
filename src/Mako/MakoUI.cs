using Silk.NET.Maths;
using Silk.NET.Windowing;
using Silk.NET.Windowing.Glfw;
using Silk.NET.OpenGL;
using Silk.NET.Input;
using Silk.NET.Input.Glfw;
using ImGuiNET;
using System.Numerics;

namespace Mako;

/// MakoUI — MAKO's immediate-mode GUI layer built on Dear ImGui + Silk.NET.
///
/// MAKO usage pattern:
///
///   using MakoUI;
///
///   main() {
///       MakoUI.init("My App", 1280, 720);
///
///       count = 0;
///
///       while MakoUI.running() {
///           MakoUI.begin();
///
///           MakoUI.begin_window("Controls");
///           MakoUI.text("Count: {count}");
///           if MakoUI.button("Increment") { count += 1; }
///           MakoUI.end_window();
///
///           MakoUI.end();
///       }
///   }
///
sealed class MakoUI : IDisposable
{
    private IWindow?         _win;
    private GL?              _gl;
    private IInputContext?   _input;
    private ImGuiController? _ctrl;
    private DateTime         _lastFrame = DateTime.UtcNow;
    private bool             _disposed;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public void Init(string title, int width, int height)
    {
        GlfwWindowing.RegisterPlatform();
        GlfwInput.RegisterPlatform();

        var opts = WindowOptions.Default with
        {
            Title  = title,
            Size   = new Vector2D<int>(width, height),
            API    = new GraphicsAPI(
                         ContextAPI.OpenGL,
                         ContextProfile.Core,
                         ContextFlags.ForwardCompatible,
                         new APIVersion(3, 3)),
            ShouldSwapAutomatically = false,
            VSync  = true,
        };

        _win = Window.Create(opts);
        _win.Initialize();

        _gl    = _win.CreateOpenGL();
        _input = _win.CreateInput();
        _ctrl  = new ImGuiController(_gl, _win, _input);
    }

    /// Process pending OS events and return false when the user closes the window.
    public bool Running()
    {
        _win!.DoEvents();
        return !_win.IsClosing;
    }

    /// Clear the framebuffer and start a new ImGui frame.
    public void Begin()
    {
        var now = DateTime.UtcNow;
        var dt  = (float)(now - _lastFrame).TotalSeconds;
        _lastFrame = now;

        _gl!.ClearColor(0.12f, 0.12f, 0.12f, 1f);
        _gl.Clear(ClearBufferMask.ColorBufferBit);

        _ctrl!.NewFrame(dt);
    }

    /// Render the ImGui frame and swap buffers.
    public void End()
    {
        _ctrl!.Render();
        _win!.SwapBuffers();
    }

    // ── Windows ───────────────────────────────────────────────────────────────

    public bool BeginWindow(string title)         => ImGui.Begin(title);
    public bool BeginWindow(string title, bool open)
    {
        ImGui.Begin(title, ref open);
        return open;
    }
    public void EndWindow()                        => ImGui.End();

    // ── Layout ────────────────────────────────────────────────────────────────

    public void Separator()                        => ImGui.Separator();
    public void SameLine()                         => ImGui.SameLine();
    public void Spacing()                          => ImGui.Spacing();
    public void NewLine()                          => ImGui.NewLine();

    public void SetNextWindowSize(double w, double h) =>
        ImGui.SetNextWindowSize(new Vector2((float)w, (float)h), ImGuiCond.Once);

    public void SetNextWindowPos(double x, double y) =>
        ImGui.SetNextWindowPos(new Vector2((float)x, (float)y), ImGuiCond.Once);

    // ── Widgets ───────────────────────────────────────────────────────────────

    public void   Text(string s)              => ImGui.Text(s);
    public void   TextColored(double r, double g, double b, double a, string s) =>
        ImGui.TextColored(new Vector4((float)r, (float)g, (float)b, (float)a), s);

    public bool   Button(string label)        => ImGui.Button(label);
    public bool   SmallButton(string label)   => ImGui.SmallButton(label);

    public bool   Checkbox(string label, bool v)
    {
        ImGui.Checkbox(label, ref v);
        return v;
    }

    public double Slider(string label, double v, double lo, double hi)
    {
        float fv = (float)v;
        ImGui.SliderFloat(label, ref fv, (float)lo, (float)hi);
        return fv;
    }

    public double SliderInt(string label, double v, double lo, double hi)
    {
        int iv = (int)v;
        ImGui.SliderInt(label, ref iv, (int)lo, (int)hi);
        return iv;
    }

    public string InputText(string label, string v)
    {
        ImGui.InputText(label, ref v, 512);
        return v;
    }

    public double InputNumber(string label, double v)
    {
        float fv = (float)v;
        ImGui.InputFloat(label, ref fv);
        return fv;
    }

    public bool CollapsingHeader(string label)    => ImGui.CollapsingHeader(label);

    public void ProgressBar(double fraction, string? overlay = null)
    {
        var size = new Vector2(-1, 0);
        if (overlay != null) ImGui.ProgressBar((float)fraction, size, overlay);
        else                  ImGui.ProgressBar((float)fraction, size);
    }

    // ── Style ─────────────────────────────────────────────────────────────────

    public void PushStyleColor(int idx, double r, double g, double b, double a = 1.0) =>
        ImGui.PushStyleColor((ImGuiCol)idx, new Vector4((float)r, (float)g, (float)b, (float)a));

    public void PopStyleColor(int count = 1) => ImGui.PopStyleColor(count);

    public void PushStyleVar(int idx, double val) =>
        ImGui.PushStyleVar((ImGuiStyleVar)idx, (float)val);

    public void PopStyleVar(int count = 1) => ImGui.PopStyleVar(count);

    // ── Cleanup ───────────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _ctrl?.Dispose();
        _input?.Dispose();
        _win?.Dispose();
    }
}
