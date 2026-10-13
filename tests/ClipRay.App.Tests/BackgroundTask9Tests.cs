#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClipRay.PreparedCli;

internal static class BackgroundTask9Tests
{
    internal const string FailureSentinelName = "failure-sentinel";
    internal const string FailureSentinelMarker = "TASK9_REGISTERED_INTENTIONAL_FAILURE";

    // This is the actual new Task9 registration, shared by parsing, collect, default selection and Run.
    internal static RegisteredCase[] CreateCases(Func<Program.ActualCliCatalog> catalog) => new[]
    {
        new RegisteredCase("original-grammar", () => OriginalGrammarOracle(catalog())),
        new RegisteredCase("actual-registries", () => ActualRegistries(catalog())),
        new RegisteredCase("invalid-zero-effects", () => InvalidInputsHaveNoEffects(catalog())),
        new RegisteredCase("pure-entries", () => PureActualEntries(catalog())),
        new RegisteredCase("registered-failure", () => RegisteredFailureContract(catalog())),
        new RegisteredCase("actual-collect", () => ActualCollectionContract(catalog())),
        new RegisteredCase(FailureSentinelName,
            () => throw new InvalidOperationException(FailureSentinelMarker), IncludeByDefault: false)
    };

    internal static int Run(Program.ActualCliCatalog catalog, IReadOnlyList<RegisteredCase> selected,
        TextWriter output, TextWriter error)
    {
        var actual = catalog.Require("background-task-9");
        if (selected.Count == 0 || selected.Any(item => !actual.Cases.Any(c => ReferenceEquals(c, item)))
            || selected.Distinct().Count() != selected.Count) return 2;
        var failed = 0;
        foreach (var test in selected)
        {
            try { test.Run(); output.WriteLine("PASS task-9/" + test.Name); }
            catch (Exception failure)
            {
                failed++;
                error.WriteLine("FAIL task-9/" + test.Name + ": " + failure.GetType().Name + ": " + failure.Message);
            }
        }
        output.WriteLine($"TASK_9_BACKGROUND {selected.Count - failed}/{selected.Count} passed");
        return failed == 0 ? 0 : 1;
    }

