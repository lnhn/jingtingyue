using System.Text.Json;
using JingTingYue.Models;
using JingTingYue.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;
using Windows.UI;

namespace JingTingYue.Views;

public sealed partial class ReaderPage : Page
{
    private bool _readerReady;
    private bool _tocVisible;
    private StoredBook? _book;
    private int _currentChapter;
    private int _pendingPageToRestore = -1;
    private bool _suppressOneSave;
    private DateTime _lastSave = DateTime.MinValue;
    private readonly TtsService _tts = new();
    private List<(string text, int start, int end)> _sentences = new();
    private bool _ttsActive;
    private bool _startingTts;
    private int _readRequestId;
    private int _speakingIdx;
    private string _noteSelectionText = "";
    private int _noteSelectionStart;
    private int _noteSelectionEnd;
    private int _noteChapter;
    private bool _noteEditorBusy;
    private WebView2? _shareWeb;
    private bool _shareSaving;

    private static readonly (string No, string Title)[] SampleToc = new[]
    {
        ("一", "修省"),
        ("二", "持身"),
        ("三", "处世"),
        ("四", "读书"),
    };

    public ReaderPage()
    {
        InitializeComponent();
        Unloaded += (_, _) => StopReading();
        Loaded += async (s, e) =>
        {
            FillVoices();
            await InitWebViewAsync();
        };
        // 键盘快捷键（用隧道事件，WebView2 聚焦时也能收到）
        AddHandler(UIElement.KeyDownEvent, new Microsoft.UI.Xaml.Input.KeyEventHandler(OnKeyDown), true);
    }

