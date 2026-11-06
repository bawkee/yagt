using System.Collections.Generic;

namespace SourceGit.Models
{
    public class CommitSubjectMatch
    {
        public string SHA { get; set; } = string.Empty;
        public List<string> Branches { get; set; } = [];
        public string BranchNames => string.Join(", ", Branches);
    }
}
