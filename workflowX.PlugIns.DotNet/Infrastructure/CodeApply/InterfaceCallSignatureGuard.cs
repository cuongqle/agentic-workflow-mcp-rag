using System.Text.RegularExpressions;

namespace workflowX.Infrastructure.CodeApply.DotNet;

/// <summary>
/// Validates calls through injected interface dependencies against contracts discovered from the repository.
/// </summary>
internal static class InterfaceCallSignatureGuard
{
    internal static readonly Regex InjectedInterfaceFieldRegex = new(
        @"(?:private|public|protected)\s+(?:readonly\s+)?(I[A-Za-z0-9_]+)\s+(_?[A-Za-z][A-Za-z0-9_]*)\s*;",
        RegexOptions.Compiled);

    private static readonly Regex InterfaceDeclarationRegex = new(
        @"\binterface\s+(I[A-Za-z0-9_]*)\b",
        RegexOptions.Compiled);

    private static readonly Regex InterfaceMethodRegex = new(
        @"^\s*(?:[\w<>\[\],\s\?\.]+\s+)+([A-Za-z_][A-Za-z0-9_]*)\s*\(([^)]*)\)\s*;",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex InterfaceGenericMethodRegex = new(
        @"^\s*(?:[\w<>\[\],\s\?\.]+\s+)+([A-Za-z_][A-Za-z0-9_]*)<[^>]+>\s*\(([^)]*)\)\s*;",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex MethodBlockStartRegex = new(
        @"(?:public|protected|internal)\s+(?:async\s+)?(?:[\w<>\[\],\s\?\.]+\s+)+([A-Za-z_][A-Za-z0-9_]*)\s*\(([^)]*)\)\s*\{",
        RegexOptions.Compiled);

    private static readonly Regex CallRegex = new(
        @"\b(_?[A-Za-z][A-Za-z0-9_]*)\.([A-Za-z_][A-Za-z0-9_]*)\s*\(([^)]*)\)",
        RegexOptions.Compiled);

    internal sealed class SignatureCatalog
    {
        private readonly Dictionary<string, Dictionary<string, string[]>> _signatures =
            new(StringComparer.Ordinal);

        private readonly Dictionary<string, HashSet<string>> _methodNames =
            new(StringComparer.Ordinal);

        internal void RegisterInterface(string interfaceName)
        {
            if (!_methodNames.ContainsKey(interfaceName))
            {
                _methodNames[interfaceName] = new HashSet<string>(StringComparer.Ordinal);
                _signatures[interfaceName] = new Dictionary<string, string[]>(StringComparer.Ordinal);
            }
        }

        internal void AddMethod(string interfaceName, string methodName, IReadOnlyList<string> parameterTypes)
        {
            RegisterInterface(interfaceName);
            Dictionary<string, string[]> methods = _signatures[interfaceName];

            methods[methodName] = parameterTypes.ToArray();

            _methodNames[interfaceName].Add(methodName);
        }

        internal bool TryGetParameterTypes(string interfaceName, string methodName, out string[] parameterTypes)
        {
            parameterTypes = Array.Empty<string>();
            return _signatures.TryGetValue(interfaceName, out Dictionary<string, string[]>? methods)
                   && methods.TryGetValue(methodName, out parameterTypes!);
        }

        internal bool InterfaceIsKnown(string interfaceName) =>
            _methodNames.ContainsKey(interfaceName);

        internal bool TryGetMethodNames(string interfaceName, out HashSet<string> methodNames) =>
            _methodNames.TryGetValue(interfaceName, out methodNames!);

        internal IEnumerable<KeyValuePair<string, IReadOnlyList<string>>> EnumerateMethods()
        {
            foreach ((string interfaceName, Dictionary<string, string[]> methods) in _signatures)
            {
                foreach ((string methodName, string[] parameterTypes) in methods)
                {
                    yield return new KeyValuePair<string, IReadOnlyList<string>>(
                        $"{interfaceName}.{methodName}",
                        parameterTypes);
                }
            }
        }
    }

    internal static bool HasInjectedInterfaceDependencies(string content) =>
        InjectedInterfaceFieldRegex.IsMatch(content);

    internal static string? BuildRagContext(string repoPath, IReadOnlyList<GeneratedFile> proposedFiles)
    {
        SignatureCatalog catalog = BuildCatalog(repoPath, proposedFiles);
        var lines = catalog.EnumerateMethods()
            .GroupBy(entry => entry.Key[..entry.Key.LastIndexOf('.')])
            .Take(10)
            .Select(group =>
            {
                string iface = group.Key;
                var methods = group
                    .Select(entry =>
                    {
                        string methodName = entry.Key[(entry.Key.LastIndexOf('.') + 1)..];
                        string args = entry.Value.Count == 0
                            ? "()"
                            : $"({string.Join(", ", entry.Value)})";
                        return $"{methodName}{args}";
                    })
                    .Take(10);
                return $"- {iface}: {string.Join(", ", methods)}";
            })
            .ToList();
        if (lines.Count == 0)
        {
            return """
                Discovered interface contracts:
                - Only call members declared on injected I* dependencies.
                - Match parameter types end-to-end across every layer.
                """;
        }

        return "Discovered interface contracts (only call declared members; match parameter types across layers):\n"
               + string.Join('\n', lines);
    }

    internal static SignatureCatalog BuildCatalog(string repoPath, IReadOnlyList<GeneratedFile> proposedFiles)
    {
        var catalog = new SignatureCatalog();
        if (!string.IsNullOrWhiteSpace(repoPath))
        {
            foreach (string file in Directory.EnumerateFiles(repoPath, "I*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
                    || file.Contains("/bin/", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                AddInterfaceMethods(File.ReadAllText(file), catalog);
            }
        }

        foreach (GeneratedFile generated in proposedFiles.Where(f =>
                     Path.GetFileName(f.RelativePath).StartsWith("I", StringComparison.Ordinal)
                     && f.RelativePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
        {
            AddInterfaceMethods(generated.Content, catalog, overwrite: true);
        }

        return catalog;
    }

    internal static bool TryValidate(
        string content,
        SignatureCatalog catalog,
        out string reason)
    {
        reason = string.Empty;
        var fieldTypeMap = InjectedInterfaceFieldRegex.Matches(content)
            .ToDictionary(m => m.Groups[2].Value, m => m.Groups[1].Value, StringComparer.Ordinal);
        if (fieldTypeMap.Count == 0)
        {
            return true;
        }

        foreach ((Dictionary<string, string> parameterTypes, string body) in EnumerateMethodScopes(content))
        {
            foreach (Match call in CallRegex.Matches(body))
            {
                string variable = call.Groups[1].Value;
                string method = call.Groups[2].Value;
                if (!fieldTypeMap.TryGetValue(variable, out string? interfaceName))
                {
                    continue;
                }

                if (!catalog.TryGetParameterTypes(interfaceName, method, out string[] expectedTypes))
                {
                    if (catalog.InterfaceIsKnown(interfaceName)
                        && catalog.TryGetMethodNames(interfaceName, out HashSet<string>? knownMethods)
                        && knownMethods.Count > 0
                        && !knownMethods.Contains(method))
                    {
                        string knownMembers = string.Join(
                            ", ",
                            knownMethods.OrderBy(name => name, StringComparer.Ordinal).Take(12));
                        reason =
                            $"Method call {variable}.{method}(...) is not declared on {interfaceName}. Known members: {knownMembers}.";
                        return false;
                    }

                    continue;
                }

                string[] arguments = SplitArguments(call.Groups[3].Value);
                if (arguments.Length != expectedTypes.Length)
                {
                    continue;
                }

                for (int i = 0; i < arguments.Length; i++)
                {
                    if (!TryResolveArgumentType(arguments[i], parameterTypes, out string? actualType)
                        || actualType is null)
                    {
                        continue;
                    }

                    if (TypesMatch(actualType, expectedTypes[i]))
                    {
                        continue;
                    }

                    reason =
                        $"Call {variable}.{method}(...) passes {actualType} for parameter {i + 1}, "
                        + $"but {interfaceName} expects {expectedTypes[i]}. "
                        + "Use matching CLR types across all layers for the same value.";
                    return false;
                }
            }
        }

        return true;
    }

    private static void AddInterfaceMethods(string content, SignatureCatalog catalog, bool overwrite = false)
    {
        foreach (Match ifaceMatch in InterfaceDeclarationRegex.Matches(content))
        {
            string interfaceName = ifaceMatch.Groups[1].Value;
            catalog.RegisterInterface(interfaceName);
            string body = ExtractInterfaceBody(content, ifaceMatch.Index);
            foreach (Match methodMatch in InterfaceMethodRegex.Matches(body))
            {
                string methodName = methodMatch.Groups[1].Value;
                if (overwrite
                    || !catalog.TryGetParameterTypes(interfaceName, methodName, out _))
                {
                    catalog.AddMethod(
                        interfaceName,
                        methodName,
                        ParseParameterTypeList(methodMatch.Groups[2].Value));
                }
            }

            foreach (Match methodMatch in InterfaceGenericMethodRegex.Matches(body))
            {
                string methodName = methodMatch.Groups[1].Value;
                if (overwrite
                    || !catalog.TryGetParameterTypes(interfaceName, methodName, out _))
                {
                    catalog.AddMethod(
                        interfaceName,
                        methodName,
                        ParseParameterTypeList(methodMatch.Groups[2].Value));
                }
            }
        }
    }

    private static IEnumerable<(Dictionary<string, string> ParameterTypes, string Body)> EnumerateMethodScopes(string content)
    {
        foreach (Match methodMatch in MethodBlockStartRegex.Matches(content))
        {
            int openBraceIndex = methodMatch.Index + methodMatch.Length - 1;
            if (openBraceIndex < 0
                || openBraceIndex >= content.Length
                || content[openBraceIndex] != '{'
                || !TryReadBracedBlock(content, openBraceIndex, out string body))
            {
                continue;
            }

            yield return (ParseParameterMap(methodMatch.Groups[2].Value), body);
        }
    }

    private static Dictionary<string, string> ParseParameterMap(string parameterList)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string segment in SplitArguments(parameterList))
        {
            if (!TryParseParameter(segment, out string? type, out string? name))
            {
                continue;
            }

            map[name] = type;
        }

        return map;
    }

    private static IReadOnlyList<string> ParseParameterTypeList(string parameterList)
    {
        var types = new List<string>();
        foreach (string segment in SplitArguments(parameterList))
        {
            if (TryParseParameter(segment, out string? type, out _))
            {
                types.Add(type);
            }
        }

        return types;
    }

    private static bool TryParseParameter(string segment, out string type, out string name)
    {
        type = string.Empty;
        name = string.Empty;
        string trimmed = segment.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return false;
        }

        int equalsIndex = trimmed.IndexOf('=');
        if (equalsIndex >= 0)
        {
            trimmed = trimmed[..equalsIndex].Trim();
        }

        int lastSpace = trimmed.LastIndexOf(' ');
        if (lastSpace <= 0 || lastSpace >= trimmed.Length - 1)
        {
            return false;
        }

        type = NormalizeType(trimmed[..lastSpace].Trim());
        name = trimmed[(lastSpace + 1)..].Trim();
        return !string.IsNullOrWhiteSpace(type) && !string.IsNullOrWhiteSpace(name);
    }

    private static bool TryResolveArgumentType(
        string argument,
        IReadOnlyDictionary<string, string> parameterTypes,
        out string? type)
    {
        type = null;
        string trimmed = argument.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return false;
        }

        if (parameterTypes.TryGetValue(trimmed, out string? parameterType))
        {
            type = parameterType;
            return true;
        }

        if (trimmed.StartsWith('"') && trimmed.EndsWith('"'))
        {
            type = "string";
            return true;
        }

        if (trimmed.Equals("true", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            type = "bool";
            return true;
        }

        if (Regex.IsMatch(trimmed, @"^-?\d+$"))
        {
            type = "int";
            return true;
        }

        if (Regex.IsMatch(trimmed, @"^-?\d+\.\d+$"))
        {
            type = "double";
            return true;
        }

        if (trimmed.StartsWith("new ", StringComparison.Ordinal))
        {
            string remainder = trimmed["new ".Length..].TrimStart();
            int nameEnd = 0;
            while (nameEnd < remainder.Length
                   && (char.IsLetterOrDigit(remainder[nameEnd]) || remainder[nameEnd] is '_' or '.'))
            {
                nameEnd++;
            }

            type = NormalizeType(remainder[..nameEnd]);
            return !string.IsNullOrWhiteSpace(type);
        }

        return false;
    }

    private static bool TypesMatch(string actualType, string expectedType) =>
        string.Equals(NormalizeType(actualType), NormalizeType(expectedType), StringComparison.Ordinal);

    private static string NormalizeType(string type)
    {
        string normalized = type.Trim();
        if (normalized.EndsWith('?'))
        {
            normalized = normalized[..^1];
        }

        int genericStart = normalized.IndexOf('<');
        if (genericStart > 0)
        {
            normalized = normalized[..genericStart];
        }

        return normalized;
    }

    private static string[] SplitArguments(string argumentList)
    {
        if (string.IsNullOrWhiteSpace(argumentList))
        {
            return Array.Empty<string>();
        }

        var arguments = new List<string>();
        int depth = 0;
        int start = 0;
        for (int i = 0; i < argumentList.Length; i++)
        {
            char c = argumentList[i];
            if (c is '(' or '{' or '<')
            {
                depth++;
            }
            else if (c is ')' or '}' or '>')
            {
                depth--;
            }
            else if (c == ',' && depth == 0)
            {
                arguments.Add(argumentList[start..i].Trim());
                start = i + 1;
            }
        }

        arguments.Add(argumentList[start..].Trim());
        return arguments.Where(arg => !string.IsNullOrWhiteSpace(arg)).ToArray();
    }

    private static string ExtractInterfaceBody(string content, int interfaceIndex)
    {
        int braceStart = content.IndexOf('{', interfaceIndex);
        if (braceStart < 0)
        {
            return string.Empty;
        }

        return TryReadBracedBlock(content, braceStart, out string body) ? body : string.Empty;
    }

    private static bool TryReadBracedBlock(string content, int openBraceIndex, out string block)
    {
        block = string.Empty;
        int depth = 0;
        for (int i = openBraceIndex; i < content.Length; i++)
        {
            char c = content[i];
            if (c == '{')
            {
                depth++;
            }
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                {
                    block = content[(openBraceIndex + 1)..i];
                    return true;
                }
            }
        }

        return false;
    }
}
