using JingTingYue.Models;
using JingTingYue.Services;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.UI;
using Windows.Storage.Pickers;

namespace JingTingYue.Views;

public sealed partial class BookshelfPage : Page
{
    private static readonly string[][] CoverPalettes =
    {
        new[] { "#334E5C", "#3B5D50", "#774A51", "#775C3D", "#375C63", "#5B4D67", "#605C45", "#694E43" },
        new[] { "#465563", "#4B5E50", "#805451", "#846744", "#486467", "#665569", "#666047", "#76554B" },
        new[] { "#344759", "#365549", "#69474D", "#705638", "#36565E", "#53485F", "#54533F", "#62483E" },
    };

    private static int StableIndex(string value)
    {
        uint hash = 2166136261;
        foreach (char ch in value.ToUpperInvariant())
            hash = (hash ^ ch) * 16777619;
        return (int)(hash % CoverPalettes[0].Length);
    }

    private static Dictionary<string, int> CoverIndexes()
    {
        var result = new Dictionary<string, int>();
        var used = new HashSet<int>();
        int Reserve(int first)
        {
            int index = first;
            while (used.Count < CoverPalettes[0].Length && used.Contains(index))
                index = (index + 1) % CoverPalettes[0].Length;
            used.Add(index);
            return index;
        }
        var groups = BookStore.Books.Where(b => !string.IsNullOrWhiteSpace(b.Category))
            .GroupBy(b => b.Category).OrderBy(g => g.Key, StringComparer.Ordinal);
        foreach (var group in groups)
        {
            int index = Reserve(StableIndex(group.Key));
            foreach (var book in group) result[book.Id] = index;
        }
        foreach (var book in BookStore.Books.Where(b => string.IsNullOrWhiteSpace(b.Category)).OrderBy(b => b.Id, StringComparer.Ordinal))
            result[book.Id] = Reserve(StableIndex(book.Title + "|" + book.Author));
        return result;
    }

    private static SolidColorBrush CoverBrush(int index)
    {
        int palette = AppTheme.Current switch { "sepia" => 1, "night" => 2, _ => 0 };
        var hex = CoverPalettes[palette][index];
        return new SolidColorBrush(Color.FromArgb(255,
            Convert.ToByte(hex.Substring(1, 2), 16),
            Convert.ToByte(hex.Substring(3, 2), 16),
            Convert.ToByte(hex.Substring(5, 2), 16)));
    }

