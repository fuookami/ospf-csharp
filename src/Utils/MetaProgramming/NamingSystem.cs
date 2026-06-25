#nullable enable

using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Utils.MetaProgramming;
/// <summary>命名系统枚举 / Naming system enum (mirrors ospf-kotlin NamingSystem).</summary>
public enum NamingSystem {
    SnakeCase,
    UpperSnakeCase,
    KebabCase,
    CamelCase,
    PascalCase,
}

/// <summary>NamingSystem 扩展方法 / NamingSystem extension methods.</summary>
public static class NamingSystems {
    /// <summary>前端命名转换 / Frontend naming conversion.</summary>
    public static List<string> Frontend(this NamingSystem ns, string name, IReadOnlySet<string>? abbreviations = null) =>
        // Split name into words based on naming system
        SplitWords(name, ns, abbreviations);

    /// <summary>后端命名转换 / Backend naming conversion.</summary>
    public static string Backend(this NamingSystem ns, IEnumerable<string> words, IReadOnlySet<string>? abbreviations = null) {
        var wordList = words.ToList();
        return ns switch {
            NamingSystem.SnakeCase => string.Join("_", wordList.Select(w => w.ToLowerInvariant())),
            NamingSystem.UpperSnakeCase => string.Join("_", wordList.Select(w => w.ToUpperInvariant())),
            NamingSystem.KebabCase => string.Join("-", wordList.Select(w => w.ToLowerInvariant())),
            NamingSystem.CamelCase => string.Concat(wordList.Select((w, i) => i == 0 ? w.ToLowerInvariant() : Capitalize(w))),
            NamingSystem.PascalCase => string.Concat(wordList.Select(Capitalize)),
            _ => string.Join("_", wordList),
        };
    }

    private static List<string> SplitWords(string name, NamingSystem ns, IReadOnlySet<string>? abbreviations) {
        // Simple split by common delimiters
        var words = new List<string>();
        foreach (string part in name.Split('_', '-', '.')) {
            if (string.IsNullOrEmpty(part)) {
                continue;
            }
            // Split camelCase/PascalCase
            string current = "";
            for (int i = 0; i < part.Length; i++) {
                if (char.IsUpper(part[i]) && current.Length > 0 && !char.IsUpper(current[current.Length - 1])) {
                    words.Add(current);
                    current = part[i].ToString();
                }
                else {
                    current += part[i];
                }
            }
            if (current.Length > 0) {
                words.Add(current);
            }
        }
        return words;
    }

    private static string Capitalize(string word) =>
        word.Length == 0 ? word : char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant();
}
