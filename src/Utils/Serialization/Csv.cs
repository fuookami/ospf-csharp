#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Fuookami.Ospf.Utils.Serialization
{
    /// <summary>CSV 编解码器 / CSV codec (mirrors ospf-kotlin CSV).</summary>
    public static class Csv
    {
        /// <summary>从 CSV 文件读取 / Read from CSV file.</summary>
        public static List<string[]> ReadFromCsv(string path, char delimiter = ',')
        {
            var lines = File.ReadAllLines(path);
            return lines.Select(line => ParseCsvLine(line, delimiter)).ToList();
        }

        /// <summary>从 CSV 流读取 / Read from CSV stream.</summary>
        public static List<string[]> ReadFromCsv(Stream stream, char delimiter = ',')
        {
            using var reader = new StreamReader(stream);
            var result = new List<string[]>();
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                result.Add(ParseCsvLine(line, delimiter));
            }
            return result;
        }

        /// <summary>写入 CSV 到文件 / Write CSV to file.</summary>
        public static void WriteCsvToFile(string path, IEnumerable<string[]> rows, char delimiter = ',')
        {
            var lines = rows.Select(row => string.Join(delimiter, row.Select(cell => EscapeCsv(cell, delimiter))));
            File.WriteAllLines(path, lines);
        }

        private static string[] ParseCsvLine(string line, char delimiter)
        {
            // Simple CSV parser — handles quoted fields
            var fields = new List<string>();
            var current = "";
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                }
                else if (c == delimiter && !inQuotes)
                {
                    fields.Add(current);
                    current = "";
                }
                else
                {
                    current += c;
                }
            }
            fields.Add(current);
            return fields.ToArray();
        }

        private static string EscapeCsv(string cell, char delimiter)
        {
            if (cell.Contains(delimiter) || cell.Contains('"') || cell.Contains('\n'))
            {
                return $"\"{cell.Replace("\"", "\"\"")}\"";
            }
            return cell;
        }
    }
}
