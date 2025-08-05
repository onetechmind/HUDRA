using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace HUDRA.Utils
{
    public static class VdfParser
    {
        public static Dictionary<string, object> Parse(string vdfContent)
        {
            var reader = new StringReader(vdfContent);
            return ParseObject(reader);
        }

        public static Dictionary<string, object> ParseFile(string filePath)
        {
            if (!File.Exists(filePath))
                return new Dictionary<string, object>();

            try
            {
                var content = File.ReadAllText(filePath, Encoding.UTF8);
                return Parse(content);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to parse VDF file {filePath}: {ex.Message}");
                return new Dictionary<string, object>();
            }
        }

        private static Dictionary<string, object> ParseObject(StringReader reader)
        {
            var result = new Dictionary<string, object>();
            string? line;

            while ((line = reader.ReadLine()) != null)
            {
                line = line.Trim();
                
                if (string.IsNullOrEmpty(line) || line.StartsWith("//"))
                    continue;

                if (line == "}")
                    break;

                var parts = SplitVdfLine(line);
                if (parts.Length < 1)
                    continue;

                var key = parts[0];

                if (parts.Length == 1)
                {
                    // This is likely an object start, next line should be "{"
                    var nextLine = reader.ReadLine()?.Trim();
                    if (nextLine == "{")
                    {
                        result[key] = ParseObject(reader);
                    }
                }
                else if (parts.Length == 2)
                {
                    // Key-value pair
                    result[key] = parts[1];
                }
            }

            return result;
        }

        private static string[] SplitVdfLine(string line)
        {
            var parts = new List<string>();
            var currentPart = new StringBuilder();
            bool inQuotes = false;
            bool escaped = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (escaped)
                {
                    currentPart.Append(c);
                    escaped = false;
                    continue;
                }

                if (c == '\\' && inQuotes)
                {
                    escaped = true;
                    continue;
                }

                if (c == '"')
                {
                    if (inQuotes)
                    {
                        // End of quoted string
                        parts.Add(currentPart.ToString());
                        currentPart.Clear();
                        inQuotes = false;
                    }
                    else
                    {
                        // Start of quoted string
                        inQuotes = true;
                    }
                    continue;
                }

                if (!inQuotes && char.IsWhiteSpace(c))
                {
                    if (currentPart.Length > 0)
                    {
                        parts.Add(currentPart.ToString());
                        currentPart.Clear();
                    }
                    continue;
                }

                currentPart.Append(c);
            }

            if (currentPart.Length > 0)
            {
                parts.Add(currentPart.ToString());
            }

            return parts.ToArray();
        }
    }
}