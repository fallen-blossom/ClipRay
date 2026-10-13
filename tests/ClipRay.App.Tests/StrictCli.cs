#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace ClipRay.PreparedCli;

internal sealed record RegisteredCase(string Name, Action Run, bool IncludeByDefault = true);

// The caller must supply the actual collection consumed by the existing stage runner.
// Do not construct a second list of case names for this interface.
internal sealed record ActualCaseRegistry(string Id, IReadOnlyList<RegisteredCase> Cases);

internal interface IPreparedCaseBindings
{
    // Metadata only: never initialize WPF/resources, load models, or run a case here.
    // Missing bindings must throw; there is no empty/default registry fallback.
    ActualCaseRegistry Require(string id);
}

internal enum GrammarKind { NoArguments, Positional, CaseOption, BareCases, LegacyFilter }
internal enum ValueKind { NonEmptyString, Int32, PositiveInt32, Enum }
internal sealed record ValueRule(string Name, ValueKind Kind,
    string[]? AllowedValues = null, StringComparison Comparison = StringComparison.Ordinal,
    int? Minimum = null, int? Maximum = null, string ForbiddenCharacters = "");
internal sealed record PositionRule(int ArgvIndex, ValueRule Value);
internal sealed record RepeatedRule(int FirstArgvIndex, ValueRule Value, bool RejectDuplicates = false);
internal sealed record Cardinality(int Minimum, int? Maximum, int[]? AllowedCounts)
{
    public bool Accepts(int count) => count >= Minimum && (Maximum is null || count <= Maximum.Value)
        && (AllowedCounts is null || AllowedCounts.Contains(count));
}

internal sealed record ArgumentGrammar(GrammarKind Kind, Cardinality Count,
    PositionRule[] Positions, RepeatedRule? Repeated = null, ActualCaseRegistry? Cases = null, string CaseSeparators = "|")
{
    public static ArgumentGrammar NoArguments() =>
        new(GrammarKind.NoArguments, new(0, 0, new[] { 0 }), Array.Empty<PositionRule>());
    public static ArgumentGrammar CaseOption(ActualCaseRegistry cases, string separators = "|") =>
        new(GrammarKind.CaseOption, new(0, 0, new[] { 0 }), Array.Empty<PositionRule>(), Cases: cases, CaseSeparators: separators);
    public static ArgumentGrammar BareCases(ActualCaseRegistry cases) =>
        new(GrammarKind.BareCases, new(0, null, null), Array.Empty<PositionRule>(), Cases: cases);
    public static ArgumentGrammar LegacyFilter(ActualCaseRegistry cases) =>
        new(GrammarKind.LegacyFilter, new(1, 1, new[] { 1 }), Array.Empty<PositionRule>(), Cases: cases);
}

internal sealed record CommandDescriptor(string Name, StringComparison Comparison,
    ArgumentGrammar Grammar, Func<ValidatedInvocation, int> Run, string SourceBranchId);
internal sealed record ValidatedInvocation(CommandDescriptor Command, string[] CanonicalArgs,
    IReadOnlyList<RegisteredCase> SelectedCases);

internal static class StrictCli
{
    // Registration errors deliberately throw: no malformed descriptor may become a native fallback.
    // A valid input dispatches exactly the delegate stored beside its grammar.
    public static int Dispatch(string[] args, IReadOnlyList<CommandDescriptor> commands, TextWriter error)
    {
        if (!TryParse(args, commands, out var invocation, out var diagnostic))
        {
            error.WriteLine(diagnostic);
            return 2;
        }
        return invocation!.Command.Run(invocation);
    }

