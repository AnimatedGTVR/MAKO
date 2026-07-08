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

/// MakoUI — MAKO's immediate-mode GUI layer built on Dear ImGui.
///
/// Two ways to use it:
///
/// **Standalone window** (its own Silk.NET/OpenGL window — for tools/editors):
///
///   using MakoUI;
///
///   main() {
///       MakoUI.init("My App", 1280, 720);
///       count = 0;
///       while MakoUI.running() {
///           MakoUI.begin();
///           MakoUI.begin_window("Controls");
///           MakoUI.text("Count: {count}");
///           if MakoUI.button("Increment") { count += 1; }
///           MakoUI.end_window();
///           MakoUI.end();
///       }
///   }
///
/// **Embedded in a Mako3D/Mako2D window** (a toolbar/panel over the scene —
/// no second window, renders via raylib's own GL context):
///
///   using Mako3D;
///   using MakoUI;
///
///   main() {
///       Mako3D.init(1280, 720, "My Game");
///       MakoUI.attach();                 # attach to the already-open window
///       while Mako3D.running() {
///           Mako3D.begin();
///           Mako3D.clear(Mako3D.BLACK);
///           Mako3D.begin_3d(cam); ... Mako3D.end_3d();
///
///           MakoUI.begin();               # ImGui frame — no clear, no swap
///           MakoUI.begin_window("Toolbar");
///           MakoUI.fps_counter();
///           MakoUI.end_window();
///           MakoUI.end();                 # renders ImGui — no swap
///
///           Mako3D.end();                 # swaps buffers once, for the whole frame
///       }
///   }
///
sealed class MakoUI : IDisposable
{
    private IWindow?               _win;
    private GL?                    _gl;
    private IInputContext?         _input;
    private ImGuiController?       _ctrl;
    private ImGuiRaylibController? _rlCtrl;
    private bool                   _embedded;
    private DateTime                _lastFrame = DateTime.UtcNow;
    private bool                    _disposed;

    // Rolling FPS history for FpsCounter() — independent of ImGui's own
    // internal average, since a dedicated widget should track and show its
    // own real samples rather than just printing a borrowed number.
    private readonly float[] _fpsHistory = new float[90];
    private int _fpsIndex;
    private int _fpsSamples;

    // ── Lifecycle — standalone window ────────────────────────────────────────

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

    // ── Lifecycle — embedded in an already-open raylib window ────────────────

    /// Attach MakoUI to a raylib window already opened by Mako3D/Mako2D/MakoRay,
    /// instead of opening a second window. Call once, after that window's init.
    public void Attach()
    {
        if (!Raylib_cs.Raylib.IsWindowReady())
            throw new MakoError("MakoUI.attach() requires a window already open — call Mako3D.init()/Mako2D.init() first");
        _embedded = true;
        _rlCtrl = new ImGuiRaylibController();
    }

    /// Process pending OS events and return false when the user closes the window.
    public bool Running()
    {
        if (_embedded) return !Raylib_cs.Raylib.WindowShouldClose();
        _win!.DoEvents();
        return !_win.IsClosing;
    }

    /// Start a new ImGui frame. In embedded mode this does NOT clear the
    /// framebuffer or begin a raylib frame — Mako3D/Mako2D own that.
    public void Begin()
    {
        if (_embedded)
        {
            _rlCtrl!.NewFrame();
            RecordFpsSample(Raylib_cs.Raylib.GetFrameTime());
            return;
        }

        var now = DateTime.UtcNow;
        var dt  = (float)(now - _lastFrame).TotalSeconds;
        _lastFrame = now;
        RecordFpsSample(dt);

        _gl!.ClearColor(0.12f, 0.12f, 0.12f, 1f);
        _gl.Clear(ClearBufferMask.ColorBufferBit);

        _ctrl!.NewFrame(dt);
    }

