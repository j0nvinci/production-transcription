using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ProductionTranscription.App.Models;

public sealed class SourceMeter : INotifyPropertyChanged
{
    public SourceConfig Source { get; }
    private double _level;
    public double Level
    {
        get => _level;
        set { if (Math.Abs(_level - value) < .01) return; _level = value; OnPropertyChanged(); }
    }
    public SourceMeter(SourceConfig source) => Source = source;
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
