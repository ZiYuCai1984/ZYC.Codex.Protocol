using System.Text;
using System.Text.Json;

namespace ZYC.Codex.Protocol.Generator.Generation;

public class CodeWriter
{
    private int _indent;

    private StringBuilder Text { get; } = new();

    public void Line(string value = "")
    {
        Text.Append(value.Length == 0 ? "" : new string(' ', _indent * 4)).Append(value).Append('\n');
    }

    public IDisposable Indent()
    {
        _indent++;
        return new Indentation(this);
    }

    public void Summary(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return;
        }

        Line("/// <summary>");
        foreach (var line in description.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            Line("///     " + line.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;"));
        }

        Line("/// </summary>");
    }

    public static string Literal(string value)
    {
        return JsonSerializer.Serialize(value);
    }

    public override string ToString()
    {
        return Text.ToString();
    }

    private class Indentation : IDisposable
    {
        private CodeWriter Writer { get; }

        public Indentation(CodeWriter writer)
        {
            Writer = writer;
        }

        public void Dispose()
        {
            Writer._indent--;
        }
    }
}