    /// Render the ImGui frame. In embedded mode this does NOT swap buffers —
    /// Mako3D.end()/Mako2D.end() do that once for the whole combined frame.
    public void End()
    {
        if (_embedded)
        {
            _rlCtrl!.RenderDrawData();
            return;
        }

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

    /// A real toolbar: a slim strip pinned to the top-left of the window,
    /// spanning its full width, with no title bar and no resize/move/scroll —
    /// a fixed row of buttons/icons, not a floating panel. Follow with
    /// same_line() between buttons, exactly like a real app's toolbar.
    public bool BeginToolbar(string id, double height = 44)
    {
        var vp = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(vp.WorkPos);
        ImGui.SetNextWindowSize(new Vector2(vp.WorkSize.X, (float)height));
        return ImGui.Begin(id,
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize |
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoScrollbar |
            ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoCollapse);
    }

    // ── Query ─────────────────────────────────────────────────────────────────

    public bool IsItemHovered()              => ImGui.IsItemHovered();
    public bool IsItemClicked()              => ImGui.IsItemClicked();
    public bool IsKeyPressed(int key)        => ImGui.IsKeyPressed((ImGuiKey)key);
    public double GetTime()                  => ImGui.GetTime();
    public double GetFramerate()             => ImGui.GetIO().Framerate;

    /// True while the mouse is over a MakoUI panel/widget — check this before
    /// doing your own 3D picking so a click on the UI doesn't also select an
    /// object underneath it.
    public bool WantsMouse()                  => ImGui.GetIO().WantCaptureMouse;

    // ── Tabs ──────────────────────────────────────────────────────────────────

    public bool BeginTabBar(string id)       => ImGui.BeginTabBar(id);
    public void EndTabBar()                  => ImGui.EndTabBar();
    public bool BeginTabItem(string label)    => ImGui.BeginTabItem(label);
    public void EndTabItem()                  => ImGui.EndTabItem();

    // ── FPS counter ───────────────────────────────────────────────────────────

    private void RecordFpsSample(float dt)
    {
        _fpsHistory[_fpsIndex] = dt > 0.00001f ? 1f / dt : 0f;
        _fpsIndex = (_fpsIndex + 1) % _fpsHistory.Length;
        if (_fpsSamples < _fpsHistory.Length) _fpsSamples++;
    }

    /// A real, self-tracked FPS widget: current value (color-coded) plus a
    /// live rolling graph — not a static number, not borrowed from vsync.
    public void FpsCounter()
    {
        if (_fpsSamples == 0) { ImGui.Text("FPS: --"); return; }

        int last = (_fpsIndex - 1 + _fpsHistory.Length) % _fpsHistory.Length;
        float current = _fpsHistory[last];

        var color = current >= 55 ? new Vector4(0.4f, 0.9f, 0.4f, 1f)
                  : current >= 30 ? new Vector4(0.95f, 0.8f, 0.3f, 1f)
                  :                 new Vector4(0.95f, 0.35f, 0.35f, 1f);

        ImGui.TextColored(color, $"{current:0} FPS");
        ImGui.SameLine();
        ImGui.TextDisabled($"({1000f / Math.Max(current, 0.01f):0.0} ms)");

        // Build a display buffer starting from the oldest sample so the
        // graph scrolls left-to-right in real time.
        Span<float> ordered = stackalloc float[_fpsSamples];
        for (int i = 0; i < _fpsSamples; i++)
            ordered[i] = _fpsHistory[(_fpsIndex - _fpsSamples + i + _fpsHistory.Length) % _fpsHistory.Length];

        ImGui.PlotLines("##fps_graph", ref ordered[0], _fpsSamples, 0, "",
            0f, 120f, new Vector2(200, 40));
    }

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
        // Embedded mode: the window belongs to Mako3D/Mako2D, not us — only
        // dispose our own ImGui renderer, never close the shared window.
        _rlCtrl?.Dispose();
        _ctrl?.Dispose();
        _input?.Dispose();
        _win?.Dispose();
    }
}
