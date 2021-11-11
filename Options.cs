// --------------------------------------------------------------------------------------------------------------------
// <copyright file="Options.cs" company="Mike Fourie"> (c) Mike Fourie. All other rights reserved.</copyright>
// --------------------------------------------------------------------------------------------------------------------
namespace LineCounter;

using CommandLine;

public class Options
{
    [Option('v', "verbose", Required = false, HelpText = "Set output to verbose messages")]
    public bool Verbose { get; set; }

    [Option('r', "recursive", Required = false, HelpText = "Set recursive folder search", Default = true)]
    public bool Recursive { get; set; }

    [Option('p', "path", Required = false, HelpText = "Set the path to search")]
    public string Path { get; set; }

    [Option('s', "xsmaller", Required = false, HelpText = "Set the smaller size to exclude")]
    public double XSmaller { get; set; }

    [Option('l', "xlarger", Required = false, HelpText = "Set the larger size to exclude")]
    public double XLarger { get; set; }

    [Option('f', "xfiles", Required = false, HelpText = "Set the files to exclude")]
    public string XFiles { get; set; }

    [Option('d', "xfolders", Required = false, HelpText = "Set the folders to exclude")]
    public string XFolders { get; set; }

    [Option('c', "categories", Required = false, HelpText = "Set the categories file")]
    public string Categories { get; set; }

    [Option('e', "exportcsv", Required = false, HelpText = "Export full results to CSV. Default is false", Default = false)]
    public bool ExportCsv { get; set; }

    [Option('o', "outputfile", Required = false, HelpText = "Set name of the CSV file")]
    public string OutputFile { get; set; }
}
