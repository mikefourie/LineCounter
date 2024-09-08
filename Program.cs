// -----------------------------------------------------------------------------------------------------------------
// <copyright file="Program.cs" company="Mike Fourie"> (c) Mike Fourie. All other rights reserved.</copyright>
// --------------------------------------------------------------------------------------------------------------------
namespace LineCounter;

using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using CommandLine;
using ConsoleTables;

public class Program
{
    private static readonly ArrayList ExtensionlessFiles = new ();
    private static ConcurrentBag<CsvFile> csvFiles;
    private static List<FileCategory> cats;
    private static IEnumerable<FileInfo> foundFiles;
    private static Options programOptions = new ();

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
                DateTime start = DateTime.Now;
                Scan(path, largerMultiplier);
                DateTime end = DateTime.Now;
                TimeSpan t = end - start;

                if (programOptions.ExportCsv)
                {
                    Console.WriteLine("...writing to csv file");
                    ExportToCsv();
                }

                ConsoleTable table = new ("Category", "Files", "Lines", "Code", "Comments", "Empty", "Files Incl.", "Files Excl.");
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

                Console.WriteLine($"Scan Time: {t.TotalSeconds}s");
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
        StringBuilder sb = new ();
        var columnLabels = dataTable.Columns;
        var fullRows = dataTable.Rows;
        for (int i = 0; i < fullRows.Count; ++i)
        {
            for (int col = 0; col < columnLabels.Count; ++col)
            {
                string value = fullRows[i][col].ToString();
                sb.Append(value + ",");
            }

            sb.AppendLine();
        }

        File.WriteAllText(Path.Combine($"{Directory.GetCurrentDirectory()}", "FileMetricsOutput.csv"), sb.ToString());
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
        DirectoryInfo dir = new (path.Replace("*", string.Empty));

        // Get all files
        IEnumerable<FileInfo> allFiles = dir.GetFiles("*", recursiveSearch).Where(x => (x.Attributes & FileAttributes.Hidden) == 0);

        // Filter out .git files
        foundFiles = allFiles.Where(x => !x.FullName.Contains("\\.git\\"));

        csvFiles = new ConcurrentBag<CsvFile>();
        if (string.IsNullOrEmpty(programOptions.Categories))
        {
            programOptions.Categories = Path.Combine($"{Directory.GetCurrentDirectory()}", "categories.json");
        }

