using NAudio.CoreAudioApi;
using NAudio.Wave;
using ProductionTranscription.App.Models;

namespace ProductionTranscription.App.Services;

public sealed record AudioDeviceInfo(string Id, string Name);

public static class AudioDeviceDiscovery
{
    // Opening an ASIO driver may be exclusive. Probe only at the operator's request.
    public static int GetInputChannelCount(AudioBackend backend, string deviceId)
    {
        if (backend == AudioBackend.Asio)
        {
            using var asio = new AsioOut(deviceId);
            return asio.DriverInputChannelCount;
        }
        using var enumerator = new MMDeviceEnumerator();
        using var endpoint = enumerator.GetDevice(deviceId);
        return endpoint.AudioClient.MixFormat.Channels;
    }

    public static List<AudioDeviceInfo> GetDevices(AudioBackend backend)
    {
        if (backend == AudioBackend.Asio)
            return AsioOut.GetDriverNames()
                .Select(name => new AudioDeviceInfo(name, name))
                .ToList();

        using var enumerator = new MMDeviceEnumerator();
        return enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
            .Select(device => new AudioDeviceInfo(device.ID, device.FriendlyName))
            .ToList();
    }
}
