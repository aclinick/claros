using Windows.ApplicationModel;
using Windows.ApplicationModel.AppExtensions;
using Windows.Foundation.Collections;
using Claros.Internal;

namespace Claros;

/// <summary>
/// Enumerates installed Windows Natural Voice packages via the
/// <c>com.microsoft.voice.model.1</c> AppExtension contract that Microsoft
/// declares on every voice package. The catalog raises <see cref="VoicesChanged"/>
/// whenever the OS reports a package install, update, or uninstall so a
/// consuming app can rebuild its voice list without polling.
/// </summary>
public sealed class VoiceCatalog : IDisposable
{
    private const string ExtensionContract = "com.microsoft.voice.model.1";

    private readonly AppExtensionCatalog _catalog;
    private bool _disposed;

    /// <summary>
    /// Opens the Windows <c>AppExtensionCatalog</c> for the neural voice model
    /// contract (<c>com.microsoft.voice.model.1</c>) and subscribes to package
    /// install, update, and removal events so <see cref="VoicesChanged"/> fires
    /// when the set of installed voices changes.
    /// </summary>
    public VoiceCatalog()
    {
        _catalog = AppExtensionCatalog.Open(ExtensionContract);
        _catalog.PackageInstalled += OnCatalogChanged;
        _catalog.PackageUpdated += OnCatalogChanged;
        _catalog.PackageUninstalling += OnCatalogChanged;
        _catalog.PackageUpdating += OnCatalogChanged;
        _catalog.PackageStatusChanged += OnCatalogChanged;
    }

    /// <summary>
    /// Fires when the OS reports a change in installed voice packages.
    /// Handlers should call <see cref="ListVoicesAsync"/> again.
    /// </summary>
    public event EventHandler? VoicesChanged;

    /// <summary>
    /// Return every installed voice that advertises the Natural Voice
    /// AppExtension contract. Never cached; each call queries the OS.
    /// </summary>
    /// <remarks>
    /// A package that cannot describe itself (for example one caught mid-install
    /// or mid-uninstall) is skipped rather than failing the whole enumeration, so
    /// one bad package cannot hide every other installed voice.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the OS query and the per-package reads.</param>
    public async Task<IReadOnlyList<VoiceInfo>> ListVoicesAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var extensions = await _catalog.FindAllAsync().AsTask(cancellationToken)
            .ConfigureAwait(false);
        var result = new List<VoiceInfo>(extensions.Count);
        foreach (var ext in extensions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var voice = await TryBuildVoiceInfoAsync(ext, cancellationToken).ConfigureAwait(false);
            if (voice is not null) result.Add(voice);
        }
        return result;
    }

    private static async Task<VoiceInfo?> TryBuildVoiceInfoAsync(
        AppExtension ext, CancellationToken cancellationToken)
    {
        Package pkg;
        string installedPath;
        try
        {
            pkg = ext.Package;
            installedPath = pkg.InstalledLocation.Path;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The package is not readable right now (mid-install, mid-uninstall,
            // or otherwise in a bad state). Skip this one and keep enumerating.
            return null;
        }

        var locale = await TryReadLocaleAsync(ext, cancellationToken).ConfigureAwait(false);

        var tokensPath = Path.Combine(installedPath, "Tokens.xml");
        var tokens = TokensXmlParser.TryParse(tokensPath);

        var displayName = tokens?.DisplayName ?? ext.DisplayName;

        return new VoiceInfo(
            Id: $"{ext.Id}@{pkg.Id.FamilyName}",
            DisplayName: displayName,
            Locale: locale,
            Gender: tokens?.Gender ?? string.Empty,
            Age: tokens?.Age ?? string.Empty,
            Vendor: tokens?.Vendor ?? pkg.PublisherDisplayName ?? string.Empty,
            Version: tokens?.Version ?? pkg.Id.Version.ToString() ?? string.Empty,
            PackageFamilyName: pkg.Id.FamilyName,
            PackageFullName: pkg.Id.FullName,
            InstalledPath: installedPath);
    }

    // Reads the extension's declared LocaleId. This is awaited rather than
    // blocked on: the previous GetAwaiter().GetResult() was sync-over-async and
    // could deadlock when discovery ran on a UI thread.
    private static async Task<string> TryReadLocaleAsync(
        AppExtension ext, CancellationToken cancellationToken)
    {
        IPropertySet? props;
        try
        {
            props = await ext.GetExtensionPropertiesAsync().AsTask(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return string.Empty;
        }

        if (props is null || !props.TryGetValue("LocaleId", out var value))
        {
            return string.Empty;
        }

        if (value is IPropertySet localeSet
            && localeSet.TryGetValue("#text", out var textObj)
            && textObj is string text)
        {
            return text;
        }

        return value?.ToString() ?? string.Empty;
    }

    private void OnCatalogChanged(AppExtensionCatalog sender, object args) =>
        VoicesChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Unsubscribes from the catalog's package events. Safe to call more than
    /// once. After disposal <see cref="VoicesChanged"/> no longer fires.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _catalog.PackageInstalled -= OnCatalogChanged;
        _catalog.PackageUpdated -= OnCatalogChanged;
        _catalog.PackageUninstalling -= OnCatalogChanged;
        _catalog.PackageUpdating -= OnCatalogChanged;
        _catalog.PackageStatusChanged -= OnCatalogChanged;
    }
}
