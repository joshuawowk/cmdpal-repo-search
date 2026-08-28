using System.Diagnostics;
using System.Text;
using RepoSearch.Core;
using RepoSearch.Probe;

Console.OutputEncoding = Encoding.UTF8;

var root = args.Length > 0
    ? args[0]
    : @"C:\Users\jwowk\OneDrive - Getinge AB\Repositories";

// ---------------------------------------------------------------- remote parsing
Console.WriteLine("=== GitRemote.Parse ===");
string[] cases =
[
    "https://github.com/joshuawowk/agc-rbt.git",
    "https://github.com/JoshuaWowk/airbyte.git",
    "https://github.com/joshuawowk/drone-sentinel",
    "git@github.com:joshuawowk/oikos.git",
    "ssh://git@github.com/joshuawowk/oikos.git",
    "https://github.com/espressif/esp-idf.git",
    "https://gitlab.com/some/group/project.git",
    "github.com/owner/repo",
    "https://dev.azure.com/org/proj/_git/repo",
    "not a url",
];
foreach (var c in cases)
{
    var r = GitRemote.Parse(c);
    Console.WriteLine($"  {c,-52} -> host={r?.Host ?? "-",-16} key={r?.Key ?? "-",-32} gh={r?.IsGitHub}");
}

// ---------------------------------------------------------------- discovery
Console.WriteLine($"\n=== Scan: {root} ===");
var sw = Stopwatch.StartNew();
var scanner = new RepoScanner();
var repos = scanner.Scan([root], maxDepth: 3);
sw.Stop();
Console.WriteLine($"  {repos.Count} repos in {sw.ElapsedMilliseconds}ms (no git.exe spawned)\n");

var noRemote = repos.Where(r => !r.HasRemote).ToList();
var gh = repos.Where(r => r.GitHubKey is not null).ToList();
Console.WriteLine($"  with GitHub remote : {gh.Count}");
Console.WriteLine($"  no remote at all   : {noRemote.Count}");
Console.WriteLine($"  non-GitHub remote  : {repos.Count - gh.Count - noRemote.Count}");

// One remote can have several local clones — the pairing model must handle 1:N.
Console.WriteLine("\n  -- remotes with MORE THAN ONE local clone --");
foreach (var g in gh.GroupBy(r => r.GitHubKey!).Where(g => g.Count() > 1))
    Console.WriteLine($"    {g.Key,-46} <- {string.Join(", ", g.Select(r => r.Name))}");

// Folder name != repo name happens, so never match on folder name.
Console.WriteLine("\n  -- folder name differs from remote repo name --");
foreach (var r in gh.Where(r => !string.Equals(r.Name, r.Origin!.Name, StringComparison.OrdinalIgnoreCase)))
    Console.WriteLine($"    {r.Name,-46} -> {r.Origin!.Key}");

// Case mismatch between clone URLs and the API login.
Console.WriteLine("\n  -- distinct remote OWNER spellings --");
foreach (var g in gh.GroupBy(r => r.Origin!.Owner!, StringComparer.Ordinal).OrderBy(g => g.Key))
    Console.WriteLine($"    {g.Key,-24} x{g.Count()}");

Console.WriteLine("\n  -- first 12 repos --");
foreach (var r in repos.Take(12))
    Console.WriteLine($"    {r.Name,-34} {r.DisplayHead,-22} {r.Origin?.Key ?? "(no remote)"}");

// ---------------------------------------------------------------- status
Console.WriteLine("\n=== GitStatusReader (3 repos, untracked=no) ===");
var reader = new GitStatusReader();
foreach (var r in repos.Take(3))
{
    var t = Stopwatch.StartNew();
    var st = await reader.ReadAsync(r.Path, UntrackedMode.None, TimeSpan.FromSeconds(20));
    t.Stop();
    Console.WriteLine($"  {r.Name,-34} {st.Format(),-46} {t.ElapsedMilliseconds}ms");
}

