// Keep C# 5 syntax so the source also compiles with Windows PowerShell 5.1.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace File2MD
{
    internal static class RegressionTests
    {
        private static string _root;
        private static FolderSnapshot _source;
        private static byte[] _sample;
        private static int _passed;

        public static int Main()
        {
            _root = Path.Combine(Path.Combine(Directory.GetCurrentDirectory(), "artifacts"), "File2MD-regression-" + Guid.NewGuid().ToString("N"));
            try
            {
                var input = Path.Combine(_root, "input");
                Directory.CreateDirectory(input);
                _sample = new byte[24000]; new Random(1776).NextBytes(_sample);
                File.WriteAllBytes(Path.Combine(input, "sample.bin"), _sample);
                File.WriteAllText(Path.Combine(input, "note.txt"), "File2MD regression 日本語\n");
                _source = FolderCatalog.Inspect(input);
                Run("CLI argument validation", TestOptions);
                Run("parallel output directories are unique", TestOutputDirectories);
                Run("plain-text Markdown conversion", TestMarkdown);
                Run("hidden entries stay hidden while explicit text selection works", TestMarkdownVisibility);
                Run("source files and extensionless text Markdown conversion", TestMarkdownSources);
                Run("BOM and CP932 source text", TestMarkdownEncodings);
                Run("source text keeps LF and CRLF line endings", TestMarkdownLineEndings);
                Run("binary and invalid text excluded from Markdown", TestMarkdownBinary);
                Run("text validation at export and safe code fences", TestMarkdownFences);
                Run("Markdown ignores generated text and backup suffixes", TestMarkdownExcludedExtensions);
                Run("Markdown lists omissions and their reasons", TestMarkdownOmissions);
                Run("Markdown can write only omissions without changing selection rules", TestMarkdownOnlyOmissions);
                Run("large omission lists preserve every relative path", TestMarkdownManyOmissions);
                Run("large Markdown files can be excluded together or included", TestMarkdownLargeFiles);
                Console.WriteLine("PASS: " + _passed + " backend tests");
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
            finally { try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch { } }
        }

        private static void Run(string name, Action action)
        { action(); _passed++; Console.WriteLine("PASS " + name); }
        private static void Require(bool condition, string message)
        { if (!condition) throw new Exception(message); }
        private static void Throws<T>(Action action) where T : Exception
        {
            try { action(); } catch (T) { return; }
            throw new Exception("Expected " + typeof(T).Name);
        }
        private static IList<string> Names() { return new[] { "sample.bin", "note.txt" }; }
        private static void TestOptions()
        {
            Require(AppOptions.Parse(new[] { "--no-browser", "--port", "18423" }).Port == 18423, "port");
            Require(AppOptions.Parse(new[] { "--help" }).ShowHelp, "help");
            Throws<ArgumentException>(delegate { AppOptions.Parse(new[] { "--port", "0" }); });
            Throws<ArgumentException>(delegate { AppOptions.Parse(new[] { "--cli" }); });
            Throws<ArgumentException>(delegate { AppOptions.Parse(new[] { "--mode", "bad" }); });
            Require(AppOptions.Parse(new string[0]).InitialMode == "markdown", "default mode");
            Throws<ArgumentException>(delegate { AppOptions.Parse(new[] { "--mode", "vba" }); });
            Throws<ArgumentException>(delegate { AppOptions.Parse(new[] { "--mode", "optical" }); });
        }
        private static void TestOutputDirectories()
        {
            var directories = new string[32];
            Parallel.For(0, directories.Length, delegate(int i) { directories[i] = OutputLayout.CreateRunDirectory(Path.Combine(_root, "parallel"), "same"); });
            Require(directories.Distinct(StringComparer.Ordinal).Count() == directories.Length, "parallel output collision");
        }
        private static void TestMarkdown()
        {
            var converted = MarkdownService.Convert(_source, new[] { "note.txt" }, true, Path.Combine(_root, "markdown"));
            Require(File.Exists(converted.OutputPath) && File.ReadAllText(converted.OutputPath).Contains("File2MD regression 日本語"), "plain text conversion failed");
        }

        private static void TestMarkdownSources()
        {
            var input = Path.Combine(_root, "sources");
            Directory.CreateDirectory(input);
            var names = new[] { "Program.cs", "Makefile", "Dockerfile", "LICENSE",
                "main.c", "main.cpp", "main.h", "main.hpp", "main.java", "main.rs", "main.go",
                "main.rb", "main.php", "main.swift", "main.kt", "main.sh", "config.toml", "main.vb",
                "main.fs", "main.lua", "main.r", "main.scss", "main.vue", "build.gradle", "source.unlisted" };
            foreach (var name in names) File.WriteAllText(Path.Combine(input, name), "// source 日本語: " + name + "\n");
            var snapshot = FolderCatalog.Inspect(input);
            foreach (var name in names)
            {
                var file = snapshot.Files.SingleOrDefault(f => f.Name == name);
                Require(file != null && file.MarkdownSupported && file.Kind == "Text", "source missing/not Text: " + name);
                Require(FolderCatalog.SupportsMarkdown(Path.Combine(input, name)), "direct selection rejected: " + name);
            }
            var converted = MarkdownService.Convert(snapshot, names, true, Path.Combine(_root, "markdown-sources"));
            Require(converted.ConvertedCount == names.Length && converted.Failures.Count == 0 && converted.FilesWritten == 1, "sources not combined");
            var content = File.ReadAllText(converted.OutputPath);
            Require(content.Contains("```csharp") && content.Contains("```makefile") && content.Contains("```dockerfile"), "language fences missing");
            Require(content.Contains("```\n// source 日本語: source.unlisted".Replace("\n", Environment.NewLine)), "unknown source needs plain fence");
            Require(FolderCatalog.PickerPattern("markdown") == "*", "picker still hides source/extensionless files");
        }

        private static void TestMarkdownVisibility()
        {
            var input = Path.Combine(_root, "visibility");
            Directory.CreateDirectory(input);
            File.WriteAllText(Path.Combine(input, "visible.cs"), "// visible source\n");
            var hiddenFile = Path.Combine(input, ".gitignore");
            File.WriteAllText(hiddenFile, "bin/" + Environment.NewLine + "obj/" + Environment.NewLine);
            var hiddenDirectory = Directory.CreateDirectory(Path.Combine(input, ".hidden"));
            File.WriteAllText(Path.Combine(hiddenDirectory.FullName, "inside.cs"), "// hidden source\n");
            if (PlatformInfo.IsWindows)
            {
                Require(FolderCatalog.Inspect(input).Files.Any(f => f.Name == ".gitignore"), "Windows dotfile without Hidden attribute disappeared");
                var windowsFolder = FolderCatalog.Inspect(input);
                Require(!windowsFolder.Files.Single(f => f.Name == ".gitignore").MarkdownSupported,
                    "Windows dotfile leaked into Markdown candidates");
                var windowsResult = MarkdownService.Convert(windowsFolder, new[] { "visible.cs" }, true, Path.Combine(_root, "markdown-windows-hidden"));
                var windowsContent = File.ReadAllText(windowsResult.OutputPath);
                Require(windowsContent.Contains(".gitignore</code> | Hidden file") && !windowsContent.Contains("bin/"),
                    "Windows dotfile must retain only its name in the report");
                File.SetAttributes(hiddenFile, File.GetAttributes(hiddenFile) | FileAttributes.Hidden);
                hiddenDirectory.Attributes |= FileAttributes.Hidden;
            }
            var folder = FolderCatalog.Inspect(input);
            Require(folder.Files.Count == 1 && folder.Files[0].Name == "visible.cs", "hidden entries leaked into folder listing");
            var explicitFile = FolderCatalog.InspectFiles(new[] { hiddenFile });
            Require(explicitFile.Files.Count == 1 && explicitFile.Files[0].MarkdownSupported && explicitFile.Files[0].Kind == "Text", "explicit hidden text selection rejected");
            var result = MarkdownService.Convert(explicitFile, new[] { ".gitignore" }, true, Path.Combine(_root, "markdown-hidden"));
            Require(result.ConvertedCount == 1 && result.Failures.Count == 0 && result.FilesWritten == 1, "explicit hidden text conversion failed");
            Require(File.ReadAllText(result.OutputPath).Contains("bin/" + Environment.NewLine + "obj/"), "explicit hidden text content lost");
        }

        private static void TestMarkdownEncodings()
        {
            var input = Path.Combine(_root, "encodings");
            Directory.CreateDirectory(input);
            // Fixed CP932 bytes avoid depending on the runtime's encoding provider.
            File.WriteAllBytes(Path.Combine(input, "cp932.cs"), new byte[] { 47, 47, 32, 0x93, 0xfa, 0x96, 0x7b, 0x8c, 0xea, 13, 10 });
            File.WriteAllText(Path.Combine(input, "utf8.cs"), "// 日本語\n", new UTF8Encoding(true));
            File.WriteAllText(Path.Combine(input, "utf16.cs"), "// 日本語\n", Encoding.Unicode);
            File.WriteAllText(Path.Combine(input, "utf16be.cs"), "// 日本語\n", Encoding.BigEndianUnicode);
            // A multibyte character crosses the catalogue probe boundary.
            File.WriteAllText(Path.Combine(input, "boundary.cs"), new string(' ', 8191) + "日本語\n", new UTF8Encoding(false));
            var snapshot = FolderCatalog.Inspect(input);
            Require(snapshot.Files.All(f => f.MarkdownSupported), "encoded source rejected");
            var result = MarkdownService.Convert(snapshot, snapshot.Files.Select(f => f.Name).ToArray(), true, Path.Combine(_root, "markdown-encodings"));
            Require(result.ConvertedCount == 5 && result.Failures.Count == 0, "encoded sources failed");
            var content = File.ReadAllText(result.OutputPath);
            Require(content.Split(new[] { "日本語" }, StringSplitOptions.None).Length == 6 && !content.Contains("\ufffd"), "Japanese text corrupted");
        }

        private static void TestMarkdownLineEndings()
        {
            var input = Directory.CreateDirectory(Path.Combine(_root, "line-endings")).FullName;
            const string lf = "// LF 日本語\nclass LF {}";
            const string crlf = "// CRLF 日本語\r\nclass CRLF {}";
            File.WriteAllText(Path.Combine(input, "lf.cs"), lf, new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(input, "crlf.cs"), crlf, new UTF8Encoding(false));
            var snapshot = FolderCatalog.Inspect(input);
            var result = MarkdownService.Convert(snapshot, new[] { "lf.cs", "crlf.cs" }, true, Path.Combine(_root, "markdown-line-endings"));
            var content = File.ReadAllText(result.OutputPath);
            Require(result.ConvertedCount == 2 && result.FailedCount == 0 && content.Contains(lf) && content.Contains(crlf), "source line endings changed");
        }

        private static void TestMarkdownBinary()
        {
            var input = Path.Combine(_root, "binary");
            Directory.CreateDirectory(input);
            File.WriteAllBytes(Path.Combine(input, "image.png"), Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+j6V8AAAAASUVORK5CYII="));
            File.WriteAllBytes(Path.Combine(input, "program.exe"), new byte[] { 77, 90, 0, 0, 0, 0 });
            File.Copy(Path.Combine(input, "program.exe"), Path.Combine(input, "renamed.cs"));
            File.WriteAllBytes(Path.Combine(input, "nul.txt"), new byte[] { 65, 0, 66 });
            File.WriteAllBytes(Path.Combine(input, "invalid.cs"), new byte[] { 0xef, 0xbb, 0xbf, 0xff });
            File.WriteAllBytes(Path.Combine(input, "invalid-utf16.cs"), new byte[] { 0xff, 0xfe, 0x00, 0xd8 });
            File.WriteAllBytes(Path.Combine(input, "truncated-cp932.cs"), new byte[] { 0x82 });
            File.WriteAllText(Path.Combine(input, "control.cs"), "text\u0093text", new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(input, "replacement.cs"), "text\ufffdtext", new UTF8Encoding(false));
            var snapshot = FolderCatalog.Inspect(input);
            foreach (var file in snapshot.Files)
            {
                Require(!file.MarkdownSupported && !FolderCatalog.SupportsMarkdown(file.FullPath), "binary admitted: " + file.Name);
                Throws<ArgumentException>(delegate { MarkdownService.Convert(snapshot, new[] { file.Name }, true, Path.Combine(_root, "binary-output")); });
            }
        }

        private static void TestMarkdownExcludedExtensions()
        {
            var input = Directory.CreateDirectory(Path.Combine(_root, "excluded-extensions")).FullName;
            var excluded = new[] { "icon.svg", "test.TRX", "yarn.lock", "file.sha256",
                "file.sha512", "build.metadata", "app.js.map",
                "source.cs.bak", "source.cs.bak-20000101-sample", "source.cs.bak_20000101" };
            var included = new[] { "Program.cs", "Makefile", "source.unknown", "app.log", "data.csv",
                "data.tsv", "config.json", "events.jsonl", "changes.patch", "changes.diff", "source.locksmith", "source.bakery" };
            foreach (var name in excluded.Concat(included)) File.WriteAllText(Path.Combine(input, name), "TEXT_BODY_SENTINEL\n");
            var snapshot = FolderCatalog.Inspect(input);
            foreach (var name in excluded)
            {
                Require(!snapshot.Files.Single(f => f.Name == name).MarkdownSupported, "excluded suffix admitted: " + name);
                Require(!FolderCatalog.SupportsMarkdown(Path.Combine(input, name)), "direct support differs: " + name);
            }
            Require(snapshot.Files.Count == excluded.Length + included.Length, "optical catalogue lost excluded files");
            Require(snapshot.Files.Where(f => f.MarkdownSupported).Select(f => f.Name).OrderBy(n => n)
                .SequenceEqual(included.OrderBy(n => n)), "ordinary source/log/CSV/TSV text changed");
        }

        private static void TestMarkdownOmissions()
        {
            var input = Directory.CreateDirectory(Path.Combine(_root, "omissions")).FullName;
            Directory.CreateDirectory(Path.Combine(input, "assets"));
            File.WriteAllText(Path.Combine(input, "Program.cs"), "class Included {}\n");
            File.WriteAllText(Path.Combine(input, "unchecked.cs"), "UNCHECKED_BODY_SENTINEL\n");
            File.WriteAllText(Path.Combine(input, "assets/icon.svg"), "EXCLUDED_BODY_SENTINEL\n");
            File.WriteAllBytes(Path.Combine(input, "assets/image.png"), new byte[] { 137, 80, 78, 71, 0 });
            File.WriteAllText(Path.Combine(input, "broken.docx"), "BROKEN_BODY_SENTINEL\n");
            File.WriteAllText(Path.Combine(input, "gone.cs"), "GONE_BODY_SENTINEL\n");
            File.WriteAllText(Path.Combine(input, "tail.cs"), new string('a', 9000) + "\0BINARY_BODY_SENTINEL");
            using (var stream = File.Create(Path.Combine(input, "large.pdf"))) stream.SetLength(512L * 1024 * 1024 + 1);
            using (var stream = File.Create(Path.Combine(input, "large.docx")))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            using (var part = zip.CreateEntry("word/document.xml", CompressionLevel.Fastest).Open())
            {
                var buffer = new byte[1024 * 1024];
                for (var i = 0; i < 65; i++) part.Write(buffer, 0, buffer.Length);
            }
            var snapshot = FolderCatalog.Inspect(input);
            File.Delete(Path.Combine(input, "gone.cs"));
            var selected = snapshot.Files.Where(f => f.MarkdownSupported && f.Name != "unchecked.cs").Select(f => f.Name).ToArray();
            var result = MarkdownService.Convert(snapshot, selected, true, Path.Combine(_root, "markdown-omissions"));
            Require(result.ConvertedCount == 1 && result.FailedCount == 5 && result.FilesWritten == 1, "omission counts changed");
            var content = File.ReadAllText(result.OutputPath);
            Require(content.Contains("## Omitted files"), "omission section missing");
            foreach (var row in new[] {
                "assets/icon.svg</code> | Excluded file extension",
                "assets/image.png</code> | Binary data or invalid text encoding",
                "tail.cs</code> | Binary data or invalid text encoding",
                "large.pdf</code> | Exceeds the extraction size limit",
                "large.docx</code> | Exceeds the extraction size limit",
                "broken.docx</code> | Conversion failed",
                "gone.cs</code> | Conversion failed",
                "unchecked.cs</code> | Not selected" }) Require(content.Contains(row), "missing omission: " + row);
            Require(content.Contains("class Included {}") && !content.Contains("BODY_SENTINEL") && !content.Contains("\0"), "omitted contents leaked");
            Require(!content.Contains(input), "omission report contains an absolute path");
            Require(Directory.GetFiles(Path.GetDirectoryName(result.OutputPath)).Length == 1, "omissions written as separate files");
            var single = FolderCatalog.InspectFiles(new[] { Path.Combine(input, "Program.cs") });
            var singleResult = MarkdownService.Convert(single, new[] { "Program.cs" }, true, Path.Combine(_root, "markdown-single-omissions"));
            Require(!File.ReadAllText(singleResult.OutputPath).Contains("icon.svg"), "report escaped explicit selection scope");
        }

        private static void TestMarkdownOnlyOmissions()
        {
            var input = Directory.CreateDirectory(Path.Combine(_root, "only-omissions")).FullName;
            File.WriteAllText(Path.Combine(input, "icon.svg"), "DO_NOT_READ_BODY_SENTINEL");
            var snapshot = FolderCatalog.Inspect(input);
            var result = MarkdownService.Convert(snapshot, new string[0], true, Path.Combine(_root, "markdown-only-omissions"));
            Require(result.FilesWritten == 1 && result.ConvertedCount == 0 && result.FailedCount == 0, "exclusions-only export failed");
            var content = File.ReadAllText(result.OutputPath);
            Require(content.Contains("icon.svg</code> | Excluded file extension") && !content.Contains("BODY_SENTINEL"), "exclusions-only report lost names");
            Throws<ArgumentException>(delegate { MarkdownService.Convert(_source, new string[0], true, Path.Combine(_root, "no-selection")); });
            Throws<ArgumentException>(delegate { MarkdownService.Convert(_source, new[] { "../outside.cs" }, true, Path.Combine(_root, "bad-selection")); });
        }

        private static void TestMarkdownManyOmissions()
        {
            var input = Directory.CreateDirectory(Path.Combine(_root, "many-omissions")).FullName;
            File.WriteAllText(Path.Combine(input, "Program.cs"), "class Included {}");
            for (var i = 0; i < 120; i++) File.WriteAllText(Path.Combine(input, "icon-" + i + ".svg"), "EXCLUDED_BODY_SENTINEL");
            File.WriteAllText(Path.Combine(input, "a&b`[x].svg"), "EXCLUDED_BODY_SENTINEL");
            var result = MarkdownService.Convert(FolderCatalog.Inspect(input), new[] { "Program.cs" }, true, Path.Combine(_root, "markdown-many"));
            var content = File.ReadAllText(result.OutputPath);
            Require(content.Contains("<details>") && content.Contains("121 omitted files") && content.Contains("</details>"), "large list not folded");
            for (var i = 0; i < 120; i++) Require(content.Contains("icon-" + i + ".svg</code>"), "omission list truncated: " + i);
            Require(content.Contains("a&amp;b&#96;&#91;x&#93;.svg"), "path markup not escaped");
            Require(!content.Contains("BODY_SENTINEL"), "excluded content leaked into large report");
        }

        private static void TestMarkdownLargeFiles()
        {
            var input = Directory.CreateDirectory(Path.Combine(_root, "large-text")).FullName;
            var names = new[] { "small.jsonl", "boundary.jsonl", "large.jsonl", "large.log" };
            File.WriteAllText(Path.Combine(input, names[0]), "SMALL_JSONL", new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(input, names[1]), new string('a', 1048576), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(input, names[2]), "LARGE_JSONL_BODY" + new string('b', 1048576), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(input, names[3]), "LARGE_LOG_BODY" + new string('c', 1048576), new UTF8Encoding(false));
            var snapshot = FolderCatalog.Inspect(input);
            Require(snapshot.Files.All(f => f.MarkdownSupported), "large text disappeared before user choice");
            var excluded = MarkdownService.Convert(snapshot, names, true, Path.Combine(_root, "large-exclude"), null, true);
            var content = File.ReadAllText(excluded.OutputPath);
            Require(excluded.ConvertedCount == 2 && excluded.FailedCount == 0, "bulk size exclusion boundary/count wrong");
            Require(content.Contains("SMALL_JSONL") && content.Contains("## boundary.jsonl"), "small or exactly 1 MB text omitted");
            Require(content.Contains("large.jsonl</code> | Large file (over 1 MiB; excluded by size)")
                && content.Contains("large.log</code> | Large file (over 1 MiB; excluded by size)"), "size exclusion reasons missing");
            Require(!content.Contains("LARGE_JSONL_BODY") && !content.Contains("LARGE_LOG_BODY"), "excluded large contents leaked");
            var included = MarkdownService.Convert(snapshot, names, true, Path.Combine(_root, "large-include"), null, false);
            content = File.ReadAllText(included.OutputPath);
            Require(included.ConvertedCount == 4 && included.FailedCount == 0
                && content.Contains("LARGE_JSONL_BODY") && content.Contains("LARGE_LOG_BODY"), "include-all was not respected");
        }

        private static void TestMarkdownFences()
        {
            var input = Path.Combine(_root, "fences");
            Directory.CreateDirectory(input);
            const string code = "var sample = \"\"\"\n```\n# must stay inside code\n\"\"\";";
            File.WriteAllText(Path.Combine(input, "code.cs"), code);
            File.WriteAllText(Path.Combine(input, "README.md"), "# Keep Markdown\n");
            File.WriteAllText(Path.Combine(input, "changed.cs"), "class Before {}\n");
            File.WriteAllText(Path.Combine(input, "late-binary.cs"), new string('a', 20000) + "\0BINARY_SENTINEL");
            var snapshot = FolderCatalog.Inspect(input);
            File.WriteAllBytes(Path.Combine(input, "changed.cs"), new byte[] { 65, 0, 66 });
            var result = MarkdownService.Convert(snapshot, snapshot.Files.Select(f => f.Name).ToArray(), true, Path.Combine(_root, "markdown-fences"));
            Require(result.ConvertedCount == 2 && result.Failures.Count == 2, "export did not reject binary after probe");
            var content = File.ReadAllText(result.OutputPath);
            Require(content.Contains("````csharp" + Environment.NewLine + code + Environment.NewLine + "````"), "embedded fence broke code");
            Require(content.Contains("## README.md" + Environment.NewLine + Environment.NewLine + "# Keep Markdown"), "Markdown formatting changed");
            Require(!content.Contains("BINARY_SENTINEL") && !content.Contains("\0"), "binary leaked into Markdown");
        }
    }
}
