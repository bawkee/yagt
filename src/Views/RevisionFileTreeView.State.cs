using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace SourceGit.Views
{
    public partial class RevisionFileTreeView
    {
        protected override void OnDataContextChanged(EventArgs e)
        {
            if (_treeViewModel != null)
                _treeViewModel.PropertyChanged -= OnTreeViewModelChanged;

            base.OnDataContextChanged(e);
            _treeViewModel = DataContext as ViewModels.CommitDetail;
            if (_treeViewModel != null)
            {
                _treeViewModel.PropertyChanged += OnTreeViewModelChanged;

                var repo = _treeViewModel.Repository.FullPath;
                if (!string.Equals(_treeRepository, repo, StringComparison.Ordinal))
                {
                    _treeRepository = repo;
                    _treeState = new ViewModels.RevisionFileTreeState();
                    _selectedObject = null;
                    _selectedPath = string.Empty;
                    _selectedType = Models.ObjectType.None;
                }
            }

            InvalidateTreeRevision();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);

            // Avalonia has no effective-visibility event, so watch every ancestor that can hide this tree.
            foreach (var ancestor in this.GetVisualAncestors())
            {
                ancestor.PropertyChanged += OnTreeAncestorChanged;
                _treeVisibilityAncestors.Add(ancestor);
            }
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            _isTreeLoaded = false;
            CancelTreeRequests();
            foreach (var ancestor in _treeVisibilityAncestors)
                ancestor.PropertyChanged -= OnTreeAncestorChanged;
            _treeVisibilityAncestors.Clear();
            base.OnDetachedFromVisualTree(e);
        }

        protected override void OnUnloaded(RoutedEventArgs e)
        {
            _isTreeLoaded = false;
            CancelTreeRequests();
            base.OnUnloaded(e);
        }

        private void OnTreeViewModelChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ViewModels.CommitDetail.ActiveTabIndex))
                ScheduleTreeRefresh();
        }

        private void OnTreeAncestorChanged(object sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == IsVisibleProperty)
                ScheduleTreeRefresh();
        }

        private bool CanLoadTree()
        {
            return _isTreeLoaded && IsEffectivelyVisible &&
                DataContext is ViewModels.CommitDetail { ActiveTabIndex: 2, Commit: not null };
        }

        /// <summary>
        ///     Cancels the debounce and all Git work for the current revision. Every change that can make
        ///     that work stale (revision, tab, visibility, attachment) goes through here, so a request is
        ///     current exactly while its token is not cancelled.
        /// </summary>
        private void CancelTreeRequests()
        {
            _isSearchPending = false;
            _previewNeedsRefresh = true;
            if (_treeRequests == null)
                return;

            _treeRequests.Cancel();
            _treeRequests.Dispose();
            _treeRequests = null;
        }

        private CancellationToken GetTreeRequestToken()
        {
            _treeRequests ??= new CancellationTokenSource();
            return _treeRequests.Token;
        }

        private void InvalidateTreeRevision()
        {
            _loadedRevision = string.Empty;
            _isShowingSearchResult = false;
            _treeLoadRetryCount = 0;
            ScheduleTreeRefresh();
        }

        private void ScheduleTreeRefresh()
        {
            CancelTreeRequests();
            _ = RefreshTreeAfterDelayAsync(GetTreeRequestToken());
        }

        private async Task RefreshTreeAfterDelayAsync(CancellationToken token)
        {
            try
            {
                // Wait for navigation to settle before starting Git or rebuilding the list.
                await Task.Delay(TREE_REFRESH_DELAY, token);
                await RefreshTreeIfNeededAsync(token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // A newer selection, tab or visibility change owns the next refresh.
            }
            catch (Exception e)
            {
                if (!token.IsCancellationRequested && CanLoadTree())
                    HandleTreeLoadFailure(e.Message);
            }
        }

        private async Task RefreshTreeIfNeededAsync(CancellationToken token)
        {
            if (!CanLoadTree())
            {
                if (string.IsNullOrEmpty(_loadedRevision) && Rows.Count > 0)
                    ReplaceTreeRows([], null);
                return;
            }

            var vm = (ViewModels.CommitDetail)DataContext;
            var sha = vm.Commit.SHA;
            if (string.Equals(_loadedRevision, sha, StringComparison.Ordinal))
            {
                if (_previewNeedsRefresh)
                {
                    _previewNeedsRefresh = false;
                    await vm.ViewRevisionFileAsync(_selectedObject, token);
                }
                return;
            }

            // Rows from the previous revision stay visible (and inert) until the new ones replace them.
            var repo = vm.Repository.FullPath;
            var state = _treeState;
            var expanded = state.GetExpandedPaths(_selectedPath);
            var selectedPath = _selectedPath;
            var selectedType = _selectedType;
            var result = await Task.Run(async () =>
            {
                var roots = await state.LoadAsync(repo, sha, expanded, token).ConfigureAwait(false);
                if (roots == null)
                    return (Rows: (List<ViewModels.RevisionFileTreeNode>)null, Selection: (Models.Object)null);

                var rows = new List<ViewModels.RevisionFileTreeNode>();
                MakeRows(rows, roots, 0);

                var selection = rows.Find(x => x.Backend.Path.Equals(selectedPath, StringComparison.Ordinal))?.Backend;
                if (selection == null && !string.IsNullOrEmpty(selectedPath))
                {
                    // The selection may live in a folder that is too deep to have been restored.
                    var objects = await new Commands.QueryRevisionObjects(repo, sha, selectedPath) { CancellationToken = token }
                        .GetResultAsync()
                        .ConfigureAwait(false);
                    if (objects is { Count: 1 } && objects[0].Path.Equals(selectedPath, StringComparison.Ordinal))
                        selection = objects[0];
                }

                if (selection?.Type != selectedType)
                    selection = null;

                return (Rows: rows, Selection: selection);
            }, token);

            token.ThrowIfCancellationRequested();
            if (result.Rows == null)
            {
                HandleTreeLoadFailure("Git could not read the revision tree.");
                return;
            }

            _loadedRevision = sha;
            _selectedObject = result.Selection;
            _previewNeedsRefresh = false;
            _treeLoadRetryCount = 0;
            ReplaceTreeRows(result.Rows, result.Selection);
            await vm.ViewRevisionFileAsync(result.Selection, token);
        }

        private void HandleTreeLoadFailure(string reason)
        {
            ReplaceTreeRows([], null);
            if (_treeLoadRetryCount++ == 0)
                ScheduleTreeRefresh();
            else if (DataContext is ViewModels.CommitDetail vm)
                vm.Repository.SendNotification($"Failed to load files for the selected commit. {reason}", true);
        }

        private void ReplaceTreeRows(List<ViewModels.RevisionFileTreeNode> rows, Models.Object selection)
        {
            _disableSelectionChangingEvent = true;
            try
            {
                Rows.Clear();
                Rows.AddRange(rows);

                var selected = selection == null ? null : rows.Find(x => x.Backend.Path.Equals(selection.Path, StringComparison.Ordinal));
                RowsList.SelectedItem = selected;
                if (selected != null)
                    RowsList.ScrollIntoView(selected);
            }
            finally
            {
                _disableSelectionChangingEvent = false;
            }
        }

        private async Task PreviewTreeSelectionAsync()
        {
            if (!CanLoadTree() || DataContext is not ViewModels.CommitDetail vm ||
                !string.Equals(_loadedRevision, vm.Commit.SHA, StringComparison.Ordinal))
                return;

            _selectedObject = (RowsList.SelectedItem as ViewModels.RevisionFileTreeNode)?.Backend;
            _selectedPath = _selectedObject?.Path ?? string.Empty;
            _selectedType = _selectedObject?.Type ?? Models.ObjectType.None;
            _previewNeedsRefresh = false;
            await vm.ViewRevisionFileAsync(_selectedObject, GetTreeRequestToken());
        }

        private async Task ToggleTreeNodeAsync(ViewModels.RevisionFileTreeNode node)
        {
            if (!CanLoadTree() || !node.IsFolder || Rows.IndexOf(node) < 0 ||
                DataContext is not ViewModels.CommitDetail vm ||
                !string.Equals(_loadedRevision, vm.Commit.SHA, StringComparison.Ordinal))
                return;

            node.IsExpanded = !node.IsExpanded;
            if (!node.IsExpanded)
            {
                await CollapseTreeNodeAsync(node);
                return;
            }

            if (!string.IsNullOrEmpty(node.Backend.SHA))
                _treeState.RememberExpanded(node.Backend.Path);
            if (!_loadingTreeNodes.Add(node))
                return;

            var token = GetTreeRequestToken();
            var repo = vm.Repository.FullPath;
            var state = _treeState;
            try
            {
                var children = node.Children.Count > 0 ? node.Children : await Task.Run(() =>
                    state.LoadChildrenAsync(repo, node.Backend, token), token);

                token.ThrowIfCancellationRequested();
                var index = Rows.IndexOf(node);
                if (children == null || index < 0)
                {
                    node.IsExpanded = false;
                    return;
                }

                if (!node.IsExpanded)
                    return;

                node.Children = children;
                var subrows = new List<ViewModels.RevisionFileTreeNode>();
                MakeRows(subrows, children, node.Depth + 1);

                _disableSelectionChangingEvent = true;
                try
                {
                    Rows.InsertRange(index + 1, subrows);
                }
                finally
                {
                    _disableSelectionChangingEvent = false;
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                node.IsExpanded = false;
            }
            finally
            {
                _loadingTreeNodes.Remove(node);
            }
        }

        private async Task CollapseTreeNodeAsync(ViewModels.RevisionFileTreeNode node)
        {
            _treeState.ForgetExpanded(node.Backend.Path);

            var index = Rows.IndexOf(node);
            var count = 0;
            while (index + count + 1 < Rows.Count && Rows[index + count + 1].Depth > node.Depth)
                count++;

            var hidesSelection = _selectedPath.StartsWith(node.Backend.Path + "/", StringComparison.Ordinal);
            _disableSelectionChangingEvent = true;
            try
            {
                Rows.RemoveRange(index + 1, count);
                if (hidesSelection)
                    RowsList.SelectedItem = node;
            }
            finally
            {
                _disableSelectionChangingEvent = false;
            }

            if (hidesSelection)
                await PreviewTreeSelectionAsync();
        }

        private async Task SetTreeSearchResultAsync(string file)
        {
            if (!CanLoadTree() || DataContext is not ViewModels.CommitDetail vm)
                return;

            if (string.IsNullOrEmpty(file))
            {
                if (_isShowingSearchResult || _isSearchPending)
                    InvalidateTreeRevision();
                return;
            }

            CancelTreeRequests();
            _isSearchPending = true;
            var token = GetTreeRequestToken();
            var repo = vm.Repository.FullPath;
            var sha = vm.Commit.SHA;
            try
            {
                var objects = await Task.Run(() => new Commands.QueryRevisionObjects(repo, sha, file) { CancellationToken = token }
                    .GetResultAsync(), token);

                token.ThrowIfCancellationRequested();
                if (objects is not { Count: 1 } || !objects[0].Path.Equals(file, StringComparison.Ordinal))
                    return;

                // Show only the matched object below its (expanded) parent folders.
                var roots = new List<ViewModels.RevisionFileTreeNode>();
                var last = roots;
                var start = 0;
                for (var separator = file.IndexOf('/'); separator >= 0; separator = file.IndexOf('/', start))
                {
                    var folder = new ViewModels.RevisionFileTreeNode
                    {
                        Backend = new Models.Object { Type = Models.ObjectType.Tree, Path = file[..separator] },
                        IsExpanded = true,
                    };
                    last.Add(folder);
                    last = folder.Children;
                    start = separator + 1;
                }

                last.Add(new ViewModels.RevisionFileTreeNode { Backend = objects[0] });
                var rows = new List<ViewModels.RevisionFileTreeNode>();
                MakeRows(rows, roots, 0);

                _isShowingSearchResult = true;
                _loadedRevision = sha;
                _selectedObject = objects[0];
                _selectedPath = file;
                _selectedType = objects[0].Type;
                _previewNeedsRefresh = false;
                ReplaceTreeRows(rows, objects[0]);
                await vm.ViewRevisionFileAsync(objects[0], token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // Search belongs to the revision at the time it was requested.
            }
            finally
            {
                if (!token.IsCancellationRequested)
                {
                    _isSearchPending = false;
                    if (!string.Equals(_loadedRevision, sha, StringComparison.Ordinal))
                        ScheduleTreeRefresh();
                }
            }
        }

        private const int TREE_REFRESH_DELAY = 200;
        private readonly List<Visual> _treeVisibilityAncestors = [];
        private readonly HashSet<ViewModels.RevisionFileTreeNode> _loadingTreeNodes = [];
        private ViewModels.RevisionFileTreeState _treeState = new();
        private ViewModels.CommitDetail _treeViewModel = null;
        private CancellationTokenSource _treeRequests = null;
        private string _treeRepository = string.Empty;
        private string _loadedRevision = string.Empty;
        private string _selectedPath = string.Empty;
        private Models.Object _selectedObject = null;
        private Models.ObjectType _selectedType = Models.ObjectType.None;
        private int _treeLoadRetryCount = 0;
        private bool _isTreeLoaded = false;
        private bool _isShowingSearchResult = false;
        private bool _isSearchPending = false;
        private bool _previewNeedsRefresh = true;
    }
}
