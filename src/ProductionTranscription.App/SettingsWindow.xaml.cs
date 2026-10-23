using MessageBox = System.Windows.MessageBox;
using Brushes = System.Windows.Media.Brushes;
using SystemColors = System.Windows.SystemColors;
using ColorConverter = System.Windows.Media.ColorConverter;
using Button = System.Windows.Controls.Button;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Collections.ObjectModel;
using System.Windows.Media;
using ProductionTranscription.App.Models;
using ProductionTranscription.App.Services;
namespace ProductionTranscription.App;

public partial class SettingsWindow : Window
{
    private AppSettings _settings = new();
    private bool _initialized;
    private bool _refreshingDevices;
    private bool _refreshingModels;
    private bool _loadingKey;
    private int? _detectedChannels;
    private readonly AudioCaptureSession _capture = new();
    public ObservableCollection<AudioDeviceInfo> WasapiDevices { get; } = new();
    public AudioDeviceInfo? DefaultWasapiDevice => WasapiDevices.FirstOrDefault(d => d.Id == _settings.DeviceName);
    public bool IsWasapiMode => _settings.Backend == AudioBackend.Wasapi;
    private readonly DispatcherTimer _meterTimer = new() { Interval = TimeSpan.FromMilliseconds(70) };
    private List<SourceMeter> _meters = new();
    private bool _capturing;
    private bool _starting;
    private ITranscriptionSession? _transcription;
    public event Action<string, string>? TranscriptReceived;
    public event Action<string, string>? PartialTranscriptReceived;
    public event Action<string>? StatusChanged;
    public event Action<string, string>? SourceColorChanged;
    public event Action? SourcesChanged;
    public event Action<AppSettings>? WebSettingsUpdated;
    public event Action? OperatorSettingsUpdated;
    public AppSettings WebConfiguration => _settings;
    public void SaveTranscriptPreferences() => SaveSettings();
    public bool LightTheme { get => _settings.LightTheme; set { _settings.LightTheme = value; SaveSettings(); } }
    public bool CompactView { get => _settings.CompactView; set { _settings.CompactView = value; SaveSettings(); } }
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _muted = new();
    public bool IsMuted(string source) => _muted.ContainsKey(source);
    public void SetMuted(string source, bool muted)
    {
        if (muted) _muted[source] = 1;
        else _muted.TryRemove(source, out _);
    }
    public IReadOnlyList<(string Name, int Input, bool Enabled)> Channels => _settings.Sources.Select(v => (v.Name, v.InputNumber, v.Enabled)).ToArray();
    private bool _shuttingDown;
    public SettingsWindow()
    {
        InitializeComponent();
        _settings = SettingsStore.Load();
        BackendBox.SelectedIndex = _settings.Backend == AudioBackend.Asio ? 0 : 1;
        ProfileBox.SelectedIndex = (int)_settings.Profile;
        EngineBox.SelectedIndex = (int)_settings.RecognitionEngine;
        _loadingKey = true;
        GroqKeyBox.Password = GroqKeyStore.Load();
        _loadingKey = false;
        DataContext = this;
        RefreshSourceMeters();
        InitializeWebSettings();
        InitializeOperatorSettings();
        _initialized = true;
        RefreshDevices();
        RefreshModels();
        _capture.CaptureError += message => Dispatcher.BeginInvoke(() =>
        {
            if (_capturing) { StopCapture(); CaptureStatus.Text = "Capture stopped: " + message; }
        });
        _meterTimer.Tick += (_, _) =>
        {
            StatusChanged?.Invoke(CaptureStatus.Text);
            foreach (var meter in _meters)
                meter.Level = _capturing && meter.Source.Enabled ?
                    Math.Max(Math.Min(100, _capture.TakePeak(meter.Source) * Math.Pow(10, meter.Source.GainDb / 20.0) * 100), meter.Level * .65) : 0;
        };
        Closing += (_, e) =>
        {
            if (!_shuttingDown) { e.Cancel = true; Hide(); }
            else { StopCapture(); SaveSettings(); }
        };
    }
    private void InitializeOperatorSettings()
    {
        DesktopFontBox.Text = _settings.DesktopTranscriptFontSize.ToString();
        WebFontBox.Text = _settings.WebTranscriptFontSize.ToString();
        DesktopNewestBox.IsChecked = _settings.DesktopNewestFirst;
        WebNewestBox.IsChecked = _settings.WebNewestFirst;
        AutoScrollBox.IsChecked = _settings.AutoScrollEnabled;
        KeywordBox.IsChecked = _settings.KeywordHighlightEnabled;
        KeywordListBox.Text = string.Join(Environment.NewLine, _settings.HighlightKeywords ?? new());
        SpeechBox.IsChecked = _settings.SpeechActivityEnabled;
        TimestampsBox.IsChecked = _settings.TimestampsEnabled;
        PinningBox.IsChecked = _settings.PinningEnabled;
        EmphasisBox.IsChecked = _settings.SourceEmphasisEnabled;
        KioskBox.IsChecked = _settings.KioskAvailable;
        HealthBox.IsChecked = _settings.HealthIndicatorsEnabled;
        PresetsBox.IsChecked = _settings.VisibilityPresetsEnabled;
    }
    private void OperatorSettingsChanged(object sender, RoutedEventArgs e)
    {
        if (!_initialized) return;
        if (!int.TryParse(DesktopFontBox.Text, out int desktop) || desktop is < 10 or > 36 ||
            !int.TryParse(WebFontBox.Text, out int web) || web is < 10 or > 36)
        {
            MessageBox.Show("Font sizes must be between 10 and 36.", "Invalid font size");
            return;
        }
        _settings.DesktopTranscriptFontSize = desktop;
        _settings.WebTranscriptFontSize = web;
        _settings.DesktopNewestFirst = DesktopNewestBox.IsChecked == true;
        _settings.WebNewestFirst = WebNewestBox.IsChecked == true;
        _settings.AutoScrollEnabled = AutoScrollBox.IsChecked == true;
        _settings.KeywordHighlightEnabled = KeywordBox.IsChecked == true;
        _settings.HighlightKeywords = KeywordListBox.Text.Split(new[] { "\r\n", "\n" },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        _settings.SpeechActivityEnabled = SpeechBox.IsChecked == true;
        _settings.TimestampsEnabled = TimestampsBox.IsChecked == true;
        _settings.PinningEnabled = PinningBox.IsChecked == true;
        _settings.SourceEmphasisEnabled = EmphasisBox.IsChecked == true;
        _settings.KioskAvailable = KioskBox.IsChecked == true;
        _settings.HealthIndicatorsEnabled = HealthBox.IsChecked == true;
        _settings.VisibilityPresetsEnabled = PresetsBox.IsChecked == true;
        SaveSettings();
        OperatorSettingsUpdated?.Invoke();
    }
    private sealed record WebInterface(string Id, string Name);
    private void InitializeWebSettings()
    {
        WebInterfaceBox.ItemsSource = WebViewerHost.BindAddresses()
            .Select(ip => new WebInterface(ip, ip == "0.0.0.0" ? "All network interfaces (0.0.0.0)" :
                ip == "127.0.0.1" ? "Localhost only (127.0.0.1)" : ip)).ToList();
        WebInterfaceBox.SelectedValue = _settings.WebBindAddress;
        if (WebInterfaceBox.SelectedItem is null) WebInterfaceBox.SelectedValue = "127.0.0.1";
        WebPortBox.Text = _settings.WebPort.ToString();
        WebEnabledBox.IsChecked = _settings.WebEnabled;
        WebAccessBox.SelectedIndex = _settings.WebReadWrite ? 1 : 0;
        WebPasswordBox.Text = _settings.WebPassword;
        UpdateWebStatus();
    }
    private void UpdateWebStatus()
    {
        WebStatus.Text = _settings.WebEnabled
            ? $"Listener: http://{(_settings.WebBindAddress == "0.0.0.0" ? "<computer-ip>" : _settings.WebBindAddress)}:{_settings.WebPort}/ — {(_settings.WebPassword.Length == 0 ? "no password" : "password required")}"
            : "Web viewer disabled";
    }
    private void WebSettingsChanged(object sender, RoutedEventArgs e)
    {
        if (!_initialized) return;
        if (!int.TryParse(WebPortBox.Text, out int port) || port is < 1 or > 65535)
        {
            WebStatus.Text = "Port must be between 1 and 65535.";
            return;
        }
        _settings.WebEnabled = WebEnabledBox.IsChecked == true;
        _settings.WebPort = port;
        _settings.WebBindAddress = WebInterfaceBox.SelectedValue as string ?? "127.0.0.1";
        _settings.WebReadWrite = WebAccessBox.SelectedIndex == 1;
        SaveSettings();
        UpdateWebStatus();
        WebSettingsUpdated?.Invoke(_settings);
    }
    private void WebPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (!_initialized || _settings.WebPassword == WebPasswordBox.Text) return;
        _settings.WebPassword = WebPasswordBox.Text;
        SaveSettings();
        UpdateWebStatus();
        WebSettingsUpdated?.Invoke(_settings);
    }
    public void SetWebError(string message) => WebStatus.Text = message;

