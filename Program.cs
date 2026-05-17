// -----------------------------------------------------------------------------------------------------------------
// <copyright file="Program.cs" company="Mike Fourie"> (c) Mike Fourie. All other rights reserved.</copyright>
// --------------------------------------------------------------------------------------------------------------------
namespace LineCounter;

using CommandLine;
using ConsoleTables;
using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

public class Program
{
    private static readonly ArrayList ExtensionlessFiles = new();
    private static ConcurrentBag<CsvFile> csvFiles;
    private static List<FileCategory> cats;
    private static IEnumerable<FileInfo> foundFiles;
    private static Options programOptions = new();

    public static void Main(string[] args)
    {
        const int largerMultiplier = 1024 * 1024;

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
                Scan(path, largerMultiplier);
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
        StringBuilder sb = new();
        var columnLabels = dataTable.Columns;
        var fullRows = dataTable.Rows;
        for (int i = 0; i < fullRows.Count; ++i)
        {
            for (int col = 0; col < columnLabels.Count; ++col)
            {
                string value = fullRows[i][col].ToString();
                sb.Append(value).Append(',');
            }

            sb.AppendLine();
        }

        File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), "FileMetricsOutput.csv"), sb.ToString());
    }

    private static void WriteHeader()
    {
        Console.WriteLine("Line Counter. For help, run dotnet linecounter.dll --help");
        Console.WriteLine("----------------------------------------------------------------------\n");
    }

    private static void Scan(string path, int largerMultiplier)
    {
        Console.WriteLine($"Scanning: {path}");
        var recursiveSearch = programOptions.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        DirectoryInfo dir = new(path.Replace("*", string.Empty));

        // Get all files
        IEnumerable<FileInfo> allFiles = dir.GetFiles("*", recursiveSearch).Where(x => (x.Attributes & FileAttributes.Hidden) == 0);

        // Filter out .git files
        string gitSegment = $"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}";
        foundFiles = [.. allFiles.Where(x => !x.FullName.Contains(gitSegment))];

        csvFiles = new ConcurrentBag<CsvFile>();
        if (string.IsNullOrEmpty(programOptions.Categories))
        {
            programOptions.Categories = Path.Combine(Directory.GetCurrentDirectory(), "categories.json");
        }

        cats = JsonSerializer.Deserialize<List<FileCategory>>(File.ReadAllText(programOptions.Categories));
        if (cats != null)
        {
            string fileExclusions = programOptions.XFiles;
            string folderExclusions = programOptions.XFolders;

            ILookup<string, FileInfo> filesByExtension = foundFiles.ToLookup(f => f.Extension, StringComparer.OrdinalIgnoreCase);

            ILookup<string, FileInfo> filesByName = foundFiles.ToLookup(f => f.Name, StringComparer.OrdinalIgnoreCase);

            Parallel.ForEach(cats, cat =>
            {
                ProcessPath(cat, filesByExtension, filesByName, fileExclusions, folderExclusions, largerMultiplier);
            });

            int totalFiles = cats.Sum(f => f.TotalFiles);
            int totalLines = cats.Sum(f => f.TotalLines);
            int totalCode = cats.Sum(f => f.Code);
            int totalComments = cats.Sum(f => f.Comments);
            int totalEmpty = cats.Sum(f => f.Empty);
            int totalIncluded = cats.Sum(f => f.IncludedFiles);
            int totalExcluded = cats.Sum(f => f.ExcludedFiles);

            cats.Add(new FileCategory { Include = false, Code = totalCode, Comments = totalComments, ExcludedFiles = totalExcluded, Empty = Convert.ToInt32(totalEmpty), FileTypes = "--------", IncludedFiles = totalIncluded, MultilineCommentEnd = "--------", MultilineCommentStart = "--------", Category = "TOTAL", TotalLines = totalLines, TotalFiles = totalFiles, SingleLineComment = "--------", NameExclusions = "--------" });
            WhatDidWeSkip();

            if (totalLines == 0 && totalFiles == 0)
            {
                Console.WriteLine("Nothing scanned...");
            }
        }
    }

    private static void WhatDidWeSkip()
    {
        ArrayList usedExtensions = new();
        foreach (FileCategory cat in cats)
        {
            foreach (string s in cat.FileTypes.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!usedExtensions.Contains(s))
                {
                    usedExtensions.Add(s.ToLower());
                }
            }
        }

        foreach (FileInfo f in foundFiles)
        {
            if (!ExtensionlessFiles.Contains(f.FullName))
            {
                if (!usedExtensions.Contains(f.Extension.ToLower()))
                {
                    csvFiles.Add(new CsvFile { File = f.FullName, Extension = f.Extension, Status = "Excluded", Reason = "Extension", Lines = 0, CreatedDateTime = f.CreationTime, LastWriteTime = f.LastWriteTime, Length = f.Length, Parent = f.Directory.Parent.Name, Directory = f.Directory.Name });
                }
            }
        }
    }

    private static void ProcessPath(FileCategory cat, ILookup<string, FileInfo> filesByExtension, ILookup<string, FileInfo> filesByName, string fileExclusions, string folderExclusions, int largerMultiplier)
    {
        cat.Code = 0;
        cat.Comments = 0;
        cat.Empty = 0;
        cat.ExcludedFiles = 0;
        cat.IncludedFiles = 0;
        cat.TotalFiles = 0;
        cat.TotalLines = 0;

        if (!cat.Include)
        {
            return;
        }

        string[] singleLineComments = cat.SingleLineComment.Split(',', StringSplitOptions.RemoveEmptyEntries);
        string[] fileExclusionParts = fileExclusions?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? [];
        string[] folderExclusionParts = folderExclusions?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? [];
        string[] nameExclusionParts = cat.NameExclusions?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? [];

        // Then pass these arrays to CountLines instead of raw strings
        if (!cat.Extensionless)
        {
            foreach (string type in cat.FileTypes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                foreach (FileInfo f in filesByExtension[type])
                {
                    cat.TotalFiles++;
                    CountLines(f, cat, fileExclusionParts, folderExclusionParts, nameExclusionParts, largerMultiplier, singleLineComments);
                }
            }
        }
        else
        {
            foreach (string type in cat.FileTypes.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                foreach (FileInfo f in filesByName[type])
                {
                    cat.TotalFiles++;
                    CountLines(f, cat, fileExclusionParts, folderExclusionParts, nameExclusionParts, largerMultiplier, singleLineComments);
                    ExtensionlessFiles.Add(f.FullName);
                }
            }
        }

        cat.Code = cat.TotalLines - cat.Empty - cat.Comments;
    }

    private static void CountLines(FileInfo thisFile, FileCategory cat, string[] fileExclusionParts, string[] folderExclusionParts, string[] nameExclusionParts, int largerMultiplier, string[] singleLineComments)
    {
        if (fileExclusionParts.Length != 0)
        {
            if (fileExclusionParts.Any(s => thisFile.Name.Contains(s, StringComparison.OrdinalIgnoreCase)))
            {
                csvFiles.Add(CreateCsvFile(thisFile, cat.Category, "Excluded", "Global File Name", 0));
                cat.ExcludedFiles++;
                return;
            }
        }

        if (folderExclusionParts.Length != 0)
        {
            if (folderExclusionParts.Any(s => thisFile.DirectoryName.Contains(s, StringComparison.OrdinalIgnoreCase)))
            {
                csvFiles.Add(CreateCsvFile(thisFile, cat.Category, "Excluded", "Global Folder Name", 0));
                cat.ExcludedFiles++;
                return;
            }
        }

        if (!string.IsNullOrEmpty(cat.NameExclusions))
        {
            if (nameExclusionParts.Any(s => thisFile.Name.Contains(s, StringComparison.OrdinalIgnoreCase)))
            {
                csvFiles.Add(CreateCsvFile(thisFile, cat.Category, "Excluded", "Category File Name", 0));
                cat.ExcludedFiles++;
                return;
            }
        }

        if (Math.Abs(programOptions.XLarger) > 0 && thisFile.Length > programOptions.XLarger * largerMultiplier)
        {
            csvFiles.Add(CreateCsvFile(thisFile, cat.Category, "Excluded", "Large Size", 0));
            cat.ExcludedFiles++;
            return;
        }

        if (Math.Abs(programOptions.XSmaller) > 0 && thisFile.Length < programOptions.XSmaller * 1024)
        {
            csvFiles.Add(CreateCsvFile(thisFile, cat.Category, "Excluded", "Small Size", 0));
            cat.ExcludedFiles++;
            return;
        }

        bool inComment = false;
        bool handleMulti = !string.IsNullOrWhiteSpace(cat.MultilineCommentStart);
        int fileLineCount = 0;
        foreach (string line in File.ReadLines(thisFile.FullName))
        {
            fileLineCount++;
            ReadOnlySpan<char> trimmedLine = line.AsSpan().Trim();

            if (trimmedLine.IsEmpty)
            {
                cat.Empty++;
                cat.TotalLines++;
                continue;
            }

            if (handleMulti && inComment)
            {
                cat.Comments++;
                if (trimmedLine.EndsWith(cat.MultilineCommentEnd, StringComparison.OrdinalIgnoreCase))
                {
                    inComment = false;
                }
            }
            else if (handleMulti && trimmedLine.StartsWith(cat.MultilineCommentStart, StringComparison.OrdinalIgnoreCase))
            {
                inComment = true;
                cat.Comments++;
                if (trimmedLine.EndsWith(cat.MultilineCommentEnd, StringComparison.OrdinalIgnoreCase))
                {
                    inComment = false;
                }
            }
            else
            {
                for (int i = 0; i < singleLineComments.Length; i++)
                {
                    if (trimmedLine.StartsWith(singleLineComments[i], StringComparison.OrdinalIgnoreCase))
                    {
                        cat.Comments++;
                        break;
                    }
                }
            }

            cat.TotalLines++;
        }

        string ext = thisFile.Extension;
        if (string.IsNullOrEmpty(ext))
        {
            ext = ".noextension";
        }

        csvFiles.Add(new CsvFile { File = thisFile.FullName, Extension = ext, Reason = "Match", Status = "Included", Lines = fileLineCount, Category = cat.Category, CreatedDateTime = thisFile.CreationTime, LastWriteTime = thisFile.LastWriteTime, Length = thisFile.Length, Parent = thisFile.Directory.Parent.Name, Directory = thisFile.Directory.Name });
        cat.IncludedFiles++;
    }

    private static void ExportToCsv()
    {
        StringBuilder sb = new();

        sb.AppendLine("File,Lines,Extension,CreatedDateTime,Category,Status,Reason,Length,Directory,Parent,LastWriteTime");
        foreach (CsvFile file in csvFiles.OrderBy(f => f.File))
        {
            sb.Append(StringToCSVCell(file.File)).Append(',');
            sb.Append(file.Lines).Append(',');
            sb.Append(file.Extension).Append(',');
            sb.Append(file.CreatedDateTime).Append(',');
            sb.Append(file.Category).Append(',');
            sb.Append(file.Status).Append(',');
            sb.Append(file.Reason).Append(',');
            sb.Append(file.Length).Append(',');
            sb.Append(file.Directory).Append(',');
            sb.Append(file.Parent).Append(',');
            sb.Append(file.LastWriteTime).Append(',');
            sb.AppendLine();
        }

        if (string.IsNullOrEmpty(programOptions.OutputFile))
        {
            programOptions.OutputFile = Path.Combine(Directory.GetCurrentDirectory(), "output.csv");
        }

        File.WriteAllText(programOptions.OutputFile, sb.ToString());
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
            Parent = file.Directory.Parent.Name,
            Directory = file.Directory.Name,
        };
    }
}