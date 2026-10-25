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
    public bool DetectClickTempo { get; set; }
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
    public bool WebEnabled { get; set; }
    public int WebPort { get; set; } = 8765;
    public string WebBindAddress { get; set; } = "127.0.0.1";
    public bool WebReadWrite { get; set; }
    public string WebAccessToken { get; set; } = ""; // Legacy setting; no longer used.
    public string WebPassword { get; set; } = "";
    public int DesktopTranscriptFontSize { get; set; } = 16;
    public int WebTranscriptFontSize { get; set; } = 16;
    public bool DesktopNewestFirst { get; set; }
    public bool WebNewestFirst { get; set; }
    public bool AutoScrollEnabled { get; set; } = true;
    public bool KeywordHighlightEnabled { get; set; }
    public List<string> HighlightKeywords { get; set; } = new();
    public bool SpeechActivityEnabled { get; set; } = true;
    public bool TimestampsEnabled { get; set; } = true;
    public bool PinningEnabled { get; set; } = true;
    public bool SourceEmphasisEnabled { get; set; }
    public bool KioskAvailable { get; set; } = true;
    public bool HealthIndicatorsEnabled { get; set; } = true;
    public bool VisibilityPresetsEnabled { get; set; } = true;
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
