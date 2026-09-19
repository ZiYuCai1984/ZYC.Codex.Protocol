#pragma warning disable IDE0130
namespace ZYC.Codex.Protocol;

public class ProductInfo
{
    public static string Copyright =>
        $"© 2015 - {DateTime.Now.Year} {Author}. All rights reserved.";

    public static string Description => $"Strongly typed .NET data contracts and JSON serialization support for the Codex app-server protocol. (Powered by {Author})";

    public static string Author => "tomoko";

    public static string Version => "0.9.0";
}