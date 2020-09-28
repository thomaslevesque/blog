using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace NewArticle
{
    static class Program
    {
        static void Main(string[] args)
        {
            string title = args.FirstOrDefault();
            if (string.IsNullOrEmpty(title))
            {
                Console.WriteLine("Usage: NewArticle <title>");
                return;
            }

            var date = DateTime.Today;
            var slug = title.MakeUrlSlug();
            string directory = $@"content\posts\{date:yyyy-MM-dd}-{slug}";
            string url = $"/{date:yyyy/MM/dd}/{slug}/";

            var path = Path.Combine(directory, "index.md");
            Directory.CreateDirectory(directory);
            using var writer = new StreamWriter(path, false, Encoding.UTF8);
            writer.WriteLine("---");
            writer.WriteLine("layout: post");
            writer.WriteLine("layout: post");
            writer.WriteLine($"title: {title}");
            writer.WriteLine($"date: {date:yyyy-MM-dd}");
            writer.WriteLine($"url: {url}");
            writer.WriteLine("tags:");
            writer.WriteLine("  - ");
            writer.WriteLine("---");
            writer.WriteLine();
        }

        public static string MakeUrlSlug(this string s)
        {
            if (string.IsNullOrEmpty(s))
                return s;

            s = s.RemoveDiacritics();
            s = Regex.Replace(s, "[^a-zA-Z0-9]", "-");
            s = s.ToLowerInvariant();
            return s;
        }

        private static readonly (int start, int end)[] DiacriticRanges =
        {
            (0x0300, 0x036F),
            (0x1AB0, 0x1AFF),
            (0x1DC0, 0x1DFF),
            (0x20D0, 0x20FF),
            (0xFE20, 0xFE2F),
        };

        private static string RemoveDiacritics(this string s)
        {
            if (string.IsNullOrEmpty(s))
                return s;

            // Normalize to decomposed form
            var formD = s.Normalize(NormalizationForm.FormD);

            var sb = new StringBuilder(formD.Length);
            foreach (var c in formD)
            {
                int value = c;
                if (DiacriticRanges.Any(r => value >= r.start && value <= r.end))
                    continue;
                sb.Append(c);
            }

            return sb.ToString().Normalize(NormalizationForm.FormC);
        }
    }
}
