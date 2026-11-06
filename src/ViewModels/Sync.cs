using System;
using System.Threading.Tasks;

using Avalonia.Threading;

namespace SourceGit.ViewModels
{
    public class Sync : Popup
    {
        public Models.Branch Current { get; }
        public Models.Branch Upstream { get; }

        public bool UseRebase
        {
            get => _repo.UIStates.PreferRebaseInsteadOfMerge;
            set => _repo.UIStates.PreferRebaseInsteadOfMerge = value;
        }

        public bool StashLocalChanges
        {
            get => _stashLocalChanges;
            set => SetProperty(ref _stashLocalChanges, value);
        }

        public Sync(Repository repo, Models.Remote remote, Models.Branch upstream)
        {
            _repo = repo;
            _remote = remote;
            Current = repo.CurrentBranch;
            Upstream = upstream;
            _stashLocalChanges = Preferences.Instance.UseStashAndReapplyByDefault;
        }

        public override async Task<bool> Sure()
        {
            using var lockWatcher = _repo.LockWatcher();
            var log = _repo.CreateLog("Sync");
            Use(log);

            try
            {
                var current = await new Commands.QueryCurrentBranch(_repo.FullPath).GetResultAsync().ConfigureAwait(false);
                if (!current.Equals(Current.Name, StringComparison.Ordinal))
                {
                    _repo.SendNotification("The current branch has changed. Please reopen Sync.", true);
                    return false;
                }

                var needPopStash = false;
                if (StashLocalChanges && await new Commands.CountLocalChanges(_repo.FullPath, true).GetResultAsync().ConfigureAwait(false) > 0)
                {
                    if (!await new Commands.Stash(_repo.FullPath).Use(log).PushAsync("SYNC_AUTO_STASH").ConfigureAwait(false))
                        return false;

                    needPopStash = true;
                }

                Dispatcher.UIThread.Post(() => ProgressDescription = $"Pulling {_remote.Name} ...");
                var pulled = await new Commands.Pull(_repo.FullPath, _remote, null, UseRebase).Use(log).ExecAsync().ConfigureAwait(false);
                if (!pulled)
                {
                    if (needPopStash)
                        _repo.SendNotification("Sync stopped. Your local changes remain in the SYNC_AUTO_STASH stash. Restore them after resolving the pull failure.", true);

                    return false;
                }

                Dispatcher.UIThread.Post(_repo.MarkFetched);

                // Git's own autostash can leave conflicts even when pull exits successfully.
                var changes = await new Commands.QueryLocalChanges(_repo.FullPath, false).GetResultAsync().ConfigureAwait(false);
                if (changes.Exists(x => x.IsConflicted))
                {
                    _repo.SendNotification("Sync stopped because pulling left conflicts. Resolve them before synchronizing again.", true);
                    return false;
                }

                await Dispatcher.UIThread.InvokeAsync(() => _repo.AutoUpdateSubmodulesAsync(log)).ConfigureAwait(false);

                if (needPopStash && !await new Commands.Stash(_repo.FullPath).Use(log).PopAsync("stash@{0}").ConfigureAwait(false))
                    return false;

                Dispatcher.UIThread.Post(() => ProgressDescription = $"Pushing {Current.Name} -> {Upstream.FriendlyName} ...");

                return await new Commands.Push(_repo.FullPath, Current, _remote, Upstream, false, true, false, false, false)
                    .Use(log)
                    .ExecAsync()
                    .ConfigureAwait(false);
            }
            finally
            {
                log.Complete();
                Dispatcher.UIThread.Post(() =>
                {
                    _repo.MarkBranchesDirtyManually();
                    _repo.MarkStashesDirtyManually();
                });
            }
        }

        private readonly Repository _repo;
        private readonly Models.Remote _remote;
        private bool _stashLocalChanges;
    }
}
