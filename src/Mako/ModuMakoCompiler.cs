using System.Text;
using System.Text.RegularExpressions;

namespace Mako;

/// Lowers the deliberately small ModuMAKO authoring surface to ModuCPP.
/// Modularity remains responsible for native compilation and the script ABI.
static partial class ModuMakoCompiler
{
    private static readonly HashSet<string> StandaloneRuntimePackages = new(StringComparer.Ordinal)
    {
        "MakoUI", "MakoGUI", "Mako2D", "Mako3D", "MakoRay", "MakoRay2D", "MakoRay3D",
        "Physics2D", "Physics3D", "Inputs", "Players", "Controllers", "Models",
    };

    private static readonly HashSet<string> Hooks = new(StringComparer.Ordinal)
    {
        "Begin", "TickUpdate", "Update", "Spec", "TestEditor",
        "RenderEditorWindow", "ExitRenderEditorWindow", "Script_OnInspector",
        "OnCollideEnter", "OnCollideHold", "OnCollideExit",
    };

    public static string Compile(string source, string sourceName = "<memory>")
    {
        source = source.Replace("\r\n", "\n");
        var lines = source.Split('\n');
        var output = new StringBuilder();
        var modules = new List<string>();
        string? scriptName = null;
        string? scriptBase = null;
        bool inHook = false;
        int hookDepth = 0;
        var locals = new HashSet<string>(StringComparer.Ordinal);
        var fields = new HashSet<string>(StringComparer.Ordinal);

        for (int index = 0; index < lines.Length; index++)
        {
            string line = lines[index];
            string trimmed = line.Trim();
            int lineNo = index + 1;

            if (trimmed.Length == 0) { output.AppendLine(); continue; }
            if (trimmed.StartsWith('#'))
            {
                int marker = line.IndexOf('#');
                output.AppendLine(line[..marker] + "//" + line[(marker + 1)..]);
                continue;
            }

            var script = ScriptHeader().Match(trimmed);
            if (script.Success)
            {
                if (scriptName != null) Error(sourceName, lineNo, "only one ModuMAKO script declaration is allowed");
                scriptName = script.Groups[1].Value;
                scriptBase = script.Groups[2].Value;
                if (scriptBase != "ModuNode")
                    Error(sourceName, lineNo, $"unsupported script environment '{scriptBase}'; use ModuNode");
                continue;
            }

            var import = UsingLine().Match(trimmed);
            if (import.Success && !inHook)
            {
                string name = import.Groups[1].Value;
                if (StandaloneRuntimePackages.Contains(name))
                    Error(sourceName, lineNo,
                        $"'{name}' belongs to MAKO's standalone runtime; ModuMAKO uses Modularity for UI, physics, rendering, input, and scene behavior");
                string module = name switch
                {
                    "ModuMAKO" => "ModuCPP",
                    "ModuEngine" => "ModuEngine",
                    "ModuInput" => "ModuInput",
                    _ => name,
                };
                if (!modules.Contains(module)) modules.Add(module);
                continue;
            }

            var hook = HookHeader().Match(trimmed);
            if (!inHook && hook.Success && Hooks.Contains(hook.Groups[1].Value))
            {
                EnsureHeader(sourceName, lineNo, scriptName, scriptBase);
                if (output.Length == 0 || !output.ToString().Contains($"public class {Sanitize(scriptName!)}"))
                    EmitPreamble(output, modules, scriptName!);
                string name = hook.Groups[1].Value;
                string args = LowerParameters(hook.Groups[2].Value);
                output.AppendLine($"    // @modumako-source-line {lineNo}");
                output.AppendLine($"    void {name}({args}) {{");
                inHook = true;
                hookDepth = 1;
                locals.Clear();
                continue;
            }

            var helper = HelperHeader().Match(trimmed);
            if (!inHook && helper.Success)
            {
                EnsureHeader(sourceName, lineNo, scriptName, scriptBase);
                if (output.Length == 0 || !output.ToString().Contains($"public class {Sanitize(scriptName!)}"))
                    EmitPreamble(output, modules, scriptName!);
                string returnAnnotation = helper.Groups[3].Value;
                if (returnAnnotation.Length == 0)
                    Error(sourceName, lineNo, $"native helper '{helper.Groups[1].Value}' needs a return type, such as '-> f32' or '-> none'");
                string returnType = returnAnnotation == "none" ? "void" : MapType(returnAnnotation);
                string args = LowerParameters(helper.Groups[2].Value, sourceName, lineNo, requireTypes: true);
                output.AppendLine($"    // @modumako-source-line {lineNo}");
                output.AppendLine($"    private {returnType} {helper.Groups[1].Value}({args}) {{");
                inHook = true;
                hookDepth = 1;
                locals.Clear();
                continue;
            }

            if (!inHook)
            {
                var field = FieldLine().Match(trimmed);
                if (!field.Success)
                    Error(sourceName, lineNo, "expected a typed field or lifecycle hook such as Begin() or TickUpdate()");
                EnsureHeader(sourceName, lineNo, scriptName, scriptBase);
                if (output.Length == 0 || !output.ToString().Contains($"public class {Sanitize(scriptName!)}"))
                    EmitPreamble(output, modules, scriptName!);
                string name = field.Groups[1].Value;
                string annotation = field.Groups[2].Value;
                string value = field.Groups[3].Value;
                string type = annotation.Length > 0 ? MapType(annotation) : InferFieldType(value, sourceName, lineNo, name);
                string visibility = name.StartsWith('_') ? "private" : "public";
                fields.Add(name);
                string initializer = value.Trim() == "none" && type == "SceneObj"
                    ? ""
                    : $" = {LowerExpression(value, type)}";
                output.AppendLine($"    // @modumako-source-line {lineNo}");
                output.AppendLine($"    {visibility} {type} {name}{initializer};");
                continue;
            }

            int parenthesisBalance = CountOutsideStrings(line, '(') - CountOutsideStrings(line, ')');
            if (parenthesisBalance > 0)
            {
                string indent = line[..(line.Length - line.TrimStart().Length)];
                var statement = new StringBuilder(trimmed);
                while (parenthesisBalance > 0 && index + 1 < lines.Length)
                {
                    string continuation = lines[++index].Trim();
                    statement.Append(' ').Append(continuation);
                    parenthesisBalance += CountOutsideStrings(continuation, '(') -
                                          CountOutsideStrings(continuation, ')');
                }
                if (parenthesisBalance != 0)
                    Error(sourceName, lineNo, "statement is missing a closing ')'");
                line = indent + statement;
            }

            int closes = CountOutsideStrings(line, '}');
            int opens = CountOutsideStrings(line, '{');
            hookDepth += opens - closes;
            if (hookDepth <= 0)
            {
                output.AppendLine("    }");
                inHook = false;
                continue;
            }

            string lowered = LowerStatement(line, locals, fields, sourceName, lineNo);
            output.AppendLine($"    // @modumako-source-line {lineNo}");
            output.AppendLine("    " + lowered);
        }

        EnsureHeader(sourceName, 1, scriptName, scriptBase);
        if (inHook) Error(sourceName, lines.Length, "lifecycle hook is missing a closing '}'");
        if (!output.ToString().Contains($"public class {Sanitize(scriptName!)}"))
            EmitPreamble(output, modules, scriptName!);
        output.AppendLine("};");
        return output.ToString();
    }

