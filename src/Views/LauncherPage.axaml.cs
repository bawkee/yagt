using System;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;

namespace SourceGit.Views
{
    public partial class LauncherPage : UserControl
    {
        public LauncherPage()
        {
            InitializeComponent();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (e.Handled || e.Key != Key.F ||
                e.KeyModifiers != (OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control) ||
                e.Source is TextBox ||
                DataContext is not ViewModels.LauncherPage { Popup: null, Data: ViewModels.Repository repo } ||
                this.FindAncestorOfType<Launcher>()?.DataContext is not ViewModels.Launcher { CommandPalette: null })
                return;

            if (repo.IsDashboardVisible)
            {
                repo.Histories.IsSearchingCommits = true;
                this.FindDescendantOfType<CommitSearchView>()?.FocusSearchBox();
            }
            else if (repo.IsWorkingCopyVisible)
            {
                this.FindDescendantOfType<WorkingCopy>()?.FocusSearchBox();
            }
            else if (repo.IsStashesVisible)
            {
                this.FindDescendantOfType<StashesPage>()?.FocusSearchBox();
            }

            e.Handled = true;
        }

        private async void OnPopupSureByHotKey(object sender, RoutedEventArgs e)
        {
            var children = PopupPanel.GetLogicalDescendants();
            foreach (var child in children)
            {
                if (child is Control { IsKeyboardFocusWithin: true, Tag: StealHotKey steal } control &&
                    steal is { Key: Key.Enter, KeyModifiers: KeyModifiers.None })
                {
                    var fake = new KeyEventArgs()
                    {
                        RoutedEvent = KeyDownEvent,
                        Route = RoutingStrategies.Direct,
                        Source = control,
                        Key = Key.Enter,
                        KeyModifiers = KeyModifiers.None,
                        PhysicalKey = PhysicalKey.Enter,
                    };

                    control.RaiseEvent(fake);
                    e.Handled = false;
                    return;
                }
            }

            if (DataContext is ViewModels.LauncherPage page)
                await page.ProcessPopupAsync();

            e.Handled = true;
        }

        private async void OnPopupSure(object _, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.LauncherPage page)
                await page.ProcessPopupAsync();

            e.Handled = true;
        }

        private void OnPopupCancel(object _, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.LauncherPage page)
                page.CancelPopup();

            e.Handled = true;
        }

        private void OnPopupTerminate(object _, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.LauncherPage page)
                page.TerminatePopup();

            e.Handled = true;
        }

        private void OnMaskClicked(object sender, PointerPressedEventArgs e)
        {
            OnPopupCancel(sender, e);
        }

        private void OnDismissNotification(object sender, RoutedEventArgs e)
        {
            if (sender is Button { DataContext: Models.Notification notice } &&
                DataContext is ViewModels.LauncherPage page)
                page.Notifications.Remove(notice);

            e.Handled = true;
        }

        private void OnToolBarPointerPressed(object sender, PointerPressedEventArgs e)
        {
            this.FindAncestorOfType<ChromelessWindow>()?.BeginMoveWindow(sender, e);
        }
    }
}
