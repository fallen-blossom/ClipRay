// Original command delegates are retained; metadata uses the actual stage registrations.
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClipRay.PreparedCli;

internal sealed record ApprovedStage8Binding(
    Func<RegisteredCase[]> CreateActualCases,
    Func<ActualCaseRegistry, CommandDescriptor> CreateActualCommand);

internal sealed record CollectedCase(string Id, string Name, bool IncludeByDefault, string Method);
internal sealed record CollectedRegistry(string Id, IReadOnlyList<CollectedCase> Cases);
internal sealed record CollectedCommand(string Name, string Comparison, string Grammar,
    string SourceBranchId, string? RegistryId, IReadOnlyList<string> CaseIds);
internal sealed record ActualCollection(IReadOnlyList<CollectedCommand> Commands,
    IReadOnlyList<CollectedRegistry> Registries);

internal static partial class Program
{
    private static ApprovedStage8Binding CreateApprovedStage8Binding() => new(
        () => BackgroundTask8Tests.CreateCases().Select(c => new RegisteredCase(c.Name, c.Test)).ToArray(),
        registry => new CommandDescriptor("--background-task-8-tests", StringComparison.Ordinal,
            ArgumentGrammar.CaseOption(registry, ",|"),
            invocation => BackgroundTask8Tests.Run(invocation.CanonicalArgs), "actual-task8-comma-or-pipe-selector"));

    internal static ActualCliCatalog BuildActualCatalog(ApprovedStage8Binding approvedStage8) => new(approvedStage8);

    internal sealed class ActualCliCatalog : IPreparedCaseBindings
    {
        private readonly Dictionary<string, ActualCaseRegistry> _registries = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Func<RegisteredCase[]>> _factories = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _noArgRegistryOwners = new(StringComparer.Ordinal);
        internal IReadOnlyList<CommandDescriptor> Commands { get; }
        internal IReadOnlyDictionary<string, ActualCaseRegistry> Registries => _registries;
        internal IReadOnlyDictionary<string, Func<RegisteredCase[]>> ActualFactories => _factories;

        internal ActualCliCatalog(ApprovedStage8Binding approvedStage8)
        {
            ArgumentNullException.ThrowIfNull(approvedStage8);
            ArgumentNullException.ThrowIfNull(approvedStage8.CreateActualCases);
            ArgumentNullException.ThrowIfNull(approvedStage8.CreateActualCommand);
            // Use the original tuple registrations, not copied name lists.
            AddTuples("background-task-1", () => BackgroundTask1Tests.CreateCases());
            AddTuples("background-task-2", () => BackgroundTask2Tests.CreateCases());
            AddTuples("background-task-3", () => BackgroundTask3Tests.CreateCases());
            AddTuples("background-task-4", () => BackgroundTask4Tests.CreateCases());
            AddTuples("background-task-5", () => BackgroundTask5Tests.CreateCases());
            AddTuples("background-task-6", () => BackgroundTask6Tests.CreateCases());
            AddTuples("background-task-7", () => BackgroundTask7Tests.CreateCases());
            AddTuples("background-task-7-revision", () => BackgroundTask7Tests.CreateRevisionCases());
            AddTuples("legacy-main-tests-array", CreateActualLegacyCases);
            Add("background-task-8", approvedStage8.CreateActualCases);
            Add("background-task-9", () => BackgroundTask9Tests.CreateCases(() => this));
            Add("annotation-tools", CreateAnnotationCases);

            var stage8 = approvedStage8.CreateActualCommand(Require("background-task-8"));
            if (stage8 is null || !ReferenceEquals(stage8.Grammar.Cases, Require("background-task-8")))
                throw new InvalidOperationException("Stage8 must bind its actual registry explicitly.");
            var extras = new List<CommandDescriptor>
            {
                WithNoArgRegistry("--annotation-tools-tests", "annotation-tools",
                    _ => RunActualLegacySelected(Require("annotation-tools").Cases), "annotation-tools-background"),
                WithNoArgRegistry("--background-task-7-tests", "background-task-7", _ => BackgroundTask7Tests.Run(), "0db-main-39"),
                WithNoArgRegistry("--background-task-7-revision-tests", "background-task-7-revision", _ => BackgroundTask7Tests.RunRevision(), "0db-main-38"),
                stage8,
                new("--background-task-9-tests", StringComparison.Ordinal,
                    ArgumentGrammar.BareCases(Require("background-task-9")),
                    invocation => BackgroundTask9Tests.Run(this, invocation.SelectedCases, Console.Out, Console.Error), "task9-registered-runner"),
                new("--collect", StringComparison.Ordinal, ArgumentGrammar.NoArguments(), _ => WriteCollection(Console.Out), "actual-catalog-collection"),
                WithNoArgRegistry("--legacy-all-tests", "legacy-main-tests-array",
                    _ => RunActualLegacySelected(Require("legacy-main-tests-array").Cases.Where(c => c.IncludeByDefault).ToArray()), "explicit-original-legacy-all")
            };
            // The existing generated factory owns each original lambda and grammar together.
            // BackgroundTask1..6 still execute their original Run methods and per-case context handling.
            Commands = CreatePreparedCommandDescriptors(this, RunActualLegacySelected, extras);
        }