    private static void EmitPreamble(StringBuilder output, List<string> modules, string scriptName)
    {
        if (!modules.Contains("ModuCPP")) modules.Insert(0, "ModuCPP");
        var existing = output.ToString();
        output.Clear();
        foreach (string module in modules) output.AppendLine($"add {module};");
        output.AppendLine();
        output.AppendLine($"public class {Sanitize(scriptName)} : ModuNode {{");
        output.Append(existing);
    }

    private static string LowerStatement(string line, HashSet<string> locals, HashSet<string> fields,
                                         string sourceName, int lineNo)
    {
        string indent = line[..(line.Length - line.TrimStart().Length)];
        string text = line.Trim();
        if (text.StartsWith('#')) return indent + "//" + text[1..];
        var inlineControl = InlineControlBlock().Match(text);
        if (inlineControl.Success)
        {
            string keyword = inlineControl.Groups[1].Value;
            string loweredCondition = LowerExpression(inlineControl.Groups[2].Value.Trim(), null);
            string body = LowerStatement(inlineControl.Groups[3].Value.Trim(), locals, fields, sourceName, lineNo).Trim();
            return indent + keyword + " (" + loweredCondition + ") { " + body + " }";
        }
        var inlineElse = InlineElseBlock().Match(text);
        if (inlineElse.Success)
        {
            string body = LowerStatement(inlineElse.Groups[1].Value.Trim(), locals, fields, sourceName, lineNo).Trim();
            return indent + "else { " + body + " }";
        }
        var inlineElseIf = InlineElseIfBlock().Match(text);
        if (inlineElseIf.Success)
        {
            string loweredElseIfCondition = LowerExpression(inlineElseIf.Groups[1].Value.Trim(), null);
            string body = LowerStatement(inlineElseIf.Groups[2].Value.Trim(), locals, fields, sourceName, lineNo).Trim();
            return indent + "else if (" + loweredElseIfCondition + ") { " + body + " }";
        }
        var condition = ControlHeader().Match(text);
        if (condition.Success)
            return indent + condition.Groups[1].Value + " (" +
                   LowerExpression(condition.Groups[2].Value.Trim(), null) + ") {";
        var inlineRange = InlineRangeLoop().Match(text);
        if (inlineRange.Success)
        {
            string variable = inlineRange.Groups[1].Value;
            string loop = LowerRangeHeader(variable, inlineRange.Groups[2].Value, locals);
            string body = LowerExpression(inlineRange.Groups[3].Value.Trim(), null);
            return indent + loop + body + " }";
        }
        var range = RangeLoop().Match(text);
        if (range.Success)
        {
            string variable = range.Groups[1].Value;
            return indent + LowerRangeHeader(variable, range.Groups[2].Value, locals);
        }
        var inlineIterable = InlineIterableLoop().Match(text);
        if (inlineIterable.Success)
        {
            string variable = inlineIterable.Groups[1].Value;
            locals.Add(variable);
            string iterable = LowerExpression(inlineIterable.Groups[2].Value.Trim(), null);
            string body = LowerStatement(inlineIterable.Groups[3].Value.Trim(), locals, fields, sourceName, lineNo).Trim();
            return indent + $"for (auto {variable} : {iterable}) {{ {body} }}";
        }
        var iterableLoop = IterableLoop().Match(text);
        if (iterableLoop.Success)
        {
            string variable = iterableLoop.Groups[1].Value;
            locals.Add(variable);
            string iterable = LowerExpression(iterableLoop.Groups[2].Value.Trim(), null);
            return indent + $"for (auto {variable} : {iterable}) {{";
        }
        var typedAssignment = TypedLocalAssignment().Match(text);
        if (typedAssignment.Success)
        {
            string name = typedAssignment.Groups[1].Value;
            string mappedType = MapType(typedAssignment.Groups[2].Value);
            string value = LowerExpression(typedAssignment.Groups[3].Value, mappedType);
            locals.Add(name);
            return indent + mappedType + " " + name + " = " + value + ";";
        }
        var assignment = LocalAssignment().Match(text);
        if (assignment.Success)
        {
            string name = assignment.Groups[1].Value;
            string rawValue = assignment.Groups[2].Value;
            if (!fields.Contains(name) && !locals.Contains(name) && IsListLiteral(rawValue))
            {
                string listType = InferListType(rawValue, name, sourceName, lineNo);
                locals.Add(name);
                return indent + listType + " " + name + " = " + LowerExpression(rawValue, listType) + ";";
            }
            string prefix = !fields.Contains(name) && locals.Add(name) ? "auto " : "";
            return indent + prefix + name + " = " + LowerExpression(rawValue, null) + ";";
        }
        return indent + LowerExpression(text, null);
    }

