using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace SourceGit.Views
{
    public partial class HistoriesDetails : UserControl
    {
        public static readonly DirectProperty<HistoriesDetails, bool> IsDetailsPanelExpandedProperty =
            AvaloniaProperty.RegisterDirect<HistoriesDetails, bool>(
                nameof(IsDetailsPanelExpanded),
                static o => o.IsDetailsPanelExpanded,
                static (o, v) => o.IsDetailsPanelExpanded = v);

        public bool IsDetailsPanelExpanded
        {
            get => _isDetailsPanelExpanded;
            set => SetAndRaise(IsDetailsPanelExpandedProperty, ref _isDetailsPanelExpanded, value);
        }

        public HistoriesDetails()
        {
            InitializeComponent();
        }

        private void OnTabHeaderPointerPressed(object sender, PointerPressedEventArgs e)
        {
            if (ViewModels.Preferences.Instance.UseTwoColumnsLayoutInHistories)
                return;

            if (DataContext is not ViewModels.Histories vm)
                return;

            if (vm.IsCollapseDetails)
                vm.IsCollapseDetails = false;
        }

        private void OnOpenDetailsAsStandalone(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.Histories vm)
            {
                if (vm.DetailContext is ViewModels.CommitDetail detail)
                {
                    var standalone = new CommitDetailStandalone();
                    standalone.DataContext = detail.Clone();
                    this.ShowWindow(standalone);
                }
                else if (vm.DetailContext is ViewModels.RevisionCompare compare)
                {
                    var standalone = new RevisionCompareStandalone();
                    standalone.DataContext = compare.Clone();
                    this.ShowWindow(standalone);
                }
            }

            e.Handled = true;
        }

        private bool _isDetailsPanelExpanded = true;
    }
}
