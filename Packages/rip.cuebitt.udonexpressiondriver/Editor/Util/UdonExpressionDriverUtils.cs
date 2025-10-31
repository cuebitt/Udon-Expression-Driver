using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using Random = System.Random;

namespace UdonExpressionDriver.Editor
{
    public static class UdonExpressionDriverUtils
    {
        private static readonly Random Random = new();

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

                // Capitalize only the first letter of the part
                if (char.IsLetter(part[0]))
                    sb.Append(char.ToUpper(part[0]) + part[1..]);
                else
                    sb.Append(part); // keep digits etc.
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
            return "Assets" + path[Application.dataPath.Length..];
        }
    }
}