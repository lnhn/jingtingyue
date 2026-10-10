using System.Text.RegularExpressions;
using JingTingYue.Models;
using JingTingYue.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace JingTingYue.Views;

public sealed partial class SnippetsPage : Page
{
    private readonly TtsService _tts = new();
    private Snippet? _current;
    private List<SpokenSentence> _sentences = new();
    private bool _ttsActive;
    private bool _paused;
    private string _voiceId = "zf_xiaoxiao";
    private double _rate = 1.0;
    private bool _voicesLoaded;
    private double _fontSize = 20;

    private static readonly double[] Rates = { 0.75, 1.0, 1.25, 1.5, 2.0 };

    public SnippetsPage()
    {
        InitializeComponent();
        Unloaded += (_, _) => StopReading();
        BuildRateButtons();
        if (TextEditor != null) SetFontSize(_fontSize);
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        BuildList();
        _ = FillVoicesAsync(); // 不阻塞导航，音色后台加载
        // 如果带了参数（从某处新建），直接开新片段
        if (e.Parameter is Snippet existing)
            LoadSnippet(existing);
        else if (SnippetStore.Snippets.Count > 0)
            LoadSnippet(SnippetStore.AllOrdered()[0]);
        else
            NewSnippet();
    }

    protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        StopReading();
        // 离开时自动保存当前片段
        if (_current != null && !string.IsNullOrWhiteSpace(TextEditor.Text))
        {
            _current.Title = TitleBox.Text?.Trim() ?? "";
            _current.Text = TextEditor.Text ?? "";
            _ = SnippetStore.UpdateAsync(_current);
        }
        base.OnNavigatedFrom(e);
    }

    // ---------- 列表 ----------

    private void BuildList()
    {
        SnippetList.Children.Clear();
        var all = SnippetStore.AllOrdered();
        if (all.Count == 0)
        {
            SnippetList.Children.Add(new TextBlock
            {
                Text = "还没有片段。\n粘贴一段文字，\n让它读给你听。",
                Foreground = (Brush)Application.Current.Resources["MutedInkBrush"],
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(14, 30, 14, 0),
                LineHeight = 24,
            });
            return;
        }
        foreach (var s in all)
        {
            bool selected = _current?.Id == s.Id;
            var card = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = selected
                    ? (Brush)Application.Current.Resources["SelectedSageBrush"]
                    : new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(14, 12, 14, 12),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Tag = s,
            };
            var sp = new StackPanel { Spacing = 4 };
            var title = string.IsNullOrWhiteSpace(s.Title) ? "未命名片段" : s.Title;
            sp.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = (Brush)Application.Current.Resources["BookInkBrush"],
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            var preview = s.Text.Length > 60 ? s.Text.Substring(0, 60) + "…" : s.Text;
            preview = preview.Replace("\r", " ").Replace("\n", " ").Trim();
            sp.Children.Add(new TextBlock
            {
                Text = string.IsNullOrEmpty(preview) ? "（空）" : preview,
                FontSize = 11,
                Foreground = (Brush)Application.Current.Resources["MutedInkBrush"],
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            sp.Children.Add(new TextBlock
            {
                Text = DateTimeOffset.FromUnixTimeSeconds(s.UpdatedAt).ToLocalTime().ToString("MM-dd HH:mm"),
                FontSize = 10,
                Foreground = (Brush)Application.Current.Resources["MutedInkBrush"],
            });
            card.Content = sp;
            card.Click += SnippetCard_Click;
            SnippetList.Children.Add(card);
        }
    }

    private void SnippetCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Snippet s })
        {
            // 自动保存当前
            if (_current != null && !ReferenceEquals(_current, s) && !string.IsNullOrWhiteSpace(TextEditor.Text))
            {
                _current.Title = TitleBox.Text?.Trim() ?? "";
                _current.Text = TextEditor.Text ?? "";
                _ = SnippetStore.UpdateAsync(_current);
            }
            LoadSnippet(s);
        }
    }

    private void LoadSnippet(Snippet s)
    {
        StopReading();
        _current = s;
        TitleBox.Text = s.Title;
        TextEditor.Text = s.Text;
        _voiceId = string.IsNullOrEmpty(s.VoiceId) ? "zf_xiaoxiao" : s.VoiceId;
        _rate = s.Rate > 0 ? s.Rate : 1.0;
        ApplyVoiceAndRateToUi();
        BuildList();
    }

    private void NewSnippet()
    {
        StopReading();
        _current = null;
        TitleBox.Text = "";
        TextEditor.Text = "";
        ApplyVoiceAndRateToUi();
        BuildList();
        TitleBox.Focus(FocusState.Programmatic);
    }

    private void NewSnippet_Click(object sender, RoutedEventArgs e) => NewSnippet();

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        var text = TextEditor.Text ?? "";
        if (string.IsNullOrWhiteSpace(text))
        {
            await ShowDialogAsync("提示", "内容为空，没有可保存的文字。");
            return;
        }
        if (_current == null)
        {
            var s = new Snippet
            {
                Title = TitleBox.Text?.Trim() ?? "",
                Text = text,
                VoiceId = _voiceId,
                Rate = _rate,
            };
            _current = await SnippetStore.AddAsync(s);
        }
        else
        {
            _current.Title = TitleBox.Text?.Trim() ?? "";
            _current.Text = text;
            _current.VoiceId = _voiceId;
            _current.Rate = _rate;
            await SnippetStore.UpdateAsync(_current);
        }
        BuildList();
        await ShowDialogAsync("已保存", "片段已保存到本机。");
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_current == null) return;
        try
        {
            var dlg = new ContentDialog
            {
                Title = "删除这个片段？",
                Content = "删除后不可恢复。",
                PrimaryButtonText = "删除",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot,
            };
            if (await dlg.ShowAsync() == ContentDialogResult.Primary)
            {
                var id = _current.Id;
                _current = null;
                await SnippetStore.DeleteAsync(id);
                NewSnippet();
            }
        }
        catch (Exception ex)
        {
            TtsService.Log("delete snippet: " + ex.Message);
        }
    }

    private void BackToShelf_Click(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack) Frame.GoBack();
    }

    private void TextEditor_TextChanged(object sender, TextChangedEventArgs e)
    {
        // 编辑中不做额外动作；自动保存留给离开/切换时
    }

    // ---------- 编辑器字体 / 字号 ----------

    private void FontCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TextEditor == null) return;
        if (FontCombo.SelectedItem is ComboBoxItem it && it.Tag is string fontName)
            TextEditor.FontFamily = new FontFamily(fontName);
    }

    private void FontSize_Inc(object sender, RoutedEventArgs e) => SetFontSize(_fontSize + 1);
    private void FontSize_Dec(object sender, RoutedEventArgs e) => SetFontSize(_fontSize - 1);

    private void SetFontSize(double size)
    {
        _fontSize = Math.Clamp(size, 14, 36);
        TextEditor.FontSize = _fontSize;
        FontSizeText.Text = _fontSize.ToString("0");
    }

    // ---------- 音色 / 语速 ----------

    private async Task FillVoicesAsync()
    {
        if (_voicesLoaded) return;
        _voicesLoaded = true;

        // 先立刻填上默认音色，不等服务器
        var defaults = new List<VoiceInfo>
        {
            new("zf_xiaoxiao", "女声 · 晓晓"),
            new("zf_xiaobei", "女声 · 小北"),
            new("zm_yunjian", "男声 · 云健"),
            new("zm_yunxi", "男声 · 云希"),
            new("zm_yunyang", "男声 · 云扬"),
        };
        VoiceCombo.Items.Clear();
        foreach (var v in defaults)
            VoiceCombo.Items.Add(new ComboBoxItem { Content = v.Name, Tag = v.Id });
        ApplyVoiceAndRateToUi();

        // 后台慢慢拉真实音色，成功了再替换
        try
        {
            var real = await TtsService.ChineseVoicesAsync();
            if (real.Count > 0)
            {
                VoiceCombo.Items.Clear();
                foreach (var v in real)
                    VoiceCombo.Items.Add(new ComboBoxItem { Content = v.Name, Tag = v.Id });
                ApplyVoiceAndRateToUi();
            }
        }
        catch { }
    }

    private void ApplyVoiceAndRateToUi()
    {
        foreach (var obj in VoiceCombo.Items)
            if (obj is ComboBoxItem it && it.Tag is string id && id == _voiceId)
            {
                VoiceCombo.SelectedItem = it;
                break;
            }
        if (VoiceCombo.SelectedItem == null && VoiceCombo.Items.Count > 0)
            VoiceCombo.SelectedIndex = 0;
        StyleRateButtons();
    }

    private void VoiceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (VoiceCombo.SelectedItem is ComboBoxItem it && it.Tag is string id)
        {
            _voiceId = id;
            _tts.SelectVoice(id);
        }
    }

    private void BuildRateButtons()
    {
        RatePanel.Children.Clear();
        foreach (var r in Rates)
        {
            var b = new Button
            {
                Height = 32,
                Padding = new Thickness(10, 0, 10, 0),
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(0),
                Content = r.ToString("0.##") + "x",
                FontSize = 12,
                Tag = r,
            };
            b.Click += (s, e) => { _rate = r; _tts.SetRate(r); StyleRateButtons(); };
            RatePanel.Children.Add(b);
        }
        StyleRateButtons();
    }

    private void StyleRateButtons()
    {
        foreach (var c in RatePanel.Children)
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

    // ---------- 朗读 ----------

    private void Play_Click(object sender, RoutedEventArgs e)
    {
        if (_ttsActive) { TogglePause(); return; }
        StartReading();
    }

    private void StartReading()
    {
        var raw = TextEditor.Text ?? "";
        if (string.IsNullOrWhiteSpace(raw))
        {
            _ = ShowDialogAsync("没有文字", "请先在编辑框里粘贴或输入要朗读的内容。");
            return;
        }
        _sentences = SplitSentences(raw);
        if (_sentences.Count == 0)
        {
            _ = ShowDialogAsync("没有可朗读的文字", "内容里没找到成句的文字。");
            return;
        }
        _tts.SelectVoice(_voiceId);
        _tts.SetRate(_rate);
        _tts.SentenceStart += OnSentenceStart;
        _tts.Finished += OnTtsFinished;
        _tts.Error += OnTtsError;
        _tts.Generating += OnTtsGenerating;
        _tts.Buffering += OnTtsBuffering;
        _ttsActive = true;
        _paused = false;
        StopButton.IsEnabled = true;
        PlayIcon.Glyph = "\uE769"; // pause
        StatusText.Text = "● 正在朗读";
        _tts.Start(_sentences, 0);
    }

    private void OnTtsGenerating()
    {
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            if (_ttsActive && !_paused) StatusText.Text = "● 正在缓冲语音…";
        });
    }

    private void OnTtsBuffering(int done, int total)
    {
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            if (_ttsActive && !_paused) StatusText.Text = $"● 正在缓冲语音 ({done}/{total} 句)";
        });
    }

    private void OnSentenceStart(int idx)
    {
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            if (idx < 0 || idx >= _sentences.Count) return;
            StatusText.Text = $"● 正在朗读 ({idx + 1}/{_sentences.Count})";
            var s = _sentences[idx];
            try
            {
                TextEditor.Focus(FocusState.Programmatic);
                TextEditor.Select(s.CharStart, Math.Max(1, s.CharEnd - s.CharStart));
            }
            catch { }
        });
    }

    private void OnTtsFinished()
    {
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            StopReading();
            StatusText.Text = "● 朗读完毕";
        });
    }

    private void OnTtsError(string msg)
    {
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            StatusText.Text = "● " + msg;
        });
    }

    private void TogglePause()
    {
        if (_paused)
        {
            _tts.Resume();
            _paused = false;
            PlayIcon.Glyph = "\uE769";
            StatusText.Text = _tts.IsBuffering ? "● 正在缓冲语音…" : "● 正在朗读";
        }
        else
        {
            _tts.Pause();
            _paused = true;
            PlayIcon.Glyph = "\uE768";
            StatusText.Text = "● 已暂停";
        }
    }

    private void Stop_Click(object sender, RoutedEventArgs e) => StopReading();

    private void StopReading()
    {
        if (!_ttsActive && !_paused) return;
        _tts.Stop();
        _tts.SentenceStart -= OnSentenceStart;
        _tts.Finished -= OnTtsFinished;
        _tts.Error -= OnTtsError;
        _tts.Generating -= OnTtsGenerating;
        _tts.Buffering -= OnTtsBuffering;
        _ttsActive = false;
        _paused = false;
        StopButton.IsEnabled = false;
        PlayIcon.Glyph = "\uE768";
        try { TextEditor.Select(TextEditor.Text?.Length ?? 0, 0); } catch { }
        if (StatusText.Text.StartsWith("● 正在") || StatusText.Text.StartsWith("● 已"))
            StatusText.Text = "● 准备就绪";
    }

    // ---------- 分句 ----------

    private static readonly Regex CitationPattern = new(
        @"\[\d+(?:[–,\-]\d+)*(?:\s*,\s*\d+(?:[–,\-]\d+)*)*\]|https?://\S+|www\.\S+|[\w.+-]+@[\w-]+\.[\w.]+",
        RegexOptions.Compiled);

    private static readonly Regex ReferenceHeader = new(
        @"^\s*(参考文献|references?|bibliography|引用文献)\s*[:：]?\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// 把整段文字切成朗读用的句子，过滤引用角标、URL、邮箱；
    /// 遇到"参考文献"标题后整段跳过。
    /// </summary>
    private static List<SpokenSentence> SplitSentences(string text)
    {
        var result = new List<SpokenSentence>();
        if (string.IsNullOrEmpty(text)) return result;

        // 先按换行粗切，避免把跨段长句硬连
        var lines = text.Split('\n');
        int pos = 0;
        bool inReference = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');
            if (ReferenceHeader.IsMatch(line)) { inReference = true; pos += line.Length + 1; continue; }
            if (inReference) { pos += line.Length + 1; continue; }

            // 逐句切：。！？；!?; 以及省略号结尾
            int start = 0;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                bool endOfSentence = c is '。' or '！' or '？' or '；' or '!' or '?' or ';';
                if (!endOfSentence) continue;

                int len = i - start + 1; // 含句末标点
                AddSentence(result, line, start, len, pos + start, pos + i + 1);
                start = i + 1;
            }
            // 行尾剩余（换行前最后一个不完整句）
            if (start < line.Length)
                AddSentence(result, line, start, line.Length - start, pos + start, pos + line.Length);

            pos += line.Length + 1;
        }

        return result;
    }

    private static void AddSentence(List<SpokenSentence> list, string line, int relStart, int len, int absStart, int absEnd)
    {
        var raw = line.Substring(relStart, len).Trim();
        if (string.IsNullOrWhiteSpace(raw)) return;
        // 去掉引用角标 / URL / 邮箱
        var cleaned = CitationPattern.Replace(raw, "").Trim();
        // 清理多余空白
        cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\s+", " ");
        if (cleaned.Length < 2) return;
        list.Add(new SpokenSentence(cleaned, absStart, absEnd));
    }

    private async Task ShowDialogAsync(string title, string content)
    {
        var dlg = new ContentDialog
        {
            Title = title,
            Content = content,
            CloseButtonText = "知道了",
            XamlRoot = XamlRoot,
        };
        await dlg.ShowAsync();
    }
}
