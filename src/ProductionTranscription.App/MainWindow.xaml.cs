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
    private readonly Services.TranscriptCensor _censor = new();
    private readonly ObservableCollection<ChatMessage> _messages = new();
    private readonly ObservableCollection<ChannelOption> _channels = new();
    private ICollectionView? _view;
    private string? _selectedChannel;
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
        _settingsWindow.SourcesChanged += RefreshChannels;
        RefreshChannels();
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
            if (_selectedChannel is null || _selectedChannel == source) ChatList.ScrollIntoView(item);
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
            ChatList.ScrollIntoView(item);
        };
        _settingsWindow.StatusChanged += message => StatusText.Text = message;
        _statusTimer.Tick += (_, _) =>
        {
            StartButton.IsEnabled = !_settingsWindow.IsCapturing;
            StopButton.IsEnabled = _settingsWindow.IsCapturing;
        };
        _statusTimer.Start();
        Closing += (_, _) => { _statusTimer.Stop(); _settingsWindow.Shutdown(); };
    }
    public sealed record ChannelOption(string Name, string Detail, Brush Accent, string? SourceName);
    private void RefreshChannels()
    {
        _channels.Clear();
        _channels.Add(new ChannelOption("All", "All communications", Brushes.White, null));
        foreach (var channel in _settingsWindow.Channels)
            _channels.Add(new ChannelOption(channel.Name, "Input " + channel.Input, ColorFor(channel.Name), channel.Name));
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
    private void SettingsClicked(object sender, RoutedEventArgs e)
    {
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }
    public sealed class ChatMessage : INotifyPropertyChanged
    {
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