        public ActualCaseRegistry Require(string id) => _registries.TryGetValue(id, out var registry)
            ? registry : throw new InvalidOperationException("Missing actual registry: " + id);

        private void AddTuples(string id, Func<(string Name, Action Run)[]> actualFactory) =>
            Add(id, () => actualFactory().Select(item => new RegisteredCase(item.Name, item.Run)).ToArray());

        private void Add(string id, Func<RegisteredCase[]> actualFactory)
        {
            var items = actualFactory() ?? throw new InvalidOperationException("Null case factory: " + id);
            if (items.Length == 0 || items.Any(c => c is null || string.IsNullOrWhiteSpace(c.Name) || c.Run is null)
                || items.Select(c => c.Name).Distinct(StringComparer.Ordinal).Count() != items.Length)
                throw new InvalidOperationException("Invalid actual case factory: " + id);
            _factories.Add(id, actualFactory);
            _registries.Add(id, new(id, Array.AsReadOnly(items)));
        }

        private CommandDescriptor WithNoArgRegistry(string name, string registryId,
            Func<ValidatedInvocation, int> run, string source)
        {
            Require(registryId);
            _noArgRegistryOwners.Add(name, registryId);
            return new(name, StringComparison.Ordinal, ArgumentGrammar.NoArguments(), run, source);
        }

        internal ActualCaseRegistry? RegistryFor(CommandDescriptor command)
        {
            if (command.Grammar.Cases is { } registry) return registry;
            return _noArgRegistryOwners.TryGetValue(command.Name, out var id) ? Require(id) : null;
        }

        internal ActualCollection CollectMetadata()
        {
            var registries = _registries.Values.OrderBy(r => r.Id, StringComparer.Ordinal).Select(registry =>
                new CollectedRegistry(registry.Id, registry.Cases.Select(c => new CollectedCase(
                    registry.Id + "/" + c.Name, c.Name, c.IncludeByDefault,
                    c.Run.Method.DeclaringType?.FullName + "." + c.Run.Method.Name)).ToArray())).ToArray();
            var commands = Commands.OrderBy(c => c.Name, StringComparer.Ordinal).Select(command =>
            {
                var registry = RegistryFor(command);
                return new CollectedCommand(command.Name, command.Comparison.ToString(), command.Grammar.Kind.ToString(),
                    command.SourceBranchId, registry?.Id,
                    registry is null ? Array.Empty<string>() : registry.Cases.Select(c => registry.Id + "/" + c.Name).ToArray());
            }).ToArray();
            return new(commands, registries);
        }

        internal int WriteCollection(TextWriter output)
        {
            // Hash exactly the stable, sorted metadata printed in the same document.
            var inventory = CollectMetadata();
            var canonical = JsonSerializer.Serialize(inventory);
            var sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
            output.WriteLine(JsonSerializer.Serialize(new { schema_version = 1, inventory_sha256 = sha256,
                command_count = inventory.Commands.Count,
                case_count = inventory.Registries.Sum(r => r.Cases.Count), inventory }));
            return 0;
        }
    }
}
