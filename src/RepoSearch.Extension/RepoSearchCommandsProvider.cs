using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using RepoSearch.Extension.Commands;
using RepoSearch.Extension.Pages;

namespace RepoSearch.Extension;

public sealed partial class RepoSearchCommandsProvider : CommandProvider
{
    private readonly SettingsManager _settingsManager = new();
    private readonly RepoSearchService _service;
    private readonly RepoSearchPage _page;
    private readonly ICommandItem[] _commands;
    private readonly IFallbackCommandItem[] _fallbacks;

    public RepoSearchCommandsProvider()
    {
        Id = "repo-search";
        DisplayName = "Repository Search";
        Icon = Glyphs.Brand;

        _service = new RepoSearchService(_settingsManager);
        _page = new RepoSearchPage(_settingsManager, _service);

        var refresh = new ImmediateCommand(
            "Refresh repositories",
            "repo-search.refresh",
            Glyphs.Refresh,
            () =>
            {
                _page.ForceRefresh();
                return Core.OperationResult.Ok("Refreshing repositories...");
            },
            dismissOnSuccess: false);

        _commands =
        [
            new CommandItem(_page)
            {
                Title = DisplayName,
                Subtitle = "Search git repositories locally and on GitHub",
                MoreCommands =
                [
                    new CommandContextItem(refresh),
                    new CommandContextItem(_settingsManager.Settings.SettingsPage),
                ],
            },
        ];

        // Contribute the top matches straight to CmdPal's main list, so repositories are
        // reachable without opening the page first. One coordinator computes the result set
        // once per query and every slot reads from it.
        var coordinator = new FallbackSearchCoordinator(_service, _settingsManager);
        var rowBuilder = new RepoRowBuilder(_settingsManager, _service);

        _fallbacks = new IFallbackCommandItem[RepoFallbackItem.SlotCount];
        for (var slot = 0; slot < RepoFallbackItem.SlotCount; slot++)
            _fallbacks[slot] = new RepoFallbackItem(slot, coordinator, rowBuilder);

        // Surfacing Settings here is what makes CmdPal show this extension in its settings UI.
        Settings = _settingsManager.Settings;

        // A settings change can alter the roots or the token, so drop the caches.
        _settingsManager.SettingsChangedExternal += () => _page.ForceRefresh();
    }

    public override ICommandItem[] TopLevelCommands() => _commands;

    public override IFallbackCommandItem[] FallbackCommands() => _fallbacks;

    public override void Dispose()
    {
        _page.Dispose();
        _service.Dispose();
    }
}
