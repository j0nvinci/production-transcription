using ProductionTranscription.App.Models;
namespace ProductionTranscription.App.Services;
public interface ITranscriptionSession : IDisposable
{
    event Action<string>? Diagnostic;
    event Action<string, string>? PartialRecognized;
    void Submit(SourceConfig source, float[] samples, int sampleRate);
}
