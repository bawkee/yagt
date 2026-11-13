using System;
using System.Collections.Generic;

using Avalonia.Collections;

namespace SourceGit.ViewModels
{
    public partial class Histories
    {
        public bool IsHistoryFocused
        {
            get => _repo.UIStates.IsHistoryFocused;
            set
            {
                if (value == _repo.UIStates.IsHistoryFocused)
                    return;

                _repo.UIStates.IsHistoryFocused = value;
                NotifyHistoryScopeChanged();
                _repo.RefreshCommits();
            }
        }

        public bool IsSelectedBranchHighlighting => GraphHighlighting == Models.CommitGraphHighlighting.SelectedBranchOnly;

        public bool IsBranchHistoryMode => IsHistoryFocused || IsSelectedBranchHighlighting;

        public bool CanHighlightSelectedBranch => !string.IsNullOrEmpty(_repo.UIStates.SelectedHistoryBranch) &&
            !_repo.UIStates.SelectedHistoryBranch.Equals("HEAD", StringComparison.Ordinal) &&
            !_repo.UIStates.SelectedHistoryBranch.Equals(_currentBranch?.IsDetachedHead == true ? "HEAD" : _currentBranch?.FullName, StringComparison.Ordinal);

        public bool ShowIncludedCurrentHistoryBranch => IncludeCurrentBranchInHistory && _currentBranch != null && CanHighlightSelectedBranch;

        public string IncludedCurrentHistoryBranchName => _currentBranch == null ? string.Empty :
            _currentBranch.IsDetachedHead ? "HEAD" : _currentBranch.FriendlyName;

        public bool IncludeCurrentBranchInHistory
        {
            get => _repo.UIStates.IncludeCurrentBranchInHistory;
            set
            {
                if (value == _repo.UIStates.IncludeCurrentBranchInHistory)
                    return;

                _repo.UIStates.IncludeCurrentBranchInHistory = value;
                RefreshAdditionalHistoryBranches();
                OnPropertyChanged();
                OnPropertyChanged(nameof(HistoryScopeDescription));
                OnPropertyChanged(nameof(ShowIncludedCurrentHistoryBranch));
                OnPropertyChanged(nameof(AvailableHistoryBranches));
                if (IsBranchHistoryMode)
                    _repo.RefreshCommits();
            }
        }

        public string HistoryBranchName
        {
            get
            {
                var name = _repo.UIStates.SelectedHistoryBranch;
                var branch = FindHistoryBranch(name);
                if (branch != null)
                    return branch.IsDetachedHead ? "HEAD" : branch.FriendlyName;

                if (name.StartsWith("refs/heads/", StringComparison.Ordinal))
                    return name.Substring(11);
                if (name.StartsWith("refs/remotes/", StringComparison.Ordinal))
                    return name.Substring(13);

                return string.IsNullOrEmpty(name) ? "HEAD" : name;
            }
        }

        public string HistoryScopeDescription
        {
            get
            {
                if (!IsBranchHistoryMode)
                {
                    return App.Text(_repo.UIStates.HistoryFilters.Count == 0 ?
                        "Histories.Scope.Repository" : "Histories.Scope.Custom");
                }

                var name = _repo.UIStates.SelectedHistoryBranch;
                if (string.IsNullOrEmpty(name))
                    return App.Text("Histories.Scope.NoBranch");
                if (_historyBranchesLoaded && FindHistoryBranch(name) == null)
                    return App.Text("Histories.Scope.MissingBranch", HistoryBranchName);

                var description = ShowIncludedCurrentHistoryBranch ?
                    App.Text("Histories.Scope.WithCurrent", HistoryBranchName, IncludedCurrentHistoryBranchName) :
                    App.Text("Histories.Scope.Branch", HistoryBranchName);

                if (_additionalHistoryBranches.Count > 0)
                    description = App.Text("Histories.Scope.WithAdditional", description, _additionalHistoryBranches.Count);
                if (HasShowFlag(Models.HistoryShowFlags.FirstParentOnly))
                    description = App.Text("Histories.Scope.FirstParent", description);
                if (!IsLoading && _commits.Count >= Preferences.Instance.MaxHistoryCommits)
                {
                    description = App.Text("Histories.Scope.Limited", description, Preferences.Instance.MaxHistoryCommits);
                    var branch = FindHistoryBranch(name);
                    if (branch != null && !_commits.Exists(x => x.SHA.Equals(branch.Head, StringComparison.Ordinal)))
                        description = App.Text("Histories.Scope.TargetOutsideLimit", description);
                }

                return description;
            }
        }

        public AvaloniaList<Models.Branch> AdditionalHistoryBranches => _additionalHistoryBranches;

        public List<Models.Branch> AvailableHistoryBranches
        {
            get
            {
                var branches = new List<Models.Branch>();
                foreach (var branch in _repo.Branches)
                {
                    if (!branch.IsDetachedHead &&
                        !branch.FullName.Equals(_repo.UIStates.SelectedHistoryBranch, StringComparison.Ordinal) &&
                        !(ShowIncludedCurrentHistoryBranch && branch.FullName.Equals(_currentBranch.FullName, StringComparison.Ordinal)) &&
                        !_repo.UIStates.AdditionalHistoryBranches.Contains(branch.FullName))
                        branches.Add(branch);
                }

                branches.Sort((l, r) => string.Compare(l.FriendlyName, r.FriendlyName, StringComparison.Ordinal));
                return branches;
            }
        }

