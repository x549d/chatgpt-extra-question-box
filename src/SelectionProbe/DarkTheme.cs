using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ContextNotes.SelectionProbe;

internal static class DarkTheme
{
    public static void Apply(Window window)
    {
        window.FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI"); window.FontSize = 12;
        foreach (Type type in new[] { typeof(Button), typeof(TextBox), typeof(ListBox), typeof(ListBoxItem), typeof(ComboBox), typeof(ComboBoxItem), typeof(DatePicker), typeof(CheckBox) })
        {
            var style = new Style(type);
            style.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(35, 36, 40))));
            // Native picker templates keep their light selection/editor surface on Windows.
            // Their selected text must remain dark; dropdown items retain the dark theme below.
            style.Setters.Add(new Setter(Control.ForegroundProperty,
                type == typeof(ComboBox) || type == typeof(DatePicker) ? Brushes.Black : Brushes.Gainsboro));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(69, 72, 82))));
            window.Resources[type] = style;
        }
    }
}