    // This method performs only in-memory checks. It never invokes a command or a case.
    public static bool TryParse(string[] args, IReadOnlyList<CommandDescriptor> commands,
        out ValidatedInvocation? invocation, out string diagnostic)
    {
        invocation = null;
        diagnostic = "";
        if (args is null || args.Length == 0)
            return Fail("Specify one explicit command; no default suite is executed.", out diagnostic);
        ValidateCatalog(commands);
        var command = Find(commands, args[0]);
        if (command is null) return Fail("Unknown primary command.", out diagnostic);
        for (var i = 1; i < args.Length; i++)
        {
            var other = Find(commands, args[i]);
            if (other is not null)
                return Fail(other == command ? "Duplicate primary command." : "Conflicting primary commands.", out diagnostic);
        }
        var grammar = command.Grammar;
        IReadOnlyList<RegisteredCase> selected = Array.Empty<RegisteredCase>();
        var canonical = args.ToArray();
        // Keep this selected alias, never the first name in its shared source branch.
        canonical[0] = command.Name;
        switch (grammar.Kind)
        {
            case GrammarKind.NoArguments:
                if (args.Length != 1) return Fail("This command accepts no arguments or options.", out diagnostic);
                break;
            case GrammarKind.Positional:
                if (!grammar.Count.Accepts(args.Length - 1))
                    return Fail("Incorrect positional argument count.", out diagnostic);
                HashSet<string>? repeatedSeen = grammar.Repeated?.RejectDuplicates == true
                    ? new(StringComparer.Ordinal) : null;
                for (var i = 1; i < args.Length; i++)
                {
                    var position = grammar.Positions.SingleOrDefault(p => p.ArgvIndex == i);
                    var repeated = grammar.Repeated is { } repeat && i >= repeat.FirstArgvIndex;
                    var value = position?.Value ?? (repeated ? grammar.Repeated!.Value : null);
                    if (value is null) throw new InvalidOperationException("Unbound positional slot.");
                    if (!CheckValue(args[i], value, out diagnostic)) return false;
                    if (repeated && repeatedSeen is not null && !repeatedSeen.Add(args[i]))
                        return Fail("Duplicate repeated positional argument.", out diagnostic);
                }
                break;
            case GrammarKind.CaseOption:
            {
                var registry = grammar.Cases!;
                if (args.Length == 1)
                    selected = registry.Cases.Where(c => c.IncludeByDefault).ToArray();
                else
                {
                    // The registered --case grammar explicitly owns its separator.
                    // One option, one value; no token is discarded or searched out of order.
                    if (args.Length != 3 || !string.Equals(args[1], "--case", StringComparison.Ordinal))
                        return Fail("Expected exactly one --case value, or no selector.", out diagnostic);
                    if (!CheckToken(args[2], out diagnostic)) return false;
                    if (!SelectExact(registry, args[2].Split(grammar.CaseSeparators.ToCharArray()), out selected, out diagnostic)) return false;
                }
                if (selected.Count == 0) return Fail("0 cases selected.", out diagnostic);
                // Explicit selection also prevents future opt-in sentinels running through an old all-cases path.
                canonical = new[] { command.Name, "--case", string.Join(grammar.CaseSeparators[0].ToString(), selected.Select(c => c.Name)) };
                break;
            }
            case GrammarKind.BareCases:
            {
                var registry = grammar.Cases!;
                if (args.Length == 1)
                    selected = registry.Cases.Where(c => c.IncludeByDefault).ToArray();
                else if (!SelectExact(registry, args.Skip(1), out selected, out diagnostic)) return false;
                if (selected.Count == 0) return Fail("0 cases selected.", out diagnostic);
                canonical = new[] { command.Name }.Concat(selected.Select(c => c.Name)).ToArray();
                break;
            }
            case GrammarKind.LegacyFilter:
            {
                if (args.Length != 2) return Fail("Expected exactly one --filter value.", out diagnostic);
                if (!CheckToken(args[1], out diagnostic)) return false;
                var registry = grammar.Cases!;
                var terms = args[1].Split('|').Select(term => term.Trim()).ToArray();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var matches = new HashSet<string>(StringComparer.Ordinal);
                foreach (var term in terms)
                {
                    if (!CheckToken(term, out diagnostic)) return false;
                    if (!seen.Add(term)) return Fail("Duplicate filter term.", out diagnostic);
                    var found = registry.Cases.Where(c => c.Name.Contains(term, StringComparison.OrdinalIgnoreCase)).ToArray();
                    if (found.Length == 0) return Fail("Unknown filter term; no cases executed.", out diagnostic);
                    foreach (var item in found) matches.Add(item.Name);
                }
                selected = registry.Cases.Where(c => matches.Contains(c.Name)).ToArray();
                if (selected.Count == 0) return Fail("0 cases selected.", out diagnostic);
                break;
            }
            default: throw new InvalidOperationException("Unsupported grammar kind.");
        }
        invocation = new(command, canonical, selected);
        return true;
    }