    private void OnKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case Windows.System.VirtualKey.Right:
            case Windows.System.VirtualKey.Space:
                _ = RunReader("window.JTReader.next();");
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.Left:
                _ = RunReader("window.JTReader.prev();");
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.PageDown:
                NavigateChapter(_currentChapter + 1);
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.PageUp:
                NavigateChapter(_currentChapter - 1);
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.Escape:
                if (PanelDismissLayer.Visibility == Visibility.Visible)
                {
                    DismissOverlay();
                    e.Handled = true;
                    break;
                }
                if (Frame.CanGoBack) Frame.GoBack();
                e.Handled = true;
                break;
        }
    }

    private string _voiceId = "zf_xiaoxiao";
    private double _rate = 1.0;
    private bool _syncingVoiceSelection;

    private async void FillVoices()
    {
        List<VoiceInfo> voices = new();
        // TTS 冷启动要加载模型，最多等 ~30 秒
        for (int i = 0; i < 15; i++)
        {
            try { voices = await TtsService.ChineseVoicesAsync(); } catch { voices = new(); }
            if (voices.Count > 0) break;
            await Task.Delay(2000);
        }
        try
        {
            SettingsVoiceCombo.Items.Clear();
            VoiceGrid.Children.Clear();
            VoiceGrid.RowDefinitions.Clear();
            foreach (var v in voices)
            {
                SettingsVoiceCombo.Items.Add(new ComboBoxItem { Content = v.Name, Tag = v.Id });
                AddVoiceCard(v.Id, v.Name);
            }
            if (voices.Count % 2 == 1 && VoiceGrid.Children.LastOrDefault() is ToggleButton lastCard)
            {
                Grid.SetColumnSpan(lastCard, 2);
                lastCard.HorizontalAlignment = HorizontalAlignment.Center;
                lastCard.Width = 190;
            }
            if (SettingsVoiceCombo.Items.Count > 0)
            {
                int selected = voices.FindIndex(v => v.Id == _voiceId);
                SettingsVoiceCombo.SelectedIndex = selected >= 0 ? selected : 0;
            }
            StyleVoiceCards();
            BuildRateSeg();
        }
        catch { }
    }

    private void AddVoiceCard(string id, string name)
    {
        int row = VoiceGrid.Children.Count / 2;
        while (VoiceGrid.RowDefinitions.Count <= row) VoiceGrid.RowDefinitions.Add(new RowDefinition());
        var card = new ToggleButton
        {
            Height = 54, Padding = new Thickness(12,0,12,0),
            CornerRadius = new CornerRadius(14),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            BorderThickness = new Thickness(0),
            Tag = id,
        };
        var selected = (Brush)Application.Current.Resources["SelectedSageBrush"];
        var plain = (Brush)Application.Current.Resources["SidebarBgBrush"];
        var ink = (Brush)Application.Current.Resources["BookInkBrush"];
        card.Resources["ToggleButtonBackground"] = plain;
        card.Resources["ToggleButtonBackgroundPointerOver"] = plain;
        card.Resources["ToggleButtonBackgroundPressed"] = plain;
        card.Resources["ToggleButtonBackgroundChecked"] = selected;
        card.Resources["ToggleButtonBackgroundCheckedPointerOver"] = selected;
        card.Resources["ToggleButtonBackgroundCheckedPressed"] = selected;
        card.Resources["ToggleButtonForegroundChecked"] = ink;
        card.Resources["ToggleButtonForegroundCheckedPointerOver"] = ink;
        card.Resources["ToggleButtonForegroundCheckedPressed"] = ink;
        card.Resources["ToggleButtonBorderBrushChecked"] = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        card.Resources["ToggleButtonBorderBrushCheckedPointerOver"] = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var txt = new TextBlock { Text = name, FontSize = 14, VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)Application.Current.Resources["BookInkBrush"] };
        sp.Children.Add(txt);
        card.Content = sp;
        card.IsChecked = id == _voiceId;
        card.Checked += (s, e) => SelectVoice(id, true);
        card.Unchecked += (s, e) =>
        {
            if (!_syncingVoiceSelection && _voiceId == id) card.IsChecked = true;
        };
        Grid.SetRow(card, row);
        Grid.SetColumn(card, VoiceGrid.Children.Count % 2);
        VoiceGrid.Children.Add(card);
    }

    private void StyleVoiceCards()
    {
        foreach (var obj in VoiceGrid.Children)
            if (obj is ToggleButton b)
            {
                bool on = b.IsChecked == true;
                b.Background = on ? (Brush)Application.Current.Resources["SelectedSageBrush"]
                                  : (Brush)Application.Current.Resources["SidebarBgBrush"];
            }
    }

    private void SelectVoice(string id, bool save)
    {
        if (_syncingVoiceSelection) return;
        _syncingVoiceSelection = true;
        try
        {
            _voiceId = id;
            _tts.SelectVoice(id);
            foreach (var child in VoiceGrid.Children)
                if (child is ToggleButton card && card.Tag is string cardId)
                    card.IsChecked = cardId == id;
            foreach (var item in SettingsVoiceCombo.Items)
                if (item is ComboBoxItem option && option.Tag is string optionId && optionId == id)
                {
                    if (!ReferenceEquals(SettingsVoiceCombo.SelectedItem, option))
                        SettingsVoiceCombo.SelectedItem = option;
                    break;
                }
            StyleVoiceCards();
        }
        finally { _syncingVoiceSelection = false; }
        if (save && _book != null) _ = BookStore.SaveSettingsAsync(_book.Id, voiceId: id);
    }

    private static readonly double[] Rates = { 0.75, 1.0, 1.25, 1.5, 2.0 };
    private void BuildRateSeg()
    {
        for (int i = 0; i < Rates.Length; i++)
        {
            double r = Rates[i];
            var b = new Button
            {
                Height = 40, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(0),
                Content = r.ToString("0.##") + "x", FontFamily = new FontFamily("Microsoft YaHei UI"),
                FontSize = 13, HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            b.Click += (s, e) => { _rate = r; _tts.SetRate(r); StyleRateSeg(); };
            b.Tag = r;
            Grid.SetColumn(b, i);
            RateSeg.Children.Add(b);
        }
        if (RateSeg != null) StyleRateSeg();
    }

    private void StyleRateSeg()
    {
        foreach (var c in RateSeg.Children)
            if (c is Button b && b.Tag is double r)
            {
                bool on = Math.Abs(r - _rate) < 0.01;
                b.Background = on ? (Brush)Application.Current.Resources["ForestGreenBrush"]
                                  : (Brush)Application.Current.Resources["SidebarBgBrush"];
                b.Foreground = on ? (Brush)Application.Current.Resources["OnAccentBrush"]
                                   : (Brush)Application.Current.Resources["BookInkBrush"];
                b.Resources["ButtonBackgroundPointerOver"] = b.Background;
                b.Resources["ButtonForegroundPointerOver"] = b.Foreground;
                b.Resources["ButtonBackgroundPressed"] = b.Background;
                b.Resources["ButtonForegroundPressed"] = b.Foreground;
            }
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is not ReaderRequest req) return;
        _book = req.Book;
        BookTitleText.Text = _book?.Title ?? req.Title;
        BookAuthorText.Text = _book?.Author ?? req.Author;

        if (_book?.Book is { Chapters.Count: > 0 })
        {
            _currentChapter = Math.Min(_book.LastChapter, _book.Book.Chapters.Count - 1);
            _pendingPageToRestore = _book.LastPage;
            BuildTocFromBook();
        }
        else
        {
            _currentChapter = 0;
            _pendingPageToRestore = -1;
            BuildSampleToc();
        }
        UpdateChapterButtons();
        ApplySavedBookSettings();
    }

    // 每本书独立设置：打开时恢复
    private void ApplySavedBookSettings()
    {
        if (_book == null) return;
        if (!string.IsNullOrEmpty(_book.VoiceId)) _voiceId = _book.VoiceId;
        if (_book.Rate > 0) _rate = _book.Rate;
        // 字体下拉：选中匹配项
        if (!string.IsNullOrEmpty(_book.FontFamily))
        {
            foreach (var obj in FontCombo.Items)
                if (obj is ComboBoxItem it && it.Tag is string tag && tag == _book.FontFamily)
                { FontCombo.SelectedItem = it; break; }
        }
        // 字号
        if (_book.FontSize > 0) SizeSlider.Value = _book.FontSize;
        // 语速
        SettingsRateSlider.Value = _rate;
        // 主题
        if (!string.IsNullOrEmpty(_book.Theme)) AppTheme.Set(_book.Theme);
    }

    private bool HasRealBook => _book?.Book is { Chapters.Count: > 0 };

    private void BuildSampleToc()
    {
        TocList.Children.Clear();
        for (int i = 0; i < SampleToc.Length; i++)
            AddTocItem(SampleToc[i].No, SampleToc[i].Title, i, i == 0);
    }

    private void BuildTocFromBook()
    {
        TocList.Children.Clear();
        for (int i = 0; i < _book!.Book!.Chapters.Count; i++)
        {
            var ch = _book.Book.Chapters[i];
            AddTocItem($"{i + 1:00}", ch.Title, i, i == _currentChapter);
        }
    }

    private void AddTocItem(string no, string title, int tag, bool selected)
    {
        var item = new Button
        {
            Background = selected ? (Brush)Application.Current.Resources["SelectedSageBrush"] : new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10, 8, 10, 8),
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Tag = tag,
        };
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        panel.Children.Add(new TextBlock
        {
            Text = no,
            Foreground = (Brush)Application.Current.Resources["MutedInkBrush"],
            FontFamily = new FontFamily("Microsoft YaHei UI"), FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center, Width = 28,
        });
        panel.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = (Brush)Application.Current.Resources["BookInkBrush"],
            FontFamily = new FontFamily("Microsoft YaHei UI"), FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap,
        });
        item.Content = panel;
        item.Click += TocItem_Click;
        TocList.Children.Add(item);
    }

    private readonly Microsoft.UI.Xaml.Controls.WebView2 ReaderWeb = new();

    private async Task InitWebViewAsync()
    {
        ReaderHost.Children.Clear();
        ReaderHost.Children.Add(ReaderWeb);
        await ReaderWeb.EnsureCoreWebView2Async();
        var core = ReaderWeb.CoreWebView2;
        var assetsDir = Path.Combine(AppContext.BaseDirectory, "Assets");
        core.SetVirtualHostNameToFolderMapping("jingtingyue.local", assetsDir, CoreWebView2HostResourceAccessKind.Allow);
        core.WebMessageReceived += OnWebMessageReceived;
        core.NavigationCompleted += OnNavigationCompleted;
        core.Navigate("https://jingtingyue.local/reader/reader.html?v=12");
    }

    private void OnNavigationCompleted(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        _readerReady = args.IsSuccess;
    }

    private string CurrentChapterHtml()
    {
        if (HasRealBook) return _book!.Book!.Chapters[_currentChapter].HtmlBody;
        var p = Path.Combine(AppContext.BaseDirectory, "Assets", "samples", "chapter1.html");
        return File.Exists(p) ? File.ReadAllText(p) : "<p>（示例内容缺失）</p>";
    }

    private string ChapterNotesScript()
    {
        if (!HasRealBook || _book!.Id.StartsWith("sample-")) return "window.JTReader.setNotes([]);";
        var notes = NoteStore.ForBook(_book.Id)
            .Where(n => n.ChapterIndex == _currentChapter)
            .Select(n => new { start = n.CharStart, end = n.CharEnd, text = n.NoteText });
        return $"window.JTReader.setNotes({JsonSerializer.Serialize(notes)});";
    }

    private void OnWebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        try
        {
            var text = args.TryGetWebMessageAsString();
            if (string.IsNullOrEmpty(text)) return;
            using var doc = JsonDocument.Parse(text);
            var type = doc.RootElement.GetProperty("type").GetString();
            if (type == "ready")
            {
                _ = sender.ExecuteScriptAsync(
                    $"window.JTReader.setTheme({JsonSerializer.Serialize(AppTheme.Current)});");
                // 恢复本书字体/字号
                if (_book != null)
                {
                    if (!string.IsNullOrEmpty(_book.FontFamily))
                        _ = sender.ExecuteScriptAsync($"window.JTReader.setFontFamily({JsonSerializer.Serialize(_book.FontFamily)});");
                    if (_book.FontSize > 0)
                        _ = sender.ExecuteScriptAsync($"window.JTReader.setFontSize({_book.FontSize});");
                }
                var html = CurrentChapterHtml();
                var restore = _pendingPageToRestore;
                _pendingPageToRestore = -1;
                _suppressOneSave = restore > 0;
                var startArg = restore >= 0 ? "," + restore : "";
                _ = sender.ExecuteScriptAsync(
                    $"window.JTReader.setContent({JsonSerializer.Serialize(html)}{startArg});{ChapterNotesScript()}");
            }
            else if (type == "pageChanged")
            {
                var cur = doc.RootElement.GetProperty("current").GetInt32() + 1;
                var total = doc.RootElement.GetProperty("total").GetInt32();
                PageIndicatorText.Text = $"{cur} / {total}";
                if (_suppressOneSave) { _suppressOneSave = false; return; }
                SavePositionThrottled(doc.RootElement.GetProperty("current").GetInt32());
            }
            else if (type == "noteRequest")
            {
                var t = doc.RootElement.GetProperty("text").GetString();
                var s = doc.RootElement.GetProperty("start").GetInt32();
                var e = doc.RootElement.GetProperty("end").GetInt32();
                _ = ShowNoteDialogAsync(t!, s, e);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("reader msg: " + ex.Message);
        }
    }

    private void SavePositionThrottled(int page)
    {
        if (_book == null || _book.Id.StartsWith("sample-")) return;
        if ((DateTime.Now - _lastSave).TotalSeconds < 2) return;
        _lastSave = DateTime.Now;
        int chapters = _book.Book?.Chapters.Count ?? 1;
        int progress = Math.Min(99, (int)Math.Round(100.0 * _currentChapter / chapters));
        _ = BookStore.UpdateProgressAsync(_book.Id, _currentChapter, page, progress);
    }

    private async Task<string?> RunReader(string js)
    {
        if (!_readerReady) return null;
        try { return await ReaderWeb.CoreWebView2.ExecuteScriptAsync(js); }
        catch { return null; }
    }

    private void Prev_Click(object sender, RoutedEventArgs e) => _ = RunReader("window.JTReader.prev();");
    private void Next_Click(object sender, RoutedEventArgs e) => _ = RunReader("window.JTReader.next();");

    private int ChapterCount => HasRealBook ? _book!.Book!.Chapters.Count : SampleToc.Length;

    private void UpdateChapterButtons()
    {
        PrevChapterButton.IsEnabled = _currentChapter > 0;
        NextChapterButton.IsEnabled = _currentChapter < ChapterCount - 1;
    }

    private void PrevChapter_Click(object sender, RoutedEventArgs e) => NavigateChapter(_currentChapter - 1);
    private void NextChapter_Click(object sender, RoutedEventArgs e) => NavigateChapter(_currentChapter + 1);

    private void NavigateChapter(int index)
    {
        if (index < 0 || index >= ChapterCount || index == _currentChapter) return;
        if (_ttsActive) Stop_Click(this, new RoutedEventArgs());
        _currentChapter = index;
        for (int i = 0; i < TocList.Children.Count; i++)
            if (TocList.Children[i] is Button item)
                item.Background = i == index
                    ? (Brush)Application.Current.Resources["SelectedSageBrush"]
                    : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        UpdateChapterButtons();
        _ = RunReader($"window.JTReader.setContent({JsonSerializer.Serialize(CurrentChapterHtml())});{ChapterNotesScript()}");
        if (_book is { } book && !book.Id.StartsWith("sample-"))
            _ = BookStore.UpdateProgressAsync(book.Id, index, 0, Math.Min(99, (int)Math.Round(100.0 * index / ChapterCount)));
    }

    private void TocItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not int idx) return;
        NavigateChapter(idx);
    }

    private void TocToggle_Click(object sender, RoutedEventArgs e)
    {
        _tocVisible = !_tocVisible;
        TocColumn.Width = new GridLength(_tocVisible ? 320 : 0);
    }

    private void Format_Click(object sender, RoutedEventArgs e) { }

    private async void Theme_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not string theme) return;
        AppTheme.Set(theme);
        await RunReader($"window.JTReader.setTheme({JsonSerializer.Serialize(theme)});");
        if (_book != null) _ = BookStore.SaveSettingsAsync(_book.Id, theme: theme);
    }

    private async void FontCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FontCombo.SelectedItem is ComboBoxItem it && it.Tag is string f)
        {
            await RunReader($"window.JTReader.setFontFamily({JsonSerializer.Serialize(f)});");
            if (_book != null) _ = BookStore.SaveSettingsAsync(_book.Id, fontFamily: f);
        }
    }

    private async void SizeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        int v = (int)Math.Round(e.NewValue);
        SizeText.Text = v.ToString();
        await RunReader($"window.JTReader.setFontSize({v});");
        if (_book != null) _ = BookStore.SaveSettingsAsync(_book.Id, fontSize: v);
    }

    private void SettingsVoiceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SettingsVoiceCombo.SelectedItem is ComboBoxItem it && it.Tag is string id)
            SelectVoice(id, true);
    }

    private void SettingsRateSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        double v = e.NewValue;
        _rate = v;
        SettingsRateText.Text = v.ToString("0.0") + "x";
        _tts.SetRate(v);
        if (RateSeg != null) StyleRateSeg();
        if (_book != null) _ = BookStore.SaveSettingsAsync(_book.Id, rate: v);
    }

    private void OpenTtsPanel_Click(object sender, RoutedEventArgs e)
    {
        ShowOverlay(TtsPanel);
    }

    private void ShowOverlay(Border panel)
    {
        TtsPanel.Visibility = Visibility.Collapsed;
        NotesPanel.Visibility = Visibility.Collapsed;
        SettingsPanel.Visibility = Visibility.Collapsed;
        NoteEditorPanel.Visibility = Visibility.Collapsed;
        if (panel != SharePanel) CloseShare();
        panel.Visibility = Visibility.Visible;
        PanelDismissLayer.Visibility = Visibility.Visible;
    }

    private void RefreshDismissLayer()
    {
        PanelDismissLayer.Visibility = TtsPanel.Visibility == Visibility.Visible ||
            NotesPanel.Visibility == Visibility.Visible || SettingsPanel.Visibility == Visibility.Visible ||
            NoteEditorPanel.Visibility == Visibility.Visible || SharePanel.Visibility == Visibility.Visible
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void PanelDismissLayer_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e) => DismissOverlay();

    private void OutsidePanel_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (PanelDismissLayer.Visibility == Visibility.Visible) DismissOverlay();
    }

    private void DismissOverlay()
    {
        if (SharePanel.Visibility == Visibility.Visible) CloseShare();
        else if (NoteEditorPanel.Visibility == Visibility.Visible) CloseNoteEditor();
        else if (SettingsPanel.Visibility == Visibility.Visible) CloseSettings_Click(this, new RoutedEventArgs());
        else if (NotesPanel.Visibility == Visibility.Visible) CloseNotes_Click(this, new RoutedEventArgs());
        else if (TtsPanel.Visibility == Visibility.Visible) CloseTtsPanel_Click(this, new RoutedEventArgs());
    }

    private async void StartTts_Click(object sender, RoutedEventArgs e)
    {
        if (_ttsActive) { TogglePause(); return; }
        if (_startingTts) return;
        _startingTts = true;
        StartTtsButton.IsEnabled = false;
        TtsStatus.Text = "● 正在准备朗读";
        try { await StartReadingAsync(++_readRequestId); }
        finally
        {
            _startingTts = false;
            StartTtsButton.IsEnabled = true;
            if (!_ttsActive && TtsPanel.Visibility == Visibility.Visible)
                TtsStatus.Text = "● 准备就绪";
        }
    }

    private void CloseTtsPanel_Click(object sender, RoutedEventArgs e)
    {
        if (_startingTts && !_ttsActive) _readRequestId++;
        TtsPanel.Visibility = Visibility.Collapsed;
        RefreshDismissLayer();
    }

    private async Task StartReadingAsync(int requestId)
    {
        string? raw = null;
        try
        {
            raw = await RunReader("window.JTReader.getSentences()");
            if (requestId != _readRequestId) return;
            if (string.IsNullOrWhiteSpace(raw) || raw == "null") return;
            using var doc = JsonDocument.Parse(raw);
            _sentences = new();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                _sentences.Add((
                    el.GetProperty("text").GetString() ?? "",
                    el.GetProperty("start").GetInt32(),
                    el.GetProperty("end").GetInt32()));
            }
            if (_sentences.Count == 0)
            {
                RunReader("window.JTReader.showHint('这一章没有可朗读的文字', 2000);");
                return;
            }

            // 从当前可见位置所在句开始（用已验证的 getVisibleTextIndex）
            int startChar = 0;
            var visRaw = await RunReader("window.JTReader.getVisibleTextIndex()");
            if (requestId != _readRequestId) return;
            if (!string.IsNullOrWhiteSpace(visRaw) && visRaw != "null")
                int.TryParse(visRaw.Trim('"'), out startChar);
            int startIdx = 0;
            for (int i = 0; i < _sentences.Count; i++)
                if (_sentences[i].end > startChar) { startIdx = i; break; }

            _tts.SentenceStart += OnTtsSentence;
            _tts.Finished += OnTtsFinished;
            _tts.Error += OnTtsError;
            _tts.Buffering += OnTtsBuffering;
            _ttsActive = true;
            StopTtsButton.IsEnabled = true;
            _tts.SelectVoice(_voiceId);
            _tts.SetRate(_rate);
            ShowOverlay(TtsPanel);
            StartTtsLabel.Text = "暂停朗读";
            TtsStatus.Text = "● 正在准备语音";
            _tts.Start(_sentences.Select(s => new SpokenSentence(s.text, s.start, s.end)).ToList(), startIdx);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("tts start: " + ex.Message);
        }
    }

    private async void OnTtsSentence(int idx)
    {
        _ = DispatcherQueue.TryEnqueue(async () =>
        {
            if (idx >= _sentences.Count) return;
            _speakingIdx = idx;
            if (!_paused) TtsStatus.Text = "● 正在朗读";
            var s = _sentences[idx];
            // 朗读句进入下一页时再翻页，当前页内只更新高亮。
            await RunReader($"window.JTReader.highlightRange({s.start},{s.end});window.JTReader.goToOffset({s.start}, true);");
        });
    }

    private void OnTtsFinished()
    {
        _ = DispatcherQueue.TryEnqueue(async () =>
        {
            bool hadMore = _currentChapter < ChapterCount - 1;
            StopReading();
            if (!hadMore)
            {
                TtsStatus.Text = "● 本书朗读完毕";
                return;
            }
            // 弹对话框问要不要读下一章
            var dialog = new ContentDialog
            {
                Title = "继续朗读",
                Content = $"本章已读完，是否继续朗读下一章？",
                PrimaryButtonText = "继续",
                CloseButtonText = "不用了",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.Content.XamlRoot,
            };
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                NavigateChapter(_currentChapter + 1);
                await Task.Delay(500); // 等 WebView 加载新章节
                await StartReadingAsync(++_readRequestId);
            }
        });
    }

    private void OnTtsError(string msg)
    {
        _ = DispatcherQueue.TryEnqueue(async () =>
        {
            await RunReader($"window.JTReader.showHint('语音错误: {msg}', 4000);");
        });
    }

    private void OnTtsBuffering(int done, int total)
    {
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            if (_ttsActive && !_paused) TtsStatus.Text = $"● 正在缓冲语音 ({done}/{total} 句)";
        });
    }

    private bool _paused;
    private void TogglePause()
    {
        if (_paused) { _tts.Resume(); _paused = false; StartTtsLabel.Text = "暂停朗读"; TtsStatus.Text = _tts.IsBuffering ? "● 正在缓冲语音…" : "● 正在朗读"; }
        else { _tts.Pause(); _paused = true; StartTtsLabel.Text = "继续朗读"; TtsStatus.Text = "● 已暂停"; }
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        StopReading();
    }

    private void StopReading()
    {
        _readRequestId++;
        _tts.Stop();
        _ttsActive = false;
        _paused = false;
        StopTtsButton.IsEnabled = false;
        TtsStatus.Text = "● 准备就绪";
        _tts.SentenceStart -= OnTtsSentence;
        _tts.Finished -= OnTtsFinished;
        _tts.Error -= OnTtsError;
        _tts.Buffering -= OnTtsBuffering;
        StartTtsLabel.Text = "开始朗读";
        TtsPanel.Visibility = Visibility.Collapsed;
        RefreshDismissLayer();
        _ = RunReader("window.JTReader.highlightRange(0,0);");
    }

    protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        StopReading();
        base.OnNavigatedFrom(e);
    }

    private void RateSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e) { }

    private void Placeholder_Click(object sender, RoutedEventArgs e) { }

    private async void Note_Click(object sender, RoutedEventArgs e)
    {
        if (!HasRealBook || _book!.Id.StartsWith("sample-"))
        {
            await RunReader("window.JTReader.showHint('示例书不能写笔记，请先导入 EPUB', 2500);");
            return;
        }
        var raw = await RunReader("window.JTReader.getSelectionRange()");
        if (string.IsNullOrWhiteSpace(raw) || raw == "null")
        {
            await RunReader("window.JTReader.showHint('请先在正文里选中要记的文字', 2200);");
            return;
        }
        string selText = "";
        int selStart = 0, selEnd = 0;
        try
        {
            using var d = JsonDocument.Parse(raw);
            selText = d.RootElement.GetProperty("text").GetString() ?? "";
            selStart = d.RootElement.GetProperty("start").GetInt32();
            selEnd = d.RootElement.GetProperty("end").GetInt32();
        }
        catch { return; }
        await ShowNoteDialogAsync(selText, selStart, selEnd);
    }

    private async Task ShowNoteDialogAsync(string selText, int selStart, int selEnd)
    {
        if (!HasRealBook || _book!.Id.StartsWith("sample-"))
        {
            await RunReader("window.JTReader.showHint('示例书不能写笔记，请先导入 EPUB', 2500);");
            return;
        }
        _noteSelectionText = selText.Trim();
        _noteSelectionStart = selStart;
        _noteSelectionEnd = selEnd;
        _noteChapter = _currentChapter;
        NoteEditorBookTitle.Text = BookTitleText.Text;
        NoteQuoteText.Text = _noteSelectionText;
        NoteEditorInput.Text = "";
        ShowOverlay(NoteEditorPanel);
        NoteEditorInput.Focus(FocusState.Programmatic);
    }

    private void CloseNoteEditor_Click(object sender, RoutedEventArgs e) => CloseNoteEditor();

    private void CloseNoteEditor()
    {
        if (_noteEditorBusy) return;
        NoteEditorPanel.Visibility = Visibility.Collapsed;
        RefreshDismissLayer();
    }

    private async void SaveNoteEditor_Click(object sender, RoutedEventArgs e)
    {
        if (_noteEditorBusy || !HasRealBook) return;
        _noteEditorBusy = true;
        NoteEditorSaveButton.IsEnabled = false;
        int start = _noteSelectionStart, end = _noteSelectionEnd;
        var note = new BookNote
        {
            BookId = _book!.Id,
            ChapterIndex = _noteChapter,
            CharStart = start,
            CharEnd = end,
            SelectedText = _noteSelectionText,
            NoteText = NoteEditorInput.Text?.Trim() ?? "",
        };
        try
        {
            await NoteStore.AddAsync(note);
            NoteEditorPanel.Visibility = Visibility.Collapsed;
            RefreshDismissLayer();
            if (_currentChapter == _noteChapter)
                await RunReader(ChapterNotesScript());
            await RunReader("window.JTReader.showHint('笔记已保存', 1600);");
        }
        finally
        {
            _noteEditorBusy = false;
            NoteEditorSaveButton.IsEnabled = true;
        }
    }

    private void NotesList_Click(object sender, RoutedEventArgs e) => OpenNotesPanel();

    private void OpenNotesPanel()
    {
        NotesBookLabel.Text = $"{BookTitleText.Text} · {BookAuthorText.Text}";
        RenderNotes("");
        ShowOverlay(NotesPanel);
    }

    private void CloseNotes_Click(object sender, RoutedEventArgs e)
    {
        NotesPanel.Visibility = Visibility.Collapsed;
        RefreshDismissLayer();
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e) => ShowOverlay(SettingsPanel);
    private void CloseSettings_Click(object sender, RoutedEventArgs e)
    {
        SettingsPanel.Visibility = Visibility.Collapsed;
        RefreshDismissLayer();
    }

    private void NotesSearch_TextChanged(object sender, TextChangedEventArgs e) =>
        RenderNotes(NotesSearch.Text?.Trim() ?? "");

    private void RenderNotes(string query)
    {
        NotesListPanel.Children.Clear();
        var all = HasRealBook ? NoteStore.ForBook(_book!.Id) : new();
        var notes = all.Where(n =>
            string.IsNullOrEmpty(query) ||
            n.SelectedText.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            (n.NoteText ?? "").Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        NotesCountLabel.Text = $"{notes.Count} 段摘录与想法";

        if (notes.Count == 0)
        {
            var empty = new StackPanel { Spacing = 14, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,40,0,0) };
            empty.Children.Add(new TextBlock { Text = "静", FontFamily = new FontFamily("KaiTi"), FontSize = 44, HorizontalAlignment = HorizontalAlignment.Center, Foreground = (Brush)Application.Current.Resources["MutedInkBrush"] });
            empty.Children.Add(new TextBlock { Text = "让阅读，留下痕迹", FontSize = 20, HorizontalAlignment = HorizontalAlignment.Center, Foreground = (Brush)Application.Current.Resources["BookInkBrush"] });
            empty.Children.Add(new TextBlock { Text = "选中正文，右键添加笔记，收藏一句话，也留住当时的想法。", FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, Foreground = (Brush)Application.Current.Resources["MutedInkBrush"] });
            NotesListPanel.Children.Add(empty);
            return;
        }
        foreach (var n in notes)
        {
            var card = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = (Brush)Application.Current.Resources["NoteCardBrush"],
                BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(14),
                Padding = new Thickness(16, 12, 16, 12), HorizontalContentAlignment = HorizontalAlignment.Left,
                Tag = n,
            };
            var sp = new StackPanel { Spacing = 6 };
            sp.Children.Add(new TextBlock { Text = n.SelectedText, FontSize = 14, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)Application.Current.Resources["BookInkBrush"] });
            if (!string.IsNullOrWhiteSpace(n.NoteText))
                sp.Children.Add(new TextBlock { Text = n.NoteText, FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)Application.Current.Resources["TerracottaBrush"] });
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
            var jump = new HyperlinkButton { Content = "回到原文", FontSize = 12, Foreground = (Brush)Application.Current.Resources["ForestGreenBrush"], Tag = n };
            jump.Click += NoteJump_Click;
            var share = new HyperlinkButton { Content = "分享卡片", FontSize = 12, Foreground = (Brush)Application.Current.Resources["ForestGreenBrush"], Tag = n };
            share.Click += ShareNote_Click;
            var del = new HyperlinkButton { Content = "删除", FontSize = 12, Foreground = (Brush)Application.Current.Resources["DangerBrush"], Tag = n };
            del.Click += async (s, a) => { await NoteStore.DeleteAsync(n.Id); RenderNotes(NotesSearch.Text?.Trim() ?? ""); };
            row.Children.Add(jump); row.Children.Add(share); row.Children.Add(del);
            sp.Children.Add(row);
            card.Content = sp;
            NotesListPanel.Children.Add(card);
        }
    }

    private async void NoteJump_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not HyperlinkButton hb || hb.Tag is not BookNote note) return;
        NotesPanel.Visibility = Visibility.Collapsed;
        RefreshDismissLayer();
        if (note.ChapterIndex != _currentChapter)
        {
            _currentChapter = note.ChapterIndex;
            await RunReader($"window.JTReader.setContent({JsonSerializer.Serialize(CurrentChapterHtml())});{ChapterNotesScript()}");
            await Task.Delay(250);
        }
        await RunReader($"window.JTReader.goToOffset({note.CharStart}, false);");
        await RunReader($"window.JTReader.highlightRange({note.CharStart},{note.CharEnd});");
    }

    private async void NoteItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not BookNote note) return;
        if (note.ChapterIndex != _currentChapter)
        {
            _currentChapter = note.ChapterIndex;
            var html = CurrentChapterHtml();
            await RunReader($"window.JTReader.setContent({JsonSerializer.Serialize(html)});{ChapterNotesScript()}");
            await Task.Delay(250);
        }
        await RunReader($"window.JTReader.goToOffset({note.CharStart}, false);");
        await RunReader($"window.JTReader.highlightRange({note.CharStart},{note.CharEnd});");
    }

    private async void ShareNote_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not HyperlinkButton hb || hb.Tag is not BookNote note) return;
        NotesPanel.Visibility = Visibility.Collapsed;
        SharePreviewHost.Children.Clear();
        var web = new WebView2 { Width = ShareCard.PreviewWidth, Height = ShareCard.PreviewHeight };
        _shareWeb = web;
        SharePreviewHost.Children.Add(web);
        ShowOverlay(SharePanel);
        SaveShareButton.IsEnabled = false;
        try
        {
            await ShareCard.LoadCardAsync(web, note.SelectedText, note.NoteText,
                BookTitleText.Text, BookAuthorText.Text);
            if (ReferenceEquals(_shareWeb, web)) SaveShareButton.IsEnabled = true;
        }
        catch
        {
            CloseShare();
            await RunReader("window.JTReader.showHint('卡片预览失败，请重试', 3000);");
        }
    }

    private void CloseShare_Click(object sender, RoutedEventArgs e) => CloseShare();

    private void CloseShare()
    {
        if (_shareSaving) return;
        SharePanel.Visibility = Visibility.Collapsed;
        SharePreviewHost.Children.Clear();
        _shareWeb = null;
        RefreshDismissLayer();
    }

    private async void SaveShare_Click(object sender, RoutedEventArgs e)
    {
        var web = _shareWeb;
        if (web is null) return;
        _shareSaving = true;
        SaveShareButton.IsEnabled = false;
        try
        {
            var path = await ShareCard.CaptureToPngAsync(web);
            _shareSaving = false;
            CloseShare();
            await RunReader($"window.JTReader.showHint({JsonSerializer.Serialize($"已保存到: {path}")}, 4000);");
        }
        catch
        {
            await RunReader("window.JTReader.showHint('保存卡片失败，请重试', 3000);");
        }
        finally { _shareSaving = false; SaveShareButton.IsEnabled = true; }
    }

    private void BackShelf_Click(object sender, RoutedEventArgs e)
    {
        StopReading();
        if (Frame.CanGoBack) Frame.GoBack();
    }
}
