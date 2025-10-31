using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace UdonExpressionDriver.Editor
{
    public static class UdonExpressionDriverUtils
    {
        private static readonly System.Random Random = new();

        public static string ToValidClassName(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return "__" + Random.Next(0x1000, 0xFFFF).ToString("X4");

            // Split on any non-alphanumeric character
            var parts = Regex.Split(input, @"[^A-Za-z0-9]+", RegexOptions.Compiled);

            var sb = new StringBuilder();
            foreach (var part in parts)
            {
                if (string.IsNullOrEmpty(part))
                    continue;

                // Capitalize only the first letter of the part; handle single-char parts safely
                var first = char.ToUpperInvariant(part[0]).ToString();
                if (part.Length > 1)
                    sb.Append(first + part[1..]);
                else
                    sb.Append(first);
            }

            if (sb.Length == 0)
                return "_";

            // Ensure valid C# identifier start
            if (!char.IsLetter(sb[0]) && sb[0] != '_')
                sb.Insert(0, '_');

            return sb.ToString();
        }

        public static string ToRelativePath(string path)
        {
            // Check for null or empty path
            if (string.IsNullOrEmpty(path))
                return path;
            
            // Convert absolute path to relative if it starts with Application.dataPath
            var dataPath = Application.dataPath;
            if (path.StartsWith(dataPath))
                return "Assets" + path[dataPath.Length..];
            
            // Otherwise, return the original path as-is
            return path;
        }
    }
}