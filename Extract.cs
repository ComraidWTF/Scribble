using System;
using System.Text.RegularExpressions;

public class EnumValueParser
{
    public static (int s, string hash) ExtractValues(string enumValueString)
    {
        // Extract the description part: everything inside the quotes after description="
        var descriptionMatch = Regex.Match(enumValueString, @"description=""([^""]*)""");
        if (!descriptionMatch.Success)
        {
            throw new ArgumentException("Description not found in the string.");
        }

        var description = descriptionMatch.Groups[1].Value;

        // Extract s: followed by a number
        var sMatch = Regex.Match(description, @"s:(\d+)");
        int s = sMatch.Success ? int.Parse(sMatch.Groups[1].Value) : 0; // Default to 0 if not found

        // Extract Hash(value)
        var hashMatch = Regex.Match(description, @"Hash\(([^)]+)\)");
        string hash = hashMatch.Success ? hashMatch.Groups[1].Value : string.Empty; // Default to empty if not found

        return (s, hash);
    }

    public static void Main()
    {
        string input = @"<EnumValue name=""test"" value=""36372"" description=""s:46374 u:474857 h:474858 Hash(ejdhfus) "">";

        var (s, hash) = ExtractValues(input);

        Console.WriteLine($"s: {s}");
        Console.WriteLine($"Hash: {hash}");
    }
}