// ---------------------------------------------------------------- parser unit checks
Console.WriteLine("\n=== ParsePorcelainV2 unit checks ===");
const string sample = """
# branch.oid 1111111111111111111111111111111111111111
# branch.head main
# branch.upstream origin/main
# branch.ab +2 -1
1 M. N... 100644 100644 100644 aaa bbb staged-modified.txt
1 A. N... 100644 100644 100644 aaa bbb staged-added.txt
1 D. N... 100644 100644 100644 aaa bbb staged-deleted.txt
1 .M N... 100644 100644 100644 aaa bbb working-modified.txt
1 .D N... 100644 100644 100644 aaa bbb working-deleted.txt
1 MM N... 100644 100644 100644 aaa bbb both.txt
2 R. N... 100644 100644 100644 aaa bbb R100 new.txt<old.txt
u UU N... 100644 100644 100644 100644 aaa bbb ccc conflict.txt
? untracked-one.txt
? untracked-two.txt
""";

var p = GitStatusReader.ParsePorcelainV2(sample);
void Check(string label, object actual, object expected)
{
    var okMark = Equals(actual, expected) ? "ok  " : "FAIL";
    Console.WriteLine($"  [{okMark}] {label,-22} actual={actual} expected={expected}");
}
Check("branch", p.Branch!, "main");
Check("upstream", p.Upstream!, "origin/main");
Check("ahead", p.Ahead, 2);
Check("behind", p.Behind, 1);
Check("index added", p.IndexAdded, 1);
Check("index modified", p.IndexModified, 3);   // M., MM, R.
Check("index deleted", p.IndexDeleted, 1);
Check("working added", p.WorkingAdded, 0);
Check("working modified", p.WorkingModified, 2); // .M, MM
Check("working deleted", p.WorkingDeleted, 1);
Check("untracked", p.Untracked, 2);
Check("conflicts", p.Conflicts, 1);

// posh-git semantics, verified against dahlbyk/posh-git source:
//  * untracked files are counted in the WORKING "+" column (GitUtils.ps1 maps '?' -> filesAdded)
//  * a diverged branch renders behind-first, with no up/down glyph (BranchBehindAndAheadDisplay=Full)
//  * the trailing marker is '!' for a dirty working tree, '~' for index-only, else nothing
Check("working +N shown", p.WorkingAddedDisplay, 2);
Check("divergence", p.DivergenceText, "↓1 ↑2");
Check("local symbol", p.LocalStatusSymbol, "!");
Console.WriteLine($"  formatted: {p.Format()}");

var stagedOnly = GitStatusReader.ParsePorcelainV2("""
# branch.oid 1111111111111111111111111111111111111111
# branch.head main
# branch.upstream origin/main
# branch.ab +0 -0
1 M. N... 100644 100644 100644 aaa bbb staged-only.txt
""");
Check("staged-only symbol", stagedOnly.LocalStatusSymbol, "~");
Console.WriteLine($"  staged only : {stagedOnly.Format()}");

var aheadOnly = GitStatusReader.ParsePorcelainV2("""
# branch.oid 1111111111111111111111111111111111111111
# branch.head main
# branch.upstream origin/main
# branch.ab +3 -0
""");
Check("ahead only", aheadOnly.DivergenceText, "↑3");

var behindOnly = GitStatusReader.ParsePorcelainV2("""
# branch.oid 1111111111111111111111111111111111111111
# branch.head main
# branch.upstream origin/main
# branch.ab +0 -4
""");
Check("behind only", behindOnly.DivergenceText, "↓4");

var clean = GitStatusReader.ParsePorcelainV2("""
# branch.oid 1111111111111111111111111111111111111111
# branch.head main
# branch.upstream origin/main
# branch.ab +0 -0
""");
Console.WriteLine($"  clean    : {clean.Format()}   (IsClean={clean.IsClean})");

var noUp = GitStatusReader.ParsePorcelainV2("""
# branch.oid 1111111111111111111111111111111111111111
# branch.head feature/x
""");
Console.WriteLine($"  no upstream: {noUp.Format()}");
Check("no upstream marker", noUp.DivergenceText, "");

SyncPlanTest.Run();

var _tok = Environment.GetEnvironmentVariable("GH_TOKEN");
if (!string.IsNullOrEmpty(_tok)) await RedirectTest.RunAsync(_tok);
await GitHubProbe.RunAsync(root);
