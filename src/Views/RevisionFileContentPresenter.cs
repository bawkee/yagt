using System;

using Avalonia.Controls;
using Avalonia.Layout;

namespace SourceGit.Views
{
    internal sealed class RevisionFileContentPresenter : Grid
    {
        public RevisionFileContentPresenter()
        {
            Children.Add(_text);
            Children.Add(_other);
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);

            if (DataContext is Models.RevisionTextFile file)
            {
                _other.IsVisible = false;
                _other.Content = null;
                _text.Content = file;
                _text.IsVisible = true;
            }
            else
            {
                _text.IsVisible = false;
                if (_text.Content is Models.RevisionTextFile previous && !string.IsNullOrEmpty(previous.Content))
                {
                    // Keep the editor and grammar, but release the previous document and its undo history.
                    _text.Content = new Models.RevisionTextFile { FileName = previous.FileName, Content = string.Empty };
                }

                _other.Content = DataContext;
                _other.IsVisible = true;
            }
        }

        private readonly ContentControl _text = new()
        {
            IsVisible = false,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
        };

        private readonly ContentControl _other = new()
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
        };
    }
}
