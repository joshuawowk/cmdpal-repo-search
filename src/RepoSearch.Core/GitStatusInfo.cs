using System.Text;

namespace RepoSearch.Core;

/// <summary>
/// A repo's working-tree state, shaped to render the way posh-git's prompt does:
///
///     [main ≡ +1 ~2 -0 | +0 ~3 -1 !]
///      |    |  |          |          |
///      |    |  index       working    untracked present
///      |    upstream divergence
///      branch
/// </summary>
public sealed record GitStatusInfo
{
    public string? Branch { get; init; }
    public string? Upstream { get; init; }
    public bool Detached { get; init; }

    public int Ahead { get; init; }
    public int Behind { get; init; }

    // Index (staged) counts
    public int IndexAdded { get; init; }
    public int IndexModified { get; init; }
    public int IndexDeleted { get; init; }

    // Working tree (unstaged) counts
    public int WorkingAdded { get; init; }
    public int WorkingModified { get; init; }
    public int WorkingDeleted { get; init; }

    public int Untracked { get; init; }
    public int Conflicts { get; init; }
    public int Stashes { get; init; }

    /// <summary>True when the untracked walk was skipped (-uno), so Untracked is unknown rather than zero.</summary>
    public bool UntrackedUnknown { get; init; }

    /// <summary>Set when status could not be computed (timeout, git missing, not a repo).</summary>
    public string? Error { get; init; }

    public bool HasUpstream => !string.IsNullOrEmpty(Upstream);

    public int IndexTotal => IndexAdded + IndexModified + IndexDeleted + Conflicts;

    /// <summary>
    /// Working-tree "added" as posh-git counts it: untracked files land in this bucket
    /// (GitUtils.ps1 maps both '?' and 'A' to filesAdded), not in a separate marker.
    /// </summary>
    public int WorkingAddedDisplay => WorkingAdded + Untracked;

    public int WorkingTotal => WorkingAddedDisplay + WorkingModified + WorkingDeleted + Conflicts;

    public bool HasIndex => IndexAdded + IndexModified + IndexDeleted > 0;
    public bool HasWorking => WorkingTotal > 0;
    public bool IsClean => !HasIndex && !HasWorking;

    /// <summary>
    /// Changes to TRACKED files only — untracked files deliberately excluded.
    ///
    /// This is the right notion of "dirty" for deciding whether a sync is safe: git will not
    /// touch untracked files during a fast-forward or a push, so a folder full of build output
    /// must not block one. It differs from <see cref="HasWorking"/>, which follows posh-git and
    /// counts untracked files for display.
    /// </summary>
    public bool HasTrackedChanges =>
        IndexAdded + IndexModified + IndexDeleted +
        WorkingAdded + WorkingModified + WorkingDeleted + Conflicts > 0;

    // posh-git's documented defaults (PoshGitTypes.ps1).
    public const string AheadSymbol = "↑";        // up arrow
    public const string BehindSymbol = "↓";       // down arrow
    public const string IdenticalSymbol = "≡";    // triple bar
    public const string WorkingSymbol = "!";           // LocalWorkingStatusSymbol
    public const string StagedSymbol = "~";            // LocalStagedStatusSymbol

    /// <summary>
    /// The upstream-divergence marker on its own.
    ///
    /// Matches posh-git's default BranchBehindAndAheadDisplay of Full, which renders a
    /// diverged branch BEHIND-FIRST as "down2 up1" and does not use the up/down glyph.
    /// A branch with no upstream renders as nothing at all (BranchUntrackedText is '').
    /// </summary>
    public string DivergenceText
    {
        get
        {
            if (Detached || !HasUpstream) return string.Empty;
            if (Behind == 0 && Ahead == 0) return IdenticalSymbol;
            if (Behind >= 1 && Ahead >= 1) return $"{BehindSymbol}{Behind} {AheadSymbol}{Ahead}";
            if (Behind >= 1) return $"{BehindSymbol}{Behind}";
            return $"{AheadSymbol}{Ahead}";
        }
    }

    /// <summary>
    /// The trailing local-state marker: '!' when the working tree is dirty, otherwise '~'
    /// when only the index is, otherwise nothing. Note this is NOT an untracked indicator -
    /// untracked files are already counted in the working "+" column.
    /// </summary>
    public string LocalStatusSymbol =>
        HasWorking ? WorkingSymbol : HasIndex ? StagedSymbol : string.Empty;

    /// <summary>
    /// The full posh-git style summary, e.g. "[main down1 up2 +1 ~2 -0 | +0 ~3 -1 !]".
    /// <paramref name="includeBrackets"/> off is handy when rendering into a CmdPal tag.
    /// </summary>
    public string Format(bool includeBrackets = true)
    {
        if (Error is not null) return includeBrackets ? $"[{Error}]" : Error;

        var sb = new StringBuilder();
        if (includeBrackets) sb.Append('[');

        sb.Append(Detached ? "(detached)" : Branch ?? "?");

        var divergence = DivergenceText;
        if (divergence.Length > 0) sb.Append(' ').Append(divergence);

        sb.Append(' ').Append('+').Append(IndexAdded)
          .Append(" ~").Append(IndexModified)
          .Append(" -").Append(IndexDeleted);

        sb.Append(" | ")
          .Append('+').Append(WorkingAddedDisplay)
          .Append(" ~").Append(WorkingModified)
          .Append(" -").Append(WorkingDeleted);

        var local = LocalStatusSymbol;
        if (local.Length > 0) sb.Append(' ').Append(local);

        // Not a posh-git default, but the stash count is genuinely useful in a list row
        // and posh-git shows it too when EnableStashStatus is on.
        if (Stashes > 0) sb.Append(" $").Append(Stashes);

        if (includeBrackets) sb.Append(']');
        return sb.ToString();
    }

    public override string ToString() => Format();
}
