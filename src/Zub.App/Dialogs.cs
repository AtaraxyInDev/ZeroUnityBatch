using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using System;
using System.Threading.Tasks;

namespace Zub.App
{
    /// <summary>Diálogos propios (Avalonia no trae MessageBox).</summary>
    public static class Dialogs
    {
        public static async Task<bool> ConfirmAsync(Window owner, string title, string message,
            string accept = "Continuar", string cancel = "Cancelar")
        {
            return await ShowAsync(owner, title, message, accept, cancel);
        }

        public static async Task InfoAsync(Window owner, string title, string message)
        {
            await ShowAsync(owner, title, message, "Entendido", null);
        }

        private static async Task<bool> ShowAsync(Window owner, string title, string message,
            string accept, string cancel)
        {
            var win = new Window
            {
                Title = title,
                Width = 520,
                SizeToContent = SizeToContent.Height,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = new SolidColorBrush(Color.Parse("#1E1E23"))
            };

            var text = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.Parse("#E9E9F0")),
                FontSize = 13
            };

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 18, 0, 0)
            };

            var ok = new Button
            {
                Content = accept,
                Classes = { "accent" },
                MinWidth = 110
            };
            ok.Click += (s, e) => win.Close(true);

            if (cancel != null)
            {
                var no = new Button { Content = cancel, MinWidth = 110 };
                no.Click += (s, e) => win.Close(false);
                buttons.Children.Add(no);
            }
            buttons.Children.Add(ok);

            var root = new StackPanel { Margin = new Thickness(22), Spacing = 0 };
            root.Children.Add(text);
            root.Children.Add(buttons);
            win.Content = root;

            return await win.ShowDialog<bool>(owner);
        }
    }
}