    private static string LowerRangeHeader(string variable, string arguments, HashSet<string> locals)
    {
        var args = SplitArguments(arguments);
        if (args.Count is < 1 or > 3)
            throw new MakoError("ModuMAKO range() expects one, two, or three arguments");
        string start = args.Count == 1 ? "0" : args[0];
        string stop = args.Count == 1 ? args[0] : args[1];
        string step = args.Count == 3 ? args[2] : "1";
        bool descending = double.TryParse(step, out double numericStep) && numericStep < 0;
        string compare = descending ? ">" : "<";
        string advance = step == "1" ? $"++{variable}" : step == "-1" ? $"--{variable}" : $"{variable} += {step}";
        locals.Add(variable);
        return $"for (int {variable} = {LowerExpression(start, null)}; {variable} {compare} {LowerExpression(stop, null)}; {advance}) {{";
    }

    private static string LowerExpression(string value, string? type)
    {
        string result = LowerInterpolatedStrings(value);
        if (IsListType(type) && IsListLiteral(result))
            result = "{" + result.Trim()[1..^1] + "}";
        result = Regex.Replace(result, @"\band\b", "&&");
        result = Regex.Replace(result, @"\bor\b", "||");
        result = Regex.Replace(result, @"\bnot\b", "!");
        result = Regex.Replace(result, @"\bnone\b", "nullptr");
        result = Regex.Replace(result, @"\bStandaloneMovement(Settings|State|Debug)\s*\(", "ScriptContext::StandaloneMovement$1(");
        result = Regex.Replace(result, @"\bTickStandaloneMovement\s*\((.*),\s*([A-Za-z_][A-Za-z0-9_]*)\s*\)", "ctx.TickStandaloneMovement($1, &$2)");
        result = Regex.Replace(result, @"\bAddRigidbodyImpulse\s*\(", "ctx.AddRigidbodyImpulse(");
        result = Regex.Replace(result,
            @"\bpush\s*\(\s*([A-Za-z_][A-Za-z0-9_]*)\s*,\s*(.+)\)\s*;?$",
            "$1.push_back($2);");
        result = Regex.Replace(result,
            @"\blen\s*\(\s*([A-Za-z_][A-Za-z0-9_]*)\s*\)",
            "static_cast<std::int64_t>($1.size())");
        result = Regex.Replace(result, @"\bpop\s*\(\s*([A-Za-z_][A-Za-z0-9_]*)\s*\)", "MakoPop($1)");
        result = Regex.Replace(result, @"\bfirst\s*\(\s*([A-Za-z_][A-Za-z0-9_]*)\s*\)", "MakoFirst($1)");
        result = Regex.Replace(result, @"\blast\s*\(\s*([A-Za-z_][A-Za-z0-9_]*)\s*\)", "MakoLast($1)");
        result = Regex.Replace(result,
            @"\bhas\s*\(\s*([A-Za-z_][A-Za-z0-9_]*)\s*,\s*([^,()]+)\)",
            "MakoHas($1, $2)");
        result = Regex.Replace(result, @"\bobj\.position\b", "obj->position");
        result = result.Replace(".moveTuning.walkSpeed", ".moveTuning.x", StringComparison.Ordinal);
        result = result.Replace(".locomotionTuning.jumpImpulse", ".moveTuning.z", StringComparison.Ordinal);
        result = Regex.Replace(result, @"^(\s*state\.localVelocity\s*=\s*)Vector3\s*\(([^,]+),\s*([^,]+),\s*[^)]+\)", "$1Vector2($2, $3)");
        result = Regex.Replace(result, @"\b1\s*/\s*60\b", "1.0f / 60.0f");
        result = Regex.Replace(result,
            "\\bAddLog\\s*\\((\"(?:\\\\.|[^\"])*\")\\s*\\+\\s*([^,]+),",
            "AddLog($1 + FloatR($2),");
        var assertion = TwoArgumentAssert().Match(result.Trim());
        if (assertion.Success)
            result = $"if (!({assertion.Groups[1].Value.Trim()})) {{ AddLog({assertion.Groups[2].Value}, Type.Error); }}";
        if (type == "float" && NumberOnly().IsMatch(result.Trim()))
        {
            result = result.TrimEnd(';');
            if (!result.Contains('.')) result += ".0";
            if (!result.EndsWith('f')) result += "f";
        }
        return result;
    }

