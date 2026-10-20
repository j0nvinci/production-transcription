using System.Windows;
using System.Windows.Controls;
using ProductionTranscription.App.Models;
using ProductionTranscription.App.Services;
namespace ProductionTranscription.App;

public partial class MainWindow : Window
{
    private AppSettings _settings = new();
    private bool _initialized;
    private bool _refreshingDevices;
    private int? _detectedChannels;
    public MainWindow()
    {
        InitializeComponent();
        _settings = SettingsStore.Load();
        BackendBox.SelectedIndex = _settings.Backend == AudioBackend.Asio ? 0 : 1;
        ProfileBox.SelectedIndex = (int)_settings.Profile;
        SourcesList.ItemsSource = _settings.Sources;
        _initialized = true;
        RefreshDevices();
        Closing += (_, _) => SaveSettings();
    }
    private void SettingsChanged(object sender, RoutedEventArgs e)
    {
        if (!_initialized) return;
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
    private void RefreshDevicesClicked(object sender, RoutedEventArgs e) => RefreshDevices();
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
        SaveSettings();
    }
    private void DeviceSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized || _refreshingDevices) return;
        if (DeviceBox.SelectedItem is not AudioDeviceInfo selected) return;
        _settings.DeviceName = selected.Id;
        _detectedChannels = null;
        DeviceStatus.Text = "Selected: " + selected.Name;
        SaveSettings();
    }
    private void InspectDeviceClicked(object sender, RoutedEventArgs e)
    {
        if (DeviceBox.SelectedItem is not AudioDeviceInfo selected)
        {
            DeviceStatus.Text = "Select a detected device first.";
            return;
        }
        try
        {
            int channelCount = AudioDeviceDiscovery.GetInputChannelCount(_settings.Backend, selected.Id);
            _detectedChannels = channelCount;
            int[] invalid = _settings.Sources.Where(s => s.Enabled && s.InputNumber > channelCount)
                .Select(s => s.InputNumber).Distinct().ToArray();
            DeviceStatus.Text = $"{selected.Name}: {channelCount} input channel(s) available." +
                (invalid.Length == 0 ? " All enabled input mappings are within range." :
                " Out-of-range configured inputs: " + string.Join(", ", invalid) + ".");
        }
        catch (Exception ex)
        {
            _detectedChannels = null;
            DeviceStatus.Text = "Unable to inspect this device: " + ex.Message +
                ". ASIO drivers may be busy in another application.";
        }
    }
    private void SourceChanged(object sender, RoutedEventArgs e) { if (_initialized) SaveSettings(); }
    private void SourceInputLostFocus(object sender, RoutedEventArgs e)
    {
        if (!_initialized) return;
        if (sender is not TextBox box || box.DataContext is not SourceConfig source) return;
        if (!int.TryParse(box.Text, out var input) || input < 1)
        {
            box.Text = source.InputNumber.ToString();
            MessageBox.Show("Input numbers must be positive integers.", "Invalid channel",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (_detectedChannels.HasValue && input > _detectedChannels.Value)
        {
            box.Text = source.InputNumber.ToString();
            MessageBox.Show($"The inspected device exposes only {_detectedChannels.Value} input channels.",
                "Input outside device range", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        source.InputNumber = input;
        SaveSettings();
    }
    private void AddSourceClicked(object sender, RoutedEventArgs e)
    {
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
        SourcesList.ItemsSource = null;
        SourcesList.ItemsSource = _settings.Sources;
        SaveSettings();
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
