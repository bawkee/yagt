using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.ViewModels
{
    public class CommitSubjectMatches : ObservableObject
    {
        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (SetProperty(ref _isEnabled, value))
                    Refresh();
            }
        }

        public bool IsLoading
        {
            get => _isLoading;
            private set => SetProperty(ref _isLoading, value);
        }

        public bool HasFailed
        {
            get => _hasFailed;
            private set => SetProperty(ref _hasFailed, value);
        }

        public bool IsEmpty
        {
            get => _isEmpty;
            private set => SetProperty(ref _isEmpty, value);
        }

        public bool IsTruncated
        {
            get => _isTruncated;
            private set => SetProperty(ref _isTruncated, value);
        }

        public List<Models.CommitSubjectMatch> Matches
        {
            get => _matches;
            private set => SetProperty(ref _matches, value);
        }

        public Models.CommitSubjectMatch SelectedCommit
        {
            get => _selectedCommit;
            private set => SetProperty(ref _selectedCommit, value);
        }

        public CommitSubjectMatches(string repo, bool enabled)
        {
            _repo = repo;
            _isEnabled = enabled;
        }

        public void SetCommit(Models.Commit commit)
        {
            _commit = commit;
            Refresh();
        }

        public void Cancel()
        {
            _cancellation?.Cancel();
            _cancellation?.Dispose();
            _cancellation = null;
            IsLoading = false;
        }

        public void Refresh()
        {
            Cancel();
            Matches = [];
            SelectedCommit = null;
            HasFailed = false;
            IsEmpty = false;
            IsTruncated = false;

            if (!_isEnabled || _commit == null)
                return;

            _cancellation = new CancellationTokenSource();
            var token = _cancellation.Token;
            var commit = _commit;
            IsLoading = true;

            Task.Run(async () =>
            {
                try
                {
                    // Avoid starting a full-history search for every intermediate keyboard selection.
                    await Task.Delay(200, token).ConfigureAwait(false);
                    var query = new Commands.QueryCommitSubjectMatches(_repo, commit) { CancellationToken = token };
                    var matches = await query.GetResultAsync().ConfigureAwait(false);
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (token.IsCancellationRequested)
                            return;

                        Matches = matches ?? [];
                        SelectedCommit = matches != null ? query.SelectedCommit : null;
                        HasFailed = matches == null;
                        IsEmpty = matches is { Count: 0 };
                        IsTruncated = matches != null && query.IsTruncated;
                        IsLoading = false;
                    });
                }
                catch (OperationCanceledException)
                {
                    // A newer selection or disabling the feature cancels this lookup.
                }
                catch (Exception)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (!token.IsCancellationRequested)
                        {
                            HasFailed = true;
                            IsLoading = false;
                        }
                    });
                }
            }, token);
        }

        private readonly string _repo;
        private Models.Commit _commit;
        private CancellationTokenSource _cancellation;
        private bool _isEnabled;
        private bool _isLoading;
        private bool _hasFailed;
        private bool _isEmpty;
        private bool _isTruncated;
        private List<Models.CommitSubjectMatch> _matches = [];
        private Models.CommitSubjectMatch _selectedCommit;
    }
}
