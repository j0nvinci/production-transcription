using System.Windows;
using System.Windows.Controls;
using ProductionTranscription.App.Models;
using ProductionTranscription.App.Services;
namespace ProductionTranscription.App;

public partial class MainWindow : Window
{
    private AppSettings _settings = new();
    private bool _initialized;
    public MainWindow()
    {
        InitializeComponent();
        _settings = SettingsStore.Load();
        BackendBox.SelectedIndex = _settings.Backend == AudioBackend.Asio ? 0 : 1;
        ProfileBox.SelectedIndex = (int)_settings.Profile;
        DeviceBox.Text = _settings.DeviceName;
        SourcesList.ItemsSource = _settings.Sources;
        _initialized = true;
        Closing += (_, _) => SaveSettings();
    }
    private void SettingsChanged(object sender, RoutedEventArgs e)
    {
        if (!_initialized) return;
        _settings.Backend = BackendBox.SelectedIndex == 0 ? AudioBackend.Asio : AudioBackend.Wasapi;
        _settings.Profile = (TranscriptionProfile)Math.Max(0, ProfileBox.SelectedIndex);
        _settings.DeviceName = DeviceBox.Text;
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
