// -----------------------------------------------------------------------------------------------------------------
// <copyright file="Program.cs" company="Mike Fourie"> (c) Mike Fourie. All other rights reserved.</copyright>
// --------------------------------------------------------------------------------------------------------------------
namespace LineCounterXP
{
    using CommandLine;
    using ConsoleTables;
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Text.Json;

    class Program
    {
        private static List<CsvFile> csvFiles;
        private static List<FileCategory> cats;
        private static IEnumerable<FileInfo> foundFiles;
        private static Options programOptions = new Options();

        static void Main(string[] args)
        {
            try
            {
                WriteHeader();
                Parser.Default.ParseArguments<Options>(args)
                       .WithParsed<Options>(o =>
                       {
                           programOptions = o;
                       });

                string path = Directory.GetCurrentDirectory();
                if (!string.IsNullOrEmpty(programOptions.Path))
                {
                    path = programOptions.Path;
                }

                DateTime start = DateTime.Now;
                if (Directory.Exists(path))
                {
                    Scan(path);
                    DateTime end = DateTime.Now;
                    TimeSpan t = end - start;

                    if (programOptions.ExportCsv)
                    {
                        Console.WriteLine("...writing to csv file");
                        ExportToCsv();
                    }

                    var table = new ConsoleTable("Category", "Files", "Lines", "Code", "Comments", "Empty", "Files Inc.", "Files Excl.");
                    foreach (FileCategory fc in cats)
                    {
                        if (fc.TotalLines > 0)
                        {
                            table.AddRow(fc.Category, fc.TotalFiles, fc.TotalLines, fc.Code, fc.Comments, fc.Empty, fc.IncludedFiles, fc.ExcludedFiles);
                        }
                    }

                    table.Options.EnableCount = false;
                    table.Write(Format.Minimal);

                    Console.WriteLine($"Scan Time: {t.Seconds}s:{t.Milliseconds}ms");
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

        private static void WriteHeader()
        {
            Console.WriteLine("Line Counter --- run dotnet linecounter.dll --help for help");
            Console.WriteLine("----------------------------------------------------------------------\n");
        }

        private static void Scan(string path)
        {
            var recursiveSearch = programOptions.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

            string rootPath = path.Replace("*", string.Empty);
            DirectoryInfo dir = new DirectoryInfo(rootPath);
            foundFiles = dir.GetFiles("*", recursiveSearch).Where(x => (x.Attributes & FileAttributes.Hidden) == 0);
            csvFiles = new List<CsvFile>(foundFiles.Count());           
            if (string.IsNullOrEmpty(programOptions.Categories))
            {
                programOptions.Categories = Path.Combine($"{Directory.GetCurrentDirectory()}","categories.json");
            }

            cats =  JsonSerializer.Deserialize<List<FileCategory>>(File.ReadAllText(programOptions.Categories));
            System.Threading.Tasks.Task[] tasks = new System.Threading.Tasks.Task[cats.Count];
            ObservableCollection<FileReport> xxx = new ObservableCollection<FileReport>();
            string fileexclusions = programOptions.XFiles;
            string folderexclusions;
            folderexclusions = string.IsNullOrEmpty(programOptions.XFolders) ? ".git" : $".git,{programOptions.XFolders}";

            for (int i = 0; i < cats.Count; i++)
            {
                int i1 = i;
                tasks[i] = System.Threading.Tasks.Task.Factory.StartNew(() => ProcessPath(cats[i1], foundFiles, xxx, fileexclusions, folderexclusions));
            }

            // Block until all tasks complete.
            System.Threading.Tasks.Task.WaitAll(tasks);
            ObservableCollection<FileReport> ReportedFiles = xxx;

            int tfiles = 0;
            int tlines = 0;
            int tincluded = 0;
            int texcluded = 0;
            double tcomments = 0;
            double tcode = 0;
            double tempty = 0;

            foreach (FileCategory f in cats)
            {
                tfiles += f.TotalFiles;
                tlines += f.TotalLines;
                tcomments += f.Comments;
                tempty += f.Empty;
                tcode += f.Code;
                tincluded += f.IncludedFiles;
                texcluded += f.ExcludedFiles;
            }

            cats.Add(new FileCategory { Include = false, Code = Convert.ToInt32(tcode), Comments = Convert.ToInt32(tcomments), ExcludedFiles = texcluded, Empty = Convert.ToInt32(tempty), FileTypes = "--------", IncludedFiles = tincluded, MultilineCommentEnd = "--------", MultilineCommentStart = "--------", Category = "TOTAL", TotalLines = tlines, TotalFiles = tfiles, SingleLineComment = "--------", NameExclusions = "--------" });
            WhatDidWeSkip();
            if (tlines == 0 && tfiles == 0)
            {
                Console.WriteLine("Nothing scanned...");
            }
        }

        private static void WhatDidWeSkip()
        {
            ArrayList usedExtensions = new ArrayList();
            ObservableCollection<IgnoredFile> ignoredFiles = new ObservableCollection<IgnoredFile>();
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
                if (!usedExtensions.Contains(f.Extension.ToLower()))
                {
                    ignoredFiles.Add(new IgnoredFile { File = f.FullName, Extension = f.Extension });
                    csvFiles.Add(new CsvFile { File = f.FullName, Extension = f.Extension, Status = "Excluded", Reason = "Extension", Lines = 0, CreatedDateTime = f.CreationTime, LastWriteTime = f.LastWriteTime, Length = f.Length, Parent = f.Directory.Parent.Name, Directory = f.Directory.Name });
                }
            }

            string[] extension = new string[ignoredFiles.Count];
            int j = 0;
            foreach (IgnoredFile ig in ignoredFiles)
            {
                extension[j++] = ig.Extension;
            }

            var groups = extension.GroupBy(v => v);
            List<IgnoredExtension> ignoredExtensions = new List<IgnoredExtension>();
            foreach (var group in groups)
            {
                ignoredExtensions.Add(new IgnoredExtension { Extension = group.Key, Count = group.Count() });
            }
        }

        private static void ProcessPath(FileCategory cat, IEnumerable<FileInfo> fileInfo, ObservableCollection<FileReport> filereport, string fileExclusions, string folderExclusions)
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

            foreach (FileInfo f in cat.FileTypes.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).SelectMany(type => fileInfo.Where(f => f.Extension.ToLower() == type.TrimStart().ToLower())))
            {
                cat.TotalFiles++;
                CountLines(f, cat, filereport, fileExclusions, folderExclusions);
            }

            cat.Code = cat.TotalLines - cat.Empty - cat.Comments;
        }

