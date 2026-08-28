using RepoSearch.Core;

namespace RepoSearch.Probe;

/// <summary>Table test for the Sync decision, which must never silently lose work.</summary>
public static class SyncPlanTest
{
    public static void Run()
    {
        Console.WriteLine();
        Console.WriteLine("=== SyncPlan decision table ===");

        var pass = 0;
        var fail = 0;

        void Case(string label, GitStatusInfo s, SyncPlan expected, bool allowRebase = false)
        {
            var actual = GitOperations.PlanSync(s, allowRebase);
            var ok = actual == expected;
            if (ok) pass++; else fail++;
            Console.WriteLine($"  [{(ok ? "ok  " : "FAIL")}] {label,-40} -> {actual,-18} {(ok ? "" : $"(expected {expected})")}");
        }

        var upstream = new GitStatusInfo { Branch = "main", Upstream = "origin/main" };

        Case("clean, in sync", upstream, SyncPlan.UpToDate);
        Case("clean, behind 3", upstream with { Behind = 3 }, SyncPlan.Pull);
        Case("clean, ahead 2", upstream with { Ahead = 2 }, SyncPlan.Push);
        Case("clean, diverged", upstream with { Ahead = 2, Behind = 3 }, SyncPlan.BlockedDiverged);
        Case("clean, diverged, rebase ok", upstream with { Ahead = 2, Behind = 3 }, SyncPlan.PullThenPush, allowRebase: true);

        Case("dirty working, behind", upstream with { Behind = 3, WorkingModified = 1 }, SyncPlan.BlockedDirty);
        Case("dirty index, ahead", upstream with { Ahead = 1, IndexAdded = 1 }, SyncPlan.BlockedDirty);

        // Dirty but nothing to transfer should still read as up to date, not blocked.
        Case("dirty, in sync", upstream with { WorkingModified = 5 }, SyncPlan.UpToDate);

        Case("no upstream", new GitStatusInfo { Branch = "feature" }, SyncPlan.BlockedNoUpstream);
        Case("detached HEAD", new GitStatusInfo { Detached = true }, SyncPlan.BlockedDetached);
        Case("merge conflicts", upstream with { Conflicts = 2, Behind = 1 }, SyncPlan.BlockedConflicts);

        // Untracked files alone must NOT block a sync; they are not at risk from ff/push.
        Case("untracked only, behind", upstream with { Behind = 1, Untracked = 9 }, SyncPlan.Pull);

        Console.WriteLine($"  {pass} passed, {fail} failed");
    }
}
