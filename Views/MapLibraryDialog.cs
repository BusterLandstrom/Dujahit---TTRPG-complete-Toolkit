using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Dujahit.Models;
using Dujahit.ViewModels;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Dujahit.Views
{
    public class MapLibraryDialog : DialogWindow
    {
        private readonly MapCanvasViewModel _canvas;

        public MapLibraryDialog(MapCanvasViewModel canvas, MapHubViewModel? hub)
        {
            _canvas = canvas;
            Title = "Library";
            ContentWidthCap = 880;
            MinWidth = 520;

            var tabs = new TabControl { Padding = new Avalonia.Thickness(0, 12, 0, 0) };
            tabs.Items.Add(Tab("Objects", ObjectsTab()));
            tabs.Items.Add(Tab("Tokens", TokensTab()));
            if (hub != null) tabs.Items.Add(Tab("Maps", MapsTab(hub)));

            Mount("Library", tabs);
        }

        private static TabItem Tab(string header, Control body) => new()
        {
            Header = header,
            Content = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MaxHeight = 460,
                Content = body
            }
        };

        private static TextBlock Hint(string text) => new()
        {
            Text = text,
            Classes = { "muted" },
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Avalonia.Thickness(0, 0, 0, 10)
        };

        private static TextBlock Empty(string text) => new()
        {
            Text = text,
            Classes = { "muted" },
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        };

        private static ItemsControl Grid(IEnumerable source, IDataTemplate template) => new()
        {
            ItemsSource = source,
            ItemsPanel = new FuncTemplate<Panel?>(() => new WrapPanel()),
            ItemTemplate = template
        };

        private StackPanel DropPanel(Func<IEnumerable<IStorageItem>, Task<int>> import)
        {
            var panel = new StackPanel { Spacing = 4, Background = Brushes.Transparent };
            DragDrop.SetAllowDrop(panel, true);

            panel.AddHandler(DragDrop.DragOverEvent, (_, e) =>
            {
                e.DragEffects = _canvas.IsHost && e.Data.Contains(DataFormats.Files) ? DragDropEffects.Copy : DragDropEffects.None;
                e.Handled = true;
            });

            panel.AddHandler(DragDrop.DropEvent, async (_, e) =>
            {
                try
                {
                    e.Handled = true;
                    if (!_canvas.IsHost) return;
                    var dropped = e.Data.GetFiles();
                    if (dropped == null) return;
                    if (await import(dropped) > 0) Close();
                }
                catch (Exception ex) { ErrorLog.Log("[Library] dropped files failed", ex); }
            });

            return panel;
        }

        private async Task PickFolderAsync(Func<IEnumerable<IStorageItem>, Task<int>> import)
        {
            try
            {
                var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Pick a folder of images" });
                if (folders.Count == 0) return;
                if (await import(folders) > 0) Close();
            }
            catch (Exception ex) { ErrorLog.Log("[Library] folder pick failed", ex); }
        }

        private Button AddButton(string text, Action onClick)
        {
            var b = new Button { Content = text, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
            b.Classes.Add("choose");
            b.Click += (_, _) => onClick();
            return b;
        }

        private Control ObjectsTab()
        {
            Func<IEnumerable<IStorageItem>, Task<int>> import = files => _canvas.ImportPropFilesAsync(files);

            var panel = DropPanel(import);
            panel.Children.Add(Hint("Pick one and the map arms it, then click the map to drop it. The cross forgets it for good. Images dropped in here are kept as objects."));
            panel.Children.Add(new TextBox { Watermark = "Name it before you pick", [!TextBox.TextProperty] = new Binding(nameof(MapCanvasViewModel.NewPropName)) { Source = _canvas, Mode = BindingMode.TwoWay } });
            panel.Children.Add(AddButton("Upload a map object", () => { Close(); _canvas.UploadPropCommand.Execute().Subscribe(); }));
            panel.Children.Add(AddButton("Take a whole folder", () => _ = PickFolderAsync(import)));
            if (_canvas.PropLibrary.Count == 0)
                panel.Children.Add(Empty("Nothing kept yet. Name one and upload it here, or pick an image from the map objects tool."));
            else
                panel.Children.Add(Grid(_canvas.PropLibrary, SlotTemplate(entry =>
                {
                    _ = _canvas.SelectPropEntry(entry);
                    Close();
                })));
            return panel;
        }

        private Control TokensTab()
        {
            Func<IEnumerable<IStorageItem>, Task<int>> import = files => _canvas.ImportTokenFilesAsync(files);

            var panel = DropPanel(import);
            panel.Children.Add(Hint("Pick one and the map arms it as the token you are placing. Bind a monster on the tool panel to give it a statblock. Drop images on this tab or straight onto the map and they land in here."));
            panel.Children.Add(AddButton("Upload a token", () => { Close(); _canvas.UploadTokenCommand.Execute().Subscribe(); }));
            panel.Children.Add(AddButton("Take a whole folder", () => _ = PickFolderAsync(import)));
            if (_canvas.Library.Count == 0)
                panel.Children.Add(Empty("No tokens yet. Upload an image or make a colour token from the tokens tool."));
            else
                panel.Children.Add(Grid(_canvas.Library, SlotTemplate(entry =>
                {
                    _canvas.SelectLibraryEntry(entry);
                    Close();
                })));
            return panel;
        }

        private Control MapsTab(MapHubViewModel hub)
        {
            var panel = new StackPanel { Spacing = 4 };
            panel.Children.Add(Hint("Opening a map here swaps what everyone is looking at, the same as the maps screen does."));
            if (hub.Maps.Count == 0)
                panel.Children.Add(Empty("No maps in this campaign yet."));
            else
                panel.Children.Add(Grid(hub.Maps, new FuncDataTemplate<MapSummaryViewModel>((map, _) =>
                {
                    if (map == null) return null;
                    var stack = new StackPanel { Spacing = 4, Width = 148, Margin = new Avalonia.Thickness(0, 0, 10, 10) };
                    var button = new Button
                    {
                        Classes = { "tokenslot" },
                        Width = 148,
                        Height = 96,
                        Content = map.Thumbnail == null
                            ? new TextBlock { Text = map.GridLabel, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
                            : new Image { Source = map.Thumbnail, Stretch = Stretch.UniformToFill }
                    };
                    AutomationProperties.SetName(button, map.Name);
                    button.Click += (_, _) =>
                    {
                        hub.ActivateMapCommand.Execute(map).Subscribe();
                        Close();
                    };
                    stack.Children.Add(button);
                    stack.Children.Add(new TextBlock
                    {
                        Text = map.Name,
                        FontSize = 12,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        HorizontalAlignment = HorizontalAlignment.Center
                    });
                    return stack;
                }, true)));
            return panel;
        }

        private static FuncDataTemplate<TokenLibraryEntryViewModel> SlotTemplate(Action<TokenLibraryEntryViewModel> onPick) =>
            new((entry, _) =>
            {
                if (entry == null) return null;
                var stack = new StackPanel { Spacing = 4, Width = 76, Margin = new Avalonia.Thickness(0, 0, 10, 10) };

                var host = new Panel();
                var button = new Button
                {
                    Classes = { "tokenslot" },
                    Width = 76,
                    Height = 76,
                    Content = new Image { Source = entry.Preview, Width = 62, Height = 62, Stretch = Stretch.Uniform }
                };
                AutomationProperties.SetName(button, entry.Name);
                button.Click += (_, _) => onPick(entry);
                host.Children.Add(button);

                var remove = new Button
                {
                    Classes = { "tokendel" },
                    Content = "x",
                    VerticalAlignment = VerticalAlignment.Top,
                    HorizontalAlignment = HorizontalAlignment.Right
                };
                AutomationProperties.SetName(remove, "Remove " + entry.Name);
                remove.Click += (_, _) => entry.RemoveCommand.Execute().Subscribe();
                host.Children.Add(remove);

                stack.Children.Add(host);
                stack.Children.Add(new TextBlock
                {
                    Text = entry.Name,
                    FontSize = 11,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    HorizontalAlignment = HorizontalAlignment.Center
                });
                return stack;
            }, true);
    }
}