    private static string LowerInterpolatedStrings(string value)
    {
        return Regex.Replace(value, "\"([^\"\\\\]*(?:\\\\.[^\"\\\\]*)*)\"", match =>
        {
            string contents = match.Groups[1].Value;
            if (!contents.Contains('{') || !contents.Contains('}')) return match.Value;

            var parts = new List<string>();
            int cursor = 0;
            foreach (Match interpolation in Regex.Matches(contents, @"\{([^{}]+)\}"))
            {
                if (interpolation.Index > cursor)
                    parts.Add("\"" + contents[cursor..interpolation.Index] + "\"");
                parts.Add("TextR(" + interpolation.Groups[1].Value.Trim() + ")");
                cursor = interpolation.Index + interpolation.Length;
            }
            if (cursor < contents.Length) parts.Add("\"" + contents[cursor..] + "\"");
            return parts.Count > 0 ? "(" + string.Join(" + ", parts) + ")" : match.Value;
        });
    }

    private static string InferFieldType(string value, string source, int line, string name)
    {
        string v = value.Trim();
        if (Regex.IsMatch(v, @"^StandaloneMovementSettings\s*\(")) return "ScriptContext::StandaloneMovementSettings";
        if (Regex.IsMatch(v, @"^StandaloneMovementState\s*\(")) return "ScriptContext::StandaloneMovementState";
        if (Regex.IsMatch(v, @"^StandaloneMovementDebug\s*\(")) return "ScriptContext::StandaloneMovementDebug";
        if (Regex.IsMatch(v, @"^Vector2\s*\(")) return "Vector2";
        if (Regex.IsMatch(v, @"^Vector3\s*\(")) return "Vector3";
        if (Regex.IsMatch(v, @"^(?:Vector4|Color)\s*\(")) return "Vector4";
        if (IsListLiteral(v)) return InferListType(v, name, source, line);
        if (v is "true" or "false") return "bool";
        if (v.StartsWith('"')) return "string";
        if (NumberOnly().IsMatch(v)) return "float";
        Error(source, line, $"cannot infer engine field '{name}'; add a type annotation, for example '{name}: SceneObj = none;'");
        return "auto";
    }

