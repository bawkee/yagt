using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SourceGit.ViewModels
{
    /// <summary>
    ///     Remembers expanded folders across revisions and caches folder listings by tree SHA.
    ///     Use one instance per repository.
    /// </summary>
    public class RevisionFileTreeState
    {
        public void RememberExpanded(string path)
        {
            _expandedPaths.RemoveAll(x => x.Equals(path, StringComparison.Ordinal));
            _expandedPaths.Insert(0, path);
            if (_expandedPaths.Count > MAX_EXPANDED_FOLDERS)
                _expandedPaths.RemoveAt(MAX_EXPANDED_FOLDERS);
        }

        public void ForgetExpanded(string path)
        {
            var prefix = path + "/";
            _expandedPaths.RemoveAll(x => x.Equals(path, StringComparison.Ordinal) || x.StartsWith(prefix, StringComparison.Ordinal));
        }

        public IReadOnlyCollection<string> GetExpandedPaths(string selectedPath)
        {
            var paths = new HashSet<string>(StringComparer.Ordinal);
            var parentEnd = selectedPath?.LastIndexOf('/') ?? -1;
            if (parentEnd > 0)
                AddAncestors(paths, selectedPath[..parentEnd]);

            foreach (var path in _expandedPaths)
                AddAncestors(paths, path);

            return paths;
        }

        public async Task<List<RevisionFileTreeNode>> LoadAsync(string repo, string revision, IReadOnlyCollection<string> expandedPaths, CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var objects = await new Commands.QueryRevisionObjects(repo, revision, null) { CancellationToken = cancellationToken }
                    .GetResultAsync().ConfigureAwait(false);
                if (objects == null)
                    return null;

                var expanded = new HashSet<string>(expandedPaths, StringComparer.Ordinal);
                var nodes = CreateNodes(objects, string.Empty);
                var missing = new List<string>();
                RestoreCached(nodes, expanded, missing, cancellationToken);
                if (missing.Count == 0)
                    return nodes;

                var paths = new List<string>();
                foreach (var path in expanded)
                {
                    foreach (var root in missing)
                    {
                        if (path.Equals(root, StringComparison.Ordinal) || path.StartsWith(root + "/", StringComparison.Ordinal))
                        {
                            paths.Add(path + "/");
                            break;
                        }
                    }
                }

                var loaded = await new Commands.QueryRevisionObjects(repo, revision, paths, true) { CancellationToken = cancellationToken }
                    .GetResultAsync().ConfigureAwait(false);
                if (loaded == null)
                    return null;

                var children = new Dictionary<string, List<Models.Object>>(StringComparer.Ordinal);
                foreach (var obj in loaded)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var separator = obj.Path.LastIndexOf('/');
                    var parent = separator < 0 ? string.Empty : obj.Path[..separator];
                    if (!children.TryGetValue(parent, out var entries))
                    {
                        entries = [];
                        children.Add(parent, entries);
                    }

                    entries.Add(new Models.Object { SHA = obj.SHA, Type = obj.Type, Path = obj.Path[(separator + 1)..] });
                }

                RestoreQueried(nodes, expanded, children, cancellationToken);
                return nodes;
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task<List<RevisionFileTreeNode>> LoadChildrenAsync(string repo, Models.Object folder, CancellationToken cancellationToken)
        {
            if (folder?.Type != Models.ObjectType.Tree || string.IsNullOrEmpty(folder.SHA))
                return null;

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!TryGetCached(folder.SHA, out var objects))
                {
                    objects = await new Commands.QueryRevisionObjects(repo, folder.SHA, null) { CancellationToken = cancellationToken }
                        .GetResultAsync().ConfigureAwait(false);
                    if (objects == null)
                        return null;

                    Cache(folder.SHA, objects);
                }

                cancellationToken.ThrowIfCancellationRequested();
                return CreateNodes(objects, folder.Path);
            }
            finally
            {
                _gate.Release();
            }
        }

        private static void AddAncestors(HashSet<string> paths, string path)
        {
            var start = 0;
            while (paths.Count < MAX_EXPANDED_FOLDERS)
            {
                var separator = path.IndexOf('/', start);
                paths.Add(separator < 0 ? path : path[..separator]);
                if (separator < 0)
                    break;

                start = separator + 1;
            }
        }

        private static List<RevisionFileTreeNode> CreateNodes(List<Models.Object> objects, string parent)
        {
            var prefix = string.IsNullOrEmpty(parent) ? string.Empty : parent + "/";
            var nodes = new List<RevisionFileTreeNode>(objects.Count);
            foreach (var obj in objects)
            {
                nodes.Add(new RevisionFileTreeNode
                {
                    Backend = new Models.Object { SHA = obj.SHA, Type = obj.Type, Path = prefix + obj.Path },
                });
            }

            nodes.Sort((l, r) => l.IsFolder == r.IsFolder ? Models.NumericSort.Compare(l.Name, r.Name) : l.IsFolder ? -1 : 1);
            return nodes;
        }

        private void RestoreCached(List<RevisionFileTreeNode> nodes, HashSet<string> expanded, List<string> missing, CancellationToken cancellationToken)
        {
            foreach (var node in nodes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!node.IsFolder || !expanded.Contains(node.Backend.Path))
                    continue;

                if (TryGetCached(node.Backend.SHA, out var objects))
                {
                    node.Children = CreateNodes(objects, node.Backend.Path);
                    node.IsExpanded = true;
                    RestoreCached(node.Children, expanded, missing, cancellationToken);
                }
                else
                {
                    missing.Add(node.Backend.Path);
                }
            }
        }

        private void RestoreQueried(List<RevisionFileTreeNode> nodes, HashSet<string> expanded, Dictionary<string, List<Models.Object>> children, CancellationToken cancellationToken)
        {
            foreach (var node in nodes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!node.IsFolder || !expanded.Contains(node.Backend.Path))
                    continue;

                if (!node.IsExpanded)
                {
                    if (!children.TryGetValue(node.Backend.Path, out var objects))
                        objects = [];

                    Cache(node.Backend.SHA, objects);
                    node.Children = CreateNodes(objects, node.Backend.Path);
                    node.IsExpanded = true;
                }

                RestoreQueried(node.Children, expanded, children, cancellationToken);
            }
        }

        private bool TryGetCached(string sha, out List<Models.Object> objects)
        {
            if (_cache.TryGetValue(sha, out var node))
            {
                _cacheOrder.Remove(node);
                _cacheOrder.AddFirst(node);
                objects = node.Value.Objects;
                return true;
            }

            objects = null;
            return false;
        }

        private void Cache(string sha, List<Models.Object> objects)
        {
            if (_cache.ContainsKey(sha))
                return;

            var size = 64L;
            foreach (var obj in objects)
                size += 128L + (obj.Path.Length + obj.SHA.Length) * 2L;

            if (size > MAX_CACHE_BYTES)
                return;

            while (_cache.Count >= MAX_CACHED_TREES || _cacheSize + size > MAX_CACHE_BYTES)
            {
                var oldest = _cacheOrder.Last;
                _cache.Remove(oldest.Value.SHA);
                _cacheSize -= oldest.Value.Size;
                _cacheOrder.RemoveLast();
            }

            var entry = new CachedTree { SHA = sha, Objects = objects, Size = size };
            _cache.Add(sha, _cacheOrder.AddFirst(entry));
            _cacheSize += size;
        }

        private sealed class CachedTree
        {
            public string SHA { get; init; }
            public List<Models.Object> Objects { get; init; }
            public long Size { get; init; }
        }

        private const int MAX_EXPANDED_FOLDERS = 32;
        private const int MAX_CACHED_TREES = 256;
        private const int MAX_CACHE_BYTES = 4 * 1024 * 1024;
        private readonly List<string> _expandedPaths = [];
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly Dictionary<string, LinkedListNode<CachedTree>> _cache = new(StringComparer.Ordinal);
        private readonly LinkedList<CachedTree> _cacheOrder = new();
        private long _cacheSize = 0;
    }
}
