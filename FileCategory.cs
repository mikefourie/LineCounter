// --------------------------------------------------------------------------------------------------------------------
// <copyright file="FileCategory.cs" company="Mike Fourie"> (c) Mike Fourie. All other rights reserved.</copyright>
// --------------------------------------------------------------------------------------------------------------------
namespace LineCounter
{
    public class FileCategory
    {
        public bool Include { get; set; }

        public string Category { get; set; }

        public string FileTypes { get; set; }

        public bool Extensionless { get; set; }

        public string SingleLineComment { get; set; }

        public string MultilineCommentStart { get; set; }

        public string MultilineCommentEnd { get; set; }

        public string NameExclusions { get; set; }

        public int TotalLines { get; set; }

        public int TotalFiles { get; set; }

        public int Code { get; set; }

        public int Comments { get; set; }

        public int Empty { get; set; }

        public int IncludedFiles { get; set; }

        public int ExcludedFiles { get; set; }
    }
}