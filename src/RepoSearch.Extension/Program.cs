using Microsoft.CommandPalette.Extensions;
using Shmuelie.WinRTServer;
using Shmuelie.WinRTServer.CsWinRT;

namespace RepoSearch.Extension;

public static class Program
{
    [MTAThread]
    public static void Main(string[] args)
    {
        // Command Palette activates this exe as an out-of-process COM server. Launched any
        // other way there is nothing to do.
        if (args.Length == 0 || args[0] != "-RegisterProcessAsComServer")
        {
            Console.WriteLine("Not being launched as an extension... exiting.");
            return;
        }

        // ComServer is not IDisposable; it is torn down with Stop() + UnsafeDispose() below.
        var server = new ComServer();
        var extensionDisposedEvent = new ManualResetEvent(false);

        // One instance handed out for every activation, so CmdPal always talks to the same
        // object graph (and therefore the same caches).
        var extensionInstance = new RepoSearchExtension(extensionDisposedEvent);
        server.RegisterClass<RepoSearchExtension, IExtension>(() => extensionInstance);
        server.Start();

        // Block until CmdPal disposes the extension.
        extensionDisposedEvent.WaitOne();

        server.Stop();
        server.UnsafeDispose();
    }
}
