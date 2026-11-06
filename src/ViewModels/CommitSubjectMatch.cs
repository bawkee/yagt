using System;

using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.ViewModels
{
    public class CommitSubjectMatch : ObservableObject
    {
        public string SHA => _match.SHA;
        public bool HasBranches => _match.Branches.Count > 0;
        public string BranchNames => _showAllBranches ? _match.BranchNames : _branchPreview;
        public int RemainingBranchCount => Math.Max(0, _match.Branches.Count - VISIBLE_BRANCHES);
        public bool HasMoreBranches => !_showAllBranches && RemainingBranchCount > 0;

        public CommitSubjectMatch(Models.CommitSubjectMatch match)
        {
            _match = match;
            _branchPreview = string.Join(", ", match.Branches.GetRange(0, Math.Min(match.Branches.Count, VISIBLE_BRANCHES)));
        }

        public void ShowAllBranches()
        {
            if (_showAllBranches)
                return;

            _showAllBranches = true;
            OnPropertyChanged(nameof(BranchNames));
            OnPropertyChanged(nameof(HasMoreBranches));
        }

        private const int VISIBLE_BRANCHES = 15;
        private readonly Models.CommitSubjectMatch _match;
        private readonly string _branchPreview;
        private bool _showAllBranches;
    }
}
