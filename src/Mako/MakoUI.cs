using Silk.NET.Maths;
using Silk.NET.Windowing;
using Silk.NET.Windowing.Glfw;
using Silk.NET.OpenGL;
using Silk.NET.Input;
using Silk.NET.Input.Glfw;
using ImGuiNET;
using System.Numerics;
using System.Linq;

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

    // ── Menus ─────────────────────────────────────────────────────────────────

    public bool BeginMenuBar()               => ImGui.BeginMenuBar();
    public void EndMenuBar()                 => ImGui.EndMenuBar();
    public bool BeginMainMenuBar()           => ImGui.BeginMainMenuBar();
    public void EndMainMenuBar()             => ImGui.EndMainMenuBar();
    public bool BeginMenu(string label)      => ImGui.BeginMenu(label);
    public void EndMenu()                    => ImGui.EndMenu();
    public bool MenuItem(string label)       => ImGui.MenuItem(label);
    public bool MenuItem(string label, string shortcut) => ImGui.MenuItem(label, shortcut);

    // ── Popups & Modals ───────────────────────────────────────────────────────

    public void OpenPopup(string id)         => ImGui.OpenPopup(id);
    public bool BeginPopup(string id)        => ImGui.BeginPopup(id);
    public bool BeginModal(string title)
    {
        bool open = true;
        return ImGui.BeginPopupModal(title, ref open);
    }
    public void ClosePopup()                 => ImGui.CloseCurrentPopup();
    public void EndPopup()                   => ImGui.EndPopup();

    // ── Tables ────────────────────────────────────────────────────────────────

    public bool BeginTable(string id, int cols) =>
        ImGui.BeginTable(id, cols, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg);

    public bool BeginTable(string id, int cols, bool borders, bool stripes)
    {
        var flags = ImGuiTableFlags.None;
        if (borders) flags |= ImGuiTableFlags.Borders;
        if (stripes) flags |= ImGuiTableFlags.RowBg;
        return ImGui.BeginTable(id, cols, flags);
    }

    public void TableColumn(string label)    => ImGui.TableSetupColumn(label);
    public void TableHeaderRow()             => ImGui.TableHeadersRow();
    public void TableNextRow()               => ImGui.TableNextRow();
    public void TableNextCol()               => ImGui.TableNextColumn();
    public void EndTable()                   => ImGui.EndTable();

    // ── Drag widgets ──────────────────────────────────────────────────────────

    public double Drag(string label, double v, double speed = 1.0)
    {
        float fv = (float)v;
        ImGui.DragFloat(label, ref fv, (float)speed);
        return fv;
    }

    public double DragInt(string label, double v, double speed = 1.0)
    {
        int iv = (int)v;
        ImGui.DragInt(label, ref iv, (float)speed);
        return iv;
    }

    public double DragRange(string label, double lo, double hi, double speed = 1.0)
    {
        float flo = (float)lo, fhi = (float)hi;
        ImGui.DragFloatRange2(label, ref flo, ref fhi, (float)speed);
        return flo; // returns updated lo; caller reads hi via separate call or ignores
    }

    // ── Tooltips ──────────────────────────────────────────────────────────────

    public void Tooltip(string text)
    {
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.Text(text);
            ImGui.EndTooltip();
        }
    }

    public void SetTooltip(string text)      => ImGui.SetTooltip(text);

    // ── Combo / Select ────────────────────────────────────────────────────────

    public int Combo(string label, int current, List<object?> items)
    {
        var strs = items.Select(i => i?.ToString() ?? "").ToArray();
        ImGui.Combo(label, ref current, strs, strs.Length);
        return current;
    }

    // ── Text input variants ───────────────────────────────────────────────────

    public string InputTextMulti(string label, string v, int lines = 6)
    {
        ImGui.InputTextMultiline(label, ref v, 4096,
            new Vector2(-1, ImGui.GetTextLineHeight() * lines));
        return v;
    }

    // ── Window flags ─────────────────────────────────────────────────────────

    public bool BeginWindowMenuBar(string title)
    {
        bool open = true;
        return ImGui.Begin(title, ref open, ImGuiWindowFlags.MenuBar);
    }

    // ── Query ─────────────────────────────────────────────────────────────────

    public bool IsItemHovered()              => ImGui.IsItemHovered();
    public bool IsItemClicked()              => ImGui.IsItemClicked();
    public bool IsKeyPressed(int key)        => ImGui.IsKeyPressed((ImGuiKey)key);
    public double GetTime()                  => ImGui.GetTime();
    public double GetFramerate()             => ImGui.GetIO().Framerate;

    // ── Style ─────────────────────────────────────────────────────────────────

    public void PushStyleColor(int idx, double r, double g, double b, double a = 1.0) =>
        ImGui.PushStyleColor((ImGuiCol)idx, new Vector4((float)r, (float)g, (float)b, (float)a));

    public void PopStyleColor(int count = 1) => ImGui.PopStyleColor(count);

    public void PushStyleVar(int idx, double val) =>
        ImGui.PushStyleVar((ImGuiStyleVar)idx, (float)val);

    public void PopStyleVar(int count = 1) => ImGui.PopStyleVar(count);

    // ── Themes ────────────────────────────────────────────────────────────────

    public void ThemeDark()  => ImGui.StyleColorsDark();
    public void ThemeLight() => ImGui.StyleColorsLight();

    /// Cherry blossom theme — MAKO's visual identity.
    public void ThemeMako()
    {
        ImGui.StyleColorsDark();
        var s = ImGui.GetStyle();

        // Yozakura palette
        var bg        = new Vector4(0.071f, 0.043f, 0.055f, 1f);   // #120B0E deep plum
        var bgMid     = new Vector4(0.118f, 0.075f, 0.094f, 1f);   // #1E1318
        var bgHigh    = new Vector4(0.165f, 0.106f, 0.133f, 1f);   // #2A1B22
        var accent    = new Vector4(1.000f, 0.561f, 0.667f, 1f);   // #FF8FAA bright pink
        var accentDim = new Vector4(0.788f, 0.310f, 0.427f, 1f);   // #C94F6D deep rose
        var accentHov = new Vector4(1.000f, 0.690f, 0.757f, 1f);   // #FFB0C1 petal
        var text      = new Vector4(0.992f, 0.969f, 0.976f, 1f);   // #FDF7F9 petal white
        var textDim   = new Vector4(0.700f, 0.600f, 0.640f, 1f);   // muted

        ImGui.PushStyleColor(ImGuiCol.WindowBg,          bg);
        ImGui.PushStyleColor(ImGuiCol.ChildBg,           bg);
        ImGui.PushStyleColor(ImGuiCol.PopupBg,           bgMid);
        ImGui.PushStyleColor(ImGuiCol.Border,            accentDim with { W = 0.4f });
        ImGui.PushStyleColor(ImGuiCol.FrameBg,           bgHigh);
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered,    bgHigh with { W = 0.8f });
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive,     accentDim with { W = 0.3f });
        ImGui.PushStyleColor(ImGuiCol.TitleBg,           bgMid);
        ImGui.PushStyleColor(ImGuiCol.TitleBgActive,     accentDim with { W = 0.8f });
        ImGui.PushStyleColor(ImGuiCol.TitleBgCollapsed,  bg);
        ImGui.PushStyleColor(ImGuiCol.MenuBarBg,         bgMid);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarBg,       bg);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab,     accentDim);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, accentHov);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabActive,  accent);
        ImGui.PushStyleColor(ImGuiCol.CheckMark,         accent);
        ImGui.PushStyleColor(ImGuiCol.SliderGrab,        accentDim);
        ImGui.PushStyleColor(ImGuiCol.SliderGrabActive,  accent);
        ImGui.PushStyleColor(ImGuiCol.Button,            accentDim with { W = 0.6f });
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered,     accentHov with { W = 0.8f });
        ImGui.PushStyleColor(ImGuiCol.ButtonActive,      accent);
        ImGui.PushStyleColor(ImGuiCol.Header,            accentDim with { W = 0.4f });
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered,     accentDim with { W = 0.7f });
        ImGui.PushStyleColor(ImGuiCol.HeaderActive,      accent with { W = 0.8f });
        ImGui.PushStyleColor(ImGuiCol.Separator,         accentDim with { W = 0.5f });
        ImGui.PushStyleColor(ImGuiCol.Text,              text);
        ImGui.PushStyleColor(ImGuiCol.TextDisabled,      textDim);
        ImGui.PushStyleColor(ImGuiCol.Tab,               bgMid);
        ImGui.PushStyleColor(ImGuiCol.TabHovered,        accentDim);
        ImGui.PushStyleColor(ImGuiCol.TabSelected,       accentDim with { W = 0.9f });
        ImGui.PushStyleColor(ImGuiCol.TableBorderLight,  accentDim with { W = 0.2f });
        ImGui.PushStyleColor(ImGuiCol.TableBorderStrong, accentDim with { W = 0.5f });
        ImGui.PushStyleColor(ImGuiCol.TableRowBg,        bg);
        ImGui.PushStyleColor(ImGuiCol.TableRowBgAlt,     bgMid);

        s.WindowRounding    = 6f;
        s.FrameRounding     = 4f;
        s.PopupRounding     = 4f;
        s.ScrollbarRounding = 4f;
        s.GrabRounding      = 4f;
        s.TabRounding       = 4f;
        s.FramePadding      = new Vector2(8, 4);
        s.ItemSpacing       = new Vector2(8, 6);
        s.WindowPadding     = new Vector2(10, 10);
    }

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
