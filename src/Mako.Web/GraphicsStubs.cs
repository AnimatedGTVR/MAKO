namespace Mako;

/// Stand-in for MAKO's native, raylib/ImGui-bound graphics and input
/// packages (MakoRay, MakoRay2D, MakoRay3D, MakoUI, MakoAudio, MakoInputs)
/// in the browser-wasm build. Interpreter.cs, Physics2D, and Physics3D are
/// all graphics-free and compile/run unmodified for web — only these six
/// classes have no WASM-compatible implementation (raylib-cs and
/// Silk.NET.Windowing.Glfw are native OpenGL/GLFW libraries with no
/// WebAssembly target), so this project excludes the real files
/// (MakoRay*.cs, MakoUI.cs, MakoAudio.cs, MakoInputs.cs, ImGui*.cs — see
/// Mako.Web.csproj's <Compile Remove> list) and substitutes this file
/// instead: same namespace, same class/member names Interpreter.cs already
/// calls, but every entry point throws a clear, specific error instead of
/// silently doing nothing or crashing on a missing native library.
///
/// This keeps Interpreter.cs itself completely unmodified between the
/// native and web builds — no #if scattered through 1800+ lines of working
/// interpreter code — at the cost of this one file needing to stay in sync
/// with the real graphics classes' public surface if that surface changes.
internal static class WebUnsupported
{
    public const string Message =
        "this requires graphics/audio/input, which aren't available in a web (WASM) build yet — " +
        "see docs/foundry.md for the web target's current scope (language-only)";

    public static MakoError Error(string what) => new($"{what}() {Message}");
}

static class MakoRay
{
    public static readonly Dictionary<string, object?> Colors = new();
    public static readonly Dictionary<string, Func<List<object?>, object?>> Funcs = new()
    {
        ["init"] = _ => throw WebUnsupported.Error("MakoRay.init"),
    };

    // Interpreter.Execute's cleanup path checks IsWindowReady() before
    // CloseWindow() — always false here, since nothing in the web build
    // can ever set _rayActive/_ray2DActive/_ray3DActive true in the first
    // place (their init() calls throw before getting that far).
    public static bool IsWindowReady() => false;
    public static void CloseWindow() { }
}

static class MakoRay2D
{
    public static readonly Dictionary<string, object?> Colors = new();
    public static readonly Dictionary<string, Func<List<object?>, object?>> Funcs = new()
    {
        ["init"] = _ => throw WebUnsupported.Error("Mako2D.init"),
    };
    public static void UnloadAll() { }
}

static class MakoRay3D
{
    public static readonly Dictionary<string, object?> Colors = new();
    public static readonly Dictionary<string, Func<List<object?>, object?>> Funcs = new()
    {
        ["init"] = _ => throw WebUnsupported.Error("Mako3D.init"),
    };
    public static void UnloadAll() { }
}

static class MakoAudio
{
    public static readonly Dictionary<string, Func<List<object?>, object?>> Funcs = new()
    {
        ["init"] = _ => throw WebUnsupported.Error("Audio.init"),
    };
    public static void UnloadAll() { }
}

static class MakoInputs
{
    public static readonly Dictionary<string, Func<List<object?>, object?>> Funcs = new()
    {
        ["key_down"] = _ => throw WebUnsupported.Error("Inputs.key_down"),
    };
}

/// Net is excluded from the web build for a different reason than
/// graphics: it's not a native-library problem, it's that MakoNet.cs
/// blocks synchronously (GetAwaiter().GetResult()) waiting on HttpClient,
/// which deadlocks on browser-wasm's single-threaded runtime — there's no
/// background thread pool to run the awaited task on while the calling
/// thread blocks. Making Net work for real in web builds needs an
/// async-all-the-way-through call path through the interpreter, which is
/// real future work, not something this stub can paper over.
static class MakoNet
{
    public static readonly Dictionary<string, Func<List<object?>, object?>> Funcs = new()
    {
        ["get"] = _ => throw WebUnsupported.Error("Net.get"),
    };
}