    public BookshelfPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        BuildBooks();
        BuildContinue();
        UpdateThemeButtons();
    }

    private string? _filter; // null=全部, ""=未分类, 其他=分类名

    private void BuildContinue()
    {
        var last = BookStore.LastRead;
        if (last == null) { ContinueCard.Visibility = Visibility.Collapsed; return; }
        ContinueCard.Visibility = Visibility.Visible;
        ContinueTitleText.Text = last.Title;
        ContinueProgressText.Text = $"{last.Progress}%";
        ContinueCover.Background = CoverBrush(CoverIndexes()[last.Id]);
    }

    private List<StoredBook> Filtered()
    {
        var all = BookStore.Books;
        if (_filter == null) return all;
        if (_filter == "") return all.Where(b => b.Category == "").ToList();
        return all.Where(b => b.Category == _filter).ToList();
    }

    private void BuildBooks()
    {
        BookGrid.Items.Clear();
        var coverIndexes = CoverIndexes();
        foreach (var b in Filtered())
            BookGrid.Items.Add(MakeBookCard(b, coverIndexes[b.Id]));
        EmptyState.Visibility = BookGrid.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var realCount = BookStore.Books.Count;
        BookCountText.Text = _filter == null
            ? $"{realCount} 份收藏"
            : $"分类 · {_filter ?? "全部"}（{Filtered().Count} 本）";
        BuildNav();
    }

    private void BuildNav()
    {
        NavPanel.Children.Clear();
        void AddItem(string label, string? key, int count, string glyph)
        {
            bool selected = _filter == key;
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.Children.Add(new TextBlock { Text = label,
                Foreground = selected ? (Brush)Application.Current.Resources["BookInkBrush"] : (Brush)Application.Current.Resources["MutedInkBrush"],
                FontFamily = new FontFamily("Microsoft YaHei UI"), FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
            var countText = new TextBlock { Text = count.ToString(),
                Foreground = (Brush)Application.Current.Resources["MutedInkBrush"], FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(countText, 1);
            g.Children.Add(countText);

            var item = new Border
            {
                Background = selected ? (Brush)Application.Current.Resources["SelectedSageBrush"] : new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12, 9, 12, 9),
                Margin = new Thickness(0, 0, 0, 4),
                Child = g,
            };
            item.PointerPressed += (s, e) => { _filter = key; BuildBooks(); };
            NavPanel.Children.Add(item);
        }

        var all = BookStore.Books;
        AddItem("全部内容", null, all.Count, "&#xE8B7;");
        AddItem("未分类", "", all.Count(b => b.Category == ""), "&#xE7B8;");

        var header = new TextBlock { Text = "分类", FontFamily = new FontFamily("Microsoft YaHei UI"),
            FontSize = 12, Foreground = (Brush)Application.Current.Resources["MutedInkBrush"],
            Margin = new Thickness(8, 18, 0, 8) };
        NavPanel.Children.Add(header);
        foreach (var c in BookStore.Categories)
            AddItem(c, c, all.Count(b => b.Category == c), "&#xE8B7;");
    }

    private FrameworkElement MakeBookCard(StoredBook b, int coverIndex)
    {
        var coverBrush = CoverBrush(coverIndex);

        var cover = new Border
        {
            Width = 206,
            Height = 278,
            CornerRadius = new CornerRadius(5, 12, 12, 5),
            Background = coverBrush,
            Padding = new Thickness(0),
            Clip = new Microsoft.UI.Xaml.Media.RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, 206, 278) },
        };

        // 有封面图就显示图片，否则显示纯色+文字
        var coverPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "JingTingYue", "epubs", b.CoverFile);
        if (!string.IsNullOrEmpty(b.CoverFile) && File.Exists(coverPath))
        {
            var img = new Image
            {
                Source = new BitmapImage(new Uri(coverPath)),
                Stretch = Stretch.UniformToFill,
                Width = 206,
                Height = 278,
            };
            cover.Child = img;
        }
        else
        {
            cover.Padding = new Thickness(22, 21, 22, 21);
            var coverPanel = new Grid();
            coverPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            coverPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            coverPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var edition = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(b.Category) ? "静 听 阅  ·  藏 书" : b.Category,
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                FontFamily = new FontFamily("Microsoft YaHei UI"),
                FontSize = 10,
                CharacterSpacing = 60,
            };
            coverPanel.Children.Add(edition);
            var coverTitle = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Text = b.Title,
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                FontFamily = new FontFamily("Microsoft YaHei UI"),
                FontSize = 21,
                FontWeight = FontWeights.SemiBold,
                LineHeight = 32,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxHeight = 166,
            };
            Grid.SetRow(coverTitle, 1);
            coverPanel.Children.Add(coverTitle);
            var coverAuthor = new TextBlock
            {
                Text = b.Author,
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                FontFamily = new FontFamily("Microsoft YaHei UI"),
                FontSize = 12,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            Grid.SetRow(coverAuthor, 2);
            coverPanel.Children.Add(coverAuthor);
            cover.Child = coverPanel;
        }

        var coverBtn = new Button
        {
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            Content = cover,
        };
        var captured = b;
        coverBtn.Click += (s, e) => OpenBook(captured);
        coverBtn.ContextFlyout = BuildBookFlyout(captured);

        var info = new StackPanel { Width = 206, Margin = new Thickness(0, 16, 0, 0), Spacing = 5 };
        info.Children.Add(new TextBlock
        {
            Text = b.Title,
            Foreground = (Brush)Application.Current.Resources["BookInkBrush"],
            FontFamily = new FontFamily("Microsoft YaHei UI"),
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        info.Children.Add(new TextBlock
        {
            Text = $"EPUB · {b.Author}",
            Foreground = (Brush)Application.Current.Resources["MutedInkBrush"],
            FontFamily = new FontFamily("Microsoft YaHei UI"),
            FontSize = 11,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 6, 0, 0) };
        row.Children.Add(new TextBlock { Text = "在读", Foreground = (Brush)Application.Current.Resources["MutedInkBrush"], FontSize = 11 });
        row.Children.Add(new TextBlock { Text = $"{b.Progress}%", Foreground = (Brush)Application.Current.Resources["MutedInkBrush"], FontSize = 11 });
        info.Children.Add(row);
        var bar = new ProgressBar { Height = 3, Maximum = 100, Value = b.Progress,
            Foreground = (Brush)Application.Current.Resources["ForestGreenBrush"],
            Background = (Brush)Application.Current.Resources["HairlineBrush"],
            BorderThickness = new Thickness(0), Margin = new Thickness(0, 4, 0, 0) };
        info.Children.Add(bar);

        var card = new StackPanel { Spacing = 0 };
        card.Children.Add(coverBtn);
        card.Children.Add(info);
        return card;
    }

    private void OpenBook(StoredBook b)
    {
        Frame.Navigate(typeof(ReaderPage), new ReaderRequest { Book = b, Title = b.Title, Author = b.Author });
    }

    private MenuFlyout BuildBookFlyout(StoredBook b)
    {
        var flyout = new MenuFlyout();

        var moveItem = new MenuFlyoutSubItem { Text = "移动至分类" };
        moveItem.Items.Add(new MenuFlyoutItem { Text = "未分类", Tag = (b, "") });
        foreach (var c in BookStore.Categories)
            moveItem.Items.Add(new MenuFlyoutItem { Text = c, Tag = (b, c) });
        moveItem.Items.Add(new MenuFlyoutItem { Text = "新建分类…", Tag = (b, "__new__") });
        foreach (var item in moveItem.Items)
        {
            var mi = (MenuFlyoutItem)item;
            mi.Click += async (s, e) =>
            {
                var (book, cat) = ((StoredBook, string))((MenuFlyoutItem)s!).Tag!;
                if (cat == "__new__") cat = await AskNewCategoryAsync();
                if (cat == null) return; // 用户取消
                await BookStore.MoveToCategoryAsync(book.Id, cat);
                BuildBooks();
                BuildContinue();
            };
        }
        flyout.Items.Add(moveItem);

        var del = new MenuFlyoutItem { Text = "删除", Tag = b };
        del.Click += async (s, e) =>
        {
            var book = (StoredBook)((MenuFlyoutItem)s!).Tag!;
            var dlg = new ContentDialog
            {
                Title = "删除这本书？",
                Content = $"将从书架移除《{book.Title}》，本地文件一并删除。",
                PrimaryButtonText = "删除",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot,
            };
            if (book.Id.StartsWith("sample-"))
            {
                await new ContentDialog { Title="提示", Content="示例书不能删除。", CloseButtonText="知道了", XamlRoot=XamlRoot }.ShowAsync();
                return;
            }
            if (await dlg.ShowAsync() == ContentDialogResult.Primary)
            {
                await BookStore.DeleteAsync(book.Id);
                BuildBooks();
                BuildContinue();
            }
        };
        flyout.Items.Add(del);
        return flyout;
    }

    private async Task<string?> AskNewCategoryAsync()
    {
        var box = new TextBox { PlaceholderText = "新分类名称" };
        var dlg = new ContentDialog
        {
            Title = "新建分类",
            Content = box,
            PrimaryButtonText = "确定",
            CloseButtonText = "取消",
            XamlRoot = XamlRoot,
        };
        if (await dlg.ShowAsync() != ContentDialogResult.Primary) return null;
        var name = box.Text?.Trim() ?? "";
        return string.IsNullOrEmpty(name) ? null : name;
    }

    private void ContinueCard_Click(object sender, RoutedEventArgs e)
    {
        var last = BookStore.LastRead;
        if (last != null) OpenBook(last);
    }

    private async void ImportEpub_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker();
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.CurrentWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            picker.FileTypeFilter.Add(".epub");
            var file = await picker.PickSingleFileAsync();
            if (file == null) return;

            var parsed = EpubParser.Parse(file.Path);
            var added = await BookStore.AddImportedAsync(file.Path, parsed);
            BuildBooks();
            BuildContinue();
            OpenBook(added);
        }
        catch (Exception ex)
        {
            var dialog = new ContentDialog
            {
                Title = "导入失败",
                Content = ex.Message,
                CloseButtonText = "知道了",
                XamlRoot = XamlRoot,
            };
            await dialog.ShowAsync();
        }
    }

    private void Placeholder_Click(object sender, RoutedEventArgs e) { }

    private void Theme_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string theme })
        {
            AppTheme.Set(theme);
            BuildBooks();
            BuildContinue();
            UpdateThemeButtons();
        }
    }

    private void About_Click(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(AboutPage));

    private void UpdateThemeButtons()
    {
        foreach (var child in ThemeButtons.Children)
            if (child is Button button && button.Tag is string theme)
            {
                var active = theme == AppTheme.Current;
                button.Background = (Brush)Application.Current.Resources[active ? "SelectedSageBrush" : "SidebarBgBrush"];
                button.Foreground = (Brush)Application.Current.Resources[active ? "ForestGreenBrush" : "BookInkBrush"];
            }
    }
}
