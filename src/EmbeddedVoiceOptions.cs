using System.Runtime.Versioning;

namespace WindowsNaturalVoices;

/// <summary>
/// Configuration for <see cref="EmbeddedVoiceSpeaker"/>, the flagship engine
/// that drives a Windows Natural Voice through Microsoft's own on-device
/// Azure Embedded Speech runtime.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed record EmbeddedVoiceOptions
{
    /// <summary>
    /// Output sample rate in hertz. The on-device HD models are authored at
    /// 24 kHz; other rates are rejected by the runtime.
    /// </summary>
    public int SampleRate { get; init; } = 24_000;

    /// <summary>
    /// Force the high-fidelity HD acoustic model for every utterance by
    /// building a threshold-patched overlay of the voice package. Leaving this
    /// on is strongly recommended: with the package default, short phrases fall
    /// back to a low-fidelity device vocoder that sounds noticeably degraded.
    /// </summary>
    public bool ForceHd { get; init; } = true;

    /// <summary>
    /// The <c>HDVoiceThreshold</c> written into the overlay when
    /// <see cref="ForceHd"/> is set. Zero forces HD for every utterance.
    /// </summary>
    public int HdThreshold { get; init; }

    /// <summary>
    /// Directory under which forced-HD overlays are created. Defaults to a
    /// per-user location under local application data.
    /// </summary>
    public string? OverlayRoot { get; init; }

    /// <summary>
    /// Prefer symbolic links over copies when building the overlay, so the
    /// multi-hundred-megabyte model files are referenced rather than
    /// duplicated. Falls back to copying when symlink creation is denied (for
    /// example without Developer Mode).
    /// </summary>
    public bool PreferSymlink { get; init; } = true;

    /// <summary>
    /// Copy the gated native extension DLLs and UWP VC++ runtimes out of the OS
    /// next to the running application on load, so the Embedded Speech runtime
    /// can be resolved. Disable when the host already deploys them.
    /// </summary>
    public bool StageNativeRuntime { get; init; } = true;
}
