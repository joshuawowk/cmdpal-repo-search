using System.Runtime.InteropServices;
using Microsoft.CommandPalette.Extensions;

namespace RepoSearch.Extension;

/// <summary>
/// The COM-activated entry point Command Palette instantiates.
///
/// The GUID below must stay identical to com:Class Id and CreateInstance ClassId in
/// AppxManifest.xml. If they drift, the package still registers and the extension simply
/// never appears, with no error anywhere.
/// </summary>
[Guid("DC569BEC-66EE-4CD8-B76F-DA445CD6FCFB")]
public sealed partial class RepoSearchExtension : IExtension, IDisposable
{
    private readonly ManualResetEvent _extensionDisposedEvent;
    private readonly RepoSearchCommandsProvider _provider = new();

    public RepoSearchExtension(ManualResetEvent extensionDisposedEvent) =>
        _extensionDisposedEvent = extensionDisposedEvent;

    public object? GetProvider(ProviderType providerType) => providerType switch
    {
        ProviderType.Commands => _provider,
        _ => null,
    };

    public void Dispose()
    {
        _provider.Dispose();
        _extensionDisposedEvent.Set();
    }
}
