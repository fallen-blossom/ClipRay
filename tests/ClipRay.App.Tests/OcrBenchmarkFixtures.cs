using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

internal static class OcrBenchmarkFixtures
{
    internal static int Run(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("Expected --ocr-benchmark-fixtures <new-output-directory>.");
        var root = Path.GetFullPath(args[1]);
        if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())
            throw new IOException("Fixture output must be empty; existing evidence is not overwritten.");
        Directory.CreateDirectory(root);
        var cases = new List<object>();
        var truth = new List<object>();
        Add("english-lines", 1000, 300, Color.White, Color.Black,
            [("Keep every complete sentence visible.", "Arial", 26, 24, 24),
             ("Detection and recognition use the same local image.", "Arial", 24, 24, 104),
             ("A small fragment must never remove this entire line.", "Arial", 24, 24, 184)]);
        Add("edge-dark", 1000, 190, Color.FromArgb(25, 25, 25), Color.FromArgb(238, 238, 238),
            [("Edge text remains readable.", "Arial", 22, 0, 0),
             ("Keep punctuation: (sample), 1234!", "Arial", 22, 4, 65),
             ("The final line touches the lower edge.", "Arial", 22, 0, 160)]);
        Add("low-contrast", 1000, 300, Color.FromArgb(242, 242, 242), Color.FromArgb(135, 135, 135),
            [("Low contrast is still meaningful text.", "Arial", 22, 20, 20),
             ("Keep the smaller sentence and final punctuation.", "Arial", 16, 20, 110),
             ("This independent line must also survive.", "Arial", 22, 20, 190)]);
        Add("multiscript", 1000, 420, Color.White, Color.Black,
            [("Local OCR keeps English text.", "Arial", 24, 20, 20),
             ("中文内容应该完整保留。", "Microsoft YaHei", 26, 20, 110),
             ("今日は使えますが、明日は使えません。", "Yu Gothic", 26, 20, 210),
             ("한국어 본문을 빠짐없이 읽습니다.", "Malgun Gothic", 26, 20, 310)]);
        File.WriteAllText(Path.Combine(root, "manifest.json"), JsonSerializer.Serialize(new { cases }, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(Path.Combine(root, "truth.json"), JsonSerializer.Serialize(new
        {
            Origin = "Self-authored synthetic text drawn locally with System.Drawing; no screen/user data or external service.",
            Cases = truth,
            Scope = "Expected source strings and intended draw positions; an independent quality oracle, not OCR-derived truth. Inspect PNGs before accepting edge clipping or font coverage."
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Generated {cases.Count} local OCR fixtures: {root}");
        return 0;

        void Add(string id, int width, int height, Color background, Color foreground,
            (string Text, string Family, float Size, float X, float Y)[] lines)
        {
            using var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            using var graphics = Graphics.FromImage(bitmap);
            using var brush = new SolidBrush(foreground);
            using var format = (StringFormat)StringFormat.GenericTypographic.Clone();
            graphics.Clear(background);
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            var rendered = new List<object>();
            foreach (var line in lines)
            {
                using var font = new Font(line.Family, line.Size, FontStyle.Regular, GraphicsUnit.Pixel);
                graphics.DrawString(line.Text, font, brush, line.X, line.Y, format);
                var measured = graphics.MeasureString(line.Text, font, int.MaxValue, format);
                if (line.X + measured.Width > width || line.Y + measured.Height > height)
                    throw new InvalidDataException("Synthetic line exceeds its intended image; adjust fixture instead of accepting clipped truth.");
                rendered.Add(new { line.Text, RequestedFamily = line.Family, ActualFamily = font.Name, FontPixels = line.Size,
                    line.X, line.Y, measured.Width, measured.Height });
            }
            var image = id + ".png";
            var path = Path.Combine(root, image);
            bitmap.Save(path, ImageFormat.Png);
            using var stream = File.OpenRead(path);
            var sha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            cases.Add(new { id, image, targetLanguage = "zh", expectation = "all-translated" });
            truth.Add(new { Id = id, Image = image, Sha256 = sha256, Width = width, Height = height,
                ForegroundArgb = foreground.ToArgb(), BackgroundArgb = background.ToArgb(), Lines = rendered });
        }
    }
}
