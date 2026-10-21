using System.Text.Json.Serialization;
namespace ProductionTranscription.App.Models;

public enum AudioBackend { Asio, Wasapi }
public enum RecognitionEngine { Vosk, Whisper, Groq }
public enum TranscriptionProfile { SpeedFirst, Balanced, AccuracyFirst }
public sealed class SourceConfig
{
    public string Name { get; set; } = "Source";
    public int InputNumber { get; set; } = 1;
    public string WasapiDeviceId { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public double GainDb { get; set; } = 0;
    public double GateDbfs { get; set; } = -50;
    public string ColorHex { get; set; } = "";
}
public sealed class AppSettings
{
    public AudioBackend Backend { get; set; } = AudioBackend.Asio;
    public string DeviceName { get; set; } = "";
    public string ModelName { get; set; } = "";
    public RecognitionEngine RecognitionEngine { get; set; } = RecognitionEngine.Vosk;
    public string GroqModel { get; set; } = "whisper-large-v3-turbo";
    public bool LightTheme { get; set; }
    public bool CompactView { get; set; }
    public TranscriptionProfile Profile { get; set; } = TranscriptionProfile.Balanced;
    public List<SourceConfig> Sources { get; set; } = new()
    {
        new() { Name = "MD", InputNumber = 7 },
        new() { Name = "FOH", InputNumber = 3 },
        new() { Name = "Camera Director", InputNumber = 9 },
        new() { Name = "Broadcast", InputNumber = 1 },
    };
}
