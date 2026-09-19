using System.Text.RegularExpressions;

namespace ZYC.Codex.Protocol.Generator.Naming;

public static class CSharpNaming
{
    public static string TypeName(string value)
    {
        return Identifier(value);
    }

    public static string PropertyName(string value)
    {
        return Identifier(value);
    }

    public static string EnumMemberName(string value)
    {
        return Identifier(value);
    }

    private static string Identifier(string value)
    {
        var words = Regex.Split(value, "[^a-zA-Z0-9]+").Where(x => x.Length > 0);
        var name = string.Concat(words.Select(x => char.ToUpperInvariant(x[0]) + x[1..]));
        if (name.Length == 0)
        {
            return "Value";
        }

        return char.IsDigit(name[0]) ? "_" + name : name;
        // PascalCase ASCII identifiers cannot be C# keywords (all keywords start lowercase).
    }

    public static string Unique(string name, ISet<string> used)
    {
        var candidate = name;
        for (var i = 2; !used.Add(candidate); i++)
        {
            candidate = name + i;
        }

        return candidate;
    }

    public static HashSet<string> MemberScope(string typeName)
    {
        return new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            typeName, "Clone", "EqualityContract", "Equals", "GetHashCode", "ToString", "PrintMembers", "Deconstruct"
        };
    }
}