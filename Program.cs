// -----------------------------------------------------------------------------------------------------------------
// <copyright file="Program.cs" company="Mike Fourie"> (c) Mike Fourie. All other rights reserved.</copyright>
// --------------------------------------------------------------------------------------------------------------------
namespace LineCounter;

using CommandLine;
using ConsoleTables;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

public class Program
{
    private static readonly HashSet<string> PrunedDirectoryNames = new(StringComparer.OrdinalIgnoreCase) { ".git" };
    private static ConcurrentBag<CsvFile> csvFiles;
    private static List<FileCategory> cats;
    private static IEnumerable<FileInfo> foundFiles;
    private static Options programOptions = new();

    public static void Main(string[] args)
    {
        try
        {
            WriteHeader();
            Parser.Default.ParseArguments<Options>(args)
                   .WithParsed(o =>
                   {
                       programOptions = o;
                   });

            string path = Directory.GetCurrentDirectory();
            if (!string.IsNullOrEmpty(programOptions.Path))
            {
                path = programOptions.Path;
            }

            if (Directory.Exists(path))
            {
                Stopwatch sw = Stopwatch.StartNew();
                Scan(path);
                sw.Stop();

                if (programOptions.ExportCsv)
                {
                    Console.WriteLine("...writing to csv file");
                    ExportToCsv();
                }

                ConsoleTable table = new("Category", "Files", "Lines", "Code", "Comments", "Empty", "Files Incl.", "Files Excl.");
                foreach (FileCategory fc in cats)
                {
                    if (fc.TotalLines > 0)
                    {
                        table.AddRow(fc.Category, fc.TotalFiles, fc.TotalLines, fc.Code, fc.Comments, fc.Empty, fc.IncludedFiles, fc.ExcludedFiles);
                    }
                }

                table.Options.EnableCount = false;
                table.Write(Format.Minimal);
                if (programOptions.ExportCsv)
                {
                    WriteDataToCSV(table);
                }

                Console.WriteLine($"Scan Time: {sw.Elapsed.TotalSeconds}s");
            }
            else
            {
                Console.WriteLine("Path not found...");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
        }
    }

    private static void WriteDataToCSV(ConsoleTable dataTable)
    {
        using StreamWriter writer = new(Path.Combine(Directory.GetCurrentDirectory(), "FileMetricsOutput.csv"));
        var columnLabels = dataTable.Columns;
        var fullRows = dataTable.Rows;
        for (int i = 0; i < fullRows.Count; ++i)
        {
            for (int col = 0; col < columnLabels.Count; ++col)
            {
                writer.Write(fullRows[i][col]);
                writer.Write(',');
            }

            writer.WriteLine();
        }
    }

    private static void WriteHeader()
    {
        Console.WriteLine("Line Counter. For help, run dotnet linecounter.dll --help");
        Console.WriteLine("----------------------------------------------------------------------\n");
    }

    private static void Scan(string path)
    {
        Console.WriteLine($"Scanning: {path}");
        DirectoryInfo dir = new(path.Replace("*", string.Empty));

        foundFiles = [.. EnumerateSourceFiles(dir, programOptions.Recursive)];

        csvFiles = new ConcurrentBag<CsvFile>();
        if (string.IsNullOrEmpty(programOptions.Categories))
        {
            programOptions.Categories = Path.Combine(Directory.GetCurrentDirectory(), "categories.json");
        }

        cats = JsonSerializer.Deserialize<List<FileCategory>>(File.ReadAllText(programOptions.Categories));
        if (cats == null)
        {
            return;
        }

        string[] fileExclusionParts = programOptions.XFiles?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? [];
        string[] folderExclusionParts = programOptions.XFolders?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? [];
        long xLargerBytes = programOptions.XLarger > 0 ? (long)(programOptions.XLarger * 1024 * 1024) : 0;
        long xSmallerBytes = programOptions.XSmaller > 0 ? (long)(programOptions.XSmaller * 1024) : 0;

        Dictionary<string, List<CategoryContext>> byExtension = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, List<CategoryContext>> byName = new(StringComparer.OrdinalIgnoreCase);
        foreach (FileCategory c in cats)
        {
            if (!c.Include)
            {
                continue;
            }

            CategoryContext ctx = new()
            {
                Category = c,
                SingleLineComments = c.SingleLineComment.Split(',', StringSplitOptions.RemoveEmptyEntries),
                NameExclusionParts = c.NameExclusions?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? [],
                HandleMulti = !string.IsNullOrWhiteSpace(c.MultilineCommentStart),
            };
            Dictionary<string, List<CategoryContext>> map = c.Extensionless ? byName : byExtension;
            foreach (string type in c.FileTypes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!map.TryGetValue(type, out List<CategoryContext> list))
                {
                    list = [];
                    map[type] = list;
                }

                list.Add(ctx);
            }
        }

        object mergeLock = new();
        Parallel.ForEach(
            foundFiles,
            () => new Dictionary<CategoryContext, LocalCounter>(),
            (file, _, localStats) =>
            {
                bool matched = false;

                if (byExtension.TryGetValue(file.Extension, out List<CategoryContext> extMatches))
                {
                    matched = true;
                    foreach (CategoryContext ctx in extMatches)
                    {
                        AccumulateForCategory(file, ctx, localStats, fileExclusionParts, folderExclusionParts, xLargerBytes, xSmallerBytes);
                    }
                }

                if (byName.TryGetValue(file.Name, out List<CategoryContext> nameMatches))
                {
                    matched = true;
                    foreach (CategoryContext ctx in nameMatches)
                    {
                        AccumulateForCategory(file, ctx, localStats, fileExclusionParts, folderExclusionParts, xLargerBytes, xSmallerBytes);
                    }
                }

                if (!matched)
                {
                    csvFiles.Add(CreateCsvFile(file, string.Empty, "Excluded", "Extension", 0));
                }

                return localStats;
            },
            localStats =>
            {
                lock (mergeLock)
                {
                    foreach (KeyValuePair<CategoryContext, LocalCounter> kvp in localStats)
                    {
                        FileCategory fc = kvp.Key.Category;
                        fc.TotalFiles += kvp.Value.TotalFiles;
                        fc.TotalLines += kvp.Value.TotalLines;
                        fc.Comments += kvp.Value.Comments;
                        fc.Empty += kvp.Value.Empty;
                        fc.IncludedFiles += kvp.Value.IncludedFiles;
                        fc.ExcludedFiles += kvp.Value.ExcludedFiles;
                    }
                }
            });

        foreach (FileCategory c in cats)
        {
            if (c.Include)
            {
                c.Code = c.TotalLines - c.Empty - c.Comments;
            }
        }

        int totalFiles = cats.Sum(f => f.TotalFiles);
        int totalLines = cats.Sum(f => f.TotalLines);
        int totalCode = cats.Sum(f => f.Code);
        int totalComments = cats.Sum(f => f.Comments);
        int totalEmpty = cats.Sum(f => f.Empty);
        int totalIncluded = cats.Sum(f => f.IncludedFiles);
        int totalExcluded = cats.Sum(f => f.ExcludedFiles);

        cats.Add(new FileCategory
        {
            Include = false,
            Category = "TOTAL",
            FileTypes = "--------",
            SingleLineComment = "--------",
            MultilineCommentStart = "--------",
            MultilineCommentEnd = "--------",
            NameExclusions = "--------",
            TotalFiles = totalFiles,
            TotalLines = totalLines,
            Code = totalCode,
            Comments = totalComments,
            Empty = totalEmpty,
            IncludedFiles = totalIncluded,
            ExcludedFiles = totalExcluded,
        });

        if (totalLines == 0 && totalFiles == 0)
        {
            Console.WriteLine("Nothing scanned...");
        }
    }

