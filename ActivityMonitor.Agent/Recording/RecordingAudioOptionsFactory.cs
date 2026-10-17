using ActivityMonitor.Core.Options;
using Microsoft.Extensions.Logging;
using ScreenRecorderLib;

namespace ActivityMonitor.Agent.Recording;

/// <summary>Builds audio settings without starting a recording or opening an audio stream.</summary>
public sealed class RecordingAudioOptionsFactory
{
    private readonly ILogger _logger;
    private readonly Func<CaptureAudioSource?> _defaultMicrophone;
    private readonly Func<string, CaptureAudioSource?> _selectedMicrophone;
    private readonly Func<LoopbackAudioSource?> _defaultLoopback;

    public RecordingAudioOptionsFactory(
        ILogger logger,
        Func<CaptureAudioSource?>? defaultMicrophone = null,
        Func<string, CaptureAudioSource?>? selectedMicrophone = null,
        Func<LoopbackAudioSource?>? defaultLoopback = null)
    {
        _logger = logger;
        _defaultMicrophone = defaultMicrophone ?? (() => CaptureAudioSource.Default);
        _selectedMicrophone = selectedMicrophone ?? FindMicrophone;
        _defaultLoopback = defaultLoopback ?? (() => LoopbackAudioSource.Default);
    }

    public AudioOptions Create(RecordingOptions options)
    {
        var audio = new AudioOptions { IsAudioEnabled = false };
        if (!options.RecordAudio) return audio;

        if (options.RecordMicrophone)
        {
            try
            {
                var microphone = ResolveMicrophone(options.SelectedMicrophoneDeviceName);
                if (microphone != null && !string.IsNullOrWhiteSpace(microphone.DeviceName))
                {
                    microphone.IsAudioCaptureEnabled = true;
                    microphone.Volume = Math.Clamp(options.MicrophoneVolume, 0.1f, 3.0f);
                    audio.AudioSources.Add(microphone);
                    _logger.LogInformation("Microphone audio source configured on endpoint '{Device}'.", microphone.DeviceName);
                }
                else
                {
                    _logger.LogWarning("No active Windows microphone endpoint is available. Microphone audio will be absent from this segment.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not configure microphone audio. Check the recording device and Windows microphone access for desktop apps.");
            }
        }

        if (options.RecordSystemAudio)
        {
            try
            {
                var loopback = _defaultLoopback();
                if (loopback != null && !string.IsNullOrWhiteSpace(loopback.DeviceName))
                {
                    loopback.IsAudioCaptureEnabled = true;
                    audio.AudioSources.Add(loopback);
                    _logger.LogInformation("System audio source configured on the Windows default playback endpoint '{Device}'.", loopback.DeviceName);
                }
                else
                {
                    _logger.LogWarning("No active Windows playback endpoint is available. System audio will be absent from this segment.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not configure system audio; other recording sources remain enabled.");
            }
        }

        audio.IsAudioEnabled = audio.AudioSources.Count > 0;
        audio.MasterVolume = 1.0f;
        audio.Channels = AudioChannels.Stereo;
        audio.Bitrate = options.AudioBitrateKbps switch
        {
            <= 96 => AudioBitrate.bitrate_96kbps,
            <= 128 => AudioBitrate.bitrate_128kbps,
            <= 160 => AudioBitrate.bitrate_160kbps,
            _ => AudioBitrate.bitrate_192kbps
        };
        _logger.LogInformation("Recorder audio configured with {Count} source(s); audio track enabled: {Enabled}.",
            audio.AudioSources.Count, audio.IsAudioEnabled);
        return audio;
    }

    private CaptureAudioSource? ResolveMicrophone(string? configuredDevice)
    {
        if (!string.IsNullOrWhiteSpace(configuredDevice))
        {
            try
            {
                var selected = _selectedMicrophone(configuredDevice.Trim());
                if (selected != null && !string.IsNullOrWhiteSpace(selected.DeviceName)) return selected;
                _logger.LogWarning("Configured microphone '{Device}' is unavailable; using the Windows default microphone.", configuredDevice);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not resolve the selected microphone; using the Windows default microphone.");
            }
        }

        // The parameterless constructor leaves DeviceName null; Default resolves a real endpoint ID.
        return _defaultMicrophone();
    }

    private static CaptureAudioSource? FindMicrophone(string deviceId)
    {
        var device = Recorder.GetSystemAudioCaptureDevices()?.FirstOrDefault(d =>
            string.Equals(d.DeviceName, deviceId, StringComparison.OrdinalIgnoreCase));
        return device == null ? null : new CaptureAudioSource(device.DeviceName);
    }
}