        cats = JsonSerializer.Deserialize<List<FileCategory>>(File.ReadAllText(programOptions.Categories));
        if (cats != null)
        {
            ObservableCollection<FileReport> xxx = new ();
            string fileExclusions = programOptions.XFiles;
            string folderExclusions = programOptions.XFolders;

            Parallel.ForEach(cats, cat =>
            {
                ProcessPath(cat, foundFiles, xxx, fileExclusions, folderExclusions, largerMultiplier);
            });

            int totalFiles = 0;
            int totalLines = 0;
            int totalIncluded = 0;
            int totalExcluded = 0;
            double totalComments = 0;
            double totalCode = 0;
            double totalEmpty = 0;

            foreach (FileCategory f in cats)
            {
                totalFiles += f.TotalFiles;
                totalLines += f.TotalLines;
                totalComments += f.Comments;
                totalEmpty += f.Empty;
                totalCode += f.Code;
                totalIncluded += f.IncludedFiles;
                totalExcluded += f.ExcludedFiles;
            }

            cats.Add(new FileCategory { Include = false, Code = Convert.ToInt32(totalCode), Comments = Convert.ToInt32(totalComments), ExcludedFiles = totalExcluded, Empty = Convert.ToInt32(totalEmpty), FileTypes = "--------", IncludedFiles = totalIncluded, MultilineCommentEnd = "--------", MultilineCommentStart = "--------", Category = "TOTAL", TotalLines = totalLines, TotalFiles = totalFiles, SingleLineComment = "--------", NameExclusions = "--------" });
            WhatDidWeSkip();

            if (totalLines == 0 && totalFiles == 0)
            {
                Console.WriteLine("Nothing scanned...");
            }
        }
    }

    private static void WhatDidWeSkip()
    {
        ArrayList usedExtensions = new ();
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

    private static void ProcessPath(FileCategory cat, IEnumerable<FileInfo> fileInfo, ObservableCollection<FileReport> fileReport, string fileExclusions, string folderExclusions, int largerMultiplier)
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

        if (!cat.Extensionless)
        {
            foreach (FileInfo f in cat.FileTypes.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).SelectMany(type => fileInfo.Where(f => f.Extension.ToLower() == type.TrimStart().ToLower())))
            {
                cat.TotalFiles++;
                CountLines(f, cat, fileReport, fileExclusions, folderExclusions, largerMultiplier);
            }
        }
        else
        {
            foreach (FileInfo f in cat.FileTypes.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).SelectMany(type => fileInfo.Where(f => f.Name.ToLower() == type.TrimStart().ToLower())))
            {
                cat.TotalFiles++;
                CountLines(f, cat, fileReport, fileExclusions, folderExclusions, largerMultiplier);
                ExtensionlessFiles.Add(f.FullName);
            }
        }

        cat.Code = cat.TotalLines - cat.Empty - cat.Comments;
    }

    private static void CountLines(FileSystemInfo i, FileCategory cat, ObservableCollection<FileReport> fileReport, string fileExclusions, string folderExclusions, int largerMultiplier)
    {
        FileInfo thisFile = new (i.FullName);

        if (!string.IsNullOrEmpty(fileExclusions))
        {
            if (fileExclusions.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Any(s => thisFile.Name.ToLower().Contains(s.ToLower())))
            {
                fileReport.Add(new FileReport { File = thisFile.FullName, Extension = thisFile.Extension, Reason = "Global File Name", Status = "Excluded", Lines = 0, Category = cat.Category });
                csvFiles.Add(new CsvFile { File = thisFile.FullName, Extension = thisFile.Extension, Reason = "Global File Name", Status = "Excluded", Lines = 0, Category = cat.Category, CreatedDateTime = thisFile.CreationTime, LastWriteTime = thisFile.LastWriteTime, Length = thisFile.Length, Parent = thisFile.Directory.Parent.Name, Directory = thisFile.Directory.Name });
                cat.ExcludedFiles++;
                return;
            }
        }

        if (!string.IsNullOrEmpty(folderExclusions))
        {
            if (folderExclusions.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Any(s => thisFile.DirectoryName.ToLower().Contains(s.ToLower())))
            {
                fileReport.Add(new FileReport { File = thisFile.FullName, Extension = thisFile.Extension, Reason = "Global Folder Name", Status = "Excluded", Lines = 0, Category = cat.Category });
                csvFiles.Add(new CsvFile { File = thisFile.FullName, Extension = thisFile.Extension, Reason = "Global Folder Name", Status = "Excluded", Lines = 0, Category = cat.Category, CreatedDateTime = thisFile.CreationTime, LastWriteTime = thisFile.LastWriteTime, Length = thisFile.Length, Parent = thisFile.Directory.Parent.Name, Directory = thisFile.Directory.Name });
                cat.ExcludedFiles++;
                return;
            }
        }

        if (!string.IsNullOrEmpty(cat.NameExclusions))
        {
            if (cat.NameExclusions.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Any(s => thisFile.Name.ToLower().Contains(s.ToLower())))
            {
                fileReport.Add(new FileReport { File = thisFile.FullName, Extension = thisFile.Extension, Reason = "Category File Name", Status = "Excluded", Lines = 0, Category = cat.Category });
                csvFiles.Add(new CsvFile { File = thisFile.FullName, Extension = thisFile.Extension, Reason = "Category File Name", Status = "Excluded", Lines = 0, Category = cat.Category, CreatedDateTime = thisFile.CreationTime, LastWriteTime = thisFile.LastWriteTime, Length = thisFile.Length, Parent = thisFile.Directory.Parent.Name, Directory = thisFile.Directory.Name });
                cat.ExcludedFiles++;
                return;
            }
        }

        if (Math.Abs(programOptions.XLarger) > 0 && thisFile.Length > programOptions.XLarger * largerMultiplier)
        {
            fileReport.Add(new FileReport { File = thisFile.FullName, Extension = thisFile.Extension, Reason = "Large Size", Status = "Excluded", Lines = 0, Category = cat.Category });
            csvFiles.Add(new CsvFile { File = thisFile.FullName, Extension = thisFile.Extension, Reason = "Large Size", Status = "Excluded", Lines = 0, Category = cat.Category, CreatedDateTime = thisFile.CreationTime, LastWriteTime = thisFile.LastWriteTime, Length = thisFile.Length, Parent = thisFile.Directory.Parent.Name, Directory = thisFile.Directory.Name });
            cat.ExcludedFiles++;
            return;
        }

        if (Math.Abs(programOptions.XSmaller) > 0 && thisFile.Length < programOptions.XSmaller * 1024)
        {
            fileReport.Add(new FileReport { File = thisFile.FullName, Extension = thisFile.Extension, Reason = "Small Size", Status = "Excluded", Lines = 0, Category = cat.Category });
            csvFiles.Add(new CsvFile { File = thisFile.FullName, Extension = thisFile.Extension, Reason = "Small Size", Status = "Excluded", Lines = 0, Category = cat.Category, CreatedDateTime = thisFile.CreationTime, LastWriteTime = thisFile.LastWriteTime, Length = thisFile.Length, Parent = thisFile.Directory.Parent.Name, Directory = thisFile.Directory.Name });
            cat.ExcludedFiles++;
            return;
        }

        bool inComment = false;
        bool handleMulti = !string.IsNullOrWhiteSpace(cat.MultilineCommentStart);
        int fileLineCount = 0;
        foreach (string line in File.ReadLines(i.FullName))
        {
            fileLineCount++;
            string trimmedLine = line.Trim();

            if (string.IsNullOrWhiteSpace(trimmedLine))
            {
                cat.Empty++;
            }
            else
            {
                if (handleMulti)
                {
                    if (inComment)
                    {
                        cat.Comments++;
                        if (trimmedLine.EndsWith(cat.MultilineCommentEnd, StringComparison.OrdinalIgnoreCase))
                        {
                            inComment = false;
                        }
                    }
                    else
                    {
                        if (trimmedLine.StartsWith(cat.MultilineCommentStart, StringComparison.OrdinalIgnoreCase))
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
                            foreach (string unused in cat.SingleLineComment.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Where(s => trimmedLine.StartsWith(s, StringComparison.OrdinalIgnoreCase)))
                            {
                                cat.Comments++;
                            }
                        }
                    }
                }
                else
                {
                    foreach (string unused in cat.SingleLineComment.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Where(s => trimmedLine.StartsWith(s, StringComparison.OrdinalIgnoreCase)))
                    {
                        cat.Comments++;
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

        fileReport.Add(new FileReport { File = thisFile.FullName, Extension = ext, Reason = "Match", Status = "Included", Lines = fileLineCount, Category = cat.Category });
        csvFiles.Add(new CsvFile { File = thisFile.FullName, Extension = ext, Reason = "Match", Status = "Included", Lines = fileLineCount, Category = cat.Category, CreatedDateTime = thisFile.CreationTime, LastWriteTime = thisFile.LastWriteTime, Length = thisFile.Length, Parent = thisFile.Directory.Parent.Name, Directory = thisFile.Directory.Name });
        cat.IncludedFiles++;
    }

    private static void ExportToCsv()
    {
        StringBuilder sb = new ();

        sb.AppendLine("File,Lines,Extension,CreationDateTime,Category,Status,Reason,Length,Directory,Parent,CreatedDateTime,LastWriteTime");
        foreach (CsvFile file in csvFiles.OrderBy(f => f.File))
        {
            sb.Append(StringToCSVCell(file.File) + ",");
            sb.Append(file.Lines + ",");
            sb.Append(file.Extension + ",");
            sb.Append(file.CreatedDateTime + ",");
            sb.Append(file.Category + ",");
            sb.Append(file.Status + ",");
            sb.Append(file.Reason + ",");
            sb.Append(file.Length + ",");
            sb.Append(file.Directory + ",");
            sb.Append(file.Parent + ",");
            sb.Append(file.CreatedDateTime + ",");
            sb.Append(file.LastWriteTime + ",");
            sb.AppendLine();
        }

        if (string.IsNullOrEmpty(programOptions.OutputFile))
        {
            programOptions.OutputFile = Path.Combine($"{Directory.GetCurrentDirectory()}", "output.csv");
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
        bool mustQuote = str.Contains(',') || str.Contains('"') || str.Contains('\r') || str.Contains('\n');
        if (mustQuote)
        {
            StringBuilder sb = new ();
            sb.Append('"');
            foreach (char nextChar in str)
            {
                sb.Append(nextChar);
                if (nextChar == '"')
                {
                    sb.Append('"');
                }
            }

            sb.Append('"');
            return sb.ToString();
        }

        return str;
    }
}