        public void SetHistoryBranch(Models.Branch branch)
        {
            if (branch == null)
                return;

            var name = branch.IsDetachedHead ? "HEAD" : branch.FullName;
            if (name.Equals(_repo.UIStates.SelectedHistoryBranch, StringComparison.Ordinal))
                return;

            _repo.UIStates.SelectedHistoryBranch = name;
            _repo.UIStates.AdditionalHistoryBranches.Remove(name);
            RefreshAdditionalHistoryBranches();
            NotifyHistoryScopeChanged();
            if (IsBranchHistoryMode)
                _repo.RefreshCommits();
        }

        public void AddHistoryBranch(Models.Branch branch)
        {
            if (branch == null || branch.IsDetachedHead ||
                branch.FullName.Equals(_repo.UIStates.SelectedHistoryBranch, StringComparison.Ordinal) ||
                _repo.UIStates.AdditionalHistoryBranches.Contains(branch.FullName))
                return;

            _repo.UIStates.AdditionalHistoryBranches.Add(branch.FullName);
            RefreshAdditionalHistoryBranches();
            OnPropertyChanged(nameof(AvailableHistoryBranches));
            OnPropertyChanged(nameof(HistoryScopeDescription));
            if (IsBranchHistoryMode)
                _repo.RefreshCommits();
        }

        public void RemoveHistoryBranch(Models.Branch branch)
        {
            if (branch != null && _repo.UIStates.AdditionalHistoryBranches.Remove(branch.FullName))
            {
                RefreshAdditionalHistoryBranches();
                OnPropertyChanged(nameof(AvailableHistoryBranches));
                OnPropertyChanged(nameof(HistoryScopeDescription));
                if (IsBranchHistoryMode)
                    _repo.RefreshCommits();
            }
        }

        public void RemoveCurrentHistoryBranch()
        {
            if (_currentBranch == null)
                return;

            _repo.UIStates.AdditionalHistoryBranches.Remove(_currentBranch.FullName);
            IncludeCurrentBranchInHistory = false;
        }

        public bool RefreshHistoryBranches()
        {
            _historyBranchesLoaded = true;
            if (string.IsNullOrEmpty(_repo.UIStates.SelectedHistoryBranch) && _currentBranch != null)
                _repo.UIStates.SelectedHistoryBranch = _currentBranch.IsDetachedHead ? "HEAD" : _currentBranch.FullName;

            _repo.UIStates.AdditionalHistoryBranches.RemoveAll(x => FindHistoryBranch(x) == null);
            RefreshAdditionalHistoryBranches();
            NotifyHistoryScopeChanged();

            var signature = string.Join('\0', GetHistoryRefs());
            if (signature.Equals(_historyHeadSignature, StringComparison.Ordinal))
                return false;

            _historyHeadSignature = signature;
            return true;
        }

        public List<string> GetHistoryRefs()
        {
            var refs = new List<string>();
            if (!_historyBranchesLoaded)
                return refs;

            var branch = FindHistoryBranch(_repo.UIStates.SelectedHistoryBranch);
            if (branch != null && !string.IsNullOrEmpty(branch.Head))
                refs.Add(branch.Head);

            foreach (var name in _repo.UIStates.AdditionalHistoryBranches)
            {
                var additional = FindHistoryBranch(name);
                if (additional != null && !string.IsNullOrEmpty(additional.Head) && !refs.Contains(additional.Head))
                    refs.Add(additional.Head);
            }

            if (IncludeCurrentBranchInHistory && _currentBranch != null &&
                !string.IsNullOrEmpty(_currentBranch.Head) && !refs.Contains(_currentBranch.Head))
                refs.Add(_currentBranch.Head);

            return refs;
        }

        public void SetHistoryCommits(List<Models.Commit> commits, HashSet<string> highlightedCommits)
        {
            _historyHighlights = highlightedCommits;
            Commits = commits;
            OnPropertyChanged(nameof(HistoryScopeDescription));
        }

        public void NotifyHistoryScopeChanged()
        {
            OnPropertyChanged(nameof(IsHistoryFocused));
            OnPropertyChanged(nameof(IsSelectedBranchHighlighting));
            OnPropertyChanged(nameof(IsBranchHistoryMode));
            OnPropertyChanged(nameof(CanHighlightSelectedBranch));
            OnPropertyChanged(nameof(ShowIncludedCurrentHistoryBranch));
            OnPropertyChanged(nameof(IncludedCurrentHistoryBranchName));
            OnPropertyChanged(nameof(HistoryBranchName));
            OnPropertyChanged(nameof(HistoryScopeDescription));
            OnPropertyChanged(nameof(AvailableHistoryBranches));
        }

        private Models.Branch FindHistoryBranch(string name)
        {
            if (name.Equals("HEAD", StringComparison.Ordinal))
                return _currentBranch;

            return _repo.Branches.Find(x => x.FullName.Equals(name, StringComparison.Ordinal));
        }

        private void RefreshAdditionalHistoryBranches()
        {
            _additionalHistoryBranches.Clear();
            foreach (var name in _repo.UIStates.AdditionalHistoryBranches)
            {
                var branch = FindHistoryBranch(name);
                if (branch != null && !name.Equals(_repo.UIStates.SelectedHistoryBranch, StringComparison.Ordinal) &&
                    !(ShowIncludedCurrentHistoryBranch && name.Equals(_currentBranch.FullName, StringComparison.Ordinal)))
                    _additionalHistoryBranches.Add(branch);
            }
        }

        private readonly AvaloniaList<Models.Branch> _additionalHistoryBranches = [];
        private HashSet<string> _historyHighlights = [];
        private bool _historyBranchesLoaded = false;
        private string _historyHeadSignature = string.Empty;
    }
}
