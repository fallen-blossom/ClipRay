// Original Program must become partial before integration. This file alone does not replace Main.
#nullable enable
using System.Drawing;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using ClipRay.App.Capture;
using ClipRay.App.Acceptance;
using ClipRay.App.Cleanup;
using ClipRay.App.Diagnostics;
using ClipRay.App.Editing;
using ClipRay.App.Feedback;
using ClipRay.App.Infrastructure;
using ClipRay.App.LiveTranslation;
using ClipRay.App.Overlay;
using ClipRay.App.Security;
using ClipRay.App.Settings;
using ClipRay.App.Scrolling;
using ClipRay.App.Ocr;
using ClipRay.App.Recording;
using ClipRay.App.Translation;
using ClipRay.Core.Geometry;
using ClipRay.TestLab;
using RapidOcrNet;
using SkiaSharp;
using MediaColor = System.Windows.Media.Color;
using System;
using System.Collections.Generic;
using System.Linq;
using ClipRay.PreparedCli;

internal static partial class Program
{
    internal static IReadOnlyList<CommandDescriptor> CreatePreparedCommandDescriptors(
        IPreparedCaseBindings actualCases,
        Func<IReadOnlyList<RegisteredCase>, int> runLegacySelected,
        IEnumerable<CommandDescriptor>? additionalCommands = null)
    {
        ArgumentNullException.ThrowIfNull(actualCases);
        ArgumentNullException.ThrowIfNull(runLegacySelected);
        var commands = new List<CommandDescriptor>();

        // Frozen Main line 38; original body retained once.
        Func<string[], int> branch0038 = args => { return BackgroundTask6Tests.Run(args); };
        commands.Add(new CommandDescriptor("--background-task-6-tests", StringComparison.Ordinal,
            ArgumentGrammar.BareCases(RequirePreparedRegistry(actualCases, "background-task-6")),
            invocation => branch0038(invocation.CanonicalArgs), "main-line-0038"));

        // Frozen Main line 39; original body retained once.
        Func<string[], int> branch0039 = args => { return BackgroundTask5Tests.Run(args); };
        commands.Add(new CommandDescriptor("--background-task-5-tests", StringComparison.Ordinal,
            ArgumentGrammar.BareCases(RequirePreparedRegistry(actualCases, "background-task-5")),
            invocation => branch0039(invocation.CanonicalArgs), "main-line-0039"));

        // Frozen Main line 40; original body retained once.
        Func<string[], int> branch0040 = args => { return BackgroundTask4Tests.Run(args); };
        commands.Add(new CommandDescriptor("--background-task-4-tests", StringComparison.Ordinal,
            ArgumentGrammar.BareCases(RequirePreparedRegistry(actualCases, "background-task-4")),
            invocation => branch0040(invocation.CanonicalArgs), "main-line-0040"));

        // Frozen Main line 41; original body retained once.
        Func<string[], int> branch0041 = args => { return BackgroundTask3Tests.Run(args); };
        commands.Add(new CommandDescriptor("--background-task-3-tests", StringComparison.Ordinal,
            ArgumentGrammar.BareCases(RequirePreparedRegistry(actualCases, "background-task-3")),
            invocation => branch0041(invocation.CanonicalArgs), "main-line-0041"));

        // Frozen Main line 42; original body retained once.
        Func<string[], int> branch0042 = args => { return BackgroundTask2Tests.Run(args); };
        commands.Add(new CommandDescriptor("--background-task-2-tests", StringComparison.Ordinal,
            ArgumentGrammar.CaseOption(RequirePreparedRegistry(actualCases, "background-task-2")),
            invocation => branch0042(invocation.CanonicalArgs), "main-line-0042"));

        // Frozen Main line 43; original body retained once.
        Func<string[], int> branch0043 = args => { return BackgroundTask1Tests.Run(args); };
        commands.Add(new CommandDescriptor("--background-task-1-tests", StringComparison.Ordinal,
            ArgumentGrammar.CaseOption(RequirePreparedRegistry(actualCases, "background-task-1")),
            invocation => branch0043(invocation.CanonicalArgs), "main-line-0043"));

        // Frozen Main line 44; original body retained once.
        Func<string[], int> branch0044 = args => { return LocalLmtBatchTransportTests.ProtocolChild(); };
        commands.Add(new CommandDescriptor("--local-lmt-batch-protocol-child", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0044(invocation.CanonicalArgs), "main-line-0044"));

        // Frozen Main line 45; original body retained once.
        Func<string[], int> branch0045 = args => { LocalLmtBatchTransportTests.StartupBannerAndPriorTurnAreNotReturnedAsTranslations().GetAwaiter().GetResult(); return 0; };
        commands.Add(new CommandDescriptor("--local-lmt-batch-transport-tests", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0045(invocation.CanonicalArgs), "main-line-0045"));

        // Frozen Main line 46; original body retained once.
        Func<string[], int> branch0046 = args => { RejectsLocalLmtStartupBannerForHeading(); return 0; };
        commands.Add(new CommandDescriptor("--local-lmt-output-safety-tests", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0046(invocation.CanonicalArgs), "main-line-0046"));

        // Frozen Main line 47; original body retained once.
        Func<string[], int> branch0047 = args => { return LocalLmtProcessLifetimeTests.SleepAsChild(args); };
        commands.Add(new CommandDescriptor("--local-lmt-sleep-child", StringComparison.Ordinal,
            new ArgumentGrammar(GrammarKind.Positional, new Cardinality(1, 1, new int[] { 1 }), new PositionRule[] { new PositionRule(1, new ValueRule("pid-file", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")) }, Repeated: null),
            invocation => branch0047(invocation.CanonicalArgs), "main-line-0047"));

        // Frozen Main line 48; original body retained once.
        Func<string[], int> branch0048 = args => { LocalLmtProcessLifetimeTests.CancellingOneShotStopsItsChild().GetAwaiter().GetResult(); return 0; };
        commands.Add(new CommandDescriptor("--local-lmt-process-lifetime-tests", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0048(invocation.CanonicalArgs), "main-line-0048"));

        // Frozen Main line 49; original body retained once.
        Func<string[], int> branch0049 = args => { WorkbenchGroupingTests.RunAsync().GetAwaiter().GetResult(); return 0; };
        commands.Add(new CommandDescriptor("--workbench-grouping-tests", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0049(invocation.CanonicalArgs), "main-line-0049"));

        // Frozen Main line 50; original body retained once.
        Func<string[], int> branch0050 = args => { return WorkbenchHeldoutEvidence.Run(args); };
        commands.Add(new CommandDescriptor("--workbench-heldout-evidence", StringComparison.Ordinal,
            new ArgumentGrammar(GrammarKind.Positional, new Cardinality(4, 6, new int[] { 4, 5, 6 }), new PositionRule[] { new PositionRule(1, new ValueRule("model", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(2, new ValueRule("server", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(3, new ValueRule("fixture", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(4, new ValueRule("fresh-output", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(5, new ValueRule("prior-images", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(6, new ValueRule("rounds", ValueKind.PositiveInt32, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: 1, Maximum: 3, ForbiddenCharacters: "")) }, Repeated: null),
            invocation => branch0050(invocation.CanonicalArgs), "main-line-0050"));

        // Frozen Main line 51; original body retained once.
        Func<string[], int> branch0051 = args => { return WorkbenchManualEvidence.Run(args); };
        commands.Add(new CommandDescriptor("--workbench-manual-evidence", StringComparison.Ordinal,
            new ArgumentGrammar(GrammarKind.Positional, new Cardinality(2, 2, new int[] { 2 }), new PositionRule[] { new PositionRule(1, new ValueRule("source-image", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(2, new ValueRule("fresh-output", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")) }, Repeated: null),
            invocation => branch0051(invocation.CanonicalArgs), "main-line-0051"));

        // Frozen Main line 52; original body retained once.
        Func<string[], int> branch0052 = args => { return WorkbenchRecoveryEvidence.Run(args); };
        commands.Add(new CommandDescriptor("--workbench-recovery-evidence", StringComparison.Ordinal,
            new ArgumentGrammar(GrammarKind.Positional, new Cardinality(7, 8, new int[] { 7, 8 }), new PositionRule[] { new PositionRule(1, new ValueRule("image", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(2, new ValueRule("report", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(3, new ValueRule("source-language", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(4, new ValueRule("python", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(5, new ValueRule("worker", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(6, new ValueRule("model", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(7, new ValueRule("gpu-directory", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(8, new ValueRule("mode", ValueKind.Enum, AllowedValues: new string[] { "merge", "split" }, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")) }, Repeated: null),
            invocation => branch0052(invocation.CanonicalArgs), "main-line-0052"));

        // Frozen Main line 53; original body retained once.
        Func<string[], int> branch0053 = args => { MixedScriptOcrTests.Run(); return 0; };
        commands.Add(new CommandDescriptor("--mixed-script-ocr-tests", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0053(invocation.CanonicalArgs), "main-line-0053"));

        // Frozen Main line 54; original body retained once.
        Func<string[], int> branch0054 = args => { return OcrProfileEvidence.ReplayRegions(args); };
        commands.Add(new CommandDescriptor("--ocr-region-evidence", StringComparison.Ordinal,
            new ArgumentGrammar(GrammarKind.Positional, new Cardinality(3, 4, new int[] { 3, 4 }), new PositionRule[] { new PositionRule(1, new ValueRule("ocr-report", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(2, new ValueRule("image", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(3, new ValueRule("output", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(4, new ValueRule("mode", ValueKind.Enum, AllowedValues: new string[] { "detector-lines" }, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")) }, Repeated: null),
            invocation => branch0054(invocation.CanonicalArgs), "main-line-0054"));

        // Frozen Main line 55; original body retained once.
        Func<string[], int> branch0055 = args => { return OcrProfileEvidence.ProbeCrop(args); };
        commands.Add(new CommandDescriptor("--ocr-crop", StringComparison.Ordinal,
            new ArgumentGrammar(GrammarKind.Positional, new Cardinality(6, 6, new int[] { 6 }), new PositionRule[] { new PositionRule(1, new ValueRule("image", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(2, new ValueRule("left", ValueKind.Int32, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: -2147483648, Maximum: 2147483647, ForbiddenCharacters: "")), new PositionRule(3, new ValueRule("top", ValueKind.Int32, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: -2147483648, Maximum: 2147483647, ForbiddenCharacters: "")), new PositionRule(4, new ValueRule("width", ValueKind.PositiveInt32, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: 1, Maximum: 2147483647, ForbiddenCharacters: "")), new PositionRule(5, new ValueRule("height", ValueKind.PositiveInt32, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: 1, Maximum: 2147483647, ForbiddenCharacters: "")), new PositionRule(6, new ValueRule("profile", ValueKind.Enum, AllowedValues: new string[] { "visual", "common" }, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")) }, Repeated: null),
            invocation => branch0055(invocation.CanonicalArgs), "main-line-0055"));

        // Frozen Main line 56; original body retained once.
        Func<string[], int> branch0056 = args => { return OcrProfileEvidence.Run(args); };
        commands.Add(new CommandDescriptor("--ocr-profile-evidence", StringComparison.Ordinal,
            new ArgumentGrammar(GrammarKind.Positional, new Cardinality(4, 4, new int[] { 4 }), new PositionRule[] { new PositionRule(1, new ValueRule("root", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(2, new ValueRule("output", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(3, new ValueRule("case-id", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(4, new ValueRule("profile", ValueKind.Enum, AllowedValues: new string[] { "common", "windows-en", "common-korean", "multilingual", "multilingual4", "multilingual-no-spin", "traced", "traced-no-spin", "traced4-no-spin", "primary-only" }, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")) }, Repeated: null),
            invocation => branch0056(invocation.CanonicalArgs), "main-line-0056"));

        // Frozen Main line 57; original body retained once.
        Func<string[], int> branch0057 = args => { return MachineExecutionEvidenceTests.StartupFailureProbe(args); };
        commands.Add(new CommandDescriptor("--machine-startup-failure-probe", StringComparison.Ordinal,
            new ArgumentGrammar(GrammarKind.Positional, new Cardinality(3, 3, new int[] { 3 }), new PositionRule[] { new PositionRule(1, new ValueRule("python-without-dependencies", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(2, new ValueRule("worker", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(3, new ValueRule("model", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")) }, Repeated: null),
            invocation => branch0057(invocation.CanonicalArgs), "main-line-0057"));

        // Frozen Main line 58; original body retained once.
        Func<string[], int> branch0058 = args => { MachineExecutionEvidenceTests.Run(); return 0; };
        commands.Add(new CommandDescriptor("--machine-evidence-tests", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0058(invocation.CanonicalArgs), "main-line-0058"));

        // Frozen Main line 59; original body retained once.
        Func<string[], int> branch0059 = args => { return MachineExecutionEvidenceTests.Probe(args); };
        commands.Add(new CommandDescriptor("--machine-evidence-probe", StringComparison.Ordinal,
            new ArgumentGrammar(GrammarKind.Positional, new Cardinality(4, 5, new int[] { 4, 5 }), new PositionRule[] { new PositionRule(1, new ValueRule("python", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(2, new ValueRule("worker", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(3, new ValueRule("model", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(4, new ValueRule("fresh-report-path", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(5, new ValueRule("gpu-directory", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")) }, Repeated: null),
            invocation => branch0059(invocation.CanonicalArgs), "main-line-0059"));

        // Frozen Main line 60; original body retained once.
        Func<string[], int> branch0060 = args => { RecoversLocalModelAfterFailedVerification(); CancelsLocalModelVerification(); return 0; };
        commands.Add(new CommandDescriptor("--local-model-recovery-tests", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0060(invocation.CanonicalArgs), "main-line-0060"));

        // Frozen Main line 61; original body retained once.
        Func<string[], int> branch0061 = args => { PreservesExplicitLocalModelWhenMissing(); ResolvesAuditedDeveloperLocalLmtModels(); VerifiesTranslationFileInventory(); return 0; };
        commands.Add(new CommandDescriptor("--local-model-catalog-tests", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0061(invocation.CanonicalArgs), "main-line-0061"));

        // Frozen Main line 62; original body retained once.
        Func<string[], int> branch0062 = args => { LocalModelPackageCatalogTests.RunAsync().GetAwaiter().GetResult(); return 0; };
        commands.Add(new CommandDescriptor("--local-package-catalog-tests", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0062(invocation.CanonicalArgs), "main-line-0062"));

        // Frozen Main line 63; original body retained once.
        Func<string[], int> branch0063 = args => {
            PreservesRetainedEditsWhenTheSelectionIsAdjusted();
            DrawsAPenStrokeCrossingTheSelectedBoundary();
            UndoesRetainedOperationsInDrawingOrder();
            RedoesRetainedEditsAndClearsTheRedoBranch();
            StartsRecordingWhenRecordingModeSelectionIsConfirmed();
            KeepsRecordingLaunchEnterAndEscapeDeterministic();
            BlocksASecondRecordingSelectionWhileRecordingIsActive();
            LiveVisibilitySettingsTests.BorderVisibilitySurvivesRefresh();
            LiveVisibilitySettingsTests.RecordingVisibilityControlsCaptureExclusion();
            Console.WriteLine("Capture companion checks passed without showing windows.");
            return 0;
        };
        commands.Add(new CommandDescriptor("--capture-companion-tests", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0063(invocation.CanonicalArgs), "main-line-0063"));

        // Frozen Main line 77; original body retained once.
        Func<string[], int> branch0077 = args => { InterfaceLanguageTests.Run(); return 0; };
        commands.Add(new CommandDescriptor("--interface-language-tests", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0077(invocation.CanonicalArgs), "main-line-0077"));

        // Frozen Main line 78; original body retained once.
        Func<string[], int> branch0078 = args => { EnsureWpfResources(); CaptureLocalizationTests.Run(); return 0; };
        commands.Add(new CommandDescriptor("--capture-language-tests", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0078(invocation.CanonicalArgs), "main-line-0078"));

        // Frozen Main line 79; original body retained once.
        Func<string[], int> branch0079 = args => { EnsureWpfResources(); CaptureCompanionLanguageTests.Run(); return 0; };
        commands.Add(new CommandDescriptor("--capture-companion-language-tests", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0079(invocation.CanonicalArgs), "main-line-0079"));

        // Frozen Main line 80; original body retained once.
        Func<string[], int> branch0080 = args => { KoreanRegionTests.Run(); KeepsKoreanSpacesWhenJoiningOcrWrappedLines(); return 0; };
        commands.Add(new CommandDescriptor("--korean-region-tests", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0080(invocation.CanonicalArgs), "main-line-0080"));

        // Frozen Main line 81; original body retained once.
        Func<string[], int> branch0081 = args => { return QwenHttpWorkbenchProbe.RunSyntheticAsync(args).GetAwaiter().GetResult(); };
        commands.Add(new CommandDescriptor("--candidate-protected-synthetic", StringComparison.Ordinal,
            new ArgumentGrammar(GrammarKind.Positional, new Cardinality(4, 4, new int[] { 4 }), new PositionRule[] { new PositionRule(1, new ValueRule("model", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(2, new ValueRule("server", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(3, new ValueRule("fixture", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(4, new ValueRule("fresh-output", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")) }, Repeated: null),
            invocation => branch0081(invocation.CanonicalArgs), "main-line-0081"));
        commands.Add(new CommandDescriptor("--candidate-hymt2-synthetic", StringComparison.Ordinal,
            new ArgumentGrammar(GrammarKind.Positional, new Cardinality(4, 4, new int[] { 4 }), new PositionRule[] { new PositionRule(1, new ValueRule("model", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(2, new ValueRule("server", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(3, new ValueRule("fixture", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(4, new ValueRule("fresh-output", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")) }, Repeated: null),
            invocation => branch0081(invocation.CanonicalArgs), "main-line-0081"));
        commands.Add(new CommandDescriptor("--candidate-hymt2-terms-synthetic", StringComparison.Ordinal,
            new ArgumentGrammar(GrammarKind.Positional, new Cardinality(4, 4, new int[] { 4 }), new PositionRule[] { new PositionRule(1, new ValueRule("model", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(2, new ValueRule("server", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(3, new ValueRule("fixture", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(4, new ValueRule("fresh-output", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")) }, Repeated: null),
            invocation => branch0081(invocation.CanonicalArgs), "main-line-0081"));

        // Frozen Main line 82; original body retained once.
        Func<string[], int> branch0082 = args => { return QwenHttpWorkbenchProbe.RunSyntheticAsync(args).GetAwaiter().GetResult(); };
        commands.Add(new CommandDescriptor("--candidate-hymt2-context-synthetic", StringComparison.Ordinal,
            new ArgumentGrammar(GrammarKind.Positional, new Cardinality(4, 4, new int[] { 4 }), new PositionRule[] { new PositionRule(1, new ValueRule("model", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(2, new ValueRule("server", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(3, new ValueRule("fixture", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(4, new ValueRule("fresh-output", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")) }, Repeated: null),
            invocation => branch0082(invocation.CanonicalArgs), "main-line-0082"));

        // Frozen Main line 83; original body retained once.
        Func<string[], int> branch0083 = args => { return WorkbenchRealEvidence.Run(args); };
        commands.Add(new CommandDescriptor("--workbench-real-hymt2", StringComparison.Ordinal,
            new ArgumentGrammar(GrammarKind.Positional, new Cardinality(6, 6, new int[] { 6 }), new PositionRule[] { new PositionRule(1, new ValueRule("root", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(2, new ValueRule("output", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(3, new ValueRule("case-id", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(4, new ValueRule("source-language", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(5, new ValueRule("model", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(6, new ValueRule("cli-or-server", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")) }, Repeated: null),
            invocation => branch0083(invocation.CanonicalArgs), "main-line-0083"));
        commands.Add(new CommandDescriptor("--workbench-real-hymt2-terms", StringComparison.Ordinal,
            new ArgumentGrammar(GrammarKind.Positional, new Cardinality(6, 6, new int[] { 6 }), new PositionRule[] { new PositionRule(1, new ValueRule("root", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(2, new ValueRule("output", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(3, new ValueRule("case-id", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(4, new ValueRule("source-language", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(5, new ValueRule("model", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(6, new ValueRule("cli-or-server", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")) }, Repeated: null),
            invocation => branch0083(invocation.CanonicalArgs), "main-line-0083"));

        // Frozen Main line 84; original body retained once.
        Func<string[], int> branch0084 = args => { return QwenHttpWorkbenchProbe.VerifyCancellationAsync(args).GetAwaiter().GetResult(); };
        commands.Add(new CommandDescriptor("--candidate-http-cancel-probe", StringComparison.Ordinal,
            new ArgumentGrammar(GrammarKind.Positional, new Cardinality(3, 3, new int[] { 3 }), new PositionRule[] { new PositionRule(1, new ValueRule("model", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(2, new ValueRule("server", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(3, new ValueRule("fresh-output", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")) }, Repeated: null),
            invocation => branch0084(invocation.CanonicalArgs), "main-line-0084"));
        commands.Add(new CommandDescriptor("--candidate-hymt2-cancel-probe", StringComparison.Ordinal,
            new ArgumentGrammar(GrammarKind.Positional, new Cardinality(3, 3, new int[] { 3 }), new PositionRule[] { new PositionRule(1, new ValueRule("model", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(2, new ValueRule("server", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(3, new ValueRule("fresh-output", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")) }, Repeated: null),
            invocation => branch0084(invocation.CanonicalArgs), "main-line-0084"));

        // Frozen Main line 85; original body retained once.
        Func<string[], int> branch0085 = args => { QwenWorkbenchProbe.VerifyTransportGuard(); return 0; };
        commands.Add(new CommandDescriptor("--candidate-transport-tests", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0085(invocation.CanonicalArgs), "main-line-0085"));

        // Frozen Main line 86; original body retained once.
        Func<string[], int> branch0086 = args => { return ResidentModelProbe.Run(args).GetAwaiter().GetResult(); };
        commands.Add(new CommandDescriptor("--resident-probe", StringComparison.Ordinal,
            new ArgumentGrammar(GrammarKind.Positional, new Cardinality(3, 4, new int[] { 3, 4 }), new PositionRule[] { new PositionRule(1, new ValueRule("model", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "\"")), new PositionRule(2, new ValueRule("cli", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(3, new ValueRule("fresh-output-directory", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(4, new ValueRule("prior-evidence-directory", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")) }, Repeated: null),
            invocation => branch0086(invocation.CanonicalArgs), "main-line-0086"));

        // Frozen Main line 87; original body retained once.
        Func<string[], int> branch0087 = args => { return WindowsOcrProbe.Run(args); };
        commands.Add(new CommandDescriptor("--windows-ocr-probe", StringComparison.Ordinal,
            new ArgumentGrammar(GrammarKind.Positional, new Cardinality(0, 3, new int[] { 0, 3 }), new PositionRule[] { new PositionRule(1, new ValueRule("image", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(2, new ValueRule("exact-language", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(3, new ValueRule("fresh-report-file", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")) }, Repeated: null),
            invocation => branch0087(invocation.CanonicalArgs), "main-line-0087"));

        // Frozen Main line 88; original body retained once.
        Func<string[], int> branch0088 = args => { return WorkbenchRealEvidence.Run(args); };
        commands.Add(new CommandDescriptor("--workbench-real", StringComparison.Ordinal,
            new ArgumentGrammar(GrammarKind.Positional, new Cardinality(7, 8, new int[] { 7, 8 }), new PositionRule[] { new PositionRule(1, new ValueRule("root", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(2, new ValueRule("output", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(3, new ValueRule("case-id", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(4, new ValueRule("source-language", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(5, new ValueRule("python", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(6, new ValueRule("worker", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(7, new ValueRule("model", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(8, new ValueRule("gpu-directory", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")) }, Repeated: null),
            invocation => branch0088(invocation.CanonicalArgs), "main-line-0088"));
        commands.Add(new CommandDescriptor("--workbench-real-common", StringComparison.Ordinal,
            new ArgumentGrammar(GrammarKind.Positional, new Cardinality(7, 8, new int[] { 7, 8 }), new PositionRule[] { new PositionRule(1, new ValueRule("root", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(2, new ValueRule("output", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(3, new ValueRule("case-id", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(4, new ValueRule("source-language", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(5, new ValueRule("python", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(6, new ValueRule("worker", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(7, new ValueRule("model", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(8, new ValueRule("gpu-directory", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")) }, Repeated: null),
            invocation => branch0088(invocation.CanonicalArgs), "main-line-0088"));
        commands.Add(new CommandDescriptor("--workbench-real-lmt", StringComparison.Ordinal,
            new ArgumentGrammar(GrammarKind.Positional, new Cardinality(6, 6, new int[] { 6 }), new PositionRule[] { new PositionRule(1, new ValueRule("root", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(2, new ValueRule("output", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(3, new ValueRule("case-id", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(4, new ValueRule("source-language", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(5, new ValueRule("model", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(6, new ValueRule("cli-or-server", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")) }, Repeated: null),
            invocation => branch0088(invocation.CanonicalArgs), "main-line-0088"));
        commands.Add(new CommandDescriptor("--workbench-real-lmt-chat", StringComparison.Ordinal,
            new ArgumentGrammar(GrammarKind.Positional, new Cardinality(6, 6, new int[] { 6 }), new PositionRule[] { new PositionRule(1, new ValueRule("root", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(2, new ValueRule("output", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(3, new ValueRule("case-id", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(4, new ValueRule("source-language", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(5, new ValueRule("model", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(6, new ValueRule("cli-or-server", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")) }, Repeated: null),
            invocation => branch0088(invocation.CanonicalArgs), "main-line-0088"));
        commands.Add(new CommandDescriptor("--workbench-real-qwen", StringComparison.Ordinal,
            new ArgumentGrammar(GrammarKind.Positional, new Cardinality(6, 6, new int[] { 6 }), new PositionRule[] { new PositionRule(1, new ValueRule("root", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(2, new ValueRule("output", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(3, new ValueRule("case-id", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(4, new ValueRule("source-language", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(5, new ValueRule("model", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(6, new ValueRule("cli-or-server", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")) }, Repeated: null),
            invocation => branch0088(invocation.CanonicalArgs), "main-line-0088"));
        commands.Add(new CommandDescriptor("--workbench-real-qwen-context", StringComparison.Ordinal,
            new ArgumentGrammar(GrammarKind.Positional, new Cardinality(6, 6, new int[] { 6 }), new PositionRule[] { new PositionRule(1, new ValueRule("root", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(2, new ValueRule("output", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(3, new ValueRule("case-id", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(4, new ValueRule("source-language", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(5, new ValueRule("model", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(6, new ValueRule("cli-or-server", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")) }, Repeated: null),
            invocation => branch0088(invocation.CanonicalArgs), "main-line-0088"));
        commands.Add(new CommandDescriptor("--workbench-real-qwen-http", StringComparison.Ordinal,
            new ArgumentGrammar(GrammarKind.Positional, new Cardinality(6, 6, new int[] { 6 }), new PositionRule[] { new PositionRule(1, new ValueRule("root", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(2, new ValueRule("output", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(3, new ValueRule("case-id", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(4, new ValueRule("source-language", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(5, new ValueRule("model", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(6, new ValueRule("cli-or-server", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")) }, Repeated: null),
            invocation => branch0088(invocation.CanonicalArgs), "main-line-0088"));
        commands.Add(new CommandDescriptor("--workbench-real-qwen-protected", StringComparison.Ordinal,
            new ArgumentGrammar(GrammarKind.Positional, new Cardinality(6, 6, new int[] { 6 }), new PositionRule[] { new PositionRule(1, new ValueRule("root", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(2, new ValueRule("output", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(3, new ValueRule("case-id", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(4, new ValueRule("source-language", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(5, new ValueRule("model", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(6, new ValueRule("cli-or-server", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")) }, Repeated: null),
            invocation => branch0088(invocation.CanonicalArgs), "main-line-0088"));

        // Frozen Main line 89; original body retained once.
        Func<string[], int> branch0089 = args => { WorkbenchWindowTests.Run(); return 0; };
        commands.Add(new CommandDescriptor("--workbench-window-tests", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0089(invocation.CanonicalArgs), "main-line-0089"));

        // Frozen Main line 90; original body retained once.
        Func<string[], int> branch0090 = args => { WorkbenchSessionTests.Run(); return 0; };
        commands.Add(new CommandDescriptor("--workbench-session-tests", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0090(invocation.CanonicalArgs), "main-line-0090"));

        // Frozen Main line 91; original body retained once.
        Func<string[], int> branch0091 = args => { LiveTranslationRouteTests.ExplicitSupportedRouteWinsAndOnlyMissingRouteUsesLiveSettings(); return 0; };
        commands.Add(new CommandDescriptor("--live-route-tests", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0091(invocation.CanonicalArgs), "main-line-0091"));

        // Frozen Main line 92; original body retained once.
        Func<string[], int> branch0092 = args => { LiveEvidenceTests.BundleHasLinkedContentAndChecksums(); LiveEvidenceTests.DataBundleKeepsFrameAndRenderEvidenceWithoutImages(); LiveEvidenceTests.CacheAndWireDiagnosticsAreScoped(); Console.WriteLine("LIVE_EVIDENCE_TESTS_PASSED"); return 0; };
        commands.Add(new CommandDescriptor("--live-evidence-tests", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0092(invocation.CanonicalArgs), "main-line-0092"));

        // Frozen Main line 93; original body retained once.
        Func<string[], int> branch0093 = args => { ScreenshotEvidenceTests.ScreenshotSwitchIsIndependentOfLiveTrace(); ScreenshotEvidenceTests.RecordsOnePrivateRunWithSourceRequestsOutcomeAndLayer(); ScreenshotEvidenceTests.RecordsActualFallbackSubsetAndMarksBrokenLayerIncomplete(); Console.WriteLine("SCREENSHOT_EVIDENCE_TESTS_PASSED"); return 0; };
        commands.Add(new CommandDescriptor("--screenshot-evidence-tests", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0093(invocation.CanonicalArgs), "main-line-0093"));

        // Frozen Main line 94; original body retained once.
        Func<string[], int> branch0094 = args => { LiveAnimationTextSafetyTests.AdjacentAnimationInCropPaddingDoesNotStarveChangedText(); LiveAnimationTextSafetyTests.AnimationThroughTextCannotPublishOldTranslation(); LiveAnimationTextSafetyTests.OneChangedGlyphPixelCannotPassViewportCommit(); LiveAnimationTextSafetyTests.IncidentalChangeWithoutWordGeometryCannotAdvanceWitness(); LiveHoverTests.CaretShapedChangeInsideWordClearsAndRetries(); Console.WriteLine("LIVE_ANIMATION_SAFETY_TESTS_PASSED"); return 0; };
        commands.Add(new CommandDescriptor("--live-animation-safety-tests", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0094(invocation.CanonicalArgs), "main-line-0094"));

        // Frozen Main line 95; original body retained once.
        Func<string[], int> branch0095 = args => { LiveSemanticAnimationTests.AnimatedBackingRebuildsTheErasurePatch(); LiveSemanticAnimationTests.MovingBackingKeepsTextAndChangedWordReplacesIt(); LiveSemanticAnimationTests.OldWorkerCannotPublishAfterTextChanges(); LiveSemanticAnimationTests.SourceIdentityUsesFullTextAndTolerantGeometry(); LiveSemanticAnimationTests.PartialSuccessRetriesWhileWordsAnimate(); LiveSemanticAnimationTests.UnknownOcrClearsOldTextWithinBound(); LiveSemanticAnimationTests.StopCancelsSemanticOcrWithoutLateOutput(); Console.WriteLine("LIVE_SEMANTIC_ANIMATION_TESTS_PASSED"); return 0; };
        commands.Add(new CommandDescriptor("--live-semantic-animation-tests", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0095(invocation.CanonicalArgs), "main-line-0095"));

        // Frozen Main line 96; original body retained once.
        Func<string[], int> branch0096 = args => { LiveRepairTests.FailuresRecoverWithoutStoppingOrRetryCounter(); LiveRepairTests.FailureStaysActiveWithObservableReason(); return 0; };
        commands.Add(new CommandDescriptor("--live-failure-ux-tests", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0096(invocation.CanonicalArgs), "main-line-0096"));

        // Frozen Main line 97; original body retained once.
        Func<string[], int> branch0097 = args => { LiveCaptureFailureDrainsPendingTranslation(); LiveTranslationRetriesUnchangedFailureWithoutStopping(); LiveRegionValidationTests.CaptureFailureDoesNotOverlapWorkers(); return 0; };
        commands.Add(new CommandDescriptor("--live-worker-failure-tests", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0097(invocation.CanonicalArgs), "main-line-0097"));

        // Frozen Main line 98; original body retained once.
        Func<string[], int> branch0098 = args => { MachineModeAndFallbackSettingsDefaultSafely(); SeparatesRegionAndLiveProviderSettings(); DefinesFinalV1SettingDefaults(); MigratesOldSettingsWithRecordingAndCleanupDefaults(); MigratesImplicitChineseProviderToMachine(); PreservesExplicitDeepSeekChoice(); return 0; };
        commands.Add(new CommandDescriptor("--machine-default-tests", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0098(invocation.CanonicalArgs), "main-line-0098"));

        // Frozen Main line 99; original body retained once.
        Func<string[], int> branch0099 = args => { LiveVisibilitySettingsTests.SelectedRegionBorderIsVisible(); LiveSelectionUsesPhysicalCursorCoordinates(); LiveVisibilitySettingsTests.OverlayKeepsTheSelectedPhysicalPixelBounds(); LiveVisibilitySettingsTests.VisibilityChoicesPersistIndependently(); LiveVisibilitySettingsTests.RecordingVisibilityControlsCaptureExclusion(); return 0; };
        commands.Add(new CommandDescriptor("--live-recording-overlay-tests", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0099(invocation.CanonicalArgs), "main-line-0099"));

        // Frozen Main line 100; original body retained once.
        Func<string[], int> branch0100 = args => { LiveAcceptanceCapturesSourcePixelsFromTheTargetWindow(); LiveAcceptanceSignalsReadinessWhileTranslationIsStillRunning(); LiveAcceptanceClearsStaleTranslationsWhenTheSourceChanges(); LiveAcceptanceUsesTheFullViewportPipeline(); return 0; };
        commands.Add(new CommandDescriptor("--live-acceptance-harness-tests", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0100(invocation.CanonicalArgs), "main-line-0100"));

        // Frozen Main line 101; original body retained once.
        Func<string[], int> branch0101 = args => { SeparatesShortCardHeadingFromDistantBody(); DoesNotMergeHeadingWithSmallerSubtitle(); DoesNotMergeWideParagraphAcrossCardColumns(); KeepsParagraphIntactBesideSidebarRow(); return 0; };
        commands.Add(new CommandDescriptor("--card-heading-boundary-tests", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0101(invocation.CanonicalArgs), "main-line-0101"));

        // Frozen Main line 102; original body retained once.
        Func<string[], int> branch0102 = args => { ProductionLiveTranslationUsesFixedScreenRegion(); ProductionLiveTranslationOwnsStartupAndCaptureLifetime(); LiveTranslationScreenCaptureTests.SamePhysicalRegionSamplesVisibleAtoBtoA().GetAwaiter().GetResult(); LiveTranslationScreenCaptureTests.RejectsInvalidCrossMonitorOrChangedRegion().GetAwaiter().GetResult(); LiveTranslationScreenCaptureTests.CapturesDesktopAndMultipleWindowPixelsWithoutWindowIdentity().GetAwaiter().GetResult(); LiveTranslationScreenCaptureTests.RejectsChangedTopologyAndWrongSizedFrame().GetAwaiter().GetResult(); WindowCaptureStopsOnSourceClosure(); WindowCaptureGeometryRejectsDrift(); MonitorCaptureDoesNotUseWindowGeometryFaults(); WindowCaptureRejectsInvalidTargetWindows(); Console.WriteLine("LIVE_SCREEN_CAPTURE_TESTS_PASSED"); return 0; };
        commands.Add(new CommandDescriptor("--live-production-capture-tests", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0102(invocation.CanonicalArgs), "main-line-0102"));

        // Frozen Main line 103; original body retained once.
        Func<string[], int> branch0103 = args => { LiveRegionValidationTests.ManyTransientCaptureFailuresStillRecover(); Console.WriteLine("LIVE_MANY_FAILURES_TEST_PASSED"); return 0; };
        commands.Add(new CommandDescriptor("--live-many-failures-test", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0103(invocation.CanonicalArgs), "main-line-0103"));

        // Frozen Main line 104; original body retained once.
        Func<string[], int> branch0104 = args => { LivePartialRetryTests.StaticCloudPartialRequestsHaveAFiveMinuteUpperBound(); LivePartialRetryTests.ChangedTextInterruptsCloudBackoffWithinThreeSeconds(); LivePartialRetryTests.OneHundredTemporaryPartialsRemainRetryableAndSuccessResetsDelay(); LiveRegionValidationTests.StaticPartialViewportRetriesMissingTranslation(); Console.WriteLine("LIVE_PARTIAL_RETRY_TESTS_PASSED"); return 0; };
        commands.Add(new CommandDescriptor("--live-partial-retry-tests", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0104(invocation.CanonicalArgs), "main-line-0104"));

        // Frozen Main line 105; original body retained once.
        Func<string[], int> branch0105 = args => { LongCaptureCompletionTests.CompletionIsDistinctFromCancellation(); return 0; };
        commands.Add(new CommandDescriptor("--long-completion-tests", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0105(invocation.CanonicalArgs), "main-line-0105"));

        // Frozen Main line 106; original body retained once.
        Func<string[], int> branch0106 = args => { LongCapturePipelineTests.Run(); return 0; };
        commands.Add(new CommandDescriptor("--long-pipeline-tests", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0106(invocation.CanonicalArgs), "main-line-0106"));

        // Frozen Main line 107; original body retained once.
        Func<string[], int> branch0107 = args => { KeepsPaddedOcrContextOutOfTranslation(); return 0; };
        commands.Add(new CommandDescriptor("--selection-boundary-tests", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0107(invocation.CanonicalArgs), "main-line-0107"));

        // Frozen Main line 108; original body retained once.
        Func<string[], int> branch0108 = args => { MachineFallbackDoesNotCountPreservedTextAsTranslated(); return 0; };
        commands.Add(new CommandDescriptor("--fallback-outcome-tests", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0108(invocation.CanonicalArgs), "main-line-0108"));

        // Frozen Main line 109; original body retained once.
        Func<string[], int> branch0109 = args => {
            ReportsUnverifiedQualityDespiteStructuralMatch();
            ReportsKnownIncompleteContentAndLayout();
            LabelsOnlyStructuralExpectationAtRunLevel();
            return 0;
        };
        commands.Add(new CommandDescriptor("--testlab-evidence-tests", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0109(invocation.CanonicalArgs), "main-line-0109"));

        // Frozen Main line 116; original body retained once.
        Func<string[], int> branch0116 = args => {
            IntentionalIdentityPreservationIsCompleteButMissingOrdinaryLayoutStillFails();
            return 0;
        };
        commands.Add(new CommandDescriptor("--intentional-preserve-outcome-tests", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0116(invocation.CanonicalArgs), "main-line-0116"));

        // Frozen Main line 121; original body retained once.
        Func<string[], int> branch0121 = args => {
            QualityOracleDetectsIndependentFailures();
            Console.WriteLine("QUALITY_ORACLE_TESTS_PASSED");
            return 0;
        };
        commands.Add(new CommandDescriptor("--quality-oracle-tests", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0121(invocation.CanonicalArgs), "main-line-0121"));

        // Frozen Main line 127; original body retained once.
        Func<string[], int> branch0127 = args => { throw new InvalidOperationException("QUALITY_ORACLE_SENTINEL_FAILURE"); };
        commands.Add(new CommandDescriptor("--quality-oracle-sentinel", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0127(invocation.CanonicalArgs), "main-line-0127"));

        // Frozen Main line 129; original body retained once.
        Func<string[], int> branch0129 = args => {
            RejectsStandalonePromptInstructions();
            return 0;
        };
        commands.Add(new CommandDescriptor("--local-echo-guard-tests", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0129(invocation.CanonicalArgs), "main-line-0129"));

        // Frozen Main line 134; original body retained once.
        Func<string[], int> branch0134 = args => {
            OcrEvidenceTests.PreservesRawScoreAndModelThroughGrouping();
            return 0;
        };
        commands.Add(new CommandDescriptor("--ocr-evidence-tests", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0134(invocation.CanonicalArgs), "main-line-0134"));

        // Frozen Main line 139; original body retained once.
        Func<string[], int> branch0139 = args => { KeepsApplicationAndInstallerVersioningConsistent(); return 0; };
        commands.Add(new CommandDescriptor("--versioning-tests", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0139(invocation.CanonicalArgs), "main-line-0139"));

        // Frozen Main line 140; original body retained once.
        Func<string[], int> branch0140 = args => { PackagesAndOpensOfflineClipRayTutorial(); return 0; };
        commands.Add(new CommandDescriptor("--tutorial-tests", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0140(invocation.CanonicalArgs), "main-line-0140"));

        // Frozen Main line 141; original body retained once.
        Func<string[], int> branch0141 = args => { SavedFileClipboardTests.Run(); return 0; };
        commands.Add(new CommandDescriptor("--saved-file-clipboard-tests", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0141(invocation.CanonicalArgs), "main-line-0141"));

        // Frozen Main line 142; original body retained once.
        Func<string[], int> branch0142 = args => { MediaClipboardRecoveryTests.NativeRecoveryProbe(args[1]); return 0; };
        commands.Add(new CommandDescriptor("--media-native-recovery-probe", StringComparison.Ordinal,
            new ArgumentGrammar(GrammarKind.Positional, new Cardinality(1, 1, new int[] { 1 }), new PositionRule[] { new PositionRule(1, new ValueRule("media-path", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")) }, Repeated: null),
            invocation => branch0142(invocation.CanonicalArgs), "main-line-0142"));

        // Frozen Main line 143; original body retained once.
        Func<string[], int> branch0143 = args => { MediaClipboardRecoveryTests.Run(); return 0; };
        commands.Add(new CommandDescriptor("--media-clipboard-recovery-tests", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0143(invocation.CanonicalArgs), "main-line-0143"));

        // Frozen Main line 144; original body retained once.
        Func<string[], int> branch0144 = args => { LiveTranslationStabilityGateTests.AnimationHasBoundedWaitAndStaticNeighborsProceed(); return 0; };
        commands.Add(new CommandDescriptor("--stability-gate-tests", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0144(invocation.CanonicalArgs), "main-line-0144"));

        // Frozen Main line 145; original body retained once.
        Func<string[], int> branch0145 = args => { LiveTranslationViewportCommitPolicyTests.FullViewportCommitIsAtomicAndRejectsOnlyNewerViewport(); LiveTranslationViewportCommitPolicyTests.SafePartialViewportKeepsSuccessfulSiblings(); LiveTranslationViewportCommitPolicyTests.UnsafeViewportGeometryCannotBePresented(); LiveTranslationViewportCommitPolicyTests.OneUnsafeLayoutDoesNotBlankSafeTranslations(); LiveTranslationViewportCommitPolicyTests.AdjacentBodyParagraphMarginsAreSafeViewportGeometry(); LiveTranslationViewportCommitPolicyTests.AdjacentDifferentRoleTextRowsAreSafeViewportGeometry(); LiveTranslationViewportCommitPolicyTests.BodyParagraphsSharingATextRowRemainUnsafe(); LiveTranslationViewportCommitPolicyTests.OneFailedPrimaryRegionDoesNotBlankAnOtherwiseUsefulViewport(); LocalChangeTranslationTests.ViewportScrollPublishesTheCompleteCurrentFrame(); LiveRegionValidationTests.ChangedGeometryDoesNotReuseStaleTranslation(); LiveRegionValidationTests.MediaAndScrolling(); return 0; };
        commands.Add(new CommandDescriptor("--viewport-commit-tests", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0145(invocation.CanonicalArgs), "main-line-0145"));

        // Frozen Main line 146; original body retained once.
        Func<string[], int> branch0146 = args => { LiveTranslationLayoutCacheTests.FixedFontUsesRendererGeometryWithoutShrinking(); return 0; };
        commands.Add(new CommandDescriptor("--fixed-font-layout-tests", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0146(invocation.CanonicalArgs), "main-line-0146"));

        // Frozen Main line 147; original body retained once.
        Func<string[], int> branch0147 = args => { LocalChangeTranslationTests.PaddingDoesNotMergeIndependentParagraphs(); return 0; };
        commands.Add(new CommandDescriptor("--paragraph-crop-isolation-tests", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0147(invocation.CanonicalArgs), "main-line-0147"));

        // Frozen Main line 148; original body retained once.
        Func<string[], int> branch0148 = args => { LiveTranslationLayoutCacheTests.AdjacentNewParagraphsAreNotDiscardedAsOldOverlays(); return 0; };
        commands.Add(new CommandDescriptor("--adjacent-overlay-tests", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0148(invocation.CanonicalArgs), "main-line-0148"));

        // Frozen Main line 149; original body retained once.
        Func<string[], int> branch0149 = args => { LiveTranslationLayoutCacheTests.NewParagraphKeepsEstablishedFontTier(); return 0; };
        commands.Add(new CommandDescriptor("--font-tier-cache-tests", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0149(invocation.CanonicalArgs), "main-line-0149"));

        // Frozen Main line 150; original body retained once.
        Func<string[], int> branch0150 = args => { LiveTranslationLayoutCacheTests.ChangedTextWithTighterOcrBoundsKeepsItsTranslation(); return 0; };
        commands.Add(new CommandDescriptor("--live-tighter-ocr-bounds-tests", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0150(invocation.CanonicalArgs), "main-line-0150"));

        // Frozen Main line 151; original body retained once.
        Func<string[], int> branch0151 = args => { FontConsistencyTests.SameSourceSizeAcrossEnglishAndJapanese(); FontConsistencyTests.AConstrainedSidebarCannotShrinkDistantArticleText(); return 0; };
        commands.Add(new CommandDescriptor("--font-consistency-tests", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0151(invocation.CanonicalArgs), "main-line-0151"));

        // Frozen Main line 152; original body retained once.
        Func<string[], int> branch0152 = args => {
            var imageIndex = Array.IndexOf(args, "--real-x-typography-tests") + 1;
            if (imageIndex >= args.Length) throw new ArgumentException("Source image path is required.");
            FontConsistencyTests.RealXSingleLineKeepsPeerFont(args[imageIndex]);
            return 0;
        };
        commands.Add(new CommandDescriptor("--real-x-typography-tests", StringComparison.Ordinal,
            new ArgumentGrammar(GrammarKind.Positional, new Cardinality(1, 1, new int[] { 1 }), new PositionRule[] { new PositionRule(1, new ValueRule("source-image", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")) }, Repeated: null),
            invocation => branch0152(invocation.CanonicalArgs), "main-line-0152"));

        // Frozen Main line 159; original body retained once.
        Func<string[], int> branch0159 = args => {
            var imageIndex = Array.IndexOf(args, "--real-x-partial-failure-tests") + 1;
            if (imageIndex >= args.Length) throw new ArgumentException("Source image path is required.");
            ScreenshotPartialFailureTests.RealXFailureKeepsUntranslatedPixels(args[imageIndex]);
            return 0;
        };
        commands.Add(new CommandDescriptor("--real-x-partial-failure-tests", StringComparison.Ordinal,
            new ArgumentGrammar(GrammarKind.Positional, new Cardinality(1, 1, new int[] { 1 }), new PositionRule[] { new PositionRule(1, new ValueRule("source-image", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")) }, Repeated: null),
            invocation => branch0159(invocation.CanonicalArgs), "main-line-0159"));

        // Frozen Main line 166; original body retained once.
        Func<string[], int> branch0166 = args => { CommonLanguageTests.MixedJapaneseKeepsFirstCharacterAndChineseLiteral(); return 0; };
        commands.Add(new CommandDescriptor("--mixed-japanese-tests", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0166(invocation.CanonicalArgs), "main-line-0166"));

        // Frozen Main line 167; original body retained once.
        Func<string[], int> branch0167 = args => { CommonLanguageTests.ParagraphCannotJumpOverAnotherDetectedLine(); return 0; };
        commands.Add(new CommandDescriptor("--paragraph-order-tests", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0167(invocation.CanonicalArgs), "main-line-0167"));

        // Frozen Main line 168; original body retained once.
        Func<string[], int> branch0168 = args => { return RedesignFrameReplay.ProbeCommonTranslation(args[1],args[2]); };
        commands.Add(new CommandDescriptor("--common-translation-probe", StringComparison.Ordinal,
            new ArgumentGrammar(GrammarKind.Positional, new Cardinality(2, 2, new int[] { 2 }), new PositionRule[] { new PositionRule(1, new ValueRule("model", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(2, new ValueRule("fixtures", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")) }, Repeated: null),
            invocation => branch0168(invocation.CanonicalArgs), "main-line-0168"));

        // Frozen Main line 169; original body retained once.
        Func<string[], int> branch0169 = args => { CommonLanguageTests.ExplicitJapaneseHanReachesWorker(); CommonLanguageTests.SupplementalFragmentCannotReplaceCompleteLine(); CommonLanguageTests.JapaneseKanjiAreNotProtectedChinese(); return 0; };
        commands.Add(new CommandDescriptor("--common-language-tests", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0169(invocation.CanonicalArgs), "main-line-0169"));

        // Frozen Main line 170; original body retained once.
        Func<string[], int> branch0170 = args => { return RedesignFrameReplay.ProbeCommonOcr(args[1]); };
        commands.Add(new CommandDescriptor("--common-ocr-probe", StringComparison.Ordinal,
            new ArgumentGrammar(GrammarKind.Positional, new Cardinality(1, 1, new int[] { 1 }), new PositionRule[] { new PositionRule(1, new ValueRule("fixtures", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")) }, Repeated: null),
            invocation => branch0170(invocation.CanonicalArgs), "main-line-0170"));

        // Frozen Main line 171; original body retained once.
        Func<string[], int> branch0171 = args => { return RedesignFrameReplay.ExtractOnnxDictionary(args[1],args[2]); };
        commands.Add(new CommandDescriptor("--onnx-dictionary-probe", StringComparison.Ordinal,
            new ArgumentGrammar(GrammarKind.Positional, new Cardinality(2, 2, new int[] { 2 }), new PositionRule[] { new PositionRule(1, new ValueRule("input", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")), new PositionRule(2, new ValueRule("output", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")) }, Repeated: null),
            invocation => branch0171(invocation.CanonicalArgs), "main-line-0171"));

        // Frozen Main line 172; original body retained once.
        Func<string[], int> branch0172 = args => { return RedesignFrameReplay.ProbeRapidApi(); };
        commands.Add(new CommandDescriptor("--rapid-api-probe", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0172(invocation.CanonicalArgs), "main-line-0172"));

        // Frozen Main line 173; original body retained once.
        Func<string[], int> branch0173 = args => { LiveHoverTests.BackgroundHoverDoesNotScheduleTextAgain(); LiveHoverTests.HoverKeepsTranslationAndUpdatesBacking(); LiveHoverTests.CaretBlinkDoesNotClearOrRetranslateBody(); return 0; };
        commands.Add(new CommandDescriptor("--live-hover-tests", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0173(invocation.CanonicalArgs), "main-line-0173"));

        // Frozen Main line 174; original body retained once.
        Func<string[], int> branch0174 = args => {SentenceTranslationTests.ControlTermsStayWithinControls();SentenceTranslationTests.SocialMachineControlsUseSharedUiTerms();return 0;};
        commands.Add(new CommandDescriptor("--control-term-tests", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0174(invocation.CanonicalArgs), "main-line-0174"));

        // Frozen Main line 175; original body retained once.
        Func<string[], int> branch0175 = args => {SentenceTranslationTests.IconFragmentsAndStandaloneModelsDoNotBecomeProse();return 0;};
        commands.Add(new CommandDescriptor("--icon-fragment-tests", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0175(invocation.CanonicalArgs), "main-line-0175"));

        // Frozen Main line 176; original body retained once.
        Func<string[], int> branch0176 = args => {SentenceTranslationTests.PreservedNeighborKeepsOriginalInkAtOverlappingOcrBoundary();SentenceTranslationTests.PreservedEmojiDoesNotShieldTranslatedLastGlyph();SentenceTranslationTests.SameColorPreservedNeighborKeepsInkOutsideItsOcrBox();return 0;};
        commands.Add(new CommandDescriptor("--protected-neighbor-tests", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0176(invocation.CanonicalArgs), "main-line-0176"));

        // Frozen Main line 177; original body retained once.
        Func<string[], int> branch0177 = args => { return RedesignFrameReplay.ProbeBilingual(); };
        commands.Add(new CommandDescriptor("--redesign-bilingual-probe", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0177(invocation.CanonicalArgs), "main-line-0177"));

        // Frozen Main line 178; original body retained once.
        Func<string[], int> branch0178 = args => { SentenceTranslationTests.BlankParagraphGapKeepsSeparateBodyBlocks(); SentenceTranslationTests.CompleteSentenceRowsStayIndependentVisualRegions(); SentenceTranslationTests.ClippedViewportTopKeepsWrappedSentenceTogether(); SentenceTranslationTests.TallOcrWordCannotPullAdjacentRowIntoHeader(); return 0; };
        commands.Add(new CommandDescriptor("--ocr-row-tests", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0178(invocation.CanonicalArgs), "main-line-0178"));

        // Frozen Main line 179; original body retained once.
        Func<string[], int> branch0179 = args => { return RedesignFrameReplay.Run(args[1]); };
        commands.Add(new CommandDescriptor("--redesign-frame-replay", StringComparison.Ordinal,
            new ArgumentGrammar(GrammarKind.Positional, new Cardinality(1, 1, new int[] { 1 }), new PositionRule[] { new PositionRule(1, new ValueRule("source-image", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")) }, Repeated: null),
            invocation => branch0179(invocation.CanonicalArgs), "main-line-0179"));

        // Frozen Main line 180; original body retained once.
        Func<string[], int> branch0180 = args => { return RedesignFrameReplay.RunLiveGate(args[1..]); };
        commands.Add(new CommandDescriptor("--live-replay-gate", StringComparison.Ordinal,
            new ArgumentGrammar(GrammarKind.Positional, new Cardinality(2, null, null), new PositionRule[] {  }, Repeated: new RepeatedRule(1, new ValueRule("frame-image", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: ""), false)),
            invocation => branch0180(invocation.CanonicalArgs), "main-line-0180"));

        // Frozen Main line 181; original body retained once.
        Func<string[], int> branch0181 = args => { SentenceTranslationTests.SeparateNameAndHandleStayIdentityMetadata(); return 0; };
        commands.Add(new CommandDescriptor("--identity-peer-tests", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0181(invocation.CanonicalArgs), "main-line-0181"));

        // Frozen Main line 182; original body retained once.
        Func<string[], int> branch0182 = args => { AccountNameTranslationTests.LatinIdentityNamesAllowLocalizedDatesButNotUnchangedDates(); AccountNameTranslationTests.RecommendedAccountNamesArePreservedWithoutExemptingControls(); return 0; };
        commands.Add(new CommandDescriptor("--account-name-policy-tests", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0182(invocation.CanonicalArgs), "main-line-0182"));

        // Frozen Main line 183; original body retained once.
        Func<string[], int> branch0183 = args => { SentenceTranslationTests.NarrowControlCannotShrinkBodyTypography(); return 0; };
        commands.Add(new CommandDescriptor("--body-peer-tests", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0183(invocation.CanonicalArgs), "main-line-0183"));

        // Frozen Main line 184; original body retained once.
        Func<string[], int> branch0184 = args => { LiveTranslationLayoutCacheTests.SameSemanticSourceKeepsFirstTypography(); return 0; };
        commands.Add(new CommandDescriptor("--layout-cache-tests", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0184(invocation.CanonicalArgs), "main-line-0184"));

        // Frozen Main line 185; original body retained once.
        Func<string[], int> branch0185 = args => { LiveTranslationLayoutCacheTests.ConstrainedParagraphCannotSetUnrelatedBodyFontTier(); LiveTranslationLayoutCacheTests.NewParagraphKeepsEstablishedFontTier(); LiveTranslationLayoutCacheTests.ChangedTextWithTighterOcrBoundsKeepsItsTranslation(); Console.WriteLine("LIVE_FONT_TIER_TESTS_PASSED"); return 0; };
        commands.Add(new CommandDescriptor("--layout-font-tier-tests", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0185(invocation.CanonicalArgs), "main-line-0185"));

        // Frozen Main line 186; original body retained once.
        Func<string[], int> branch0186 = args => { LiveCloudPartialReturnsBeforeFailedSiblingRetry(); LiveCloudMissingItemReturnsAvailableSibling(); LiveCloudPartialContractStillRejectsUnsafeResults(); LiveCloudCacheRetriesOnlyMissingSibling(); RetriesOnlyFailedVisualTranslationSiblings(); Console.WriteLine("LIVE_CLOUD_PARTIAL_TESTS_PASSED"); return 0; };
        commands.Add(new CommandDescriptor("--live-cloud-partial-tests", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0186(invocation.CanonicalArgs), "main-line-0186"));

        // Frozen Main line 187; original body retained once.
        Func<string[], int> branch0187 = args => { LiveTranslationCacheTests.OnlyTransientFailedParagraphIsRetried(); LiveTranslationCacheTests.SemanticCacheKeepsNumbersAndRetriesTimeouts(); LiveTranslationCacheTests.NormalizesWhitespaceAndRetriesUnchangedNaturalLanguage(); LiveTranslationCacheTests.IntegrityFailureExpiresWithoutRepeatedImmediateRequests(); return 0; };
        commands.Add(new CommandDescriptor("--live-cache-tests", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0187(invocation.CanonicalArgs), "main-line-0187"));

        // Frozen Main line 188; original body retained once.
        Func<string[], int> branch0188 = args => { LocalChangeTranslationTests.AnimationDoesNotStarveLocalText(); LocalChangeTranslationTests.PerpetualAnimationDoesNotRetranslateStableViewport(); LocalChangeTranslationTests.CropMarginNeverCutsNeighboringParagraph(); LocalChangeTranslationTests.OnlyChangedParagraphIsScheduled(); LocalChangeTranslationTests.SessionRebuildsChangedViewportFromFullFrame(); LocalChangeTranslationTests.CropCoordinatesReturnToSourcePixels(); LocalChangeTranslationTests.FullViewportScrollSchedulesOneFullFrame(); LocalChangeTranslationTests.SingleParagraphViewportScrollSchedulesOneFullFrame(); LocalChangeTranslationTests.SparseParagraphScrollDoesNotDependOnCanvasArea(); LocalChangeTranslationTests.FixedChromeDoesNotHideViewportScroll(); LocalChangeTranslationTests.DistantSmallEditsStayLocal(); LocalChangeTranslationTests.LargeLocalAnimationDoesNotBecomeAViewportScroll(); LocalChangeTranslationTests.LargeAnimationTouchingCaptionDoesNotBecomeAViewportScroll(); LocalChangeTranslationTests.ViewportScrollRestartsFromTheCurrentFullFrame(); LocalChangeTranslationTests.ViewportScrollPublishesTheCompleteCurrentFrame(); return 0; };
        commands.Add(new CommandDescriptor("--local-change-tests", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0188(invocation.CanonicalArgs), "main-line-0188"));

        // Frozen Main line 189; original body retained once.
        Func<string[], int> branch0189 = args => { SentenceTranslationTests.FailedEmptyFrameWaitsForChangeWithoutPublishing(); return 0; };
        commands.Add(new CommandDescriptor("--static-partial-tests", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0189(invocation.CanonicalArgs), "main-line-0189"));

        // Frozen Main line 190; original body retained once.
        Func<string[], int> branch0190 = args => { SentenceTranslationTests.SingleLineBodyDoesNotAlignToUnrelatedControls(); SentenceTranslationTests.TightOcrWordBoundsDoNotShrinkRealGlyphs(); SentenceTranslationTests.RendererUsesMeasuredLineGeometry(); return 0; };
        commands.Add(new CommandDescriptor("--renderer-geometry-tests", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0190(invocation.CanonicalArgs), "main-line-0190"));

        // Frozen Main line 191; original body retained once.
        Func<string[], int> branch0191 = args => { SentenceTranslationTests.InlineProtectedTextKeepsOneSentence(); SentenceTranslationTests.SocialIdentityStaysOutsideBody(); SentenceTranslationTests.NumericBodyKeepsSentenceContext(); SentenceTranslationTests.VisualLineBreakIsNotSentenceBoundary(); SentenceTranslationTests.CompleteOcrLinesAreIndependentMachineUnits(); return 0; };
        commands.Add(new CommandDescriptor("--sentence-redesign-tests", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0191(invocation.CanonicalArgs), "main-line-0191"));

        // Frozen Main line 192; original body retained once.
        Func<string[], int> branch0192 = args => { MachineCalendarIntegrityTests.OcrHandleNoiseDoesNotEnterTheModel(); return 0; };
        commands.Add(new CommandDescriptor("--source-noise-tests", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0192(invocation.CanonicalArgs), "main-line-0192"));

        // Frozen Main line 193; original body retained once.
        Func<string[], int> branch0193 = args => { MachineChineseSourceTests.MachineOcrRetainsChineseAndEnglish(); return 0; };
        commands.Add(new CommandDescriptor("--bilingual-machine-ocr-tests", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0193(invocation.CanonicalArgs), "main-line-0193"));

        // Frozen Main line 194; original body retained once.
        Func<string[], int> branch0194 = args => { MachineChineseSourceTests.MachineOcrLoadsOnlySupportedSourceScripts(); return 0; };
        commands.Add(new CommandDescriptor("--machine-ocr-plan-tests", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0194(invocation.CanonicalArgs), "main-line-0194"));

        // Frozen Main line 195; original body retained once.
        Func<string[], int> branch0195 = args => { MachineChineseSourceTests.ChineseSourceDoesNotStartEnglishTranslation(); MachineChineseSourceTests.MixedEnglishTranslatesWithoutSendingChineseToModel(); return 0; };
        commands.Add(new CommandDescriptor("--chinese-source-tests", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0195(invocation.CanonicalArgs), "main-line-0195"));

        // Frozen Main line 196; original body retained once.
        Func<string[], int> branch0196 = args => { MachineCalendarIntegrityTests.CalendarNamesCanBecomeNumericMonths(); return 0; };
        commands.Add(new CommandDescriptor("--calendar-integrity-tests", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0196(invocation.CanonicalArgs), "main-line-0196"));

        // Frozen Main line 197; original body retained once.
        Func<string[], int> branch0197 = args => { MachineCalendarIntegrityTests.WrittenNumberConversionKeepsExplicitNumerals(); MachineCalendarIntegrityTests.ConjoinedOcrDigitsRemainNumericInvariants(); return 0; };
        commands.Add(new CommandDescriptor("--written-number-integrity-tests", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0197(invocation.CanonicalArgs), "main-line-0197"));

        // Frozen Main line 198; original body retained once.
        Func<string[], int> branch0198 = args => { MachineCalendarIntegrityTests.WebAddressesAndAgesBypassTheModel(); MachineCalendarIntegrityTests.InlineWebAddressesStayOutOfModelInput(); return 0; };
        commands.Add(new CommandDescriptor("--web-age-tests", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0198(invocation.CanonicalArgs), "main-line-0198"));

        // Frozen Main line 199; original body retained once.
        Func<string[], int> branch0199 = args => { NeutralTextRegionTests.NeutralShadesKeepSentenceContext(); return 0; };
        commands.Add(new CommandDescriptor("--neutral-region-tests", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0199(invocation.CanonicalArgs), "main-line-0199"));

        // Frozen Main line 200; original body retained once.
        Func<string[], int> branch0200 = args => { MachineReliabilityTests.WorkerDecodesTargetWordBoundaries(); return 0; };
        commands.Add(new CommandDescriptor("--target-decode-tests", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0200(invocation.CanonicalArgs), "main-line-0200"));

        // Frozen Main line 201; original body retained once.
        Func<string[], int> branch0201 = args => {
            FastEnglishOcrTests.RealFallbackIsFastAndReadsEnglish();
            return 0;
        };
        commands.Add(new CommandDescriptor("--fast-english-real-tests", StringComparison.OrdinalIgnoreCase,
            ArgumentGrammar.NoArguments(),
            invocation => branch0201(invocation.CanonicalArgs), "main-line-0201"));

        // Frozen Main line 206; original body retained once.
        Func<string[], int> branch0206 = args => { MachineReliabilityTests.SocialCountersDoNotStartMachineTranslation(); MachineReliabilityTests.FlashDisplayNameUsesApiIdentifier(); return 0; };
        commands.Add(new CommandDescriptor("--machine-counter-tests", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0206(invocation.CanonicalArgs), "main-line-0206"));

        // Frozen Main line 207; original body retained once.
        Func<string[], int> branch0207 = args => { MachineReliabilityTests.IntegrityAcceptsIdentifiersAdjacentToChinese(); return 0; };
        commands.Add(new CommandDescriptor("--integrity-boundary-tests", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0207(invocation.CanonicalArgs), "main-line-0207"));

        // Frozen Main line 208; original body retained once.
        Func<string[], int> branch0208 = args => { MachineReliabilityTests.MachineKeepsProtectedTokenSeparators(); return 0; };
        commands.Add(new CommandDescriptor("--machine-separator-tests", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0208(invocation.CanonicalArgs), "main-line-0208"));

        // Frozen Main line 209; original body retained once.
        Func<string[], int> branch0209 = args => { LiveRegionValidationTests.StaticPartialViewportRetriesMissingTranslation(); LiveRegionValidationTests.MotionDeadlineUsesNewestCapturedFrame(); LiveRegionValidationTests.NewViewportCancelsOldPipelineAndKeepsItsIdentity(); LiveRegionValidationTests.UncooperativeStaleWorkerStopsWithinBound(); LiveRegionValidationTests.ChangedGeometryDoesNotReuseStaleTranslation(); LiveRegionValidationTests.MediaAndScrolling(); LiveRegionValidationTests.StopWaitsForLateWorker(); LiveRegionValidationTests.StopBoundsUncooperativeWorkerWithoutDisposingItsSource(); LiveRegionValidationTests.StopBoundsUncooperativeWorkerWithoutDisposingItsSource(captureFailure: true); LiveRegionValidationTests.CaptureFailureDoesNotOverlapWorkers(); return 0; };
        commands.Add(new CommandDescriptor("--live-region-tests", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0209(invocation.CanonicalArgs), "main-line-0209"));

        // Frozen Main line 210; original body retained once.
        Func<string[], int> branch0210 = args => { FastEnglishOcrTests.UsesEnglishWithoutSupplement(); FastEnglishOcrTests.EmptyEnglishUsesFallback(); FastEnglishOcrTests.FailedEnglishUsesFallback(); FastEnglishOcrTests.CancellationDoesNotStartFallback(); FastEnglishOcrTests.MissingEnglishDoesNotUseChinese(); FastEnglishOcrTests.RegionalEnglishIsAccepted(); return 0; };
        commands.Add(new CommandDescriptor("--fast-english-tests", StringComparison.Ordinal,
            ArgumentGrammar.NoArguments(),
            invocation => branch0210(invocation.CanonicalArgs), "main-line-0210"));

        // Frozen Main line 211; original body retained once.
        Func<string[], int> branch0211 = args => { return RunRealTranslationProbe(); };
        commands.Add(new CommandDescriptor("--real-translation", StringComparison.OrdinalIgnoreCase,
            ArgumentGrammar.NoArguments(),
            invocation => branch0211(invocation.CanonicalArgs), "main-line-0211"));

        // Frozen Main line 212; original body retained once.
        Func<string[], int> branch0212 = args => { return RunRealMachineTranslationProbe(); };
        commands.Add(new CommandDescriptor("--real-machine-translation", StringComparison.OrdinalIgnoreCase,
            ArgumentGrammar.NoArguments(),
            invocation => branch0212(invocation.CanonicalArgs), "main-line-0212"));

        // Frozen Main line 213; original body retained once.
        Func<string[], int> branch0213 = args => { return RunRealLocalLmtFixtureProbe(args[1]); };
        commands.Add(new CommandDescriptor("--real-local-lmt-fixture", StringComparison.OrdinalIgnoreCase,
            new ArgumentGrammar(GrammarKind.Positional, new Cardinality(1, 1, new int[] { 1 }), new PositionRule[] { new PositionRule(1, new ValueRule("fixture-path", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")) }, Repeated: null),
            invocation => branch0213(invocation.CanonicalArgs), "main-line-0213"));

        // Frozen Main line 214; original body retained once.
        Func<string[], int> branch0214 = args => { return RunRealLocalLmtQualityProbe(); };
        commands.Add(new CommandDescriptor("--real-local-lmt-quality", StringComparison.OrdinalIgnoreCase,
            ArgumentGrammar.NoArguments(),
            invocation => branch0214(invocation.CanonicalArgs), "main-line-0214"));

        // Frozen Main line 215; original body retained once.
        Func<string[], int> branch0215 = args => { return RunRealPpOcrProbe(args[1]); };
        commands.Add(new CommandDescriptor("--real-ppocr", StringComparison.OrdinalIgnoreCase,
            new ArgumentGrammar(GrammarKind.Positional, new Cardinality(1, 1, new int[] { 1 }), new PositionRule[] { new PositionRule(1, new ValueRule("image-path", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")) }, Repeated: null),
            invocation => branch0215(invocation.CanonicalArgs), "main-line-0215"));

        // Frozen Main line 216; original body retained once.
        Func<string[], int> branch0216 = args => { return RunRealLayoutBaseline(args[1]); };
        commands.Add(new CommandDescriptor("--real-layout-baseline", StringComparison.OrdinalIgnoreCase,
            new ArgumentGrammar(GrammarKind.Positional, new Cardinality(1, 1, new int[] { 1 }), new PositionRule[] { new PositionRule(1, new ValueRule("image-path", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")) }, Repeated: null),
            invocation => branch0216(invocation.CanonicalArgs), "main-line-0216"));

        // Frozen Main line 217; original body retained once.
        Func<string[], int> branch0217 = args => { return RunRealLayoutSafety(args[1]); };
        commands.Add(new CommandDescriptor("--real-layout-safety", StringComparison.OrdinalIgnoreCase,
            new ArgumentGrammar(GrammarKind.Positional, new Cardinality(1, 1, new int[] { 1 }), new PositionRule[] { new PositionRule(1, new ValueRule("image-path", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")) }, Repeated: null),
            invocation => branch0217(invocation.CanonicalArgs), "main-line-0217"));

        // Frozen Main line 218; original body retained once.
        Func<string[], int> branch0218 = args => { return RunRealPpOcrTwiceProbe(args[1]); };
        commands.Add(new CommandDescriptor("--real-ppocr-twice", StringComparison.OrdinalIgnoreCase,
            new ArgumentGrammar(GrammarKind.Positional, new Cardinality(1, 1, new int[] { 1 }), new PositionRule[] { new PositionRule(1, new ValueRule("image-path", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")) }, Repeated: null),
            invocation => branch0218(invocation.CanonicalArgs), "main-line-0218"));

        // Frozen Main line 219; original body retained once.
        Func<string[], int> branch0219 = args => { return RunRealRapidOcrVariants(args[1]); };
        commands.Add(new CommandDescriptor("--real-rapidocr-variants", StringComparison.OrdinalIgnoreCase,
            new ArgumentGrammar(GrammarKind.Positional, new Cardinality(1, 1, new int[] { 1 }), new PositionRule[] { new PositionRule(1, new ValueRule("image-path", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")) }, Repeated: null),
            invocation => branch0219(invocation.CanonicalArgs), "main-line-0219"));

        // Frozen Main line 220; original body retained once.
        Func<string[], int> branch0220 = args => { return RunRealWindowsOcrProbe(args[1]); };
        commands.Add(new CommandDescriptor("--real-windows-ocr", StringComparison.OrdinalIgnoreCase,
            new ArgumentGrammar(GrammarKind.Positional, new Cardinality(1, 1, new int[] { 1 }), new PositionRule[] { new PositionRule(1, new ValueRule("image-path", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")) }, Repeated: null),
            invocation => branch0220(invocation.CanonicalArgs), "main-line-0220"));

        // Frozen Main line 221; original body retained once.
        Func<string[], int> branch0221 = args => { return RunRealDeepSeekProbe("English"); };
        commands.Add(new CommandDescriptor("--real-deepseek-english", StringComparison.OrdinalIgnoreCase,
            ArgumentGrammar.NoArguments(),
            invocation => branch0221(invocation.CanonicalArgs), "main-line-0221"));

        // Frozen Main line 222; original body retained once.
        Func<string[], int> branch0222 = args => { return RunRealImageDeepSeekProbe(args[1]); };
        commands.Add(new CommandDescriptor("--real-image-deepseek-english", StringComparison.OrdinalIgnoreCase,
            new ArgumentGrammar(GrammarKind.Positional, new Cardinality(1, 1, new int[] { 1 }), new PositionRule[] { new PositionRule(1, new ValueRule("image-path", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")) }, Repeated: null),
            invocation => branch0222(invocation.CanonicalArgs), "main-line-0222"));

        // Frozen Main line 223; original body retained once.
        Func<string[], int> branch0223 = args => { return RunRealImageDeepSeekProbe(args[1], "简体中文"); };
        commands.Add(new CommandDescriptor("--real-image-deepseek-chinese", StringComparison.OrdinalIgnoreCase,
            new ArgumentGrammar(GrammarKind.Positional, new Cardinality(1, 1, new int[] { 1 }), new PositionRule[] { new PositionRule(1, new ValueRule("image-path", ValueKind.NonEmptyString, AllowedValues: null, Comparison: StringComparison.Ordinal, Minimum: null, Maximum: null, ForbiddenCharacters: "")) }, Repeated: null),
            invocation => branch0223(invocation.CanonicalArgs), "main-line-0223"));

        // Frozen Main line 224; original body retained once.
        Func<string[], int> branch0224 = args => { return RunRealDeepSeekProbe(); };
        commands.Add(new CommandDescriptor("--real-deepseek", StringComparison.OrdinalIgnoreCase,
            ArgumentGrammar.NoArguments(),
            invocation => branch0224(invocation.CanonicalArgs), "main-line-0224"));

        // Frozen Main line 225; original body retained once.
        Func<string[], int> branch0225 = args => { return RunRealMultilingualDeepSeekProbe(); };
        commands.Add(new CommandDescriptor("--real-multilingual-deepseek", StringComparison.OrdinalIgnoreCase,
            ArgumentGrammar.NoArguments(),
            invocation => branch0225(invocation.CanonicalArgs), "main-line-0225"));

        // Frozen Main line 226; original body retained once.
        Func<string[], int> branch0226 = args => { return RunRealJapaneseQualityProbe(); };
        commands.Add(new CommandDescriptor("--real-japanese-quality", StringComparison.OrdinalIgnoreCase,
            ArgumentGrammar.NoArguments(),
            invocation => branch0226(invocation.CanonicalArgs), "main-line-0226"));

        // Frozen Main line 227; original body retained once.
        Func<string[], int> branch0227 = args => { return RunRealWindowsGraphicsCaptureProbe(); };
        commands.Add(new CommandDescriptor("--real-wgc-current", StringComparison.OrdinalIgnoreCase,
            ArgumentGrammar.NoArguments(),
            invocation => branch0227(invocation.CanonicalArgs), "main-line-0227"));

        // Frozen Main line 228; original body retained once.
        Func<string[], int> branch0228 = args => { return RunRealHotkeyDisplayProbe(); };
        commands.Add(new CommandDescriptor("--real-hotkey-display", StringComparison.OrdinalIgnoreCase,
            ArgumentGrammar.NoArguments(),
            invocation => branch0228(invocation.CanonicalArgs), "main-line-0228"));

        // The legacy runner is injected from the real Main array/execution loop, never a duplicate name list.
        commands.Add(new CommandDescriptor("--filter", StringComparison.OrdinalIgnoreCase,
            ArgumentGrammar.LegacyFilter(RequirePreparedRegistry(actualCases, "legacy-main-tests-array")),
            invocation => runLegacySelected(invocation.SelectedCases), "legacy-filter-1025"));
        if (additionalCommands is not null) commands.AddRange(additionalCommands);
        StrictCli.ValidateCatalog(commands);
        return commands;
    }

    private static ActualCaseRegistry RequirePreparedRegistry(IPreparedCaseBindings bindings, string id)
    {
        var registry = bindings.Require(id)
            ?? throw new InvalidOperationException("Missing actual case registry: " + id);
        if (!string.Equals(registry.Id, id, StringComparison.Ordinal))
            throw new InvalidOperationException("Wrong actual case registry binding: " + id);
        return registry;
    }
}