    private static string InferListType(string literal, string name, string source, int line)
    {
        string inner = literal.Trim()[1..^1].Trim();
        if (inner.Length == 0)
            Error(source, line, $"cannot infer empty list '{name}'; add a type such as 'List<f32>'");

        var values = SplitArguments(inner);
        bool allStrings = values.All(value => value.Trim().StartsWith('"') && value.Trim().EndsWith('"'));
        bool allBooleans = values.All(value => value.Trim() is "true" or "false");
        bool allNumbers = values.All(value => NumberOnly().IsMatch(value.Trim()));
        if (allStrings) return "List<string>";
        if (allBooleans) return "List<bool>";
        if (allNumbers) return "List<float>";
        Error(source, line, $"cannot infer mixed list '{name}'; add an explicit List<T> type");
        return "List<float>";
    }

    private static bool IsListType(string? type) => type != null &&
        (type.StartsWith("List<", StringComparison.Ordinal) ||
         type.StartsWith("Array<", StringComparison.Ordinal) ||
         type.StartsWith("std::vector<", StringComparison.Ordinal));

    private static bool IsListLiteral(string value)
    {
        string trimmed = value.Trim();
        return trimmed.Length >= 2 && trimmed[0] == '[' && trimmed[^1] == ']';
    }

    private static string MapType(string type)
    {
        string trimmed = type.Trim();
        var collection = GenericCollectionType().Match(trimmed);
        if (collection.Success)
            return $"{collection.Groups[1].Value}<{MapType(collection.Groups[2].Value)}>";

        return trimmed switch
    {
        "number" or "f32" => "float",
        "f64" or "float" or "double" => "double",
        "i8" => "std::int8_t",
        "i16" => "std::int16_t",
        "i32" => "std::int32_t",
        "i64" or "int" or "isize" => "std::int64_t",
        "u8" => "std::uint8_t",
        "u16" => "std::uint16_t",
        "u32" => "std::uint32_t",
        "u64" or "usize" => "std::uint64_t",
        "string" or "str" => "string",
        "bool" or "boolean" => "bool",
        "none" or "void" => "void",
        "MovementSettings" => "ScriptContext::StandaloneMovementSettings",
        "MovementState" => "ScriptContext::StandaloneMovementState",
        "MovementDebug" => "ScriptContext::StandaloneMovementDebug",
        "Color" => "Vector4",
        "SceneObj" or "Vector2" or "Vector3" or "Vector4" => trimmed,
        var other => other,
    };
    }

    private static string LowerParameters(string parameters, string source = "<memory>", int line = 0, bool requireTypes = false) => string.Join(", ",
        parameters.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(p =>
        {
            var pieces = p.Split(':', 2, StringSplitOptions.TrimEntries);
            if (pieces.Length == 2) return $"{MapType(pieces[1])} {pieces[0]}";
            if (p is "dt" or "deltaTime") return $"float {p}";
            if (p == "ctx") return "ScriptContext& ctx";
            if (requireTypes) Error(source, line, $"native parameter '{p}' needs a type annotation");
            return p;
        }));

