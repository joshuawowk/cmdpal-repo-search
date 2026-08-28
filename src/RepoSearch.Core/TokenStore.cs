using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace RepoSearch.Core;

public enum TokenSource
{
    None,
    CredentialManager,
    Environment,
    GitHubCli,
}

/// <summary>
/// Resolves the GitHub token, preferring Windows Credential Manager.
///
/// The token is never written to source, to the repo, or to the settings JSON. Credential
/// Manager keeps it encrypted per-user, and the user can inspect or delete it from
/// Control Panel > Credential Manager > Windows Credentials.
///
/// Resolution order: Credential Manager, then GH_TOKEN/GITHUB_TOKEN, then the gh CLI.
/// </summary>
public static class TokenStore
{
    public const string CredentialTarget = "cmdpal-repo-search:github";

    public static (string? Token, TokenSource Source) Resolve()
    {
        if (ReadCredential(CredentialTarget) is { Length: > 0 } stored)
            return (stored, TokenSource.CredentialManager);

        foreach (var name in new[] { "GH_TOKEN", "GITHUB_TOKEN" })
            if (Environment.GetEnvironmentVariable(name) is { Length: > 0 } env)
                return (env, TokenSource.Environment);

        if (TryGitHubCli() is { Length: > 0 } cli)
            return (cli, TokenSource.GitHubCli);

        return (null, TokenSource.None);
    }

    // ------------------------------------------------------------------ Credential Manager

    private const int CRED_TYPE_GENERIC = 1;
    private const int CRED_PERSIST_LOCAL_MACHINE = 2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public int Flags;
        public int Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public long LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, int type, int reservedFlag, out IntPtr credentialPtr);

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref CREDENTIAL credential, int flags);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, int type, int flags);

    [DllImport("advapi32.dll", EntryPoint = "CredFree")]
    private static extern void CredFree(IntPtr buffer);

    public static string? ReadCredential(string target)
    {
        var ptr = IntPtr.Zero;
        try
        {
            if (!CredRead(target, CRED_TYPE_GENERIC, 0, out ptr)) return null;

            var cred = Marshal.PtrToStructure<CREDENTIAL>(ptr);
            if (cred.CredentialBlobSize == 0 || cred.CredentialBlob == IntPtr.Zero) return null;

            var bytes = new byte[cred.CredentialBlobSize];
            Marshal.Copy(cred.CredentialBlob, bytes, 0, cred.CredentialBlobSize);
            return Encoding.Unicode.GetString(bytes);
        }
        catch { return null; }
        finally { if (ptr != IntPtr.Zero) CredFree(ptr); }
    }

    public static bool WriteCredential(string target, string secret, string userName = "github")
    {
        var blob = Encoding.Unicode.GetBytes(secret);
        var blobPtr = Marshal.AllocHGlobal(blob.Length);
        var targetPtr = Marshal.StringToHGlobalUni(target);
        var userPtr = Marshal.StringToHGlobalUni(userName);

        try
        {
            Marshal.Copy(blob, 0, blobPtr, blob.Length);

            var cred = new CREDENTIAL
            {
                Type = CRED_TYPE_GENERIC,
                TargetName = targetPtr,
                CredentialBlobSize = blob.Length,
                CredentialBlob = blobPtr,
                Persist = CRED_PERSIST_LOCAL_MACHINE,
                UserName = userPtr,
            };

            return CredWrite(ref cred, 0);
        }
        catch { return false; }
        finally
        {
            Marshal.FreeHGlobal(blobPtr);
            Marshal.FreeHGlobal(targetPtr);
            Marshal.FreeHGlobal(userPtr);
        }
    }

    public static bool DeleteCredential(string target)
    {
        try { return CredDelete(target, CRED_TYPE_GENERIC, 0); }
        catch { return false; }
    }

    // ------------------------------------------------------------------ gh CLI fallback

    /// <summary>Asks the gh CLI for its token. Useful when the user is already signed in there.</summary>
    private static string? TryGitHubCli()
    {
        try
        {
            var psi = new ProcessStartInfo("gh", "auth token")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var proc = Process.Start(psi);
            if (proc is null) return null;

            var output = proc.StandardOutput.ReadToEnd();
            if (!proc.WaitForExit(5000))
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                return null;
            }

            return proc.ExitCode == 0 ? output.Trim() : null;
        }
        catch { return null; }
    }

    /// <summary>Masks a token for display, e.g. "ghp_…3ezUv".</summary>
    public static string Mask(string? token) =>
        string.IsNullOrEmpty(token) ? "(none)"
        : token.Length <= 10 ? "***"
        : $"{token[..Math.Min(4, token.Length)]}...{token[^5..]}";
}
