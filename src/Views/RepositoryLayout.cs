using Avalonia;
using Avalonia.Controls;

namespace SourceGit.Views
{
    public class RepositoryLayout : Grid
    {
        public static readonly StyledProperty<bool> UseHorizontalProperty =
            AvaloniaProperty.Register<RepositoryLayout, bool>(nameof(UseHorizontal));

        public static readonly StyledProperty<bool> UseWideDetailsProperty =
            AvaloniaProperty.Register<RepositoryLayout, bool>(nameof(UseWideDetails));

        public static readonly StyledProperty<bool> IsDashboardVisibleProperty =
            AvaloniaProperty.Register<RepositoryLayout, bool>(nameof(IsDashboardVisible), true);

        public bool UseHorizontal
        {
            get => GetValue(UseHorizontalProperty);
            set => SetValue(UseHorizontalProperty, value);
        }

        public bool UseWideDetails
        {
            get => GetValue(UseWideDetailsProperty);
            set => SetValue(UseWideDetailsProperty, value);
        }

        public bool IsDashboardVisible
        {
            get => GetValue(IsDashboardVisibleProperty);
            set => SetValue(IsDashboardVisibleProperty, value);
        }

        public bool IsWideDetailsActive => IsDashboardVisible && UseWideDetails && !UseHorizontal;

        protected override System.Type StyleKeyOverride => typeof(Grid);

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            RefreshLayout();
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (IsLoaded && (change.Property == UseHorizontalProperty ||
                change.Property == UseWideDetailsProperty ||
                change.Property == IsDashboardVisibleProperty))
                RefreshLayout();
        }

        private void RefreshLayout()
        {
            // Children stay attached: sidebar, sidebar splitter, pages, details splitter, details.
            // Only their grid positions change, preserving bindings, focus, and selection.
            if (Children.Count < 5)
                return;

            var horizontal = IsDashboardVisible && UseHorizontal;
            var wide = IsWideDetailsActive;
            var pageRows = IsDashboardVisible && !horizontal ? 1 : 3;

            // Only Horizontal has two independent content columns. Reserving both
            // minimum widths in the stacked layouts can push details out of a narrow window.
            ColumnDefinitions[4].MinWidth = horizontal ? 100 : 0;

            SetArea(Children[0], 0, 0, wide ? 1 : 3, 1);
            SetArea(Children[1], 0, 1, wide ? 1 : 3, 1);
            SetArea(Children[2], 0, 2, pageRows, horizontal ? 1 : 3);

            if (horizontal)
            {
                SetArea(Children[3], 0, 3, 3, 1);
                SetArea(Children[4], 0, 4, 3, 1);
                ((GridSplitter)Children[3]).BorderThickness = new Thickness(1, 0, 0, 0);
            }
            else
            {
                SetArea(Children[3], 1, wide ? 0 : 2, 1, wide ? 5 : 3);
                SetArea(Children[4], 2, wide ? 0 : 2, 1, wide ? 5 : 3);
                ((GridSplitter)Children[3]).BorderThickness = new Thickness(0, 1, 0, 0);
            }
        }

        private static void SetArea(Control control, int row, int column, int rowSpan, int columnSpan)
        {
            SetRow(control, row);
            SetColumn(control, column);
            SetRowSpan(control, rowSpan);
            SetColumnSpan(control, columnSpan);
        }
    }
}