        private static void CountLines(FileSystemInfo i, FileCategory cat, ObservableCollection<FileReport> filereport, string fileExclusions, string folderExclusions)
        {
            FileInfo thisFile = new FileInfo(i.FullName);
            if (!string.IsNullOrEmpty(fileExclusions))
            {
                if (fileExclusions.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Any(s => thisFile.Name.ToLower().Contains(s.ToLower())))
                {
                    filereport.Add(new FileReport { File = thisFile.FullName, Extension = thisFile.Extension, Reason = "Global File Name", Status = "Excluded", Lines = 0, Category = cat.Category });
                    csvFiles.Add(new CsvFile { File = thisFile.FullName, Extension = thisFile.Extension, Reason = "Global File Name", Status = "Excluded", Lines = 0, Category = cat.Category, CreatedDateTime = thisFile.CreationTime, LastWriteTime = thisFile.LastWriteTime, Length = thisFile.Length, Parent = thisFile.Directory.Parent.Name, Directory = thisFile.Directory.Name });
                    cat.ExcludedFiles++;
                    return;
                }
            }

            if (!string.IsNullOrEmpty(folderExclusions))
            {
                if (folderExclusions.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Any(s => thisFile.DirectoryName.ToLower().Contains(s.ToLower())))
                {
                    filereport.Add(new FileReport { File = thisFile.FullName, Extension = thisFile.Extension, Reason = "Global Folder Name", Status = "Excluded", Lines = 0, Category = cat.Category });
                    csvFiles.Add(new CsvFile { File = thisFile.FullName, Extension = thisFile.Extension, Reason = "Global Folder Name", Status = "Excluded", Lines = 0, Category = cat.Category, CreatedDateTime = thisFile.CreationTime, LastWriteTime = thisFile.LastWriteTime, Length = thisFile.Length, Parent = thisFile.Directory.Parent.Name, Directory = thisFile.Directory.Name });
                    cat.ExcludedFiles++;
                    return;
                }
            }

            if (!string.IsNullOrEmpty(cat.NameExclusions))
            {
                if (cat.NameExclusions.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Any(s => thisFile.Name.ToLower().Contains(s.ToLower())))
                {
                    filereport.Add(new FileReport { File = thisFile.FullName, Extension = thisFile.Extension, Reason = "Category File Name", Status = "Excluded", Lines = 0, Category = cat.Category });
                    csvFiles.Add(new CsvFile { File = thisFile.FullName, Extension = thisFile.Extension, Reason = "Category File Name", Status = "Excluded", Lines = 0, Category = cat.Category, CreatedDateTime = thisFile.CreationTime, LastWriteTime = thisFile.LastWriteTime, Length = thisFile.Length, Parent = thisFile.Directory.Parent.Name, Directory = thisFile.Directory.Name });
                    cat.ExcludedFiles++;
                    return;
                }
            }

            if (Math.Abs(programOptions.XLarger) > 0 && (thisFile.Length > programOptions.XLarger * 1024 * 1024))
            {
                filereport.Add(new FileReport { File = thisFile.FullName, Extension = thisFile.Extension, Reason = "Large Size", Status = "Excluded", Lines = 0, Category = cat.Category });
                csvFiles.Add(new CsvFile { File = thisFile.FullName, Extension = thisFile.Extension, Reason = "Large Size", Status = "Excluded", Lines = 0, Category = cat.Category, CreatedDateTime = thisFile.CreationTime, LastWriteTime = thisFile.LastWriteTime, Length = thisFile.Length, Parent = thisFile.Directory.Parent.Name, Directory = thisFile.Directory.Name });
                cat.ExcludedFiles++;
                return;
            }

            if (Math.Abs(programOptions.XSmaller) > 0 && (thisFile.Length < programOptions.XSmaller * 1024))
            {
                filereport.Add(new FileReport { File = thisFile.FullName, Extension = thisFile.Extension, Reason = "Small Size", Status = "Excluded", Lines = 0, Category = cat.Category });
                csvFiles.Add(new CsvFile { File = thisFile.FullName, Extension = thisFile.Extension, Reason = "Small Size", Status = "Excluded", Lines = 0, Category = cat.Category, CreatedDateTime = thisFile.CreationTime, LastWriteTime = thisFile.LastWriteTime, Length = thisFile.Length, Parent = thisFile.Directory.Parent.Name, Directory = thisFile.Directory.Name });
                cat.ExcludedFiles++;
                return;
            }

            bool incomment = false;
            bool handlemulti = !string.IsNullOrWhiteSpace(cat.MultilineCommentStart);
            int filelinecount = 0;
            foreach (string line in File.ReadLines(i.FullName))
            {
                filelinecount++;
                string line1 = line;
                if (string.IsNullOrWhiteSpace(line))
                {
                    cat.Empty++;
                }
                else
                {
                    if (handlemulti)
                    {
                        if (incomment)
                        {
                            cat.Comments++;
                            if (line1.TrimEnd(' ').EndsWith(cat.MultilineCommentEnd, StringComparison.OrdinalIgnoreCase))
                            {
                                incomment = false;
                            }
                        }
                        else
                        {
                            if (line1.TrimStart(' ').StartsWith(cat.MultilineCommentStart, StringComparison.OrdinalIgnoreCase))
                            {
                                incomment = true;
                                cat.Comments++;
                                if (line1.TrimEnd(' ').EndsWith(cat.MultilineCommentEnd, StringComparison.OrdinalIgnoreCase))
                                {
                                    incomment = false;
                                }
                            }
                            else
                            {
                                foreach (string s in cat.SingleLineComment.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Where(s => line1.TrimStart(' ').StartsWith(s, StringComparison.OrdinalIgnoreCase)))
                                {
                                    cat.Comments++;
                                }
                            }
                        }
                    }
                    else
                    {
                        foreach (string s in cat.SingleLineComment.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Where(s => line1.TrimStart(' ').StartsWith(s, StringComparison.OrdinalIgnoreCase)))
                        {
                            cat.Comments++;
                        }
                    }
                }

                cat.TotalLines++;
            }

            filereport.Add(new FileReport { File = thisFile.FullName, Extension = thisFile.Extension, Reason = "Match", Status = "Included", Lines = filelinecount, Category = cat.Category });
            csvFiles.Add(new CsvFile { File = thisFile.FullName, Extension = thisFile.Extension, Reason = "Match", Status = "Included", Lines = filelinecount, Category = cat.Category, CreatedDateTime = thisFile.CreationTime, LastWriteTime = thisFile.LastWriteTime, Length = thisFile.Length, Parent = thisFile.Directory.Parent.Name, Directory = thisFile.Directory.Name });
            cat.IncludedFiles++;
        }

        private static void ExportToCsv()
        {
            StringBuilder sb = new StringBuilder();

            sb.AppendLine("File,Lines,Extension,CreationDateTime,Category,Status,Reason,Length,Directory,Parent,CreatedDateTime,LastWriteTime");
            foreach (CsvFile file in csvFiles)
            {
                sb.Append(file.File + ",");
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
    }
}