    private static bool SelectExact(ActualCaseRegistry registry, IEnumerable<string> names,
        out IReadOnlyList<RegisteredCase> selected, out string diagnostic)
    {
        selected = Array.Empty<RegisteredCase>();
        diagnostic = "";
        var wanted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            if (!CheckToken(name, out diagnostic)) return false;
            if (!wanted.Add(name)) return Fail("Duplicate case selector.", out diagnostic);
            if (!registry.Cases.Any(c => string.Equals(c.Name, name, StringComparison.Ordinal)))
                return Fail("Unknown case selector; no cases executed.", out diagnostic);
        }
        selected = registry.Cases.Where(c => wanted.Contains(c.Name)).ToArray();
        return true;
    }

    private static bool CheckToken(string? value, out string diagnostic)
    {
        diagnostic = "";
        if (string.IsNullOrWhiteSpace(value)) return Fail("Missing or empty argument value.", out diagnostic);
        if (value.StartsWith("--", StringComparison.Ordinal))
            return Fail("Unknown option or missing argument value.", out diagnostic);
        return true;
    }

    private static bool CheckValue(string value, ValueRule rule, out string diagnostic)
    {
        if (!CheckToken(value, out diagnostic)) return false;
        if (rule.ForbiddenCharacters.Any(value.Contains))
            return Fail("Forbidden character in " + rule.Name + ".", out diagnostic);
        switch (rule.Kind)
        {
            case ValueKind.NonEmptyString: return true;
            case ValueKind.Enum:
                return rule.AllowedValues!.Any(allowed => string.Equals(value, allowed, rule.Comparison))
                    || Fail("Invalid value for " + rule.Name + ".", out diagnostic);
            case ValueKind.Int32:
            case ValueKind.PositiveInt32:
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.CurrentCulture, out var number)
                    || (rule.Kind == ValueKind.PositiveInt32 && number <= 0)
                    || (rule.Minimum is int minimum && number < minimum)
                    || (rule.Maximum is int maximum && number > maximum))
                    return Fail("Invalid integer for " + rule.Name + ".", out diagnostic);
                return true;
            default: throw new InvalidOperationException("Unsupported value kind.");
        }
    }

    private static CommandDescriptor? Find(IReadOnlyList<CommandDescriptor> commands, string? name) =>
        commands.FirstOrDefault(command => string.Equals(name, command.Name, command.Comparison));

    public static void ValidateCatalog(IReadOnlyList<CommandDescriptor> commands)
    {
        if (commands is null || commands.Count == 0) throw new InvalidOperationException("No command descriptors.");
        for (var i = 0; i < commands.Count; i++)
        {
            var command = commands[i];
            if (command is null || string.IsNullOrWhiteSpace(command.Name)
                || !command.Name.StartsWith("--", StringComparison.Ordinal) || command.Run is null
                || command.Comparison is not (StringComparison.Ordinal or StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Invalid command descriptor.");
            for (var j = 0; j < i; j++)
                if (string.Equals(command.Name, commands[j].Name, command.Comparison)
                    || string.Equals(command.Name, commands[j].Name, commands[j].Comparison))
                    throw new InvalidOperationException("Duplicate or ambiguous command descriptor.");
            ValidateGrammar(command.Grammar);
        }
    }

    private static void ValidateGrammar(ArgumentGrammar grammar)
    {
        if (grammar is null || !Enum.IsDefined(typeof(GrammarKind), grammar.Kind))
            throw new InvalidOperationException("Missing or unsupported grammar.");
        if (grammar.Kind == GrammarKind.CaseOption
            ? string.IsNullOrEmpty(grammar.CaseSeparators) || grammar.CaseSeparators.Length > 2
                || grammar.CaseSeparators.Any(c => c is not ('|' or ','))
                || grammar.CaseSeparators.Distinct().Count() != grammar.CaseSeparators.Length
            : grammar.CaseSeparators != "|")
            throw new InvalidOperationException("Unsupported or ignored case separators.");
        var count = grammar.Count;
        if (count is null || count.Minimum < 0 || (count.Maximum is int max && max < count.Minimum)
            || (count.AllowedCounts is { } allowed && (allowed.Length == 0 || allowed.Distinct().Count() != allowed.Length
                || allowed.Any(n => n < count.Minimum || (count.Maximum is int limit && n > limit)))))
            throw new InvalidOperationException("Invalid positional cardinality.");
        if (grammar.Positions is null || grammar.Positions.Any(p => p is null || p.ArgvIndex < 1)
            || grammar.Positions.Select(p => p.ArgvIndex).Distinct().Count() != grammar.Positions.Length)
            throw new InvalidOperationException("Invalid positional slots.");
        var needsCases = grammar.Kind is GrammarKind.CaseOption or GrammarKind.BareCases or GrammarKind.LegacyFilter;
        if (needsCases != (grammar.Cases is not null)) throw new InvalidOperationException("Missing or unexpected actual case binding.");
        if (needsCases)
        {
            if (grammar.Positions.Length != 0 || grammar.Repeated is not null)
                throw new InvalidOperationException("Case grammar cannot carry ignored positional rules.");
            ValidateCases(grammar.Cases!, grammar.Kind == GrammarKind.CaseOption ? grammar.CaseSeparators : null);
            var expected = grammar.Kind == GrammarKind.CaseOption ? ArgumentGrammar.CaseOption(grammar.Cases!)
                : grammar.Kind == GrammarKind.BareCases ? ArgumentGrammar.BareCases(grammar.Cases!)
                : ArgumentGrammar.LegacyFilter(grammar.Cases!);
            if (!SameCount(count, expected.Count)) throw new InvalidOperationException("Unsupported case grammar cardinality.");
        }
        else if (grammar.Kind == GrammarKind.NoArguments)
        {
            if (grammar.Positions.Length != 0 || grammar.Repeated is not null
                || !SameCount(count, new(0, 0, new[] { 0 })))
                throw new InvalidOperationException("No-argument grammar has extra rules.");
        }
        else
        {
            foreach (var position in grammar.Positions) ValidateValue(position.Value);
            if (grammar.Repeated is { } repeated)
            {
                if (repeated.FirstArgvIndex != grammar.Positions.Length + 1)
                    throw new InvalidOperationException("Repeated slots must follow the fixed prefix.");
                ValidateValue(repeated.Value);
            }
            var expectedIndices = Enumerable.Range(1, grammar.Positions.Length);
            if (!grammar.Positions.Select(p => p.ArgvIndex).OrderBy(n => n).SequenceEqual(expectedIndices)
                || (count.Maximum is null && grammar.Repeated is null)
                || (count.Maximum is int finiteMax && grammar.Repeated is null && finiteMax > grammar.Positions.Length)
                || (count.Maximum is int shortMax && shortMax < grammar.Positions.Length))
                throw new InvalidOperationException("Unbound or unreachable positional slots.");
        }
    }

    private static bool SameCount(Cardinality actual, Cardinality expected) =>
        actual.Minimum == expected.Minimum && actual.Maximum == expected.Maximum
        && (actual.AllowedCounts is null ? expected.AllowedCounts is null
            : expected.AllowedCounts is not null && actual.AllowedCounts.SequenceEqual(expected.AllowedCounts));

    private static void ValidateCases(ActualCaseRegistry registry, string? separators)
    {
        if (string.IsNullOrWhiteSpace(registry.Id) || registry.Cases is null || registry.Cases.Count == 0
            || registry.Cases.Any(c => c is null || string.IsNullOrWhiteSpace(c.Name) || c.Run is null
                || c.Name.StartsWith("--", StringComparison.Ordinal) || (separators is not null && separators.Any(c.Name.Contains)))
            || registry.Cases.Select(c => c.Name).Distinct(StringComparer.Ordinal).Count() != registry.Cases.Count)
            throw new InvalidOperationException("Invalid actual case collection.");
    }

    private static void ValidateValue(ValueRule value)
    {
        if (value is null || string.IsNullOrWhiteSpace(value.Name) || !Enum.IsDefined(typeof(ValueKind), value.Kind)
            || value.ForbiddenCharacters is null
            || value.Comparison is not (StringComparison.Ordinal or StringComparison.OrdinalIgnoreCase)
            || (value.Minimum is int minimum && value.Maximum is int maximum && minimum > maximum))
            throw new InvalidOperationException("Invalid value rule.");
        if (value.Kind == ValueKind.Enum)
        {
            if (value.AllowedValues is null || value.AllowedValues.Length == 0
                || value.AllowedValues.Any(string.IsNullOrWhiteSpace)
                || value.AllowedValues.Distinct(value.Comparison == StringComparison.Ordinal
                    ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase).Count() != value.AllowedValues.Length)
                throw new InvalidOperationException("Missing or duplicate enum values.");
        }
        else if (value.AllowedValues is not null) throw new InvalidOperationException("Unexpected enum values.");
        if ((value.Kind is ValueKind.Enum or ValueKind.NonEmptyString) && (value.Minimum is not null || value.Maximum is not null))
            throw new InvalidOperationException("Unexpected numeric bounds.");
    }

    private static bool Fail(string message, out string diagnostic) { diagnostic = message; return false; }
}