    private static void OriginalGrammarOracle(Program.ActualCliCatalog catalog)
    {
        Check(PreparedGrammarOracleData.Cases.Count == 1092, "Frozen original grammar DATA count drifted.");
        var prior = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            foreach (var row in PreparedGrammarOracleData.Cases)
            {
                // NEVER execute the selected original native/evidence delegate in this loop.
                var valid = StrictCli.TryParse(row.Args, catalog.Commands, out var invocation, out var diagnostic);
                Check(valid == row.ExpectedValid, row.Id + ": " + diagnostic);
                if (valid)
                {
                    Check(invocation is not null && invocation.Command.Name == row.ExpectedPrimary, row.Id + ": wrong primary");
                    Check(invocation!.CanonicalArgs[0] == row.ExpectedPrimary, row.Id + ": alias mode changed");
                }
                else Check(invocation is null, row.Id + ": rejected input delivered an invocation");
            }
        }
        finally { CultureInfo.CurrentCulture = prior; }
        // Newly added safe syntax is validated only; legacy-all and Stage7 bodies are not executed here.
        foreach (var primary in new[] { "--background-task-7-tests", "--background-task-7-revision-tests", "--collect", "--legacy-all-tests" })
        {
            Check(StrictCli.TryParse(new[] { primary }, catalog.Commands, out _, out _), "Missing explicit command " + primary);
            Check(!StrictCli.TryParse(new[] { primary, "extra" }, catalog.Commands, out _, out _), "Extra value accepted by " + primary);
        }
    }

    private static void ActualRegistries(Program.ActualCliCatalog catalog)
    {
        foreach (var pair in catalog.ActualFactories)
        {
            var current = pair.Value();
            var bound = catalog.Require(pair.Key).Cases;
            Check(current.Length == bound.Count, pair.Key + ": factory count drift");
            for (var i = 0; i < current.Length; i++)
                Check(current[i].Name == bound[i].Name && current[i].IncludeByDefault == bound[i].IncludeByDefault
                    && current[i].Run.Method == bound[i].Run.Method, pair.Key + ": actual delegate metadata differs");
        }
        var stage8 = catalog.Require("background-task-8").Cases.Take(2).ToArray();
        Check(stage8.Length == 2, "Stage8 actual metadata lacks a two-case positive control");
        var comma = string.Join(",", stage8.Select(c => c.Name));
        Check(StrictCli.TryParse(new[] { "--background-task-8-tests", "--case", comma }, catalog.Commands, out var eighth, out _)
            && eighth!.SelectedCases.SequenceEqual(stage8), "Stage8 actual comma selector changed");
        Check(StrictCli.TryParse(new[] { "--background-task-8-tests", "--case", string.Join("|", stage8.Select(c => c.Name)) }, catalog.Commands, out var eighthPipe, out _)
            && eighthPipe!.SelectedCases.SequenceEqual(stage8), "Stage8 actual pipe compatibility changed");
        foreach (var value in new[] { comma + ",unknown-case", stage8[0].Name + "," + stage8[0].Name, ",", stage8[0].Name + "|" + stage8[0].Name })
            Check(!StrictCli.TryParse(new[] { "--background-task-8-tests", "--case", value }, catalog.Commands, out _, out _), "Invalid Stage8 selector accepted");
        foreach (var command in catalog.Commands.Where(c => c.Grammar.Cases is not null))
        {
            var registry = command.Grammar.Cases!;
            Check(ReferenceEquals(registry, catalog.Require(registry.Id)), command.Name + ": detached registry");
            if (command.Grammar.Kind == GrammarKind.LegacyFilter) continue;
            Check(StrictCli.TryParse(new[] { command.Name }, catalog.Commands, out var defaults, out _), command.Name + ": default parse");
            var expectedDefaults = registry.Cases.Where(c => c.IncludeByDefault).ToArray();
            Check(defaults!.SelectedCases.SequenceEqual(expectedDefaults), command.Name + ": wrong default selection");
            foreach (var actual in registry.Cases)
            {
                var args = command.Grammar.Kind == GrammarKind.CaseOption
                    ? new[] { command.Name, "--case", actual.Name } : new[] { command.Name, actual.Name };
                Check(StrictCli.TryParse(args, catalog.Commands, out var selected, out _), command.Name + ": rejected actual case");
                Check(selected!.SelectedCases.Count == 1 && ReferenceEquals(selected.SelectedCases[0], actual), "Selected a copied case");
            }
        }
    }

    private sealed class FakeResource : IDisposable
    {
        private readonly Action _release;
        internal FakeResource(Action acquire, Action release) { _release = release; acquire(); }
        public void Dispose() => _release();
    }

    private static void InvalidInputsHaveNoEffects(Program.ActualCliCatalog catalog)
    {
        var dispatches = 0; var created = 0; var live = 0;
        // Clone only execution endpoints. Keep the actual catalog's grammar and registry objects.
        var fakeCatalog = catalog.Commands.Select(command => command with { Run = _ =>
        {
            dispatches++;
            using var resource = new FakeResource(() => { created++; live++; }, () => live--);
            return 0;
        }}).ToArray();
        foreach (var row in PreparedGrammarOracleData.Cases.Where(row => !row.ExpectedValid))
        {
            Check(StrictCli.Dispatch(row.Args, fakeCatalog, TextWriter.Null) == 2, row.Id + ": invalid dispatch result");
            Check(dispatches == 0 && created == 0 && live == 0, row.Id + ": invalid input reached delegate/resource");
        }
        foreach (var args in new[] {
            new[] { "--background-task-9-tests", "--unknown" },
            new[] { "--collect", "--collect" }, new[] { "--legacy-all-tests", "--collect" },
            new[] { "--filter" }, new[] { "--filter", "" },
            new[] { "--background-task-1-tests", "--case", "" } })
        {
            Check(StrictCli.Dispatch(args, fakeCatalog, TextWriter.Null) == 2, "Unconsumed/unknown option accepted");
            Check(dispatches == 0 && created == 0 && live == 0, "Rejected input reached fake resource");
        }
        Check(StrictCli.Dispatch(new[] { "--collect" }, fakeCatalog, TextWriter.Null) == 0, "Fake positive control failed");
        Check(dispatches == 1 && created == 1 && live == 0, "Fake counter does not observe accepted invocation");
    }

    private static void PureActualEntries(Program.ActualCliCatalog catalog)
    {
        // These are narrow original source-proven pure leaves, not a whole stage/native suite.
        // Each selected delegate identity is checked before the narrow actual execution.
        RunPure(catalog, new[] { "--background-task-5-tests", "daily-policy" }, "DailyPolicy", "PASS daily-policy");
        RunPure(catalog, new[] { "--background-task-1-tests", "--case", "route-policy" },
            "ExplicitSupportedRouteWinsAndOnlyMissingRouteUsesLiveSettings", "PASS task-1/route-policy");
        RunPure(catalog, new[] { "--filter", "Normalizes Local LMT language names for the model template" },
            "NormalizesLocalLmtLanguageNames", "PASS Normalizes Local LMT language names for the model template");
    }

    private static void RunPure(Program.ActualCliCatalog catalog, string[] args, string expectedMethod, string expectedPass)
    {
        Check(StrictCli.TryParse(args, catalog.Commands, out var invocation, out var diagnostic), diagnostic);
        Check(invocation!.SelectedCases.Count == 1 && invocation.SelectedCases[0].Run.Method.Name == expectedMethod,
            "Pure entry identity drifted; refusing to execute");
        var result = Capture(() => StrictCli.Dispatch(args, catalog.Commands, Console.Error));
        Check(result.Exit == 0 && result.Output.Contains(expectedPass, StringComparison.Ordinal)
            && !result.Error.Contains("FAIL", StringComparison.Ordinal), "Actual narrow entry failed");
    }

    private static void RegisteredFailureContract(Program.ActualCliCatalog catalog)
    {
        var actual = catalog.Require("background-task-9");
        var sentinel = actual.Cases.Single(c => c.Name == FailureSentinelName);
        Check(!sentinel.IncludeByDefault, "Intentional failure entered default execution");
        Check(StrictCli.TryParse(new[] { "--background-task-9-tests" }, catalog.Commands, out var defaults, out _)
            && !defaults!.SelectedCases.Contains(sentinel), "Default parser selected sentinel");
        var explicitArgs = new[] { "--background-task-9-tests", FailureSentinelName };
        Check(StrictCli.TryParse(explicitArgs, catalog.Commands, out var selected, out _)
            && selected!.SelectedCases.Count == 1 && ReferenceEquals(selected.SelectedCases[0], sentinel), "Sentinel is not an actual registered case");
        // Uses the same CommandDescriptor, Run, try/catch and failed counter as ordinary Task9 cases.
        var result = Capture(() => StrictCli.Dispatch(explicitArgs, catalog.Commands, Console.Error));
        Check(result.Exit == 1 && result.Output.Contains("TASK_9_BACKGROUND 0/1 passed", StringComparison.Ordinal)
            && result.Error.Contains("FAIL task-9/" + FailureSentinelName, StringComparison.Ordinal)
            && result.Error.Contains(FailureSentinelMarker, StringComparison.Ordinal), "Registered failure did not reach the ordinary failure counter");
    }

    private static void ActualCollectionContract(Program.ActualCliCatalog catalog)
    {
        var metadata = catalog.CollectMetadata();
        foreach (var registry in metadata.Registries)
        {
            var actual = catalog.Require(registry.Id).Cases;
            Check(registry.Cases.Select(c => c.Name).SequenceEqual(actual.Select(c => c.Name)), "Collect names differ from actual collection");
            Check(registry.Cases.Select(c => c.IncludeByDefault).SequenceEqual(actual.Select(c => c.IncludeByDefault)), "Collect default flags drifted");
        }
        foreach (var command in metadata.Commands)
        {
            var actual = catalog.Commands.Single(c => c.Name == command.Name);
            var registry = catalog.RegistryFor(actual);
            Check(command.RegistryId == registry?.Id, "Collect command/registry binding mismatch");
            Check(command.CaseIds.SequenceEqual(registry is null ? Array.Empty<string>() : registry.Cases.Select(c => registry.Id + "/" + c.Name)),
                "Collect manufactured case IDs");
        }
        var result = Capture(() => StrictCli.Dispatch(new[] { "--collect" }, catalog.Commands, Console.Error));
        Check(result.Exit == 0, "Collect command failed");
        using var json = JsonDocument.Parse(result.Output);
        var root = json.RootElement;
        var canonical = root.GetProperty("inventory").GetRawText();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        Check(root.GetProperty("inventory_sha256").GetString() == hash, "Collect hash does not describe its actual metadata");
        Check(root.GetProperty("command_count").GetInt32() == catalog.Commands.Count, "Collect command count mismatch");
        Check(root.GetProperty("case_count").GetInt32() == catalog.Registries.Values.Sum(r => r.Cases.Count), "Collect case count mismatch");
    }

    private sealed record Captured(int Exit, string Output, string Error);
    private static Captured Capture(Func<int> action)
    {
        var oldOutput = Console.Out; var oldError = Console.Error;
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var error = new StringWriter(CultureInfo.InvariantCulture);
        try { Console.SetOut(output); Console.SetError(error); return new(action(), output.ToString(), error.ToString()); }
        finally { Console.SetOut(oldOutput); Console.SetError(oldError); }
    }
    private static void Check(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
}