    private static List<string> SplitArguments(string text)
    {
        var result = new List<string>();
        int depth = 0, start = 0;
        bool quoted = false, escaped = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (escaped) { escaped = false; continue; }
            if (c == '\\' && quoted) { escaped = true; continue; }
            if (c == '"') { quoted = !quoted; continue; }
            if (quoted) continue;
            if (c is '(' or '[' or '{') depth++;
            else if (c is ')' or ']' or '}') depth--;
            else if (c == ',' && depth == 0)
            {
                result.Add(text[start..i].Trim());
                start = i + 1;
            }
        }
        result.Add(text[start..].Trim());
        return result;
    }

    private static int CountOutsideStrings(string line, char target)
    {
        bool quoted = false, escaped = false;
        int count = 0;
        foreach (char c in line)
        {
            if (escaped) { escaped = false; continue; }
            if (c == '\\' && quoted) { escaped = true; continue; }
            if (c == '"') quoted = !quoted;
            else if (!quoted && c == target) count++;
        }
        return count;
    }

    private static string Sanitize(string name)
    {
        string result = Regex.Replace(name, "[^A-Za-z0-9_]", "_");
        if (result.Length == 0 || char.IsDigit(result[0])) result = "Script_" + result;
        return result;
    }

    private static void EnsureHeader(string source, int line, string? name, string? scriptBase)
    {
        if (name == null || scriptBase == null)
            Error(source, line, "ModuMAKO requires 'script \"Name\" : ModuNode;'");
    }

    private static void Error(string source, int line, string message) =>
        throw new MakoError($"{source}:{line}: ModuMAKO: {message}", line, 1, 1);

    [GeneratedRegex("^script\\s+\"([^\"]+)\"\\s*:\\s*([A-Za-z_][A-Za-z0-9_]*)\\s*;$")]
    private static partial Regex ScriptHeader();
    [GeneratedRegex("^using\\s+([A-Za-z_][A-Za-z0-9_]*)\\s*;$")]
    private static partial Regex UsingLine();
    [GeneratedRegex("^([A-Za-z_][A-Za-z0-9_]*)\\s*\\(([^)]*)\\)\\s*\\{$")]
    private static partial Regex HookHeader();
    [GeneratedRegex("^fn\\s+([A-Za-z_][A-Za-z0-9_]*)\\s*\\(([^)]*)\\)\\s*(?:->\\s*([A-Za-z_][A-Za-z0-9_<>]*))?\\s*\\{$")]
    private static partial Regex HelperHeader();
    [GeneratedRegex("^([A-Za-z_][A-Za-z0-9_]*)(?:\\s*:\\s*([A-Za-z_][A-Za-z0-9_<>]*))?\\s*=\\s*(.+);$")]
    private static partial Regex FieldLine();
    [GeneratedRegex("^([A-Za-z_][A-Za-z0-9_]*)\\s*=\\s*(.+);$")]
    private static partial Regex LocalAssignment();
    [GeneratedRegex("^([A-Za-z_][A-Za-z0-9_]*)\\s*:\\s*([A-Za-z_][A-Za-z0-9_<>]*)\\s*=\\s*(.+);$")]
    private static partial Regex TypedLocalAssignment();
    [GeneratedRegex("^-?[0-9]+(?:\\.[0-9]+)?(?:f)?$")]
    private static partial Regex NumberOnly();
    [GeneratedRegex("^(if|while)\\s+(.+)\\s*\\{$")]
    private static partial Regex ControlHeader();
    [GeneratedRegex("^(if|while)\\s+(.+?)\\s*\\{(.*)\\}\\s*$")]
    private static partial Regex InlineControlBlock();
    [GeneratedRegex("^else\\s+if\\s+(.+?)\\s*\\{(.*)\\}\\s*$")]
    private static partial Regex InlineElseIfBlock();
    [GeneratedRegex("^else\\s*\\{(.*)\\}\\s*$")]
    private static partial Regex InlineElseBlock();
    [GeneratedRegex("^for\\s+([A-Za-z_][A-Za-z0-9_]*)\\s+in\\s+range\\((.*)\\)\\s*\\{$")]
    private static partial Regex RangeLoop();
    [GeneratedRegex("^for\\s+([A-Za-z_][A-Za-z0-9_]*)\\s+in\\s+range\\((.*?)\\)\\s*\\{(.*)\\}\\s*$")]
    private static partial Regex InlineRangeLoop();
    [GeneratedRegex("^for\\s+([A-Za-z_][A-Za-z0-9_]*)\\s+in\\s+(.+?)\\s*\\{$")]
    private static partial Regex IterableLoop();
    [GeneratedRegex("^for\\s+([A-Za-z_][A-Za-z0-9_]*)\\s+in\\s+(.+?)\\s*\\{(.*)\\}\\s*$")]
    private static partial Regex InlineIterableLoop();
    [GeneratedRegex("^assert\\((.*),\\s*(\"(?:\\\\.|[^\"])*\")\\)(;?)$")]
    private static partial Regex TwoArgumentAssert();
    [GeneratedRegex("^(List|Array)<(.+)>$")]
    private static partial Regex GenericCollectionType();
}
