using Microsoft.CommandPalette.Extensions.Toolkit;
using RepoSearch.Core;

namespace RepoSearch.Extension.Commands;

/// <summary>
/// Base for actions that touch the network or the disk (clone, sync, fork, publish).
///
/// Invoke() runs on a background thread but must still return promptly: blocking it freezes
/// the palette. So the work is started unawaited and we return KeepOpen() immediately,
/// reporting progress and the outcome through toasts.
/// </summary>
internal abstract partial class AsyncActionCommand : InvokableCommand
{
    private int _running;

    /// <summary>Message shown the moment the action starts.</summary>
    protected abstract string StartMessage { get; }

    protected abstract Task<OperationResult> RunAsync(IProgress<string> progress, CancellationToken ct);

    /// <summary>Raised after a successful run so the page can refresh the affected row.</summary>
    public event Action<OperationResult>? Completed;

    public override CommandResult Invoke()
    {
        // Guard against a double Enter kicking off two clones.
        if (Interlocked.Exchange(ref _running, 1) == 1)
        {
            new ToastStatusMessage("Already running...").Show();
            return CommandResult.KeepOpen();
        }

        new ToastStatusMessage(StartMessage).Show();

        _ = Task.Run(async () =>
        {
            var progress = new Progress<string>(m => new ToastStatusMessage(m).Show());

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(20));
                var result = await RunAsync(progress, cts.Token).ConfigureAwait(false);

                var text = result.Success
                    ? result.Message
                    : result.Detail is { Length: > 0 } d ? $"{result.Message} - {d}" : result.Message;

                new ToastStatusMessage(text).Show();
                Completed?.Invoke(result);
            }
            catch (OperationCanceledException)
            {
                new ToastStatusMessage($"{Name} timed out").Show();
            }
            catch (Exception ex)
            {
                // An unhandled exception here would take the whole extension process down,
                // which CmdPal surfaces only as the extension vanishing.
                new ToastStatusMessage($"{Name} failed: {ex.Message}").Show();
            }
            finally { Interlocked.Exchange(ref _running, 0); }
        });

        return CommandResult.KeepOpen();
    }
}

/// <summary>Runs a synchronous action (opening Explorer, VS Code, a browser) and toasts failures.</summary>
internal sealed partial class ImmediateCommand : InvokableCommand
{
    private readonly Func<OperationResult> _action;
    private readonly bool _dismissOnSuccess;

    public ImmediateCommand(string name, string id, IconInfo icon, Func<OperationResult> action, bool dismissOnSuccess = true)
    {
        Name = name;
        Id = id;
        Icon = icon;
        _action = action;
        _dismissOnSuccess = dismissOnSuccess;
    }

    public override CommandResult Invoke()
    {
        OperationResult result;
        try { result = _action(); }
        catch (Exception ex) { result = OperationResult.Fail($"{Name} failed", ex.Message); }

        if (result.Success) return _dismissOnSuccess ? CommandResult.Dismiss() : CommandResult.KeepOpen();

        var text = result.Detail is { Length: > 0 } d ? $"{result.Message} - {d}" : result.Message;
        new ToastStatusMessage(text).Show();
        return CommandResult.KeepOpen();
    }
}
