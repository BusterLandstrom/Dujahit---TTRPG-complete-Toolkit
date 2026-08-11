using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using Dujahit.Models;
using Dujahit.ViewModels;
using System;

namespace Dujahit
{
    public class ViewLocator : IDataTemplate
    {

        public Control? Build(object? data)
        {
            if (data is null)
                return null;

            var name = data.GetType().FullName!.Replace("ViewModel", "View", StringComparison.Ordinal);
            var type = Type.GetType(name);

            if (type != null)
            {
                try
                {
                    var control = (Control)Activator.CreateInstance(type)!;
                    control.DataContext = data;
                    return control;
                }
                catch (Exception ex)
                {
                    ErrorLog.Log("[ViewLocator] " + name + " would not build", ex);
                    return new TextBlock
                    {
                        Text = name + " failed to open, the reason is in the log under AppData logs.",
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(24)
                    };
                }
            }

            return new TextBlock { Text = "Not Found: " + name };
        }

        public bool Match(object? data)
        {
            return data is ViewModelBase;
        }
    }
}
