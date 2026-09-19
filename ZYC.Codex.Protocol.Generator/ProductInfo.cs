#pragma warning disable IDE0130
namespace ZYC.Codex.Protocol;

public class ProductInfo
{
    public static string Copyright =>
        $"© 2015 - {DateTime.Now.Year} {Author}. All rights reserved.";

    public static string Description => $"Codex app-server protocol data contracts for .NET. (Powered by {Author})";

    public static string Author => "tomoko";

    public static string Version => "0.9.0";
}