using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using Silk.NET.Input;
using ImGuiNET;
using System.Numerics;

namespace Mako;

/// OpenGL 3.3 rendering backend for Dear ImGui, driven by Silk.NET.
unsafe sealed class ImGuiController : IDisposable
{
    private readonly GL             _gl;
    private readonly IWindow        _window;
    private readonly IInputContext  _input;

    private uint _vao, _vbo, _ebo, _shader, _fontTex;
    private int  _uniTex, _uniProj;
    private bool _disposed;

    // ── GLSL shaders ──────────────────────────────────────────────────────────

    private const string VtxSrc = """
        #version 330 core
        layout(location = 0) in vec2 aPos;
        layout(location = 1) in vec2 aUV;
        layout(location = 2) in vec4 aColor;
        uniform mat4 uProj;
        out vec2 vUV;
        out vec4 vColor;
        void main() {
            vUV    = aUV;
            vColor = aColor;
            gl_Position = uProj * vec4(aPos, 0.0, 1.0);
        }
        """;

    private const string FrgSrc = """
        #version 330 core
        in vec2 vUV;
        in vec4 vColor;
        uniform sampler2D uTex;
        out vec4 fragColor;
        void main() { fragColor = vColor * texture(uTex, vUV); }
        """;

    // ── Construction ──────────────────────────────────────────────────────────

    public ImGuiController(GL gl, IWindow window, IInputContext input)
    {
        _gl = gl; _window = window; _input = input;

        var ctx = ImGui.CreateContext();
        ImGui.SetCurrentContext(ctx);

        var io = ImGui.GetIO();
        io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset;
        io.ConfigFlags  |= ImGuiConfigFlags.NavEnableKeyboard;
        io.DisplaySize   = new Vector2(window.FramebufferSize.X, window.FramebufferSize.Y);
        io.DeltaTime     = 1f / 60f;

        UploadFontAtlas();
        BuildShader();
        BuildBuffers();
        HookInput();

        window.FramebufferResize += size =>
            ImGui.GetIO().DisplaySize = new Vector2(size.X, size.Y);
    }

    // ── Font atlas ────────────────────────────────────────────────────────────