/// MakoUI's constructor throws immediately, so `using MakoUI;` fails the
/// moment a web-built script tries to activate it — every other method
/// below exists only to satisfy Interpreter.cs's call sites at compile
/// time; none are ever reachable, since _ui can never hold a live instance.
sealed class MakoUI : IDisposable
{
    public MakoUI() => throw WebUnsupported.Error("MakoUI/IMGUI");

    public void Init(string title, int width, int height) => throw WebUnsupported.Error("MakoUI.init");
    public void Attach() => throw WebUnsupported.Error("MakoUI.attach");
    public bool Running() => throw WebUnsupported.Error("MakoUI.running");
    public void Begin() => throw WebUnsupported.Error("MakoUI.begin");
    public void End() => throw WebUnsupported.Error("MakoUI.end");
    public bool BeginWindow(string title) => throw WebUnsupported.Error("MakoUI.begin_window");
    public bool BeginWindow(string title, bool open) => throw WebUnsupported.Error("MakoUI.begin_window");
    public void EndWindow() => throw WebUnsupported.Error("MakoUI.end_window");
    public void Separator() => throw WebUnsupported.Error("MakoUI.separator");
    public void SameLine() => throw WebUnsupported.Error("MakoUI.same_line");
    public void Spacing() => throw WebUnsupported.Error("MakoUI.spacing");
    public void NewLine() => throw WebUnsupported.Error("MakoUI.new_line");
    public void SetNextWindowSize(double w, double h) => throw WebUnsupported.Error("MakoUI.set_window_size");
    public void SetNextWindowPos(double x, double y) => throw WebUnsupported.Error("MakoUI.set_window_pos");
    public void Text(string s) => throw WebUnsupported.Error("MakoUI.text");
    public void TextColored(string s, double r, double g, double b, double a = 1.0) => throw WebUnsupported.Error("MakoUI.text_colored");
    public bool Button(string label) => throw WebUnsupported.Error("MakoUI.button");
    public bool SmallButton(string label) => throw WebUnsupported.Error("MakoUI.small_button");
    public bool Checkbox(string label, bool v) => throw WebUnsupported.Error("MakoUI.checkbox");
    public double Slider(string label, double v, double lo, double hi) => throw WebUnsupported.Error("MakoUI.slider");
    public double SliderInt(string label, double v, double lo, double hi) => throw WebUnsupported.Error("MakoUI.slider_int");
    public string InputText(string label, string v) => throw WebUnsupported.Error("MakoUI.input_text");
    public double InputNumber(string label, double v) => throw WebUnsupported.Error("MakoUI.input_number");
    public bool CollapsingHeader(string label) => throw WebUnsupported.Error("MakoUI.collapsing");
    public void ProgressBar(double fraction, string? overlay = null) => throw WebUnsupported.Error("MakoUI.progress");
    public bool BeginMenuBar() => throw WebUnsupported.Error("MakoUI.begin_menu_bar");
    public void EndMenuBar() => throw WebUnsupported.Error("MakoUI.end_menu_bar");
    public bool BeginMainMenuBar() => throw WebUnsupported.Error("MakoUI.begin_main_menu_bar");
    public void EndMainMenuBar() => throw WebUnsupported.Error("MakoUI.end_main_menu_bar");
    public bool BeginMenu(string label) => throw WebUnsupported.Error("MakoUI.begin_menu");
    public void EndMenu() => throw WebUnsupported.Error("MakoUI.end_menu");
    public bool MenuItem(string label) => throw WebUnsupported.Error("MakoUI.menu_item");
    public bool MenuItem(string label, string shortcut) => throw WebUnsupported.Error("MakoUI.menu_item");
    public void OpenPopup(string id) => throw WebUnsupported.Error("MakoUI.open_popup");
    public bool BeginPopup(string id) => throw WebUnsupported.Error("MakoUI.begin_popup");
    public bool BeginModal(string title) => throw WebUnsupported.Error("MakoUI.begin_modal");
    public void ClosePopup() => throw WebUnsupported.Error("MakoUI.close_popup");
    public void EndPopup() => throw WebUnsupported.Error("MakoUI.end_popup");
    public bool BeginTable(string id, int cols) => throw WebUnsupported.Error("MakoUI.begin_table");
    public bool BeginTable(string id, int cols, bool borders, bool stripes) => throw WebUnsupported.Error("MakoUI.begin_table");
    public void TableColumn(string label) => throw WebUnsupported.Error("MakoUI.table_column");
    public void TableHeaderRow() => throw WebUnsupported.Error("MakoUI.table_header_row");
    public void TableNextRow() => throw WebUnsupported.Error("MakoUI.table_next_row");
    public void TableNextCol() => throw WebUnsupported.Error("MakoUI.table_next_col");
    public void EndTable() => throw WebUnsupported.Error("MakoUI.end_table");
    public double Drag(string label, double v, double speed = 1.0) => throw WebUnsupported.Error("MakoUI.drag");
    public double DragInt(string label, double v, double speed = 1.0) => throw WebUnsupported.Error("MakoUI.drag_int");
    public List<object?> DragRange(string label, double lo, double hi, double speed = 1.0) => throw WebUnsupported.Error("MakoUI.drag_range");
    public void Tooltip(string text) => throw WebUnsupported.Error("MakoUI.tooltip");
    public void SetTooltip(string text) => throw WebUnsupported.Error("MakoUI.set_tooltip");
    public int Combo(string label, int current, List<object?> items) => throw WebUnsupported.Error("MakoUI.combo");
    public string InputTextMulti(string label, string v, int lines = 6) => throw WebUnsupported.Error("MakoUI.input_text_multi");
    public bool BeginWindowMenuBar(string title) => throw WebUnsupported.Error("MakoUI.begin_window_menu");
    public bool BeginToolbar(string id, double height = 44) => throw WebUnsupported.Error("MakoUI.begin_toolbar");
    public bool IsItemHovered() => throw WebUnsupported.Error("MakoUI.is_hovered");
    public bool IsItemClicked() => throw WebUnsupported.Error("MakoUI.is_clicked");
    public bool IsKeyPressed(int key) => throw WebUnsupported.Error("MakoUI.is_key_pressed");
    public double GetTime() => throw WebUnsupported.Error("MakoUI.get_time");
    public double GetFramerate() => throw WebUnsupported.Error("MakoUI.framerate");
    public bool WantsMouse() => throw WebUnsupported.Error("MakoUI.wants_mouse");
    public bool WantsKeyboard() => throw WebUnsupported.Error("MakoUI.wants_keyboard");
    public List<object?> ColorPicker(string label, double r, double g, double b) => throw WebUnsupported.Error("MakoUI.color_picker");
    public bool BeginTabBar(string id) => throw WebUnsupported.Error("MakoUI.begin_tab_bar");
    public void EndTabBar() => throw WebUnsupported.Error("MakoUI.end_tab_bar");
    public bool BeginTabItem(string label) => throw WebUnsupported.Error("MakoUI.begin_tab_item");
    public void EndTabItem() => throw WebUnsupported.Error("MakoUI.end_tab_item");
    public void FpsCounter() => throw WebUnsupported.Error("MakoUI.fps_counter");
    public void PushStyleColor(int idx, double r, double g, double b, double a = 1.0) => throw WebUnsupported.Error("MakoUI.push_color");
    public void PopStyleColor(int count = 1) => throw WebUnsupported.Error("MakoUI.pop_color");
    public void PushStyleVar(int idx, double val) => throw WebUnsupported.Error("MakoUI.push_var");
    public void PopStyleVar(int count = 1) => throw WebUnsupported.Error("MakoUI.pop_var");
    public void ThemeDark() => throw WebUnsupported.Error("MakoUI.theme_dark");
    public void ThemeLight() => throw WebUnsupported.Error("MakoUI.theme_light");
    public void ThemeMako() => throw WebUnsupported.Error("MakoUI.theme_mako");
    public void Preview(int handle, float width, float height) => throw WebUnsupported.Error("MakoUI.preview");
    public void Dispose() { }
}
