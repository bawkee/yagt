using System;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace SourceGit.Views
{
    public partial class CommitSearchView : UserControl
    {
        public CommitSearchView()
        {
            InitializeComponent();
            AddHandler(KeyDownEvent, OnSearchPanelKeyDown, RoutingStrategies.Tunnel);
            TxtSearchCommitsBox.AddHandler(KeyDownEvent, OnSearchBoxKeyDown, RoutingStrategies.Tunnel);
            SearchSuggestionBox.AddHandler(KeyDownEvent, OnSearchPanelKeyDown, RoutingStrategies.Tunnel);
            TxtSearchCommitsBox.LostFocus += (_, _) => _searchFocusVersion++;
        }

        public void FocusSearchBox()
        {
            _searchFocusVersion++;
            TxtSearchCommitsBox.Focus(NavigationMethod.Directional);
            TxtSearchCommitsBox.SelectAll();
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == IsVisibleProperty && IsVisible)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (!IsKeyboardFocusWithin && IsEffectivelyVisible &&
                        TopLevel.GetTopLevel(this) is Window { IsActive: true })
                        FocusSearchBox();
                }, DispatcherPriority.Background);
            }
        }

        private void OnSearchPanelKeyDown(object _, KeyEventArgs e)
        {
            if (e.Key == Key.F && e.KeyModifiers == (OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control))
            {
                FocusSearchBox();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None &&
                this.FindAncestorOfType<Repository>() is { DataContext: ViewModels.Repository repo } view)
            {
                repo.Histories.IsSearchingCommits = false;
                view.FindDescendantOfType<HistoriesCommitList>()?.Focus(NavigationMethod.Directional);
                e.Handled = true;
            }
        }

        private async void OnSearchBoxKeyDown(object _, KeyEventArgs e)
        {
            if (DataContext is not ViewModels.SearchCommitContext ctx)
                return;

            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await SubmitSearchAsync();
            }
            else if (e.Key == Key.Down)
            {
                if (ctx.Suggestions is { Count: > 0 })
                {
                    SearchSuggestionBox.Focus(NavigationMethod.Tab);
                    SearchSuggestionBox.SelectedIndex = 0;
                }
                else if (ctx.Results is { Count: > 0 })
                {
                    FocusResults();
                }

                e.Handled = true;
            }
        }

        private async void OnSearchSuggestionBoxKeyDown(object _, KeyEventArgs e)
        {
            if (DataContext is not ViewModels.SearchCommitContext ctx)
                return;

            if (e.Key == Key.Enter)
            {
                var selected = SearchSuggestionBox.SelectedItem;
                if (selected is string content)
                {
                    ctx.Filter = content;
                    TxtSearchCommitsBox.CaretIndex = content.Length;
                }
                else if (selected is Models.User user)
                {
                    var apply = user.ToString().EscapeForBRE();
                    ctx.Filter = apply;
                    TxtSearchCommitsBox.CaretIndex = apply.Length;
                }

                e.Handled = true;
                await SubmitSearchAsync();
            }
        }

        private async void OnSearchSuggestionTapped(object sender, TappedEventArgs e)
        {
            if (DataContext is not ViewModels.SearchCommitContext ctx)
                return;

            var selected = (sender as Control)?.DataContext;
            if (selected is string content)
            {
                ctx.Filter = content;
                TxtSearchCommitsBox.CaretIndex = content.Length;
            }
            else if (selected is Models.User user)
            {
                var apply = user.ToString().EscapeForBRE();
                ctx.Filter = apply;
                TxtSearchCommitsBox.CaretIndex = apply.Length;
            }

            e.Handled = true;
            await SubmitSearchAsync();
        }

        private async Task SubmitSearchAsync()
        {
            if (DataContext is not ViewModels.SearchCommitContext ctx)
                return;

            var search = ctx.StartSearchAsync();
            TxtSearchCommitsBox.Focus(NavigationMethod.Directional);
            var focusVersion = ++_searchFocusVersion;

            if (await search &&
                focusVersion == _searchFocusVersion &&
                ReferenceEquals(DataContext, ctx) &&
                IsEffectivelyVisible &&
                TxtSearchCommitsBox.IsKeyboardFocusWithin &&
                TopLevel.GetTopLevel(this) is Window { IsActive: true } &&
                ctx.Results is { Count: > 0 })
            {
                SearchResultsBox.SelectedIndex = 0;
                FocusResults();
            }
        }

        private void FocusResults()
        {
            if (SearchResultsBox.SelectedIndex < 0)
                SearchResultsBox.SelectedIndex = 0;

            var selected = SearchResultsBox.SelectedItem;
            SearchResultsBox.ScrollIntoView(selected);
            SearchResultsBox.Focus(NavigationMethod.Directional);
            Dispatcher.UIThread.Post(() =>
            {
                if (SearchResultsBox.IsKeyboardFocusWithin && ReferenceEquals(SearchResultsBox.SelectedItem, selected))
                    SearchResultsBox.ContainerFromItem(selected)?.Focus(NavigationMethod.Directional);
            }, DispatcherPriority.Background);
        }

        private int _searchFocusVersion = 0;
    }
}