    private void UploadFontAtlas()
    {
        var io = ImGui.GetIO();
        io.Fonts.GetTexDataAsRGBA32(out byte* pix, out int w, out int h);

        _fontTex = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, _fontTex);
        _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba,
            (uint)w, (uint)h, 0, PixelFormat.Rgba, PixelType.UnsignedByte, pix);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);

        io.Fonts.SetTexID((IntPtr)_fontTex);
        io.Fonts.ClearTexData();
    }

    // ── Shader ────────────────────────────────────────────────────────────────

    private void BuildShader()
    {
        _shader = _gl.CreateProgram();
        uint vert = Compile(ShaderType.VertexShader,   VtxSrc);
        uint frag = Compile(ShaderType.FragmentShader, FrgSrc);
        _gl.AttachShader(_shader, vert);
        _gl.AttachShader(_shader, frag);
        _gl.LinkProgram(_shader);
        _gl.GetProgram(_shader, GLEnum.LinkStatus, out int ok);
        if (ok == 0) throw new Exception($"ImGui shader link failed: {_gl.GetProgramInfoLog(_shader)}");
        _gl.DetachShader(_shader, vert); _gl.DeleteShader(vert);
        _gl.DetachShader(_shader, frag); _gl.DeleteShader(frag);

        _uniTex  = _gl.GetUniformLocation(_shader, "uTex");
        _uniProj = _gl.GetUniformLocation(_shader, "uProj");
    }

    private uint Compile(ShaderType type, string src)
    {
        var s = _gl.CreateShader(type);
        _gl.ShaderSource(s, src);
        _gl.CompileShader(s);
        _gl.GetShader(s, ShaderParameterName.CompileStatus, out int ok);
        if (ok == 0) throw new Exception($"ImGui shader compile failed ({type}): {_gl.GetShaderInfoLog(s)}");
        return s;
    }

    // ── GPU buffers ───────────────────────────────────────────────────────────

    private void BuildBuffers()
    {
        _vao = _gl.GenVertexArray();
        _vbo = _gl.GenBuffer();
        _ebo = _gl.GenBuffer();

        // Describe ImDrawVert layout (pos:8, uv:8, col:4 = stride 20).
        _gl.BindVertexArray(_vao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ebo);

        uint stride = (uint)sizeof(ImDrawVert);
        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, stride, (void*)0);
        _gl.EnableVertexAttribArray(1);
        _gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, stride, (void*)8);
        _gl.EnableVertexAttribArray(2);
        _gl.VertexAttribPointer(2, 4, VertexAttribPointerType.UnsignedByte, true,  stride, (void*)16);

        _gl.BindVertexArray(0);
    }

    // ── Input forwarding ─────────────────────────────────────────────────────

    private void HookInput()
    {
        foreach (var kb in _input.Keyboards)
        {
            kb.KeyChar += (_, c)       => ImGui.GetIO().AddInputCharacter(c);
            kb.KeyDown += (_, k, _)    => ImGui.GetIO().AddKeyEvent(MapKey(k), true);
            kb.KeyUp   += (_, k, _)    => ImGui.GetIO().AddKeyEvent(MapKey(k), false);
        }
        foreach (var m in _input.Mice)
        {
            m.MouseMove += (_, p)  => ImGui.GetIO().AddMousePosEvent(p.X, p.Y);
            m.MouseDown += (_, b)  => ImGui.GetIO().AddMouseButtonEvent(SilkBtn(b), true);
            m.MouseUp   += (_, b)  => ImGui.GetIO().AddMouseButtonEvent(SilkBtn(b), false);
            m.Scroll    += (_, s)  => ImGui.GetIO().AddMouseWheelEvent(s.X, s.Y);
        }
    }

    private static int SilkBtn(MouseButton b) => b switch
    {
        MouseButton.Left   => 0,
        MouseButton.Right  => 1,
        MouseButton.Middle => 2,
        _                  => -1,
    };

    private static ImGuiKey MapKey(Key k) => k switch
    {
        Key.Tab          => ImGuiKey.Tab,
        Key.Left         => ImGuiKey.LeftArrow,
        Key.Right        => ImGuiKey.RightArrow,
        Key.Up           => ImGuiKey.UpArrow,
        Key.Down         => ImGuiKey.DownArrow,
        Key.Home         => ImGuiKey.Home,
        Key.End          => ImGuiKey.End,
        Key.PageUp       => ImGuiKey.PageUp,
        Key.PageDown     => ImGuiKey.PageDown,
        Key.Delete       => ImGuiKey.Delete,
        Key.Backspace    => ImGuiKey.Backspace,
        Key.Enter        => ImGuiKey.Enter,
        Key.KeypadEnter  => ImGuiKey.KeypadEnter,
        Key.Escape       => ImGuiKey.Escape,
        Key.Space        => ImGuiKey.Space,
        Key.ControlLeft  => ImGuiKey.LeftCtrl,
        Key.ControlRight => ImGuiKey.RightCtrl,
        Key.ShiftLeft    => ImGuiKey.LeftShift,
        Key.ShiftRight   => ImGuiKey.RightShift,
        Key.AltLeft      => ImGuiKey.LeftAlt,
        Key.AltRight     => ImGuiKey.RightAlt,
        Key.A => ImGuiKey.A, Key.B => ImGuiKey.B, Key.C => ImGuiKey.C,
        Key.D => ImGuiKey.D, Key.E => ImGuiKey.E, Key.F => ImGuiKey.F,
        Key.G => ImGuiKey.G, Key.H => ImGuiKey.H, Key.I => ImGuiKey.I,
        Key.J => ImGuiKey.J, Key.K => ImGuiKey.K, Key.L => ImGuiKey.L,
        Key.M => ImGuiKey.M, Key.N => ImGuiKey.N, Key.O => ImGuiKey.O,
        Key.P => ImGuiKey.P, Key.Q => ImGuiKey.Q, Key.R => ImGuiKey.R,
        Key.S => ImGuiKey.S, Key.T => ImGuiKey.T, Key.U => ImGuiKey.U,
        Key.V => ImGuiKey.V, Key.W => ImGuiKey.W, Key.X => ImGuiKey.X,
        Key.Y => ImGuiKey.Y, Key.Z => ImGuiKey.Z,
        Key.Number0 => ImGuiKey._0, Key.Number1 => ImGuiKey._1,
        Key.Number2 => ImGuiKey._2, Key.Number3 => ImGuiKey._3,
        Key.Number4 => ImGuiKey._4, Key.Number5 => ImGuiKey._5,
        Key.Number6 => ImGuiKey._6, Key.Number7 => ImGuiKey._7,
        Key.Number8 => ImGuiKey._8, Key.Number9 => ImGuiKey._9,
        _ => ImGuiKey.None,
    };

    // ── Frame ─────────────────────────────────────────────────────────────────

    public void NewFrame(float dt)
    {
        ImGui.GetIO().DeltaTime = MathF.Max(dt, 0.0001f);
        ImGui.NewFrame();
    }

    public void Render()
    {
        ImGui.Render();
        RenderDrawData(ImGui.GetDrawData());
    }

    // ── Draw data → GL ────────────────────────────────────────────────────────

    private void RenderDrawData(ImDrawData* data)
    {
        if (data == null || data->CmdListsCount == 0) return;

        int fw = (int)(data->DisplaySize.X * data->FramebufferScale.X);
        int fh = (int)(data->DisplaySize.Y * data->FramebufferScale.Y);
        if (fw <= 0 || fh <= 0) return;

        _gl.Enable(EnableCap.Blend);
        _gl.BlendEquation(BlendEquationModeEXT.FuncAdd);
        _gl.BlendFuncSeparate(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha,
                              BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha);
        _gl.Disable(EnableCap.CullFace);
        _gl.Disable(EnableCap.DepthTest);
        _gl.Disable(EnableCap.StencilTest);
        _gl.Enable(EnableCap.ScissorTest);
        _gl.Viewport(0, 0, (uint)fw, (uint)fh);

        float L = data->DisplayPos.X, R = data->DisplayPos.X + data->DisplaySize.X;
        float T = data->DisplayPos.Y, B = data->DisplayPos.Y + data->DisplaySize.Y;
        float[] proj =
        [
            2f/(R-L),    0,          0, 0,
            0,           2f/(T-B),   0, 0,
            0,           0,         -1, 0,
            (R+L)/(L-R), (T+B)/(B-T), 0, 1,
        ];

        _gl.UseProgram(_shader);
        _gl.Uniform1(_uniTex, 0);
        _gl.UniformMatrix4(_uniProj, 1, false, proj.AsSpan());
        _gl.BindVertexArray(_vao);

        for (int n = 0; n < data->CmdListsCount; n++)
        {
            var cl = ((ImDrawList**)data->CmdLists.Data)[n];

            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
            _gl.BufferData(BufferTargetARB.ArrayBuffer,
                (nuint)(cl->VtxBuffer.Size * sizeof(ImDrawVert)),
                (void*)cl->VtxBuffer.Data, BufferUsageARB.StreamDraw);

            _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ebo);
            _gl.BufferData(BufferTargetARB.ElementArrayBuffer,
                (nuint)(cl->IdxBuffer.Size * 2),
                (void*)cl->IdxBuffer.Data, BufferUsageARB.StreamDraw);

            for (int ci = 0; ci < cl->CmdBuffer.Size; ci++)
            {
                var cmd = ((ImDrawCmd*)cl->CmdBuffer.Data)[ci];
                var co  = data->DisplayPos;
                var cs  = data->FramebufferScale;

                _gl.Scissor(
                    (int)((cmd.ClipRect.X - co.X) * cs.X),
                    (int)(fh - (cmd.ClipRect.W - co.Y) * cs.Y),
                    (uint)((cmd.ClipRect.Z - cmd.ClipRect.X) * cs.X),
                    (uint)((cmd.ClipRect.W - cmd.ClipRect.Y) * cs.Y));

                _gl.ActiveTexture(TextureUnit.Texture0);
                _gl.BindTexture(TextureTarget.Texture2D, (uint)(nint)cmd.TextureId);

                _gl.DrawElementsBaseVertex(
                    PrimitiveType.Triangles,
                    cmd.ElemCount,
                    DrawElementsType.UnsignedShort,
                    (void*)(cmd.IdxOffset * 2u),
                    (int)cmd.VtxOffset);
            }
        }

        _gl.BindVertexArray(0);
        _gl.UseProgram(0);
        _gl.Disable(EnableCap.ScissorTest);
    }

    // ── Cleanup ───────────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _gl.DeleteVertexArray(_vao);
        _gl.DeleteBuffer(_vbo);
        _gl.DeleteBuffer(_ebo);
        _gl.DeleteProgram(_shader);
        _gl.DeleteTexture(_fontTex);
        ImGui.DestroyContext();
    }
}