    private static IEnumerable<FileInfo> EnumerateSourceFiles(DirectoryInfo root, bool recursive)
    {
        Stack<DirectoryInfo> stack = new();
        stack.Push(root);

        while (stack.Count > 0)
        {
            DirectoryInfo dir = stack.Pop();

            foreach (FileInfo file in dir.EnumerateFiles())
            {
                if ((file.Attributes & FileAttributes.Hidden) == 0)
                {
                    yield return file;
                }
            }

            if (!recursive)
            {
                continue;
            }

            foreach (DirectoryInfo sub in dir.EnumerateDirectories())
            {
                if (!PrunedDirectoryNames.Contains(sub.Name))
                {
                    stack.Push(sub);
                }
            }
        }
    }

    private static void AccumulateForCategory(FileInfo file, CategoryContext ctx, Dictionary<CategoryContext, LocalCounter> localStats, string[] fileExclusionParts, string[] folderExclusionParts, long xLargerBytes, long xSmallerBytes)
    {
        if (!localStats.TryGetValue(ctx, out LocalCounter counter))
        {
            counter = new LocalCounter();
            localStats[ctx] = counter;
        }

        counter.TotalFiles++;
        CountLines(file, ctx, counter, fileExclusionParts, folderExclusionParts, xLargerBytes, xSmallerBytes);
    }

