using ActivityMonitor.Agent.Recording;
using ActivityMonitor.Core.Options;
using Microsoft.Extensions.Logging.Abstractions;
using ScreenRecorderLib;
using Xunit;

namespace ActivityMonitor.Tests;

/// <summary>Uses endpoint metadata only; these tests never create a recorder or open an audio stream.</summary>
public sealed class RecordingAudioOptionsTests
{
    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    public void DisabledAudio_DoesNotQueryAnyAudioDevices(bool enabled, bool microphone, bool systemAudio)
    {
        var calls = 0;
        var factory = new RecordingAudioOptionsFactory(NullLogger.Instance,
            () => { calls++; return new CaptureAudioSource("default-microphone"); },
            _ => { calls++; return new CaptureAudioSource("selected-microphone"); },
            () => { calls++; return new LoopbackAudioSource("default-speakers"); });

        var audio = factory.Create(new RecordingOptions
        {
            RecordAudio = enabled, RecordMicrophone = microphone, RecordSystemAudio = systemAudio
        });

        Assert.False(audio.IsAudioEnabled);
        Assert.Empty(audio.AudioSources);
        Assert.Equal(0, calls);
    }

    [Fact]
    public void DefaultMicrophone_UsesResolvedWindowsEndpoint_WithoutEnablingSystemAudio()
    {
        var defaultCalls = 0;
        var loopbackCalls = 0;
        var factory = new RecordingAudioOptionsFactory(NullLogger.Instance,
            () => { defaultCalls++; return new CaptureAudioSource("windows-default-microphone"); },
            _ => throw new InvalidOperationException("No specific microphone was selected."),
            () => { loopbackCalls++; return new LoopbackAudioSource("speakers"); });

        var audio = factory.Create(MicrophoneOptions());

        var microphone = Assert.IsType<CaptureAudioSource>(Assert.Single(audio.AudioSources));
        Assert.Equal("windows-default-microphone", microphone.DeviceName);
        Assert.True(microphone.IsAudioCaptureEnabled);
        Assert.True(audio.IsAudioEnabled);
        Assert.Equal(1, defaultCalls);
        Assert.Equal(0, loopbackCalls);
        Assert.Equal(1.0f, audio.MasterVolume);
        Assert.Equal(AudioBitrate.bitrate_128kbps, audio.Bitrate);
    }

    [Fact]
    public void SelectedMicrophone_IsPreserved_AndDoesNotResolveTheDefaultDevice()
    {
        var defaultCalls = 0;
        string? requestedDevice = null;
        var factory = new RecordingAudioOptionsFactory(NullLogger.Instance,
            () => { defaultCalls++; return new CaptureAudioSource("default"); },
            id => { requestedDevice = id; return new CaptureAudioSource(id); });
        var options = MicrophoneOptions();
        options.SelectedMicrophoneDeviceName = "  chosen-microphone  ";
        options.MicrophoneVolume = 1.5f;

        var audio = factory.Create(options);

        var microphone = Assert.IsType<CaptureAudioSource>(Assert.Single(audio.AudioSources));
        Assert.Equal("chosen-microphone", requestedDevice);
        Assert.Equal("chosen-microphone", microphone.DeviceName);
        Assert.Equal(1.5f, microphone.Volume);
        Assert.Equal(0, defaultCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnavailableSelectedMicrophone_FallsBackToResolvedWindowsDefault(bool lookupThrows)
    {
        var factory = new RecordingAudioOptionsFactory(NullLogger.Instance,
            () => new CaptureAudioSource("windows-default"),
            _ => lookupThrows ? throw new InvalidOperationException("Device removed.") : null);
        var options = MicrophoneOptions();
        options.SelectedMicrophoneDeviceName = "disconnected-microphone";

        var audio = factory.Create(options);

        var microphone = Assert.IsType<CaptureAudioSource>(Assert.Single(audio.AudioSources));
        Assert.Equal("windows-default", microphone.DeviceName);
        Assert.True(audio.IsAudioEnabled);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingMicrophone_DoesNotAddAnUnresolvedSource(bool unresolvedSource)
    {
        var factory = new RecordingAudioOptionsFactory(NullLogger.Instance,
            () => unresolvedSource ? new CaptureAudioSource() : null);

        var audio = factory.Create(MicrophoneOptions());

        Assert.False(audio.IsAudioEnabled);
        Assert.Empty(audio.AudioSources);
    }

    [Fact]
    public void MicrophoneFailure_DoesNotDisableRequestedSystemAudio()
    {
        var factory = new RecordingAudioOptionsFactory(NullLogger.Instance,
            () => throw new UnauthorizedAccessException("Microphone access denied."),
            defaultLoopback: () => new LoopbackAudioSource("windows-default-playback"));
        var options = MicrophoneOptions();
        options.RecordSystemAudio = true;

        var audio = factory.Create(options);

        var loopback = Assert.IsType<LoopbackAudioSource>(Assert.Single(audio.AudioSources));
        Assert.Equal("windows-default-playback", loopback.DeviceName);
        Assert.True(audio.IsAudioEnabled);
    }

    [Fact]
    public void SystemAudioFailure_DoesNotDisableRequestedMicrophone()
    {
        var factory = new RecordingAudioOptionsFactory(NullLogger.Instance,
            () => new CaptureAudioSource("microphone"),
            defaultLoopback: () => throw new InvalidOperationException("Playback device removed."));
        var options = MicrophoneOptions();
        options.RecordSystemAudio = true;

        var audio = factory.Create(options);

        Assert.IsType<CaptureAudioSource>(Assert.Single(audio.AudioSources));
        Assert.True(audio.IsAudioEnabled);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingSystemAudio_DoesNotAddAnUnresolvedSource(bool unresolvedSource)
    {
        var factory = new RecordingAudioOptionsFactory(NullLogger.Instance,
            defaultLoopback: () => unresolvedSource ? new LoopbackAudioSource() : null);

        var audio = factory.Create(new RecordingOptions { RecordAudio = true, RecordSystemAudio = true });

        Assert.Empty(audio.AudioSources);
        Assert.False(audio.IsAudioEnabled);
    }

    private static RecordingOptions MicrophoneOptions() => new()
    {
        RecordAudio = true, RecordMicrophone = true, RecordSystemAudio = false
    };
}
