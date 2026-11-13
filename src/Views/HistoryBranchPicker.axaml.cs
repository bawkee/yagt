using System;
using System.Collections.Generic;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace SourceGit.Views
{
    public partial class HistoryBranchPicker : UserControl
    {
        public static readonly DirectProperty<HistoryBranchPicker, List<Models.Branch>> BranchesProperty =
            AvaloniaProperty.RegisterDirect<HistoryBranchPicker, List<Models.Branch>>(
                nameof(Branches),
                static o => o.Branches,
                static (o, v) => o.Branches = v);

        public List<Models.Branch> Branches
        {
            get => _branches;
            set => SetAndRaise(BranchesProperty, ref _branches, value);
        }

        public static readonly DirectProperty<HistoryBranchPicker, List<Models.Branch>> VisibleBranchesProperty =
            AvaloniaProperty.RegisterDirect<HistoryBranchPicker, List<Models.Branch>>(
                nameof(VisibleBranches),
                static o => o.VisibleBranches);

        public List<Models.Branch> VisibleBranches
        {
            get => _visibleBranches;
            private set => SetAndRaise(VisibleBranchesProperty, ref _visibleBranches, value);
        }

        public event Action<Models.Branch> BranchSelected;

        public HistoryBranchPicker()
        {
            InitializeComponent();
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == BranchesProperty)
                UpdateVisibleBranches();
        }

        private void OnPickerLoaded(object sender, RoutedEventArgs e)
        {
            FilterBox.Focus();
        }

        private void OnFilterChanged(object sender, TextChangedEventArgs e)
        {
            UpdateVisibleBranches();
        }

        private void UpdateVisibleBranches()
        {
            var filter = FilterBox?.Text ?? string.Empty;
            var visible = new List<Models.Branch>();
            foreach (var branch in _branches)
            {
                if (branch.FriendlyName.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    visible.Add(branch);
            }

            VisibleBranches = visible;
            if (BranchesList != null)
                BranchesList.SelectedIndex = visible.Count > 0 ? 0 : -1;
        }

        private void OnFilterKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Down && VisibleBranches.Count > 0)
            {
                BranchesList.Focus();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                SelectBranch();
                e.Handled = true;
            }
        }

        private void OnBranchesKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                SelectBranch();
                e.Handled = true;
            }
        }

        private void OnBranchPointerReleased(object sender, PointerReleasedEventArgs e)
        {
            if (e.InitialPressMouseButton == MouseButton.Left && sender is Control { DataContext: Models.Branch branch })
            {
                BranchSelected?.Invoke(branch);
                e.Handled = true;
            }
        }

        private void SelectBranch()
        {
            if (BranchesList.SelectedItem is Models.Branch branch)
                BranchSelected?.Invoke(branch);
        }

        private List<Models.Branch> _branches = [];
        private List<Models.Branch> _visibleBranches = [];
    }
}