    private static void CountLines(FileInfo thisFile, CategoryContext ctx, LocalCounter counter, string[] fileExclusionParts, string[] folderExclusionParts, long xLargerBytes, long xSmallerBytes)
    {
        if (fileExclusionParts.Length != 0)
        {
            if (fileExclusionParts.Any(s => thisFile.Name.Contains(s, StringComparison.OrdinalIgnoreCase)))
            {
                csvFiles.Add(CreateCsvFile(thisFile, ctx.Category.Category, "Excluded", "Global File Name", 0));
                counter.ExcludedFiles++;
                return;
            }
        }

        if (folderExclusionParts.Length != 0)
        {
            if (folderExclusionParts.Any(s => thisFile.DirectoryName.Contains(s, StringComparison.OrdinalIgnoreCase)))
            {
                csvFiles.Add(CreateCsvFile(thisFile, ctx.Category.Category, "Excluded", "Global Folder Name", 0));
                counter.ExcludedFiles++;
                return;
            }
        }

        if (ctx.NameExclusionParts.Length != 0)
        {
            if (ctx.NameExclusionParts.Any(s => thisFile.Name.Contains(s, StringComparison.OrdinalIgnoreCase)))
            {
                csvFiles.Add(CreateCsvFile(thisFile, ctx.Category.Category, "Excluded", "Category File Name", 0));
                counter.ExcludedFiles++;
                return;
            }
        }

        if (xLargerBytes > 0 && thisFile.Length > xLargerBytes)
        {
            csvFiles.Add(CreateCsvFile(thisFile, ctx.Category.Category, "Excluded", "Large Size", 0));
            counter.ExcludedFiles++;
            return;
        }

        if (xSmallerBytes > 0 && thisFile.Length < xSmallerBytes)
        {
            csvFiles.Add(CreateCsvFile(thisFile, ctx.Category.Category, "Excluded", "Small Size", 0));
            counter.ExcludedFiles++;
            return;
        }

        bool inComment = false;
        int fileLineCount = 0;
        foreach (string line in File.ReadLines(thisFile.FullName))
        {
            fileLineCount++;
            ReadOnlySpan<char> trimmedLine = line.AsSpan().Trim();

            if (trimmedLine.IsEmpty)
            {
                counter.Empty++;
                counter.TotalLines++;
                continue;
            }

            if (ctx.HandleMulti && inComment)
            {
                counter.Comments++;
                if (trimmedLine.EndsWith(ctx.Category.MultilineCommentEnd, StringComparison.OrdinalIgnoreCase))
                {
                    inComment = false;
                }
            }
            else if (ctx.HandleMulti && trimmedLine.StartsWith(ctx.Category.MultilineCommentStart, StringComparison.OrdinalIgnoreCase))
            {
                inComment = true;
                counter.Comments++;
                if (trimmedLine.EndsWith(ctx.Category.MultilineCommentEnd, StringComparison.OrdinalIgnoreCase))
                {
                    inComment = false;
                }
            }
            else
            {
                for (int i = 0; i < ctx.SingleLineComments.Length; i++)
                {
                    if (trimmedLine.StartsWith(ctx.SingleLineComments[i], StringComparison.OrdinalIgnoreCase))
                    {
                        counter.Comments++;
                        break;
                    }
                }
            }

            counter.TotalLines++;
        }

        csvFiles.Add(CreateCsvFile(thisFile, ctx.Category.Category, "Included", "Match", fileLineCount));
        counter.IncludedFiles++;
    }

    private static void ExportToCsv()
    {
        if (string.IsNullOrEmpty(programOptions.OutputFile))
        {
            programOptions.OutputFile = Path.Combine(Directory.GetCurrentDirectory(), "output.csv");
        }

        using StreamWriter writer = new(programOptions.OutputFile);
        writer.WriteLine("File,Lines,Extension,CreatedDateTime,Category,Status,Reason,Length,Directory,Parent,LastWriteTime");
        foreach (CsvFile file in csvFiles.OrderBy(f => f.File))
        {
            writer.Write(StringToCSVCell(file.File));
            writer.Write(',');
            writer.Write(file.Lines);
            writer.Write(',');
            writer.Write(file.Extension);
            writer.Write(',');
            writer.Write(file.CreatedDateTime);
            writer.Write(',');
            writer.Write(file.Category);
            writer.Write(',');
            writer.Write(file.Status);
            writer.Write(',');
            writer.Write(file.Reason);
            writer.Write(',');
            writer.Write(file.Length);
            writer.Write(',');
            writer.Write(file.Directory);
            writer.Write(',');
            writer.Write(file.Parent);
            writer.Write(',');
            writer.Write(file.LastWriteTime);
            writer.WriteLine(',');
        }
    }

    /// <summary>
    /// Converts a string into a CSV cell output.
    /// </summary>
    /// <param name="str">The string to convert.</param>
    /// <returns>The converted string in CSV cell format.</returns>
    private static string StringToCSVCell(string str)
    {
        return !str.AsSpan().ContainsAny([',', '"', '\r', '\n']) ? str : $"\"{str.Replace("\"", "\"\"")}\"";
    }

    private static CsvFile CreateCsvFile(FileInfo file, string category, string status, string reason, int lines)
    {
        return new CsvFile
        {
            File = file.FullName,
            Extension = string.IsNullOrEmpty(file.Extension) ? ".noextension" : file.Extension,
            Category = category,
            Status = status,
            Reason = reason,
            Lines = lines,
            CreatedDateTime = file.CreationTime,
            LastWriteTime = file.LastWriteTime,
            Length = file.Length,
            Parent = file.Directory?.Parent?.Name ?? string.Empty,
            Directory = file.Directory?.Name ?? string.Empty,
        };
    }

    private sealed class CategoryContext
    {
        public FileCategory Category { get; init; }

        public string[] SingleLineComments { get; init; }

        public string[] NameExclusionParts { get; init; }

        public bool HandleMulti { get; init; }
    }

    private sealed class LocalCounter
    {
        public int TotalFiles { get; set; }

        public int TotalLines { get; set; }

        public int Comments { get; set; }

        public int Empty { get; set; }

        public int IncludedFiles { get; set; }

        public int ExcludedFiles { get; set; }
    }
}