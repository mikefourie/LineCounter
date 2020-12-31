// --------------------------------------------------------------------------------------------------------------------
// <copyright file="CsvFile.cs" company="Mike Fourie"> (c) Mike Fourie. All other rights reserved.</copyright>
// --------------------------------------------------------------------------------------------------------------------
namespace LineCounterXP
{
    using System;

    public class CsvFile
    {
        public string File { get; set; }

        public int Lines { get; set; }

        public string Extension { get; set; }

        public string Directory { get; set; }
        
        public string Parent { get; set; }

        public string Category { get; set; }
        
        public string Status { get; set; }

        public string Reason { get; set; }

        public DateTime CreatedDateTime { get; set; }

        public DateTime LastWriteTime { get; set; }
        
        public long Length { get; set; }
    }
}