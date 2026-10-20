using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace ProductionTranscription.App;

public partial class MainWindow : Window
{
    private readonly SettingsWindow _settingsWindow;
    private readonly ObservableCollection<ChatMessage> _messages = new();
    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private static readonly Brush[] Palette = new Brush[]
    {
        Brushes.DeepSkyBlue, Brushes.MediumSpringGreen, Brushes.Gold,
        Brushes.Orchid, Brushes.Coral, Brushes.Turquoise, Brushes.LightPink
    };
    private readonly Dictionary<string, Brush> _sourceColors = new();
    public MainWindow()
    {
        InitializeComponent();
        ChatList.ItemsSource = _messages;
        _settingsWindow = new SettingsWindow();
        _settingsWindow.TranscriptReceived += (source, words) =>
        {
            if (!_sourceColors.TryGetValue(source, out var accent))
            {
                accent = Palette[_sourceColors.Count % Palette.Length];
                _sourceColors[source] = accent;
            }
            _messages.Add(new ChatMessage(source, words, DateTime.Now.ToString("HH:mm:ss"), accent));
            while (_messages.Count > 500) _messages.RemoveAt(0);
            ChatList.ScrollIntoView(_messages[^1]);
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
    private void StartClicked(object sender, RoutedEventArgs e) => _settingsWindow.StartFromMain();
    private void StopClicked(object sender, RoutedEventArgs e) => _settingsWindow.StopFromMain();
    private void SettingsClicked(object sender, RoutedEventArgs e)
    {
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }
    public sealed record ChatMessage(string Source, string Words, string Time, Brush Accent);
}