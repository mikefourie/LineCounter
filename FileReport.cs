// --------------------------------------------------------------------------------------------------------------------
// <copyright file="FileReport.cs" company="Mike Fourie"> (c) Mike Fourie. All other rights reserved.</copyright>
// --------------------------------------------------------------------------------------------------------------------
namespace LineCounter;

public class FileReport
{
    public string File { get; set; }

    public int Lines { get; set; }

    public string Extension { get; set; }

    public string Category { get; set; }

    public string Status { get; set; }

    public string Reason { get; set; }
}