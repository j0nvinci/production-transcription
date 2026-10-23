using ColorConverter = System.Windows.Media.ColorConverter;
using Brushes = System.Windows.Media.Brushes;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace ProductionTranscription.App;

public partial class MainWindow : Window
{
    private readonly SettingsWindow _settingsWindow;
    private readonly Services.WebViewerHost _webViewer = new();
    private readonly Services.TranscriptCensor _censor = new();
    private readonly ObservableCollection<ChatMessage> _messages = new();
    private readonly ObservableCollection<ChannelOption> _channels = new();
    private ICollectionView? _view;
    private string? _selectedChannel;
    private bool _compact;
    private bool _light;
    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private static readonly Brush[] Palette = new Brush[]
    {
        Brushes.DeepSkyBlue, Brushes.MediumSpringGreen, Brushes.Gold,
        Brushes.Orchid, Brushes.Coral, Brushes.Turquoise, Brushes.LightPink
    };
    private readonly Dictionary<string, Brush> _sourceColors = new();
    private readonly Dictionary<string, ChatMessage> _drafts = new();
    public MainWindow()
    {
        InitializeComponent();
        _view = CollectionViewSource.GetDefaultView(_messages);
        _view.Filter = entry => entry is ChatMessage msg && (_selectedChannel is null || msg.Source == _selectedChannel);
        ChatList.ItemsSource = _view;
        ChannelList.ItemsSource = _channels;
        _settingsWindow = new SettingsWindow();
        _settingsWindow.WebSettingsUpdated += config => { _ = RestartWebViewerAsync(); };
        Loaded += async (_, _) => await RestartWebViewerAsync();
        _settingsWindow.SourcesChanged += RefreshChannels;
        RefreshChannels();
        ApplyTheme(_settingsWindow.LightTheme);
        ApplyCompact(_settingsWindow.CompactView);
        ApplyTranscriptPreferences();
        _settingsWindow.SourceColorChanged += (name, hex) =>
        {
            _sourceColors.Remove(name);
            RefreshChannels();
            Brush brush = ColorFor(name);
            foreach (var item in _messages.Where(v => v.Source == name))
                item.Accent = brush;
        };
        _ = _censor.UpdateCommunityListAsync();
        _settingsWindow.PartialTranscriptReceived += (source, words) =>
        {
            if (string.IsNullOrWhiteSpace(words))
            {
                if (_drafts.Remove(source, out var stale)) _messages.Remove(stale);
                return;
            }
            if (!_drafts.TryGetValue(source, out var item))
            {
                item = new ChatMessage(source, words, DateTime.Now.ToString("HH:mm:ss"), ColorFor(source), true, _censor);
                _drafts[source] = item;
                _messages.Add(item);
            }
            else item.Words = words;
            if (_selectedChannel is null || _selectedChannel == source) if (_settingsWindow.WebConfiguration.AutoScrollEnabled) ChatList.ScrollIntoView(item);
        };
        _settingsWindow.TranscriptReceived += (source, words) =>
        {
            words = _censor.Censor(words);
            if (_drafts.Remove(source, out var item))
            {
                item.Words = words;
                item.IsPartial = false;
            }
            else
            {
                item = new ChatMessage(source, words, DateTime.Now.ToString("HH:mm:ss"), ColorFor(source), false, _censor);
                _messages.Add(item);
            }
            while (_messages.Count > 500) _messages.RemoveAt(0);
            if (_settingsWindow.WebConfiguration.AutoScrollEnabled) ChatList.ScrollIntoView(item);
        };
        _settingsWindow.StatusChanged += message => StatusText.Text = message;
        _statusTimer.Tick += (_, _) =>
        {
            StartButton.IsEnabled = !_settingsWindow.IsCapturing;
            StopButton.IsEnabled = _settingsWindow.IsCapturing;
        };
        _statusTimer.Start();
        Closing += (_, _) => { _statusTimer.Stop(); _ = _webViewer.StopAsync(); _settingsWindow.Shutdown(); };
    }
    private object WebSnapshot() => new
    {
        capturing = _settingsWindow.IsCapturing,
        channels = _settingsWindow.Channels.Where(c => c.Enabled).Select(c => c.Name).ToArray(),
        messages = _messages.Select(m => new
        {
            source = m.Source, time = m.Time, words = _censor.Censor(m.Words),
            partial = m.IsPartial, color = _settingsWindow.ColorForSource(m.Source)
        }).ToArray()
    };
    private async Task RestartWebViewerAsync()
    {
        try
        {
            await _webViewer.StartAsync(_settingsWindow.WebConfiguration,
                () => Dispatcher.Invoke(WebSnapshot),
                async start =>
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        if (start) _settingsWindow.StartFromMain();
                        else
                        {
                            _settingsWindow.StopFromMain();
                            foreach (var draft in _drafts.Values.ToArray()) _messages.Remove(draft);
                            _drafts.Clear();
                        }
                    });
                });
            _settingsWindow.SetWebError(_settingsWindow.WebConfiguration.WebEnabled
                ? "Web listener active — authentication follows web password setting" : "Web viewer disabled");
        }
        catch (Exception ex)
        {
            _settingsWindow.SetWebError("Web listener failed: " + ex.Message);
        }
    }
    public sealed record ChannelOption(string Name, string Detail, Brush Accent, string? SourceName, bool Muted)
    {
        public string MuteLabel => Muted ? "Unmute" : "Mute";
    }
    private void MuteChannelClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button || button.Tag is not ChannelOption option ||
            option.SourceName is null) return;
        _settingsWindow.SetMuted(option.SourceName, !option.Muted);
        RefreshChannels();
    }
    private void RefreshChannels()
    {
        _channels.Clear();
        _channels.Add(new ChannelOption("All", "All communications", Brushes.White, null, false));
        foreach (var channel in _settingsWindow.Channels)
            _channels.Add(new ChannelOption(channel.Name, "Input " + channel.Input, ColorFor(channel.Name), channel.Name, _settingsWindow.IsMuted(channel.Name)));
        ChannelList.SelectedItem = _channels.FirstOrDefault(v => v.SourceName == _selectedChannel) ?? _channels[0];
    }
    private void ChannelSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (ChannelList.SelectedItem is not ChannelOption choice) return;
        _selectedChannel = choice.SourceName;
        ViewTitle.Text = choice.SourceName ?? "All communications";
        _view?.Refresh();
    }
    private Brush ColorFor(string source)
    {
        if (!_sourceColors.TryGetValue(source, out var brush))
        {
            string hex = _settingsWindow.ColorForSource(source);
            try { brush = string.IsNullOrEmpty(hex) ? Palette[_sourceColors.Count % Palette.Length] :
                new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!); }
            catch { brush = Palette[_sourceColors.Count % Palette.Length]; }
            _sourceColors[source] = brush;
        }
        return brush;
    }
    private void StartClicked(object sender, RoutedEventArgs e) => _settingsWindow.StartFromMain();
    private void StopClicked(object sender, RoutedEventArgs e)
    {
        _settingsWindow.StopFromMain();
        foreach (var item in _drafts.Values.ToArray()) _messages.Remove(item);
        _drafts.Clear();
    }
    private void ApplyTranscriptPreferences()
    {
        var settings = _settingsWindow.WebConfiguration;
        ChatList.FontSize = Math.Clamp(settings.DesktopTranscriptFontSize, 10, 36);
        _view!.SortDescriptions.Clear();
        _view.SortDescriptions.Add(new SortDescription(nameof(ChatMessage.Sequence),
            settings.DesktopNewestFirst ? ListSortDirection.Descending : ListSortDirection.Ascending));
        OrderButton.Content = settings.DesktopNewestFirst ? "Newest ↑" : "Newest ↓";
    }
    private void FontSmallerClicked(object sender, RoutedEventArgs e) => ChangeFont(-1);
    private void FontLargerClicked(object sender, RoutedEventArgs e) => ChangeFont(1);
    private void ChangeFont(int delta)
    {
        var settings = _settingsWindow.WebConfiguration;
        settings.DesktopTranscriptFontSize = Math.Clamp(settings.DesktopTranscriptFontSize + delta, 10, 36);
        _settingsWindow.SaveTranscriptPreferences();
        ApplyTranscriptPreferences();
    }
    private void OrderClicked(object sender, RoutedEventArgs e)
    {
        var settings = _settingsWindow.WebConfiguration;
        settings.DesktopNewestFirst = !settings.DesktopNewestFirst;
        _settingsWindow.SaveTranscriptPreferences();
        ApplyTranscriptPreferences();
    }
    private void CompactClicked(object sender, RoutedEventArgs e)
    {
        ApplyCompact(!_compact);
        _settingsWindow.CompactView = _compact;
    }
    private void ApplyCompact(bool enabled)
    {
        _compact = enabled;
        SidebarPanel.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        SidebarColumn.Width = new GridLength(enabled ? 0 : 225);
        SubtitleBar.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        ShellGrid.Margin = new Thickness(enabled ? 9 : 22);
        MinHeight = enabled ? 170 : 420;
        Height = enabled ? Math.Min(Height, 290) : Math.Max(Height, 580);
        CompactButton.Content = enabled ? "Full view" : "Compact";
    }
    private void ThemeClicked(object sender, RoutedEventArgs e)
    {
        ApplyTheme(!_light);
        _settingsWindow.LightTheme = _light;
    }
    private void ApplyTheme(bool light)
    {
        _light = light;
        Brush Make(string hex) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);
        Background = Make(light ? "#EEF3F8" : "#0B1019");
        SidebarPanel.Background = Make(light ? "#E5ECF5" : "#151B29");
        SidebarPanel.BorderBrush = Make(light ? "#BCCBDA" : "#2B394B");
        FeedHeading.Background = Make(light ? "#DCE7F2" : "#151D2A");
        FeedPanel.Background = Make(light ? "#FFFFFF" : "#101824");
        FeedPanel.BorderBrush = Make(light ? "#BCCBDA" : "#283749");
        ThemeButton.Content = light ? "Dark" : "Light";
        Application.Current.Resources["MessageText"] = Make(light ? "#17283D" : "#F1F6FD");
        Application.Current.Resources["MessageSecondary"] = Make(light ? "#51667B" : "#899DB5");
        ChatList.Foreground = Make(light ? "#17283D" : "#F2F6FC");
        ChannelList.Foreground = Make(light ? "#17283D" : "#F3F6FD");
        ViewTitle.Foreground = Make(light ? "#17283D" : "#F2F6FC");
    }
    private void SettingsClicked(object sender, RoutedEventArgs e)
    {
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }
    public sealed class ChatMessage : INotifyPropertyChanged
    {
        private static long _nextSequence;
        public long Sequence { get; } = System.Threading.Interlocked.Increment(ref _nextSequence);
        public string Source { get; }
        public string Time { get; }
        private Brush _accent;
        public Brush Accent
        {
            get => _accent;
            set { _accent = value; Changed(); }
        }
        public Services.TranscriptCensor Filter { get; }
        private string _words;
        public string Words
        {
            get => _words;
            set { _words = value; Changed(); }
        }
        private bool _isPartial;
        public bool IsPartial
        {
            get => _isPartial;
            set { _isPartial = value; Changed(); Changed(nameof(State)); }
        }
        public string State => IsPartial ? "Listening…" : "";
        public ChatMessage(string source, string words, string time, Brush accent, bool partial, Services.TranscriptCensor filter)
        {
            Source = source; _words = words; Time = time; _accent = accent; _isPartial = partial; Filter = filter;
        }
        public event PropertyChangedEventHandler? PropertyChanged;
        private void Changed([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}