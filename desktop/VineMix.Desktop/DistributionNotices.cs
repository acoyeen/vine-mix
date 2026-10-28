using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace VineMix.Desktop;

/// <summary>Authoritative distribution texts embedded in every desktop format.</summary>
internal static class DistributionNotices
{
    internal static readonly string[] FileNames = ["LICENSE", "ATTRIBUTION.md", "THIRD_PARTY_NOTICES.md"];

    internal static byte[] ReadBytes(string fileName)
    {
        if (Array.IndexOf(FileNames, fileName) < 0) throw new ArgumentException("Unknown distribution notice.", nameof(fileName));
        using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("VineMix.Notices." + fileName)
            ?? throw new InvalidOperationException("A required distribution notice is missing: " + fileName);
        using var copy = new MemoryStream();
        resource.CopyTo(copy);
        return copy.ToArray();
    }

    internal static string Hash(string fileName) => Convert.ToHexString(SHA256.HashData(ReadBytes(fileName))).ToLowerInvariant();

    internal static string ReadText(string fileName)
    {
        using var reader = new StreamReader(new MemoryStream(ReadBytes(fileName)), new UTF8Encoding(false, true), true);
        return reader.ReadToEnd();
    }

    // Keep the source files unaltered and present their limited Markdown as
    // selectable plain text. Preserve link destinations and every notice word.
    internal static string DisplayText(string fileName)
    {
        string text = ReadText(fileName);
        if (fileName == "LICENSE") return text;
        text = Regex.Replace(text, @"\[([^\]]+)\]\(([^)]+)\)", "$1 ($2)");
        text = Regex.Replace(text, @"(?m)^#{1,6} +", "");
        text = Regex.Replace(text, @"(?m)^> ?", "");
        text = Regex.Replace(text, @"(?m)^```[^\r\n]*(?:\r?\n|$)", "");
        text = text.Replace("`", "", StringComparison.Ordinal);
        // Source paragraphs are hard-wrapped for review; let the view wrap them
        // to the current window and display scale instead.
        text = Regex.Replace(text, @"(?<=[^\r\n])\r?\n(?=[^\r\n|\-])", " ");
        text = Regex.Replace(text, @"(?m)^\|[\s:|\-]+\|\r?$", "");
        text = Regex.Replace(text, @"(?m)^\|(.+)\|\r?$", match =>
            string.Join("\n", Array.ConvertAll(match.Groups[1].Value.Split('|'), value => value.Trim())) + "\n");
        return text.Trim();
    }
}
