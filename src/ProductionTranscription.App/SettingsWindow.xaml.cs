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
    private int? _detectedChannels;
    private readonly AudioCaptureSession _capture = new();
    private readonly DispatcherTimer _meterTimer = new() { Interval = TimeSpan.FromMilliseconds(70) };
    private List<SourceMeter> _meters = new();
    private bool _capturing;
    private bool _starting;
    private ITranscriptionSession? _transcription;
    public event Action<string, string>? TranscriptReceived;
    public event Action<string, string>? PartialTranscriptReceived;
    public event Action<string>? StatusChanged;
    private bool _shuttingDown;
    public SettingsWindow()
    {
        InitializeComponent();
        _settings = SettingsStore.Load();
        BackendBox.SelectedIndex = _settings.Backend == AudioBackend.Asio ? 0 : 1;
        ProfileBox.SelectedIndex = (int)_settings.Profile;
        EngineBox.SelectedIndex = (int)_settings.RecognitionEngine;
        RefreshSourceMeters();
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
                    Math.Max(Math.Min(100, _capture.TakePeak(meter.Source.InputNumber) * Math.Pow(10, meter.Source.GainDb / 20.0) * 100), meter.Level * .65) : 0;
        };
        Closing += (_, e) =>
        {
            if (!_shuttingDown) { e.Cancel = true; Hide(); }
            else { StopCapture(); SaveSettings(); }
        };
    }
    private sealed record InstalledModel(string Id, string Name);
    private void EngineChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized || _capturing || _starting) return;
        _settings.RecognitionEngine = EngineBox.SelectedIndex == 1 ? RecognitionEngine.Whisper : RecognitionEngine.Vosk;
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
            ModelBox.ItemsSource = options;
            ModelBox.SelectedValue = _settings.ModelName;
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
        _settings.ModelName = selection.Id;
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

    private string? InputError(string inputText)
    {
        if (!int.TryParse(inputText, out int n) || n < 1)
            return "Enter a positive input number, starting at 1.";
        if (_detectedChannels is int total && n > total)
            return $"This device has {total} inputs. Choose a number from 1 to {total}.";
        return null;
    }

    private void ValidateInput(TextBox box)
    {
        if (!_initialized || box.DataContext is not SourceMeter meter) return;
        meter.ValidationMessage = InputError(box.Text) ?? "";
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

    private void GainChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_initialized) SaveSettings();
    }
    private void SourceChanged(object sender, RoutedEventArgs e) { if (_initialized && !_capturing) SaveSettings(); }
    private void SourceInputLostFocus(object sender, RoutedEventArgs e)
    {
        if (!_initialized || _capturing || sender is not TextBox box ||
            box.DataContext is not SourceMeter meter) return;
        var error = InputError(box.Text);
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
        if (_capturing || _starting || DeviceBox.SelectedItem is not AudioDeviceInfo device) return;
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
            if (!(_settings.RecognitionEngine == RecognitionEngine.Vosk ? System.IO.Directory.Exists(modelPath) : System.IO.File.Exists(modelPath)))
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
            CaptureStatus.Text = "Loading offline recognition model: " + System.IO.Path.GetFileName(modelPath) + " …";
            StatusChanged?.Invoke(CaptureStatus.Text);
            var selectedSources = _settings.Sources.Where(s => s.Enabled).ToArray();
            Action<string, string> emit = (source, words) =>
                Dispatcher.BeginInvoke(() => TranscriptReceived?.Invoke(source, words));
            if (_settings.RecognitionEngine == RecognitionEngine.Whisper)
            {
                var cli = System.IO.Path.Combine(AppContext.BaseDirectory, "tools", "whisper-cli.exe");
                _transcription = new WhisperTranscription(cli, modelPath, selectedSources, emit);
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
            _capture.Start(_settings.Backend, device.Id, _settings.Sources);
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
            _capture.SourceAudioAvailable -= OnSourceAudio;
            _transcription?.Dispose();
            _transcription = null;
            CaptureStatus.Text = "Unable to start capture: " + ex.Message;
            StatusChanged?.Invoke(CaptureStatus.Text);
        }
    }
    private void OnSourceAudio(SourceConfig source, float[] samples, int sampleRate) =>
        _transcription?.Submit(source, samples, sampleRate);
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