    private sealed record InstalledModel(string Id, string Name);
    private void GroqKeyChanged(object sender, RoutedEventArgs e)
    {
        if (!_initialized || _loadingKey) return;
        try { GroqKeyStore.Save(GroqKeyBox.Password); }
        catch (Exception ex) { CaptureStatus.Text = "Key storage failed: " + ex.Message; }
    }
    private void EngineChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized || _capturing || _starting) return;
        _settings.RecognitionEngine = EngineBox.SelectedIndex == 2 ? RecognitionEngine.Groq :
            EngineBox.SelectedIndex == 1 ? RecognitionEngine.Whisper : RecognitionEngine.Vosk;
        RefreshModels();
        SaveSettings();
    }
    private void RefreshModelsClicked(object sender, RoutedEventArgs e)
    {
        if (!_capturing && !_starting) RefreshModels();
    }
    private void RefreshModels()
    {
        if (!_initialized || _starting || _capturing) return;
        _refreshingModels = true;
        try
        {
            string root = System.IO.Path.Combine(AppContext.BaseDirectory, "models");
            var options = System.IO.Directory.Exists(root)
                ? System.IO.Directory.GetDirectories(root)
                    .Where(p => System.IO.Directory.Exists(System.IO.Path.Combine(p, "am")) &&
                                System.IO.Directory.Exists(System.IO.Path.Combine(p, "conf")) &&
                                System.IO.Directory.Exists(System.IO.Path.Combine(p, "graph")))
                    .Select(p => new InstalledModel(System.IO.Path.GetFileName(p), "Vosk · " + System.IO.Path.GetFileName(p)))
                    .OrderBy(p => p.Name).ToList()
                : new List<InstalledModel>();
            if (_settings.RecognitionEngine == RecognitionEngine.Whisper)
                options = System.IO.Directory.Exists(root)
                    ? System.IO.Directory.GetFiles(root, "ggml-*.bin")
                        .Select(file => new InstalledModel(System.IO.Path.GetFileName(file), "Whisper · " + System.IO.Path.GetFileName(file)))
                        .OrderBy(item => item.Name).ToList()
                    : new List<InstalledModel>();
            if (_settings.RecognitionEngine == RecognitionEngine.Groq)
                options = new List<InstalledModel> {
                    new("whisper-large-v3-turbo", "Groq Whisper Large V3 Turbo"),
                    new("whisper-large-v3", "Groq Whisper Large V3")
                };
            ModelBox.ItemsSource = options;
            ModelBox.SelectedValue = _settings.RecognitionEngine == RecognitionEngine.Groq ? _settings.GroqModel : _settings.ModelName;
            if (ModelBox.SelectedItem is null && options.Count > 0)
            {
                ModelBox.SelectedItem = options[0];
                _settings.ModelName = options[0].Id;
            }
            ModelBox.IsEnabled = options.Count > 0;
            if (options.Count == 0)
                CaptureStatus.Text = "No models detected for the selected engine in models directory.";
        }
        finally { _refreshingModels = false; }
        SaveSettings();
    }
    private void ModelChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized || _refreshingModels || _capturing || _starting ||
            ModelBox.SelectedItem is not InstalledModel selection) return;
        if (_settings.RecognitionEngine == RecognitionEngine.Groq) _settings.GroqModel = selection.Id;
        else _settings.ModelName = selection.Id;
        SaveSettings();
    }

    private void SettingsChanged(object sender, RoutedEventArgs e)
    {
        if (!_initialized || _capturing) return;
        AudioBackend backend = BackendBox.SelectedIndex == 0 ? AudioBackend.Asio : AudioBackend.Wasapi;
        if (_settings.Backend != backend)
        {
            _settings.Backend = backend;
            _settings.DeviceName = "";
            _detectedChannels = null;
            RefreshDevices();
        }
        _settings.Profile = (TranscriptionProfile)Math.Max(0, ProfileBox.SelectedIndex);
        SaveSettings();
    }
    private void RefreshDevicesClicked(object sender, RoutedEventArgs e) { if (!_capturing) RefreshDevices(); }
    private void RefreshDevices()
    {
        if (!_initialized) return;
        _refreshingDevices = true;
        try
        {
            var devices = AudioDeviceDiscovery.GetDevices(_settings.Backend);
            WasapiDevices.Clear();
            if (IsWasapiMode)
                foreach (var endpoint in devices) WasapiDevices.Add(endpoint);
            DeviceBox.ItemsSource = devices;
            DeviceBox.SelectedValue = _settings.DeviceName;
            // Never silently switch an established mapping to another device.
            if (DeviceBox.SelectedItem is null && string.IsNullOrEmpty(_settings.DeviceName) && devices.Count > 0)
            {
                DeviceBox.SelectedIndex = 0;
                _settings.DeviceName = devices[0].Id;
            }
            DeviceBox.IsEnabled = devices.Count > 0;
            DeviceStatus.Text = devices.Count == 0
                ? $"No {(_settings.Backend == AudioBackend.Asio ? "ASIO drivers" : "active WASAPI recording endpoints")} detected. Use Refresh after connecting a device."
                : $"{devices.Count} device(s) detected. " + (DeviceBox.SelectedItem is null ? "Previously selected device is unavailable; select another." : "Select a device from the dropdown.");
        }
        catch (Exception ex)
        {
            DeviceBox.ItemsSource = null;
            DeviceBox.IsEnabled = false;
            DeviceStatus.Text = "Device enumeration failed: " + ex.Message;
            MessageBox.Show("Audio device enumeration failed: " + ex.Message, "Device discovery",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { _refreshingDevices = false; }
        InspectSelectedDevice();
        SaveSettings();
    }
    private void SourceDeviceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized || _refreshingDevices || _capturing || _starting) return;
        SaveSettings();
        RefreshInputWarnings();
    }
    private void DeviceSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized || _refreshingDevices || _capturing) return;
        if (DeviceBox.SelectedItem is not AudioDeviceInfo selected) return;
        _settings.DeviceName = selected.Id;
        _detectedChannels = null;
        DeviceStatus.Text = "Selected: " + selected.Name;
        InspectSelectedDevice();
        SaveSettings();
    }
    // Query the selected device automatically. No manual Inspect button is required.
    private void InspectSelectedDevice()
    {
        if (_capturing || DeviceBox.SelectedItem is not AudioDeviceInfo selected) return;
        try
        {
            _detectedChannels = AudioDeviceDiscovery.GetInputChannelCount(_settings.Backend, selected.Id);
            DeviceStatus.Text = $"{selected.Name} — {_detectedChannels} input channel(s) detected";
        }
        catch (Exception ex)
        {
            _detectedChannels = null;
            DeviceStatus.Text = $"{selected.Name} — channel count unavailable ({ex.Message}). Inputs will be checked when capture starts.";
        }
        RefreshInputWarnings();
    }

    private string? InputError(string inputText, SourceConfig? source = null)
    {
        if (!int.TryParse(inputText, out int n) || n < 1)
            return "Enter a positive input number, starting at 1.";
        int? count = _detectedChannels;
        if (IsWasapiMode && source is not null)
        {
            string id = string.IsNullOrWhiteSpace(source.WasapiDeviceId)
                ? _settings.DeviceName : source.WasapiDeviceId;
            if (!WasapiDevices.Any(d => d.Id == id))
                return "The selected WASAPI recording device is unavailable.";
            try { count = AudioDeviceDiscovery.GetInputChannelCount(AudioBackend.Wasapi, id); }
            catch (Exception ex) { return "Cannot inspect recording device: " + ex.Message; }
        }
        if (count is int total && n > total)
            return $"This device has {total} inputs. Choose a number from 1 to {total}.";
        return null;
    }

    private void ValidateInput(TextBox box)
    {
        if (!_initialized || box.DataContext is not SourceMeter meter) return;
        meter.ValidationMessage = InputError(box.Text, meter.Source) ?? "";
        box.Foreground = meter.ValidationMessage.Length == 0 ? Brushes.Black : Brushes.DarkRed;
        box.BorderBrush = meter.ValidationMessage.Length == 0 ? SystemColors.ControlDarkBrush : Brushes.IndianRed;
        box.BorderThickness = new Thickness(meter.ValidationMessage.Length == 0 ? 1 : 2);
    }

    private void RefreshInputWarnings()
    {
        foreach (var meter in _meters)
        {
            if (SourcesList.ItemContainerGenerator.ContainerFromItem(meter) is not FrameworkElement container)
                continue;
            foreach (var box in FindInputBoxes(container))
                ValidateInput(box);
        }
    }

    private static IEnumerable<TextBox> FindInputBoxes(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is TextBox box && box.Width == 55) yield return box;
            foreach (var nested in FindInputBoxes(child)) yield return nested;
        }
    }

    private void SourceInputTextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox box && _initialized && !_capturing) ValidateInput(box);
    }

    public string ColorForSource(string name) =>
        _settings.Sources.FirstOrDefault(v => v.Name == name)?.ColorHex ?? "";

    private static bool TryColor(string input, out Color color)
    {
        color = Colors.Transparent;
        if (!System.Text.RegularExpressions.Regex.IsMatch(input, "^#[0-9A-Fa-f]{6}$")) return false;
        try { color = (Color)ColorConverter.ConvertFromString(input)!; return true; }
        catch { return false; }
    }
    private void SetSourceColor(SourceMeter meter, Color color)
    {
        meter.Source.ColorHex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        meter.RefreshColor();
        SaveSettings();
        SourceColorChanged?.Invoke(meter.Source.Name, meter.Source.ColorHex);
    }
    private void PickSourceColorClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: SourceMeter meter }) return;
        using var dialog = new System.Windows.Forms.ColorDialog { FullOpen = true, AnyColor = true };
        if (TryColor(meter.Source.ColorHex, out var initial))
            dialog.Color = System.Drawing.Color.FromArgb(initial.R, initial.G, initial.B);
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            SetSourceColor(meter, Color.FromRgb(dialog.Color.R, dialog.Color.G, dialog.Color.B));
    }
    private void ColorHexLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox box || box.Tag is not SourceMeter meter) return;
        string entered = box.Text.Trim();
        if (TryColor(entered, out var color)) SetSourceColor(meter, color);
        else if (entered.Length == 0)
        {
            meter.Source.ColorHex = "";
            meter.RefreshColor();
            SaveSettings();
            SourceColorChanged?.Invoke(meter.Source.Name, "");
        }
        else CaptureStatus.Text = "Invalid hex color. Use #RRGGBB.";
        box.Text = meter.Source.ColorHex;
    }
    private void GainChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_initialized) SaveSettings();
    }
    private void SourceChanged(object sender, RoutedEventArgs e) { if (_initialized && !_capturing) { SaveSettings(); SourcesChanged?.Invoke(); } }
    private void SourceInputLostFocus(object sender, RoutedEventArgs e)
    {
        if (!_initialized || _capturing || sender is not TextBox box ||
            box.DataContext is not SourceMeter meter) return;
        var error = InputError(box.Text, meter.Source);
        if (error is null && int.TryParse(box.Text, out int n))
        {
            meter.Source.InputNumber = n;
            SaveSettings();
        }
        // Invalid entries stay visible in red; the saved mapping remains unchanged.
        ValidateInput(box);
    }
    private void AddSourceClicked(object sender, RoutedEventArgs e)
    {
        if (_capturing) return;
        int suggested = Enumerable.Range(1, Math.Max(1, _detectedChannels ?? 32))
            .FirstOrDefault(n => _settings.Sources.All(s => s.InputNumber != n));
        _settings.Sources.Add(new SourceConfig
        {
            Name = $"Source {_settings.Sources.Count + 1}",
            InputNumber = suggested > 0 ? suggested : 1
        });
        RefreshSources();
    }
    private void RemoveSourceClicked(object sender, RoutedEventArgs e)
    {
        if (_capturing) return;
        if (sender is Button { Tag: SourceConfig source })
        {
            if (MessageBox.Show($"Remove '{source.Name}' from the source configuration?",
                    "Remove transcription source", MessageBoxButton.YesNo,
                    MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
            _settings.Sources.Remove(source);
            RefreshSources();
        }
    }
    private void RefreshSources()
    {
        RefreshSourceMeters();
        SaveSettings();
        SourcesChanged?.Invoke();
    }
    private SourceConfig? _dragSource;
    private System.Windows.Point _dragStart;

    private void SourceGrabMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_capturing || _starting || sender is not FrameworkElement { Tag: SourceConfig source }) return;
        _dragSource = source;
        _dragStart = e.GetPosition(this);
    }

    private void SourceGrabMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_dragSource is null || e.LeftButton != System.Windows.Input.MouseButtonState.Pressed)
        {
            _dragSource = null;
            return;
        }
        var current = e.GetPosition(this);
        if (Math.Abs(current.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var source = _dragSource;
        _dragSource = null;
        System.Windows.DragDrop.DoDragDrop((DependencyObject)sender,
            new System.Windows.DataObject(typeof(SourceConfig), source),
            System.Windows.DragDropEffects.Move);
    }

    private void SourcesDragOver(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = !_capturing && !_starting && e.Data.GetDataPresent(typeof(SourceConfig))
            ? System.Windows.DragDropEffects.Move : System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private void SourcesDrop(object sender, System.Windows.DragEventArgs e)
    {
        if (_capturing || _starting || e.Data.GetData(typeof(SourceConfig)) is not SourceConfig source) return;
        var targetElement = e.OriginalSource as DependencyObject;
        while (targetElement is not null && targetElement is not FrameworkElement { DataContext: SourceMeter })
            targetElement = System.Windows.Media.VisualTreeHelper.GetParent(targetElement);
        if (targetElement is not FrameworkElement { DataContext: SourceMeter target }) return;
        int from = _settings.Sources.IndexOf(source);
        int to = _settings.Sources.IndexOf(target.Source);
        if (from < 0 || to < 0 || from == to) return;
        _settings.Sources.RemoveAt(from);
        _settings.Sources.Insert(to, source);
        RefreshSourceMeters();
        SaveSettings();
        SourcesChanged?.Invoke();
        e.Handled = true;
    }

    private void RefreshSourceMeters()
    {
        _meters = _settings.Sources.Select(s => new SourceMeter(s)).ToList();
        SourcesList.ItemsSource = _meters;
    }

    public void StartFromMain() => StartCaptureClicked(this, new RoutedEventArgs());
    public void StopFromMain() => StopCapture();
    public void Shutdown() { _shuttingDown = true; Close(); }
    public bool IsCapturing => _capturing || _starting;
    private async void StartCaptureClicked(object sender, RoutedEventArgs e)
    {
        if (_capturing || _starting) return;
        if (DeviceBox.SelectedItem is not AudioDeviceInfo device &&
            (_settings.Backend == AudioBackend.Asio ||
             _settings.Sources.Any(s => s.Enabled && string.IsNullOrWhiteSpace(s.WasapiDeviceId))))
        {
            CaptureStatus.Text = "Select a default audio device or assign one to each enabled source.";
            return;
        }
        string defaultDeviceId = (DeviceBox.SelectedItem as AudioDeviceInfo)?.Id ?? _settings.DeviceName;
        try
        {
            foreach (var meter in _meters)
            {
                if (SourcesList.ItemContainerGenerator.ContainerFromItem(meter) is not FrameworkElement container)
                    continue;
                foreach (var box in FindInputBoxes(container))
                {
                    ValidateInput(box);
                    if (meter.ValidationMessage.Length == 0) continue;
                    box.Focus();
                    CaptureStatus.Text = "Correct the highlighted source input before starting capture.";
                    return;
                }
            }
            if (ModelBox.SelectedItem is not InstalledModel chosen)
            {
                CaptureStatus.Text = "Select an installed model in Settings.";
                StatusChanged?.Invoke(CaptureStatus.Text);
                return;
            }
            string modelPath = System.IO.Path.Combine(AppContext.BaseDirectory, "models", chosen.Id);
            if (_settings.RecognitionEngine != RecognitionEngine.Groq &&
                !(_settings.RecognitionEngine == RecognitionEngine.Vosk ? System.IO.Directory.Exists(modelPath) : System.IO.File.Exists(modelPath)))
            {
                CaptureStatus.Text = "Selected model is missing; refresh the model list.";
                StatusChanged?.Invoke(CaptureStatus.Text);
                return;
            }
            _starting = true;
            StartCaptureButton.IsEnabled = false;
            BackendBox.IsEnabled = false;
            DeviceBox.IsEnabled = false;
            ModelBox.IsEnabled = false;
            EngineBox.IsEnabled = false;
            GroqKeyBox.IsEnabled = false;
            CaptureStatus.Text = _settings.RecognitionEngine == RecognitionEngine.Groq
                ? "Starting Groq cloud transcription; audio will be uploaded…"
                : "Loading offline recognition model: " + System.IO.Path.GetFileName(modelPath) + " …";
            StatusChanged?.Invoke(CaptureStatus.Text);
            var selectedSources = _settings.Sources.Where(s => s.Enabled).ToArray();
            Action<string, string> emit = (source, words) =>
                Dispatcher.BeginInvoke(() => TranscriptReceived?.Invoke(source, words));
            if (_settings.RecognitionEngine == RecognitionEngine.Groq)
            {
                string key = GroqKeyStore.Load();
                if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("Enter your Groq API key in Settings.");
                _transcription = new GroqTranscription(key, chosen.Id, selectedSources, emit);
            }
            else if (_settings.RecognitionEngine == RecognitionEngine.Whisper)
            {
                _transcription = await Task.Run(() => (ITranscriptionSession)new WhisperTranscription(modelPath, selectedSources, emit));
            }
            else
                _transcription = await Task.Run(() => (ITranscriptionSession)new OfflineTranscription(modelPath, selectedSources, emit));
            CaptureStatus.Text = "Model loaded. Starting audio capture…";
            StatusChanged?.Invoke(CaptureStatus.Text);
            _transcription.PartialRecognized += (source, words) => Dispatcher.BeginInvoke(() =>
            {
                if (_capturing) PartialTranscriptReceived?.Invoke(source, words);
            });
            _transcription.Diagnostic += message => Dispatcher.BeginInvoke(() =>
            {
                if (_capturing) CaptureStatus.Text = message;
            });
            _capture.SourceAudioAvailable += OnSourceAudio;
            _capture.Start(_settings.Backend, defaultDeviceId, _settings.Sources);
            _capturing = true;
            _starting = false;
            StartCaptureButton.IsEnabled = false;
            StopCaptureButton.IsEnabled = true;
            BackendBox.IsEnabled = false;
            DeviceBox.IsEnabled = false;
            _meterTimer.Start();
            CaptureStatus.Text = "Capture active • Metering selected physical inputs • No audio stored";
        }
        catch (Exception ex)
        {
            _starting = false;
            StartCaptureButton.IsEnabled = true;
            BackendBox.IsEnabled = true;
            DeviceBox.IsEnabled = true;
            ModelBox.IsEnabled = true;
            EngineBox.IsEnabled = true;
            GroqKeyBox.IsEnabled = true;
            _capture.SourceAudioAvailable -= OnSourceAudio;
            _transcription?.Dispose();
            _transcription = null;
            CaptureStatus.Text = "Unable to start capture: " + ex.Message;
            StatusChanged?.Invoke(CaptureStatus.Text);
        }
    }
    private void OnSourceAudio(SourceConfig source, float[] samples, int sampleRate)
    {
        if (!IsMuted(source.Name)) _transcription?.Submit(source, samples, sampleRate);
    }
    private void StopCaptureClicked(object sender, RoutedEventArgs e) => StopCapture();
    private void StopCapture()
    {
        _meterTimer.Stop();
        _capture.Stop();
        _capture.SourceAudioAvailable -= OnSourceAudio;
        _transcription?.Dispose();
        _transcription = null;
        _capturing = false;
        _starting = false;
        StartCaptureButton.IsEnabled = true;
        StopCaptureButton.IsEnabled = false;
        BackendBox.IsEnabled = true;
        DeviceBox.IsEnabled = true;
        ModelBox.IsEnabled = ModelBox.Items.Count > 0;
        EngineBox.IsEnabled = true;
        GroqKeyBox.IsEnabled = true;
        foreach (var meter in _meters) meter.Level = 0;
        CaptureStatus.Text = "Capture stopped";
    }
    private void SaveSettings()
    {
        if (!_initialized) return;
        try { SettingsStore.Save(_settings); }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Unable to save settings", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
