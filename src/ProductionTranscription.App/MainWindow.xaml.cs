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
        }
        catch (Exception ex)
        {
            DeviceBox.ItemsSource = null;
            DeviceBox.IsEnabled = false;
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
        SaveSettings();
    }
    private void SourceChanged(object sender, RoutedEventArgs e) { if (_initialized) SaveSettings(); }
    private void SourceInputLostFocus(object sender, RoutedEventArgs e)
    {
        if (!_initialized) return;
        if (sender is TextBox box && int.TryParse(box.Text, out var input) && input > 0)
            SaveSettings();
        else
            MessageBox.Show("Input numbers must be positive integers.", "Invalid channel",
                MessageBoxButton.OK, MessageBoxImage.Warning);
    }
    private void AddSourceClicked(object sender, RoutedEventArgs e)
    {
        _settings.Sources.Add(new SourceConfig());
        RefreshSources();
    }
    private void RemoveSourceClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: SourceConfig source })
        {
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
