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

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--local-lmt-batch-protocol-child") return LocalLmtBatchTransportTests.ProtocolChild();
        if (args.Contains("--local-lmt-batch-transport-tests")) { LocalLmtBatchTransportTests.StartupBannerAndPriorTurnAreNotReturnedAsTranslations().GetAwaiter().GetResult(); return 0; }
        if (args.Contains("--local-lmt-output-safety-tests")) { RejectsLocalLmtStartupBannerForHeading(); return 0; }
        if (args.Length > 1 && args[0] == "--local-lmt-sleep-child") return LocalLmtProcessLifetimeTests.SleepAsChild(args);
        if (args.Contains("--local-lmt-process-lifetime-tests")) { LocalLmtProcessLifetimeTests.CancellingOneShotStopsItsChild().GetAwaiter().GetResult(); return 0; }
        if (args.Contains("--workbench-grouping-tests")) { WorkbenchGroupingTests.RunAsync().GetAwaiter().GetResult(); return 0; }
        if (args.Length > 0 && args[0] == "--workbench-heldout-evidence") return WorkbenchHeldoutEvidence.Run(args);
        if (args.Length > 0 && args[0] == "--workbench-manual-evidence") return WorkbenchManualEvidence.Run(args);
        if (args.Length > 0 && args[0] == "--workbench-recovery-evidence") return WorkbenchRecoveryEvidence.Run(args);
        if (args.Contains("--mixed-script-ocr-tests")) { MixedScriptOcrTests.Run(); return 0; }
        if (args.Length > 0 && args[0] == "--ocr-region-evidence") return OcrProfileEvidence.ReplayRegions(args);
        if (args.Length > 0 && args[0] == "--ocr-crop") return OcrProfileEvidence.ProbeCrop(args);
        if (args.Length > 0 && args[0] == "--ocr-profile-evidence") return OcrProfileEvidence.Run(args);
        if (args.Length > 0 && args[0] == "--machine-startup-failure-probe") return MachineExecutionEvidenceTests.StartupFailureProbe(args);
        if (args.Contains("--machine-evidence-tests")) { MachineExecutionEvidenceTests.Run(); return 0; }
        if (args.Length > 0 && args[0] == "--machine-evidence-probe") return MachineExecutionEvidenceTests.Probe(args);
        if (args.Contains("--local-model-recovery-tests")) { RecoversLocalModelAfterFailedVerification(); CancelsLocalModelVerification(); return 0; }
        if (args.Contains("--local-model-catalog-tests")) { PreservesExplicitLocalModelWhenMissing(); ResolvesAuditedDeveloperLocalLmtModels(); VerifiesTranslationFileInventory(); return 0; }
        if (args.Contains("--local-package-catalog-tests")) { LocalModelPackageCatalogTests.RunAsync().GetAwaiter().GetResult(); return 0; }
        if (args.Contains("--capture-companion-tests"))
        {
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
        }
        if (args.Contains("--interface-language-tests")) { InterfaceLanguageTests.Run(); return 0; }
        if (args.Contains("--capture-language-tests")) { EnsureWpfResources(); CaptureLocalizationTests.Run(); return 0; }
        if (args.Contains("--capture-companion-language-tests")) { EnsureWpfResources(); CaptureCompanionLanguageTests.Run(); return 0; }
        if (args.Contains("--korean-region-tests")) { KoreanRegionTests.Run(); KeepsKoreanSpacesWhenJoiningOcrWrappedLines(); return 0; }
        if (args.Contains("--candidate-protected-synthetic") || args.Contains("--candidate-hymt2-synthetic") || args.Contains("--candidate-hymt2-terms-synthetic")) return QwenHttpWorkbenchProbe.RunSyntheticAsync(args).GetAwaiter().GetResult();
        if (args.Contains("--candidate-hymt2-context-synthetic")) return QwenHttpWorkbenchProbe.RunSyntheticAsync(args).GetAwaiter().GetResult();
        if (args.Contains("--workbench-real-hymt2") || args.Contains("--workbench-real-hymt2-terms")) return WorkbenchRealEvidence.Run(args);
        if (args.Contains("--candidate-http-cancel-probe") || args.Contains("--candidate-hymt2-cancel-probe")) return QwenHttpWorkbenchProbe.VerifyCancellationAsync(args).GetAwaiter().GetResult();
        if (args.Contains("--candidate-transport-tests")) { QwenWorkbenchProbe.VerifyTransportGuard(); return 0; }
        if (args.Contains("--resident-probe")) return ResidentModelProbe.Run(args).GetAwaiter().GetResult();
        if (args.Contains("--windows-ocr-probe")) return WindowsOcrProbe.Run(args);
        if (args.Contains("--workbench-real") || args.Contains("--workbench-real-common") || args.Contains("--workbench-real-lmt") || args.Contains("--workbench-real-lmt-chat") || args.Contains("--workbench-real-qwen") || args.Contains("--workbench-real-qwen-context") || args.Contains("--workbench-real-qwen-http") || args.Contains("--workbench-real-qwen-protected")) return WorkbenchRealEvidence.Run(args);
        if (args.Contains("--workbench-window-tests")) { WorkbenchWindowTests.Run(); return 0; }
        if (args.Contains("--workbench-session-tests")) { WorkbenchSessionTests.Run(); return 0; }
        if(args.Contains("--live-route-tests")) { LiveTranslationRouteTests.ExplicitSupportedRouteWinsAndMissingOrUnsupportedRouteUsesLiveSettings(); return 0; }
        if(args.Contains("--live-evidence-tests")) { LiveEvidenceTests.BundleHasLinkedContentAndChecksums(); LiveEvidenceTests.CacheAndWireDiagnosticsAreScoped(); Console.WriteLine("LIVE_EVIDENCE_TESTS_PASSED"); return 0; }
        if(args.Contains("--screenshot-evidence-tests")) { ScreenshotEvidenceTests.ScreenshotSwitchIsIndependentOfLiveTrace(); ScreenshotEvidenceTests.RecordsOnePrivateRunWithSourceRequestsOutcomeAndLayer(); ScreenshotEvidenceTests.RecordsActualFallbackSubsetAndMarksBrokenLayerIncomplete(); Console.WriteLine("SCREENSHOT_EVIDENCE_TESTS_PASSED"); return 0; }
        if(args.Contains("--live-animation-safety-tests")) { LiveAnimationTextSafetyTests.AdjacentAnimationInCropPaddingDoesNotStarveChangedText(); LiveAnimationTextSafetyTests.AnimationThroughTextCannotPublishOldTranslation(); LiveAnimationTextSafetyTests.OneChangedGlyphPixelCannotPassViewportCommit(); LiveAnimationTextSafetyTests.IncidentalChangeWithoutWordGeometryCannotAdvanceWitness(); LiveHoverTests.CaretShapedChangeInsideWordClearsAndRetries(); Console.WriteLine("LIVE_ANIMATION_SAFETY_TESTS_PASSED"); return 0; }
        if(args.Contains("--live-semantic-animation-tests")) { LiveSemanticAnimationTests.AnimatedBackingRebuildsTheErasurePatch(); LiveSemanticAnimationTests.MovingBackingKeepsTextAndChangedWordReplacesIt(); LiveSemanticAnimationTests.OldWorkerCannotPublishAfterTextChanges(); LiveSemanticAnimationTests.SourceIdentityUsesFullTextAndTolerantGeometry(); LiveSemanticAnimationTests.PartialSuccessRetriesWhileWordsAnimate(); LiveSemanticAnimationTests.UnknownOcrClearsOldTextWithinBound(); LiveSemanticAnimationTests.StopCancelsSemanticOcrWithoutLateOutput(); Console.WriteLine("LIVE_SEMANTIC_ANIMATION_TESTS_PASSED"); return 0; }
        if(args.Contains("--live-failure-ux-tests")) { LiveRepairTests.FailuresRecoverWithoutStoppingOrRetryCounter(); LiveRepairTests.FailureStaysActiveWithObservableReason(); return 0; }
        if(args.Contains("--live-worker-failure-tests")) { LiveCaptureFailureDrainsPendingTranslation(); LiveTranslationRetriesUnchangedFailureWithoutStopping(); LiveRegionValidationTests.CaptureFailureDoesNotOverlapWorkers(); return 0; }
        if(args.Contains("--machine-default-tests")) { MachineModeAndFallbackSettingsDefaultSafely(); SeparatesRegionAndLiveProviderSettings(); DefinesFinalV1SettingDefaults(); MigratesOldSettingsWithRecordingAndCleanupDefaults(); MigratesImplicitChineseProviderToMachine(); PreservesExplicitDeepSeekChoice(); return 0; }
        if(args.Contains("--live-recording-overlay-tests")) { LiveVisibilitySettingsTests.SelectedRegionBorderIsVisible(); LiveSelectionUsesPhysicalCursorCoordinates(); LiveVisibilitySettingsTests.OverlayKeepsTheSelectedPhysicalPixelBounds(); LiveVisibilitySettingsTests.VisibilityChoicesPersistIndependently(); LiveVisibilitySettingsTests.RecordingVisibilityControlsCaptureExclusion(); return 0; }
        if(args.Contains("--live-acceptance-harness-tests")) { LiveAcceptanceCapturesSourcePixelsFromTheTargetWindow(); LiveAcceptanceSignalsReadinessWhileTranslationIsStillRunning(); LiveAcceptanceClearsStaleTranslationsWhenTheSourceChanges(); LiveAcceptanceUsesTheFullViewportPipeline(); return 0; }
        if(args.Contains("--card-heading-boundary-tests")) { SeparatesShortCardHeadingFromDistantBody(); DoesNotMergeHeadingWithSmallerSubtitle(); DoesNotMergeWideParagraphAcrossCardColumns(); KeepsParagraphIntactBesideSidebarRow(); return 0; }
        if(args.Contains("--live-production-capture-tests")) { ProductionLiveTranslationUsesFixedScreenRegion(); ProductionLiveTranslationOwnsStartupAndCaptureLifetime(); LiveTranslationScreenCaptureTests.SamePhysicalRegionSamplesVisibleAtoBtoA().GetAwaiter().GetResult(); LiveTranslationScreenCaptureTests.RejectsInvalidCrossMonitorOrChangedRegion().GetAwaiter().GetResult(); LiveTranslationScreenCaptureTests.CapturesDesktopAndMultipleWindowPixelsWithoutWindowIdentity().GetAwaiter().GetResult(); LiveTranslationScreenCaptureTests.RejectsChangedTopologyAndWrongSizedFrame().GetAwaiter().GetResult(); WindowCaptureStopsOnSourceClosure(); WindowCaptureGeometryRejectsDrift(); MonitorCaptureDoesNotUseWindowGeometryFaults(); WindowCaptureRejectsInvalidTargetWindows(); Console.WriteLine("LIVE_SCREEN_CAPTURE_TESTS_PASSED"); return 0; }
        if(args.Contains("--live-many-failures-test")) { LiveRegionValidationTests.ManyTransientCaptureFailuresStillRecover(); Console.WriteLine("LIVE_MANY_FAILURES_TEST_PASSED"); return 0; }
        if(args.Contains("--live-partial-retry-tests")) { LivePartialRetryTests.StaticCloudPartialRequestsHaveAFiveMinuteUpperBound(); LivePartialRetryTests.ChangedTextInterruptsCloudBackoffWithinThreeSeconds(); LivePartialRetryTests.OneHundredTemporaryPartialsRemainRetryableAndSuccessResetsDelay(); LiveRegionValidationTests.StaticPartialViewportRetriesMissingTranslation(); Console.WriteLine("LIVE_PARTIAL_RETRY_TESTS_PASSED"); return 0; }
        if(args.Contains("--long-completion-tests")) { LongCaptureCompletionTests.CompletionIsDistinctFromCancellation(); return 0; }
        if(args.Contains("--selection-boundary-tests")) { KeepsPaddedOcrContextOutOfTranslation(); return 0; }
        if(args.Contains("--fallback-outcome-tests")) { MachineFallbackDoesNotCountPreservedTextAsTranslated(); return 0; }
        if (args.Contains("--testlab-evidence-tests"))
        {
            ReportsUnverifiedQualityDespiteStructuralMatch();
            ReportsKnownIncompleteContentAndLayout();
            LabelsOnlyStructuralExpectationAtRunLevel();
            return 0;
        }
        if (args.Contains("--intentional-preserve-outcome-tests"))
        {
            IntentionalIdentityPreservationIsCompleteButMissingOrdinaryLayoutStillFails();
            return 0;
        }
        if (args.Contains("--quality-oracle-tests"))
        {
            QualityOracleDetectsIndependentFailures();
            Console.WriteLine("QUALITY_ORACLE_TESTS_PASSED");
            return 0;
        }
        if (args.Contains("--quality-oracle-sentinel"))
            throw new InvalidOperationException("QUALITY_ORACLE_SENTINEL_FAILURE");
        if (args.Contains("--local-echo-guard-tests"))
        {
            RejectsStandalonePromptInstructions();
            return 0;
        }
        if (args.Contains("--ocr-evidence-tests"))
        {
            OcrEvidenceTests.PreservesRawScoreAndModelThroughGrouping();
            return 0;
        }
        if(args.Contains("--versioning-tests")) { KeepsApplicationAndInstallerVersioningConsistent(); return 0; }
        if(args.Contains("--tutorial-tests")) { PackagesAndOpensOfflineClipRayTutorial(); return 0; }
        if (args.Contains("--saved-file-clipboard-tests")) { SavedFileClipboardTests.Run(); return 0; }
        if(args.Contains("--stability-gate-tests")) { LiveTranslationStabilityGateTests.AnimationHasBoundedWaitAndStaticNeighborsProceed(); return 0; }
        if(args.Contains("--viewport-commit-tests")) { LiveTranslationViewportCommitPolicyTests.FullViewportCommitIsAtomicAndRejectsOnlyNewerViewport(); LiveTranslationViewportCommitPolicyTests.SafePartialViewportKeepsSuccessfulSiblings(); LiveTranslationViewportCommitPolicyTests.UnsafeViewportGeometryCannotBePresented(); LiveTranslationViewportCommitPolicyTests.OneUnsafeLayoutDoesNotBlankSafeTranslations(); LiveTranslationViewportCommitPolicyTests.AdjacentBodyParagraphMarginsAreSafeViewportGeometry(); LiveTranslationViewportCommitPolicyTests.AdjacentDifferentRoleTextRowsAreSafeViewportGeometry(); LiveTranslationViewportCommitPolicyTests.BodyParagraphsSharingATextRowRemainUnsafe(); LiveTranslationViewportCommitPolicyTests.OneFailedPrimaryRegionDoesNotBlankAnOtherwiseUsefulViewport(); LocalChangeTranslationTests.ViewportScrollPublishesTheCompleteCurrentFrame(); LiveRegionValidationTests.ChangedGeometryDoesNotReuseStaleTranslation(); LiveRegionValidationTests.MediaAndScrolling(); return 0; }
        if(args.Contains("--fixed-font-layout-tests")) { LiveTranslationLayoutCacheTests.FixedFontUsesRendererGeometryWithoutShrinking(); return 0; }
        if(args.Contains("--paragraph-crop-isolation-tests")) { LocalChangeTranslationTests.PaddingDoesNotMergeIndependentParagraphs(); return 0; }
        if(args.Contains("--adjacent-overlay-tests")) { LiveTranslationLayoutCacheTests.AdjacentNewParagraphsAreNotDiscardedAsOldOverlays(); return 0; }
        if(args.Contains("--font-tier-cache-tests")) { LiveTranslationLayoutCacheTests.NewParagraphKeepsEstablishedFontTier(); return 0; }
        if(args.Contains("--live-tighter-ocr-bounds-tests")) { LiveTranslationLayoutCacheTests.ChangedTextWithTighterOcrBoundsKeepsItsTranslation(); return 0; }
        if(args.Contains("--font-consistency-tests")) { FontConsistencyTests.SameSourceSizeAcrossEnglishAndJapanese(); FontConsistencyTests.AConstrainedSidebarCannotShrinkDistantArticleText(); return 0; }
        if(args.Contains("--real-x-typography-tests"))
        {
            var imageIndex = Array.IndexOf(args, "--real-x-typography-tests") + 1;
            if (imageIndex >= args.Length) throw new ArgumentException("Source image path is required.");
            FontConsistencyTests.RealXSingleLineKeepsPeerFont(args[imageIndex]);
            return 0;
        }
        if(args.Contains("--real-x-partial-failure-tests"))
        {
            var imageIndex = Array.IndexOf(args, "--real-x-partial-failure-tests") + 1;
            if (imageIndex >= args.Length) throw new ArgumentException("Source image path is required.");
            ScreenshotPartialFailureTests.RealXFailureKeepsUntranslatedPixels(args[imageIndex]);
            return 0;
        }
        if(args.Contains("--mixed-japanese-tests")) { CommonLanguageTests.MixedJapaneseKeepsFirstCharacterAndChineseLiteral(); return 0; }
        if(args.Contains("--paragraph-order-tests")) { CommonLanguageTests.ParagraphCannotJumpOverAnotherDetectedLine(); return 0; }
        if(args.Length==3 && args[0]=="--common-translation-probe") return RedesignFrameReplay.ProbeCommonTranslation(args[1],args[2]);
        if(args.Contains("--common-language-tests")) { CommonLanguageTests.ExplicitJapaneseHanReachesWorker(); CommonLanguageTests.SupplementalFragmentCannotReplaceCompleteLine(); CommonLanguageTests.JapaneseKanjiAreNotProtectedChinese(); return 0; }
        if(args.Length==2 && args[0]=="--common-ocr-probe") return RedesignFrameReplay.ProbeCommonOcr(args[1]);
        if(args.Length==3 && args[0]=="--onnx-dictionary-probe") return RedesignFrameReplay.ExtractOnnxDictionary(args[1],args[2]);
        if(args.Contains("--rapid-api-probe")) return RedesignFrameReplay.ProbeRapidApi();
        if(args.Contains("--live-hover-tests")) { LiveHoverTests.BackgroundHoverDoesNotScheduleTextAgain(); LiveHoverTests.HoverKeepsTranslationAndUpdatesBacking(); LiveHoverTests.CaretBlinkDoesNotClearOrRetranslateBody(); return 0; }
        if(args.Contains("--control-term-tests")) {SentenceTranslationTests.ControlTermsStayWithinControls();SentenceTranslationTests.SocialMachineControlsUseSharedUiTerms();return 0;}
        if(args.Contains("--icon-fragment-tests")) {SentenceTranslationTests.IconFragmentsAndStandaloneModelsDoNotBecomeProse();return 0;}
        if(args.Contains("--protected-neighbor-tests")) {SentenceTranslationTests.PreservedNeighborKeepsOriginalInkAtOverlappingOcrBoundary();SentenceTranslationTests.PreservedEmojiDoesNotShieldTranslatedLastGlyph();SentenceTranslationTests.SameColorPreservedNeighborKeepsInkOutsideItsOcrBox();return 0;}
        if(args.Contains("--redesign-bilingual-probe")) return RedesignFrameReplay.ProbeBilingual();
        if(args.Contains("--ocr-row-tests")) { SentenceTranslationTests.BlankParagraphGapKeepsSeparateBodyBlocks(); SentenceTranslationTests.CompleteSentenceRowsStayIndependentVisualRegions(); SentenceTranslationTests.ClippedViewportTopKeepsWrappedSentenceTogether(); SentenceTranslationTests.TallOcrWordCannotPullAdjacentRowIntoHeader(); return 0; }
        if(args.Length==2 && args[0]=="--redesign-frame-replay") return RedesignFrameReplay.Run(args[1]);
        if(args.Length>=3 && args[0]=="--live-replay-gate") return RedesignFrameReplay.RunLiveGate(args[1..]);
        if(args.Contains("--identity-peer-tests")) { SentenceTranslationTests.SeparateNameAndHandleStayIdentityMetadata(); return 0; }
        if(args.Contains("--account-name-policy-tests")) { AccountNameTranslationTests.LatinIdentityNamesAllowLocalizedDatesButNotUnchangedDates(); AccountNameTranslationTests.RecommendedAccountNamesArePreservedWithoutExemptingControls(); return 0; }
        if(args.Contains("--body-peer-tests")) { SentenceTranslationTests.NarrowControlCannotShrinkBodyTypography(); return 0; }
        if(args.Contains("--layout-cache-tests")) { LiveTranslationLayoutCacheTests.SameSemanticSourceKeepsFirstTypography(); return 0; }
        if(args.Contains("--live-cache-tests")) { LiveTranslationCacheTests.OnlyTransientFailedParagraphIsRetried(); LiveTranslationCacheTests.SemanticCacheKeepsNumbersAndRetriesTimeouts(); LiveTranslationCacheTests.NormalizesWhitespaceAndRetriesUnchangedNaturalLanguage(); LiveTranslationCacheTests.IntegrityFailureExpiresWithoutRepeatedImmediateRequests(); return 0; }
        if(args.Contains("--local-change-tests")) { LocalChangeTranslationTests.AnimationDoesNotStarveLocalText(); LocalChangeTranslationTests.PerpetualAnimationDoesNotRetranslateStableViewport(); LocalChangeTranslationTests.CropMarginNeverCutsNeighboringParagraph(); LocalChangeTranslationTests.OnlyChangedParagraphIsScheduled(); LocalChangeTranslationTests.SessionRebuildsChangedViewportFromFullFrame(); LocalChangeTranslationTests.CropCoordinatesReturnToSourcePixels(); LocalChangeTranslationTests.FullViewportScrollSchedulesOneFullFrame(); LocalChangeTranslationTests.SingleParagraphViewportScrollSchedulesOneFullFrame(); LocalChangeTranslationTests.SparseParagraphScrollDoesNotDependOnCanvasArea(); LocalChangeTranslationTests.FixedChromeDoesNotHideViewportScroll(); LocalChangeTranslationTests.DistantSmallEditsStayLocal(); LocalChangeTranslationTests.LargeLocalAnimationDoesNotBecomeAViewportScroll(); LocalChangeTranslationTests.LargeAnimationTouchingCaptionDoesNotBecomeAViewportScroll(); LocalChangeTranslationTests.ViewportScrollRestartsFromTheCurrentFullFrame(); LocalChangeTranslationTests.ViewportScrollPublishesTheCompleteCurrentFrame(); return 0; }
        if(args.Contains("--static-partial-tests")) { SentenceTranslationTests.FailedEmptyFrameWaitsForChangeWithoutPublishing(); return 0; }
        if(args.Contains("--renderer-geometry-tests")) { SentenceTranslationTests.SingleLineBodyDoesNotAlignToUnrelatedControls(); SentenceTranslationTests.TightOcrWordBoundsDoNotShrinkRealGlyphs(); SentenceTranslationTests.RendererUsesMeasuredLineGeometry(); return 0; }
        if (args.Contains("--sentence-redesign-tests")) { SentenceTranslationTests.InlineProtectedTextKeepsOneSentence(); SentenceTranslationTests.SocialIdentityStaysOutsideBody(); SentenceTranslationTests.NumericBodyKeepsSentenceContext(); SentenceTranslationTests.VisualLineBreakIsNotSentenceBoundary(); SentenceTranslationTests.CompleteOcrLinesAreIndependentMachineUnits(); return 0; }
        if (args.Contains("--source-noise-tests")) { MachineCalendarIntegrityTests.OcrHandleNoiseDoesNotEnterTheModel(); return 0; }
        if (args.Contains("--bilingual-machine-ocr-tests")) { MachineChineseSourceTests.MachineOcrRetainsChineseAndEnglish(); return 0; }
        if (args.Contains("--machine-ocr-plan-tests")) { MachineChineseSourceTests.MachineOcrLoadsOnlySupportedSourceScripts(); return 0; }
        if (args.Contains("--chinese-source-tests")) { MachineChineseSourceTests.ChineseSourceDoesNotStartEnglishTranslation(); MachineChineseSourceTests.MixedEnglishTranslatesWithoutSendingChineseToModel(); return 0; }
        if (args.Contains("--calendar-integrity-tests")) { MachineCalendarIntegrityTests.CalendarNamesCanBecomeNumericMonths(); return 0; }
        if (args.Contains("--written-number-integrity-tests")) { MachineCalendarIntegrityTests.WrittenNumberConversionKeepsExplicitNumerals(); MachineCalendarIntegrityTests.ConjoinedOcrDigitsRemainNumericInvariants(); return 0; }
        if (args.Contains("--web-age-tests")) { MachineCalendarIntegrityTests.WebAddressesAndAgesBypassTheModel(); MachineCalendarIntegrityTests.InlineWebAddressesStayOutOfModelInput(); return 0; }
        if (args.Contains("--neutral-region-tests")) { NeutralTextRegionTests.NeutralShadesKeepSentenceContext(); return 0; }
        if (args.Contains("--target-decode-tests")) { MachineReliabilityTests.WorkerDecodesTargetWordBoundaries(); return 0; }
        if (args.Contains("--fast-english-real-tests", StringComparer.OrdinalIgnoreCase))
        {
            FastEnglishOcrTests.RealFallbackIsFastAndReadsEnglish();
            return 0;
        }
        if (args.Contains("--machine-counter-tests")) { MachineReliabilityTests.SocialCountersDoNotStartMachineTranslation(); MachineReliabilityTests.FlashDisplayNameUsesApiIdentifier(); return 0; }
        if (args.Contains("--integrity-boundary-tests")) { MachineReliabilityTests.IntegrityAcceptsIdentifiersAdjacentToChinese(); return 0; }
        if (args.Contains("--machine-separator-tests")) { MachineReliabilityTests.MachineKeepsProtectedTokenSeparators(); return 0; }
        if (args.Contains("--live-region-tests")) { LiveRegionValidationTests.StaticPartialViewportRetriesMissingTranslation(); LiveRegionValidationTests.MotionDeadlineUsesNewestCapturedFrame(); LiveRegionValidationTests.NewViewportCancelsOldPipelineAndKeepsItsIdentity(); LiveRegionValidationTests.UncooperativeStaleWorkerStopsWithinBound(); LiveRegionValidationTests.ChangedGeometryDoesNotReuseStaleTranslation(); LiveRegionValidationTests.MediaAndScrolling(); LiveRegionValidationTests.StopWaitsForLateWorker(); LiveRegionValidationTests.StopBoundsUncooperativeWorkerWithoutDisposingItsSource(); LiveRegionValidationTests.StopBoundsUncooperativeWorkerWithoutDisposingItsSource(captureFailure: true); LiveRegionValidationTests.CaptureFailureDoesNotOverlapWorkers(); return 0; }
        if (args.Contains("--fast-english-tests")) { FastEnglishOcrTests.UsesEnglishWithoutSupplement(); FastEnglishOcrTests.EmptyEnglishUsesFallback(); FastEnglishOcrTests.FailedEnglishUsesFallback(); FastEnglishOcrTests.CancellationDoesNotStartFallback(); FastEnglishOcrTests.MissingEnglishDoesNotUseChinese(); FastEnglishOcrTests.RegionalEnglishIsAccepted(); return 0; }
        if (args.Contains("--real-translation", StringComparer.OrdinalIgnoreCase)) return RunRealTranslationProbe();
        if (args.Contains("--real-machine-translation", StringComparer.OrdinalIgnoreCase)) return RunRealMachineTranslationProbe();
        if (args.Length == 2 && string.Equals(args[0], "--real-local-lmt-fixture", StringComparison.OrdinalIgnoreCase)) return RunRealLocalLmtFixtureProbe(args[1]);
        if (args.Contains("--real-local-lmt-quality", StringComparer.OrdinalIgnoreCase)) return RunRealLocalLmtQualityProbe();
        if (args.Length == 2 && string.Equals(args[0], "--real-ppocr", StringComparison.OrdinalIgnoreCase)) return RunRealPpOcrProbe(args[1]);
        if (args.Length == 2 && string.Equals(args[0], "--real-layout-baseline", StringComparison.OrdinalIgnoreCase)) return RunRealLayoutBaseline(args[1]);
        if (args.Length == 2 && string.Equals(args[0], "--real-layout-safety", StringComparison.OrdinalIgnoreCase)) return RunRealLayoutSafety(args[1]);
        if (args.Length == 2 && string.Equals(args[0], "--real-ppocr-twice", StringComparison.OrdinalIgnoreCase)) return RunRealPpOcrTwiceProbe(args[1]);
        if (args.Length == 2 && string.Equals(args[0], "--real-rapidocr-variants", StringComparison.OrdinalIgnoreCase)) return RunRealRapidOcrVariants(args[1]);
        if (args.Length == 2 && string.Equals(args[0], "--real-windows-ocr", StringComparison.OrdinalIgnoreCase)) return RunRealWindowsOcrProbe(args[1]);
        if (args.Contains("--real-deepseek-english", StringComparer.OrdinalIgnoreCase)) return RunRealDeepSeekProbe("English");
        if (args.Length == 2 && string.Equals(args[0], "--real-image-deepseek-english", StringComparison.OrdinalIgnoreCase)) return RunRealImageDeepSeekProbe(args[1]);
        if (args.Length == 2 && string.Equals(args[0], "--real-image-deepseek-chinese", StringComparison.OrdinalIgnoreCase)) return RunRealImageDeepSeekProbe(args[1], "简体中文");
        if (args.Contains("--real-deepseek", StringComparer.OrdinalIgnoreCase)) return RunRealDeepSeekProbe();
        if (args.Contains("--real-multilingual-deepseek", StringComparer.OrdinalIgnoreCase)) return RunRealMultilingualDeepSeekProbe();
        if (args.Contains("--real-japanese-quality", StringComparer.OrdinalIgnoreCase)) return RunRealJapaneseQualityProbe();
        if (args.Contains("--real-wgc-current", StringComparer.OrdinalIgnoreCase)) return RunRealWindowsGraphicsCaptureProbe();
        if (args.Contains("--real-hotkey-display", StringComparer.OrdinalIgnoreCase)) return RunRealHotkeyDisplayProbe();
        var tests = new (string Name, Action Run)[]
        {
            ("Workbench session lifecycle and stale result isolation", WorkbenchSessionTests.Run),
            ("Live translation honors supported requests and its independent default", LiveTranslationRouteTests.ExplicitSupportedRouteWinsAndMissingOrUnsupportedRouteUsesLiveSettings),
            ("Completing a long capture is distinct from cancelling it", LongCaptureCompletionTests.CompletionIsDistinctFromCancellation),
            ("Saved images and videos provide clipboard files", SavedFileClipboardTests.Run),
            ("Separate social names and handles stay identity metadata", SentenceTranslationTests.SeparateNameAndHandleStayIdentityMetadata),
            ("Tall OCR words cannot pull the next row into an identity header", SentenceTranslationTests.TallOcrWordCannotPullAdjacentRowIntoHeader),
            ("Blank paragraph gaps keep independent body blocks", SentenceTranslationTests.BlankParagraphGapKeepsSeparateBodyBlocks),
            ("Complete sentence rows stay independent visual regions", SentenceTranslationTests.CompleteSentenceRowsStayIndependentVisualRegions),
            ("Clipped viewport-top prose keeps its sentence continuation", SentenceTranslationTests.ClippedViewportTopKeepsWrappedSentenceTogether),
            ("Icon fragments and standalone models stay identifiers", SentenceTranslationTests.IconFragmentsAndStandaloneModelsDoNotBecomeProse),
            ("Preserved adjacent link keeps original ink at an overlapping OCR boundary", SentenceTranslationTests.PreservedNeighborKeepsOriginalInkAtOverlappingOcrBoundary),
            ("Preserved emoji does not shield the last translated source glyph", SentenceTranslationTests.PreservedEmojiDoesNotShieldTranslatedLastGlyph),
            ("Same-color preserved neighbor retains ink beyond OCR bounds", SentenceTranslationTests.SameColorPreservedNeighborKeepsInkOutsideItsOcrBox),
            ("Decimal multipliers preserve their point", SentenceTranslationTests.DecimalMultiplierKeepsItsPoint),
            ("Control terminology cannot replace body translation", SentenceTranslationTests.ControlTermsStayWithinControls),
            ("Machine social UI controls use shared context terms", SentenceTranslationTests.SocialMachineControlsUseSharedUiTerms),
            ("Social counters cannot join body sentences", SentenceTranslationTests.SocialCounterCannotJoinBody),
            ("Narrow controls cannot shrink unrelated body typography", SentenceTranslationTests.NarrowControlCannotShrinkBodyTypography),
            ("Same semantic source preserves first text and typography", LiveTranslationLayoutCacheTests.SameSemanticSourceKeepsFirstTypography),
            ("Inline protected text retains one complete semantic sentence", SentenceTranslationTests.InlineProtectedTextKeepsOneSentence),
            ("Complete OCR lines remain independent machine translation units", SentenceTranslationTests.CompleteOcrLinesAreIndependentMachineUnits),
            ("Production worker packs complete sentences without token truncation", MachineReliabilityTests.WorkerChunksAtSentenceBoundaries),
            ("Only transient failed live paragraphs are retried", LiveTranslationCacheTests.OnlyTransientFailedParagraphIsRetried),
            ("Animation does not starve a stable local body change", LocalChangeTranslationTests.AnimationDoesNotStarveLocalText),
            ("Animated crops have bounded settling without blocking static neighbors", LiveTranslationStabilityGateTests.AnimationHasBoundedWaitAndStaticNeighborsProceed),
            ("Semantic live cache preserves numeric changes and retries timeouts", LiveTranslationCacheTests.SemanticCacheKeepsNumbersAndRetriesTimeouts),
            ("Semantic live cache normalizes whitespace and retries unchanged output", LiveTranslationCacheTests.NormalizesWhitespaceAndRetriesUnchangedNaturalLanguage),
            ("Semantic live cache retries integrity failures with backoff", LiveTranslationCacheTests.IntegrityFailureExpiresWithoutRepeatedImmediateRequests),
            ("Single-line body does not inherit control alignment", SentenceTranslationTests.SingleLineBodyDoesNotAlignToUnrelatedControls),
            ("Tight OCR word geometry preserves ordinary source glyph height", SentenceTranslationTests.TightOcrWordBoundsDoNotShrinkRealGlyphs),
            ("Social identities and durations do not break body semantics", SentenceTranslationTests.SocialIdentityStaysOutsideBody),
            ("Capture margin never cuts neighboring paragraphs", LocalChangeTranslationTests.CropMarginNeverCutsNeighboringParagraph),
            ("Crop padding does not merge independent changed paragraphs", LocalChangeTranslationTests.PaddingDoesNotMergeIndependentParagraphs),
            ("Local OCR coordinates return to source pixels exactly once", LocalChangeTranslationTests.CropCoordinatesReturnToSourcePixels),
            ("Live session rebuilds changed viewports from the full current frame", LocalChangeTranslationTests.SessionRebuildsChangedViewportFromFullFrame),
            ("A changed word schedules only its whole source paragraph", LocalChangeTranslationTests.OnlyChangedParagraphIsScheduled),
            ("A viewport scroll restarts from one current full frame", LocalChangeTranslationTests.ViewportScrollRestartsFromTheCurrentFullFrame),
            ("A viewport scroll publishes one complete atomic current frame", LocalChangeTranslationTests.ViewportScrollPublishesTheCompleteCurrentFrame),
            ("A viewport commit rejects only a newer viewport replacement", LiveTranslationViewportCommitPolicyTests.FullViewportCommitIsAtomicAndRejectsOnlyNewerViewport),
            ("Safe partial translation keeps successful siblings visible", LiveTranslationViewportCommitPolicyTests.SafePartialViewportKeepsSuccessfulSiblings),
            ("Unsafe viewport geometry cannot be presented", LiveTranslationViewportCommitPolicyTests.UnsafeViewportGeometryCannotBePresented),
            ("One unsafe layout preserves safe translations", LiveTranslationViewportCommitPolicyTests.OneUnsafeLayoutDoesNotBlankSafeTranslations),
            ("Adjacent body paragraph margins are safe viewport geometry", LiveTranslationViewportCommitPolicyTests.AdjacentBodyParagraphMarginsAreSafeViewportGeometry),
            ("Adjacent different-role text rows are safe viewport geometry", LiveTranslationViewportCommitPolicyTests.AdjacentDifferentRoleTextRowsAreSafeViewportGeometry),
            ("Body paragraphs sharing a text row remain unsafe", LiveTranslationViewportCommitPolicyTests.BodyParagraphsSharingATextRowRemainUnsafe),
            ("One failed primary region does not blank an otherwise useful viewport", LiveTranslationViewportCommitPolicyTests.OneFailedPrimaryRegionDoesNotBlankAnOtherwiseUsefulViewport),
            ("A full viewport scroll schedules one current full frame", LocalChangeTranslationTests.FullViewportScrollSchedulesOneFullFrame),
            ("A one-paragraph viewport scroll schedules one current full frame", LocalChangeTranslationTests.SingleParagraphViewportScrollSchedulesOneFullFrame),
            ("Sparse paragraph scrolling does not depend on canvas area", LocalChangeTranslationTests.SparseParagraphScrollDoesNotDependOnCanvasArea),
            ("Fixed chrome cannot hide a body viewport scroll", LocalChangeTranslationTests.FixedChromeDoesNotHideViewportScroll),
            ("Distant small edits stay local", LocalChangeTranslationTests.DistantSmallEditsStayLocal),
            ("A local animation does not become a viewport scroll", LocalChangeTranslationTests.LargeLocalAnimationDoesNotBecomeAViewportScroll),
            ("A large animation touching a caption stays local", LocalChangeTranslationTests.LargeAnimationTouchingCaptionDoesNotBecomeAViewportScroll),
            ("Failed empty frames wait for change without publishing", SentenceTranslationTests.FailedEmptyFrameWaitsForChangeWithoutPublishing),
            ("Raster renderer retains measured paragraph glyph geometry", SentenceTranslationTests.RendererUsesMeasuredLineGeometry),
            ("Numeric body translation retains full sentence context", SentenceTranslationTests.NumericBodyKeepsSentenceContext),
            ("Visual OCR line breaks are joined for semantic body input", SentenceTranslationTests.VisualLineBreakIsNotSentenceBoundary),
            ("Caret blink does not clear or retransmit translated body", LiveHoverTests.CaretBlinkDoesNotClearOrRetranslateBody),
            ("Caret change through source glyphs clears and retries", LiveHoverTests.CaretShapedChangeInsideWordClearsAndRetries),
            ("Calendar month names allow matching numeric month translations", MachineCalendarIntegrityTests.CalendarNamesCanBecomeNumericMonths),
            ("Written number conversion keeps explicit numeric identifiers", MachineCalendarIntegrityTests.WrittenNumberConversionKeepsExplicitNumerals),
            ("OCR-conjoined digits remain numeric invariants", MachineCalendarIntegrityTests.ConjoinedOcrDigitsRemainNumericInvariants),
            ("OCR account noise and social counter prefixes bypass model input", MachineCalendarIntegrityTests.OcrHandleNoiseDoesNotEnterTheModel),
            ("Already Chinese source bypasses English translation", MachineChineseSourceTests.ChineseSourceDoesNotStartEnglishTranslation),
            ("Japanese kanji stay in a complete translated sentence", CommonLanguageTests.JapaneseKanjiAreNotProtectedChinese),
            ("Tiny script fragments cannot replace full English lines", CommonLanguageTests.SupplementalFragmentCannotReplaceCompleteLine),
            ("Paragraphs cannot jump over intervening detected lines", CommonLanguageTests.ParagraphCannotJumpOverAnotherDetectedLine),
            ("Mixed Japanese keeps first glyph and Chinese literals", CommonLanguageTests.MixedJapaneseKeepsFirstCharacterAndChineseLiteral),
            ("English and Japanese of one source size use one Chinese font size", FontConsistencyTests.SameSourceSizeAcrossEnglishAndJapanese),
            ("A constrained sidebar cannot shrink distant article typography", FontConsistencyTests.AConstrainedSidebarCannotShrinkDistantArticleText),
            ("Static cloud partials use bounded retries while polling the screen", LivePartialRetryTests.StaticCloudPartialRequestsHaveAFiveMinuteUpperBound),
            ("Changed text interrupts a cloud partial retry within three seconds", LivePartialRetryTests.ChangedTextInterruptsCloudBackoffWithinThreeSeconds),
            ("One hundred temporary partials remain retryable and success resets delay", LivePartialRetryTests.OneHundredTemporaryPartialsRemainRetryableAndSuccessResetsDelay),
            ("New live paragraphs keep the established font tier", LiveTranslationLayoutCacheTests.NewParagraphKeepsEstablishedFontTier),
            ("A changed live sentence survives tighter OCR bounds", LiveTranslationLayoutCacheTests.ChangedTextWithTighterOcrBoundsKeepsItsTranslation),
            ("Fixed live fonts use renderer geometry without silently shrinking", LiveTranslationLayoutCacheTests.FixedFontUsesRendererGeometryWithoutShrinking),
            ("Adjacent new paragraphs preserve all accepted layouts", LiveTranslationLayoutCacheTests.AdjacentNewParagraphsAreNotDiscardedAsOldOverlays),
            ("Machine Windows OCR retains both Chinese and English source", MachineChineseSourceTests.MachineOcrRetainsChineseAndEnglish),
            ("Machine OCR loads only supported source scripts", MachineChineseSourceTests.MachineOcrLoadsOnlySupportedSourceScripts),
            ("Mixed English source retains Chinese spans without model input", MachineChineseSourceTests.MixedEnglishTranslatesWithoutSendingChineseToModel),
            ("Web addresses and compact ages bypass the machine model", MachineCalendarIntegrityTests.WebAddressesAndAgesBypassTheModel),
            ("Inline web addresses preserve prose and bypass model input", MachineCalendarIntegrityTests.InlineWebAddressesStayOutOfModelInput),
            ("Optional machine failure evidence cannot change rejection results", MachineCalendarIntegrityTests.FailedTranslationEvidenceCannotChangeResults),
            ("Live visibility choices persist independently", LiveVisibilitySettingsTests.VisibilityChoicesPersistIndependently),
            ("Live selected-region border is visible", LiveVisibilitySettingsTests.SelectedRegionBorderIsVisible),
            ("Live selection uses physical cursor coordinates", LiveSelectionUsesPhysicalCursorCoordinates),
            ("Live recording visibility controls capture exclusion", LiveVisibilitySettingsTests.RecordingVisibilityControlsCaptureExclusion),
            ("Live border visibility survives refresh", LiveVisibilitySettingsTests.BorderVisibilitySurvivesRefresh),
            ("Live overlay keeps the selected physical-pixel bounds", LiveVisibilitySettingsTests.OverlayKeepsTheSelectedPhysicalPixelBounds),
            ("Neutral foreground shades preserve sentence translation context", NeutralTextRegionTests.NeutralShadesKeepSentenceContext),
            ("Real machine worker decodes target word boundaries before caching", MachineReliabilityTests.WorkerDecodesTargetWordBoundaries),
            ("Fast English OCR skips unnecessary multilingual inference", FastEnglishOcrTests.UsesEnglishWithoutSupplement),
            ("Fast English OCR recovers an empty recognition", FastEnglishOcrTests.EmptyEnglishUsesFallback),
            ("Fast English OCR recovers an unavailable engine", FastEnglishOcrTests.FailedEnglishUsesFallback),
            ("Fast English OCR preserves cancellation", FastEnglishOcrTests.CancellationDoesNotStartFallback),
            ("Fast English OCR rejects non-English recognizers", FastEnglishOcrTests.MissingEnglishDoesNotUseChinese),
            ("Fast English OCR accepts regional English", FastEnglishOcrTests.RegionalEnglishIsAccepted),
            ("Machine preserves noisy social counters without inference", MachineReliabilityTests.SocialCountersDoNotStartMachineTranslation),
            ("Flash display name retains the API identifier", MachineReliabilityTests.FlashDisplayNameUsesApiIdentifier),
            ("Integrity recognizes unchanged identifiers next to Chinese", MachineReliabilityTests.IntegrityAcceptsIdentifiersAdjacentToChinese),
            ("Machine preserves separators around protected tokens", MachineReliabilityTests.MachineKeepsProtectedTokenSeparators),
            ("Protected machine failures retain rejected candidate evidence", MachineCalendarIntegrityTests.ProtectedFailureRecordsRejectedCandidateWithoutChangingResult),
            ("Live media does not starve text and scrolling removes stale regions", LiveRegionValidationTests.MediaAndScrolling),
            ("Changed live geometry does not reuse a stale translation", LiveRegionValidationTests.ChangedGeometryDoesNotReuseStaleTranslation),
            ("A newer viewport cancels stale work without relabelling its identity", LiveRegionValidationTests.NewViewportCancelsOldPipelineAndKeepsItsIdentity),
            ("An uncooperative stale worker stops within a bounded time", LiveRegionValidationTests.UncooperativeStaleWorkerStopsWithinBound),
            ("Background hover does not schedule unchanged glyphs", LiveHoverTests.BackgroundHoverDoesNotScheduleTextAgain),
            ("Hover retains translation and refreshes backing", LiveHoverTests.HoverKeepsTranslationAndUpdatesBacking),
            ("Live region stop waits for the source owner and rejects late output", LiveRegionValidationTests.StopWaitsForLateWorker),
            ("Live motion deadline uses the newest captured frame", LiveRegionValidationTests.MotionDeadlineUsesNewestCapturedFrame),
            ("Static partial live viewport retries missing translations", LiveRegionValidationTests.StaticPartialViewportRetriesMissingTranslation),
            ("One hundred transient live capture failures still recover", LiveRegionValidationTests.ManyTransientCaptureFailuresStillRecover),
            ("Live evidence bundle binds events and images", LiveEvidenceTests.BundleHasLinkedContentAndChecksums),
            ("Live evidence cache and provider scope are isolated", LiveEvidenceTests.CacheAndWireDiagnosticsAreScoped),
            ("Screenshot evidence switch is independent of live evidence", ScreenshotEvidenceTests.ScreenshotSwitchIsIndependentOfLiveTrace),
            ("Screenshot evidence records source, requests, outcome, and rendered layer", ScreenshotEvidenceTests.RecordsOnePrivateRunWithSourceRequestsOutcomeAndLayer),
            ("Screenshot evidence records fallback subset and incomplete layer", ScreenshotEvidenceTests.RecordsActualFallbackSubsetAndMarksBrokenLayerIncomplete),
            ("Crop padding animation does not starve changed text", LiveAnimationTextSafetyTests.AdjacentAnimationInCropPaddingDoesNotStarveChangedText),
            ("Animation through text cannot publish stale translation", LiveAnimationTextSafetyTests.AnimationThroughTextCannotPublishOldTranslation),
            ("Animated text keeps matching source and replaces a changed word", LiveSemanticAnimationTests.MovingBackingKeepsTextAndChangedWordReplacesIt),
            ("Animated backing rebuilds the source erasure patch", LiveSemanticAnimationTests.AnimatedBackingRebuildsTheErasurePatch),
            ("Old live worker cannot publish after source text changes", LiveSemanticAnimationTests.OldWorkerCannotPublishAfterTextChanges),
            ("Live source identity uses full text and tolerant geometry", LiveSemanticAnimationTests.SourceIdentityUsesFullTextAndTolerantGeometry),
            ("Unknown live OCR clears old text within a bound", LiveSemanticAnimationTests.UnknownOcrClearsOldTextWithinBound),
            ("Live Stop cancels semantic OCR without late output", LiveSemanticAnimationTests.StopCancelsSemanticOcrWithoutLateOutput),
            ("Partial success retries while source words animate", LiveSemanticAnimationTests.PartialSuccessRetriesWhileWordsAnimate),
            ("One changed glyph pixel invalidates viewport commit", LiveAnimationTextSafetyTests.OneChangedGlyphPixelCannotPassViewportCommit),
            ("Incidental change without word geometry cannot advance witness", LiveAnimationTextSafetyTests.IncidentalChangeWithoutWordGeometryCannotAdvanceWitness),
            ("Live region stop bounds an uncooperative worker", () => LiveRegionValidationTests.StopBoundsUncooperativeWorkerWithoutDisposingItsSource()),
            ("Live region capture failure bounds an uncooperative worker", () => LiveRegionValidationTests.StopBoundsUncooperativeWorkerWithoutDisposingItsSource(captureFailure: true)),
            ("Live region capture failure cannot overlap workers", LiveRegionValidationTests.CaptureFailureDoesNotOverlapWorkers),
            ("Machine GUI decodes UTF8 Chinese exactly", MachineReliabilityTests.GuiDecodesUtf8),
            ("Machine stderr cannot block translation", MachineReliabilityTests.StderrCannotBlock),
            ("Machine worker cancellation is bounded", MachineReliabilityTests.CancellationStopsWorker),
            ("Machine worker timeout kills its child", MachineReliabilityTests.TimeoutStopsWorker),
            ("Machine invalid response allows a fresh run", MachineReliabilityTests.InvalidResponseDoesNotPoisonNextRun),
            ("Machine fallback handles primary exceptions", MachineReliabilityTests.PrimaryExceptionFallsBack),
            ("Machine fallback handles primary count mismatch", MachineReliabilityTests.PrimaryCountMismatchFallsBack),
            ("Machine fallback propagates user cancellation", MachineReliabilityTests.PrimaryCancellationDoesNotFallBack),
            ("Acceptance rejects partial translation outcomes", MachineReliabilityTests.PartialAcceptanceIsFailure),
            ("Acceptance accepts successful translation outcomes", MachineReliabilityTests.SuccessfulAcceptanceIsSuccess),
            ("Live repair recovers without a retry counter", LiveRepairTests.FailuresRecoverWithoutStoppingOrRetryCounter),
            ("Live repair keeps a visible region border", LiveRepairTests.OverlayKeepsVisibleRegionBorder),
            ("Live repair exposes start and stop hotkeys", LiveRepairTests.LiveHotkeysExistInSettings),
            ("Live repair keeps a safe observable failure reason", LiveRepairTests.FailureStaysActiveWithObservableReason),
            ("Live repair uses shared typography rendering", LiveRepairTests.LiveUsesSharedTypographyRenderer),
            ("Live overlay rejects failed viewport results", LiveRepairTests.OverlayRejectsFailedViewportResults),
            ("Live repair preserves legacy conflicting shortcuts", LiveRepairTests.NewHotkeysDoNotResetLegacySettings),
            ("Recovery repair survives dispose and startup cleanup", RecoveryRepairTests.PreserveSurvivesDisposeAndOrphanCleanup),
            ("Recovery repair keeps screenshot edits after delivery failure", RecoveryRepairTests.CaptureDeliveryFailureKeepsEditingState),
            ("Recovery repair separates saved image from clipboard failure", RecoveryRepairTests.ClipboardFailureDoesNotLoseSavedImage),
            ("Long preview retries clipboard delivery after the file is saved", RecoveryRepairTests.LongPreviewCanRetryClipboardWithoutOwningSave),
            ("Capture repair restores the original Insert contract", CaptureRecordingRepairTests.FullScreenHotkeysFollowOriginalContract),
            ("Capture repair makes every taskbar reachable in free capture", CaptureRecordingRepairTests.FreeCaptureUsesWholeDesktopBounds),
            ("Recording repair keeps 30fps cadence on near 60Hz input", CaptureRecordingRepairTests.FramePacingDoesNotLoseRefreshTicks),
            ("Recording repair uses a bounded off-thread encoder queue", CaptureRecordingRepairTests.EncoderQueueIsBoundedAndRunsOffCaptureThread),
            ("Screenshot WGC maps working area into physical monitor", CaptureRecordingRepairTests.WgcMapsCropIntoPhysicalMonitor),
            ("Recording preview does not claim unperformed clipboard copy", CaptureRecordingRepairTests.RecordingPreviewDoesNotClaimUnperformedCopy),
            ("Machine output health rejects unchanged English", MachineReliabilityTests.UnchangedEnglishIsNotSuccessfulMachineTranslation),
            ("Recovery repair protects unsaved settings on close", RecoveryRepairTests.DirtySettingsRequireAnExplicitCloseDecision),
            ("Parses bounded region acceptance commands", ParsesBoundedRegionAcceptanceCommands),
            ("Reports a missing TestLab manifest without fabricating cases", ReportsMissingTestLabManifestWithoutFabricatingCases),
            ("Rejects an unsafe TestLab manifest case", RejectsUnsafeTestLabManifestCase),
            ("Rejects a traversal TestLab case id", RejectsTraversalTestLabCaseId),
            ("Compresses missing TestLab manifest fields into a failure", CompressesMissingTestLabManifestFieldsIntoFailure),
            ("Keeps TestLab artifacts inside an immutable run root", KeepsTestLabArtifactsInsideImmutableRunRoot),
            ("Writes a sanitized TestLab case failure for a corrupt image", WritesSanitizedTestLabCaseFailureForCorruptImage),
            ("Accepts an explicit protected fallback TestLab expectation", AcceptsProtectedFallbackTestLabExpectation),
            ("Rejects partial layout for an all-translated TestLab case", RejectsPartialAllTranslatedTestLabExpectation),
            ("Accepts successful unchanged protected regions in an all-translated TestLab case", AcceptsUnchangedProtectedRegionInAllTranslatedExpectation),
            ("Intentional identity preservation completes while missing ordinary layout still fails", IntentionalIdentityPreservationIsCompleteButMissingOrdinaryLayoutStillFails),
            ("Renders a TestLab artifact from an async MTA continuation", RendersTestLabArtifactFromAsyncMtaContinuation),
            ("Applies the shared typography plan in TestLab rendering", AppliesSharedTypographyPlanInTestLabRendering),
            ("Writes chunk two protected marker failures into the TestLab report", WritesChunkTwoProtectedMarkerFailureIntoTestLabReport),
            ("Classifies sanitized Local LMT failure families in TestLab", ClassifiesSanitizedLocalLmtFailureFamiliesInTestLab),
            ("Classifies an unmerged provider result in TestLab", ClassifiesUnmergedProviderResultInTestLab),
            ("Classifies exhausted DeepSeek structural recovery in TestLab", ClassifiesExhaustedDeepSeekStructuralRecoveryInTestLab),
            ("Classifies an unrecognized provider status in TestLab without exposing it", ClassifiesUnrecognizedProviderStatusInTestLabWithoutExposingIt),
            ("Reports sanitized per-result TestLab failure diagnostics", ReportsSanitizedPerResultTestLabFailureDiagnostics),
            ("Reports sanitized Local LMT token-integrity context", ReportsSanitizedLocalLmtTokenIntegrityContext),
            ("Reports sanitized Local LMT early-output context", ReportsSanitizedLocalLmtEarlyOutputContext),
            ("Uses an explicit installed app root for TestLab runtime settings", UsesExplicitInstalledAppRootForTestLabRuntimeSettings),
            ("Stops the shared application orchestrator at OCR failure", StopsSharedApplicationOrchestratorAtOcrFailure),
            ("Reports OCR and translation progress at the real pipeline boundary", ReportsVisualTranslationPipelineProgress),
            ("Reports translation pipeline stage timings without text", ReportsTranslationPipelineStageTimingsWithoutText),
            ("Ignores a failing translation pipeline telemetry observer", IgnoresFailingTranslationPipelineTelemetryObserver),
            ("Keeps overlay error copy when translation logging fails", KeepsOverlayErrorCopyWhenTranslationLoggingFails),
            ("Uses bounded live translation refresh delays", UsesBoundedLiveTranslationRefreshDelays),
            ("Live translation discards a changed screen before publish", LiveTranslationDiscardsChangedScreen),
            ("Live translation cancels changed content while work is pending", LiveTranslationCancelsChangedPendingContent),
            ("Live capture failure drains its pending translation", LiveCaptureFailureDrainsPendingTranslation),
            ("Exposes only current DeepSeek Flash", ExposesOnlyCurrentFlash),
            ("Prices V4.1 Flash with weekend off peak", PricesCurrentFlash),
            ("Migrates legacy Pro without changing other settings", MigratesLegacyPro),
            ("Bundled machine supports a persistent worker", BundledMachineSupportsPersistentWorker),
            ("Machine timeout does not retry every segment", MachineTimeoutDoesNotRetryEverySegment),
            ("Live translation never overlaps", LiveTranslationNeverOverlaps),
            ("Live translation skips identical captures", LiveTranslationSkipsIdenticalCaptures),
            ("Live translation retries unchanged failures without stopping", LiveTranslationRetriesUnchangedFailureWithoutStopping),
            ("Cancelled live translation cannot publish a late frame", CancelledLiveTranslationCannotPublishLateFrame),
            ("Live selection produces one physical region then closes", LiveSelectionProducesOnePhysicalRegionThenCloses),
            ("Live selection rejects a cross monitor region", LiveSelectionRejectsCrossMonitorRegion),
            ("Live overlay fails closed when recording exclusion is unavailable", LiveOverlayFailsClosedWhenExclusionFails),
            ("Window WGC maps an on-screen selection without DPI scaling", WindowWgcMapsAnOnScreenSelectionWithoutDpiScaling),
            ("Live acceptance captures source pixels from the target window", LiveAcceptanceCapturesSourcePixelsFromTheTargetWindow),
            ("Live acceptance signals readiness while translation is still running", LiveAcceptanceSignalsReadinessWhileTranslationIsStillRunning),
            ("Live acceptance clears stale translations when the source changes", LiveAcceptanceClearsStaleTranslationsWhenTheSourceChanges),
            ("Live acceptance uses the full viewport translation pipeline", LiveAcceptanceUsesTheFullViewportPipeline),
            ("Production live translation uses a fixed physical screen region", ProductionLiveTranslationUsesFixedScreenRegion),
            ("Production live translation owns startup and capture lifetime", ProductionLiveTranslationOwnsStartupAndCaptureLifetime),
            ("Window capture stops when its source closes", WindowCaptureStopsOnSourceClosure),
            ("Window capture rejects geometry drift", WindowCaptureGeometryRejectsDrift),
            ("Monitor capture does not use window geometry faults", MonitorCaptureDoesNotUseWindowGeometryFaults),
            ("Window capture rejects invalid target windows", WindowCaptureRejectsInvalidTargetWindows),
            ("Live overlay uses an independent stop control", LiveOverlayUsesIndependentStopControl),
            ("Live stop control stays outside the selected region", LiveStopControlStaysOutsideSelectedRegion),
            ("Machine route sends every request to the machine provider", MachineRouteSendsEveryRequestToMachineProvider),
            ("Machine fallback retries only failed primary blocks", MachineFallbackRetriesOnlyFailedPrimaryBlocks),
            ("Machine fallback preserves a primary failure when recovery fails", MachineFallbackPreservesPrimaryFailureWhenRecoveryFails),
            ("Machine fallback does not report intentionally preserved text as translated", MachineFallbackDoesNotCountPreservedTextAsTranslated),
            ("Application translation preserves machine fallback identity", ApplicationTranslationPreservesMachineFallbackIdentity),
            ("Overlay status distinguishes direct machine and fallback recovery", OverlayStatusDistinguishesMachineTranslation),
            ("Machine route never falls through to a cloud provider", MachineRouteNeverFallsThroughToCloudProvider),
            ("Local machine provider batches protected English to Chinese requests", LocalMachineProviderBatchesProtectedEnglishToChineseRequests),
            ("Local machine provider preserves protected-only text without invoking the model", LocalMachineProviderPreservesProtectedOnlyTextWithoutInvokingModel),
            ("Local machine provider translates around protected invariants", LocalMachineProviderTranslatesAroundProtectedInvariants),
            ("Local machine provider reports a sanitized invariant mismatch", LocalMachineProviderReportsSanitizedInvariantMismatch),
            ("Local machine provider bypasses identity metadata", LocalMachineProviderBypassesIdentityMetadata),
            ("Local machine provider preserves metadata after an invariant mismatch", LocalMachineProviderPreservesMetadataAfterInvariantMismatch),
            ("Local machine provider rejects unsupported language pairs locally", LocalMachineProviderRejectsUnsupportedLanguagePairsLocally),
            ("Local machine provider isolates a failed batch", LocalMachineProviderIsolatesFailedBatch),
            ("Bundled machine worker translates English to Chinese offline", BundledMachineWorkerTranslatesEnglishToChineseOffline),
            ("Bundled machine worker chunks oversized input without truncating the tail", BundledMachineWorkerChunksOversizedInputWithoutTruncatingTail),
            ("Machine mode and fallback settings default safely", MachineModeAndFallbackSettingsDefaultSafely),
            ("Settings routes Machine verification through the offline provider", SettingsRoutesMachineVerificationThroughOfflineProvider),
            ("Standalone Local LMT uses configured machine fallback", StandaloneLocalLmtUsesConfiguredMachineFallback),
            ("Explains OCR and a cold local-model load during visual translation", ExplainsVisualTranslationProgress),
            ("Classifies a shared application layout merge failure", ClassifiesSharedApplicationLayoutMergeFailure),
            ("Fails shared application completion when a changed region is not rendered", FailsCompletionWhenChangedRegionIsNotRendered),
            ("Reports sanitized layout rejection reasons through the shared orchestrator", ReportsSanitizedLayoutRejectionReasonsThroughSharedOrchestrator),
            ("Routes overlay translation through the shared orchestrator", RoutesOverlayTranslationThroughSharedOrchestrator),
            ("Finds workspace root from nested directory", FindsWorkspaceRootFromNestedDirectory),
            ("Returns no workspace root for an installed layout", ReturnsNoWorkspaceRootForAnInstalledLayout),
            ("Saves collision-free lossless PNG in workspace", SavesCollisionFreeLosslessPngInWorkspace),
            ("Saves unique readable PNGs for concurrent timestamp collisions", SavesUniqueReadablePngsForConcurrentTimestampCollisions),
            ("Uses Windows Graphics Capture before GDI for a game-compatible screenshot", UsesWindowsGraphicsCaptureBeforeGdi),
            ("Falls back to GDI when Windows Graphics Capture is unavailable", FallsBackToGdiWhenWindowsGraphicsCaptureIsUnavailable),
            ("Marks Windows Graphics Capture frames as already having system feedback", MarksWindowsGraphicsCaptureFramesAsSystemFeedback),
            ("Avoids a second app pulse after Windows Graphics Capture feedback", AvoidsDuplicateWindowsGraphicsCaptureFeedback),
            ("Defines the success feedback pulse", DefinesSuccessFeedbackPulse),
            ("Defines the failure feedback pulses", DefinesFailureFeedbackPulses),
            ("Configures edge feedback as non-interactive", ConfiguresEdgeFeedbackAsNonInteractive),
            ("Resolves repeated interactive capture as cancellation", ResolvesRepeatedInteractiveCaptureAsCancellation),
            ("Starts recording when a recording-mode selection is confirmed", StartsRecordingWhenRecordingModeSelectionIsConfirmed),
            ("Keeps recording launch Enter and Escape deterministic", KeepsRecordingLaunchEnterAndEscapeDeterministic),
            ("Uses a purpose-built toolbar for recording selection", UsesAPurposeBuiltToolbarForRecordingSelection),
            ("Blocks a second recording selection while recording is active", BlocksASecondRecordingSelectionWhileRecordingIsActive),
            ("Loads the packaged application icon", LoadsPackagedApplicationIcon),
            ("Falls back only when the application icon is missing", FallsBackOnlyWhenApplicationIconIsMissing),
            ("Rejects a corrupt application icon", RejectsCorruptApplicationIcon),
            ("Defines transparent application icon and toolbar geometry", DefinesTransparentApplicationIconAndToolbarGeometry),
            ("Optically aligns capture toolbar glyphs", OpticallyAlignsCaptureToolbarGlyphs),
            ("Defines final v1 setting defaults", DefinesFinalV1SettingDefaults),
            ("Defines separate full-screen hotkeys with and without the taskbar", DefinesSeparateFullScreenHotkeys),
            ("Keeps application and installer versioning consistent", KeepsApplicationAndInstallerVersioningConsistent),
            ("Creates organized output directories before the first capture", CreatesOrganizedOutputDirectoriesBeforeTheFirstCapture),
            ("Classifies translated preserved failed and layout-rejected regions", ClassifiesTranslationRunOutcomes),
            ("Keeps translation diagnostics best effort when result counts mismatch", KeepsTranslationDiagnosticsBestEffortWhenResultCountsMismatch),
            ("Serializes a privacy-safe translation run log", SerializesAPrivacySafeTranslationRunLog),
            ("Treats translation run log diagnostics as best effort", TreatsTranslationRunDiagnosticsAsBestEffort),
            ("Uses the release diagnostics root for translation run logs", UsesTheReleaseDiagnosticsRootForTranslationRunLogs),
            ("Uses the development diagnostics root for translation run logs", UsesTheDevelopmentDiagnosticsRootForTranslationRunLogs),
            ("Defines installed application paths without a source workspace", DefinesInstalledApplicationPathsWithoutASourceWorkspace),
            ("Separates development and installed application profiles", SeparatesDevelopmentAndInstalledApplicationProfiles),
            ("Allows one tray owner and forwards a second launch", AllowsOneTrayOwnerAndForwardsSecondLaunch),
            ("Routes repeat launches while bypassing maintenance modes", RoutesRepeatLaunchesAndBypassesMaintenance),
            ("Acquires the tray owner before initializing resident resources", AcquiresTrayOwnerBeforeResidentResources),
            ("Defines the scaled restrained settings signature", DefinesScaledSettingsSignature),
            ("Packages and opens the offline ClipRay tutorial", PackagesAndOpensOfflineClipRayTutorial),
            ("Offers uninstall only from an installed app and documents both entry points", OffersInstalledUninstallAndDocumentsBothEntryPoints),
            ("Respects Windows client-area animation for signature motion", RespectsClientAreaAnimationForSignatureMotion),
            ("Defines recording and cleanup settings controls", DefinesRecordingAndCleanupSettingsControls),
            ("Defines installer UX, folder picker, and startup task", DefinesInstallerUxFolderPickerAndStartupTask),
            ("Builds a version-gated recording diagnostic patch without bundled models", BuildsVersionGatedRecordingDiagnosticPatchWithoutBundledModels),
            ("Configures a selected installer cleanup task through the maintenance path", ConfiguresSelectedInstallerCleanupThroughMaintenancePath),
            ("Terminates ClipRay before uninstall cleanup and clarifies the uninstall UI", TerminatesClipRayBeforeUninstallCleanupAndClarifiesTheUninstallUi),
            ("Resolves only the default Outputs folder for uninstall recycling", ResolvesOnlyDefaultOutputsForUninstallRecycling),
            ("Uses a fixed credential slot in Settings", UsesAFixedCredentialSlotInSettings),
            ("Uses one fixed local DeepSeek credential slot", UsesOneFixedLocalDeepSeekCredentialSlot),
            ("Migrates a selected legacy credential into the fixed slot", MigratesSelectedLegacyCredentialIntoFixedSlot),
            ("Migrates the selected credential before application startup routing", MigratesCredentialBeforeApplicationStartupRouting),
            ("Defines primary and optional secondary hotkey controls", DefinesPrimaryAndOptionalSecondaryHotkeyControls),
            ("Parses cleanup settings input", ParsesCleanupSettingsInput),
            ("Normalizes forgiving cleanup time input", NormalizesForgivingCleanupTimeInput),
            ("Routes Ctrl S through the Settings apply path", RoutesCtrlSThroughTheSettingsApplyPath),
            ("Switches Settings primary action only when the draft changes", SwitchesSettingsPrimaryActionOnlyWhenTheDraftChanges),
            ("Round-trips settings JSON without secrets", RoundTripsSettingsJsonWithoutSecrets),
            ("Migrates old settings with recording and cleanup defaults", MigratesOldSettingsWithRecordingAndCleanupDefaults),
            ("Migrates an implicit Chinese provider to Machine", MigratesImplicitChineseProviderToMachine),
            ("Preserves an explicit DeepSeek choice", PreservesExplicitDeepSeekChoice),
            ("Enables machine fallback when the persisted choice is absent", EnablesMachineFallbackWhenPersistedChoiceIsAbsent),
            ("Preserves an explicitly disabled machine fallback", PreservesExplicitlyDisabledMachineFallback),
            ("Rebases a complete legacy output layout to the current install root", RebasesCompleteLegacyOutputLayoutToCurrentInstallRoot),
            ("Rebases the historical spaced long-capture layout to the current install root", RebasesHistoricalSpacedLongCaptureLayoutToCurrentInstallRoot),
            ("Marks a rebased legacy output layout for startup persistence", MarksRebasedLegacyOutputLayoutForStartupPersistence),
            ("Preserves explicitly custom output directories", PreservesExplicitlyCustomOutputDirectories),
            ("Rejects cleanup retention outside the safe range", RejectsCleanupRetentionOutsideTheSafeRange),
            ("Rejects conflicting hotkeys", RejectsConflictingHotkeys),
            ("Reserves fixed overlay edit hotkeys", ReservesFixedOverlayEditHotkeys),
            ("Rejects fixed edit hotkeys during capture and long-capture validation", RejectsFixedEditHotkeysDuringCaptureAndLongCaptureValidation),
            ("Allows a single K as a custom hotkey", AllowsASingleKAsACustomHotkey),
            ("Enumerates an optional secondary hotkey", EnumeratesAnOptionalSecondaryHotkey),
            ("Formats active hotkeys for user feedback", FormatsActiveHotkeysForUserFeedback),
            ("Rejects conflicts from secondary global hotkeys", RejectsConflictsFromSecondaryGlobalHotkeys),
            ("Rejects conflicts from secondary overlay hotkeys", RejectsConflictsFromSecondaryOverlayHotkeys),
            ("Matches either primary or secondary hotkey", MatchesEitherPrimaryOrSecondaryHotkey),
            ("Wires secondary global and long-capture hotkeys into runtime", WiresSecondaryGlobalAndLongCaptureHotkeysIntoRuntime),
            ("Releases global hotkeys while settings captures a replacement", ReleasesGlobalHotkeysWhileSettingsCapturesAReplacement),
            ("Routes screenshots to configured directory", RoutesScreenshotsToConfiguredDirectory),
            ("Routes long captures to a readable daily path", RoutesLongCapturesToReadableDailyPath),
            ("Routes recordings to the configured directory", RoutesRecordingsToTheConfiguredDirectory),
            ("Deletes only expired completed ClipRay outputs", DeletesOnlyExpiredCompletedClipRayOutputs),
            ("Includes the dedicated long-capture output in cleanup", IncludesDedicatedLongCaptureOutputInCleanup),
            ("Keeps cleanup boundary incomplete recovery and unrelated files", KeepsCleanupBoundaryIncompleteRecoveryAndUnrelatedFiles),
            ("Keeps owned-looking files below non-date subdirectories", KeepsOwnedLookingFilesBelowNonDateSubdirectories),
            ("Deduplicates cleanup roots and removes only empty date folders", DeduplicatesCleanupRootsAndRemovesOnlyEmptyDateFolders),
            ("Rejects reparse point traversal during cleanup", RejectsReparsePointTraversalDuringCleanup),
            ("Continues cleanup after a locked file failure", ContinuesCleanupAfterALockedFileFailure),
            ("Does not build a scheduled cleanup plan while disabled", DoesNotBuildAScheduledCleanupPlanWhileDisabled),
            ("Round-trips a privacy-safe cleanup run status", RoundTripsPrivacySafeCleanupRunStatus),
            ("Formats the last cleanup execution for Settings", FormatsLastCleanupExecutionForSettings),
            ("Lists only masked credential metadata", ListsOnlyMaskedCredentialMetadata),
            ("Requires two confirmations before deleting the fixed local credential", RequiresTwoConfirmationsBeforeDeletingALocalCredential),
            ("Rejects structurally invalid DeepSeek credentials", RejectsStructurallyInvalidDeepSeekCredentials),
            ("Masks the current credential with an exact hidden length", MasksTheCurrentCredentialWithAnExactHiddenLength),
            ("Restores the prior credential when metadata replacement fails", RestoresPriorCredentialWhenMetadataReplacementFails),
            ("Restores the prior credential when metadata deletion fails", RestoresPriorCredentialWhenMetadataDeletionFails),
            ("Deletes every ClipRay credential during uninstall cleanup", DeletesEveryClipRayCredentialDuringUninstallCleanup),
            ("Generates current-user startup command", GeneratesCurrentUserStartupCommand),
            ("Reconciles installer startup state into Settings", ReconcilesInstallerStartupStateIntoSettings),
            ("Builds the exact current-user cleanup task definition", BuildsTheExactCurrentUserCleanupTaskDefinition),
            ("Builds the selected weekly cleanup task definition", BuildsTheSelectedWeeklyCleanupTaskDefinition),
            ("Treats both Task Scheduler not-found exception shapes as missing", TreatsBothTaskSchedulerNotFoundExceptionShapesAsMissing),
            ("Wires cleanup last-run status into Settings", WiresCleanupLastRunStatusIntoSettings),
            ("Repairs an enabled cleanup schedule during startup", RepairsAnEnabledCleanupScheduleDuringStartup),
            ("Routes cleanup schedule enable and disable through settings apply", RoutesCleanupScheduleEnableAndDisableThroughSettingsApply),
            ("Does not touch startup when only cleanup changes", DoesNotTouchStartupWhenOnlyCleanupChanges),
            ("Skips Task Scheduler when cleanup settings remain disabled", SkipsTaskSchedulerWhenCleanupSettingsRemainDisabled),
            ("Rolls back cleanup schedule when settings save fails", RollsBackCleanupScheduleWhenSettingsSaveFails),
            ("Does not save settings when cleanup schedule registration fails", DoesNotSaveSettingsWhenCleanupScheduleRegistrationFails),
            ("Rolls back startup when settings save fails", RollsBackStartupWhenSettingsSaveFails),
            ("Preserves primary error when startup rollback fails", PreservesPrimaryErrorWhenStartupRollbackFails),
            ("Recovers invalid settings JSON with a warning", RecoversInvalidSettingsJsonWithWarning),
            ("Recovers persisted hotkey conflicts with a warning", RecoversPersistedHotkeyConflictsWithWarning),
            ("Snaps the topmost eligible DWM candidate under the pointer", SnapsTopmostEligibleCandidate),
            ("Filters ineligible snap candidates before selecting a window", FiltersIneligibleSnapCandidates),
            ("Snaps ordinary ClipRay windows while excluding the overlay itself", SnapsOrdinaryClipRayWindowsButNotOverlay),
            ("Snaps primary and secondary Windows taskbars", SnapsWindowsTaskbars),
            ("Falls back to the frozen monitor for a fullscreen surface without a DWM window", FallsBackToFrozenMonitorWithoutDwmWindow),
            ("Enumerates the frozen window snapshot only once", EnumeratesFrozenWindowSnapshotOnlyOnce),
            ("Left press locks the current snap proposal for the gesture", LeftPressLocksSnapProposal),
            ("Left press permanently rejects pending snap refinements", LeftPressRejectsPendingSnapRefinements),
            ("Keeps a refined snap proposal while the pointer remains inside it", KeepsRefinedSnapProposalWhilePointerRemainsInside),
            ("Clicking a snap proposal resolves to a locked selection", ClickingSnapProposalLocksSelection),
            ("Dragging a snap proposal resolves to free draw at eight pixels", DraggingSnapProposalStartsFreeDraw),
            ("Canvas left press stops snap before picker routing", CanvasLeftPressStopsSnapBeforePickerRouting),
            ("Control left press also terminates pending snap refinement", ControlLeftPressStopsSnapBeforeControlRouting),
            ("Orders nested UI elements ahead of their top window", OrdersNestedUiElementsAheadOfWindow),
            ("Can prefer the top window over nested UI elements", PrefersTopWindowWhenRequested),
            ("Filters invalid nested UI element rectangles", FiltersInvalidNestedUiElementRectangles),
            ("Uses outer window snapping unless Ctrl is held", UsesOuterWindowSnappingUnlessCtrlIsHeld),
            ("Mentions the momentary Ctrl nested snap mode in Settings", MentionsMomentaryCtrlNestedSnapModeInSettings),
            ("Redraws inside a locked selection after a material drag", RedrawsInsideLockedSelectionAfterMaterialDrag),
            ("Double Escape closes only inside the 1.5-second window", DoubleEscapeClosesInsideWindow),
            ("Double Escape expires at 1.5 seconds and resets after pointer completion", DoubleEscapeExpiresAndResets),
            ("Defines the complete ordered toolbar with accessible hit targets", DefinesCompleteToolbarShell),
            ("Uses the locked frameless toolbar visual language", UsesLockedFramelessToolbarVisualLanguage),
            ("Uses precise consistent vector geometry for picker translation and recording", UsesPreciseConsistentToolbarGlyphs),
            ("Defines compact retained editing controls", DefinesCompactRetainedEditingControls),
            ("Maps pen points from source pixels into the selected output", MapsPenPointsFromSourcePixelsIntoSelectedOutput),
            ("Applies mosaic only inside touched block boundaries", AppliesMosaicOnlyInsideTouchedBlockBoundaries),
            ("Clips retained edits to the selected region", ClipsRetainedEditsToTheSelectedRegion),
            ("Keeps retained edits when switching tools", KeepsRetainedEditsWhenSwitchingTools),
            ("Composites retained edits in drawing order", CompositesRetainedEditsInDrawingOrder),
            ("Composites a later mosaic after pen", CompositesALaterMosaicAfterPen),
            ("Samples the exact frozen source color", SamplesTheExactFrozenSourceColor),
            ("Preserves retained edits when the selection is adjusted", PreservesRetainedEditsWhenTheSelectionIsAdjusted),
            ("Keeps resize handles ahead of active editing", KeepsResizeHandlesAheadOfActiveEditing),
            ("Appends one clamped final point when a stroke ends", AppendsOneClampedFinalPointWhenAStrokeEnds),
            ("Draws a pen stroke crossing the selected boundary", DrawsAPenStrokeCrossingTheSelectedBoundary),
            ("Clips a mosaic block to a partial selection", ClipsAMosaicBlockToAPartialSelection),
            ("Picker seam samples the source bitmap and copies uppercase text", PickerSeamSamplesTheSourceBitmapAndCopiesUppercaseText),
            ("Picker seam keeps the selected pen color when clipboard copy fails", PickerSeamKeepsTheSelectedPenColorWhenClipboardCopyFails),
            ("Picker marks its clipboard text without changing continuous picking", PickerMarksClipboardTextWithProvenance),
            ("Refreshes the WPF preview after moving and resizing an edited selection", RefreshesTheWpfPreviewAfterMovingAndResizingAnEditedSelection),
            ("Picker input router uses an in-selection source coordinate and clipboard success", PickerInputRouterUsesAnInSelectionSourceCoordinateAndClipboardSuccess),
            ("Picker input router preserves the source color after clipboard failure", PickerInputRouterPreservesTheSourceColorAfterClipboardFailure),
            ("Picker input router accepts the inclusive left top edge", PickerInputRouterAcceptsTheInclusiveLeftTopEdge),
            ("Picker input router rejects the exclusive right edge", PickerInputRouterRejectsTheExclusiveRightEdge),
            ("Picker input router rejects the exclusive bottom edge", PickerInputRouterRejectsTheExclusiveBottomEdge),
            ("Picker input router accepts pixels just inside right and bottom", PickerInputRouterAcceptsPixelsJustInsideRightAndBottom),
            ("Sends only the translation request allow-list", SendsOnlyTheTranslationRequestAllowList),
            ("Uses low-variance sampling for DeepSeek translations", UsesLowVarianceSamplingForDeepSeekTranslations),
            ("Sends visual translation semantics without screenshot data", SendsVisualTranslationSemanticsWithoutScreenshotData),
            ("Uses readable identity hashtag and UI translation policies", UsesReadableVisualTranslationPolicies),
            ("Treats short punctuated screen text as body content", TreatsShortPunctuatedScreenTextAsBodyContent),
            ("Treats separated Katakana names as proper-name headings", TreatsSeparatedKatakanaNamesAsProperNameHeadings),
            ("Detects social media from an account and compact age", DetectsSocialMediaFromAccountAndCompactAge),
            ("Protects account handles before sending OCR text", ProtectsAccountHandlesBeforeSendingOcrText),
            ("Accepts an unchanged standalone protected account handle", AcceptsUnchangedStandaloneProtectedAccountHandle),
            ("Bypasses DeepSeek for protected-only visual text", BypassesDeepSeekForProtectedOnlyVisualText),
            ("Rejects added text around a standalone protected account handle", RejectsAddedTextAroundStandaloneProtectedAccountHandle),
            ("Detects a social profile from its account and profile controls", DetectsSocialProfileFromAccountAndProfileControls),
            ("Preserves standalone brand and identifier labels without a focused retry", PreservesStandaloneBrandAndIdentifierLabelsWithoutFocusedRetry),
            ("Keeps grouped social UI and trend headings out of body review", KeepsGroupedSocialUiAndTrendHeadingsOutOfBodyReview),
            ("Keeps OCR icon noise out of body review", KeepsOcrIconNoiseOutOfBodyReview),
            ("Rejects punctuation-only changes for visual translation", RejectsPunctuationOnlyVisualTranslationChanges),
            ("Rejects identity metadata that only localizes time", RejectsIdentityMetadataThatOnlyLocalizesTime),
            ("Accepts Latin account names only when adjacent dates are localized", AccountNameTranslationTests.LatinIdentityNamesAllowLocalizedDatesButNotUnchangedDates),
            ("Preserves recommended account names without exempting UI controls", AccountNameTranslationTests.RecommendedAccountNamesArePreservedWithoutExemptingControls),
            ("Localizes compact relative time deterministically", LocalizesCompactRelativeTimeDeterministically),
            ("Preserves account brand product and trademark names in the DeepSeek prompt", PreservesIdentityAndBrandNamesInDeepSeekPrompt),
            ("Allows only final v1 DeepSeek models with Flash default", AllowsOnlyFinalV1DeepSeekModelsWithFlashDefault),
            ("Rejects missing or unverified local LMT artifacts before launching", RejectsInvalidLocalLmtArtifactsBeforeLaunching),
            ("Resolves audited hashes for both developer Local LMT models", ResolvesAuditedDeveloperLocalLmtModels),
            ("Lists the packaged Local LMT model in an installed application", ListsPackagedLocalLmtModelForInstalledApplication),
            ("Uses the top Provider selector as the only Local LMT model choice", UsesTopProviderSelectorAsOnlyLocalModelChoice),
            ("Uses the last known working Local LMT production contract", RunsLocalLmtThroughPersistentPrivateSession),
            ("Uses the labeled Local LMT template on the first attempt", UsesOutputOnlyLocalLmtTemplateOnFirstAttempt),
            ("Strips Local LMT session control output from a translation frame", StripsLocalLmtSessionControlOutput),
            ("Recovers the translated tail after an output-only Local LMT prompt echo", RecoversOutputOnlyLocalLmtTranslatedTailAfterPromptEcho),
            ("Uses a bounded first Local LMT response line before rejecting continuation", UsesBoundedFirstLocalLmtResponseLine),
            ("Recovers the translated tail after a Local LMT prompt echo", RecoversLocalLmtTranslatedTailAfterPromptEcho),
            ("Classifies oversized Local LMT output without retaining its text", ClassifiesOversizedLocalLmtOutput),
            ("Retries a target-label-only Local LMT response with the stable prompt", RetriesTargetLabelOnlyLocalLmtResponse),
            ("Does not switch Local LMT input modes after an empty interactive response", FallsBackToIsolatedLocalLmtExecution),
            ("Builds a file-backed Local LMT first turn without a command-line prompt", BuildsFileBackedLocalLmtFirstTurn),
            ("Deletes the short-lived Local LMT prompt file", DeletesShortLivedLocalLmtPromptFile),
            ("Routes production Local LMT through the file-backed first turn", RoutesProductionLocalLmtThroughFileBackedFirstTurn),
            ("Translates a Local LMT visual batch with one model launch", TranslatesLocalLmtVisualBatchWithOneModelLaunch),
            ("Separates Local LMT startup and prior turns from batch answers", () => LocalLmtBatchTransportTests.StartupBannerAndPriorTurnAreNotReturnedAsTranslations().GetAwaiter().GetResult()),
            ("Rejects Local LMT startup banners in translated headings", RejectsLocalLmtStartupBannerForHeading),
            ("Retries only a missing Local LMT batch item", RetriesOnlyMissingLocalLmtBatchItem),
            ("Removes the current prompt echo from isolated Local LMT stdout", RemovesOneShotLocalLmtPromptEcho),
            ("Keeps Korean spaces when joining OCR wrapped lines", KeepsKoreanSpacesWhenJoiningOcrWrappedLines),
            ("Stops an in-flight Local LMT session when translation is cancelled", CancelsLocalLmtSession),
            ("Reuses one Local LMT model session for direct per-item translation", ReusesOneLocalLmtSessionForDirectPerItemTranslation),
            ("Keeps one Local LMT model resident across separate requests", KeepsOneLocalLmtSessionAcrossSeparateRequests),
            ("Reuses verified Local LMT artifacts and rejects changed model files", CachesVerifiedLocalLmtArtifactsForProviderLifetime),
            ("Stops the resident Local LMT model after its idle timeout", StopsResidentLocalLmtSessionAfterIdleTimeout),
            ("Translates common Local LMT UI controls without loading the model", TranslatesCommonLocalUiControlsWithoutLoadingModel),
            ("Routes an unknown UI label to Local LMT instead of preserving it", RoutesUnknownUiLabelToLocalLmt),
            ("Keeps Local LMT protected values out of model prompts and restores them in order", KeepsLocalProtectedValuesOutOfModelPrompts),
            ("Protects a Japanese adjacent digit before Local LMT inference", ProtectsJapaneseAdjacentDigitBeforeLocalLmtInference),
            ("Preserves a trailing kaomoji outside Local LMT inference", PreservesTrailingKaomojiOutsideLocalLmtInference),
            ("Splits around a missing protected digit", SplitsAroundMissingProtectedDigit),
            ("Splits around protected values without an instruction retry", SplitsProtectedValuesWithoutInstructionRetry),
            ("Repairs a protected digit collision by translating around the token", RepairsProtectedDigitCollisionByTranslatingAroundToken),
            ("Repairs a protected digit collision inside a long body chunk", RepairsProtectedDigitCollisionInsideLongBodyChunk),
            ("Retries a long Local LMT body in chunks after protected digit token failure", RetriesLongBodyInChunksAfterProtectedDigitTokenFailure),
            ("Keeps protected brands out of Local LMT inference", KeepsProtectedBrandsOutOfLocalLmtInference),
            ("Removes a Local LMT token instruction echo before restoring protected values", RemovesLocalLmtTokenInstructionEcho),
            ("Rejects a translated Local LMT prompt echo instead of drawing it over the source", RejectsTranslatedLocalLmtPromptEcho),
            ("Rejects unchanged Local LMT body text as a successful translation", RejectsUnchangedLocalLmtBodyText),
            ("Rejects punctuation-only Local LMT Japanese body changes", RejectsPunctuationOnlyLocalLmtJapaneseBodyChanges),
            ("Rejects Japanese Local LMT body text that remains in the source script", RejectsJapaneseLocalLmtSourceScriptBodyText),
            ("Carries a structured local translation diagnostic without private text", CarriesStructuredLocalTranslationDiagnosticWithoutPrivateText),
            ("Maps Local LMT process failures without exception text", MapsLocalLmtProcessFailuresWithoutExceptionText),
            ("Reports an initial Local LMT response-boundary failure precisely", ReportsInitialLocalLmtResponseBoundaryFailurePrecisely),
            ("Reports a retry Local LMT response-boundary failure precisely", ReportsRetryLocalLmtResponseBoundaryFailurePrecisely),
            ("Retries a long Local LMT body in original line order", RetriesLongLocalLmtBodyInOriginalLineOrder),
            ("Retries dense Korean body text in bounded chunks", RetriesDenseKoreanBodyTextInBoundedChunks),
            ("Keeps every Japanese protected-token body chunk within the retry target", KeepsJapaneseProtectedTokenBodyChunksBounded),
            ("Retries a short unchanged Local LMT body with the official template", RetriesShortUnchangedLocalLmtBodyWithOfficialTemplate),
            ("Retries a short failed Local LMT body at most twice", RetriesShortFailedLocalLmtBodyAtMostTwice),
            ("Keeps Local LMT protected tokens atomic across chunks", KeepsLocalLmtProtectedTokensAtomicAcrossChunks),
            ("Carries a protected-only Local LMT retry chunk without inference", CarriesProtectedOnlyLocalLmtRetryChunkWithoutInference),
            ("Carries an unmasked protected Local LMT brand without inference", CarriesUnmaskedProtectedLocalLmtBrandWithoutInference),
            ("Retains the whole Local LMT body when one retry chunk fails", RetainsWholeLocalLmtBodyWhenOneRetryChunkFails),
            ("Reports a missing protected token in Local LMT chunk two", ReportsMissingProtectedTokenInLocalLmtChunkTwo),
            ("Does not retry a successful long Local LMT body", DoesNotRetrySuccessfulLongLocalLmtBody),
            ("Uses the official Local LMT template for a social profile passage", UsesOfficialLocalLmtTemplateForSocialProfilePrompt),
            ("Uses a fifteen minute default Local LMT idle timeout", UsesFifteenMinuteDefaultLocalLmtIdleTimeout),
            ("Localizes compact and worded relative times before Local LMT inference", LocalizesRelativeTimesBeforeLocalLmtInference),
            ("Preserves Local LMT magnitude tokens and identifier-like display names", PreservesLocalMagnitudeTokensAndIdentifierDisplayNames),
            ("Accepts reordered DeepSeek translation block IDs", AcceptsReorderedDeepSeekTranslationBlockIds),
            ("Rejects duplicate DeepSeek translation block IDs", RejectsDuplicateDeepSeekTranslationBlockIds),
            ("Rejects incomplete DeepSeek block responses", RejectsInvalidDeepSeekTranslationBlockResponses),
            ("Accepts code path and URL DeepSeek translations", AcceptsCodePathAndUrlDeepSeekTranslations),
            ("Requires nonempty translated text for every OCR block", RequiresNonemptyDeepSeekTranslationsInsteadOfCodeEchoes),
            ("Uses an object envelope compatible with DeepSeek JSON mode", UsesObjectEnvelopeCompatibleWithDeepSeekJsonMode),
            ("Uses a compact ID keyed DeepSeek response", UsesCompactIdKeyedDeepSeekResponse),
            ("Leaves the DeepSeek output token limit to the service", LeavesDeepSeekOutputTokenLimitToService),
            ("Does not retry an invalid single DeepSeek item", DoesNotRetryInvalidDeepSeekBatch),
            ("Rejects unchanged natural-language source as a successful translation", RejectsUnchangedSourceAsSuccessfulTranslation),
            ("Accepts unchanged opaque path blocks from DeepSeek", AcceptsUnchangedOpaquePathBlocks),
            ("Accepts unchanged natural text already in every supported target language", AcceptsUnchangedSupportedTargetLanguageText),
            ("Rejects unchanged source scripts when translating to Chinese", RejectsUnchangedMultilingualSourcesForChinese),
            ("Accepts an official Latin name for a separated Katakana proper name", AcceptsOfficialLatinNameForSeparatedKatakanaProperName),
            ("Rejects an unverified Latin rendering for separated Katakana text", RejectsUnverifiedLatinRenderingForSeparatedKatakanaText),
            ("Reports an unchanged separated Katakana proper name precisely", ReportsUnchangedSeparatedKatakanaProperNamePrecisely),
            ("Reports missing target-language content precisely", ReportsMissingTargetLanguageContentPrecisely),
            ("Keeps valid DeepSeek siblings when one returned block is invalid", KeepsValidDeepSeekSiblingsWhenOneBlockIsInvalid),
            ("Classifies changed unchanged and failed overlay blocks", ClassifiesTranslationOverlayBlocks),
            ("Builds overlay replacements only for changed translation blocks", BuildsOverlayOnlyForChangedTranslationBlocks),
            ("Treats mixed-language OCR as partially untranslated", TreatsMixedLanguageOcrAsPartiallyUntranslated),
            ("Accepts a fenced JSON transport envelope from DeepSeek", AcceptsFencedDeepSeekJsonTransport),
            ("Completes streaming DeepSeek responses at finish reason", CompletesStreamingDeepSeekResponseAtFinishReason),
            ("Requests and reads DeepSeek streaming usage after finish reason", RequestsAndReadsDeepSeekStreamingUsage),
            ("Calculates current and scheduled DeepSeek prices", CalculatesCurrentAndScheduledDeepSeekPrices),
            ("Distinguishes DeepSeek peak and off-peak price windows", DistinguishesDeepSeekPeakAndOffPeakPriceWindows),
            ("Accumulates DeepSeek cost across recovery requests", AccumulatesDeepSeekCostAcrossRecoveryRequests),
            ("Formats DeepSeek cost as a Chinese estimate", FormatsDeepSeekCostAsChineseEstimate),
            ("Persists only aggregate DeepSeek cost statistics", PersistsOnlyAggregateDeepSeekCostStatistics),
            ("Resets DeepSeek cost statistics atomically", ResetsDeepSeekCostStatisticsAtomically),
            ("Recovers a truncated DeepSeek batch within a bounded split budget", RecoversTruncatedDeepSeekBatchWithoutLosingOrder),
            ("Recovers a malformed DeepSeek batch without losing order", RecoversDeepSeekBatchWithOneInvalidItem),
            ("Retries only semantically invalid siblings from an otherwise valid batch", RetriesOnlySemanticallyInvalidSiblings),
            ("Keeps an explicitly selected Flash visual batch on Flash", KeepsExplicitFlashVisualBatchOnFlash),
            ("Retries only failed visual translation siblings", RetriesOnlyFailedVisualTranslationSiblings),
            ("Bounds visual cloud translation to twelve seconds", BoundsVisualCloudTranslationToTwelveSeconds),
            ("Does not review body text already in the target language", DoesNotReviewBodyAlreadyInTargetLanguage),
            ("Does not launch Pro review after an initial transport failure", DoesNotReviewAfterInitialTransportFailure),
            ("Reports a truncated single DeepSeek item explicitly", ReportsTruncatedSingleDeepSeekItemExplicitly),
            ("Reports actionable DeepSeek HTTP failures", ReportsActionableDeepSeekHttpFailures),
            ("Keeps actionable DeepSeek connection failure categories", KeepsActionableDeepSeekConnectionFailureCategories),
            ("Classifies a missing fixed DeepSeek credential", ClassifiesMissingFixedDeepSeekCredential),
            ("Classifies a DeepSeek network failure", ClassifiesDeepSeekNetworkFailure),
            ("Classifies malformed DeepSeek streaming data without exposing it", ClassifiesMalformedDeepSeekStreamingDataWithoutExposingIt),
            ("Does not split a malformed DeepSeek stream into retry batches", DoesNotSplitMalformedDeepSeekStreamIntoRetryBatches),
            ("Does not split an empty DeepSeek stream into retry batches", DoesNotSplitEmptyDeepSeekStreamIntoRetryBatches),
            ("Preserves the structural DeepSeek failure family when recovery budget is exhausted", PreservesStructuralDeepSeekFailureFamilyWhenRecoveryBudgetIsExhausted),
            ("Classifies unexpected DeepSeek provider failures without exposing details", ClassifiesUnexpectedDeepSeekProviderFailureWithoutExposingDetails),
            ("Classifies candidate DeepSeek authentication failure", ClassifiesCandidateDeepSeekAuthenticationFailure),
            ("Displays sanitized candidate connection categories in Settings", DisplaysSanitizedCandidateConnectionCategoriesInSettings),
            ("Keeps Local LMT IDs in the program instead of the model prompt", KeepsLocalLmtIdsOutOfModelPrompts),
            ("Decodes Local LMT stdout and stderr as UTF-8", DecodesLocalLmtOutputAsUtf8),
            ("Bypasses Local LMT when source and target languages match", BypassesEquivalentLocalLmtLanguages),
            ("Translates only required items in a mixed Local LMT batch", TranslatesOnlyRequiredMixedBatchItems),
            ("Validates numbers model tokens and hotkeys without placeholder inflation", ValidatesTranslationInvariantsWithoutPlaceholderInflation),
            ("Classifies the first changed translation invariant", ClassifiesFirstChangedTranslationInvariant),
            ("Redacts the credential from success error and connection diagnostics", RedactsCredentialFromAllDiagnostics),
            ("Preserves placeholders tags and numbers through translation", PreservesPlaceholdersTagsAndNumbers),
            ("Rejects standalone model prompt instructions", RejectsStandalonePromptInstructions),
            ("Checks independent OCR translation and layout oracle failures", QualityOracleDetectsIndependentFailures),
            ("Groups OCR words into deterministic ordered pixel blocks", GroupsOcrWordsDeterministically),
            ("Keeps raw OCR provenance through grouping", OcrEvidenceTests.PreservesRawScoreAndModelThroughGrouping),
            ("Joins adjacent CJK OCR words without synthetic spaces", JoinsAdjacentCjkOcrWordsWithoutSyntheticSpaces),
            ("Splits distant same-row OCR words into separate columns", SplitsDistantSameRowOcrWordsIntoColumns),
            ("Groups multiline body text into semantic visual regions", GroupsMultilineBodyTextIntoSemanticRegions),
            ("Groups wrapped short Japanese profile text into one body region", GroupsWrappedShortJapaneseProfileText),
            ("Separates a compact translation link from a larger wrapped body", SeparatesCompactTranslationLinkFromLargerWrappedBody),
            ("Keeps a same-height Show translation link out of the body region", KeepsSameHeightShowTranslationLinkOutOfBodyRegion),
            ("Does not color-split a recognized Show translation control", DoesNotColorSplitRecognizedShowTranslationControl),
            ("Separates a same-color Show translation prefix from body text", SeparatesSameColorShowTranslationPrefixFromBody),
            ("Splits mixed-color OCR lines into stable visual spans", SplitsMixedColorOcrLinesIntoStableVisualSpans),
            ("Splits adjacent UI words that use different backing surfaces", SplitsAdjacentUiWordsAcrossBackingSurfaces),
            ("Keeps a leading decorative glyph outside translated text", KeepsLeadingDecorativeGlyphOutsideTranslation),
            ("Splits OCR-merged topic chips while dropping icon noise", SplitsMergedTopicChipsWhileDroppingIconNoise),
            ("Keeps a legitimate leading one-character label", KeepsLegitimateLeadingOneCharacterLabel),
            ("Keeps a trailing colored decoration outside translated text", KeepsTrailingColoredDecorationOutsideTranslation),
            ("Trims a touching decoration with only incidental contact", TrimsTouchingDecorationWithIncidentalContact),
            ("Does not trim an attached multicolor word without a visual gap", DoesNotTrimAttachedMulticolorWord),
            ("Translates body text while preserving an unmasked inline decoration", TranslatesBodyTextWithUnmaskedInlineDecoration),
            ("Keeps grouped profile metadata out of body translation", KeepsGroupedProfileMetadataOutOfBodyTranslation),
            ("Keeps controls metadata and columns in separate visual regions", KeepsControlsMetadataAndColumnsSeparate),
            ("Does not merge a page heading with its smaller subtitle", DoesNotMergeHeadingWithSmallerSubtitle),
            ("Separates a short card heading from a distant body line", SeparatesShortCardHeadingFromDistantBody),
            ("Does not merge a wide paragraph with a neighboring card column", DoesNotMergeWideParagraphAcrossCardColumns),
            ("Keeps a paragraph intact beside an unrelated sidebar row", KeepsParagraphIntactBesideSidebarRow),
            ("Separates an inline technical identity from a status badge", SeparatesInlineTechnicalIdentityFromBadge),
            ("Builds one atomic in-place item for one translated visual region", BuildsAtomicInPlaceItemForVisualRegion),
            ("Renders a deterministic Local LMT Show translation control without OCR word boxes", RendersLocalLmtShowTranslationControlWithoutOcrWordBoxes),
            ("Renders a deterministic Local LMT control with low-confidence OCR word boxes", RendersLocalLmtControlWithLowConfidenceOcrWordBoxes),
            ("Uses conservative typography for ordinary horizontal UI text", UsesConservativeTypographyForUiText),
            ("Measures text height without a neighboring navigation icon", MeasuresTextHeightWithoutNavigationIcon),
            ("Preserves a confident muted source foreground", PreservesConfidentMutedSourceForeground),
            ("Ignores a small colored badge when estimating text foreground", IgnoresSmallColoredBadgeWhenEstimatingTextForeground),
            ("Normalizes same-row visual peers to one font size", NormalizesSameRowVisualPeersToOneFontSize),
            ("Keeps adjacent inline color fragments left flowing", KeepsAdjacentInlineColorFragmentsLeftFlowing),
            ("Uses semantic visual regions for the production translation contract", UsesSemanticRegionsInProductionTranslation),
            ("Plans reversible small-text OCR enlargement and coordinate restoration", PlansReversibleSmallTextOcrEnlargement),
            ("Uses stronger enlargement for tiny OCR selections", UsesStrongerEnlargementForTinyOcrSelections),
            ("Normalizes dark OCR screenshots without changing geometry", NormalizesDarkOcrScreenshotsWithoutChangingGeometry),
            ("Builds deterministic in-place mappings without mutating the source", BuildsDeterministicInPlaceMappings),
            ("Chooses flat and complex background modes from source pixels", ChoosesBackgroundModesFromSourcePixels),
            ("Reconstructs complex translation backing instead of painting a dark rectangle", ReconstructsComplexTranslationBacking),
            ("Cancels translation and keeps OCR bitmap lifetime independent", CancelsTranslationAndKeepsOcrBitmapAlive),
            ("Probes Windows OCR lazily with a local-only result", ProbesWindowsOcrLazily),
            ("Resolves the locked RapidOCR PP-OCRv6 model set", ResolvesLockedRapidOcrModelSet),
            ("Resolves the Korean PP-OCRv5 supplemental model set", ResolvesKoreanRapidOcrModelSet),
            ("Merges Korean OCR without duplicating Latin text", MergesKoreanOcrWithoutDuplicatingLatinText),
            ("Packages the Korean OCR model and dictionary", PackagesKoreanOcrAssets),
            ("Caps RapidOCR CPU threads for interactive latency", CapsRapidOcrThreadsForInteractiveLatency),
            ("Defines PP-OCRv6 ONNX as the primary path with Windows OCR fallback", DefinesFastOcrRuntime),
            ("Uses the in-process PP-OCRv6 path before the Windows OCR fallback", UsesPpOcrPathBeforeWindowsOcrFallback),
            ("Warms the PP-OCR primary runtime before the fallback", WarmsPpOcrPrimaryRuntimeBeforeFallback),
            ("Redacts exception messages at the test harness boundary", RedactsHarnessFailureMessages),
            ("Disables production redirects and rejects 307 and 308", RejectsRedirectsWithoutSecondOriginReplay),
            ("Selects protocol non-thinking mode in the exact payload allow-list", SelectsProtocolNonThinkingMode),
            ("Runs Settings connection tests through provider without reading secrets", RunsSettingsConnectionThroughProvider),
            ("Verifies a candidate DeepSeek credential without vault lookup", VerifiesCandidateDeepSeekCredentialWithoutVaultLookup),
            ("Clears both credential entry controls only after save", ClearsBothCredentialEntryControlsOnlyAfterSave),
            ("Uses collision-free placeholder tokens and rejects multiset corruption", ProtectsPlaceholderTokenIntegrity),
            ("Times out with sanitized diagnostics", TimesOutWithSanitizedDiagnostics),
            ("Owns UI operations by cancellation version and close", OwnsUiOperations),
            ("Keeps shutdown cleanup running after recording stop failure", KeepsShutdownCleanupRunningAfterRecordingStopFailure),
            ("Keeps overlay translation out of the standalone window", KeepsOverlayTranslationOutOfStandaloneWindow),
            ("Keeps small translated selections at their original coordinates", KeepsSmallTranslatedSelectionsAtOriginalCoordinates),
            ("Starts translated text from the measured source pixel size", UsesReadableMinimumTranslationFontSize),
            ("Uses the CJK visible glyph ratio for Chinese translations", UsesCjkVisibleGlyphRatio),
            ("Shrinks translated text only when the source box requires it", ShrinksTranslatedTextToFitSourceBounds),
            ("Fits compact translated metadata without relaxing body text", FitsCompactTranslatedMetadata),
            ("Keeps the original when translated text cannot fit safely", KeepsOriginalWhenTranslationCannotFit),
            ("Builds in-place items only for safe changed blocks", BuildsOnlySafeChangedInPlaceItems),
            ("Rejects overlapping in-place replacements", RejectsOverlappingInPlaceReplacements),
            ("Renders measured source style without fixed font clamps", RendersMeasuredSourceStyleWithoutFixedClamps),
            ("Renders masked repair without an opaque region background", RendersMaskedRepairWithoutOpaqueRegionBackground),
            ("Pads small OCR captures without moving the translation origin", PadsSmallOcrCapturesWithoutMovingTranslationOrigin),
            ("Quantizes OCR capture bounds before bitmap cropping and screen mapping", QuantizesOcrCaptureBoundsBeforeMapping),
            ("Keeps padded OCR context outside the user selection out of translation", KeepsPaddedOcrContextOutOfTranslation),
            ("Uses the padded capture bounds for OCR and translation mapping", UsesPaddedCaptureBoundsForOcrAndTranslationMapping),
            ("Keeps the translation toggle interactive and reversible", KeepsTranslationToggleInteractiveAndReversible),
            ("Keeps translation items stable across repeated display toggles", KeepsTranslationItemsStableAcrossToggles),
            ("Translated selection saves the currently displayed mode", TranslatedSelectionSavesCurrentlyDisplayedMode),
            ("Translated selection renderer exports only selected translated pixels", TranslatedSelectionRendererExportsOnlySelectedTranslatedPixels),
            ("Confirmed translated selection follows the displayed mode", ConfirmedTranslatedSelectionFollowsDisplayedMode),
            ("Does not open an independent focus window for translated selections", DoesNotOpenIndependentFocusWindowForTranslatedSelections),
            ("Keeps the PP-OCR sidecar warm between requests", KeepsPpOcrSidecarWarmBetweenRequests),
            ("Keeps extraction controls interactive over the overlay", KeepsExtractionControlsInteractiveOverOverlay),
            ("Copies extraction text through the clipboard seam", CopiesExtractionTextThroughClipboardSeam),
            ("Places extraction results beside the selection", PlacesExtractionResultsBesideSelection),
            ("Leaves enough height for extraction panel chrome", LeavesEnoughHeightForExtractionPanelChrome),
            ("Uses a readable extraction panel text size", UsesReadableExtractionPanelTextSize),
            ("Expands extraction panel into available side space", ExpandsExtractionPanelIntoAvailableSideSpace),
            ("Lets extraction text use available height before scrolling", LetsExtractionTextUseAvailableHeightBeforeScrolling),
            ("Keeps extraction panel width readable on wide side spaces", KeepsExtractionPanelWidthReadableOnWideSideSpaces),
            ("Expands extraction panel for long OCR lines before wrapping", ExpandsExtractionPanelForLongOcrLinesBeforeWrapping),
            ("Uses the larger side when the selection is near an edge", UsesTheLargerSideWhenSelectionIsNearAnEdge),
            ("Caps long extraction content without stretching the overlay", CapsLongExtractionContentWithoutStretchingTheOverlay),
            ("Measures extraction content instead of counting raw lines", MeasuresExtractionContentInsteadOfCountingRawLines),
            ("Measures wrapped extraction text independently of the scroll viewer", MeasuresWrappedExtractionText),
            ("Grows the extraction panel when final OCR content needs more room", GrowsExtractionPanelForFinalContent),
            ("Measures unwrapped extraction width before calculating height", MeasuresUnwrappedExtractionWidthBeforeCalculatingHeight),
            ("Reflows the toolbar after extraction content changes", ReflowsToolbarAfterExtractionContentChanges),
            ("Places the toolbar around a visible OCR panel", PlacesToolbarAroundVisibleOcrPanel),
            ("Places floating translation feedback outside the toolbar", PlacesFloatingTranslationFeedbackOutsideToolbar),
            ("Keeps floating translation feedback clear of expanded tool properties", KeepsFloatingTranslationFeedbackClearOfExpandedToolProperties),
            ("Keeps completed translation feedback visible outside the translated image", KeepsCompletedTranslationFeedbackVisibleOutsideTheTranslatedImage),
            ("Uses the monitor working area for taskbar-free current-screen capture", UsesWorkingAreaForTaskbarFreeCurrentScreenCapture),
            ("Free capture includes the taskbar without changing taskbar-free current-screen capture", FreeCaptureIncludesTaskbar),
            ("Keeps global hotkeys active outside a focused settings hotkey field", KeepsGlobalHotkeysActiveOutsideFocusedSettingsHotkeyField),
            ("Defines a privacy-safe clipboard owner monitor", DefinesPrivacySafeClipboardOwnerMonitor),
            ("Separates DeepSeek completion status from privacy wording", SeparatesDeepSeekCompletionStatusFromPrivacyWording),
            ("Reports full translation failure while retaining original blocks", ReportsFullTranslationFailureWithOriginalRetention),
            ("Reports partial local translation without claiming completion", ReportsPartialLocalTranslationWithoutClaimingCompletion),
            ("Reports a local retry diagnostic without private text", ReportsLocalRetryDiagnosticWithoutPrivateText),
            ("Styles the tray menu with a dark renderer", StylesTrayMenuWithDarkRenderer),
            ("Styles every translation chooser and context menu for the dark UI", StylesEveryTranslationChooserAndContextMenuForDarkUi),
            ("Keeps every compact overlay button readable while hovered", KeepsCompactOverlayButtonsReadableWhileHovered),
            ("Shows immediate progress feedback in the standalone translation window", ShowsImmediateStandaloneTranslationProgressFeedback),
            ("Uses a rainbow success animation for saved settings", UsesRainbowSettingsSuccessAnimation),
            ("Sizes settings status to its text and removes dotted button focus", SizesSettingsStatusAndRemovesDottedFocus),
            ("Places startup setting in the launch and schedule card", PlacesStartupSettingInLaunchAndScheduleCard),
            ("Shows locally accumulated DeepSeek estimates in Settings", ShowsAccumulatedDeepSeekEstimateInSettings),
            ("Documents the current recording and edit shortcuts", DocumentsTheCurrentRecordingAndEditShortcuts),
            ("Offers only concrete translation routes and selects the configured default", OffersOnlyConcreteTranslationRoutesAndSelectsConfiguredDefault),
            ("Localizes provider diagnostics before displaying them", LocalizesProviderDiagnosticsBeforeDisplay),
            ("Keeps the clarified tray menu at its default scale", KeepsClarifiedTrayMenuAtDefaultScale),
            ("Freezes selection snapshots and accepts only latest render", FreezesSelectionAndAcceptsOnlyLatest),
            ("Disposes translation captures when no subscriber owns them", DisposesUnownedTranslationCaptures),
            ("Preserves the original selection beside a padded capture", PreservesOriginalSelectionBesidePaddedCapture),
            ("Samples only the nearby ring and contrasts the actual backing", SamplesNearbyRingAndContrastsBacking),
            ("Uses the dominant nearby background instead of averaging UI outliers", UsesDominantNearbyBackground),
            ("Keeps badges and gaps outside the text replacement mask", KeepsBadgesOutsideTextReplacementMask),
            ("Repairs gradients only inside the text replacement mask", RepairsGradientsOnlyInsideTextMask),
            ("Rejects unsafe photographic backing for text replacement", RejectsUnsafePhotographicBacking),
            ("Carries source glyph color into in-place translation", CarriesSourceGlyphColorIntoInPlaceTranslation),
            ("Measures source glyph color and pixel height inside OCR bounds", MeasuresSourceGlyphColorAndPixelHeight),
            ("Estimates source weight from stable ink coverage", EstimatesSourceWeightFromInkCoverage),
            ("Infers paragraph alignment from neighboring OCR lines", InfersParagraphAlignmentFromNeighboringLines),
            ("Estimates a bounded source text rotation from ink geometry", EstimatesSourceTextRotation),
            ("Treats unstable image texture as low-confidence text style", TreatsUnstableImageTextureAsLowConfidence),
            ("Detects a consistent source italic shear", DetectsConsistentSourceItalicShear),
            ("Chooses explicit OCR language candidates deterministically", ChoosesExplicitOcrLanguageCandidates),
            ("Orders installed OCR languages for local auto detection", OrdersInstalledOcrLanguagesForAutoDetection),
            ("Rejects an empty OCR result before launching translation", RejectsEmptyOcrBeforeTranslation),
            ("Encodes OCR off caller thread without temporary image artifacts", EncodesOcrInMemoryOffCallerThread),
            ("Keeps OCR and provider factories idle until requested", KeepsTranslationRuntimeIdle),
            ("Rejects late standalone window updates after close", RejectsLateWindowUpdatesAfterClose),
            ("Stitches exact vertical overlap without changing source pixels", StitchesExactVerticalOverlapWithoutChangingSourcePixels),
            ("Stitches a unique large overlap despite sparse dynamic pixels", StitchesUniqueLargeOverlapDespiteSparseDynamicPixels),
            ("Stitches through changing edges and sparse dynamic rows", StitchesThroughChangingEdgesAndSparseDynamicRows),
            ("Rejects an otherwise repeated large frame with sparse dynamic pixels", RejectsOtherwiseRepeatedLargeFrameWithSparseDynamicPixels),
            ("Rejects low confidence and repeated frames", RejectsLowConfidenceAndRepeatedFrames),
            ("Rejects partial width and pixel format mismatches", RejectsPartialWidthAndPixelFormatMismatches),
            ("Disposes bounded lossless segments after manual partial capture", DisposesBoundedLosslessSegmentsAfterManualPartialCapture),
            ("Cleans orphan long-capture sessions while preserving active sessions", CleansOrphanLongCaptureSessionsWhilePreservingActiveSessions),
            ("Composes three dynamic-edge segments from adjacent frame overlaps", ComposesThreeDynamicEdgeSegmentsFromAdjacentFrameOverlaps),
            ("Bounds automatic wheel input and stops on cancellation", BoundsAutomaticWheelInputAndStopsOnCancellation),
            ("Manual capture waits through unchanged polls until explicitly cancelled", ManualCaptureWaitsThroughUnchangedPollsUntilExplicitlyCancelled),
            ("Manual capture waits through low-confidence desktop changes until explicitly cancelled", ManualCaptureWaitsThroughLowConfidenceUntilExplicitlyCancelled),
            ("Automatic capture waits for real first movement until explicitly cancelled", AutomaticCaptureWaitsForFirstMovementUntilCancelled),
            ("Automatic capture tolerates delayed first scroll before stopping", AutomaticCaptureToleratesDelayedFirstScrollBeforeStopping),
            ("Automatic capture survives a transient repeated-frame run", AutomaticCaptureSurvivesTransientRepeatedFrameRun),
            ("Automatic capture survives a transient low-confidence frame", AutomaticCaptureSurvivesTransientLowConfidenceFrame),
            ("Automatic scrolling never repositions the user cursor", AutomaticScrollingNeverRepositionsTheUserCursor),
            ("Long capture controls default to manual mode", LongCaptureControlsDefaultToManualMode),
            ("Manual long capture can switch automatic scrolling on and pause it", ManualLongCaptureCanToggleAutomaticScrolling),
            ("Long capture result starts fit-width and supports Ctrl wheel zoom", LongCaptureResultSupportsFitWidthAndCtrlWheelZoom),
            ("Maps mouse grab movement to bounded long-capture offsets", MapsMouseGrabMovementToBoundedLongCaptureOffsets),
            ("Closes long-capture chrome before showing the modal result", ClosesLongCaptureChromeBeforeShowingModalResult),
            ("Long-capture controls show accepted frame count and status", LongCaptureControlsShowOnlyAcceptedFrameCount),
            ("Rejects ambiguous repeated texture overlaps", RejectsAmbiguousRepeatedTextureOverlaps),
            ("Rejects row-run ambiguity in a large repeated texture", RejectsRowRunAmbiguityInLargeRepeatedTexture),
            ("Automatic capture settles before every bounded wheel capture", AutomaticCaptureSettlesBeforeEveryBoundedWheelCapture),
            ("Input failure preserves a composable partial capture", InputFailurePreservesAComposablePartialCapture),
            ("Capture failure preserves accepted partial recovery", CaptureFailurePreservesAcceptedPartialRecovery),
            ("Capture control stop remains reachable without desktop input", CaptureControlStopRemainsReachableWithoutDesktopInput),
            ("Repositions long capture choice with the selection toolbar", RepositionsLongCaptureChoiceWithTheSelectionToolbar),
            ("Stitches a tall small-scroll overlap beyond 512 rows", StitchesATallSmallScrollOverlapBeyond512Rows),
            ("Publishes the cumulative composite while long capture is running", PublishesCumulativeLongCapturePreview),
            ("Associates the final save with the same multi-frame long capture run", AssociatesFinalSaveWithSameMultiFrameLongCaptureRun),
            ("Caps and places the long capture preview beside the selection", SizesAndPlacesLongCapturePreview),
            ("Places capture controls outside edge and negative-coordinate regions", PlacesCaptureControlsOutsideEdgeAndNegativeCoordinateRegions),
            ("Rejects pre-cancelled large near-match analysis", RejectsPreCancelledLargeNearMatchAnalysis),
            ("Tears down an active displayed control without disposed cancellation fault", TearsDownAnActiveDisplayedControlWithoutDisposedCancellationFault),
            ("Maps negative physical regions into the monitor crop", MapsNegativePhysicalRegionsIntoTheMonitorCrop),
            ("Chooses the monitor with the largest selected intersection", ChoosesMonitorWithLargestSelectedIntersection),
            ("Normalizes encoder dimensions by cropping only right and bottom", NormalizesEncoderDimensionsByCroppingOnlyRightAndBottom),
            ("Uses one QPC epoch for audio and video timestamps", UsesOneQpcEpochForAudioAndVideoTimestamps),
            ("Rejects non-monotonic timestamps per recording stream", RejectsNonMonotonicTimestampsPerRecordingStream),
            ("Exposes only default-render loopback audio creation", ExposesOnlyDefaultRenderLoopbackAudioCreation),
            ("Formats a privacy-safe recording startup failure", FormatsPrivacySafeRecordingStartupFailure),
            ("Wraps a fixed recording startup stage", WrapsFixedRecordingStartupStage),
            ("Preserves the first recording startup failure during cleanup", PreservesFirstRecordingStartupFailureDuringCleanup),
            ("Routes recording startup UI through the safe formatter", RoutesRecordingStartupUiThroughSafeFormatter),
            ("Stops a recording session exactly once under concurrent calls", StopsARecordingSessionExactlyOnceUnderConcurrentCalls),
            ("Creates collision-free partial and final recording paths", CreatesCollisionFreePartialAndFinalRecordingPaths),
            ("Finalizes by an atomic no-overwrite move", FinalizesByAnAtomicNoOverwriteMove),
            ("Preserves the exact partial recording after finalize failure", PreservesTheExactPartialRecordingAfterFinalizeFailure),
            ("Defines a non-interactive capture-excluded recording border", DefinesANonInteractiveCaptureExcludedRecordingBorder),
            ("Keeps a full-screen recording border inside visible display bounds", KeepsAFullScreenRecordingBorderInsideVisibleDisplayBounds),
            ("Keeps recording alive when the optional border cannot be capture-excluded", KeepsRecordingAliveWhenOptionalBorderCannotBeCaptureExcluded),
            ("Writes a privacy-safe recording border probe", WritesPrivacySafeRecordingBorderProbe),
            ("Pins only the approved recording dependencies", PinsOnlyTheApprovedRecordingDependencies),
            ("Keeps recording backends idle until the runtime starts", KeepsRecordingBackendsIdleUntilRuntimeStarts),
            ("Creates WASAPI capture only through the default loopback constructor", CreatesWasapiCaptureOnlyThroughTheDefaultLoopbackConstructor),
            ("Encodes a generated motion and audio fixture through Media Foundation", EncodesGeneratedMotionAndAudioFixtureThroughMediaFoundation),
            ("Scales recording bitrate with frame area and bounds the result", ScalesRecordingBitrateWithFrameAreaAndBoundsTheResult),
            ("Targets 60fps across capture pacing and the encoder", CaptureRecordingRepairTests.RecordingPipelineTargetsSixtyFramesPerSecond),
            ("Dismisses recording feedback and shows the preview before the finalize finishes", CaptureRecordingRepairTests.StopShowsPreviewBeforeFinalizeFinishes),
            ("Opens the preview window while the recording is still being finalized", CaptureRecordingRepairTests.PendingPreviewWindowOpensBeforeTheFileExists),
            ("Preview window offers a draggable seek bar", CaptureRecordingRepairTests.RecordingPreviewProvidesScrubbing),
            ("Dragging the preview seek bar moves and resumes playback", CaptureRecordingRepairTests.RecordingPreviewDragSeeksAndResumes),
            ("Encoder uses hardware transforms and disables sink throttling", CaptureRecordingRepairTests.RecordingEncoderUsesHardwareTransformsWithoutSinkThrottling),
            ("Recording includes the mouse cursor", CaptureRecordingRepairTests.RecordingCapturesTheCursor),
            ("Routes Local LMT verification through the actual local provider", RoutesLocalLmtVerificationThroughActualProvider),
            ("Maps Enter to confirm without changing double Escape", MapsEnterToConfirm),
            ("Starts locked-size movement from the selection border", StartsLockedSizeMovementFromBorder),
            ("Keeps mosaic coverage circular", KeepsMosaicCoverageCircular),
            ("Composes an active mosaic stroke before mouse release", ComposesActiveMosaicBeforeMouseRelease),
            ("Formats extracted OCR text one visual line per output line", FormatsExtractedOcrTextByVisualLine),
            ("Resizes a selection around its center and clamps to bounds", ResizesSelectionAroundCenter),
            ("Undoes retained pen and mosaic operations in drawing order", UndoesRetainedOperationsInDrawingOrder),
            ("Redoes retained edits and clears the redo branch", RedoesRetainedEditsAndClearsTheRedoBranch),
            ("Formats live picker color as HEX and RGB", FormatsLivePickerColor),
            ("Captures a hotkey directly from key and modifier state", CapturesHotkeyFromKeyState),
            ("Resolves IME and system key events to their physical hotkey", ResolvesImeAndSystemKeysForHotkeyCapture),
            ("Rejects every non-registerable WPF key from persisted hotkeys", RejectsEveryNonRegisterableWpfKeyFromHotkeys),
            ("Formats printable hotkeys with keyboard-layout symbols", FormatsPrintableHotkeysWithLayoutSymbols),
            ("Hides WPF enum names in current-layout hotkey labels", HidesWpfEnumNamesInCurrentLayoutHotkeyLabels),
            ("Keeps numpad hotkeys distinct from printable symbols", KeepsNumpadHotkeysDistinctFromPrintableSymbols),
            ("Keeps hotkey display text out of persisted settings", KeepsHotkeyDisplayTextOutOfPersistedSettings),
            ("Recovers either held Windows key when WPF omits the modifier", RecoversHeldWindowsKeysWhenWpfOmitsModifier),
            ("Does not retain Windows after both Windows keys are released", DoesNotRetainReleasedWindowsKeys),
            ("Allows capture controls outside the crop without display affinity", AllowsOutsideLongCaptureControlWithoutAffinity),
            ("Exposes a visible recording stop control", ExposesVisibleRecordingStopControl),
            ("Copies the finalized recording as a clipboard file drop", CopiesFinalRecordingAsClipboardFileDrop),
            ("Normalizes Local LMT language names for the model template", NormalizesLocalLmtLanguageNames),
            ("Detects the dominant script for mixed Local LMT text", DetectsDominantScriptForMixedLocalText),
        };

        var filterIndex = Array.FindIndex(args, static argument => string.Equals(argument, "--filter", StringComparison.OrdinalIgnoreCase));
        if (filterIndex >= 0 && filterIndex + 1 < args.Length)
        {
            var filter = args[filterIndex + 1];
            var terms = filter.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            tests = tests.Where(test => terms.Any(term => test.Name.Contains(term, StringComparison.OrdinalIgnoreCase))).ToArray();
            if (tests.Length == 0)
            {
                Console.Error.WriteLine("0 tests collected");
                return 2;
            }
        }

        var failures = 0;
        foreach (var (name, run) in tests)
        {
            try
            {
                run();
                Console.WriteLine($"PASS {name}");
            }
            catch (Exception exception)
            {
                failures++;
                Console.Error.WriteLine(FormatFailure(name, exception));
            }
        }

        Console.WriteLine($"{tests.Length - failures}/{tests.Length} tests passed");
        return failures == 0 ? 0 : 1;
    }

    private static void ReportsMissingTestLabManifestWithoutFabricatingCases()
    {
        using var workspace = TemporaryWorkspace.Create();
        var manifest = TestLabManifest.Load(workspace.Root);

        Equal(0, manifest.Cases.Count);
        Equal(true, manifest.Errors.SequenceEqual(["manifest-missing"]));
    }

    private static void RejectsUnsafeTestLabManifestCase()
    {
        using var workspace = TemporaryWorkspace.Create();
        File.WriteAllText(Path.Combine(workspace.Root, "manifest.json"),
            """{"cases":[{"id":"bad/id","image":"../outside.png","targetLanguage":"","expectation":"pass"}]}""");

        var manifest = TestLabManifest.Load(workspace.Root);

        Equal(0, manifest.Cases.Count);
        Equal(true, manifest.Errors.SequenceEqual(["case-invalid"]));
    }

    private static void RejectsTraversalTestLabCaseId()
    {
        using var workspace = TemporaryWorkspace.Create();
        var image = Path.Combine(workspace.Root, "input.png");
        using (var bitmap = new Bitmap(2, 2)) bitmap.Save(image);
        File.WriteAllText(Path.Combine(workspace.Root, "manifest.json"),
            """{"cases":[{"id":"..","image":"input.png","targetLanguage":"简体中文","expectation":"all-translated"}]}""");

        var manifest = TestLabManifest.Load(workspace.Root);

        Equal(0, manifest.Cases.Count);
        Equal(true, manifest.Errors.SequenceEqual(["case-invalid"]));
    }

    private static void CompressesMissingTestLabManifestFieldsIntoFailure()
    {
        using var workspace = TemporaryWorkspace.Create();
        File.WriteAllText(Path.Combine(workspace.Root, "manifest.json"), """{"cases":[{"id":"missing-fields"}]}""");

        var manifest = TestLabManifest.Load(workspace.Root);

        Equal(0, manifest.Cases.Count);
        Equal(true, manifest.Errors.Count == 1);
    }

    private static void KeepsTestLabArtifactsInsideImmutableRunRoot()
    {
        using var workspace = TemporaryWorkspace.Create();
        var first = TestLabArtifactPaths.Create(workspace.Root, "run-one", "case-one");
        var second = TestLabArtifactPaths.Create(workspace.Root, "run-two", "case-one");

        Equal(false, string.Equals(first.CaseRoot, second.CaseRoot, StringComparison.OrdinalIgnoreCase));
        Equal(true, first.CaseRoot.StartsWith(Path.Combine(workspace.Root, "runs", "run-one") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        Equal(true, second.CaseRoot.StartsWith(Path.Combine(workspace.Root, "runs", "run-two") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        Throws<ArgumentException>(() => TestLabArtifactPaths.Create(workspace.Root, "run-one", ".."));
    }

    private static void WritesSanitizedTestLabCaseFailureForCorruptImage()
    {
        using var workspace = TemporaryWorkspace.Create();
        File.WriteAllText(Path.Combine(workspace.Root, "broken.png"), "not-an-image");
        File.WriteAllText(Path.Combine(workspace.Root, "manifest.json"),
            """{"cases":[{"id":"broken","image":"broken.png","targetLanguage":"简体中文","expectation":"all-translated"}]}""");

        var exitCode = TestLabRunner.RunAsync(
            ["local", "--root", workspace.Root],
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(1, exitCode);
        var reportPath = Directory.GetFiles(Path.Combine(workspace.Root, "reports"), "*.json").Single();
        var report = File.ReadAllText(reportPath);
        Equal(true, report.Contains("artifact-input-failed", StringComparison.Ordinal));
        Equal(false, report.Contains("not-an-image", StringComparison.Ordinal));
    }

    private static void ReportsUnverifiedQualityDespiteStructuralMatch()
    {
        var region = new VisualTextRegion("r0", "News", [], new PixelRect(10, 10, 60, 24),
            VisualTextRole.Control, VisualStructureConfidence.Medium);
        var outcome = new ApplicationTranslationOutcome(true, TranslationPipelineStage.None, "completed",
            [], [region], [new TranslationResult(true, "新闻", "ok")], []);
        var evidence = TestLabEvidenceAssessment.Create(true, true, outcome);
        Equal("met", evidence.StructuralExpectation);
        Equal("unverified", evidence.ContentQuality);
        Equal("unreviewed", evidence.VisualReadability);
        var missingArtifact = TestLabEvidenceAssessment.Create(true, false, outcome);
        Equal("not-met", missingArtifact.StructuralExpectation);
        Equal("unavailable", missingArtifact.VisualReadability);
    }

    private static void ReportsKnownIncompleteContentAndLayout()
    {
        var region = new VisualTextRegion("r0", "News", [], new PixelRect(10, 10, 60, 24),
            VisualTextRole.Control, VisualStructureConfidence.Medium);
        var contentFailure = new ApplicationTranslationOutcome(false, TranslationPipelineStage.Translation,
            "translation-failed", [], [region], [new TranslationResult(false, string.Empty, "failed")], []);
        Equal("known-incomplete", TestLabEvidenceAssessment.Create(false, true, contentFailure).ContentQuality);

        var layoutFailure = new ApplicationTranslationOutcome(false, TranslationPipelineStage.Layout,
            "layout-incomplete", [], [region], [new TranslationResult(true, "新闻", "ok")], []);
        Equal("known-incomplete", TestLabEvidenceAssessment.Create(false, true, layoutFailure).VisualReadability);

        var noRecognizedContent = new ApplicationTranslationOutcome(false, TranslationPipelineStage.Ocr,
            "ocr-failed", [], [], [], []);
        Equal("unavailable", TestLabEvidenceAssessment.Create(false, true, noRecognizedContent).ContentQuality);
        Equal("unavailable", TestLabEvidenceAssessment.InputFailure().ContentQuality);
    }

    private static void LabelsOnlyStructuralExpectationAtRunLevel()
    {
        var route = new TestLabRouteReport("local", true, true, "none", "completed", 1, 1, 0,
            "case/after-local.png", [], Evidence: new TestLabEvidenceAssessment("met", "unverified", "unreviewed"));
        var testCase = new TestLabCaseReport("case", "case.png", "hash", "case/before.png",
            "all-translated", [route]);
        Equal("expectation-met", TestLabRunStatus.From([testCase]));
        Equal("failed", TestLabRunStatus.From([testCase with
        {
            Routes = [route with { ExpectationMet = false }]
        }]));
        Equal("failed", TestLabRunStatus.From([]));

        using var workspace = TemporaryWorkspace.Create();
        TestLabReportWriter.Write(workspace.Root, new TestLabRunReport(
            "evidence-run", DateTimeOffset.UnixEpoch, "local", "expectation-met", [], [testCase]));
        var json = File.ReadAllText(Path.Combine(workspace.Root, "evidence-run.json"));
        Equal(true, json.Contains("\"SchemaVersion\": 3", StringComparison.Ordinal));
        Equal(true, json.Contains("\"ContentQuality\": \"unverified\"", StringComparison.Ordinal));
        Equal(true, json.Contains("\"VisualReadability\": \"unreviewed\"", StringComparison.Ordinal));
    }

    private static void QualityOracleDetectsIndependentFailures()
    {
        using var workspace = TemporaryWorkspace.Create();
        File.WriteAllText(Path.Combine(workspace.Root, "quality-oracle.json"), """
            {"schemaVersion":1,"cases":[{"id":"example","imageSha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            "targetLanguage":"简体中文","regions":[{"id":"body","bounds":{"x":10,"y":10,"width":120,"height":30},
            "sourceText":"Hello Codex","meaningSignals":[{"id":"greeting","anyOf":["你好"]}],
            "protectedTokens":["Codex"],"mustRender":true,"minimumFontPixels":10}]}]}
            """);
        var oracle = TestLabQualityOracle.Load(workspace.Root)!;
        Equal(true, oracle.Covers([new TestLabCase("example", "example.png", "简体中文", "all-translated")]));
        Equal(false, oracle.Covers([new TestLabCase("other", "example.png", "简体中文", "all-translated")]));
        var bounds = new PixelRect(10, 10, 120, 30);
        var line = new OcrTextBlock("Hello Codex", bounds, []);
        var region = new VisualTextRegion("r0", line.Text, [line], bounds,
            VisualTextRole.Body, VisualStructureConfidence.High);
        var layout = new InPlaceLayoutItem(line, "你好 Codex", bounds,
            InPlaceBackgroundMode.FlatSampled, System.Drawing.Color.Black,
            System.Drawing.Color.White, SourceRegion: region,
            TextLayout: new TranslationTextLayout(true, 12, 14, SourceFontWeight.Normal,
                SourceFontCategory.SansSerif, false, SourceTextAlignment.Left, 0, "Segoe UI"));
        var good = new ApplicationTranslationOutcome(true, TranslationPipelineStage.None, "completed",
            [line], [region], [new TranslationResult(true, "你好 Codex", "ok")], [layout]);
        const string hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var accepted = oracle.Assess("example", hash, "简体中文", good, true)!;
        Equal(0, accepted.Findings.Count);
        Equal("screened-needs-human-review", accepted.ContentQuality);
        Equal("machine-checked-needs-visual-review", accepted.LayoutQuality);

        var badOcr = good with { Regions = [region with { SourceText = "Hello Code" }] };
        Equal(true, oracle.Assess("example", hash, "简体中文", badOcr, true)!.Findings
            .Any(item => item.Stage == "ocr" && item.Code == "text-mismatch"));
        var badMeaning = good with
        {
            Results = [new TranslationResult(true, "您好 Codex", "ok")],
            LayoutItems = [layout with { Text = "您好 Codex" }],
        };
        Equal(true, oracle.Assess("example", hash, "简体中文", badMeaning, true)!.Findings
            .Any(item => item.Stage == "translation" && item.Code == "meaning-signal-missing"));
        var badToken = good with { Results = [new TranslationResult(true, "你好 编码助手", "ok")] };
        Equal(true, oracle.Assess("example", hash, "简体中文", badToken, true)!.Findings
            .Any(item => item.Stage == "integrity" && item.Code == "protected-token-count"));
        var badLayout = good with { LayoutItems = [] };
        Equal(true, oracle.Assess("example", hash, "简体中文", badLayout, true)!.Findings
            .Any(item => item.Stage == "layout" && item.Code == "region-not-rendered"));
        var tinyLayout = good with { LayoutItems = [layout with
        {
            TextLayout = layout.TextLayout! with { FontSizePixels = 5 },
        }] };
        Equal(true, oracle.Assess("example", hash, "简体中文", tinyLayout, true)!.Findings
            .Any(item => item.Stage == "layout" && item.Code == "unreadable-layout"));
        Equal("blocked", oracle.Assess("example", new string('b', 64), "简体中文", good, true)!.ContentQuality);
        var route = new TestLabRouteReport("local", true, true, "none", "completed", 1, 1, 0,
            "after.png", [], Oracle: oracle.Assess("example", hash, "简体中文", badMeaning, true));
        Equal("meaning-needs-human-review", route.Oracle!.ContentQuality);
        Equal("quality-needs-review", TestLabRunStatus.From([new TestLabCaseReport(
            "example", "example.png", hash, "before.png", "all-translated", [route])]));
        var mixedFailure = badMeaning with { Regions = [region with { SourceText = "Hello Code" }] };
        var mixedAssessment = oracle.Assess("example", hash, "简体中文", mixedFailure, true)!;
        Equal("failed", mixedAssessment.ContentQuality);
        Equal("quality-failed", TestLabRunStatus.From([new TestLabCaseReport(
            "example", "example.png", hash, "before.png", "all-translated", [route with { Oracle = mixedAssessment }])]));

        var orderedOracle = new OracleFile(1, [new OracleCase("example", hash, "简体中文", [
            new OracleRegion("first", new OracleBounds(10, 10, 100, 20), "First", [], [], false, 0),
            new OracleRegion("second", new OracleBounds(10, 40, 100, 20), "Second", [], [], false, 0),
        ])]);
        File.WriteAllText(Path.Combine(workspace.Root, "quality-oracle.json"), JsonSerializer.Serialize(orderedOracle));
        var orderCheck = TestLabQualityOracle.Load(workspace.Root)!;
        var first = region with { Id = "first", SourceText = "First", Bounds = new PixelRect(10, 10, 100, 20) };
        var second = region with { Id = "second", SourceText = "Second", Bounds = new PixelRect(10, 40, 100, 20) };
        var reversed = good with { Regions = [second, first], Results = [] };
        Equal(true, orderCheck.Assess("example", hash, "简体中文", reversed, true)!.Findings
            .Any(item => item.Stage == "region" && item.Code == "reading-order"));

        File.WriteAllText(Path.Combine(workspace.Root, "quality-oracle.json"), """
            {"schemaVersion":1,"cases":[{"id":"example","imageSha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            "targetLanguage":"简体中文","regions":[
            {"id":"first","bounds":{"x":10,"y":10,"width":120,"height":30},"sourceText":"First",
             "meaningSignals":[],"protectedTokens":[],"mustRender":true,"minimumFontPixels":15,"fontTier":"body"},
            {"id":"second","bounds":{"x":10,"y":50,"width":120,"height":30},"sourceText":"Second",
             "meaningSignals":[],"protectedTokens":[],"mustRender":true,"minimumFontPixels":15,"fontTier":"body"}]}]}
            """);
        var typographyOracle = TestLabQualityOracle.Load(workspace.Root)!;
        var firstLine = new OcrTextBlock("First", new PixelRect(10, 10, 120, 30), []);
        var secondLine = new OcrTextBlock("Second", new PixelRect(10, 50, 120, 30), []);
        var firstRegion = new VisualTextRegion("first", "First", [firstLine], firstLine.Bounds,
            VisualTextRole.Body, VisualStructureConfidence.High);
        var secondRegion = new VisualTextRegion("second", "Second", [secondLine], secondLine.Bounds,
            VisualTextRole.Body, VisualStructureConfidence.High);
        var firstLayout = layout with { SourceBlock = firstLine, SourceRegion = firstRegion,
            Text = "第一", Bounds = firstLine.Bounds,
            TextLayout = layout.TextLayout! with { FontSizePixels = 19.5 } };
        var secondLayout = layout with { SourceBlock = secondLine, SourceRegion = secondRegion,
            Text = "第二", Bounds = secondLine.Bounds,
            TextLayout = layout.TextLayout! with { FontSizePixels = 16.5 } };
        var mismatchedType = good with
        {
            OcrBlocks = [firstLine, secondLine],
            Regions = [firstRegion, secondRegion],
            Results = [new TranslationResult(true, "第一", "ok"),
                new TranslationResult(true, "第二", "ok")],
            LayoutItems = [firstLayout, secondLayout],
        };
        Equal(true, typographyOracle.Assess("example", hash, "简体中文", mismatchedType, true)!
            .Findings.Any(item => item.Code == "font-tier-inconsistent"));
        var matchedType = mismatchedType with
        {
            LayoutItems = [firstLayout, secondLayout with
                { TextLayout = secondLayout.TextLayout! with { FontSizePixels = 18.5 } }],
        };
        Equal(false, typographyOracle.Assess("example", hash, "简体中文", matchedType, true)!
            .Findings.Any(item => item.Code == "font-tier-inconsistent"));
    }

    private static void RejectsStandalonePromptInstructions()
    {
        Equal(true, LocalLmtPromptEchoGuard.IsLikelyEcho(
            "保持每个令牌的值不变。", "Acaban de resolver [[K0]]"));
        Equal(true, LocalLmtPromptEchoGuard.IsLikelyEcho(
            "保持每个令牌的值不变。", "Graft reduce el consumo de tokens, llamadas y tiempo."));
        Equal(true, LocalLmtPromptEchoGuard.IsLikelyEcho(
            "Keep every token like [[K0]] unchanged.", "Acaban de resolver [[K0]]"));
        Equal(true, LocalLmtPromptEchoGuard.IsLikelyEcho(
            "保持每个令牌的值不变，例如 @tamarajtran· Sep 18", "tamara @tamarajtran· Sep 18"));
        Equal(true, LocalLmtPromptEchoGuard.IsLikelyEcho(
            "保持每个令牌的值不变，例如 Codex", "Introducing the Motion MCP for Codex"));
        Equal(true, LocalLmtPromptEchoGuard.IsLikelyEcho(
            "保持每个元素的 Codex 保持不变。",
            "Codex builds the scenes, animations and transitions directly inside motion."));
        Equal(false, LocalLmtPromptEchoGuard.IsLikelyEcho(
            "保持每个令牌的值不变。", "Keep every token unchanged."));
        Equal(false, LocalLmtPromptEchoGuard.IsLikelyEcho(
            "保持每个令牌的值不变，例如 Codex", "Keep every token unchanged, for example Codex."));
        Equal(false, LocalLmtPromptEchoGuard.IsLikelyEcho(
            "这个工具能减少令牌消耗。", "Esta herramienta reduce tokens."));
    }

    private static void AcceptsProtectedFallbackTestLabExpectation()
    {
        var result = new TranslationResult(
            false,
            string.Empty,
            "Local LMT changed a protected local value.",
            Diagnostic: new TranslationDiagnostic(
                TranslationDiagnosticKind.ProtectedContentChanged,
                TranslationDiagnosticStage.RetryChunk,
                2));
        var outcome = new ApplicationTranslationOutcome(
            false,
            TranslationPipelineStage.Translation,
            "translation-failed",
            [],
            [],
            [result],
            []);

        Equal(true, TestLabExpectation.IsMet("protected-fallback", outcome));
        Equal(false, TestLabExpectation.IsMet("all-translated", outcome));
    }

    private static void RejectsPartialAllTranslatedTestLabExpectation()
    {
        var region = new VisualTextRegion(
            "r0", "News", [], new PixelRect(10, 10, 60, 24),
            VisualTextRole.Control, VisualStructureConfidence.Medium);
        var outcome = new ApplicationTranslationOutcome(
            true,
            TranslationPipelineStage.None,
            "completed",
            [],
            [region],
            [new TranslationResult(true, "新闻", "ok")],
            []);

        Equal(false, TestLabExpectation.IsMet("all-translated", outcome));
    }

    private static void AcceptsUnchangedProtectedRegionInAllTranslatedExpectation()
    {
        var region = new VisualTextRegion(
            "r0", "@moon", [], new PixelRect(10, 10, 60, 24),
            VisualTextRole.Metadata, VisualStructureConfidence.High);
        var outcome = new ApplicationTranslationOutcome(
            true,
            TranslationPipelineStage.None,
            "completed",
            [],
            [region],
            [new TranslationResult(true, "@moon", "protected")],
            []);

        Equal(true, TestLabExpectation.IsMet("all-translated", outcome));
    }

    private static void IntentionalIdentityPreservationIsCompleteButMissingOrdinaryLayoutStillFails()
    {
        var identityBounds = new PixelRect(10, 10, 260, 24);
        var bodyBounds = new PixelRect(10, 50, 260, 24);
        var identityBlock = new OcrTextBlock("tamara @tamara · Sep 18", identityBounds, []);
        var bodyBlock = new OcrTextBlock("First", bodyBounds, []);
        var identity = new VisualTextRegion("identity", identityBlock.Text, [identityBlock],
            identityBounds, VisualTextRole.Metadata, VisualStructureConfidence.High);
        var body = new VisualTextRegion("body", bodyBlock.Text, [bodyBlock],
            bodyBounds, VisualTextRole.Body, VisualStructureConfidence.High);
        var results = new TranslationResult[]
        {
            new(true, "tamara @tamara · 9月18日", "completed"),
            new(true, "第一段", "completed"),
        };
        var bodyItem = new InPlaceLayoutItem(bodyBlock, "第一段", bodyBounds,
            InPlaceBackgroundMode.FlatSampled, Color.Black, Color.White,
            SourceRegion: body);
        var rejection = new InPlaceLayoutRejection(1, "identity-pixels-preserved");
        var complete = new ApplicationTranslationOutcome(true, TranslationPipelineStage.None,
            "completed", [identityBlock, bodyBlock], [identity, body], results,
            [bodyItem], [rejection]);
        Equal(true, InPlaceLayoutService.HasCompleteCoverage(complete.Regions, complete.Results,
            complete.LayoutItems, complete.LayoutRejections));
        Equal(false, TestLabExpectation.IsMet("all-translated", complete));
        Equal(true, TestLabExpectation.IsMet("all-translated", complete,
            allowIntentionalPreservation: true));
        Equal(false, TestLabExpectation.IsMet("all-translated", complete with { LayoutItems = [] },
            allowIntentionalPreservation: true));
        Equal(false, TestLabExpectation.IsMet("all-translated", complete with
        {
            LayoutItems = [bodyItem with { SourceRegion = identity }]
        }, allowIntentionalPreservation: true));
        Equal(false, TestLabExpectation.IsMet("all-translated", complete with
        {
            LayoutItems = [bodyItem, bodyItem]
        }, allowIntentionalPreservation: true));
        Equal(false, TestLabExpectation.IsMet("all-translated", complete with
        {
            LayoutRejections = [rejection, new InPlaceLayoutRejection(2, "unsafe-background")]
        }, allowIntentionalPreservation: true));
        Equal(false, TestLabExpectation.IsMet("all-translated", complete with
        {
            IsSuccess = false,
            Results = [results[0], new TranslationResult(false, "First", "network-failed")],
        }, allowIntentionalPreservation: true));
    }

    private static void RendersTestLabArtifactFromAsyncMtaContinuation()
    {
        using var workspace = TemporaryWorkspace.Create();
        var output = Path.Combine(workspace.Root, "after.png");
        using var bitmap = new Bitmap(20, 20);

        Task.Run(() => TestLabVisualArtifactWriter.Write(bitmap, [], false, "fixture-failed", output))
            .GetAwaiter().GetResult();

        Equal(true, File.Exists(output));
        using var rendered = new Bitmap(output);
        Equal(20, rendered.Width);
        Equal(20, rendered.Height);
    }

    private static void AppliesSharedTypographyPlanInTestLabRendering()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var writer = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.TestLab", "TestLabVisualArtifactWriter.cs"));
        var factory = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "Overlay", "TranslationOverlayElementFactory.cs"));

        Equal(true, writer.Contains("TranslationOverlayElementFactory.CreatePositioned(item, 1, 1)", StringComparison.Ordinal));
        Equal(true, factory.Contains("layout.FontFamilyName", StringComparison.Ordinal));
        Equal(true, factory.Contains("layout.LineHeightPixels", StringComparison.Ordinal));
        Equal(true, factory.Contains("layout.IsItalic", StringComparison.Ordinal));
        Equal(true, factory.Contains("layout.Alignment", StringComparison.Ordinal));
        Equal(true, factory.Contains("layout.RotationDegrees", StringComparison.Ordinal));
        Equal(true, factory.Contains("layout.FontWeight", StringComparison.Ordinal));
    }

    private static void WritesChunkTwoProtectedMarkerFailureIntoTestLabReport()
    {
        using var workspace = TemporaryWorkspace.Create();
        var report = new TestLabRunReport(
            "fixture-run",
            DateTimeOffset.UnixEpoch,
            "local",
            "failed",
            [],
            [new TestLabCaseReport(
                "fixture",
                "fixture.png",
                "sanitized-hash",
                "fixture\\before.png",
                "all-translated",
                [new TestLabRouteReport(
                    "local",
                    false,
                    false,
                    "translation",
                    "protected-token-missing",
                    1,
                    0,
                    1,
                    "fixture\\after-local.png",
                    [new TestLabChunkReport(2, 41, "chunk-retry", "protected-token-missing", 2, 1)])])]);

        TestLabReportWriter.Write(workspace.Root, report);

        var markdown = File.ReadAllText(Path.Combine(workspace.Root, "fixture-run.md"));
        Equal(true, markdown.Contains("chunk=2", StringComparison.Ordinal));
        Equal(true, markdown.Contains("protected-token-missing", StringComparison.Ordinal));
        Equal(false, markdown.Contains("fixture OCR source", StringComparison.Ordinal));
    }

    private static void ClassifiesSanitizedLocalLmtFailureFamiliesInTestLab()
    {
        static ApplicationTranslationOutcome Outcome(TranslationDiagnosticKind kind) => new(
            false,
            TranslationPipelineStage.Translation,
            "translation-failed",
            [],
            [],
            [new TranslationResult(false, string.Empty, "sanitized", Diagnostic: new TranslationDiagnostic(kind, TranslationDiagnosticStage.Initial))],
            []);

        Equal("model-output", TestLabReportWriter.Categorize(Outcome(TranslationDiagnosticKind.EmptyTranslation)));
        Equal("model-output", TestLabReportWriter.Categorize(Outcome(TranslationDiagnosticKind.PromptEcho)));
        Equal("semantic-validation", TestLabReportWriter.Categorize(Outcome(TranslationDiagnosticKind.SourceUnchanged)));
        Equal("semantic-validation", TestLabReportWriter.Categorize(Outcome(TranslationDiagnosticKind.MissingTargetLanguage)));
        Equal("model-process-output", TestLabReportWriter.Categorize(Outcome(TranslationDiagnosticKind.ProcessFailure)));
    }

    private static void ClassifiesUnmergedProviderResultInTestLab()
    {
        var outcome = new ApplicationTranslationOutcome(
            false, TranslationPipelineStage.Translation, "translation-failed", [], [],
            [new TranslationResult(false, string.Empty, "translation-pending")], []);

        Equal("provider-pending", TestLabReportWriter.Categorize(outcome));
        Equal("provider-pending", TestLabReportWriter.CreateFailureReports(outcome).Single().Category);
    }

    private static void ClassifiesExhaustedDeepSeekStructuralRecoveryInTestLab()
    {
        var outcome = new ApplicationTranslationOutcome(
            false, TranslationPipelineStage.Translation, "translation-failed", [], [],
            [new TranslationResult(false, string.Empty, "DeepSeek batch recovery reached its bounded attempt limit.")], []);

        Equal("model-output", TestLabReportWriter.Categorize(outcome));
        Equal("model-output", TestLabReportWriter.CreateFailureReports(outcome).Single().Category);
    }

    private static void ClassifiesUnrecognizedProviderStatusInTestLabWithoutExposingIt()
    {
        const string privateStatus = "unrecognized private provider detail";
        var outcome = new ApplicationTranslationOutcome(
            false, TranslationPipelineStage.Translation, "translation-failed", [], [],
            [new TranslationResult(false, string.Empty, privateStatus)], []);

        Equal("unclassified-provider-failure", TestLabReportWriter.Categorize(outcome));
        var failure = TestLabReportWriter.CreateFailureReports(outcome).Single();
        Equal("unclassified-provider-failure", failure.Category);
        Equal(false, failure.Category.Contains(privateStatus, StringComparison.Ordinal));
    }

    private static void ReportsSanitizedPerResultTestLabFailureDiagnostics()
    {
        var region = new VisualTextRegion(
            "r0",
            "한국어 본문입니다.",
            [new OcrTextBlock("한국어 본문입니다.", new PixelRect(0, 0, 40, 10), [])],
            new PixelRect(0, 0, 40, 10),
            VisualTextRole.Body,
            VisualStructureConfidence.High);
        var outcome = new ApplicationTranslationOutcome(
            false,
            TranslationPipelineStage.Translation,
            "translation-failed",
            [],
            [new VisualTextRegion("ok", "English", [], new PixelRect(0, 0, 1, 1), VisualTextRole.Metadata, VisualStructureConfidence.High), region],
            [
                new TranslationResult(true, "private translated text", "completed"),
                new TranslationResult(
                    false,
                    string.Empty,
                    "private provider status",
                    Diagnostic: new TranslationDiagnostic(
                        TranslationDiagnosticKind.MissingTargetLanguage,
                        TranslationDiagnosticStage.RetryChunk,
                        2)),
            ],
            []);

        var failures = TestLabReportWriter.CreateFailureReports(outcome);

        Equal(1, failures.Count);
        Equal(2, failures[0].Result);
        Equal("semantic-validation", failures[0].Category);
        Equal("missing-target-language", failures[0].Diagnostic);
        Equal("retry-chunk", failures[0].Stage);
        Equal(2, failures[0].RetryChunk!.Value);
        Equal("Korean", failures[0].SourceLanguage!);
        Equal(region.SourceText.Length, failures[0].SourceCharacterCount!.Value);
        Equal(1, failures[0].SourceLineCount!.Value);
        Equal("body", failures[0].SourceRole!);
        var serialized = System.Text.Json.JsonSerializer.Serialize(failures);
        Equal(false, serialized.Contains("private translated text", StringComparison.Ordinal));
        Equal(false, serialized.Contains("private provider status", StringComparison.Ordinal));
    }

    private static void ReportsSanitizedLocalLmtTokenIntegrityContext()
    {
        using var workspace = TemporaryWorkspace.Create();
        var model = Path.Combine(workspace.Root, "model.gguf");
        var cli = Path.Combine(workspace.Root, "llama-cli.exe");
        File.WriteAllText(model, "fixture-model");
        File.WriteAllText(cli, "fixture-cli");
        var sessions = new RecordingLocalLmtSessionFactory("译文 [[K0]] 2");
        using var provider = new LocalTranslationProvider(
            new LocalLmtOptions(model, cli, ExpectedModelSha256: LocalLmtArtifactVerifier.Sha256File(model)),
            sessions,
            collectLocalDiagnosticContext: true);
        var result = provider.TranslateAsync(
            new TranslationRequest("English", "Chinese", "source 1", textKind: TranslationTextKind.Body),
            CancellationToken.None).GetAwaiter().GetResult();
        var region = new VisualTextRegion("r0", "source 1", [], new PixelRect(0, 0, 40, 10),
            VisualTextRole.Body, VisualStructureConfidence.High);
        var outcome = new ApplicationTranslationOutcome(false, TranslationPipelineStage.Translation,
            "translation-failed", [], [region], [result], []);

        var failure = TestLabReportWriter.CreateFailureReports(outcome).Single();
        Equal("interactive-stdin", failure.GetType().GetProperty("LocalLmtInputMode")?.GetValue(failure) as string);
        Equal("after-token-restore", failure.GetType().GetProperty("IntegrityBoundary")?.GetValue(failure) as string);
        Equal(1, (int?)failure.GetType().GetProperty("ExpectedProtectedTokenCount")?.GetValue(failure));
        Equal(1, (int?)failure.GetType().GetProperty("RestoredProtectedTokenCount")?.GetValue(failure));
        Equal(1, (int?)failure.GetType().GetProperty("SourceInvariantNumberCount")?.GetValue(failure));
        Equal(2, (int?)failure.GetType().GetProperty("TranslatedInvariantNumberCount")?.GetValue(failure));
        var serialized = System.Text.Json.JsonSerializer.Serialize(failure);
        Equal(false, serialized.Contains("source 1", StringComparison.Ordinal));
        Equal(false, serialized.Contains("译文", StringComparison.Ordinal));
    }

    private static void ReportsSanitizedLocalLmtEarlyOutputContext()
    {
        using var workspace = TemporaryWorkspace.Create();
        var model = Path.Combine(workspace.Root, "model.gguf");
        var cli = Path.Combine(workspace.Root, "llama-cli.exe");
        File.WriteAllText(model, "fixture-model");
        File.WriteAllText(cli, "fixture-cli");
        var oversized = new string('x', 129);
        var sessions = new RecordingLocalLmtSessionFactory(oversized, oversized, oversized);
        using var provider = new LocalTranslationProvider(
            new LocalLmtOptions(model, cli, ExpectedModelSha256: LocalLmtArtifactVerifier.Sha256File(model)),
            sessions,
            collectLocalDiagnosticContext: true);
        var result = provider.TranslateAsync(
            new TranslationRequest("English", "Chinese", "source 1", textKind: TranslationTextKind.Body),
            CancellationToken.None).GetAwaiter().GetResult();
        var region = new VisualTextRegion("r0", "source 1", [], new PixelRect(0, 0, 40, 10),
            VisualTextRole.Body, VisualStructureConfidence.High);
        var outcome = new ApplicationTranslationOutcome(false, TranslationPipelineStage.Translation,
            "translation-failed", [], [region], [result], []);

        var failure = TestLabReportWriter.CreateFailureReports(outcome).Single();
        Equal("interactive-stdin", failure.GetType().GetProperty("LocalLmtInputMode")?.GetValue(failure) as string);
        Equal(1, (int?)failure.GetType().GetProperty("ExpectedProtectedTokenCount")?.GetValue(failure));
        Equal(0, (int?)failure.GetType().GetProperty("RestoredProtectedTokenCount")?.GetValue(failure));
        Equal(129, (int?)failure.GetType().GetProperty("ModelResponseCharacterCount")?.GetValue(failure));
        Equal(128, (int?)failure.GetType().GetProperty("MaximumAllowedResponseCharacterCount")?.GetValue(failure));
        Equal(false, (bool?)failure.GetType().GetProperty("ModelResponseContainsSourceText")?.GetValue(failure));
        Equal(null, failure.GetType().GetProperty("IntegrityBoundary")?.GetValue(failure) as string);
    }

    private static void UsesExplicitInstalledAppRootForTestLabRuntimeSettings()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var source = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.TestLab", "TestLabRunner.cs"));

        Equal(true, source.Contains("--app-root", StringComparison.Ordinal));
        Equal(true, source.Contains("ApplicationPaths.Create(parsed.AppRoot", StringComparison.Ordinal));
        Equal(true, source.Contains("new TranslationApplicationRuntime(settings, new CredentialVault(), parsed.AppRoot)", StringComparison.Ordinal));
    }

    private static void ParsesBoundedRegionAcceptanceCommands()
    {
        using var workspace = TemporaryWorkspace.Create();
        var outputRoot = Path.Combine(workspace.Root, "acceptance");
        var translated = RegionAcceptanceCommand.TryParse(
        [
            "--acceptance-region", "100", "200", "640", "360",
            "--acceptance-mode", "translated",
            "--acceptance-output-root", outputRoot,
            "--acceptance-route", "machine",
            "--acceptance-iterations", "3",
            "--acceptance-foreground-pid", "1234",
        ], out var command, out var error);

        Equal(true, translated);
        Equal(string.Empty, error);
        Equal(new Rectangle(100, 200, 640, 360), command!.Region);
        Equal(RegionAcceptanceMode.Translated, command.Mode);
        Equal(TranslationRouteKind.Machine, command.Route);
        Equal(3, command.Iterations);
        Equal(1234, command.ForegroundProcessId!.Value);
        Equal(Path.GetFullPath(outputRoot), command.OutputRoot);

        Equal(true, RegionAcceptanceCommand.TryParse(
            ["--acceptance-region", "0", "0", "10", "10", "--acceptance-mode", "live", "--acceptance-output-root", outputRoot, "--acceptance-iterations", "180"],
            out _, out _));

        Equal(false, RegionAcceptanceCommand.TryParse(
            ["--acceptance-region", "0", "0", "0", "10", "--acceptance-mode", "original", "--acceptance-output-root", outputRoot],
            out _, out _));
        Equal(false, RegionAcceptanceCommand.TryParse(
            ["--acceptance-region", "0", "0", "10", "10", "--acceptance-mode", "live", "--acceptance-output-root", outputRoot, "--acceptance-iterations", "181"],
            out _, out _));
    }

    private static void StopsSharedApplicationOrchestratorAtOcrFailure()
    {
        using var bitmap = new Bitmap(4, 4);
        var orchestrator = new TranslationApplicationOrchestrator(
            (_, _) => Task.FromResult(new OcrRecognitionResult(false, [], "ocr-failed")),
            (_, _, _) => Task.FromException<IReadOnlyList<TranslationResult>>(
                new InvalidOperationException("translation must not run")));

        var outcome = orchestrator.ExecuteAsync(
            bitmap,
            TranslationRouteKind.LocalLmt,
            "简体中文",
            static blocks => blocks,
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(false, outcome.IsSuccess);
        Equal(TranslationPipelineStage.Ocr, outcome.FailureStage);
        Equal(0, outcome.LayoutItems.Count);
    }

    private static void ReportsVisualTranslationPipelineProgress()
    {
        using var bitmap = new Bitmap(4, 4);
        var block = new OcrTextBlock("source", new PixelRect(0, 0, 4, 4), []);
        var stages = new List<TranslationPipelineStage>();
        var orchestrator = new TranslationApplicationOrchestrator(
            (_, _) => Task.FromResult(new OcrRecognitionResult(true, [block], "completed")),
            (_, requests, _) => Task.FromResult<IReadOnlyList<TranslationResult>>(
                requests.Select(static _ => new TranslationResult(true, "译文", "completed")).ToArray()));

        var outcome = orchestrator.ExecuteAsync(
            bitmap,
            TranslationRouteKind.LocalLmt,
            "简体中文",
            static blocks => blocks,
            stages.Add,
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(true, stages.SequenceEqual([TranslationPipelineStage.Ocr, TranslationPipelineStage.Translation]));
    }

    private static void ReportsTranslationPipelineStageTimingsWithoutText()
    {
        using var bitmap = new Bitmap(4, 4);
        var block = new OcrTextBlock("private source", new PixelRect(0, 0, 4, 4), []);
        var observations = new List<TranslationPipelineTelemetry>();
        var orchestrator = new TranslationApplicationOrchestrator(
            (_, _) => Task.FromResult(new OcrRecognitionResult(true, [block], "completed")),
            (_, requests, _) => Task.FromResult<IReadOnlyList<TranslationResult>>(
                requests.Select(static _ => new TranslationResult(true, "private translation", "completed")).ToArray()));

        var outcome = orchestrator.ExecuteAsync(
            bitmap,
            TranslationRouteKind.LocalLmt,
            "简体中文",
            static blocks => blocks,
            progress: null,
            telemetry: observations.Add,
            cancellationToken: CancellationToken.None).GetAwaiter().GetResult();

        Equal(1, observations.Count);
        Equal(true, observations[0].TotalMilliseconds >= 0);
    }

    private static void IgnoresFailingTranslationPipelineTelemetryObserver()
    {
        using var bitmap = new Bitmap(4, 4);
        var block = new OcrTextBlock("source", new PixelRect(0, 0, 4, 4), []);
        var orchestrator = new TranslationApplicationOrchestrator(
            (_, _) => Task.FromResult(new OcrRecognitionResult(true, [block], "completed")),
            (_, requests, _) => Task.FromResult<IReadOnlyList<TranslationResult>>(
                requests.Select(static _ => new TranslationResult(true, "译文", "completed")).ToArray()));

        var outcome = orchestrator.ExecuteAsync(
            bitmap,
            TranslationRouteKind.LocalLmt,
            "简体中文",
            static blocks => blocks,
            progress: null,
            telemetry: _ => throw new InvalidOperationException("telemetry failure"),
            cancellationToken: CancellationToken.None).GetAwaiter().GetResult();

        Equal(false, outcome is null);
    }

    private static void KeepsOverlayErrorCopyWhenTranslationLoggingFails()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var source = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "App.xaml.cs"));

        Equal(true, source.Contains("new JsonlTranslationRunLogWriter(_paths.DiagnosticsRoot)", StringComparison.Ordinal));
        Equal(true, source.Contains("翻译失败，未显示任何私密诊断。", StringComparison.Ordinal));
    }

    private static void MachineTimeoutDoesNotRetryEverySegment()
    {
        var process = new RecordingMachineTranslationProcess(_ => throw new TimeoutException());
        var provider = new LocalMachineTranslationProvider(process);
        var results = provider.TranslateManyAsync(MachineRequests("Hello world", "Open settings"),
            CancellationToken.None).GetAwaiter().GetResult();
        Equal(1, process.Calls);
        Equal(true, results.All(item => !item.IsSuccess));
    }

    private static void BundledMachineSupportsPersistentWorker()
    {
        Equal(true, typeof(BundledMachineTranslationProcess).GetConstructors()
            .Any(ctor => ctor.GetParameters().Any(parameter => parameter.Name == "keepAlive")));
        Equal(true, typeof(IDisposable).IsAssignableFrom(typeof(BundledMachineTranslationProcess)));
    }

    private static void PricesCurrentFlash()
    {
        var weekday = DeepSeekPricing.Estimate(DeepSeekModels.Flash, 1000, 2000, 3000,
            new DateTimeOffset(2026, 9, 11, 10, 0, 0, TimeSpan.FromHours(8)));
        var weekend = DeepSeekPricing.Estimate(DeepSeekModels.Flash, 1000, 2000, 3000,
            new DateTimeOffset(2026, 9, 12, 10, 0, 0, TimeSpan.FromHours(8)));
        Equal(0.02804m, weekday.EstimatedCny);
        Equal(0.01402m, weekend.EstimatedCny);
    }

    private static void MigratesLegacyPro()
    {
        using var workspace = TemporaryWorkspace.Create();
        var path = Path.Combine(workspace.Root, "settings.json");
        var legacy = AppSettings.CreateDefault(workspace.Root) with
        { DeepSeekModel = "deepseek-v4-pro", Provider = "DeepSeek", HasExplicitProviderChoice = true, CopyToClipboard = false };
        var json = System.Text.Json.JsonSerializer.Serialize(legacy);
        File.WriteAllText(path, json);
        var loaded = new SettingsStore(path, workspace.Root).LoadRecoverable();
        Equal(legacy with { DeepSeekModel = DeepSeekModels.Flash }, loaded.Settings);
        Equal(true, loaded.RequiresSave);
        Equal(json, File.ReadAllText(path));
    }

    private static void ExposesOnlyCurrentFlash()
    {
        Equal("deepseek-flash", DeepSeekModels.Flash);
        Equal(false, TranslationRouteCatalog.Options.Any(item => item.Kind == TranslationRouteKind.DeepSeekPro));
        Throws<ArgumentException>(() => DeepSeekModels.Validate("deepseek-v4-pro"));
    }

    private static void LiveCaptureFailureDrainsPendingTranslation()
    {
        using var safetyBudget = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var captures = 0;
        var calls = 0;
        var active = 0;
        var maximum = 0;
        var cancelled = 0;
        LiveTranslationSession? session = null;
        session = new LiveTranslationSession(new Rectangle(0, 0, 2, 2), _ =>
        {
            if (Interlocked.Increment(ref captures) == 2) throw new IOException("capture-failed");
            return CreateBitmap(2, 2, Color.Black);
        }, async (_, token) =>
        {
            maximum = Math.Max(maximum, Interlocked.Increment(ref active));
            try
            {
                if (Interlocked.Increment(ref calls) == 1)
                {
                    var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    using var registration = token.Register(() => { cancelled++; release.TrySetResult(); });
                    await release.Task;
                }
                return LiveTranslationFrame.Empty("machine");
            }
            finally { Interlocked.Decrement(ref active); }
        }, TranslationRouteKind.Machine, static (_, _) => Task.CompletedTask);
        session.FrameReady += (_, _) => session.Stop();
        using (session) session.StartAsync(safetyBudget.Token).GetAwaiter().GetResult();
        Equal(1, cancelled);
        Equal(1, maximum);
        Equal(2, calls);
    }

    private static void LiveTranslationCancelsChangedPendingContent()
    {
        using var safetyBudget = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var captures = 0;
        var calls = 0;
        var cancelled = 0;
        var published = new List<string>();
        LiveTranslationSession? session = null;
        session = new LiveTranslationSession(new Rectangle(0, 0, 2, 2),
            _ => CreateBitmap(2, 2, Interlocked.Increment(ref captures) == 1 ? Color.Black : Color.White),
            async (_, token) =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                {
                    var release = new TaskCompletionSource<LiveTranslationFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
                    using var registration = token.Register(() =>
                    {
                        Interlocked.Increment(ref cancelled);
                        release.TrySetResult(new LiveTranslationFrame("machine", "old", []));
                    });
                    return await release.Task;
                }
                return new LiveTranslationFrame("machine", "new", []);
            }, TranslationRouteKind.Machine, static (_, _) => Task.CompletedTask);
        session.FrameReady += (_, args) => { published.Add(args.Frame.Status); session.Stop(); };
        using (session) session.StartAsync(safetyBudget.Token).GetAwaiter().GetResult();
        Equal(1, cancelled);
        Equal(true, published.SequenceEqual(["new"]));
    }

    private static void LiveTranslationDiscardsChangedScreen()
    {
        var captures = 0;
        var published = 0;
        LiveTranslationSession? session = null;
        session = new LiveTranslationSession(new Rectangle(0, 0, 2, 2),
            _ => CreateBitmap(2, 2, ++captures == 1 ? Color.Black : Color.White),
            (_, _) => Task.FromResult(LiveTranslationFrame.Empty("machine")),
            TranslationRouteKind.Machine,
            (_, _) => { session!.Stop(); return Task.CompletedTask; });
        session.FrameReady += (_, _) => published++;
        var invalidations = 0;
        session.FrameInvalidated += (_, _) => { if (++invalidations == 2) session.Stop(); };
        using (session) session.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        Equal(0, published);
    }

    private static void UsesBoundedLiveTranslationRefreshDelays()
    {
        Equal(TimeSpan.FromMilliseconds(1200), LiveTranslationRefreshPolicy.NextDelay(TranslationRouteKind.LocalLmt, 0));
        Equal(TimeSpan.FromMilliseconds(150), LiveTranslationRefreshPolicy.NextDelay(TranslationRouteKind.Machine, 0));
        Equal(TimeSpan.FromMilliseconds(6000), LiveTranslationRefreshPolicy.NextDelay(TranslationRouteKind.LocalLmt, 9));
    }

    private static void LiveTranslationNeverOverlaps()
    {
        var active = 0;
        var maximum = 0;
        var translated = 0;
        LiveTranslationSession? session = null;
        session = new LiveTranslationSession(
            new Rectangle(0, 0, 2, 2),
            _ => CreateBitmap(2, 2, translated % 2 == 0 ? Color.Black : Color.White),
            (_, _) =>
            {
                maximum = Math.Max(maximum, Interlocked.Increment(ref active));
                translated++;
                Interlocked.Decrement(ref active);
                if (translated == 2) session!.Stop();
                return Task.FromResult(LiveTranslationFrame.Empty("local"));
            },
            TranslationRouteKind.LocalLmt,
            static (_, _) => Task.CompletedTask);
        using (session)
            session.StartAsync(CancellationToken.None).GetAwaiter().GetResult();

        Equal(1, maximum);
    }

    private static void LiveTranslationSkipsIdenticalCaptures()
    {
        var calls = 0;
        var delays = 0;
        LiveTranslationSession? session = null;
        session = new LiveTranslationSession(
            new Rectangle(0, 0, 2, 2),
            _ => CreateBitmap(2, 2, Color.Navy),
            (_, _) =>
            {
                calls++;
                return Task.FromResult(LiveTranslationFrame.Empty("local"));
            },
            TranslationRouteKind.LocalLmt,
            (_, _) =>
            {
                if (++delays == 2) session!.Stop();
                return Task.CompletedTask;
            });
        using (session)
            session.StartAsync(CancellationToken.None).GetAwaiter().GetResult();

        Equal(1, calls);
    }

    private static void LiveTranslationRetriesUnchangedFailureWithoutStopping()
    {
        var calls = 0;
        var delays = 0;
        LiveTranslationSession? session = null;
        session = new LiveTranslationSession(
            new Rectangle(0, 0, 2, 2),
            _ => CreateBitmap(2, 2, Color.DarkGreen),
            (_, _) =>
            {
                calls++;
                throw new InvalidOperationException("transient");
            },
            TranslationRouteKind.LocalLmt,
            (_, _) =>
            {
                if (++delays == 3) session!.Stop();
                return Task.CompletedTask;
            });
        using (session)
            session.StartAsync(CancellationToken.None).GetAwaiter().GetResult();

        Equal(3, calls);
    }

    private static void CancelledLiveTranslationCannotPublishLateFrame()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<LiveTranslationFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var session = new LiveTranslationSession(
            new Rectangle(0, 0, 2, 2),
            _ => CreateBitmap(2, 2, Color.Maroon),
            (_, _) =>
            {
                entered.TrySetResult();
                return release.Task;
            },
            TranslationRouteKind.LocalLmt,
            static (_, _) => Task.CompletedTask);
        var published = 0;
        session.FrameReady += (_, _) => published++;

        var running = session.StartAsync(CancellationToken.None);
        entered.Task.GetAwaiter().GetResult();
        session.Stop();
        release.TrySetResult(LiveTranslationFrame.Empty("local"));
        running.GetAwaiter().GetResult();

        Equal(0, published);
    }

    private static void LiveSelectionProducesOnePhysicalRegionThenCloses()
    {
        var selection = new LiveTranslationSelectionCoordinator(
            [new PixelRect(0, 0, 1920, 1080)]);

        var accepted = selection.TryComplete(
            new PixelPoint(120, 160),
            new PixelPoint(620, 460),
            out var region,
            out var error);

        Equal(true, accepted);
        Equal(new PixelRect(120, 160, 500, 300), region);
        Equal(string.Empty, error);
        Equal(true, selection.IsClosed);
    }

    private static void LiveSelectionRejectsCrossMonitorRegion()
    {
        var selection = new LiveTranslationSelectionCoordinator(
        [
            new PixelRect(0, 0, 1920, 1080),
            new PixelRect(1920, 0, 1920, 1080),
        ]);

        var accepted = selection.TryComplete(
            new PixelPoint(1800, 100),
            new PixelPoint(2100, 400),
            out _,
            out var error);

        Equal(false, accepted);
        Equal("请在同一显示器内重新框选", error);
        Equal(false, selection.IsClosed);
    }

    private static void LiveOverlayFailsClosedWhenExclusionFails()
    {
        Equal(false, LiveTranslationCaptureVisibilityPolicy.MustHideDuringCapture(displayAffinityApplied: true));
        Equal(true, LiveTranslationCaptureVisibilityPolicy.MustHideDuringCapture(displayAffinityApplied: false));
        Equal(true, LiveTranslationCaptureVisibilityPolicy.CanStart(
            excludeFromCaptureRequested: true, displayAffinityApplied: true));
        Equal(false, LiveTranslationCaptureVisibilityPolicy.CanStart(
            excludeFromCaptureRequested: true, displayAffinityApplied: false));
        Equal(true, LiveTranslationCaptureVisibilityPolicy.CanStart(
            excludeFromCaptureRequested: false, displayAffinityApplied: false));
    }

    private static void WindowWgcMapsAnOnScreenSelectionWithoutDpiScaling()
    {
        var crop = WindowCaptureGeometry.MapScreenRegion(
            new Rectangle(100, 50, 1200, 800),
            new Size(1200, 800),
            new Rectangle(145, 90, 640, 320));
        Equal(new Rectangle(45, 40, 640, 320), crop);

        Throws<InvalidOperationException>(() => WindowCaptureGeometry.MapScreenRegion(
            new Rectangle(100, 50, 1200, 800),
            new Size(1199, 800),
            new Rectangle(145, 90, 640, 320)));
        Throws<ArgumentOutOfRangeException>(() => WindowCaptureGeometry.MapScreenRegion(
            new Rectangle(100, 50, 1200, 800),
            new Size(1200, 800),
            new Rectangle(90, 90, 640, 320)));
    }

    private static void LiveAcceptanceCapturesSourcePixelsFromTheTargetWindow()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var source = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "App.xaml.cs"));
        Equal(true, source.Contains("LiveTranslationWindowCapture.StartAsync", StringComparison.Ordinal));
        Equal(false, source.Contains("Dispatcher.Invoke(acceptanceOverlay.BeforeCapture)", StringComparison.Ordinal));
    }

    private static void LiveSelectionUsesPhysicalCursorCoordinates()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var source = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "LiveTranslation", "LiveTranslationSelectionWindow.xaml.cs"));
        Equal(true, source.Contains("GetPhysicalCursorPos", StringComparison.Ordinal));
        Equal(false, source.Contains("GetCursorPos(out var point)", StringComparison.Ordinal));
    }

    private static void LiveAcceptanceSignalsReadinessWhileTranslationIsStillRunning()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var source = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "App.xaml.cs"));
        var readySignal = source.IndexOf("File.Move(temporaryReadyPath, readyPath, overwrite: true)", StringComparison.Ordinal);
        var sessionStart = source.IndexOf("await session.StartAsync(stop.Token)", StringComparison.Ordinal);

        Equal(true, readySignal >= 0);
        Equal(true, sessionStart >= 0);
        Equal(true, readySignal < sessionStart);
    }

    private static void LiveAcceptanceClearsStaleTranslationsWhenTheSourceChanges()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var source = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "App.xaml.cs"));
        var liveCase = source.IndexOf("case RegionAcceptanceMode.Live:", StringComparison.Ordinal);
        var sessionStart = source.IndexOf("await session.StartAsync(stop.Token)", liveCase, StringComparison.Ordinal);
        var acceptanceBlock = source[liveCase..sessionStart];

        Equal(true, acceptanceBlock.Contains("session.FrameInvalidated", StringComparison.Ordinal));
        Equal(true, acceptanceBlock.Contains("acceptanceOverlay.Clear", StringComparison.Ordinal));
    }

    private static void LiveAcceptanceUsesTheFullViewportPipeline()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var source = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "App.xaml.cs"));
        var liveCase = source.IndexOf("case RegionAcceptanceMode.Live:", StringComparison.Ordinal);
        var frameReady = source.IndexOf("session.FrameReady", liveCase, StringComparison.Ordinal);
        var acceptanceBlock = source[liveCase..frameReady];

        Equal(false, acceptanceBlock.Contains("translateChangedRegions:", StringComparison.Ordinal));
        Equal(false, acceptanceBlock.Contains("LiveTranslationFrameCoordinates.Offset", StringComparison.Ordinal));
        Equal(true, acceptanceBlock.Contains("LiveTranslationFrame.FromOutcome", StringComparison.Ordinal));
    }

    private static void ProductionLiveTranslationUsesFixedScreenRegion()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var source = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "App.xaml.cs"));
        var start = source.IndexOf("private async void StartLiveTranslation", StringComparison.Ordinal);
        var end = source.IndexOf("private void StopLiveTranslation", start, StringComparison.Ordinal);
        var productionBlock = source[start..end];

        Equal(true, productionBlock.Contains("LiveTranslationScreenCapture.StartForRegionAsync", StringComparison.Ordinal));
        Equal(true, productionBlock.Contains("screenCapture.Capture", StringComparison.Ordinal));
        Equal(true, productionBlock.Contains("CaptureSelectedPixels", StringComparison.Ordinal));
        Equal(true, productionBlock.Contains("await screenCapture.DisposeAsync()", StringComparison.Ordinal));
        Equal(false, productionBlock.Contains("LiveTranslationWindowCapture.StartForRegionAsync", StringComparison.Ordinal));
        Equal(false, productionBlock.Contains("preferNativeWindowCapture:", StringComparison.Ordinal));
        var capture = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "LiveTranslation", "LiveTranslationScreenCapture.cs"));
        Equal(true, capture.Contains("ScreenCaptureService().CaptureRegion(region)", StringComparison.Ordinal));
        Equal(false, capture.Contains("GetForegroundWindow", StringComparison.Ordinal));
        Equal(false, capture.Contains("PrintWindow", StringComparison.Ordinal));
    }

    private static void ProductionLiveTranslationOwnsStartupAndCaptureLifetime()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var source = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "App.xaml.cs"));
        var start = source.IndexOf("private async void StartLiveTranslation", StringComparison.Ordinal);
        var end = source.IndexOf("private void StopLiveTranslation", start, StringComparison.Ordinal);
        var productionBlock = source[start..end];

        Equal(true, source.Contains("_liveTranslationStartup", StringComparison.Ordinal));
        Equal(true, productionBlock.Contains("_liveTranslationStartup is not null", StringComparison.Ordinal));
        Equal(true, productionBlock.Contains("startup.Token", StringComparison.Ordinal));
        Equal(true, productionBlock.Contains("catch (OperationCanceledException)", StringComparison.Ordinal));
        Equal(true, productionBlock.Contains("screenCapture.DisposeAsync", StringComparison.Ordinal));
    }

    private static void WindowCaptureStopsOnSourceClosure()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var live = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "LiveTranslation", "LiveTranslationWindowCapture.cs"));

        Equal(true, live.Contains("IsWindow(_windowHandle)", StringComparison.Ordinal));
        Equal(true, live.Contains("window-capture-source-closed", StringComparison.Ordinal));
        Equal(true, live.Contains("window-capture-source-minimized", StringComparison.Ordinal));
    }

    private static void WindowCaptureGeometryRejectsDrift()
    {
        var original = new Rectangle(100, 50, 1200, 800);
        var method = typeof(WindowCaptureGeometry).GetMethod("EnsureStable")
            ?? throw new Exception("Window capture does not expose a geometry drift guard.");
        method.Invoke(null, [original, original, original.Size]);
        Throws<System.Reflection.TargetInvocationException>(() => method.Invoke(null,
            [original, new Rectangle(101, 50, 1200, 800), original.Size]));
        Throws<System.Reflection.TargetInvocationException>(() => method.Invoke(null,
            [original, original, new Size(1199, 800)]));
    }

    private static void MonitorCaptureDoesNotUseWindowGeometryFaults()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var source = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "Recording", "GraphicsCaptureSource.cs"));

        Equal(true, source.Contains("_enforceSourceSize", StringComparison.Ordinal));
        Equal(true, source.Contains("if (_enforceSourceSize &&", StringComparison.Ordinal));
        Equal(true, source.Contains("enforceSourceSize: false", StringComparison.Ordinal));
        Equal(true, source.Contains("enforceSourceSize: true", StringComparison.Ordinal));
        Equal(true, source.Contains("finally", StringComparison.Ordinal));
    }

    private static void WindowCaptureRejectsInvalidTargetWindows()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var source = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "LiveTranslation", "LiveTranslationWindowCapture.cs"));

        Equal(true, source.Contains("GetShellWindow", StringComparison.Ordinal));
        Equal(true, source.Contains("GetWindowThreadProcessId", StringComparison.Ordinal));
        Equal(true, source.Contains("IsIconic", StringComparison.Ordinal));
        Equal(true, source.Contains("ResolveRootWindow", StringComparison.Ordinal));
        Equal(true, source.Contains("screenRegion.Right - 1", StringComparison.Ordinal));
        Equal(true, source.Contains("screenRegion.Bottom - 1", StringComparison.Ordinal));
        Equal(true, source.Contains("WorkerW", StringComparison.Ordinal));
        Equal(true, source.Contains("Progman", StringComparison.Ordinal));
    }

    private static void LiveOverlayUsesIndependentStopControl()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var overlay = File.ReadAllText(Path.Combine(
            workspace, "src", "ClipRay.App", "LiveTranslation", "LiveTranslationOverlayWindow.xaml.cs"));
        var stop = File.ReadAllText(Path.Combine(
            workspace, "src", "ClipRay.App", "LiveTranslation", "LiveTranslationStopControlWindow.xaml.cs"));

        Equal(true, overlay.Contains("WS_EX_TRANSPARENT", StringComparison.Ordinal));
        Equal(false, overlay.Contains("RequestStop", StringComparison.Ordinal));
        Equal(true, stop.Contains("RequestStop", StringComparison.Ordinal));
    }

    private static void LiveStopControlStaysOutsideSelectedRegion()
    {
        var placed = LiveTranslationStopPlacement.TryPlaceOutside(
            new Rectangle(1500, 100, 420, 700),
            new Rectangle(0, 0, 1920, 1040),
            new Size(132, 48),
            out var bounds);

        Equal(true, placed);
        Equal(true, !bounds.IntersectsWith(new Rectangle(1500, 100, 420, 700)));
        Equal(false, LiveTranslationStopPlacement.TryPlaceOutside(
            new Rectangle(0, 0, 1920, 1040),
            new Rectangle(0, 0, 1920, 1040),
            new Size(132, 48),
            out _));
    }

    private static void MachineRouteSendsEveryRequestToMachineProvider()
    {
        var machine = new RecordingMachineTranslationProvider(requests =>
            requests.Select(static request => new TranslationResult(true, "mt:" + request.OcrText, "machine-completed")).ToArray());
        var primaryCalls = 0;
        var coordinator = new TranslationFallbackCoordinator(
            (_, _, _) =>
            {
                primaryCalls++;
                return Task.FromResult<IReadOnlyList<TranslationResult>>([]);
            },
            machine);
        var requests = MachineRequests("one", "two");

        var outcome = coordinator.TranslateAsync(
            TranslationRouteKind.Machine,
            requests,
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(0, primaryCalls);
        Equal(1, machine.Calls);
        Equal(2, machine.LastRequests.Count);
        Equal(true, outcome.Results.All(static result => result.IsSuccess));
    }

    private static void MachineFallbackRetriesOnlyFailedPrimaryBlocks()
    {
        var machine = new RecordingMachineTranslationProvider(requests =>
            requests.Select(static request => new TranslationResult(true, "fallback:" + request.OcrText, "machine-fallback-completed")).ToArray());
        var coordinator = new TranslationFallbackCoordinator(
            (_, requests, _) => Task.FromResult<IReadOnlyList<TranslationResult>>(
            [
                new TranslationResult(true, "primary-ok", "completed"),
                new TranslationResult(false, string.Empty, "primary-failed"),
                new TranslationResult(true, "primary-ok-2", "completed"),
            ]),
            machine);

        var outcome = coordinator.TranslateAsync(
            TranslationRouteKind.DeepSeekFlash,
            MachineRequests("one", "two", "three"),
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(1, machine.Calls);
        Equal(1, machine.LastRequests.Count);
        Equal("two", machine.LastRequests[0].OcrText);
        Equal("primary-ok", outcome.Results[0].Text);
        Equal("fallback:two", outcome.Results[1].Text);
        Equal("primary-ok-2", outcome.Results[2].Text);
        Equal(1, outcome.RecoveredCount);
    }

    private static void MachineFallbackPreservesPrimaryFailureWhenRecoveryFails()
    {
        var original = new TranslationResult(false, string.Empty, "primary-failed");
        var machine = new RecordingMachineTranslationProvider(_ =>
            [new TranslationResult(false, string.Empty, "machine-failed")]);
        var coordinator = new TranslationFallbackCoordinator(
            (_, _, _) => Task.FromResult<IReadOnlyList<TranslationResult>>([original]),
            machine);

        var outcome = coordinator.TranslateAsync(
            TranslationRouteKind.LocalLmt,
            MachineRequests("one"),
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(original, outcome.Results[0]);
        Equal(0, outcome.RecoveredCount);
        Equal(1, outcome.FailedCount);
    }

    private static void MachineFallbackDoesNotCountPreservedTextAsTranslated()
    {
        var machine = new RecordingMachineTranslationProvider(_ =>
            [new TranslationResult(true, "@ClipRay", "machine-preserved")]);
        var coordinator = new TranslationFallbackCoordinator(
            (_, _, _) => Task.FromResult<IReadOnlyList<TranslationResult>>(
                [new TranslationResult(false, string.Empty, "primary-failed")]),
            machine);

        var outcome = coordinator.TranslateAsync(
            TranslationRouteKind.DeepSeekFlash,
            MachineRequests("@ClipRay"),
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(TranslationSemanticOutcome.IntentionallyPreserved, outcome.Results[0].SemanticOutcome);
        Equal(0, outcome.RecoveredCount);
        Equal(0, outcome.FailedCount);
        Equal(true, outcome.Diagnostics.Any(static item => item.Code == "machine-fallback-preserved"));
    }

    private static void ApplicationTranslationPreservesMachineFallbackIdentity()
    {
        using var bitmap = new Bitmap(20, 20);
        var block = new OcrTextBlock("source", new PixelRect(2, 2, 16, 12), []);
        var trace = new TranslationExecutionTrace(TranslationRouteKind.DeepSeekFlash, 1, 1, 0);
        var orchestrator = new TranslationApplicationOrchestrator(
            (_, _) => Task.FromResult(new OcrRecognitionResult(true, [block], "completed")),
            (_, requests, _) => Task.FromResult(new TranslationBatchOutcome(
                requests.Select(static _ => new TranslationResult(true, "译文", "machine-fallback-completed")).ToArray(),
                trace)),
            (_, _, _) => []);

        var outcome = orchestrator.ExecuteAsync(
            bitmap,
            TranslationRouteKind.DeepSeekFlash,
            "简体中文",
            static blocks => blocks,
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(trace, outcome.ExecutionTrace!);
        Equal(1, outcome.ExecutionTrace!.RecoveredCount);
    }

    private static void OverlayStatusDistinguishesMachineTranslation()
    {
        var results = new[] { new TranslationResult(true, "译文", "machine-completed") };
        var direct = TranslationStatusText.FormatOverlayStatus(
            TranslationRouteKind.Machine,
            new TranslationExecutionTrace(TranslationRouteKind.Machine, 0, 0, 0),
            1,
            0,
            results);
        var fallback = TranslationStatusText.FormatOverlayStatus(
            TranslationRouteKind.DeepSeekFlash,
            new TranslationExecutionTrace(TranslationRouteKind.DeepSeekFlash, 1, 1, 0),
            1,
            0,
            results);

        Equal(true, direct.Contains("本地机翻", StringComparison.Ordinal));
        Equal(false, direct.Contains("本地 LMT", StringComparison.Ordinal));
        Equal(true, fallback.Contains("DeepSeek", StringComparison.Ordinal));
        Equal(true, fallback.Contains("机翻兜底恢复 1 处", StringComparison.Ordinal));
    }

    private static IReadOnlyList<TranslationRequest> MachineRequests(params string[] texts) =>
        texts.Select(static text => new TranslationRequest("auto", "简体中文", text)).ToArray();

    private static void MachineRouteNeverFallsThroughToCloudProvider()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var app = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "App.xaml.cs"));
        var runtime = File.ReadAllText(Path.Combine(
            workspace, "src", "ClipRay.App", "Translation", "TranslationApplicationRuntime.cs"));

        Equal(true, app.Contains("route == TranslationRouteKind.Machine", StringComparison.Ordinal));
        Equal(true, app.Contains("GetMachineTranslationProvider().TranslateManyAsync", StringComparison.Ordinal));
        Equal(false, app.Contains("machine-provider-unavailable", StringComparison.Ordinal));
        Equal(true, runtime.Contains("route == TranslationRouteKind.Machine", StringComparison.Ordinal));
        Equal(true, runtime.Contains("TranslationPipelineTrace.ObserveMachineProvider(_machine, \"direct-machine\")", StringComparison.Ordinal));
        Equal(true, runtime.Contains("machine.TranslateManyAsync(requests, cancellationToken)", StringComparison.Ordinal));
        Equal(false, runtime.Contains("machine-provider-unavailable", StringComparison.Ordinal));
    }

    private static void LocalMachineProviderBatchesProtectedEnglishToChineseRequests()
    {
        var process = new RecordingMachineTranslationProcess(request =>
            request.Texts.Select(static text => text.Replace("Save", "保存", StringComparison.Ordinal)).ToArray());
        var provider = new LocalMachineTranslationProvider(process);

        var results = provider.TranslateManyAsync(
        [
            new TranslationRequest("English", "简体中文", "Save Ctrl+S"),
            new TranslationRequest("auto", "Chinese", "Save 42"),
        ], CancellationToken.None).GetAwaiter().GetResult();

        Equal(1, process.Calls);
        Equal("en", process.LastRequest.SourceLanguage);
        Equal("zh", process.LastRequest.TargetLanguage);
        Equal(true, results.All(static result => result.IsSuccess));
        Equal("保存 Ctrl+S", results[0].Text);
        Equal("保存 42", results[1].Text);
    }

    private static void LocalMachineProviderRejectsUnsupportedLanguagePairsLocally()
    {
        var process = new RecordingMachineTranslationProcess(_ => throw new InvalidOperationException("must not run"));
        var provider = new LocalMachineTranslationProvider(process);

        var results = provider.TranslateManyAsync(
            [new TranslationRequest("Japanese", "简体中文", "保存")],
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(0, process.Calls);
        Equal(false, results[0].IsSuccess);
        Equal("machine-language-pair-unavailable", results[0].Status);
    }

    private static void LocalMachineProviderPreservesProtectedOnlyTextWithoutInvokingModel()
    {
        var process = new RecordingMachineTranslationProcess(_ =>
            throw new InvalidOperationException("protected-only text must not reach the model"));
        var provider = new LocalMachineTranslationProvider(process);

        var results = provider.TranslateManyAsync(
            MachineRequests("@OpenAI Ctrl+S 42"),
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(0, process.Calls);
        Equal(true, results[0].IsSuccess);
        Equal("@OpenAI Ctrl+S 42", results[0].Text);
    }

    private static void LocalMachineProviderTranslatesAroundProtectedInvariants()
    {
        var process = new RecordingMachineTranslationProcess(request =>
        {
            Equal(true, request.Texts.All(static text =>
                !text.Contains("@OpenAI", StringComparison.Ordinal)
                && !text.Contains("Ctrl+S", StringComparison.Ordinal)
                && !text.Contains("42", StringComparison.Ordinal)
                && !text.Contains("CLIPRAY", StringComparison.Ordinal)));
            return request.Texts.Select(static text => text
                .Replace("Hello", "你好", StringComparison.Ordinal)
                .Replace("press", "按", StringComparison.Ordinal)
                .Replace("and keep", "并保留", StringComparison.Ordinal)).ToArray();
        });
        var provider = new LocalMachineTranslationProvider(process);

        var results = provider.TranslateManyAsync(
            MachineRequests("Hello @OpenAI, press Ctrl+S and keep 42."),
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(1, process.Calls);
        Equal(true, results[0].IsSuccess);
        Equal("你好 @OpenAI, 按 Ctrl+S 并保留 42.", results[0].Text);
    }

    private static void LocalMachineProviderReportsSanitizedInvariantMismatch()
    {
        var process = new RecordingMachineTranslationProcess(_ => ["你好 7"]);
        var provider = new LocalMachineTranslationProvider(process);

        var result = provider.TranslateManyAsync(
            MachineRequests("Hello"),
            CancellationToken.None).GetAwaiter().GetResult()[0];

        Equal(false, result.IsSuccess);
        Equal("machine-content-integrity-failed", result.Status);
        Equal(TranslationDiagnosticKind.NumericInvariantChanged, result.Diagnostic!.Kind);
    }

    private static void LocalMachineProviderBypassesIdentityMetadata()
    {
        var process = new RecordingMachineTranslationProcess(_ =>
            throw new InvalidOperationException("identity metadata must not reach the model"));
        var provider = new LocalMachineTranslationProvider(process);
        var request = new TranslationRequest(
            "auto",
            "简体中文",
            "OpenAI @OpenAI 2,104 posts",
            textKind: TranslationTextKind.IdentityMetadata);

        var result = provider.TranslateManyAsync([request], CancellationToken.None).GetAwaiter().GetResult()[0];

        Equal(0, process.Calls);
        Equal(true, result.IsSuccess);
        Equal(request.OcrText, result.Text);
        Equal("machine-preserved", result.Status);
    }

    private static void LocalMachineProviderPreservesMetadataAfterInvariantMismatch()
    {
        var process = new RecordingMachineTranslationProcess(_ => ["关注者 7"]);
        var provider = new LocalMachineTranslationProvider(process);
        var request = new TranslationRequest(
            "auto",
            "简体中文",
            "5.2M Followers",
            textKind: TranslationTextKind.Metadata);

        var result = provider.TranslateManyAsync([request], CancellationToken.None).GetAwaiter().GetResult()[0];

        Equal(true, result.IsSuccess);
        Equal(request.OcrText, result.Text);
        Equal("machine-preserved", result.Status);
    }

    private static void LocalMachineProviderIsolatesFailedBatch()
    {
        var process = new RecordingMachineTranslationProcess(request =>
        {
            if (request.Texts.Count > 1) throw new InvalidOperationException("fixture batch failure");
            return ["你好"];
        });
        var provider = new LocalMachineTranslationProvider(process);

        var results = provider.TranslateManyAsync(
            MachineRequests("Hello", "World"),
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(3, process.Calls);
        Equal(true, results.All(static result => result.IsSuccess));
    }

    private static void BundledMachineWorkerTranslatesEnglishToChineseOffline()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        using var process = new BundledMachineTranslationProcess(
            Path.Combine(workspace, "artifacts", "machine-translation", "runtime", "python.exe"),
            Path.Combine(workspace, "src", "ClipRay.App", "Translation", "machine_translation_worker.py"),
            Path.Combine(workspace, "artifacts", "machine-translation", "models", "translate-en_zh-1_9"), keepAlive: true);

        var clock = System.Diagnostics.Stopwatch.StartNew();
        var translated = process.TranslateAsync(
            new MachineTranslationProcessRequest("en", "zh", ["Hello world"]),
            CancellationToken.None).GetAwaiter().GetResult();

        var cold = clock.ElapsedMilliseconds;
        clock.Restart();
        var repeated = process.TranslateAsync(new MachineTranslationProcessRequest("en", "zh", ["Hello world"]),
            CancellationToken.None).GetAwaiter().GetResult();
        var cached = clock.ElapsedMilliseconds;
        Equal(translated[0], repeated[0]);
        var fresh = process.TranslateAsync(new MachineTranslationProcessRequest("en", "zh", ["Open settings"]),
            CancellationToken.None).GetAwaiter().GetResult();
        Equal(1, fresh.Count);
        Equal(1, process.ProcessStartCount);
        Console.WriteLine($"MACHINE_TIMING cold_ms={cold} cached_ms={cached} starts={process.ProcessStartCount}");
        Equal(1, translated.Count);
        Equal(true, !string.IsNullOrWhiteSpace(translated[0]));
        Equal(false, string.Equals("Hello world", translated[0], StringComparison.OrdinalIgnoreCase));
    }

    private static void BundledMachineWorkerChunksOversizedInputWithoutTruncatingTail()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var process = new BundledMachineTranslationProcess(
            Path.Combine(workspace, "artifacts", "machine-translation", "runtime", "python.exe"),
            Path.Combine(workspace, "src", "ClipRay.App", "Translation", "machine_translation_worker.py"),
            Path.Combine(workspace, "artifacts", "machine-translation", "models", "translate-en_zh-1_9"));
        var source = string.Concat(Enumerable.Repeat("Hello world. ", 240));

        var translated = process.TranslateAsync(
            new MachineTranslationProcessRequest("en", "zh", [source]),
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(1, translated.Count);
        Equal(true, translated[0].Length >= 600);
    }

    private static void MachineModeAndFallbackSettingsDefaultSafely()
    {
        var defaults = AppSettings.CreateDefault(Path.GetTempPath());
        Equal("Machine", defaults.Provider);
        Equal(true, defaults.MachineTranslationFallbackEnabled);
        defaults.Validate();

        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var settingsXaml = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "Settings", "SettingsWindow.xaml"));
        var machineOption = settingsXaml.IndexOf("Content=\"Machine\"", StringComparison.Ordinal);
        var deepSeekOption = settingsXaml.IndexOf("Content=\"DeepSeek\"", StringComparison.Ordinal);
        Equal(true, machineOption >= 0 && deepSeekOption > machineOption);
        var appSource = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "App.xaml.cs"));
        Equal(true, appSource.Contains("_ = EnsureMachineTranslationWarmup();", StringComparison.Ordinal));
        Equal(true, appSource.Contains("var warmup = EnsureMachineTranslationWarmup();", StringComparison.Ordinal));
        Equal(true, appSource.Contains("await warmup.ConfigureAwait(true);", StringComparison.Ordinal));
    }

    private static void SeparatesRegionAndLiveProviderSettings()
    {
        var defaults = AppSettings.CreateDefault(Path.GetTempPath());
        Equal("Machine", defaults.Provider);
        Equal("Machine", defaults.LiveTranslationProvider);
        (defaults with { Provider = "Local LMT", LiveTranslationProvider = "DeepSeek" }).Validate();
        Throws<InvalidOperationException>(() =>
            (defaults with { LiveTranslationProvider = "Local LMT" }).Validate());

        using var workspace = TemporaryWorkspace.Create();
        var path = Path.Combine(workspace.Root, "settings.json");
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(new
        {
            Provider = "DeepSeek",
            HasExplicitProviderChoice = true,
            TranslationTargetLanguage = "简体中文",
            ShowLiveTranslationBorder = true,
        }));
        var migrated = new SettingsStore(path, workspace.Root).LoadRecoverable();
        Equal("DeepSeek", migrated.Settings.Provider);
        Equal("Machine", migrated.Settings.LiveTranslationProvider);
        Equal(false, migrated.Settings.ShowLiveTranslationBorder);
        Equal(true, migrated.RequiresSave);

        var explicitSettings = migrated.Settings with { LiveTranslationProvider = "DeepSeek" };
        new SettingsStore(path, workspace.Root).Save(explicitSettings);
        Equal(explicitSettings, new SettingsStore(path, workspace.Root).Load());

        var root = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var xaml = File.ReadAllText(Path.Combine(root, "src", "ClipRay.App", "Settings", "SettingsWindow.xaml"));
        var source = File.ReadAllText(Path.Combine(root, "src", "ClipRay.App", "Settings", "SettingsWindow.xaml.cs"));
        Equal(true, xaml.Contains("x:Name=\"ProviderBox\"", StringComparison.Ordinal));
        Equal(true, xaml.Contains("x:Name=\"LiveProviderBox\"", StringComparison.Ordinal));
        Equal(true, source.Contains("LiveTranslationProvider = SelectedLiveProvider()", StringComparison.Ordinal));
        Equal(true, source.Contains("SelectComboValue(LiveProviderBox, _settings.LiveTranslationProvider)", StringComparison.Ordinal));

        EnsureWpfResources();
        using var bitmap = CreateBitmap(32, 32, Color.Black);
        var overlay = new SelectionOverlayWindow(
            new CapturedFrame(bitmap, new Rectangle(0, 0, 32, 32)),
            new PixelRect(2, 2, 20, 20),
            interactionSettings: defaults with
            {
                Provider = "DeepSeek",
                LiveTranslationProvider = "Machine",
            });
        LiveTranslationStartRequest? liveRequest = null;
        overlay.LiveTranslationRequested += (_, request) => liveRequest = request;
        try
        {
            OverlayTool.LiveTranslate.Execute(null, overlay);
            Equal(TranslationRouteKind.Machine, liveRequest!.Route);
        }
        finally
        {
            overlay.Close();
        }

        var appSource = File.ReadAllText(Path.Combine(root, "src", "ClipRay.App", "App.xaml.cs"));
        Equal(true, appSource.Contains("showBorder: LiveTranslationCaptureVisibilityPolicy.SelectedRegionBorderVisible", StringComparison.Ordinal));
        LiveVisibilitySettingsTests.SelectedRegionBorderIsVisible();
        Equal(false, xaml.Contains("x:Name=\"LiveBorderBox\"", StringComparison.Ordinal));
        Equal(false, xaml.Contains("（测试）", StringComparison.Ordinal));
    }

    private static void SettingsRoutesMachineVerificationThroughOfflineProvider()
    {
        var offlineCalls = 0;
        var cloudCalls = 0;
        var result = SettingsProviderTestRouter.TestAsync(
                "Machine",
                _ => { offlineCalls++; return Task.FromResult(new TranslationResult(true, "本机译文", "machine-completed")); },
                _ => { cloudCalls++; return Task.FromResult(new TranslationResult(true, "unused", "unused")); },
                CancellationToken.None)
            .GetAwaiter().GetResult();

        Equal(true, result.IsSuccess);
        Equal(1, offlineCalls);
        Equal(0, cloudCalls);
    }

    private static void StandaloneLocalLmtUsesConfiguredMachineFallback()
    {
        var source = File.ReadAllText(Path.Combine(
            WorkspaceLocator.Find(AppContext.BaseDirectory),
            "src", "ClipRay.App", "App.xaml.cs"));
        var start = source.IndexOf("private async Task<TranslationResult> TranslateUsingRouteAsync", StringComparison.Ordinal);
        var end = source.IndexOf("private TranslationRouteKind ResolveConfiguredTranslationRoute", start, StringComparison.Ordinal);
        var method = source[start..end];
        var localResult = method.IndexOf("result = await GetLocalTranslationProvider().TranslateAsync", StringComparison.Ordinal);
        var fallback = method.IndexOf("MachineTranslationFallbackEnabled", StringComparison.Ordinal);

        Equal(true, localResult >= 0 && fallback > localResult);
        Equal(false, method[..Math.Max(0, fallback)].Contains("return await GetLocalTranslationProvider", StringComparison.Ordinal));
    }

    private static void ExplainsVisualTranslationProgress()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var source = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "App.xaml.cs"));

        Equal(true, source.Contains("正在识别图片文字（本地 PP-OCR）", StringComparison.Ordinal));
        Equal(true, source.Contains("HasActiveSession", StringComparison.Ordinal));
        Equal(true, source.Contains("首次加载或卸载后重新加载可能需要较长时间", StringComparison.Ordinal));
        Equal(true, source.Contains("正在使用已加载的本地模型翻译", StringComparison.Ordinal));
    }

    private static void ClassifiesSharedApplicationLayoutMergeFailure()
    {
        using var bitmap = new Bitmap(4, 4);
        var block = new OcrTextBlock("source", new PixelRect(0, 0, 4, 4), []);
        var orchestrator = new TranslationApplicationOrchestrator(
            (_, _) => Task.FromResult(new OcrRecognitionResult(true, [block], "completed")),
            (_, requests, _) => Task.FromResult<IReadOnlyList<TranslationResult>>(
                requests.Select(static _ => new TranslationResult(true, "translated", "completed")).ToArray()),
            (_, _, _) => throw new InvalidOperationException("private layout detail"));

        var outcome = orchestrator.ExecuteAsync(
            bitmap,
            TranslationRouteKind.LocalLmt,
            "简体中文",
            static blocks => blocks,
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(false, outcome.IsSuccess);
        Equal(TranslationPipelineStage.Layout, outcome.FailureStage);
        Equal("layout-failed", outcome.Status);
        Equal(0, outcome.LayoutItems.Count);
    }

    private static void FailsCompletionWhenChangedRegionIsNotRendered()
    {
        using var bitmap = new Bitmap(20, 20);
        var block = new OcrTextBlock("source", new PixelRect(2, 2, 16, 12), []);
        var orchestrator = new TranslationApplicationOrchestrator(
            (_, _) => Task.FromResult(new OcrRecognitionResult(true, [block], "completed")),
            (_, requests, _) => Task.FromResult<IReadOnlyList<TranslationResult>>(
                requests.Select(static _ => new TranslationResult(true, "translated", "completed")).ToArray()),
            (_, _, _) => []);

        var outcome = orchestrator.ExecuteAsync(
            bitmap,
            TranslationRouteKind.LocalLmt,
            "简体中文",
            static blocks => blocks,
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(false, outcome.IsSuccess);
        Equal(TranslationPipelineStage.Layout, outcome.FailureStage);
        Equal("layout-incomplete", outcome.Status);
    }

    private static void ReportsSanitizedLayoutRejectionReasonsThroughSharedOrchestrator()
    {
        using var bitmap = new Bitmap(72, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var colors = new[] { Color.Red, Color.Lime, Color.Blue, Color.Yellow, Color.Magenta, Color.Cyan };
        for (var y = 0; y < bitmap.Height; y++)
        for (var x = 0; x < bitmap.Width; x++)
            bitmap.SetPixel(x, y, colors[(x * 7 + y * 11) % colors.Length]);
        var word = new OcrWord("private-source", new PixelRect(12, 8, 46, 16));
        var block = new OcrTextBlock(word.Text, word.Bounds, [word]);
        var orchestrator = new TranslationApplicationOrchestrator(
            (_, _) => Task.FromResult(new OcrRecognitionResult(true, [block], "completed")),
            (_, requests, _) => Task.FromResult<IReadOnlyList<TranslationResult>>(
                requests.Select(static _ => new TranslationResult(true, "译文", "completed")).ToArray()));

        var outcome = orchestrator.ExecuteAsync(
            bitmap,
            TranslationRouteKind.LocalLmt,
            "简体中文",
            static blocks => blocks,
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(false, outcome.IsSuccess);
        Equal("layout-incomplete", outcome.Status);
        Equal(1, outcome.LayoutRejections!.Count);
        Equal(1, outcome.LayoutRejections[0].Region);
        Equal("unsafe-background", outcome.LayoutRejections[0].Reason);
        Equal(false, outcome.LayoutRejections[0].Reason.Contains("private-source", StringComparison.Ordinal));
    }

    private static void RoutesOverlayTranslationThroughSharedOrchestrator()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var source = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "App.xaml.cs"));
        var start = source.IndexOf("private async void HandleOverlayTranslationRequested", StringComparison.Ordinal);
        var end = source.IndexOf("private async void HandleTextExtractionRequested", start, StringComparison.Ordinal);
        var method = source[start..end];

        Equal(true, method.Contains("_translationOrchestrator.ExecuteAsync", StringComparison.Ordinal));
        Equal(false, method.Contains("VisualTranslationRequestFactory.Create", StringComparison.Ordinal));
        Equal(false, method.Contains("InPlaceLayoutService.BuildRegions", StringComparison.Ordinal));
    }

    private static int RunRealTranslationProbe()
    {
        using var source = CreateBitmap(900, 190, Color.White);
        using (var graphics = Graphics.FromImage(source))
        using (var font = new System.Drawing.Font("Arial", 30, System.Drawing.FontStyle.Bold))
        {
            graphics.DrawString("Open the settings window.", font, Brushes.Black, new PointF(18, 18));
            graphics.DrawString("Save the screenshot locally.", font, Brushes.Black, new PointF(18, 92));
        }

        var ocr = new OcrService().RecognizeAsync(source, CancellationToken.None).GetAwaiter().GetResult();
        if (OverlayTranslationGuard.GetFailure(ocr) is { } failure)
        {
            Console.Error.WriteLine($"REAL_TRANSLATION_FAIL OCR {failure}");
            return 1;
        }
        using var provider = new LocalTranslationProvider(LocalLmtOptions.Discover());
        var requests = ocr.Blocks.Select(static block => new TranslationRequest("auto", "简体中文", block.Text)).ToArray();
        var results = provider.TranslateManyAsync(requests, CancellationToken.None).GetAwaiter().GetResult();
        Console.WriteLine($"REAL_TRANSLATION_OCR_LINES={ocr.Blocks.Count}");
        for (var index = 0; index < results.Count; index++)
        {
            Console.WriteLine($"REAL_TRANSLATION_{index + 1}={results[index].Text}");
            Console.WriteLine($"REAL_TRANSLATION_STATUS_{index + 1}={results[index].Status}");
        }
        var passed = results.Count == requests.Length
            && results.All(static result => result.IsSuccess)
            && results.Any(static result => result.Text.Any(value => value is >= '\u3400' and <= '\u9FFF'));
        Console.WriteLine(passed ? "REAL_TRANSLATION_PASS" : "REAL_TRANSLATION_FAIL");
        return passed ? 0 : 1;
    }

    private static int RunRealMachineTranslationProbe()
    {
        using var source = CreateBitmap(1100, 300, Color.White);
        using (var graphics = Graphics.FromImage(source))
        using (var heading = new System.Drawing.Font("Arial", 25, System.Drawing.FontStyle.Bold))
        using (var body = new System.Drawing.Font("Arial", 21, System.Drawing.FontStyle.Regular))
        {
            graphics.DrawString("OpenAI @OpenAI 2,104 posts", heading, Brushes.Black, new PointF(18, 18));
            graphics.DrawString("Our mission is to ensure artificial general intelligence benefits all of humanity.", body, Brushes.Black, new PointF(18, 82));
            graphics.DrawString("We are hiring: openai.com/jobs   4 Following   5.2M Followers", body, Brushes.Black, new PointF(18, 142));
        }

        using var ocrService = new OcrService();
        var ocr = ocrService.RecognizeAsync(source, CancellationToken.None).GetAwaiter().GetResult();
        var regions = VisualTextRegionBuilder.Build(ocr.Blocks, source);
        var requests = VisualTranslationRequestFactory.Create(regions, "简体中文", DeepSeekModels.Flash);
        if (!ocr.IsSuccess || requests.Length == 0)
        {
            Console.Error.WriteLine("REAL_MACHINE_TRANSLATION_FAIL stage=ocr");
            return 1;
        }

        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var provider = new LocalMachineTranslationProvider(new BundledMachineTranslationProcess(
            Path.Combine(workspace, "artifacts", "machine-translation", "runtime", "python.exe"),
            Path.Combine(workspace, "src", "ClipRay.App", "Translation", "machine_translation_worker.py"),
            Path.Combine(workspace, "artifacts", "machine-translation", "models", "translate-en_zh-1_9")));
        var results = provider.TranslateManyAsync(requests, CancellationToken.None).GetAwaiter().GetResult();
        var statuses = string.Join(",", results
            .GroupBy(static result => result.Status)
            .Select(static group => $"{group.Key}={group.Count()}")
            .OrderBy(static value => value, StringComparer.Ordinal));
        for (var index = 0; index < results.Count; index++)
            Console.WriteLine($"REAL_MACHINE_TRANSLATION_ITEM index={index} kind={requests[index].TextKind} status={results[index].Status} diagnostic={results[index].Diagnostic?.Kind.ToString() ?? "none"}");
        var passed = results.Count == requests.Length && results.All(static result => result.IsSuccess);
        Console.WriteLine($"REAL_MACHINE_TRANSLATION blocks={ocr.Blocks.Count} regions={regions.Count} requests={requests.Length} statuses={statuses}");
        Console.WriteLine(passed ? "REAL_MACHINE_TRANSLATION_PASS" : "REAL_MACHINE_TRANSLATION_FAIL stage=translation");
        return passed ? 0 : 1;
    }

    private static int RunRealLocalLmtFixtureProbe(string fixturePath)
    {
        var fullPath = Path.GetFullPath(fixturePath);
        if (!File.Exists(fullPath))
        {
            Console.Error.WriteLine("REAL_LOCAL_LMT_FAIL fixture missing");
            return 1;
        }

        using var source = new Bitmap(fullPath);
        using var ocrService = new OcrService();
        var ocrWatch = Stopwatch.StartNew();
        var ocr = ocrService.RecognizeAsync(source, CancellationToken.None).GetAwaiter().GetResult();
        ocrWatch.Stop();
        var regions = VisualTextRegionBuilder.Build(ocr.Blocks, source)
            .Where(static region => region.SourceText.Trim().Length >= 3)
            .Take(10)
            .ToArray();
        var requests = VisualTranslationRequestFactory.Create(regions, "简体中文", DeepSeekModels.Flash);
        if (!ocr.IsSuccess || requests.Length == 0)
        {
            Console.Error.WriteLine("REAL_LOCAL_LMT_FAIL local OCR did not produce translatable text");
            return 1;
        }

        using var provider = new LocalTranslationProvider(LocalLmtOptions.Discover());
        var translationWatch = Stopwatch.StartNew();
        var results = provider.TranslateManyAsync(requests, CancellationToken.None).GetAwaiter().GetResult();
        translationWatch.Stop();
        var successCount = results.Count(static result => result.IsSuccess);
        Console.WriteLine($"REAL_LOCAL_LMT fixture={Path.GetFileName(fullPath)} ocr_blocks={ocr.Blocks.Count} selected={requests.Length} ocr_ms={ocrWatch.Elapsed.TotalMilliseconds:F0} translation_ms={translationWatch.Elapsed.TotalMilliseconds:F0} success={successCount}/{results.Count}");
        for (var index = 0; index < results.Count; index++)
        {
            var sourceText = Regex.Replace(requests[index].OcrText, @"\s+", " ").Trim();
            Console.WriteLine($"REAL_LOCAL_LMT_RESULT_{index + 1}={results[index].IsSuccess}|{requests[index].TextKind}|{sourceText}|{results[index].Text}|{results[index].Status}");
        }
        return successCount == requests.Length ? 0 : 1;
    }

    private static int RunRealLocalLmtQualityProbe()
    {
        var options = LocalLmtOptions.Discover();
        var requests = new[]
        {
            new TranslationRequest("English", "简体中文", "I previously promised a reset for every 1M in additional active users for Codex, until 10M.", textKind: TranslationTextKind.Body),
            new TranslationRequest("English", "简体中文", "We blew past that and have been silent since 10M. Little surprise for you tomorrow.", textKind: TranslationTextKind.Body),
            new TranslationRequest("English", "简体中文", "[Flame War] I Tried Sneaking a Girl into a Women-Prohibited Zone!!", textKind: TranslationTextKind.Body),
            new TranslationRequest("Japanese", "简体中文", "はくちょうの尾をたどってこと座の姫の灯り、あの川を渡ればアルタイルに会える サブ垢→@azumino_114514 なら ぷれー→anemoi", textKind: TranslationTextKind.Body),
        };
        var arguments = $"--model \"{options.ModelPath}\" --conversation --escape --no-display-prompt --no-warmup --simple-io --log-disable --temp 0 --ctx-size 4096 --predict 256 --gpu-layers 99";
        using (var session = LocalLmtProcessSession.StartAsync(
            new LocalLmtSessionStart(options.LlamaCliPath, arguments),
            CancellationToken.None).GetAwaiter().GetResult())
        {
            for (var index = 0; index < requests.Length; index++)
            {
                var prompt = $"Translate the following text from English into Chinese.\\nEnglish: {requests[index].OcrText}\\nChinese:";
                var raw = session.TranslateAsync(prompt, CancellationToken.None).GetAwaiter().GetResult();
                Console.WriteLine($"REAL_LOCAL_LMT_RAW_{index + 1}={Regex.Replace(raw, @"\s+", " ").Trim()}");
            }
            var japaneseProtectedText = requests[3].OcrText.Replace("@azumino_114514", "[[K0]]", StringComparison.Ordinal);
            var japaneseProtectedPrompt = $"Translate the following text from Japanese into Chinese. Keep every token like [[K0]] unchanged.\\nJapanese: {japaneseProtectedText}\\nChinese:";
            var japaneseProtectedRaw = session.TranslateAsync(japaneseProtectedPrompt, CancellationToken.None).GetAwaiter().GetResult();
            Console.WriteLine($"REAL_LOCAL_LMT_JAPANESE_TOKEN_RAW={Regex.Replace(japaneseProtectedRaw, @"\s+", " ").Trim()}");
            var tokenSource = "I previously promised a reset for every [[K0]] in additional active users for Codex, until [[K1]].";
            var tokenPrompt = $"Translate the following text from English into Chinese. Keep every token like [[K0]] unchanged.\\nEnglish: {tokenSource}\\nChinese:";
            var tokenRaw = session.TranslateAsync(tokenPrompt, CancellationToken.None).GetAwaiter().GetResult();
            Console.WriteLine($"REAL_LOCAL_LMT_TOKEN_RAW={Regex.Replace(tokenRaw, @"\s+", " ").Trim()}");
            var secondTokenSource = "We blew past that and have been silent since [[K0]]. Little surprise for you tomorrow.";
            var secondTokenPrompt = $"Translate the following text from English into Chinese. Keep every token like [[K0]] unchanged.\\nEnglish: {secondTokenSource}\\nChinese:";
            var secondTokenRaw = session.TranslateAsync(secondTokenPrompt, CancellationToken.None).GetAwaiter().GetResult();
            Console.WriteLine($"REAL_LOCAL_LMT_TOKEN_RAW_2={Regex.Replace(secondTokenRaw, @"\s+", " ").Trim()}");
        }
        using var provider = new LocalTranslationProvider(options);
        var watch = Stopwatch.StartNew();
        var results = provider.TranslateManyAsync(requests, CancellationToken.None).GetAwaiter().GetResult();
        watch.Stop();
        Console.WriteLine($"REAL_LOCAL_LMT_QUALITY elapsed_ms={watch.Elapsed.TotalMilliseconds:F0} success={results.Count(static result => result.IsSuccess)}/{results.Count}");
        for (var index = 0; index < results.Count; index++)
            Console.WriteLine($"REAL_LOCAL_LMT_QUALITY_{index + 1}={results[index].IsSuccess}|{results[index].Text}|{results[index].Status}");
        return results.All(static result => result.IsSuccess) ? 0 : 1;
    }

    private static int RunRealHotkeyDisplayProbe()
    {
        var failed = false;
        foreach (var canonical in new[]
                 {
                     "D1",
                     "Shift+D1",
                     "OemQuestion",
                     "Alt+OemQuestion",
                     "Shift+OemQuestion",
                     "OemSemicolon",
                     "OemPlus",
                     "Shift+OemPlus",
                     "OemOpenBrackets",
                     "OemCloseBrackets",
                     "OemPipe",
                     "OemQuotes",
                     "OemTilde",
                     "NumPad1",
                     "Add",
                 })
        {
            var display = HotkeyDisplayFormatter.Format(GlobalHotkey.Parse(canonical));
            Console.WriteLine($"{canonical} => {display}");
            failed |= string.IsNullOrWhiteSpace(display)
                || display.Contains("Oem", StringComparison.OrdinalIgnoreCase)
                || display.EndsWith($"+{canonical}", StringComparison.OrdinalIgnoreCase);
        }

        Console.WriteLine(failed ? "REAL_HOTKEY_DISPLAY_FAIL" : "REAL_HOTKEY_DISPLAY_PASS");
        return failed ? 1 : 0;
    }

    private static int RunRealWindowsGraphicsCaptureProbe()
    {
        try
        {
            System.Windows.Forms.Application.SetHighDpiMode(System.Windows.Forms.HighDpiMode.PerMonitorV2);
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            using var frame = new ScreenCaptureService()
                .CaptureCurrentMonitorWithWindowsGraphicsAsync()
                .GetAwaiter()
                .GetResult();
            stopwatch.Stop();
            Console.WriteLine(
                $"REAL_WGC_CAPTURE_PASS size={frame.Bitmap.Width}x{frame.Bitmap.Height} bounds={frame.ScreenBounds} capture_ms={stopwatch.Elapsed.TotalMilliseconds:F1} system_feedback={frame.HasSystemCaptureFeedback}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"REAL_WGC_CAPTURE_FAIL type={exception.GetType().Name} message={exception.Message.Replace(Environment.NewLine, " ")}");
            return 1;
        }
    }

    private static int RunRealPpOcrProbe(string fixturePath)
    {
        var fullPath = Path.GetFullPath(fixturePath);
        if (!File.Exists(fullPath))
        {
            Console.Error.WriteLine("REAL_PPOCR_FAIL fixture missing");
            return 1;
        }

        using var source = new Bitmap(fullPath);
        var result = new OcrService().RecognizeAsync(source, CancellationToken.None).GetAwaiter().GetResult();
        if (!result.IsSuccess || result.Blocks.Count == 0)
        {
            Console.Error.WriteLine("REAL_PPOCR_FAIL local OCR did not return text");
            return 1;
        }

        Console.WriteLine($"REAL_PPOCR_BLOCKS={result.Blocks.Count}");
        foreach (var block in result.Blocks.Take(80))
        {
            Console.WriteLine($"REAL_PPOCR_TEXT={block.Text}|bounds={block.Bounds}|words={block.Words.Count}");
            foreach (var word in block.Words)
                Console.WriteLine($"REAL_PPOCR_WORD={word.Bounds}|{word.Text}");
        }
        var regions = VisualTextRegionBuilder.Build(result.Blocks);
        Console.WriteLine($"REAL_VISUAL_REGIONS={regions.Count}");
        foreach (var region in regions.Take(80))
        {
            Console.WriteLine($"REAL_VISUAL_REGION={region.Id}|{region.Role}|{region.Confidence}|{region.Bounds}|{region.SourceText.Replace('\n', ' ')}");
            var sampleX = Math.Clamp((int)region.Bounds.Left - 2, 0, source.Width - 1);
            var sampleY = Math.Clamp((int)(region.Bounds.Top + region.Bounds.Height / 2), 0, source.Height - 1);
            var style = SourceVisualStyleEstimator.EstimateRegion(source, region, result.Blocks, source.GetPixel(sampleX, sampleY));
            var layout = TranslationTextLayoutPolicy.Fit(region.SourceText, region.Bounds, style);
            Console.WriteLine($"REAL_VISUAL_STYLE={region.Id}|glyph={style.GlyphHeightPixels:F1}|line={style.LineHeightPixels:F1}|font={layout.FontSizePixels:F1}|render={layout.ShouldRender}|weight={style.FontWeight}");
            foreach (var line in region.Lines)
            {
                var lineStyle = SourceVisualStyleEstimator.Estimate(source, line, result.Blocks, source.GetPixel(sampleX, sampleY));
                Console.WriteLine($"REAL_VISUAL_LINE={region.Id}|bounds={line.Bounds}|glyph={lineStyle.GlyphHeightPixels:F1}|text={line.Text}");
            }
        }
        return 0;
    }

    private static int RunRealLayoutBaseline(string fixturePath)
    {
        using var source = new Bitmap(Path.GetFullPath(fixturePath));
        using var ocrService = new OcrService();
        var ocr = ocrService.RecognizeAsync(source, CancellationToken.None).GetAwaiter().GetResult();
        if (!ocr.IsSuccess) return 1;
        var regions = VisualTextRegionBuilder.Build(ocr.Blocks, source);

        VisualTextRegion Exact(string text) => regions.Single(region => region.SourceText.Trim() == text);
        SourceVisualStyle Style(VisualTextRegion region)
        {
            var x = Math.Clamp((int)region.Bounds.Left - 2, 0, source.Width - 1);
            var y = Math.Clamp((int)(region.Bounds.Top + region.Bounds.Height / 2), 0, source.Height - 1);
            return SourceVisualStyleEstimator.EstimateRegion(source, region, ocr.Blocks, source.GetPixel(x, y));
        }
        static bool Between(double value, double minimum, double maximum) => value >= minimum && value <= maximum;

        var heading = regions.Where(region => region.SourceText.Trim() == "Machines")
            .OrderByDescending(region => region.Bounds.Height)
            .First();
        var subtitle = Exact("Manage the devices connected to your tailnet. Learn more");
        var network = regions.Single(region => region.SourceText.Contains("Network", StringComparison.Ordinal));
        var operating = Exact("Operating systems");
        var cloud = Exact("Cloud providers");
        var containers = Exact("Containers");
        var cardBody = regions.Single(region => region.SourceText.Contains("Tailscale works best", StringComparison.Ordinal));
        var headingLayout = TranslationTextLayoutPolicy.Fit("机器", heading.Bounds, Style(heading));
        var operatingLayout = TranslationTextLayoutPolicy.Fit("操作系统", operating.Bounds, Style(operating));

        var passed = Between(Style(heading).GlyphHeightPixels, 26, 34)
            && Between(Style(subtitle).GlyphHeightPixels, 18, 26)
            && Between(Style(network).GlyphHeightPixels, 18, 26)
            && Between(Style(operating).GlyphHeightPixels, 14, 20)
            && Between(Style(cloud).GlyphHeightPixels, 14, 20)
            && Between(Style(containers).GlyphHeightPixels, 14, 20)
            && cardBody.Lines.Count == 2
            && !cardBody.SourceText.Contains("Operating systems", StringComparison.Ordinal)
            && !cardBody.SourceText.Contains("Cloud providers", StringComparison.Ordinal)
            && !cardBody.SourceText.Contains("Containers", StringComparison.Ordinal)
            && Between(headingLayout.FontSizePixels, 32, 34)
            && Between(operatingLayout.FontSizePixels, 17.5, 18.5);
        Console.WriteLine(passed
            ? "REAL_LAYOUT_BASELINE_PASS image_uploaded=false"
            : "REAL_LAYOUT_BASELINE_FAIL image_uploaded=false");
        return passed ? 0 : 1;
    }

    private static int RunRealLayoutSafety(string fixturePath)
    {
        using var source = new Bitmap(Path.GetFullPath(fixturePath));
        using var ocrService = new OcrService();
        var ocr = ocrService.RecognizeAsync(source, CancellationToken.None).GetAwaiter().GetResult();
        if (!ocr.IsSuccess || ocr.Blocks.Count == 0) return 1;
        var regions = VisualTextRegionBuilder.Build(ocr.Blocks, source);
        var requests = VisualTranslationRequestFactory.Create(regions, "简体中文", DeepSeekModels.Flash);
        var results = regions
            .Select(static _ => new TranslationResult(true, "译文", "local layout diagnostic"))
            .ToArray();
        var items = InPlaceLayoutService.BuildRegions(source, regions, results);
        for (var index = 0; index < requests.Length; index++)
        {
            Console.WriteLine($"REAL_LAYOUT_SAFETY_REQUEST={requests[index].Model}|{requests[index].TextKind}|{requests[index].Scene}|{requests[index].OcrText.Replace('\n', ' ')}");
        }
        foreach (var item in items)
        {
            var foreground = item.ForegroundColor;
            Console.WriteLine(
                $"REAL_LAYOUT_SAFETY_RENDERED={(item.SourceRegion?.SourceText ?? item.SourceBlock.Text).Replace('\n', ' ')}"
                + $"|fg=#{foreground.R:X2}{foreground.G:X2}{foreground.B:X2}"
                + $"|glyph={item.SourceStyle?.GlyphHeightPixels:F1}"
                + $"|font={item.TextLayout?.FontSizePixels:F1}"
                + $"|align={item.TextLayout?.Alignment}"
                + $"|bounds={item.Bounds}");
        }
        var renderedIds = items
            .Select(static item => item.SourceRegion?.Id)
            .Where(static id => id is not null)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var region in regions.Where(region => !renderedIds.Contains(region.Id)))
        {
            using var mask = TextReplacementMask.Build(source, region);
            var repair = MaskedBackgroundRepair.Repair(source, mask);
            var sampleX = Math.Clamp((int)region.Bounds.Left - 2, 0, source.Width - 1);
            var sampleY = Math.Clamp((int)(region.Bounds.Top + region.Bounds.Height / 2), 0, source.Height - 1);
            var style = SourceVisualStyleEstimator.EstimateRegion(
                source,
                region,
                regions.SelectMany(static candidate => candidate.Lines).ToArray(),
                source.GetPixel(sampleX, sampleY));
            var layout = TranslationTextLayoutPolicy.Fit("译文", region.Bounds, style);
            Console.WriteLine(
                $"REAL_LAYOUT_SAFETY_RETAINED={region.SourceText.Replace('\n', ' ')}"
                + $"|mask={mask.Confidence}|repair={repair.Confidence}"
                + $"|style={style.Confidence}|glyph={style.GlyphHeightPixels:F1}"
                + $"|layout={layout.ShouldRender}|font={layout.FontSizePixels:F1}");
        }
        Console.WriteLine($"REAL_LAYOUT_SAFETY regions={regions.Count} rendered={items.Count} retained={regions.Count - items.Count} image_uploaded=false");
        return 0;
    }

    private static int RunRealPpOcrTwiceProbe(string fixturePath)
    {
        var fullPath = Path.GetFullPath(fixturePath);
        if (!File.Exists(fullPath))
        {
            Console.Error.WriteLine("REAL_PPOCR_FAIL fixture missing");
            return 1;
        }

        using var source = new Bitmap(fullPath);
        using var service = new OcrService();
        service.WarmupAsync(CancellationToken.None).GetAwaiter().GetResult();
        Thread.Sleep(TimeSpan.FromSeconds(4));
        var first = Stopwatch.StartNew();
        var firstResult = service.RecognizeAsync(source, CancellationToken.None).GetAwaiter().GetResult();
        first.Stop();
        var second = Stopwatch.StartNew();
        var secondResult = service.RecognizeAsync(source, CancellationToken.None).GetAwaiter().GetResult();
        second.Stop();
        Console.WriteLine($"REAL_PPOCR_FIRST_SECONDS={first.Elapsed.TotalSeconds:F3}");
        Console.WriteLine($"REAL_PPOCR_SECOND_SECONDS={second.Elapsed.TotalSeconds:F3}");
        Console.WriteLine($"REAL_PPOCR_BLOCKS={firstResult.Blocks.Count},{secondResult.Blocks.Count}");
        return firstResult.IsSuccess && secondResult.IsSuccess && firstResult.Blocks.Count > 0 && secondResult.Blocks.Count > 0 ? 0 : 1;
    }

    private static int RunRealRapidOcrVariants(string fixturePath)
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var paths = RapidOcrModelPaths.Resolve(workspace);
        var classifier = Path.Combine(AppContext.BaseDirectory, "models", "v5", "ch_PP-LCNet_x0_25_textline_ori_cls_mobile.onnx");
        var models = RapidOcrModelSet.PPOCRv6Small with
        {
            DetModelPath = paths.Detector,
            ClsModelPath = classifier,
            RecModelPath = paths.Recognizer,
            KeysPath = paths.Dictionary,
        };
        using var ocr = new RapidOcr();
        ocr.InitModels(models);
        using var source = new Bitmap(Path.GetFullPath(fixturePath));
        using var inverted = InvertBitmap(source);
        var variants = new (string Name, Bitmap Bitmap, RapidOcrOptions Options)[]
        {
            ("inverted", inverted, RapidOcrOptions.PPOCRv6 with { DoAngle = false }),
        };
        foreach (var variant in variants)
        {
            using var stream = new MemoryStream();
            variant.Bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
            using var bitmap = SKBitmap.Decode(stream.ToArray());
            var stopwatch = Stopwatch.StartNew();
            var result = ocr.Detect(bitmap, variant.Options);
            stopwatch.Stop();
            var text = result.StrRes.ReplaceLineEndings(" | ");
            Console.WriteLine($"RAPIDOCR_VARIANT={variant.Name} SECONDS={stopwatch.Elapsed.TotalSeconds:F3}");
            Console.WriteLine($"RAPIDOCR_TEXT={text}");
        }
        using var invertedStream = new MemoryStream();
        inverted.Save(invertedStream, System.Drawing.Imaging.ImageFormat.Png);
        foreach (var threadCount in new[] { 12, 16, 24 })
        {
            using var sessionOptions = RapidOcr.GetDefaultSessionOptions(threadCount);
            using var threadedOcr = new RapidOcr();
            threadedOcr.InitModels(models, sessionOptions);
            using var bitmap = SKBitmap.Decode(invertedStream.ToArray());
            _ = threadedOcr.Detect(bitmap, RapidOcrOptions.PPOCRv6 with { DoAngle = false });
            var stopwatch = Stopwatch.StartNew();
            var result = threadedOcr.Detect(bitmap, RapidOcrOptions.PPOCRv6 with { DoAngle = false });
            stopwatch.Stop();
            Console.WriteLine($"RAPIDOCR_THREADS={threadCount} SECONDS={stopwatch.Elapsed.TotalSeconds:F3} HAS_LENGTH={result.StrRes.Contains("length", StringComparison.Ordinal)}");
        }
        return 0;
    }

    private static Bitmap InvertBitmap(Bitmap source)
    {
        var result = new Bitmap(source.Width, source.Height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        using var graphics = Graphics.FromImage(result);
        using var attributes = new System.Drawing.Imaging.ImageAttributes();
        attributes.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix(
        [
            [-1, 0, 0, 0, 0],
            [0, -1, 0, 0, 0],
            [0, 0, -1, 0, 0],
            [0, 0, 0, 1, 0],
            [1, 1, 1, 0, 1],
        ]));
        graphics.DrawImage(source, new Rectangle(0, 0, source.Width, source.Height), 0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
        return result;
    }

    private static int RunRealWindowsOcrProbe(string fixturePath)
    {
        using var source = new Bitmap(fixturePath);
        var runtimeType = typeof(OcrService).Assembly.GetType("ClipRay.App.Ocr.WindowsOcrRuntime")
            ?? throw new InvalidOperationException("Windows OCR runtime type is unavailable.");
        var runtime = Activator.CreateInstance(runtimeType, [null])!;
        var method = runtimeType.GetMethod("RecognizeAsync")!;
        var task = (Task)method.Invoke(runtime, [source, CancellationToken.None])!;
        task.GetAwaiter().GetResult();
        var words = (System.Collections.IEnumerable)task.GetType().GetProperty("Result")!.GetValue(task)!;
        var count = 0;
        foreach (var word in words)
        {
            count++;
            if (count <= 40)
            {
                var text = word.GetType().GetProperty("Text")?.GetValue(word) as string;
                Console.WriteLine($"REAL_WINDOWS_OCR_TEXT={text}");
            }
        }
        Console.WriteLine($"REAL_WINDOWS_OCR_WORDS={count}");
        return count > 0 ? 0 : 1;
    }

    private static int RunRealDeepSeekProbe(string? targetOverride = null)
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClipRay", "settings.json");
        var settings = new SettingsStore(settingsPath, workspace).Load();
        if (!string.Equals(settings.Provider, "DeepSeek", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(settings.CredentialName))
        {
            Console.Error.WriteLine("REAL_DEEPSEEK_FAIL configured DeepSeek credential is unavailable");
            return 1;
        }

        const string mixedSource = "验证 result: DeepSeek";
        var provider = new DeepSeekTranslationProvider(new CredentialVault());
        var targetLanguage = targetOverride ?? settings.TranslationTargetLanguage;
        var results = provider.TranslateBlocksAsync(
            [
                new TranslationRequest("English", targetLanguage, "Open the settings window.", settings.DeepSeekModel),
                new TranslationRequest("English", targetLanguage, "Save the screenshot locally.", settings.DeepSeekModel),
                new TranslationRequest("English", targetLanguage, "src/ClipRay.App/Translation/DeepSeekTranslationProvider.cs", settings.DeepSeekModel),
                new TranslationRequest("auto", targetLanguage, mixedSource, settings.DeepSeekModel),
            ],
            settings.CredentialName,
            CancellationToken.None).GetAwaiter().GetResult();
        if (results.Count != 4 || results.Any(static result => !result.IsSuccess || string.IsNullOrWhiteSpace(result.Text)))
        {
            Console.Error.WriteLine($"REAL_DEEPSEEK_FAIL strict batch response was rejected: {results.FirstOrDefault()?.Status}");
            return 1;
        }
        if (string.Equals(results[3].Text.Trim(), mixedSource, StringComparison.Ordinal))
        {
            Console.Error.WriteLine("REAL_DEEPSEEK_FAIL mixed-language item was returned unchanged");
            return 1;
        }

        Console.WriteLine("REAL_DEEPSEEK_PASS single_batch_items=4 mixed_item_changed=true");
        return 0;
    }

    private static int RunRealImageDeepSeekProbe(string fixturePath, string targetLanguage = "English")
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClipRay", "settings.json");
        var settings = new SettingsStore(settingsPath, workspace).Load();
        if (!string.Equals(settings.Provider, "DeepSeek", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(settings.CredentialName))
        {
            Console.Error.WriteLine("REAL_IMAGE_DEEPSEEK_FAIL configured DeepSeek credential is unavailable");
            return 1;
        }

        using var source = new Bitmap(Path.GetFullPath(fixturePath));
        using var ocrService = new OcrService();
        var ocr = ocrService.RecognizeAsync(source, CancellationToken.None).GetAwaiter().GetResult();
        if (!ocr.IsSuccess || ocr.Blocks.Count == 0)
        {
            Console.Error.WriteLine("REAL_IMAGE_DEEPSEEK_FAIL local OCR returned no text");
            return 1;
        }

        var regions = VisualTextRegionBuilder.Build(ocr.Blocks);
        var requests = VisualTranslationRequestFactory.Create(regions, targetLanguage, settings.DeepSeekModel);
        var results = new DeepSeekTranslationProvider(new CredentialVault())
            .TranslateBlocksAsync(requests, settings.CredentialName, CancellationToken.None)
            .GetAwaiter().GetResult();
        for (var index = 0; index < results.Count; index++)
        {
            Console.WriteLine($"REAL_IMAGE_DEEPSEEK_CASE={index}|success={results[index].IsSuccess}|changed={!string.Equals(requests[index].OcrText, results[index].Text, StringComparison.Ordinal)}|status={results[index].Status}");
            Console.WriteLine("REAL_IMAGE_DEEPSEEK_RESULT=" + System.Text.Json.JsonSerializer.Serialize(new
            {
                index,
                source = requests[index].OcrText,
                translated = results[index].Text,
                success = results[index].IsSuccess,
            }));
        }
        if (results.Count != requests.Length || results.Any(static result => !result.IsSuccess || string.IsNullOrWhiteSpace(result.Text)))
        {
            Console.Error.WriteLine($"REAL_IMAGE_DEEPSEEK_FAIL strict batch response was rejected: {results.FirstOrDefault(static result => !result.IsSuccess)?.Status}");
            return 1;
        }
        var items = InPlaceLayoutService.BuildRegions(source, regions, results);
        foreach (var item in items)
        {
            Console.WriteLine("REAL_IMAGE_DEEPSEEK_RENDERED=" + System.Text.Json.JsonSerializer.Serialize(new
            {
                source = item.SourceRegion?.SourceText ?? item.SourceBlock.Text,
                translated = item.Text,
            }));
        }
        Console.WriteLine($"REAL_IMAGE_DEEPSEEK_LAYOUT regions={regions.Count} rendered={items.Count} retained={regions.Count - items.Count}");
        Console.WriteLine($"REAL_IMAGE_DEEPSEEK_PASS single_batch_items={results.Count} target={targetLanguage} image_uploaded=false");
        return 0;
    }

    private static int RunRealMultilingualDeepSeekProbe()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClipRay", "settings.json");
        var settings = new SettingsStore(settingsPath, workspace).Load();
        if (!string.Equals(settings.Provider, "DeepSeek", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(settings.CredentialName))
        {
            Console.Error.WriteLine("REAL_MULTILINGUAL_DEEPSEEK_FAIL configured credential unavailable");
            return 1;
        }

        var cases = new[]
        {
            (Name: "JapaneseToChinese", Target: "简体中文", Text: "アイコンの子がまばたきしています。", Script: "Cjk"),
            (Name: "JapaneseHashtagToChinese", Target: "简体中文", Text: "かわいいイラストです。 #イラスト", Script: "Cjk"),
            (Name: "KoreanToChinese", Target: "简体中文", Text: "오늘 날씨가 정말 좋습니다.", Script: "Cjk"),
            (Name: "RussianToChinese", Target: "简体中文", Text: "Сегодня очень хорошая погода.", Script: "Cjk"),
            (Name: "FrenchToChinese", Target: "简体中文", Text: "Le temps est très agréable aujourd'hui.", Script: "Cjk"),
            (Name: "ArabicToChinese", Target: "简体中文", Text: "الطقس جميل جدًا اليوم.", Script: "Cjk"),
            (Name: "ThaiToChinese", Target: "简体中文", Text: "วันนี้อากาศดีมาก", Script: "Cjk"),
            (Name: "ChineseToEnglish", Target: "English", Text: "今天的天气非常好。", Script: "Latin"),
            (Name: "ChineseToJapanese", Target: "日本語", Text: "今天的天气非常好。", Script: "Japanese"),
            (Name: "ChineseToFrench", Target: "Français", Text: "今天的天气非常好。", Script: "Latin"),
            (Name: "ChineseToRussian", Target: "Русский", Text: "今天的天气非常好。", Script: "Cyrillic"),
        };
        var requests = cases
            .Select(item => new TranslationRequest(
                "auto",
                item.Target,
                item.Text,
                settings.DeepSeekModel,
                TranslationTextKind.Body,
                "screen_capture",
                "Translate the complete body text naturally and faithfully."))
            .ToArray();
        var results = new DeepSeekTranslationProvider(new CredentialVault())
            .TranslateBlocksAsync(requests, settings.CredentialName, CancellationToken.None)
            .GetAwaiter().GetResult();
        for (var index = 0; index < cases.Length; index++)
        {
            var result = results[index];
            Console.WriteLine($"REAL_MULTILINGUAL_CASE={cases[index].Name}|success={result.IsSuccess}|changed={!string.Equals(cases[index].Text, result.Text, StringComparison.Ordinal)}|status={result.Status}");
            Console.WriteLine("REAL_MULTILINGUAL_RESULT=" + System.Text.Json.JsonSerializer.Serialize(new
            {
                cases[index].Name,
                source = cases[index].Text,
                translated = result.Text,
                result.IsSuccess,
            }));
        }
        static bool HasScript(string text, string script) => script switch
        {
            "Cjk" => text.Any(value => value is >= '\u3400' and <= '\u9FFF'),
            "Latin" => text.Any(value => value is >= 'A' and <= 'Z' or >= 'a' and <= 'z'),
            "Japanese" => text.Any(value => value is >= '\u3040' and <= '\u30FF'),
            "Cyrillic" => text.Any(value => value is >= '\u0400' and <= '\u052F'),
            _ => false,
        };
        var passed = results.Count == cases.Length
            && results.Select((result, index) => result.IsSuccess
                && !string.Equals(cases[index].Text, result.Text, StringComparison.Ordinal)
                && HasScript(result.Text, cases[index].Script)).All(static value => value);
        Console.WriteLine(passed ? "REAL_MULTILINGUAL_DEEPSEEK_PASS" : "REAL_MULTILINGUAL_DEEPSEEK_FAIL");
        return passed ? 0 : 1;
    }

    private static int RunRealJapaneseQualityProbe()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClipRay", "settings.json");
        var settings = new SettingsStore(settingsPath, workspace).Load();
        if (!string.Equals(settings.Provider, "DeepSeek", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(settings.CredentialName))
        {
            Console.Error.WriteLine("REAL_JAPANESE_QUALITY_FAIL configured credential unavailable");
            return 1;
        }

        var regions = new[]
        {
            VisualRegion("r0", "くまの_よなよな @kumano_yonayona·6h", VisualTextRole.Metadata),
            VisualRegion("r1", "Show translation", VisualTextRole.Control),
            VisualRegion("r2", "アイコンの子のまばたき！！", VisualTextRole.Control),
            VisualRegion("r3", "＃イラスト", VisualTextRole.Control),
        };
        var provider = new DeepSeekTranslationProvider(new CredentialVault());
        foreach (var model in new[] { DeepSeekModels.Flash })
        {
            var requests = VisualTranslationRequestFactory.Create(regions, "简体中文", model);
            var single = provider.TranslateAsync(requests[2], settings.CredentialName, CancellationToken.None).GetAwaiter().GetResult();
            Console.WriteLine("REAL_JAPANESE_QUALITY=" + System.Text.Json.JsonSerializer.Serialize(new
            {
                model,
                mode = "single_body",
                translated = single.Text,
                success = single.IsSuccess,
                status = single.Status,
            }));
            var batch = provider.TranslateBlocksAsync(requests, settings.CredentialName, CancellationToken.None).GetAwaiter().GetResult();
            Console.WriteLine("REAL_JAPANESE_QUALITY=" + System.Text.Json.JsonSerializer.Serialize(new
            {
                model,
                mode = "mixed_batch_body",
                translated = batch[2].Text,
                success = batch[2].IsSuccess,
                status = batch[2].Status,
            }));
        }
        return 0;
    }

    private static void FindsWorkspaceRootFromNestedDirectory()
    {
        using var workspace = TemporaryWorkspace.Create();
        var nestedDirectory = Path.Combine(workspace.Root, "one", "two");
        Directory.CreateDirectory(nestedDirectory);

        Equal(workspace.Root, WorkspaceLocator.Find(nestedDirectory));
    }

    private static void ReturnsNoWorkspaceRootForAnInstalledLayout()
    {
        using var workspace = TemporaryWorkspace.Create(createSolutionMarker: false);

        Equal(true, WorkspaceLocator.TryFind(workspace.Root) is null);
    }

    private static void SavesCollisionFreeLosslessPngInWorkspace()
    {
        using var workspace = TemporaryWorkspace.Create();
        using var source = new Bitmap(2, 2);
        source.SetPixel(0, 0, Color.Red);
        source.SetPixel(1, 1, Color.FromArgb(255, 12, 34, 56));
        var timestamp = new DateTime(2026, 8, 4, 12, 34, 56, 789);
        var output = new ImageOutputService(workspace.Root);

        var first = output.SavePng(source, timestamp);
        var second = output.SavePng(source, timestamp);
        using var reloaded = new Bitmap(first);

        Equal(Path.Combine(workspace.Root, "artifacts", "screenshots", "2026-08-04"), Path.GetDirectoryName(first)!);
        NotEqual(first, second);
        Equal(2, reloaded.Width);
        Equal(source.GetPixel(1, 1).ToArgb(), reloaded.GetPixel(1, 1).ToArgb());
    }

    private static void SavesUniqueReadablePngsForConcurrentTimestampCollisions()
    {
        using var workspace = TemporaryWorkspace.Create();
        var output = new ImageOutputService(workspace.Root);
        var timestamp = new DateTime(2026, 8, 4, 12, 34, 56, 789);
        const int captureCount = 32;
        using var start = new Barrier(captureCount);

        var saves = Enumerable.Range(0, captureCount)
            .Select(index => Task.Factory.StartNew(() =>
            {
                using var bitmap = new Bitmap(2, 2);
                bitmap.SetPixel(1, 1, Color.FromArgb(255, index, 0, 0));
                start.SignalAndWait();
                return output.SavePng(bitmap, timestamp);
            }, TaskCreationOptions.LongRunning))
            .ToArray();
        var paths = Task.WhenAll(saves).GetAwaiter().GetResult();
        var folder = Path.Combine(workspace.Root, "artifacts", "screenshots", "2026-08-04");

        Equal(captureCount, paths.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Equal(captureCount, Directory.GetFiles(folder, "*.png").Length);
        foreach (var path in paths)
        {
            using var reloaded = new Bitmap(path);
            Equal(2, reloaded.Width);
            Equal(2, reloaded.Height);
        }
    }

    private static void UsesWindowsGraphicsCaptureBeforeGdi()
    {
        var preferredCalls = 0;
        var fallbackCalls = 0;
        Func<CancellationToken, Task<CapturedFrame>> preferred = _ =>
        {
            preferredCalls++;
            return Task.FromResult(new CapturedFrame(CreateBitmap(2, 2, Color.CornflowerBlue), new Rectangle(10, 20, 2, 2)));
        };
        Func<CapturedFrame> fallback = () =>
        {
            fallbackCalls++;
            return new CapturedFrame(CreateBitmap(2, 2, Color.Red), new Rectangle(10, 20, 2, 2));
        };

        using var result = InvokeGameCompatibleCapture(preferred, fallback);

        Equal(1, preferredCalls);
        Equal(0, fallbackCalls);
        Equal(Color.CornflowerBlue.ToArgb(), result.Bitmap.GetPixel(0, 0).ToArgb());
    }

    private static void FallsBackToGdiWhenWindowsGraphicsCaptureIsUnavailable()
    {
        var fallbackCalls = 0;
        Func<CancellationToken, Task<CapturedFrame>> preferred = _ =>
            Task.FromException<CapturedFrame>(new InvalidOperationException("WGC unavailable"));
        Func<CapturedFrame> fallback = () =>
        {
            fallbackCalls++;
            return new CapturedFrame(CreateBitmap(2, 2, Color.Gold), new Rectangle(30, 40, 2, 2));
        };

        using var result = InvokeGameCompatibleCapture(preferred, fallback);

        Equal(1, fallbackCalls);
        Equal(new Rectangle(30, 40, 2, 2), result.ScreenBounds);
        Equal(Color.Gold.ToArgb(), result.Bitmap.GetPixel(0, 0).ToArgb());
    }

    private static void MarksWindowsGraphicsCaptureFramesAsSystemFeedback()
    {
        var captureType = typeof(ScreenCaptureService).Assembly.GetType(
            "ClipRay.App.Capture.WindowsGraphicsScreenCapture")
            ?? throw new InvalidOperationException("Windows Graphics Capture implementation is missing.");
        var convert = captureType.GetMethod(
            "ToCapturedFrame",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            ?? throw new InvalidOperationException("Windows Graphics Capture frame conversion is missing.");
        var sample = new VideoRecordingSample(new byte[2 * 2 * 4], 2, 2, 0, 0);
        using var frame = (CapturedFrame)(convert.Invoke(null, [sample, Point.Empty])
            ?? throw new InvalidOperationException("Windows Graphics Capture returned no frame."));
        var property = typeof(CapturedFrame).GetProperty("HasSystemCaptureFeedback")
            ?? throw new InvalidOperationException("Captured frames do not expose system feedback ownership.");

        Equal(true, (bool)property.GetValue(frame)!);
    }

    private static void AvoidsDuplicateWindowsGraphicsCaptureFeedback()
    {
        var policy = typeof(CaptureFeedbackPattern).Assembly.GetType(
            "ClipRay.App.Feedback.CaptureFeedbackPolicy")
            ?? throw new InvalidOperationException("Capture feedback ownership policy is missing.");
        var shouldShow = policy.GetMethod("ShouldShowAppSuccess")
            ?? throw new InvalidOperationException("Capture feedback ownership policy entry point is missing.");

        Equal(false, (bool)shouldShow.Invoke(null, [true])!);
        Equal(true, (bool)shouldShow.Invoke(null, [false])!);
    }

    private static CapturedFrame InvokeGameCompatibleCapture(
        Func<CancellationToken, Task<CapturedFrame>> preferred,
        Func<CapturedFrame> fallback)
    {
        var policyType = typeof(ScreenCaptureService).Assembly.GetType(
            "ClipRay.App.Capture.GameCompatibleScreenCapturePolicy");
        if (policyType is null) throw new InvalidOperationException("Game-compatible capture policy is missing.");
        var method = policyType.GetMethod("CaptureAsync", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        if (method is null) throw new InvalidOperationException("Game-compatible capture policy entry point is missing.");
        var task = method.Invoke(null, [preferred, fallback, CancellationToken.None]) as Task<CapturedFrame>;
        return task?.GetAwaiter().GetResult()
            ?? throw new InvalidOperationException("Game-compatible capture policy returned no frame.");
    }

    private static void DefinesSuccessFeedbackPulse()
    {
        var pattern = CaptureFeedbackPattern.Success;

        Equal(MediaColor.FromRgb(0x1A, 0xA8, 0xFF), pattern.Color);
        Equal(1, pattern.Pulses.Count);
        Equal(new FeedbackPulse(true, TimeSpan.FromMilliseconds(150)), pattern.Pulses[0]);
    }

    private static void DefinesFailureFeedbackPulses()
    {
        var pattern = CaptureFeedbackPattern.Failure;

        Equal(MediaColor.FromRgb(0xFF, 0x4D, 0x4F), pattern.Color);
        Equal(3, pattern.Pulses.Count);
        Equal(new FeedbackPulse(true, TimeSpan.FromMilliseconds(120)), pattern.Pulses[0]);
        Equal(new FeedbackPulse(false, TimeSpan.FromMilliseconds(90)), pattern.Pulses[1]);
        Equal(new FeedbackPulse(true, TimeSpan.FromMilliseconds(120)), pattern.Pulses[2]);
    }

    private static void ConfiguresEdgeFeedbackAsNonInteractive()
    {
        var window = new ScreenEdgeFeedbackWindow(new Rectangle(100, 200, 640, 480), CaptureFeedbackPattern.Success.Color);

        Equal(false, window.ShowActivated);
        Equal(false, window.ShowInTaskbar);
        Equal(true, window.Topmost);
        Equal(false, window.IsHitTestVisible);
        window.Close();
    }

    private static void ResolvesRepeatedInteractiveCaptureAsCancellation()
    {
        Equal(InteractiveCaptureAction.Open, InteractiveCapturePolicy.Resolve(overlayOpen: false));
        Equal(InteractiveCaptureAction.CancelExisting, InteractiveCapturePolicy.Resolve(overlayOpen: true));
    }

    private static void StartsRecordingWhenRecordingModeSelectionIsConfirmed()
    {
        EnsureWpfResources();
        var bitmap = CreateBitmap(32, 32, Color.Black);
        var overlay = new SelectionOverlayWindow(
            new CapturedFrame(bitmap, new Rectangle(10, 20, 32, 32)),
            new PixelRect(2, 3, 20, 18),
            launchMode: OverlayLaunchMode.Recording);
        RegionRecordingRequest? recording = null;
        var captureCount = 0;
        overlay.RecordingRequested += (_, request) => recording = request;
        overlay.CaptureConfirmed += (_, captured) =>
        {
            captureCount++;
            captured.Dispose();
        };

        try
        {
            InvokePrivate(overlay, "ConfirmSelection");
            Equal(0, captureCount);
            Equal(true, recording is not null);
            Equal(new Rectangle(12, 23, 20, 18), recording!.PhysicalPixelRegion);
        }
        finally
        {
            overlay.Close();
        }
    }

    private static void KeepsRecordingLaunchEnterAndEscapeDeterministic()
    {
        var configuredConfirm = GlobalHotkey.Parse("Ctrl+Enter");
        Equal(true, OverlayLaunchInputPolicy.IsConfirm(
            OverlayLaunchMode.Recording,
            configuredConfirm,
            null,
            System.Windows.Input.Key.Enter,
            System.Windows.Input.ModifierKeys.None));
        Equal(false, OverlayLaunchInputPolicy.IsConfirm(
            OverlayLaunchMode.Capture,
            configuredConfirm,
            null,
            System.Windows.Input.Key.Enter,
            System.Windows.Input.ModifierKeys.None));
        Equal(true, OverlayLaunchInputPolicy.IsImmediateCancel(
            OverlayLaunchMode.Recording,
            System.Windows.Input.Key.Escape,
            System.Windows.Input.ModifierKeys.None));
        Equal(false, OverlayLaunchInputPolicy.IsImmediateCancel(
            OverlayLaunchMode.Capture,
            System.Windows.Input.Key.Escape,
            System.Windows.Input.ModifierKeys.None));
    }

    private static void BlocksASecondRecordingSelectionWhileRecordingIsActive()
    {
        Equal(true, RecordingStartPolicy.CanOpenSelection(recordingActive: false));
        Equal(false, RecordingStartPolicy.CanOpenSelection(recordingActive: true));
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var app = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "App.xaml.cs"));
        Equal(true, app.Contains("RecordingStartPolicy.CanOpenSelection(_recordingSession is not null)", StringComparison.Ordinal));
        Equal(true, app.Contains("overlay.RestoreAfterRecordingFailure(\"录屏已在进行中\")", StringComparison.Ordinal));
    }

    private static void LoadsPackagedApplicationIcon()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var iconPath = Path.Combine(workspace, "src", "ClipRay.App", "Assets", "ClipRay.ico");

        using var icon = ApplicationIconLoader.Load(iconPath);

        Equal(true, icon.Width > 0);
        Equal(true, icon.Height > 0);
    }

    private static void FallsBackOnlyWhenApplicationIconIsMissing()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), $"ClipRay-missing-{Guid.NewGuid():N}.ico");

        using var icon = ApplicationIconLoader.LoadOrDefault(missingPath);

        Equal(true, icon.Width > 0);
        Equal(true, icon.Height > 0);
    }

    private static void RejectsCorruptApplicationIcon()
    {
        var corruptPath = Path.Combine(Path.GetTempPath(), $"ClipRay-corrupt-{Guid.NewGuid():N}.ico");
        File.WriteAllText(corruptPath, "not an icon");
        try
        {
            try
            {
                using var _ = ApplicationIconLoader.LoadOrDefault(corruptPath);
                throw new InvalidOperationException("Expected corrupt icon loading to fail.");
            }
            catch (ArgumentException)
            {
            }
        }
        finally
        {
            File.Delete(corruptPath);
        }
    }

    private static void DefinesTransparentApplicationIconAndToolbarGeometry()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var assets = Path.Combine(workspace, "src", "ClipRay.App", "Assets");
        var icon = Path.Combine(assets, "ClipRay-icon-transparent.png");
        var source = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "Ui", "ToolbarIconFactory.cs"));

        Equal(true, File.Exists(icon));
        using var image = new Bitmap(icon);
        Equal(true, image.GetPixel(0, 0).A < 8);
        Equal(true, image.GetPixel(image.Width / 2, image.Height / 2).A > 240);
        Equal(true, source.Contains("enum ToolbarIconKind", StringComparison.Ordinal));
        Equal(true, source.Contains("Pause", StringComparison.Ordinal));
        Equal(true, source.Contains("FitWidth", StringComparison.Ordinal));
    }

    private static void DefinesFinalV1SettingDefaults()
    {
        using var workspace = TemporaryWorkspace.Create();
        var settings = AppSettings.CreateDefault(workspace.Root);

        Equal("Ctrl+Win+A", settings.RegionCaptureHotkey.ToString());
        Equal("Insert", settings.CurrentScreenHotkey.ToString());
        Equal("Ctrl+Insert", settings.TaskbarFreeCurrentScreenHotkey.ToString());
        Equal("Alt+OemQuestion", settings.VirtualDesktopHotkey.ToString());
        Equal("Ctrl+Win+Z", settings.StartRecordingHotkey.ToString());
        Equal("F8", settings.StopRecordingHotkey.ToString());
        Equal(Path.Combine(workspace.Root, "Outputs", "Screenshots"), settings.ScreenshotDirectory);
        Equal(Path.Combine(workspace.Root, "Outputs", "LongCaptures"), settings.LongCaptureDirectory);
        Equal(Path.Combine(workspace.Root, "Outputs", "Recordings"), settings.RecordingDirectory);
        Equal(true, settings.CopyToClipboard);
        Equal(true, settings.ShowBorderFeedback);
        Equal(false, settings.StartWithWindows);
        Equal(false, settings.CleanupScheduleEnabled);
        Equal(30, settings.CleanupRetentionDays);
        Equal(CleanupScheduleFrequency.Daily, settings.CleanupScheduleFrequency);
        Equal(new TimeOnly(21, 0), settings.CleanupDailyTime);
        Equal(true, settings.RegionCaptureSecondaryHotkey is null);
        Equal(true, settings.CurrentScreenSecondaryHotkey is null);
        Equal(true, settings.TaskbarFreeCurrentScreenSecondaryHotkey is null);
        Equal(true, settings.VirtualDesktopSecondaryHotkey is null);
        Equal(true, settings.StartRecordingSecondaryHotkey is null);
        Equal(true, settings.StopRecordingSecondaryHotkey is null);
        Equal(true, settings.ConfirmCaptureSecondaryHotkey is null);
        Equal(true, settings.CancelCaptureSecondaryHotkey is null);
        Equal(true, settings.StopLongCaptureSecondaryHotkey is null);
        Equal("Machine", settings.Provider);
        Equal("deepseek-flash", settings.DeepSeekModel);
        Equal("简体中文", settings.TranslationTargetLanguage);
    }

    private static void DefinesSeparateFullScreenHotkeys()
    {
        using var workspace = TemporaryWorkspace.Create();
        var settings = AppSettings.CreateDefault(workspace.Root);
        var sourceRoot = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var appSource = File.ReadAllText(Path.Combine(sourceRoot, "src", "ClipRay.App", "App.xaml.cs"));
        var captureSource = File.ReadAllText(Path.Combine(sourceRoot, "src", "ClipRay.App", "Capture", "ScreenCaptureService.cs"));
        var settingsXaml = File.ReadAllText(Path.Combine(sourceRoot, "src", "ClipRay.App", "Settings", "SettingsWindow.xaml"));

        Equal("Insert", settings.CurrentScreenHotkey.ToString());
        Equal("Ctrl+Insert", settings.TaskbarFreeCurrentScreenHotkey.ToString());
        Equal(true, appSource.Contains("TaskbarFreeCurrentScreenHotkey", StringComparison.Ordinal));
        Equal(true, appSource.Contains("RunCurrentMonitorCaptureIncludingTaskbarAsync", StringComparison.Ordinal));
        Equal(true, captureSource.Contains("CaptureCurrentMonitorIncludingTaskbarGameCompatibleAsync", StringComparison.Ordinal));
        Equal(true, settingsXaml.Contains("全屏截图（不含任务栏）", StringComparison.Ordinal));
    }

    private static void KeepsApplicationAndInstallerVersioningConsistent()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var project = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "ClipRay.App.csproj"));
        var manifest = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "app.manifest"));
        var xaml = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "Settings", "SettingsWindow.xaml"));
        var installer = File.ReadAllText(Path.Combine(workspace, "installer", "ClipRay.iss"));
        var buildScript = File.ReadAllText(Path.Combine(workspace, "scripts", "Build-Installer.ps1"));
        var projectFile = project;

        Equal(true, project.Contains("<Version>1.0.6</Version>", StringComparison.Ordinal));
        Equal(true, project.Contains("<AssemblyVersion>1.0.6.0</AssemblyVersion>", StringComparison.Ordinal));
        Equal(true, manifest.Contains("version=\"1.0.6.0\"", StringComparison.Ordinal));
        Equal(true, xaml.Contains("VersionText", StringComparison.Ordinal));
        Equal(true, installer.Contains("#define AppVersion \"1.0.6\"", StringComparison.Ordinal));
        Equal(true, buildScript.Contains("ClipRay-$appVersion-source.zip", StringComparison.Ordinal));
        Equal(true, buildScript.Contains("[ValidateSet('Lite', 'Full')]", StringComparison.Ordinal));
        Equal(true, buildScript.Contains("$includeLocalModel = $Flavor -eq 'Full'", StringComparison.Ordinal));
        Equal(true, buildScript.Contains("-p:ClipRayIncludeCudaRuntime=", StringComparison.Ordinal));
        Equal(true, installer.Contains("ClipRay-{#SetupFlavor}-Setup-", StringComparison.Ordinal));
        Equal(true, projectFile.Contains("'$(ClipRayIncludeCudaRuntime)' == 'true'", StringComparison.Ordinal));
        Equal(true, projectFile.Contains(@"artifacts\machine-translation\**\*", StringComparison.Ordinal));
        Equal(true, buildScript.Contains("Export-PublicSource.ps1", StringComparison.Ordinal));
        Equal(true, buildScript.Contains("-Revision $sourceCommit", StringComparison.Ordinal));
        Equal(false, buildScript.Contains("robocopy.exe", StringComparison.OrdinalIgnoreCase));
    }

    private static void CreatesOrganizedOutputDirectoriesBeforeTheFirstCapture()
    {
        using var workspace = TemporaryWorkspace.Create();
        var settings = AppSettings.CreateDefault(workspace.Root);

        OutputDirectoryLayout.Ensure(settings);

        Equal(true, Directory.Exists(settings.ScreenshotDirectory));
        Equal(true, Directory.Exists(settings.LongCaptureDirectory));
        Equal(true, Directory.Exists(settings.RecordingDirectory));
        var appSource = File.ReadAllText(Path.Combine(
            WorkspaceLocator.Find(AppContext.BaseDirectory),
            "src",
            "ClipRay.App",
            "App.xaml.cs"));
        Equal(true, appSource.Contains("OutputDirectoryLayout.Ensure(loadResult.Settings);", StringComparison.Ordinal));
    }

    private static void SerializesAPrivacySafeTranslationRunLog()
    {
        using var workspace = TemporaryWorkspace.Create();
        var entry = new TranslationRunLog(
            TranslationRunLog.CurrentSchemaVersion,
            "run-1",
            new DateTimeOffset(2026, 8, 30, 1, 2, 3, TimeSpan.Zero),
            "release",
            "1.0.2",
            "abc123",
            "deepseek",
            "success",
            "none",
            100,
            80,
            false,
            2,
            1,
            1,
            0,
            0,
            0,
            0,
            new TranslationStageTiming(1, 2, 3, 4, 10),
            "none");

        var writer = new JsonlTranslationRunLogWriter(workspace.Root);
        writer.Write(entry);

        var path = Directory.GetFiles(workspace.Root, "translation-*.jsonl").Single();
        var json = File.ReadAllText(path);
        Equal("clipray.translation-run.v2", TranslationRunLog.CurrentSchemaVersion);
        Equal(true, json.Contains("\"route\":\"deepseek\"", StringComparison.Ordinal));
        Equal(true, json.Contains("\"preservedCount\":", StringComparison.Ordinal));
        Equal(true, json.Contains("\"providerFailedCount\":", StringComparison.Ordinal));
        Equal(true, json.Contains("\"layoutRejectedCount\":", StringComparison.Ordinal));
        Equal(false, Regex.IsMatch(json, "sourceText|translatedText|apiKey|cookie|https?://|screenshot|title|private.*path|\\.png|\\.jpg", RegexOptions.IgnoreCase));
    }

    private static void ClassifiesTranslationRunOutcomes()
    {
        static VisualTextRegion Region(string id, string text, double left)
        {
            var bounds = new PixelRect(left, 0, 40, 12);
            var block = new OcrTextBlock(text, bounds, [new OcrWord(text, bounds)]);
            return new VisualTextRegion(
                id, text, [block], bounds, VisualTextRole.Body, VisualStructureConfidence.High);
        }

        var regions = new[]
        {
            Region("translated", "Hello", 0),
            Region("preserved", "@ClipRay", 50),
            Region("failed", "World", 100),
            Region("rejected", "Settings", 150),
        };
        var layout = new InPlaceLayoutItem(
            regions[0].Lines[0],
            "你好",
            regions[0].Bounds,
            InPlaceBackgroundMode.FlatSampled,
            Color.White,
            Color.Black,
            SourceRegion: regions[0]);
        var outcome = new ApplicationTranslationOutcome(
            false,
            TranslationPipelineStage.Translation,
            "translation-failed",
            regions.SelectMany(static region => region.Lines).ToArray(),
            regions,
            [
                new TranslationResult(true, "你好", "completed"),
                new TranslationResult(true, "@ClipRay", "machine-preserved"),
                new TranslationResult(false, string.Empty, "provider-failed"),
                new TranslationResult(true, "设置", "completed"),
            ],
            [layout],
            [new InPlaceLayoutRejection(4, "unsafe-background")]);

        var classification = TranslationRunClassifier.Classify(outcome);

        Equal(1, classification.TranslatedCount);
        Equal(1, classification.PreservedCount);
        Equal(1, classification.ProviderFailedCount);
        Equal(1, classification.LayoutRejectedCount);
    }

    private static void TreatsTranslationRunDiagnosticsAsBestEffort()
    {
        var writer = new JsonlTranslationRunLogWriter(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        writer.Write(null!);
    }

    private static void KeepsTranslationDiagnosticsBestEffortWhenResultCountsMismatch()
    {
        var bounds = new PixelRect(0, 0, 40, 12);
        var block = new OcrTextBlock("Hello", bounds, [new OcrWord("Hello", bounds)]);
        var region = new VisualTextRegion(
            "region", "Hello", [block], bounds, VisualTextRole.Body, VisualStructureConfidence.High);
        var outcome = new ApplicationTranslationOutcome(
            false,
            TranslationPipelineStage.Translation,
            "translation-count-mismatch",
            [block],
            [region],
            [],
            [],
            []);

        var classified = TranslationRunClassifier.TryClassify(outcome, out var classification);

        Equal(false, classified);
        Equal(new TranslationRunClassification(0, 0, 0, 0), classification);
    }

    private static void UsesTheReleaseDiagnosticsRootForTranslationRunLogs()
    {
        using var workspace = TemporaryWorkspace.Create(createSolutionMarker: false);
        var installRoot = Path.Combine(workspace.Root, "installed", "ClipRay");
        var paths = ApplicationPaths.Create(installRoot, Path.Combine(workspace.Root, "local-app-data"));

        Equal("release", paths.ProfileId);
        Equal(Path.Combine(workspace.Root, "local-app-data", "ClipRay", "diagnostics"), paths.DiagnosticsRoot);
    }

    private static void UsesTheDevelopmentDiagnosticsRootForTranslationRunLogs()
    {
        using var workspace = TemporaryWorkspace.Create(createSolutionMarker: true);
        var applicationRoot = Path.Combine(workspace.Root, "artifacts", "app");
        var localDataRoot = Path.Combine(workspace.Root, "local-app-data");
        var paths = ApplicationPaths.Create(applicationRoot, localDataRoot);

        Equal("development", paths.ProfileId);
        Equal(Path.Combine(localDataRoot, "ClipRay-Dev", "diagnostics"), paths.DiagnosticsRoot);
    }

    private static void DefinesInstalledApplicationPathsWithoutASourceWorkspace()
    {
        using var workspace = TemporaryWorkspace.Create(createSolutionMarker: false);
        var installRoot = Path.Combine(workspace.Root, "installed", "ClipRay");
        var applicationRoot = Path.Combine(installRoot, "app");
        var localDataRoot = Path.Combine(workspace.Root, "local-app-data");

        var paths = ApplicationPaths.Create(applicationRoot, localDataRoot);

        Equal(Path.GetFullPath(installRoot), paths.InstallRoot);
        Equal(Path.Combine(localDataRoot, "ClipRay"), paths.DataRoot);
        Equal(Path.Combine(localDataRoot, "ClipRay", "tmp", "long-capture"), paths.LongCaptureTempRoot);
        Equal(Path.Combine(applicationRoot, "models", "local-lmt"), paths.LocalModelRoot);
    }

    private static void SeparatesDevelopmentAndInstalledApplicationProfiles()
    {
        using var workspace = TemporaryWorkspace.Create(createSolutionMarker: false);
        var localDataRoot = Path.Combine(workspace.Root, "local-app-data");
        var developmentRoot = Path.Combine(WorkspaceLocator.Find(AppContext.BaseDirectory), "artifacts", "app");
        var installedRoot = Path.Combine(workspace.Root, "installed", "ClipRay");

        var development = ApplicationPaths.Create(developmentRoot, localDataRoot);
        var installed = ApplicationPaths.Create(installedRoot, localDataRoot);

        Equal("development", development.ProfileId);
        Equal(Path.Combine(localDataRoot, "ClipRay-Dev"), development.DataRoot);
        Equal("release", installed.ProfileId);
        Equal(Path.Combine(localDataRoot, "ClipRay"), installed.DataRoot);
        Equal(false, string.Equals(
            AppActivationPolicy.CurrentUserScope(development.ProfileId),
            AppActivationPolicy.CurrentUserScope(installed.ProfileId),
            StringComparison.Ordinal));
    }

    private static void AllowsOneTrayOwnerAndForwardsSecondLaunch()
    {
        var scope = $"ClipRay.Tests.{Guid.NewGuid():N}";
        using var signal = new ManualResetEventSlim();
        AppActivationCommand? received = null;
        using var primary = new SingleInstanceCoordinator(scope);
        primary.ActivationRequested += command =>
        {
            received = command;
            signal.Set();
        };

        Equal(SingleInstanceStartResult.Primary, primary.Start(AppActivationCommand.OpenSettings));
        var secondaryResult = Task.Run(() =>
        {
            using var secondary = new SingleInstanceCoordinator(scope);
            return secondary.Start(AppActivationCommand.OpenSettings);
        }).GetAwaiter().GetResult();
        Equal(SingleInstanceStartResult.Forwarded, secondaryResult);
        Equal(true, signal.Wait(TimeSpan.FromSeconds(2)));
        Equal(true, received.HasValue);
        Equal(AppActivationCommand.OpenSettings, received!.Value);
    }

    private static void RoutesRepeatLaunchesAndBypassesMaintenance()
    {
        Equal(AppActivationCommand.OpenSettings, AppActivationPolicy.ForwardedCommand([]));
        Equal(AppActivationCommand.OpenSettings, AppActivationPolicy.ForwardedCommand(["--open-settings"]));
        Equal(AppActivationCommand.StartInteractiveCapture, AppActivationPolicy.ForwardedCommand(["--interactive"]));
        Equal(AppActivationCommand.CaptureCurrentScreen, AppActivationPolicy.ForwardedCommand(["--quick-current"]));
        Equal(true, AppActivationPolicy.ShouldBypassSingleInstance(["--cleanup-scheduled"], isDebugBuild: false));
        Equal(true, AppActivationPolicy.ShouldBypassSingleInstance(["--uninstall-cleanup"], isDebugBuild: false));
        Equal(true, AppActivationPolicy.ShouldBypassSingleInstance(["--settings-preview"], isDebugBuild: true));
        Equal(false, AppActivationPolicy.ShouldBypassSingleInstance(["--open-settings"], isDebugBuild: false));
        var currentUserScope = AppActivationPolicy.CurrentUserScope();
        Equal(currentUserScope, AppActivationPolicy.CurrentUserScope());
        Equal(true, currentUserScope.StartsWith("ClipRay.", StringComparison.Ordinal));
    }

    private static void AcquiresTrayOwnerBeforeResidentResources()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var source = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "App.xaml.cs"));
        var ownership = source.IndexOf("TryOwnSingleInstance(e.Args, startupPaths.ProfileId)", StringComparison.Ordinal);
        var paths = source.IndexOf("_paths = startupPaths;", StringComparison.Ordinal);
        var tray = source.IndexOf("CreateTrayIcon();", StringComparison.Ordinal);

        Equal(true, ownership >= 0);
        Equal(true, ownership < paths);
        Equal(true, ownership < tray);
        Equal(true, source.Contains("HandleActivation(AppActivationCommand command)", StringComparison.Ordinal));
        Equal(true, source.Contains("_singleInstance?.Dispose();", StringComparison.Ordinal));
    }

    private static void DefinesScaledSettingsSignature()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var xaml = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "Settings", "SettingsWindow.xaml"));

        Equal(true, xaml.Contains("Text=\"by Fallen-Flower ❀O.o？\"", StringComparison.Ordinal));
        Equal(true, xaml.Contains("x:Name=\"SignatureBadge\"", StringComparison.Ordinal));
        Equal(true, xaml.Contains("Padding=\"22.4,11.2\"", StringComparison.Ordinal));
        Equal(true, xaml.Contains("CornerRadius=\"20.8\"", StringComparison.Ordinal));
        Equal(true, xaml.Contains("BorderThickness=\"1.6\"", StringComparison.Ordinal));
        Equal(true, xaml.Contains("FontSize=\"19.2\"", StringComparison.Ordinal));
        Equal(true, xaml.Contains("Width=\"57\" Height=\"57\" Margin=\"18,6,0,-6\"", StringComparison.Ordinal));
        var source = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "Settings", "SettingsWindow.xaml.cs"));
        Equal(true, source.Contains("TimeSpan.FromSeconds(10)", StringComparison.Ordinal));
        Equal(false, source.Contains("TimeSpan.FromSeconds(18)", StringComparison.Ordinal));
    }

    private static void PackagesAndOpensOfflineClipRayTutorial()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var tutorial = Path.Combine(workspace, "docs", "ClipRay-使用教程.html");
        var project = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "ClipRay.App.csproj"));
        var xaml = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "Settings", "SettingsWindow.xaml"));
        var source = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "Settings", "SettingsWindow.xaml.cs"));

        Equal(true, File.Exists(tutorial));
        Equal(true, project.Contains("ClipRay-使用教程.html", StringComparison.Ordinal));
        Equal(true, project.Contains("Link=\"docs\\ClipRay-使用教程.html\"", StringComparison.Ordinal));
        Equal(true, project.Contains("CopyToPublishDirectory=\"PreserveNewest\"", StringComparison.Ordinal));
        Equal(true, xaml.Contains("x:Name=\"TutorialButton\"", StringComparison.Ordinal));
        Equal(true, xaml.Contains("Content=\"查看使用教程 ↗\"", StringComparison.Ordinal));
        Equal(true, xaml.Contains("Margin=\"0,0,28,0\" Padding=\"14,8\" FontSize=\"13\"", StringComparison.Ordinal));
        Equal(true, xaml.Contains("Background=\"#4FA9DC\" BorderBrush=\"#A8DDF5\"", StringComparison.Ordinal));
        Equal(true, source.Contains("OpenTutorial_Click", StringComparison.Ordinal));
        Equal(true, source.Contains("AppContext.BaseDirectory, \"docs\", \"ClipRay-使用教程.html\"", StringComparison.Ordinal));
        Equal(true, source.Contains("UseShellExecute = true", StringComparison.Ordinal));
        var html = File.ReadAllText(tutorial);
        Equal(true, html.Contains("ClipRay 1.0.6", StringComparison.Ordinal));
        Equal(true, html.Contains("轻量版", StringComparison.Ordinal));
        Equal(true, html.Contains("完整版", StringComparison.Ordinal));
        Equal(true, html.Contains("区域翻译", StringComparison.Ordinal));
        Equal(true, html.Contains("实时翻译", StringComparison.Ordinal));
        Equal(true, html.Contains("Windows Credential Manager", StringComparison.Ordinal));
        Equal(true, html.Contains("Ctrl+V", StringComparison.Ordinal));
        Equal(true, html.Contains("截图、长截图和录屏", StringComparison.Ordinal));
        Equal(false, html.Contains("实时翻译固定使用本地机翻", StringComparison.Ordinal));
    }

    private static void OffersInstalledUninstallAndDocumentsBothEntryPoints()
    {
        using var temporary = TemporaryWorkspace.Create(createSolutionMarker: false);
        var installedApp = Path.Combine(temporary.Root, "ClipRay", "app");
        var uninstaller = Path.Combine(temporary.Root, "ClipRay", "unins000.exe");
        Directory.CreateDirectory(installedApp);
        File.WriteAllText(uninstaller, string.Empty);

        var installed = InstalledUninstallLauncher.Resolve(installedApp, temporary.Root);
        Equal(true, installed.IsAvailable);
        Equal(uninstaller, installed.UninstallerPath!);

        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var development = InstalledUninstallLauncher.Resolve(
            Path.Combine(workspace, "artifacts", "app-next"),
            temporary.Root);
        Equal(false, development.IsAvailable);

        var xaml = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "Settings", "SettingsWindow.xaml"));
        var source = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "Settings", "SettingsWindow.xaml.cs"));
        var tutorial = File.ReadAllText(Path.Combine(workspace, "docs", "ClipRay-使用教程.html"));
        Equal(true, xaml.Contains("x:Name=\"UninstallButton\"", StringComparison.Ordinal));
        Equal(true, xaml.Contains("Content=\"卸载 ClipRay\"", StringComparison.Ordinal));
        Equal(true, source.Contains("Uninstall_Click", StringComparison.Ordinal));
        Equal(true, source.Contains("InstalledUninstallLauncher.Launch", StringComparison.Ordinal));
        Equal(true, tutorial.Contains("如何卸载 ClipRay", StringComparison.Ordinal));
        Equal(true, tutorial.Contains("Windows 设置", StringComparison.Ordinal));
        Equal(true, tutorial.Contains("已安装的应用", StringComparison.Ordinal));
        Equal(true, tutorial.Contains("卸载 ClipRay", StringComparison.Ordinal));
    }

    private static void RespectsClientAreaAnimationForSignatureMotion()
    {
        Equal(true, SignatureMotionPolicy.ShouldAnimate(true));
        Equal(false, SignatureMotionPolicy.ShouldAnimate(false));
    }

    private static void DefinesRecordingAndCleanupSettingsControls()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var xaml = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "Settings", "SettingsWindow.xaml"));
        var source = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "Settings", "SettingsWindow.xaml.cs"));

        foreach (var name in new[]
                 {
                     "ScreenshotDirectoryBox",
                     "RecordingDirectoryBox",
                     "CleanupScheduleBox",
                     "CleanupRetentionDaysBox",
                     "CleanupDailyTimeBox",
                 })
        {
            Equal(true, xaml.Contains($"x:Name=\"{name}\"", StringComparison.Ordinal));
        }
        Equal(true, xaml.Contains("删除不进入回收站", StringComparison.Ordinal));
        Equal(true, xaml.Contains("settings:SettingsText.Chinese=\"截图\" settings:SettingsText.English=\"Screenshots\"", StringComparison.Ordinal));
        Equal(false, xaml.Contains("settings:SettingsText.Chinese=\"截图和长截图\"", StringComparison.Ordinal));
        Equal(true, source.Contains("RecordingDirectory = Path.GetFullPath(RecordingDirectoryBox.Text)", StringComparison.Ordinal));
        Equal(true, source.Contains("CleanupScheduleEnabled = CleanupScheduleBox.IsChecked == true", StringComparison.Ordinal));
        Equal(true, source.Contains("CleanupRetentionDays = SettingsInputParser.ParseRetentionDays", StringComparison.Ordinal));
        Equal(true, source.Contains("CleanupDailyTime = SettingsInputParser.ParseDailyTime", StringComparison.Ordinal));
    }

    private static void ConfiguresSelectedInstallerCleanupThroughMaintenancePath()
    {
        using var workspace = TemporaryWorkspace.Create();
        var prior = AppSettings.CreateDefault(workspace.Root) with
        {
            CleanupScheduleEnabled = false,
            CleanupRetentionDays = 7,
            CleanupScheduleFrequency = CleanupScheduleFrequency.Weekly,
            CleanupWeeklyDay = DayOfWeek.Thursday,
            CleanupDailyTime = new TimeOnly(3, 0),
        };

        var configured = InstallerCleanupConfiguration.BuildSettings(prior);
        Equal(true, configured.CleanupScheduleEnabled);
        Equal(30, configured.CleanupRetentionDays);
        Equal(CleanupScheduleFrequency.Daily, configured.CleanupScheduleFrequency);
        Equal(new TimeOnly(21, 0), configured.CleanupDailyTime);
        Equal(true, AppActivationPolicy.ShouldBypassSingleInstance(
            ["--configure-install-cleanup"], isDebugBuild: false));

        var repository = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var installer = File.ReadAllText(Path.Combine(repository, "installer", "ClipRay.iss"));
        var app = File.ReadAllText(Path.Combine(repository, "src", "ClipRay.App", "App.xaml.cs"));
        Equal(true, installer.Contains("Name: \"cleanup\"", StringComparison.Ordinal));
        Equal(true, installer.Contains("启用 Windows 自动清理（30 天、每天 21:00）", StringComparison.Ordinal));
        Equal(true, installer.Contains("WizardIsTaskSelected('cleanup')", StringComparison.Ordinal));
        Equal(true, installer.Contains("--configure-install-cleanup", StringComparison.Ordinal));
        Equal(true, app.Contains("InstallerCleanupConfiguration.Execute", StringComparison.Ordinal));
    }

    private static void UsesAFixedCredentialSlotInSettings()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var xaml = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "Settings", "SettingsWindow.xaml"));
        var source = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "Settings", "SettingsWindow.xaml.cs"));

        Equal(true, xaml.Contains("x:Name=\"CleanupRetentionDaysBox\" Width=\"62\"", StringComparison.Ordinal));
        Equal(true, xaml.Contains("x:Name=\"CleanupFrequencyBox\"", StringComparison.Ordinal));
        Equal(true, xaml.Contains("Tag=\"每天\" settings:SettingsText.Chinese=\"每天\" settings:SettingsText.English=\"Daily\"", StringComparison.Ordinal));
        Equal(true, xaml.Contains("Tag=\"每周\" settings:SettingsText.Chinese=\"每周\" settings:SettingsText.English=\"Weekly\"", StringComparison.Ordinal));
        Equal(true, xaml.Contains("x:Name=\"CleanupWeeklyDayBox\"", StringComparison.Ordinal));
        Equal(true, source.Contains("CleanupScheduleFrequency = ParseCleanupFrequency", StringComparison.Ordinal));
        Equal(false, xaml.Contains("CredentialNameBox", StringComparison.Ordinal));
        Equal(false, xaml.Contains("CredentialGrid", StringComparison.Ordinal));
        Equal(true, source.Contains("CredentialVault.FixedDeepSeekSlot", StringComparison.Ordinal));
    }

    private static void UsesOneFixedLocalDeepSeekCredentialSlot()
    {
        Equal("ClipRay.DeepSeek", CredentialVault.FixedDeepSeekSlot);
    }

    private static void MigratesSelectedLegacyCredentialIntoFixedSlot()
    {
        var backend = new MemoryCredentialBackend();
        var vault = new CredentialVault(backend, () => DateTimeOffset.UtcNow);
        const string secret = "sk-10430abcdefghijklmnopfb25";
        vault.Save("legacy-alias", "DeepSeek", secret);

        var migrated = vault.MigrateSelectedDeepSeekCredential("legacy-alias");

        Equal(true, migrated);
        Equal(secret, vault.GetForUse(CredentialVault.FixedDeepSeekSlot)!);
        Equal(true, vault.GetForUse("legacy-alias") is null);
        Equal(true, vault.List().Select(static item => item.Name).SequenceEqual([CredentialVault.FixedDeepSeekSlot]));
    }

    private static void MigratesCredentialBeforeApplicationStartupRouting()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var source = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "App.xaml.cs"));
        var migrate = source.IndexOf("MigrateSelectedDeepSeekCredential", StringComparison.Ordinal);
        var apply = source.IndexOf("ApplySettings(loadResult.Settings", StringComparison.Ordinal);
        Equal(true, migrate >= 0 && apply > migrate);
    }

    private static void DefinesPrimaryAndOptionalSecondaryHotkeyControls()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var xaml = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "Settings", "SettingsWindow.xaml"));
        var source = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "Settings", "SettingsWindow.xaml.cs"));
        foreach (var name in new[]
                 {
                     "RegionSecondaryHotkeyBox",
                     "CurrentSecondaryHotkeyBox",
                     "DesktopSecondaryHotkeyBox",
                     "StartRecordingSecondaryHotkeyBox",
                     "StopRecordingSecondaryHotkeyBox",
                     "ConfirmCaptureSecondaryHotkeyBox",
                     "CancelCaptureSecondaryHotkeyBox",
                     "StopLongCaptureSecondaryHotkeyBox",
                 })
        {
            Equal(true, xaml.Contains($"x:Name=\"{name}\"", StringComparison.Ordinal));
        }
        Equal(false, xaml.Contains("UndoEditHotkeyBox", StringComparison.Ordinal));
        Equal(true, xaml.Contains("Ctrl+Z 撤回 · Ctrl+Shift+Z 重做", StringComparison.Ordinal));
        Equal(true, xaml.Contains("主快捷键", StringComparison.Ordinal));
        Equal(true, xaml.Contains("备用快捷键（可选）", StringComparison.Ordinal));
        Equal(true, source.Contains("IsSecondaryHotkeyBox", StringComparison.Ordinal));
        Equal(true, source.Contains("SetHotkeyValue(box, null)", StringComparison.Ordinal));
        Equal(true, source.Contains("RegionCaptureSecondaryHotkey = GetOptionalHotkeyValue(RegionSecondaryHotkeyBox)", StringComparison.Ordinal));
        Equal(true, source.Contains("StartRecordingSecondaryHotkey = GetOptionalHotkeyValue(StartRecordingSecondaryHotkeyBox)", StringComparison.Ordinal));
        Equal(true, source.Contains("StopLongCaptureSecondaryHotkey = GetOptionalHotkeyValue(StopLongCaptureSecondaryHotkeyBox)", StringComparison.Ordinal));
    }

    private static void ParsesCleanupSettingsInput()
    {
        Equal(30, SettingsInputParser.ParseRetentionDays("30"));
        Equal(new TimeOnly(3, 5), SettingsInputParser.ParseDailyTime("03:05"));
        Equal(new TimeOnly(3, 5), SettingsInputParser.ParseDailyTime("3:05"));
        Throws<InvalidOperationException>(() => SettingsInputParser.ParseRetentionDays("0"));
        Throws<InvalidOperationException>(() => SettingsInputParser.ParseRetentionDays("3651"));
        Throws<InvalidOperationException>(() => SettingsInputParser.ParseRetentionDays("三十"));
        Throws<InvalidOperationException>(() => SettingsInputParser.ParseDailyTime("24:01"));
        Throws<InvalidOperationException>(() => SettingsInputParser.ParseDailyTime("later"));
    }

    private static void NormalizesForgivingCleanupTimeInput()
    {
        Equal("03:00", CleanupTimeInputPolicy.Normalize("3"));
        Equal("03:00", CleanupTimeInputPolicy.Normalize("3:0"));
        Equal("03:07", CleanupTimeInputPolicy.Normalize("3:7"));
        Equal("03:00", CleanupTimeInputPolicy.Normalize("3:00"));
        Equal("03:00", CleanupTimeInputPolicy.Normalize("03:00"));
        Equal("03:07", CleanupTimeInputPolicy.Normalize(" 3：7 "));
        Equal(true, CleanupTimeInputPolicy.IsCandidateText(""));
        Equal(true, CleanupTimeInputPolicy.IsCandidateText("3:"));
        Equal(true, CleanupTimeInputPolicy.IsCandidateText("03:0"));
        Equal(false, CleanupTimeInputPolicy.IsCandidateText("3::0"));
        Equal(false, CleanupTimeInputPolicy.IsCandidateText("3pm"));
        Throws<InvalidOperationException>(() => CleanupTimeInputPolicy.Normalize("24"));
        Throws<InvalidOperationException>(() => CleanupTimeInputPolicy.Normalize("3:70"));
        Throws<InvalidOperationException>(() => CleanupTimeInputPolicy.Normalize("3::0"));
        Throws<InvalidOperationException>(() => CleanupTimeInputPolicy.Normalize("later"));
    }

    private static void RoutesCtrlSThroughTheSettingsApplyPath()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var xaml = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "Settings", "SettingsWindow.xaml"));
        var source = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "Settings", "SettingsWindow.xaml.cs"));

        Equal(true, xaml.Contains("PreviewKeyDown=\"SettingsWindow_PreviewKeyDown\"", StringComparison.Ordinal));
        Equal(true, source.Contains("private bool TryApplyCurrentSettings()", StringComparison.Ordinal));
        Equal(true, source.Contains("Apply_Click(object sender, RoutedEventArgs e) => ExecutePrimaryAction();", StringComparison.Ordinal));
        Equal(true, source.Contains("Keyboard.Modifiers != ModifierKeys.Control", StringComparison.Ordinal));
        Equal(true, source.Contains("ExecutePrimaryAction();", StringComparison.Ordinal));
        Equal(true, source.Contains("BuildDraftFingerprint", StringComparison.Ordinal));
        Equal(true, source.Contains("SettingsPrimaryAction.Refresh", StringComparison.Ordinal));
        Equal(true, xaml.Contains("x:Key=\"RefreshButton\"", StringComparison.Ordinal));
        Equal(true, xaml.Contains("Content=\"立即刷新\"", StringComparison.Ordinal));
    }

    private static void RequiresTwoConfirmationsBeforeDeletingALocalCredential()
    {
        var prompts = new List<string>();
        var cancelledFirst = CredentialDeletionPolicy.Confirm("personal", (message, _) =>
        {
            prompts.Add(message);
            return false;
        });
        Equal(false, cancelledFirst);
        Equal(1, prompts.Count);
        Equal(true, prompts[0].Contains("在本机保存的 API key", StringComparison.Ordinal));
        Equal(true, prompts[0].Contains("不会使 DeepSeek 服务端的 key 失效", StringComparison.Ordinal));

        prompts.Clear();
        var cancelledSecond = CredentialDeletionPolicy.Confirm("personal", (message, _) =>
        {
            prompts.Add(message);
            return prompts.Count == 1;
        });
        Equal(false, cancelledSecond);
        Equal(2, prompts.Count);
        Equal("您确认删除“personal”吗？", prompts[1]);

        prompts.Clear();
        Equal(true, CredentialDeletionPolicy.Confirm("personal", (message, _) =>
        {
            prompts.Add(message);
            return true;
        }));
        Equal(2, prompts.Count);
        Equal("已删除 API 凭据“personal”。", CredentialDeletionPolicy.Success("personal"));
        Equal(false, CredentialDeletionPolicy.Success("personal").Contains("撤销", StringComparison.Ordinal));
        Equal(false, CredentialDeletionPolicy.Success("personal").Contains("失效", StringComparison.Ordinal));
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var settingsSource = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "Settings", "SettingsWindow.xaml.cs"));
        Equal(true, settingsSource.Contains("_connectionOperations.CancelPending();", StringComparison.Ordinal));
        Equal(true, settingsSource.Contains("const string name = CredentialVault.FixedDeepSeekSlot;", StringComparison.Ordinal));
    }

    private static void RoundTripsSettingsJsonWithoutSecrets()
    {
        using var workspace = TemporaryWorkspace.Create();
        var path = Path.Combine(workspace.Root, "settings.json");
        ISettingsStore store = new SettingsStore(path, workspace.Root);
        var settings = AppSettings.CreateDefault(workspace.Root) with
        {
            CredentialName = "personal-deepseek",
            DeepSeekModel = DeepSeekModels.Flash,
        };

        store.Save(settings);
        var json = File.ReadAllText(path);
        var loaded = store.Load();

        Equal(settings, loaded);
        Equal(false, json.Contains("apiKey", StringComparison.OrdinalIgnoreCase));
        Equal(false, json.Contains("secret", StringComparison.OrdinalIgnoreCase));
    }

    private static void MigratesOldSettingsWithRecordingAndCleanupDefaults()
    {
        using var workspace = TemporaryWorkspace.Create();
        var path = Path.Combine(workspace.Root, "settings.json");
        var screenshotDirectory = Path.Combine(workspace.Root, "kept-shots");
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(new
        {
            ScreenshotDirectory = screenshotDirectory,
            StartWithWindows = true,
            Provider = "DeepSeek",
            DeepSeekModel = DeepSeekModels.Flash,
            TranslationTargetLanguage = "English",
        }));
        ISettingsStore store = new SettingsStore(path, workspace.Root);

        var loaded = store.Load();

        Equal(screenshotDirectory, loaded.ScreenshotDirectory);
        Equal(Path.Combine(workspace.Root, "Outputs", "LongCaptures"), loaded.LongCaptureDirectory);
        Equal(Path.Combine(workspace.Root, "Outputs", "Recordings"), loaded.RecordingDirectory);
        Equal(true, loaded.StartWithWindows);
        Equal(DeepSeekModels.Flash, loaded.DeepSeekModel);
        Equal("English", loaded.TranslationTargetLanguage);
        Equal(false, loaded.CleanupScheduleEnabled);
        Equal(30, loaded.CleanupRetentionDays);
        Equal(CleanupScheduleFrequency.Daily, loaded.CleanupScheduleFrequency);
        Equal(new TimeOnly(21, 0), loaded.CleanupDailyTime);
        Equal("DeepSeek", loaded.Provider);
    }

    private static void MigratesImplicitChineseProviderToMachine()
    {
        using var workspace = TemporaryWorkspace.Create();
        var path = Path.Combine(workspace.Root, "settings.json");
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(new
        {
            Provider = "DeepSeek",
            TranslationTargetLanguage = "简体中文",
        }));

        var loaded = new SettingsStore(path, workspace.Root).LoadRecoverable();

        Equal("Machine", loaded.Settings.Provider);
        Equal(true, loaded.RequiresSave);
    }

    private static void PreservesExplicitDeepSeekChoice()
    {
        using var workspace = TemporaryWorkspace.Create();
        var path = Path.Combine(workspace.Root, "settings.json");
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(new
        {
            Provider = "DeepSeek",
            HasExplicitProviderChoice = true,
            TranslationTargetLanguage = "简体中文",
        }));

        var loaded = new SettingsStore(path, workspace.Root).LoadRecoverable();

        Equal("DeepSeek", loaded.Settings.Provider);
    }

    private static void EnablesMachineFallbackWhenPersistedChoiceIsAbsent()
    {
        using var workspace = TemporaryWorkspace.Create();
        var path = Path.Combine(workspace.Root, "settings.json");
        var store = new SettingsStore(path, workspace.Root);
        store.Save(AppSettings.CreateDefault(workspace.Root));
        var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Equal(true, json.Remove(nameof(AppSettings.MachineTranslationFallbackEnabled)));
        File.WriteAllText(path, json.ToJsonString());

        var loaded = store.LoadRecoverable();

        Equal(true, loaded.Settings.MachineTranslationFallbackEnabled);
        Equal(true, loaded.RequiresSave);
    }

    private static void PreservesExplicitlyDisabledMachineFallback()
    {
        using var workspace = TemporaryWorkspace.Create();
        var path = Path.Combine(workspace.Root, "settings.json");
        var store = new SettingsStore(path, workspace.Root);
        store.Save(AppSettings.CreateDefault(workspace.Root) with
        {
            MachineTranslationFallbackEnabled = false,
        });

        var loaded = store.LoadRecoverable();

        Equal(false, loaded.Settings.MachineTranslationFallbackEnabled);
        Equal(false, loaded.RequiresSave);
    }

    private static void RebasesCompleteLegacyOutputLayoutToCurrentInstallRoot()
    {
        using var workspace = TemporaryWorkspace.Create();
        var legacyRoot = Path.Combine(workspace.Root, "previous-install");
        var currentRoot = Path.Combine(workspace.Root, "current-install");
        var path = Path.Combine(workspace.Root, "settings.json");
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(new
        {
            ScreenshotDirectory = Path.Combine(legacyRoot, "Screenshots"),
            LongCaptureDirectory = Path.Combine(legacyRoot, "LongCaptures"),
            RecordingDirectory = Path.Combine(legacyRoot, "Recordings"),
        }));

        var loaded = new SettingsStore(path, currentRoot).Load();

        Equal(Path.Combine(currentRoot, "Outputs", "Screenshots"), loaded.ScreenshotDirectory);
        Equal(Path.Combine(currentRoot, "Outputs", "LongCaptures"), loaded.LongCaptureDirectory);
        Equal(Path.Combine(currentRoot, "Outputs", "Recordings"), loaded.RecordingDirectory);
    }

    private static void RebasesHistoricalSpacedLongCaptureLayoutToCurrentInstallRoot()
    {
        using var workspace = TemporaryWorkspace.Create();
        var legacyRoot = Path.Combine(workspace.Root, "previous-install");
        var currentRoot = Path.Combine(workspace.Root, "current-install");
        var path = Path.Combine(workspace.Root, "settings.json");
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(new
        {
            ScreenshotDirectory = Path.Combine(legacyRoot, "Screenshots"),
            LongCaptureDirectory = Path.Combine(legacyRoot, "Long Captures"),
            RecordingDirectory = Path.Combine(legacyRoot, "Recordings"),
        }));

        var loaded = new SettingsStore(path, currentRoot).Load();

        Equal(Path.Combine(currentRoot, "Outputs", "Screenshots"), loaded.ScreenshotDirectory);
        Equal(Path.Combine(currentRoot, "Outputs", "LongCaptures"), loaded.LongCaptureDirectory);
        Equal(Path.Combine(currentRoot, "Outputs", "Recordings"), loaded.RecordingDirectory);
    }

    private static void MarksRebasedLegacyOutputLayoutForStartupPersistence()
    {
        using var workspace = TemporaryWorkspace.Create();
        var legacyRoot = Path.Combine(workspace.Root, "previous-install");
        var currentRoot = Path.Combine(workspace.Root, "current-install");
        var path = Path.Combine(workspace.Root, "settings.json");
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(new
        {
            ScreenshotDirectory = Path.Combine(legacyRoot, "Screenshots"),
            LongCaptureDirectory = Path.Combine(legacyRoot, "LongCaptures"),
            RecordingDirectory = Path.Combine(legacyRoot, "Recordings"),
        }));

        var store = new SettingsStore(path, currentRoot);
        var result = store.LoadRecoverable();

        Equal(true, result.RequiresSave);
        store.Save(result.Settings);
        var persisted = new SettingsStore(path, currentRoot).Load();
        Equal(Path.Combine(currentRoot, "Outputs", "Screenshots"), persisted.ScreenshotDirectory);
        Equal(Path.Combine(currentRoot, "Outputs", "LongCaptures"), persisted.LongCaptureDirectory);
        Equal(Path.Combine(currentRoot, "Outputs", "Recordings"), persisted.RecordingDirectory);
    }

    private static void PreservesExplicitlyCustomOutputDirectories()
    {
        using var workspace = TemporaryWorkspace.Create();
        var currentRoot = Path.Combine(workspace.Root, "current-install");
        var customRoot = Path.Combine(workspace.Root, "my-exports");
        var path = Path.Combine(workspace.Root, "settings.json");
        var screenshot = Path.Combine(customRoot, "Shots");
        var longCapture = Path.Combine(customRoot, "Scrolls");
        var recording = Path.Combine(customRoot, "Videos");
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(new
        {
            ScreenshotDirectory = screenshot,
            LongCaptureDirectory = longCapture,
            RecordingDirectory = recording,
            MachineTranslationFallbackEnabled = false,
        }));

        var result = new SettingsStore(path, currentRoot).LoadRecoverable();

        Equal(screenshot, result.Settings.ScreenshotDirectory);
        Equal(longCapture, result.Settings.LongCaptureDirectory);
        Equal(recording, result.Settings.RecordingDirectory);
        Equal("Machine", result.Settings.LiveTranslationProvider);
        Equal(true, result.RequiresSave);
    }

    private static void RejectsCleanupRetentionOutsideTheSafeRange()
    {
        using var workspace = TemporaryWorkspace.Create();
        var settings = AppSettings.CreateDefault(workspace.Root);

        Throws<InvalidOperationException>(() => (settings with { CleanupRetentionDays = 0 }).Validate());
        Throws<InvalidOperationException>(() => (settings with { CleanupRetentionDays = 3651 }).Validate());
        (settings with { CleanupRetentionDays = 1 }).Validate();
        (settings with { CleanupRetentionDays = 3650 }).Validate();
    }

    private static void RejectsConflictingHotkeys()
    {
        using var workspace = TemporaryWorkspace.Create();
        var settings = AppSettings.CreateDefault(workspace.Root) with
        {
            CurrentScreenHotkey = GlobalHotkey.Parse("Ctrl+Win+A"),
        };

        Throws<InvalidOperationException>(() => settings.ValidateHotkeys());
    }

    private static void ReservesFixedOverlayEditHotkeys()
    {
        using var workspace = TemporaryWorkspace.Create();
        var globalConflict = AppSettings.CreateDefault(workspace.Root) with
        {
            StartRecordingHotkey = GlobalHotkey.Parse("Ctrl+Z"),
        };
        var overlayConflict = AppSettings.CreateDefault(workspace.Root) with
        {
            ConfirmCaptureSecondaryHotkey = GlobalHotkey.Parse("Ctrl+Shift+Z"),
        };
        var crossScopeConflict = AppSettings.CreateDefault(workspace.Root) with
        {
            ConfirmCaptureHotkey = GlobalHotkey.Parse("Ctrl+Win+A"),
        };

        Throws<InvalidOperationException>(() => globalConflict.ValidateHotkeys());
        Throws<InvalidOperationException>(() => overlayConflict.ValidateHotkeys());
        Throws<InvalidOperationException>(() => crossScopeConflict.ValidateHotkeys());
    }

    private static void RejectsFixedEditHotkeysDuringCaptureAndLongCaptureValidation()
    {
        using var workspace = TemporaryWorkspace.Create();
        var undoFailure = ThrowsAndReturns<ArgumentException>(() => HotkeyCapturePolicy.Capture(
            System.Windows.Input.Key.Z,
            System.Windows.Input.ModifierKeys.Control));
        var redoFailure = ThrowsAndReturns<ArgumentException>(() => HotkeyCapturePolicy.Capture(
            System.Windows.Input.Key.Z,
            System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift));

        Equal("Ctrl+Z 和 Ctrl+Shift+Z 已固定用于覆盖层撤回与重做，请改用其他快捷键。", undoFailure.Message);
        Equal(undoFailure.Message, redoFailure.Message);

        var settingsWindowSource = File.ReadAllText(Path.Combine(
            WorkspaceLocator.Find(AppContext.BaseDirectory),
            "src", "ClipRay.App", "Settings", "SettingsWindow.xaml.cs"));
        Equal(true, settingsWindowSource.Contains("catch (ArgumentException exception)", StringComparison.Ordinal));
        Equal(true, settingsWindowSource.Contains(
            "ShowStatus(Ui(exception.Message, \"That shortcut is unavailable. Choose another combination.\"), success: false);",
            StringComparison.Ordinal));

        var stopLongConflict = AppSettings.CreateDefault(workspace.Root) with
        {
            StopLongCaptureHotkey = GlobalHotkey.Parse("Ctrl+Z"),
        };
        Throws<InvalidOperationException>(() => stopLongConflict.ValidateHotkeys());
    }

    private static void AllowsASingleKAsACustomHotkey()
    {
        using var workspace = TemporaryWorkspace.Create();
        var hotkey = HotkeyCapturePolicy.Capture(
            System.Windows.Input.Key.K,
            System.Windows.Input.ModifierKeys.None);
        var settings = AppSettings.CreateDefault(workspace.Root) with
        {
            RegionCaptureHotkey = hotkey,
        };

        Equal("K", hotkey.ToString());
        Equal(true, hotkey.ToVirtualKey() != 0);
        settings.ValidateHotkeys();
    }

    private static void EnumeratesAnOptionalSecondaryHotkey()
    {
        var primary = GlobalHotkey.Parse("Ctrl+Win+A");
        var secondary = GlobalHotkey.Parse("Ctrl+Shift+A");

        Equal(1, HotkeyBindingPolicy.Active(primary, null).Count);
        Equal(2, HotkeyBindingPolicy.Active(primary, secondary).Count);
        Equal(primary, HotkeyBindingPolicy.Active(primary, secondary)[0]);
        Equal(secondary, HotkeyBindingPolicy.Active(primary, secondary)[1]);
    }

    private static void FormatsActiveHotkeysForUserFeedback()
    {
        var primary = GlobalHotkey.Parse("F8");
        var secondary = GlobalHotkey.Parse("Ctrl+F8");

        Equal("F8", HotkeyBindingPolicy.FormatForDisplay(primary, null));
        Equal("F8 或 Ctrl+F8", HotkeyBindingPolicy.FormatForDisplay(primary, secondary));

        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var appSource = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "App.xaml.cs"));
        Equal(true, appSource.Contains("HotkeyBindingPolicy.FormatForDisplay(", StringComparison.Ordinal));
        Equal(false, appSource.Contains("正在录屏，按 F8 停止", StringComparison.Ordinal));
    }

    private static void RejectsConflictsFromSecondaryGlobalHotkeys()
    {
        using var workspace = TemporaryWorkspace.Create();
        var settings = AppSettings.CreateDefault(workspace.Root) with
        {
            RegionCaptureSecondaryHotkey = GlobalHotkey.Parse("Ctrl+Insert"),
        };
        var duplicatePair = AppSettings.CreateDefault(workspace.Root) with
        {
            RegionCaptureSecondaryHotkey = GlobalHotkey.Parse("Ctrl+Win+A"),
        };
        var stopLongConflict = AppSettings.CreateDefault(workspace.Root) with
        {
            CurrentScreenSecondaryHotkey = GlobalHotkey.Parse("Ctrl+Shift+L"),
            StopLongCaptureSecondaryHotkey = GlobalHotkey.Parse("Ctrl+Shift+L"),
        };
        var duplicateStopLongPair = AppSettings.CreateDefault(workspace.Root) with
        {
            StopLongCaptureSecondaryHotkey = GlobalHotkey.Parse("Escape"),
        };

        Throws<InvalidOperationException>(() => settings.ValidateHotkeys());
        Throws<InvalidOperationException>(() => duplicatePair.ValidateHotkeys());
        Throws<InvalidOperationException>(() => stopLongConflict.ValidateHotkeys());
        Throws<InvalidOperationException>(() => duplicateStopLongPair.ValidateHotkeys());
    }

    private static void RejectsConflictsFromSecondaryOverlayHotkeys()
    {
        using var workspace = TemporaryWorkspace.Create();
        var settings = AppSettings.CreateDefault(workspace.Root) with
        {
            ConfirmCaptureSecondaryHotkey = GlobalHotkey.Parse("Escape"),
        };

        Throws<InvalidOperationException>(() => settings.ValidateHotkeys());
    }

    private static void MatchesEitherPrimaryOrSecondaryHotkey()
    {
        var primary = GlobalHotkey.Parse("Enter");
        var secondary = GlobalHotkey.Parse("Ctrl+Enter");

        Equal(true, HotkeyCapturePolicy.MatchesAny(primary, secondary, System.Windows.Input.Key.Enter, System.Windows.Input.ModifierKeys.None));
        Equal(true, HotkeyCapturePolicy.MatchesAny(primary, secondary, System.Windows.Input.Key.Enter, System.Windows.Input.ModifierKeys.Control));
        Equal(false, HotkeyCapturePolicy.MatchesAny(primary, null, System.Windows.Input.Key.Enter, System.Windows.Input.ModifierKeys.Control));
    }

    private static void WiresSecondaryGlobalAndLongCaptureHotkeysIntoRuntime()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var appSource = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "App.xaml.cs"));
        var overlaySource = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "Overlay", "SelectionOverlayWindow.xaml.cs"));
        var controlSource = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "Scrolling", "ScrollCaptureControlWindow.cs"));

        Equal(true, appSource.Contains("RegisterAll(_settings!.RegionCaptureHotkey, _settings.RegionCaptureSecondaryHotkey", StringComparison.Ordinal));
        Equal(true, appSource.Contains("RegisterAll(_settings.CurrentScreenHotkey, _settings.CurrentScreenSecondaryHotkey", StringComparison.Ordinal));
        Equal(true, appSource.Contains("RegisterAll(_settings.VirtualDesktopHotkey, _settings.VirtualDesktopSecondaryHotkey", StringComparison.Ordinal));
        Equal(true, appSource.Contains("RegisterAll(_settings.StartRecordingHotkey, _settings.StartRecordingSecondaryHotkey", StringComparison.Ordinal));
        Equal(true, appSource.Contains("RegisterAll(_settings.StopRecordingHotkey, _settings.StopRecordingSecondaryHotkey", StringComparison.Ordinal));
        Equal(true, appSource.Contains("_settings.StopLongCaptureSecondaryHotkey", StringComparison.Ordinal));
        Equal(true, overlaySource.Contains("StopLongCaptureSecondaryHotkey", StringComparison.Ordinal));
        Equal(true, overlaySource.Contains("ConfirmCaptureSecondaryHotkey", StringComparison.Ordinal));
        Equal(true, overlaySource.Contains("CancelCaptureSecondaryHotkey", StringComparison.Ordinal));
        Equal(false, overlaySource.Contains("UndoEditSecondaryHotkey", StringComparison.Ordinal));
        Equal(true, overlaySource.Contains("ModifierKeys.Control | ModifierKeys.Shift", StringComparison.Ordinal));
        Equal(true, overlaySource.Contains("RedoLastEdit();", StringComparison.Ordinal));
        Equal(true, controlSource.Contains("_secondaryStopHotkey", StringComparison.Ordinal));
        Equal(true, overlaySource.Contains("var modifiers = HotkeyCapturePolicy.ReadCurrentModifiers();", StringComparison.Ordinal));
        Equal(true, controlSource.Contains("HotkeyCapturePolicy.ReadCurrentModifiers()", StringComparison.Ordinal));
    }

    private static void ReleasesGlobalHotkeysWhileSettingsCapturesAReplacement()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var source = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "App.xaml.cs"));
        var openSettings = source.IndexOf("private void OpenSettings()", StringComparison.Ordinal);
        var windowShow = source.IndexOf("_settingsWindow.Show();", openSettings, StringComparison.Ordinal);
        var release = source.IndexOf("_hotkeyHost?.Dispose();", openSettings, StringComparison.Ordinal);
        var focusChanged = source.IndexOf("_settingsWindow.HotkeyCaptureFocusChanged", openSettings, StringComparison.Ordinal);
        var closed = source.IndexOf("_settingsWindow.Closed", openSettings, StringComparison.Ordinal);
        var restore = source.IndexOf("RegisterHotkeys();", closed, StringComparison.Ordinal);

        Equal(true, openSettings >= 0 && focusChanged > openSettings && release > focusChanged);
        Equal(true, closed > openSettings && restore > closed);
        Equal(true, source.Contains("ApplySettings(settings, registerHotkeys: true);", StringComparison.Ordinal));
    }

    private static void KeepsGlobalHotkeysActiveOutsideFocusedSettingsHotkeyField()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var appSource = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "App.xaml.cs"));
        var settingsSource = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "Settings", "SettingsWindow.xaml.cs"));

        Equal(true, appSource.Contains("HotkeyCaptureFocusChanged", StringComparison.Ordinal));
        Equal(true, settingsSource.Contains("HotkeyCaptureFocusChanged", StringComparison.Ordinal));
        Equal(true, settingsSource.Contains("GotKeyboardFocus", StringComparison.Ordinal));
        Equal(true, settingsSource.Contains("LostKeyboardFocus", StringComparison.Ordinal));
    }

    private static void UsesWorkingAreaForTaskbarFreeCurrentScreenCapture()
    {
        var bounds = new Rectangle(-1920, 0, 1920, 1080);
        var workingArea = new Rectangle(-1920, 0, 1920, 1040);

        Equal(workingArea, CurrentScreenCaptureBounds.Resolve(bounds, workingArea));
    }

    private static void FreeCaptureIncludesTaskbar()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var app = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "App.xaml.cs"));
        var service = File.ReadAllText(Path.Combine(
            workspace, "src", "ClipRay.App", "Capture", "ScreenCaptureService.cs"));
        var interactiveStart = app.IndexOf("private void StartInteractiveCapture", StringComparison.Ordinal);
        var interactiveEnd = app.IndexOf("private void StartRecordingSelection", interactiveStart, StringComparison.Ordinal);
        var interactive = app[interactiveStart..interactiveEnd];

        Equal(true, interactive.Contains("CaptureVirtualDesktop()", StringComparison.Ordinal));
        Equal(true, service.Contains("CaptureCurrentMonitorIncludingTaskbar()", StringComparison.Ordinal));
        Equal(true, service.Contains(
            "CurrentScreenCaptureBounds.Resolve(screen.Bounds, screen.WorkingArea)",
            StringComparison.Ordinal));
    }

    private static void DefinesPrivacySafeClipboardOwnerMonitor()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var scriptPath = Path.Combine(workspace, "scripts", "ClipboardOwnerMonitor.ps1");

        Equal(true, File.Exists(scriptPath));
        var source = File.ReadAllText(scriptPath);
        Equal(true, source.Contains("GetClipboardOwner", StringComparison.Ordinal));
        Equal(true, source.Contains("WM_CLIPBOARDUPDATE", StringComparison.Ordinal));
        Equal(true, source.Contains("ClipRay.ColorPicker.v1", StringComparison.Ordinal));
        Equal(true, source.Contains("TextSha256", StringComparison.Ordinal));
        Equal(true, source.Contains("#000000", StringComparison.Ordinal));
    }

    private static void RoutesScreenshotsToConfiguredDirectory()
    {
        using var workspace = TemporaryWorkspace.Create();
        var customDirectory = Path.Combine(workspace.Root, "custom-shots");
        using var bitmap = new Bitmap(2, 2);
        var output = new ImageOutputService(customDirectory, copyToClipboard: false);

        var path = output.SavePng(bitmap, new DateTime(2026, 8, 4, 12, 0, 0));

        Equal(Path.Combine(customDirectory, "2026-08-04"), Path.GetDirectoryName(path)!);
        Equal("ClipRay-Screenshot_2026-08-04_12-00-00-000.png", Path.GetFileName(path));
    }

    private static void RoutesLongCapturesToReadableDailyPath()
    {
        using var workspace = TemporaryWorkspace.Create();
        var customDirectory = Path.Combine(workspace.Root, "custom-long-captures");
        using var bitmap = new Bitmap(2, 2);
        var output = new ImageOutputService(customDirectory, copyToClipboard: false, OutputArtifactKind.LongCapture);

        var path = output.SavePng(bitmap, new DateTime(2026, 8, 4, 12, 1, 2, 345));

        Equal(Path.Combine(customDirectory, "2026-08-04"), Path.GetDirectoryName(path)!);
        Equal("ClipRay-LongCapture_2026-08-04_12-01-02-345.png", Path.GetFileName(path));
    }

    private static void RoutesRecordingsToTheConfiguredDirectory()
    {
        using var workspace = TemporaryWorkspace.Create();
        var customDirectory = Path.Combine(workspace.Root, "custom-recordings");

        var paths = RecordingOutputPaths.Create(customDirectory, new DateTime(2026, 8, 5, 8, 9, 10));

        Equal(Path.Combine(customDirectory, "2026-08-05"), Path.GetDirectoryName(paths.FinalPath)!);
        Equal(Path.Combine(customDirectory, "2026-08-05"), Path.GetDirectoryName(paths.PartialPath)!);
        Equal("ClipRay-Recording_2026-08-05_08-09-10-000.mp4", Path.GetFileName(paths.FinalPath));
        Equal("ClipRay-Recording_2026-08-05_08-09-10-000.partial.mp4", Path.GetFileName(paths.PartialPath));
    }

    private static void DeletesOnlyExpiredCompletedClipRayOutputs()
    {
        using var workspace = TemporaryWorkspace.Create();
        var screenshots = Path.Combine(workspace.Root, "screenshots");
        var recordings = Path.Combine(workspace.Root, "recordings");
        Directory.CreateDirectory(screenshots);
        Directory.CreateDirectory(Path.Combine(recordings, "2026-06-01"));
        var expiredImage = WriteAgedFile(screenshots, "ClipRay_120000_000.png", new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc));
        var expiredRecording = WriteAgedFile(Path.Combine(recordings, "2026-06-01"), "ClipRay-20260601-120000-000-0123456789abcdef0123456789abcdef.mp4", new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc));
        var settings = AppSettings.CreateDefault(workspace.Root) with
        {
            ScreenshotDirectory = screenshots,
            RecordingDirectory = recordings,
            CleanupScheduleEnabled = true,
            CleanupRetentionDays = 30,
        };
        var plan = CleanupPlan.FromSettings(settings, new DateTimeOffset(2026, 8, 12, 12, 0, 0, TimeSpan.Zero));

        var result = new CleanupService().Execute(plan!);

        Equal(false, File.Exists(expiredImage));
        Equal(false, File.Exists(expiredRecording));
        Equal(2, result.DeletedFileCount);
        Equal(1, result.DeletedDirectoryCount);
        Equal(0, result.FailedPathCount);
    }

    private static void IncludesDedicatedLongCaptureOutputInCleanup()
    {
        using var workspace = TemporaryWorkspace.Create();
        var longCaptures = Path.Combine(workspace.Root, "long-captures");
        var dated = Path.Combine(longCaptures, "2026-06-01");
        var expired = WriteAgedFile(
            dated,
            "ClipRay-LongCapture_2026-06-01_12-00-00-000.png",
            new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        var settings = AppSettings.CreateDefault(workspace.Root) with
        {
            LongCaptureDirectory = longCaptures,
            CleanupScheduleEnabled = true,
            CleanupRetentionDays = 1,
        };

        _ = new CleanupService().Execute(CleanupPlan.FromSettings(
            settings,
            new DateTimeOffset(2026, 8, 12, 12, 0, 0, TimeSpan.Zero))!);

        Equal(false, File.Exists(expired));
    }

    private static void KeepsCleanupBoundaryIncompleteRecoveryAndUnrelatedFiles()
    {
        using var workspace = TemporaryWorkspace.Create();
        var screenshots = Path.Combine(workspace.Root, "screenshots");
        var recordings = Path.Combine(workspace.Root, "recordings");
        var longCaptureRecovery = Path.Combine(workspace.Root, "tmp", "long-capture", "session");
        Directory.CreateDirectory(screenshots);
        Directory.CreateDirectory(recordings);
        Directory.CreateDirectory(longCaptureRecovery);
        var cutoff = new DateTime(2026, 7, 13, 12, 0, 0, DateTimeKind.Utc);
        var boundaryImage = WriteAgedFile(screenshots, "ClipRay_120000_000.png", cutoff);
        var incomplete = WriteAgedFile(recordings, "ClipRay-20260601-120000-000-0123456789abcdef0123456789abcdef.partial.mp4", cutoff.AddDays(-10));
        var unrelatedPng = WriteAgedFile(screenshots, "vacation.png", cutoff.AddDays(-10));
        var unrelatedMp4 = WriteAgedFile(recordings, "movie.mp4", cutoff.AddDays(-10));
        var recovery = WriteAgedFile(longCaptureRecovery, "000.png", cutoff.AddDays(-10));
        var settings = AppSettings.CreateDefault(workspace.Root) with
        {
            ScreenshotDirectory = screenshots,
            RecordingDirectory = recordings,
            CleanupScheduleEnabled = true,
            CleanupRetentionDays = 30,
        };

        _ = new CleanupService().Execute(CleanupPlan.FromSettings(
            settings,
            new DateTimeOffset(2026, 8, 12, 12, 0, 0, TimeSpan.Zero))!);

        Equal(true, File.Exists(boundaryImage));
        Equal(true, File.Exists(incomplete));
        Equal(true, File.Exists(unrelatedPng));
        Equal(true, File.Exists(unrelatedMp4));
        Equal(true, File.Exists(recovery));
    }

    private static void DeduplicatesCleanupRootsAndRemovesOnlyEmptyDateFolders()
    {
        using var workspace = TemporaryWorkspace.Create();
        var shared = Path.Combine(workspace.Root, "shared");
        var emptyDateFolder = Path.Combine(shared, "2026-06-01");
        var unrelatedEmptyFolder = Path.Combine(shared, "archive");
        Directory.CreateDirectory(emptyDateFolder);
        Directory.CreateDirectory(unrelatedEmptyFolder);
        _ = WriteAgedFile(emptyDateFolder, "ClipRay_120000_000.png", new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        _ = WriteAgedFile(emptyDateFolder, "ClipRay-20260601-120000-000-0123456789abcdef0123456789abcdef.mp4", new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        var settings = AppSettings.CreateDefault(workspace.Root) with
        {
            ScreenshotDirectory = shared,
            LongCaptureDirectory = shared,
            RecordingDirectory = shared,
            CleanupScheduleEnabled = true,
            CleanupRetentionDays = 1,
        };
        var plan = CleanupPlan.FromSettings(settings, new DateTimeOffset(2026, 8, 12, 12, 0, 0, TimeSpan.Zero))!;

        var result = new CleanupService().Execute(plan);

        Equal(1, plan.Targets.Count);
        Equal(2, plan.Targets[0].FileKinds.Count);
        Equal(false, Directory.Exists(emptyDateFolder));
        Equal(true, Directory.Exists(unrelatedEmptyFolder));
        Equal(2, result.DeletedFileCount);
    }

    private static void KeepsOwnedLookingFilesBelowNonDateSubdirectories()
    {
        using var workspace = TemporaryWorkspace.Create();
        var screenshots = Path.Combine(workspace.Root, "screenshots");
        var archive = Path.Combine(screenshots, "archive", "2026-06-01");
        var nestedOwnedLookingFile = WriteAgedFile(
            archive,
            "ClipRay_120000_000.png",
            new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        var settings = AppSettings.CreateDefault(workspace.Root) with
        {
            ScreenshotDirectory = screenshots,
            CleanupScheduleEnabled = true,
            CleanupRetentionDays = 1,
        };

        var result = new CleanupService().Execute(CleanupPlan.FromSettings(
            settings,
            new DateTimeOffset(2026, 8, 12, 12, 0, 0, TimeSpan.Zero))!);

        Equal(true, File.Exists(nestedOwnedLookingFile));
        Equal(0, result.DeletedFileCount);
    }

    private static void RejectsReparsePointTraversalDuringCleanup()
    {
        Equal(false, CleanupPathPolicy.CanDescend(FileAttributes.Directory | FileAttributes.ReparsePoint));
        Equal(true, CleanupPathPolicy.CanDescend(FileAttributes.Directory));
    }

    private static void ContinuesCleanupAfterALockedFileFailure()
    {
        using var workspace = TemporaryWorkspace.Create();
        var screenshots = Path.Combine(workspace.Root, "screenshots");
        Directory.CreateDirectory(screenshots);
        var lockedPath = WriteAgedFile(screenshots, "ClipRay_120000_000.png", new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        var removablePath = WriteAgedFile(screenshots, "ClipRay_120001_000.png", new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        var settings = AppSettings.CreateDefault(workspace.Root) with
        {
            ScreenshotDirectory = screenshots,
            RecordingDirectory = Path.Combine(workspace.Root, "recordings"),
            CleanupScheduleEnabled = true,
            CleanupRetentionDays = 1,
        };
        using (new FileStream(lockedPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var result = new CleanupService().Execute(CleanupPlan.FromSettings(
                settings,
                new DateTimeOffset(2026, 8, 12, 12, 0, 0, TimeSpan.Zero))!);

            Equal(true, File.Exists(lockedPath));
            Equal(false, File.Exists(removablePath));
            Equal(1, result.DeletedFileCount);
            Equal(1, result.FailedPathCount);
        }
    }

    private static void DoesNotBuildAScheduledCleanupPlanWhileDisabled()
    {
        using var workspace = TemporaryWorkspace.Create();
        var settings = AppSettings.CreateDefault(workspace.Root) with { CleanupScheduleEnabled = false };

        Equal(true, CleanupPlan.FromSettings(settings, DateTimeOffset.UtcNow) is null);
    }

    private static void RoundTripsPrivacySafeCleanupRunStatus()
    {
        using var workspace = TemporaryWorkspace.Create();
        var path = Path.Combine(workspace.Root, "cleanup-status.json");
        var store = new CleanupRunStatusStore(path);
        var status = new CleanupRunStatus(
            new DateTimeOffset(2026, 8, 13, 15, 0, 2, TimeSpan.Zero),
            true,
            3,
            1,
            2);

        store.Write(status);
        var loaded = store.Load();
        var json = File.ReadAllText(path);

        Equal(status, loaded!);
        Equal(false, json.Contains("ClipRay_", StringComparison.Ordinal));
        Equal(false, json.Contains("\\", StringComparison.Ordinal));
    }

    private static void FormatsLastCleanupExecutionForSettings()
    {
        Equal("从未执行", CleanupRunStatusFormatter.Format(null, TimeZoneInfo.Utc));
        var status = new CleanupRunStatus(
            new DateTimeOffset(2026, 8, 13, 15, 0, 2, TimeSpan.Zero),
            false,
            3,
            1,
            2);
        var text = CleanupRunStatusFormatter.Format(status, TimeZoneInfo.Utc);

        Equal(true, text.Contains("2026-08-13 15:00:02", StringComparison.Ordinal));
        Equal(true, text.Contains("失败 2", StringComparison.Ordinal));
    }

    private static string WriteAgedFile(string directory, string name, DateTime lastWriteTimeUtc)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name);
        File.WriteAllText(path, "fixture");
        File.SetLastWriteTimeUtc(path, lastWriteTimeUtc);
        return path;
    }

    private static void ListsOnlyMaskedCredentialMetadata()
    {
        var backend = new MemoryCredentialBackend();
        ICredentialVault vault = new CredentialVault(backend, () => new DateTimeOffset(2026, 8, 4, 5, 6, 7, TimeSpan.Zero));

        vault.Save("personal", "DeepSeek", "sk-10430abcdefghijklmnopfb25");
        var item = vault.List().Single();

        Equal("personal", item.Name);
        Equal("DeepSeek", item.Provider);
        Equal(new DateTimeOffset(2026, 8, 4, 5, 6, 7, TimeSpan.Zero), item.UpdatedAt);
        Equal("sk-10430****************fb25", item.MaskedSuffix);
    }

    private static void RejectsStructurallyInvalidDeepSeekCredentials()
    {
        var exception = ThrowsAndReturns<ArgumentException>(() =>
            CredentialSecretPolicy.ValidateCandidate("DeepSeek", "x"));

        Equal(false, exception.Message.Contains("x", StringComparison.Ordinal));
    }

    private static void MasksTheCurrentCredentialWithAnExactHiddenLength()
    {
        var backend = new MemoryCredentialBackend();
        var vault = new CredentialVault(backend, () => DateTimeOffset.UtcNow);
        const string initial = "sk-10430abcdefghijklmnopfb25";
        const string replacement = "sk-ABCDEqrstuvwxyz123456XY99";
        vault.Save("personal", "DeepSeek", initial);

        backend.WriteSecret("personal", replacement);
        var item = vault.List().Single();

        Equal(replacement.Length, item.MaskedSuffix.Length);
        Equal("sk-ABCDE****************XY99", item.MaskedSuffix);
        Equal(false, item.MaskedSuffix.Contains("fb25", StringComparison.Ordinal));
    }

    private static void RestoresPriorCredentialWhenMetadataReplacementFails()
    {
        var backend = new MemoryCredentialBackend();
        var vault = new CredentialVault(backend, () => DateTimeOffset.UtcNow);
        const string initial = "sk-10430abcdefghijklmnopfb25";
        const string replacement = "sk-ABCDEqrstuvwxyz123456XY99";
        vault.Save("personal", "DeepSeek", initial);
        backend.FailNextMetadataWrite = true;

        Throws<IOException>(() => vault.Save("personal", "DeepSeek", replacement));

        Equal(initial, vault.GetForUse("personal")!);
        Equal("sk-10430****************fb25", vault.List().Single().MaskedSuffix);
    }

    private static void RestoresPriorCredentialWhenMetadataDeletionFails()
    {
        var backend = new MemoryCredentialBackend();
        var vault = new CredentialVault(backend, () => DateTimeOffset.UtcNow);
        const string initial = "sk-10430abcdefghijklmnopfb25";
        vault.Save("personal", "DeepSeek", initial);
        backend.FailNextMetadataDelete = true;

        Throws<IOException>(() => vault.Delete("personal"));

        Equal(initial, vault.GetForUse("personal")!);
        Equal("personal", vault.List().Single().Name);
    }

    private static void DeletesEveryClipRayCredentialDuringUninstallCleanup()
    {
        var backend = new MemoryCredentialBackend();
        var vault = new CredentialVault(backend, () => DateTimeOffset.UtcNow);
        vault.Save("personal", "DeepSeek", "sk-10430abcdefghijklmnopfb25");
        vault.Save("backup", "DeepSeek", "sk-ABCDEqrstuvwxyz123456XY99");
        backend.Write("ClipRay.Metadata.CORRUPT", "{");
        backend.Write("ClipRay.Credential.CORRUPT", "damaged-secret");

        var deletedNames = vault.DeleteAllClipRayCredentials();

        Equal(2, deletedNames.Count);
        Equal(0, vault.List().Count);
        Equal(true, vault.GetForUse("personal") is null);
        Equal(true, vault.GetForUse("backup") is null);
        Equal(true, backend.Read("ClipRay.Metadata.CORRUPT") is null);
        Equal(true, backend.Read("ClipRay.Credential.CORRUPT") is null);
    }

    private static void GeneratesCurrentUserStartupCommand()
    {
        var command = StartupRegistration.BuildCommand(@"E:\Apps\ClipRay\ClipRay.App.exe");

        Equal("\"E:\\Apps\\ClipRay\\ClipRay.App.exe\" --startup", command);
    }

    private static void ReconcilesInstallerStartupStateIntoSettings()
    {
        const string executable = @"E:\Apps\ClipRay\ClipRay.App.exe";
        Equal(true, StartupRegistration.MatchesCommand(StartupRegistration.BuildCommand(executable), executable));
        Equal(false, StartupRegistration.MatchesCommand(null, executable));
        Equal(false, StartupRegistration.MatchesCommand("\"E:\\Other\\App.exe\" --startup", executable));

        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var app = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "App.xaml.cs"));
        Equal(true, app.Contains("StartupRegistration.IsEnabled(executablePath)", StringComparison.Ordinal));
        Equal(true, app.Contains("StartWithWindows = startupEnabled", StringComparison.Ordinal));
        var startup = File.ReadAllText(Path.Combine(
            workspace, "src", "ClipRay.App", "Infrastructure", "StartupRegistration.cs"));
        Equal(true, startup.Contains("IsEnabled(executablePath) != enabled", StringComparison.Ordinal));
    }

    private static void BuildsTheExactCurrentUserCleanupTaskDefinition()
    {
        var options = new CleanupScheduleOptions(
            true,
            CleanupScheduleFrequency.Daily,
            DayOfWeek.Monday,
            new TimeOnly(3, 15));

        var definition = CleanupScheduledTaskDefinition.Build(
            options,
            @"E:\Apps\ClipRay\ClipRay.App.exe",
            new DateTime(2026, 8, 12, 4, 0, 0, DateTimeKind.Local));

        Equal(@"\ClipRay Cleanup", CleanupScheduledTaskDefinition.TaskPath);
        Equal(@"E:\Apps\ClipRay\ClipRay.App.exe", definition.ExecutablePath);
        Equal("--cleanup-scheduled", definition.Arguments);
        Equal(@"E:\Apps\ClipRay", definition.WorkingDirectory);
        Equal(new DateTime(2026, 8, 13, 3, 15, 0, DateTimeKind.Local), definition.FirstRunLocal);
        Equal(true, definition.RunOnlyWhenUserIsLoggedOn);
        Equal(true, definition.StartWhenAvailable);
        Equal(true, definition.IgnoreNewInstances);
        Equal(false, definition.RequiresElevation);
        Equal(true, definition.LaunchesExecutableDirectlyWithoutShell);
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var source = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "Infrastructure", "CleanupScheduleRegistration.cs"));
        Equal(false, source.Contains("CreateFolder(", StringComparison.Ordinal));
        Equal(true, source.Contains("root.RegisterTaskDefinition(", StringComparison.Ordinal));
        Equal(true, source.Contains("ReadStatus(options, executablePath)", StringComparison.Ordinal));

        var snapshot = new CleanupScheduledTaskSnapshot(
            Enabled: true,
            ExecutablePath: definition.ExecutablePath,
            Arguments: definition.Arguments,
            WorkingDirectory: definition.WorkingDirectory,
            Frequency: CleanupScheduleFrequency.Daily,
            WeeklyDay: null,
            DailyTime: new TimeOnly(3, 15));
        Equal(true, snapshot.Matches(options, definition.ExecutablePath));
        Equal(false, (snapshot with { DailyTime = new TimeOnly(3, 16) }).Matches(options, definition.ExecutablePath));
    }

    private static void BuildsTheSelectedWeeklyCleanupTaskDefinition()
    {
        var options = new CleanupScheduleOptions(
            true,
            CleanupScheduleFrequency.Weekly,
            DayOfWeek.Friday,
            new TimeOnly(22, 0));

        var definition = CleanupScheduledTaskDefinition.Build(
            options,
            @"E:\Apps\ClipRay\ClipRay.App.exe",
            new DateTime(2026, 8, 13, 23, 0, 0, DateTimeKind.Local));

        Equal(new DateTime(2026, 8, 14, 22, 0, 0, DateTimeKind.Local), definition.FirstRunLocal);
        Equal(CleanupScheduleFrequency.Weekly, options.Frequency);
        Equal(DayOfWeek.Friday, options.WeeklyDay);
    }

    private static void TreatsBothTaskSchedulerNotFoundExceptionShapesAsMissing()
    {
        Equal(true, WindowsCleanupScheduleRegistration.IsTaskNotFound(
            new COMException("Task does not exist.", unchecked((int)0x80070002))));
        Equal(true, WindowsCleanupScheduleRegistration.IsTaskNotFound(
            new FileNotFoundException("Task does not exist.")));
        Equal(false, WindowsCleanupScheduleRegistration.IsTaskNotFound(
            new UnauthorizedAccessException("Access denied.")));
    }

    private static void WiresCleanupLastRunStatusIntoSettings()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var xaml = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "Settings", "SettingsWindow.xaml"));
        var source = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "Settings", "SettingsWindow.xaml.cs"));

        Equal(true, xaml.Contains("x:Name=\"CleanupLastRunText\"", StringComparison.Ordinal));
        Equal(true, xaml.Contains("x:Name=\"CleanupScheduleStateText\"", StringComparison.Ordinal));
        Equal(true, xaml.Contains("x:Name=\"StartupStateText\"", StringComparison.Ordinal));
        Equal(true, source.Contains("CleanupRunStatusFormatter.Format", StringComparison.Ordinal));
        Equal(true, source.Contains("CleanupScheduleStatusFormatter.Format", StringComparison.Ordinal));
        Equal("已创建", CleanupScheduleStatusFormatter.Format(
            expectedEnabled: true,
            new CleanupScheduleRegistrationStatus(CleanupScheduleRegistrationState.Registered)));
        Equal("未创建；请保存设置重试", CleanupScheduleStatusFormatter.Format(
            expectedEnabled: true,
            new CleanupScheduleRegistrationStatus(CleanupScheduleRegistrationState.Missing)));
        Equal("未启用（任务不存在）", CleanupScheduleStatusFormatter.Format(
            expectedEnabled: false,
            new CleanupScheduleRegistrationStatus(CleanupScheduleRegistrationState.Missing)));
        Equal("配置不一致；请保存设置修复", CleanupScheduleStatusFormatter.Format(
            expectedEnabled: true,
            new CleanupScheduleRegistrationStatus(CleanupScheduleRegistrationState.Mismatched)));
        Equal("无法读取（错误 0x80070005）", CleanupScheduleStatusFormatter.Format(
            expectedEnabled: false,
            new CleanupScheduleRegistrationStatus(
                CleanupScheduleRegistrationState.Unavailable,
                unchecked((int)0x80070005))));
    }

    private static void RepairsAnEnabledCleanupScheduleDuringStartup()
    {
        using var workspace = TemporaryWorkspace.Create();
        var disabled = AppSettings.CreateDefault(workspace.Root);
        var enabled = disabled with { CleanupScheduleEnabled = true };
        var registration = new RecordingCleanupScheduleRegistration();

        Equal(true, ExternalSettingsReconciler.RepairCleanupSchedule(
            disabled,
            registration,
            @"E:\Apps\ClipRay\ClipRay.App.exe") is null);
        Equal(0, registration.Options.Count);
        Equal(true, ExternalSettingsReconciler.RepairCleanupSchedule(
            enabled,
            registration,
            @"E:\Apps\ClipRay\ClipRay.App.exe") is null);
        Equal(1, registration.Options.Count);

        var warning = ExternalSettingsReconciler.RepairCleanupSchedule(
            enabled,
            new RecordingCleanupScheduleRegistration(failOnCall: 1),
            @"E:\Apps\ClipRay\ClipRay.App.exe");
        Equal(true, warning!.Contains("自动清理计划尚未修复", StringComparison.Ordinal));
        Equal(true, warning.Contains("0x", StringComparison.Ordinal));
    }

    private static void RoutesCleanupScheduleEnableAndDisableThroughSettingsApply()
    {
        using var workspace = TemporaryWorkspace.Create();
        var prior = AppSettings.CreateDefault(workspace.Root);
        var enabled = prior with { CleanupScheduleEnabled = true, CleanupDailyTime = new TimeOnly(4, 30) };
        var disabled = enabled with { CleanupScheduleEnabled = false };
        var store = new RecordingSettingsStore();
        var cleanup = new RecordingCleanupScheduleRegistration();
        var coordinator = new SettingsApplyCoordinator(
            store,
            new RecordingStartupRegistration(),
            cleanup,
            @"E:\Apps\ClipRay\ClipRay.App.exe");

        coordinator.Apply(prior, enabled);
        coordinator.Apply(enabled, disabled);

        Equal(2, cleanup.Options.Count);
        Equal(true, cleanup.Options[0].Enabled);
        Equal(new TimeOnly(4, 30), cleanup.Options[0].DailyTime);
        Equal(false, cleanup.Options[1].Enabled);
        Equal(2, store.Saved.Count);
    }

    private static void DoesNotTouchStartupWhenOnlyCleanupChanges()
    {
        using var workspace = TemporaryWorkspace.Create();
        var prior = AppSettings.CreateDefault(workspace.Root);
        var updated = prior with { CleanupScheduleEnabled = true };
        var startup = new RecordingStartupRegistration(failOnCall: 1);
        var coordinator = new SettingsApplyCoordinator(
            new RecordingSettingsStore(),
            startup,
            new RecordingCleanupScheduleRegistration(),
            @"E:\Apps\ClipRay\ClipRay.App.exe");

        coordinator.Apply(prior, updated);

        Equal(0, startup.EnabledStates.Count);
    }

    private static void RollsBackCleanupScheduleWhenSettingsSaveFails()
    {
        using var workspace = TemporaryWorkspace.Create();
        var prior = AppSettings.CreateDefault(workspace.Root);
        var updated = prior with { CleanupScheduleEnabled = true };
        var cleanup = new RecordingCleanupScheduleRegistration();
        var coordinator = new SettingsApplyCoordinator(
            new FailingSaveSettingsStore(new IOException("injected settings write failure")),
            new RecordingStartupRegistration(),
            cleanup,
            @"E:\Apps\ClipRay\ClipRay.App.exe");

        _ = ThrowsAndReturns<SettingsApplyException>(() => coordinator.Apply(prior, updated));

        Equal(2, cleanup.Options.Count);
        Equal(true, cleanup.Options[0].Enabled);
        Equal(false, cleanup.Options[1].Enabled);
    }

    private static void SkipsTaskSchedulerWhenCleanupSettingsRemainDisabled()
    {
        using var workspace = TemporaryWorkspace.Create();
        var prior = AppSettings.CreateDefault(workspace.Root);
        var updated = prior with { CopyToClipboard = false, CleanupDailyTime = new TimeOnly(5, 45) };
        var store = new RecordingSettingsStore();
        var cleanup = new RecordingCleanupScheduleRegistration(failOnCall: 1);
        var coordinator = new SettingsApplyCoordinator(
            store,
            new RecordingStartupRegistration(),
            cleanup,
            @"E:\Apps\ClipRay\ClipRay.App.exe");

        coordinator.Apply(prior, updated);

        Equal(0, cleanup.Options.Count);
        Equal(1, store.Saved.Count);
        Equal(false, store.Saved[0].CopyToClipboard);
        Equal(new TimeOnly(5, 45), store.Saved[0].CleanupDailyTime);
    }

    private static void DoesNotSaveSettingsWhenCleanupScheduleRegistrationFails()
    {
        using var workspace = TemporaryWorkspace.Create();
        var prior = AppSettings.CreateDefault(workspace.Root);
        var updated = prior with { CleanupScheduleEnabled = true };
        var store = new RecordingSettingsStore();
        var startup = new RecordingStartupRegistration();
        var cleanup = new RecordingCleanupScheduleRegistration(failOnCall: 1);
        var coordinator = new SettingsApplyCoordinator(
            store,
            startup,
            cleanup,
            @"E:\Apps\ClipRay\ClipRay.App.exe");

        var error = ThrowsAndReturns<SettingsApplyException>(() => coordinator.Apply(prior, updated));

        Equal(0, store.Saved.Count);
        Equal(0, startup.EnabledStates.Count);
        Equal(true, error.Message.Contains("自动清理计划更新失败", StringComparison.Ordinal));
    }

    private static void RollsBackStartupWhenSettingsSaveFails()
    {
        using var workspace = TemporaryWorkspace.Create();
        var prior = AppSettings.CreateDefault(workspace.Root);
        var updated = prior with { StartWithWindows = true };
        var saveFailure = new IOException("injected settings write failure");
        var store = new FailingSaveSettingsStore(saveFailure);
        var startup = new RecordingStartupRegistration();
        var coordinator = new SettingsApplyCoordinator(store, startup, @"E:\Apps\ClipRay\ClipRay.App.exe");

        var error = ThrowsAndReturns<SettingsApplyException>(() => coordinator.Apply(prior, updated));

        Equal(2, startup.EnabledStates.Count);
        Equal(true, startup.EnabledStates[0]);
        Equal(false, startup.EnabledStates[1]);
        Equal(true, ReferenceEquals(saveFailure, error.PrimaryException));
        Equal(true, error.RollbackException is null);
    }

    private static void PreservesPrimaryErrorWhenStartupRollbackFails()
    {
        using var workspace = TemporaryWorkspace.Create();
        var prior = AppSettings.CreateDefault(workspace.Root);
        var updated = prior with { StartWithWindows = true };
        var saveFailure = new IOException("injected settings write failure");
        var startup = new RecordingStartupRegistration(failOnCall: 2);
        var coordinator = new SettingsApplyCoordinator(
            new FailingSaveSettingsStore(saveFailure),
            startup,
            @"E:\Apps\ClipRay\ClipRay.App.exe");

        var error = ThrowsAndReturns<SettingsApplyException>(() => coordinator.Apply(prior, updated));

        Equal(true, ReferenceEquals(saveFailure, error.PrimaryException));
        Equal(true, error.RollbackException is not null);
        Equal("设置保存失败，且启动项回滚失败；请重试或手动恢复启动设置。", error.Message);
    }

    private static void RecoversInvalidSettingsJsonWithWarning()
    {
        using var workspace = TemporaryWorkspace.Create();
        var path = Path.Combine(workspace.Root, "settings.json");
        File.WriteAllText(path, "{ invalid json");
        ISettingsStore store = new SettingsStore(path, workspace.Root);

        var result = store.LoadRecoverable();

        Equal(AppSettings.CreateDefault(workspace.Root), result.Settings);
        Equal("设置文件无效，已使用默认设置。", result.Warning!);
    }

    private static void RecoversPersistedHotkeyConflictsWithWarning()
    {
        using var workspace = TemporaryWorkspace.Create();
        var path = Path.Combine(workspace.Root, "settings.json");
        var invalid = AppSettings.CreateDefault(workspace.Root) with
        {
            CurrentScreenHotkey = GlobalHotkey.Parse("Ctrl+Win+A"),
        };
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(invalid));
        ISettingsStore store = new SettingsStore(path, workspace.Root);

        var result = store.LoadRecoverable();

        Equal(AppSettings.CreateDefault(workspace.Root), result.Settings);
        Equal("设置文件无效，已使用默认设置。", result.Warning!);
    }

    private static void SnapsTopmostEligibleCandidate()
    {
        var source = new StaticWindowCandidateSource(
        [
            Candidate(2, 100, 100, 400, 300),
            Candidate(3, 50, 50, 800, 600),
        ]);
        var service = new WindowSnapService(new PixelRect(0, 0, 1000, 800), source);

        var result = service.TryGetCandidate(new Point(150, 150), new IntPtr(1));

        Equal(new PixelRect(100, 100, 400, 300), result!.Value);
    }

    private static void FiltersIneligibleSnapCandidates()
    {
        var source = new StaticWindowCandidateSource(
        [
            Candidate(1, 0, 0, 1000, 800),
            Candidate(2, 0, 0, 100, 100, isClipRayOwned: true),
            Candidate(3, 0, 0, 1000, 800, isVisible: false),
            Candidate(4, 0, 0, 1000, 800, isMinimized: true),
            Candidate(5, 0, 0, 1000, 800, isCloaked: true),
            Candidate(6, 0, 0, 1000, 800, isToolWindow: true),
            Candidate(9, 0, 0, 1000, 800, windowClass: "Progman"),
            Candidate(7, 900, 100, 200, 300),
        ]);
        var service = new WindowSnapService(new PixelRect(0, 0, 1000, 800), source);

        var result = service.TryGetCandidate(new Point(950, 150), new IntPtr(1));

        Equal(new PixelRect(900, 100, 100, 300), result!.Value);
    }

    private static void SnapsOrdinaryClipRayWindowsButNotOverlay()
    {
        var source = new StaticWindowCandidateSource(
        [
            Candidate(1, 0, 0, 1000, 800, isClipRayOwned: true),
            Candidate(2, 100, 100, 500, 360, isClipRayOwned: true),
        ]);
        var service = new WindowSnapService(new PixelRect(0, 0, 1000, 800), source);

        var result = service.TryGetCandidate(new Point(150, 150), new IntPtr(1));

        Equal(new PixelRect(100, 100, 500, 360), result!.Value);
    }

    private static void SnapsWindowsTaskbars()
    {
        var primary = new WindowSnapService(
            new PixelRect(0, 0, 1920, 1080),
            new StaticWindowCandidateSource([Candidate(8, 0, 1032, 1920, 48, isToolWindow: true, windowClass: "Shell_TrayWnd")]));
        var secondary = new WindowSnapService(
            new PixelRect(1920, 0, 1920, 1080),
            new StaticWindowCandidateSource([Candidate(10, 1920, 1032, 1920, 48, isToolWindow: true, windowClass: "Shell_SecondaryTrayWnd")]));

        Equal(new PixelRect(0, 1032, 1920, 48), primary.TryGetCandidate(new Point(500, 1050), new IntPtr(1))!.Value);
        Equal(new PixelRect(1920, 1032, 1920, 48), secondary.TryGetCandidate(new Point(2500, 1050), new IntPtr(1))!.Value);
    }

    private static void FallsBackToFrozenMonitorWithoutDwmWindow()
    {
        var monitor = new PixelRect(1920, 0, 2560, 1440);
        var service = new WindowSnapService(monitor, new StaticWindowCandidateSource([]));

        Equal(monitor, service.TryGetCandidate(new Point(2500, 700), new IntPtr(1))!.Value);
    }

    private static void EnumeratesFrozenWindowSnapshotOnlyOnce()
    {
        var source = new CountingWindowCandidateSource([Candidate(2, 100, 100, 400, 300)]);
        var service = new WindowSnapService(new PixelRect(0, 0, 1000, 800), source);

        Equal(new PixelRect(100, 100, 400, 300), service.TryGetCandidate(new Point(150, 150), new IntPtr(1))!.Value);
        Equal(new PixelRect(100, 100, 400, 300), service.TryGetCandidate(new Point(200, 200), new IntPtr(1))!.Value);
        Equal(1, source.EnumerationCount);
    }

    private static void LeftPressLocksSnapProposal()
    {
        var snapped = new PixelRect(100, 80, 640, 360);
        var locked = OverlaySnapLockPolicy.Lock(snapped);
        Equal(snapped, locked.Selection);
        Equal(false, locked.IsProposal);
        Equal(true, locked.GestureLocked);
        Equal(snapped, OverlaySnapLockPolicy.Resolve(latched: true, snapped, new PixelPoint(900, 700)));
        Equal(new PixelRect(100, 80, 800, 620), OverlaySnapLockPolicy.Resolve(latched: false, snapped, new PixelPoint(900, 700)));
    }

    private static void LeftPressRejectsPendingSnapRefinements()
    {
        var interaction = new OverlaySnapInteraction();
        var request = interaction.BeginRefinement();
        Equal(true, interaction.CanAcceptRefinement(request));

        interaction.StopTracking();

        Equal(false, interaction.IsTracking);
        Equal(false, interaction.CanAcceptRefinement(request));
        Equal(false, interaction.CanAcceptRefinement(interaction.RequestVersion));
    }

    private static void KeepsRefinedSnapProposalWhilePointerRemainsInside()
    {
        var refinedElement = new PixelRect(120, 140, 220, 120);
        var immediateWindow = new PixelRect(80, 100, 700, 500);

        Equal(
            false,
            SnapProposalUpdatePolicy.ShouldReplaceWithImmediate(
                refinedElement,
                isCurrentProposal: true,
                new PixelPoint(180, 180),
                immediateWindow));
        Equal(
            true,
            SnapProposalUpdatePolicy.ShouldReplaceWithImmediate(
                refinedElement,
                isCurrentProposal: true,
                new PixelPoint(600, 400),
                immediateWindow));
    }

    private static void ClickingSnapProposalLocksSelection()
    {
        var start = new PixelPoint(320, 240);

        Equal(
            OverlaySnapPressOutcome.Pending,
            OverlaySnapInteraction.ResolveProposalPress(start, new PixelPoint(326, 245), released: false, threshold: 8));
        Equal(
            OverlaySnapPressOutcome.LockProposal,
            OverlaySnapInteraction.ResolveProposalPress(start, new PixelPoint(326, 245), released: true, threshold: 8));
    }

    private static void DraggingSnapProposalStartsFreeDraw()
    {
        var start = new PixelPoint(320, 240);

        Equal(
            OverlaySnapPressOutcome.FreeDraw,
            OverlaySnapInteraction.ResolveProposalPress(start, new PixelPoint(328, 240), released: false, threshold: 8));
    }

    private static void CanvasLeftPressStopsSnapBeforePickerRouting()
    {
        EnsureWpfResources();
        using var bitmap = CreateBitmap(16, 16, Color.Black);
        var overlay = new SelectionOverlayWindow(
            new CapturedFrame(bitmap, new Rectangle(0, 0, 16, 16)),
            new PixelRect(0, 0, 16, 16),
            clipboardTextSink: new RecordingClipboardTextSink());

        try
        {
            SetPrivateField(overlay, "_isSnapProposal", true);
            SetPrivateField(overlay, "_activeEditTool", EditTool.Picker);
            var mouse = new System.Windows.Input.MouseButtonEventArgs(
                System.Windows.Input.Mouse.PrimaryDevice,
                Environment.TickCount,
                System.Windows.Input.MouseButton.Left)
            {
                RoutedEvent = System.Windows.Input.Mouse.PreviewMouseDownEvent,
                Source = overlay.FindName("OverlayCanvas"),
            };

            ((System.Windows.UIElement)overlay.FindName("OverlayCanvas")).RaiseEvent(mouse);

            var interaction = GetPrivateField<OverlaySnapInteraction>(overlay, "_snapInteraction");
            Equal(false, interaction.IsTracking);
        }
        finally
        {
            overlay.Close();
        }
    }

    private static void ControlLeftPressStopsSnapBeforeControlRouting()
    {
        EnsureWpfResources();
        using var bitmap = CreateBitmap(16, 16, Color.Black);
        var overlay = new SelectionOverlayWindow(
            new CapturedFrame(bitmap, new Rectangle(0, 0, 16, 16)),
            new PixelRect(0, 0, 16, 16),
            clipboardTextSink: new RecordingClipboardTextSink());

        try
        {
            SetPrivateField(overlay, "_isSnapProposal", true);
            var mouse = new System.Windows.Input.MouseButtonEventArgs(
                System.Windows.Input.Mouse.PrimaryDevice,
                Environment.TickCount,
                System.Windows.Input.MouseButton.Left)
            {
                RoutedEvent = System.Windows.Input.Mouse.PreviewMouseDownEvent,
                Source = overlay.FindName("SizeButton"),
            };

            ((System.Windows.UIElement)overlay.FindName("SizeButton")).RaiseEvent(mouse);

            var interaction = GetPrivateField<OverlaySnapInteraction>(overlay, "_snapInteraction");
            Equal(false, interaction.IsTracking);
        }
        finally
        {
            overlay.Close();
        }
    }

    private static void OrdersNestedUiElementsAheadOfWindow()
    {
        var point = new Point(150, 160);
        var service = new WindowSnapService(
            new PixelRect(0, 0, 1000, 800),
            new StaticWindowCandidateSource([Candidate(2, 80, 100, 700, 500)]),
            new StaticUiHierarchySource([
                new PixelRect(120, 140, 220, 120),
                new PixelRect(80, 100, 700, 500),
            ]));

        var candidates = service.GetCandidates(point, new IntPtr(1), SnapDetectionMode.ElementFirst);

        Equal(2, candidates.Count);
        Equal(new PixelRect(120, 140, 220, 120), candidates[0]);
        Equal(new PixelRect(80, 100, 700, 500), candidates[1]);
    }

    private static void PrefersTopWindowWhenRequested()
    {
        var point = new Point(150, 160);
        var service = new WindowSnapService(
            new PixelRect(0, 0, 1000, 800),
            new StaticWindowCandidateSource([Candidate(2, 80, 100, 700, 500)]),
            new StaticUiHierarchySource([new PixelRect(120, 140, 220, 120)]));

        var candidates = service.GetCandidates(point, new IntPtr(1), SnapDetectionMode.WindowFirst);

        Equal(new PixelRect(80, 100, 700, 500), candidates[0]);
        Equal(new PixelRect(120, 140, 220, 120), candidates[1]);
    }

    private static void FiltersInvalidNestedUiElementRectangles()
    {
        var point = new Point(150, 160);
        var service = new WindowSnapService(
            new PixelRect(0, 0, 1000, 800),
            new StaticWindowCandidateSource([Candidate(2, 80, 100, 700, 500)]),
            new StaticUiHierarchySource([
                new PixelRect(double.NaN, 0, 10, 10),
                new PixelRect(120, 140, 0, 120),
                new PixelRect(700, 700, 50, 50),
                new PixelRect(120, 140, 220, 120),
            ]));

        var candidates = service.GetCandidates(point, new IntPtr(1), SnapDetectionMode.ElementFirst);

        Equal(2, candidates.Count);
        Equal(new PixelRect(120, 140, 220, 120), candidates[0]);
        Equal(new PixelRect(80, 100, 700, 500), candidates[1]);
    }

    private static void UsesOuterWindowSnappingUnlessCtrlIsHeld()
    {
        var policyType = typeof(WindowSnapService).Assembly.GetType(
            "ClipRay.App.Overlay.SnapDetectionModePolicy",
            throwOnError: true)!;
        var resolve = policyType.GetMethod(
            "Resolve",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!;
        SnapDetectionMode Resolve(System.Windows.Input.ModifierKeys modifiers) =>
            (SnapDetectionMode)resolve.Invoke(null, [modifiers])!;

        Equal(
            SnapDetectionMode.WindowFirst,
            Resolve(System.Windows.Input.ModifierKeys.None));
        Equal(
            SnapDetectionMode.ElementFirst,
            Resolve(System.Windows.Input.ModifierKeys.Control));
        Equal(
            SnapDetectionMode.ElementFirst,
            Resolve(
                System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Alt));
        Equal(
            SnapDetectionMode.WindowFirst,
            Resolve(System.Windows.Input.ModifierKeys.Alt));

        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var xaml = File.ReadAllText(Path.Combine(
            workspace,
            "src",
            "ClipRay.App",
            "Overlay",
            "SelectionOverlayWindow.xaml"));
        var source = File.ReadAllText(Path.Combine(
            workspace,
            "src",
            "ClipRay.App",
            "Overlay",
            "SelectionOverlayWindow.xaml.cs"));
        Equal(true, xaml.Contains("PreviewKeyUp=\"OnPreviewKeyUp\"", StringComparison.Ordinal));
        Equal(true, source.Contains("UpdateSnapDetectionMode(modifiers)", StringComparison.Ordinal));
        Equal(true, source.Contains("UpdateSnapDetectionMode(HotkeyCapturePolicy.ReadCurrentModifiers())", StringComparison.Ordinal));
        Equal(true, source.Contains("key is Key.LeftCtrl or Key.RightCtrl", StringComparison.Ordinal));
    }

    private static void MentionsMomentaryCtrlNestedSnapModeInSettings()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var settings = File.ReadAllText(Path.Combine(
            workspace,
            "src",
            "ClipRay.App",
            "Settings",
            "SettingsWindow.xaml"));

        Equal(
            true,
            settings.Contains(
                "区域截图默认吸附外层窗口；按住 Ctrl 可临时吸附窗口内控件。",
                StringComparison.Ordinal));
    }

    private static void RedrawsInsideLockedSelectionAfterMaterialDrag()
    {
        Equal(OverlaySelectionGesture.None, OverlaySelectionGesturePolicy.Resolve(
            hasSelection: true,
            moveToolActive: false,
            onBorder: false,
            onResizeHandle: false,
            isSnapProposal: false,
            dragDistance: 7.9));
        Equal(OverlaySelectionGesture.Draw, OverlaySelectionGesturePolicy.Resolve(
            hasSelection: true,
            moveToolActive: false,
            onBorder: false,
            onResizeHandle: false,
            isSnapProposal: false,
            dragDistance: 8));
        Equal(OverlaySelectionGesture.Move, OverlaySelectionGesturePolicy.Resolve(
            hasSelection: true,
            moveToolActive: true,
            onBorder: true,
            onResizeHandle: false,
            isSnapProposal: false,
            dragDistance: 8));
        Equal(OverlaySelectionGesture.Resize, OverlaySelectionGesturePolicy.Resolve(
            hasSelection: true,
            moveToolActive: false,
            onBorder: true,
            onResizeHandle: true,
            isSnapProposal: false,
            dragDistance: 8));
    }

    private static void DoubleEscapeClosesInsideWindow()
    {
        var policy = new DoubleEscapePolicy();
        var start = DateTimeOffset.UnixEpoch;

        Equal(DoubleEscapeResult.ShowHint, policy.Press(start));
        Equal(DoubleEscapeResult.Close, policy.Press(start.AddMilliseconds(1499)));
    }

    private static void DoubleEscapeExpiresAndResets()
    {
        var policy = new DoubleEscapePolicy();
        var start = DateTimeOffset.UnixEpoch;

        Equal(DoubleEscapeResult.ShowHint, policy.Press(start));
        Equal(DoubleEscapeResult.ShowHint, policy.Press(start.AddMilliseconds(1500)));
        policy.Reset();
        Equal(DoubleEscapeResult.ShowHint, policy.Press(start.AddMilliseconds(1501)));
    }

    private static void DefinesCompleteToolbarShell()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var xaml = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "Overlay", "SelectionOverlayWindow.xaml"));
        var commands = new[]
        {
            "OverlayTool.Move", "OverlayTool.Pen", "OverlayTool.Mosaic", "OverlayTool.Picker", "OverlayTool.LongCapture",
            "OverlayTool.Translate", "OverlayTool.ExtractText", "OverlayTool.Record", "OverlayTool.Cancel", "OverlayTool.Confirm",
        };
        var toolbarStart = xaml.IndexOf("<Border x:Name=\"Toolbar\"", StringComparison.Ordinal);
        Equal(true, toolbarStart >= 0);
        var toolbar = xaml[toolbarStart..];
        var previous = -1;
        foreach (var command in commands)
        {
            var position = toolbar.IndexOf(command, StringComparison.Ordinal);
            Equal(true, position > previous);
            previous = position;
        }

        foreach (var tooltip in new[] { "移动（锁定尺寸）", "画笔", "马赛克", "取色", "长截图", "翻译（右键临时切换模型）", "文字提取", "录屏", "取消", "确认（Enter）" })
        {
            Equal(true, xaml.Contains($"ToolTip=\"{tooltip}\"", StringComparison.Ordinal));
        }

        Equal(true, xaml.Contains("<Setter Property=\"Width\" Value=\"44\" />", StringComparison.Ordinal));
        Equal(true, xaml.Contains("<Setter Property=\"Height\" Value=\"42\" />", StringComparison.Ordinal));
        Equal(true, xaml.Contains("Width=\"52\"", StringComparison.Ordinal));
        Equal(true, xaml.Contains("Width=\"28\" Height=\"28\"", StringComparison.Ordinal));
        Equal(false, xaml.Contains("Width=\"196\"", StringComparison.Ordinal));
    }

    private static void UsesLockedFramelessToolbarVisualLanguage()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var xaml = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "Overlay", "SelectionOverlayWindow.xaml"));

        Equal(true, xaml.Contains("<Setter Property=\"Background\" Value=\"Transparent\" />", StringComparison.Ordinal));
        Equal(true, xaml.Contains("<Setter Property=\"BorderThickness\" Value=\"0\" />", StringComparison.Ordinal));
        Equal(true, xaml.Contains("x:Name=\"PenGlyph\"", StringComparison.Ordinal));
        Equal(true, xaml.Contains("x:Name=\"PickerGlyph\"", StringComparison.Ordinal));
        Equal(true, xaml.Contains("x:Name=\"LongCaptureGlyph\"", StringComparison.Ordinal));
        Equal(true, xaml.Contains("x:Name=\"TranslateGlyph\"", StringComparison.Ordinal));
        Equal(true, xaml.Contains("x:Name=\"RecordGlyph\"", StringComparison.Ordinal));
    }

    private static void UsesPreciseConsistentToolbarGlyphs()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var xaml = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "Overlay", "SelectionOverlayWindow.xaml"));

        Equal(true, xaml.Contains("x:Name=\"PickerGlyph\"", StringComparison.Ordinal));
        Equal(true, xaml.Contains("Data=\"M12,9 L3.586,17.414", StringComparison.Ordinal));
        Equal(true, xaml.Contains("x:Name=\"TranslateGlyph\"", StringComparison.Ordinal));
        Equal(true, xaml.Contains("Data=\"M5,8 L11,14", StringComparison.Ordinal));
        Equal(true, xaml.Contains("x:Name=\"RecordGlyph\"", StringComparison.Ordinal));
        Equal(true, xaml.Contains("x:Name=\"RecordGlyph\" Fill=\"#EDF1FA\"", StringComparison.Ordinal));
        Equal(true, xaml.Contains("Data=\"F0 M5.25,5 A3.25,3.25", StringComparison.Ordinal));
        Equal(false, xaml.Contains("x:Name=\"RecordBody\"", StringComparison.Ordinal));
        Equal(true, xaml.Split("StrokeThickness=\"2\"", StringSplitOptions.None).Length >= 7);
        Equal(false, xaml.Contains("Text=\"中\"", StringComparison.Ordinal));
        Equal(false, xaml.Contains("Text=\"A\"", StringComparison.Ordinal));
    }

    private static void MapsPenPointsFromSourcePixelsIntoSelectedOutput()
    {
        using var source = CreateBitmap(7, 7, Color.Black);
        var selection = new PixelRect(2, 3, 4, 3);
        var layer = new EditLayer(selection);
        layer.AddPenStroke(new PenStroke([new PixelPoint(3, 4)], Color.Red, 1));

        using var composited = EditRenderer.Composite(source, layer);

        Equal(Color.Black.ToArgb(), source.GetPixel(3, 4).ToArgb());
        Equal(4, composited.Width);
        Equal(3, composited.Height);
        Equal(Color.Red.ToArgb(), composited.GetPixel(1, 1).ToArgb());
    }

    private static void DefinesCompactRetainedEditingControls()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var xaml = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "Overlay", "SelectionOverlayWindow.xaml"));

        foreach (var name in new[] { "EditPreview", "ToolProperties", "PenProperties", "MosaicProperties", "PenColorInput", "PenWidthInput", "MosaicBrushInput", "MosaicBlockInput" })
        {
            Equal(true, xaml.Contains($"x:Name=\"{name}\"", StringComparison.Ordinal));
        }
    }

    private static void AppliesMosaicOnlyInsideTouchedBlockBoundaries()
    {
        using var source = new Bitmap(8, 8);
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                source.SetPixel(x, y, Color.FromArgb(255, x * 20, y * 20, x + y));
            }
        }

        var layer = new EditLayer(new PixelRect(0, 0, 8, 8));
        layer.AddMosaicStroke(new MosaicStroke([new PixelPoint(2, 2)], brushSize: 6, blockSize: 4));

        using var composited = EditRenderer.Composite(source, layer);

        Equal(source.GetPixel(0, 0).ToArgb(), composited.GetPixel(2, 2).ToArgb());
        Equal(source.GetPixel(5, 3).ToArgb(), composited.GetPixel(5, 3).ToArgb());
        Equal(source.GetPixel(3, 5).ToArgb(), composited.GetPixel(3, 5).ToArgb());
    }

    private static void ClipsRetainedEditsToTheSelectedRegion()
    {
        using var source = CreateBitmap(7, 7, Color.Black);
        var layer = new EditLayer(new PixelRect(2, 2, 3, 3));
        layer.AddPenStroke(new PenStroke([new PixelPoint(0, 0)], Color.Red, 3));

        using var composited = EditRenderer.Composite(source, layer);

        Equal(Color.Black.ToArgb(), source.GetPixel(0, 0).ToArgb());
        Equal(Color.Black.ToArgb(), composited.GetPixel(0, 0).ToArgb());
        Equal(Color.Black.ToArgb(), composited.GetPixel(2, 2).ToArgb());
    }

    private static void KeepsRetainedEditsWhenSwitchingTools()
    {
        var layer = new EditLayer(new PixelRect(0, 0, 6, 6));
        layer.SwitchTool(EditTool.Pen);
        layer.AddPenStroke(new PenStroke([new PixelPoint(1, 1)], Color.Red, 1));
        layer.SwitchTool(EditTool.Mosaic);
        layer.AddMosaicStroke(new MosaicStroke([new PixelPoint(4, 4)], brushSize: 1, blockSize: 2));

        Equal(EditTool.Mosaic, layer.ActiveTool);
        Equal(1, layer.PenStrokes.Count);
        Equal(1, layer.MosaicStrokes.Count);
    }

    private static void SamplesTheExactFrozenSourceColor()
    {
        using var source = CreateBitmap(3, 3, Color.Black);
        source.SetPixel(1, 2, Color.FromArgb(255, 0x0A, 0x1B, 0x2C));
        var layer = new EditLayer(new PixelRect(0, 0, 3, 3));

        var value = layer.SampleSourceColor(source, new PixelPoint(1, 2));

        Equal("#0A1B2C", value);
        Equal(Color.FromArgb(255, 0x0A, 0x1B, 0x2C).ToArgb(), layer.PenColor.ToArgb());
    }

    private static void CompositesRetainedEditsInDrawingOrder()
    {
        using var source = CreateBitmap(4, 4, Color.Black);
        var layer = new EditLayer(new PixelRect(0, 0, 4, 4));
        layer.AddMosaicStroke(new MosaicStroke([new PixelPoint(2, 2)], brushSize: 4, blockSize: 4));
        layer.AddPenStroke(new PenStroke([new PixelPoint(2, 2)], Color.Red, 1));

        using var composited = EditRenderer.Composite(source, layer);

        Equal(Color.Red.ToArgb(), composited.GetPixel(2, 2).ToArgb());
    }

    private static void CompositesALaterMosaicAfterPen()
    {
        using var source = CreateBitmap(4, 4, Color.Black);
        var layer = new EditLayer(new PixelRect(0, 0, 4, 4));
        layer.AddPenStroke(new PenStroke([new PixelPoint(2, 2)], Color.Red, 1));
        layer.AddMosaicStroke(new MosaicStroke([new PixelPoint(2, 2)], brushSize: 4, blockSize: 4));

        using var composited = EditRenderer.Composite(source, layer);

        Equal(Color.Black.ToArgb(), composited.GetPixel(2, 2).ToArgb());
    }

    private static void PreservesRetainedEditsWhenTheSelectionIsAdjusted()
    {
        using var source = CreateBitmap(5, 5, Color.Black);
        var layer = new EditLayer(new PixelRect(0, 0, 3, 3));
        layer.AddPenStroke(new PenStroke([new PixelPoint(1, 1)], Color.Red, 1));

        layer.UpdateSelection(new PixelRect(1, 1, 3, 3));
        using var composited = EditRenderer.Composite(source, layer);

        Equal(1, layer.PenStrokes.Count);
        Equal(Color.Red.ToArgb(), composited.GetPixel(0, 0).ToArgb());
    }

    private static void KeepsResizeHandlesAheadOfActiveEditing()
    {
        Equal(false, OverlayInputPolicy.ShouldBeginEdit(EditTool.Pen, isResizeHandle: true, isInsideSelection: true));
        Equal(true, OverlayInputPolicy.ShouldBeginEdit(EditTool.Pen, isResizeHandle: false, isInsideSelection: true));
        Equal(false, OverlayInputPolicy.ShouldBeginEdit(EditTool.Mosaic, isResizeHandle: false, isInsideSelection: false));
    }

    private static void AppendsOneClampedFinalPointWhenAStrokeEnds()
    {
        var points = new List<PixelPoint> { new(1, 1) };
        var selection = new PixelRect(0, 0, 4, 4);

        StrokePointCollection.AppendClampedDistinct(points, new PixelPoint(9, -2), selection);
        StrokePointCollection.AppendClampedDistinct(points, new PixelPoint(4, 0), selection);

        Equal(2, points.Count);
        Equal(new PixelPoint(Math.BitDecrement(4), 0), points[1]);
    }

    private static void DrawsAPenStrokeCrossingTheSelectedBoundary()
    {
        using var source = CreateBitmap(7, 7, Color.Black);
        var layer = new EditLayer(new PixelRect(2, 2, 3, 3));
        layer.AddPenStroke(new PenStroke([new PixelPoint(0, 3), new PixelPoint(6, 3)], Color.Red, 1));

        using var composited = EditRenderer.Composite(source, layer);

        Equal(Color.Black.ToArgb(), source.GetPixel(2, 3).ToArgb());
        Equal(Color.Red.ToArgb(), composited.GetPixel(0, 1).ToArgb());
        Equal(Color.Red.ToArgb(), composited.GetPixel(2, 1).ToArgb());
    }

    private static void ClipsAMosaicBlockToAPartialSelection()
    {
        using var source = CreateBitmap(6, 6, Color.Black);
        source.SetPixel(0, 0, Color.Gold);
        source.SetPixel(4, 4, Color.Blue);
        var layer = new EditLayer(new PixelRect(2, 2, 3, 3));
        layer.AddMosaicStroke(new MosaicStroke([new PixelPoint(3, 3)], brushSize: 6, blockSize: 4));

        using var composited = EditRenderer.Composite(source, layer);

        Equal(Color.Gold.ToArgb(), composited.GetPixel(0, 0).ToArgb());
        Equal(Color.Gold.ToArgb(), composited.GetPixel(1, 1).ToArgb());
        Equal(Color.Blue.ToArgb(), composited.GetPixel(2, 2).ToArgb());
        Equal(Color.Blue.ToArgb(), source.GetPixel(4, 4).ToArgb());
    }

    private static void PickerSeamSamplesTheSourceBitmapAndCopiesUppercaseText()
    {
        using var source = CreateBitmap(3, 3, Color.Black);
        source.SetPixel(2, 1, Color.FromArgb(255, 0x0A, 0x1B, 0x2C));
        var layer = new EditLayer(new PixelRect(0, 0, 3, 3));
        var clipboard = new RecordingClipboardTextSink();
        var picker = new SourceColorPicker(layer, clipboard, _ => { });

        var result = picker.Pick(source, new PixelPoint(2, 1));

        Equal("#0A1B2C", result.Text);
        Equal(true, result.Copied);
        Equal("#0A1B2C", clipboard.Text!);
        Equal(Color.FromArgb(255, 0x0A, 0x1B, 0x2C).ToArgb(), layer.PenColor.ToArgb());
    }

    private static void PickerSeamKeepsTheSelectedPenColorWhenClipboardCopyFails()
    {
        using var source = CreateBitmap(2, 2, Color.Black);
        source.SetPixel(1, 1, Color.FromArgb(255, 0xAB, 0xCD, 0xEF));
        var layer = new EditLayer(new PixelRect(0, 0, 2, 2));
        var clipboard = new FailingClipboardTextSink();
        var picker = new SourceColorPicker(layer, clipboard, _ => { });

        var result = picker.Pick(source, new PixelPoint(1, 1));

        Equal("#ABCDEF", result.Text);
        Equal(false, result.Copied);
        Equal(3, clipboard.Attempts);
        Equal(Color.FromArgb(255, 0xAB, 0xCD, 0xEF).ToArgb(), layer.PenColor.ToArgb());
    }

    private static void PickerMarksClipboardTextWithProvenance()
    {
        using var source = CreateBitmap(2, 2, Color.Black);
        var layer = new EditLayer(new PixelRect(0, 0, 2, 2));
        var clipboard = new RecordingColorClipboardTextSink();
        var picker = new SourceColorPicker(layer, clipboard, _ => { });

        picker.Pick(source, new PixelPoint(1, 1));
        picker.Pick(source, new PixelPoint(0, 0));

        Equal(2, clipboard.ColorWrites);
        Equal(0, clipboard.PlainWrites);
        Equal("#000000", clipboard.Text!);
    }

    private static void RefreshesTheWpfPreviewAfterMovingAndResizingAnEditedSelection()
    {
        EnsureWpfResources();
        using var bitmap = CreateBitmap(9, 9, Color.Black);
        var oldSelection = new PixelRect(0, 0, 4, 4);
        var layer = new EditLayer(oldSelection);
        layer.AddPenStroke(new PenStroke([new PixelPoint(2, 2)], Color.Red, 1));
        var overlay = new SelectionOverlayWindow(new CapturedFrame(bitmap, new Rectangle(0, 0, 9, 9)), oldSelection);

        try
        {
            SetPrivateField(overlay, "_editLayer", layer);
            InvokePrivate(overlay, "UpdateEditPreview");

            var movedSelection = new PixelRect(1, 1, 4, 4);
            SetPrivateField(overlay, "_selection", movedSelection);
            InvokePrivate(overlay, "UpdateVisuals");
            AssertPreviewMatches(overlay, bitmap, layer, Color.Red, new System.Drawing.Point(1, 1));

            var resizedSelection = new PixelRect(1, 1, 6, 5);
            SetPrivateField(overlay, "_selection", resizedSelection);
            InvokePrivate(overlay, "UpdateVisuals");
            AssertPreviewMatches(overlay, bitmap, layer, Color.Red, new System.Drawing.Point(1, 1));
        }
        finally
        {
            overlay.Close();
        }
    }

    private static void PickerInputRouterUsesAnInSelectionSourceCoordinateAndClipboardSuccess()
    {
        using var source = CreateBitmap(6, 6, Color.Black);
        source.SetPixel(3, 4, Color.FromArgb(255, 0x12, 0x34, 0x56));
        var selection = new PixelRect(1, 2, 4, 3);
        var layer = new EditLayer(selection);
        var clipboard = new RecordingClipboardTextSink();

        var routed = OverlayPickerInputRouter.TryRoutePicker(
            tool: EditTool.Picker,
            selection: selection,
            point: new PixelPoint(3, 4),
            isResizeHandle: false,
            source: source,
            layer: layer,
            clipboard: clipboard,
            result: out var result,
            retryDelay: _ => { });

        Equal(true, routed);
        Equal("#123456", result.Text);
        Equal(true, result.Copied);
        Equal("#123456", clipboard.Text!);
    }

    private static void PickerInputRouterPreservesTheSourceColorAfterClipboardFailure()
    {
        using var source = CreateBitmap(6, 6, Color.Black);
        source.SetPixel(2, 3, Color.FromArgb(255, 0x65, 0x43, 0x21));
        var selection = new PixelRect(1, 2, 4, 3);
        var layer = new EditLayer(selection);
        var clipboard = new FailingClipboardTextSink();

        var routed = OverlayPickerInputRouter.TryRoutePicker(
            tool: EditTool.Picker,
            selection: selection,
            point: new PixelPoint(2, 3),
            isResizeHandle: false,
            source: source,
            layer: layer,
            clipboard: clipboard,
            result: out var result,
            retryDelay: _ => { });

        Equal(true, routed);
        Equal("#654321", result.Text);
        Equal(false, result.Copied);
        Equal(3, clipboard.Attempts);
        Equal(Color.FromArgb(255, 0x65, 0x43, 0x21).ToArgb(), layer.PenColor.ToArgb());

        Equal(false, OverlayPickerInputRouter.TryRoutePicker(
            tool: EditTool.Picker,
            selection: selection,
            point: new PixelPoint(2, 3),
            isResizeHandle: true,
            source: source,
            layer: layer,
            clipboard: clipboard,
            result: out _,
            retryDelay: _ => { }));
    }

    private static void PickerInputRouterAcceptsTheInclusiveLeftTopEdge()
    {
        using var source = CreateBitmap(7, 7, Color.Black);
        source.SetPixel(1, 2, Color.FromArgb(255, 0x11, 0x22, 0x33));
        var selection = new PixelRect(1, 2, 4, 3);
        var clipboard = new RecordingClipboardTextSink();

        var routed = OverlayPickerInputRouter.TryRoutePicker(
            tool: EditTool.Picker,
            selection: selection,
            point: new PixelPoint(1, 2),
            isResizeHandle: false,
            source: source,
            layer: new EditLayer(selection),
            clipboard: clipboard,
            result: out var result,
            retryDelay: _ => { });

        Equal(true, routed);
        Equal("#112233", result.Text);
    }

    private static void PickerInputRouterRejectsTheExclusiveRightEdge()
    {
        using var source = CreateBitmap(7, 7, Color.Black);
        source.SetPixel(5, 3, Color.FromArgb(255, 0xAA, 0x00, 0x00));
        var selection = new PixelRect(1, 2, 4, 3);
        var clipboard = new RecordingClipboardTextSink();

        var routed = OverlayPickerInputRouter.TryRoutePicker(
            tool: EditTool.Picker,
            selection: selection,
            point: new PixelPoint(5, 3),
            isResizeHandle: false,
            source: source,
            layer: new EditLayer(selection),
            clipboard: clipboard,
            result: out _,
            retryDelay: _ => { });

        Equal(false, routed);
        Equal(true, clipboard.Text is null);
    }

    private static void PickerInputRouterRejectsTheExclusiveBottomEdge()
    {
        using var source = CreateBitmap(7, 7, Color.Black);
        source.SetPixel(3, 5, Color.FromArgb(255, 0x00, 0xAA, 0x00));
        var selection = new PixelRect(1, 2, 4, 3);
        var clipboard = new RecordingClipboardTextSink();

        var routed = OverlayPickerInputRouter.TryRoutePicker(
            tool: EditTool.Picker,
            selection: selection,
            point: new PixelPoint(3, 5),
            isResizeHandle: false,
            source: source,
            layer: new EditLayer(selection),
            clipboard: clipboard,
            result: out _,
            retryDelay: _ => { });

        Equal(false, routed);
        Equal(true, clipboard.Text is null);
    }

    private static void PickerInputRouterAcceptsPixelsJustInsideRightAndBottom()
    {
        using var source = CreateBitmap(7, 7, Color.Black);
        source.SetPixel(4, 4, Color.FromArgb(255, 0x44, 0x55, 0x66));
        var selection = new PixelRect(1, 2, 4, 3);
        var clipboard = new RecordingClipboardTextSink();

        var routed = OverlayPickerInputRouter.TryRoutePicker(
            tool: EditTool.Picker,
            selection: selection,
            point: new PixelPoint(4, 4),
            isResizeHandle: false,
            source: source,
            layer: new EditLayer(selection),
            clipboard: clipboard,
            result: out var result,
            retryDelay: _ => { });

        Equal(true, routed);
        Equal("#445566", result.Text);
    }

    private static void SendsOnlyTheTranslationRequestAllowList()
    {
        const string secret = "unit-test-key-never-print";
        var vault = new RecordingCredentialVault(secret);
        var handler = new RecordingHttpHandler(HttpStatusCode.OK, JsonTranslation("[{\"id\":\"b0\",\"translation\":\"你好\"}]"));
        var provider = new DeepSeekTranslationProvider(vault, handler, TimeSpan.FromSeconds(2));

        var result = provider.TranslateAsync(
            new TranslationRequest("English", "简体中文", "Hello", DeepSeekModels.Flash),
            "personal-deepseek",
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(true, result.IsSuccess);
        Equal(new Uri("https://api.deepseek.com/chat/completions"), handler.RequestUri!);
        Equal(HttpMethod.Post, handler.Method!);
        Equal(1, vault.GetForUseCalls);
        Equal(false, handler.Body!.Contains("image", StringComparison.OrdinalIgnoreCase));
        Equal(false, handler.Body.Contains("bounds", StringComparison.OrdinalIgnoreCase));
        Equal(false, handler.Body.Contains("credential", StringComparison.OrdinalIgnoreCase));
        foreach (var forbidden in new[] { "base64", "windowTitle", "processName", "sourcePath", "screenshotPath", "credentialName", "userId", "pixelBounds" })
        {
            Equal(false, handler.Body.Contains(forbidden, StringComparison.OrdinalIgnoreCase));
        }
        Equal(false, handler.Body.Contains(secret, StringComparison.Ordinal));
        using var json = System.Text.Json.JsonDocument.Parse(handler.Body);
        Equal("deepseek-flash", json.RootElement.GetProperty("model").GetString()!);
        Equal(7, json.RootElement.EnumerateObject().Count());
        Equal(true, json.RootElement.EnumerateObject().Select(static property => property.Name).SequenceEqual([
            "model", "messages", "thinking", "response_format", "temperature", "stream", "stream_options",
        ]));
        Equal(true, json.RootElement.GetProperty("stream_options").GetProperty("include_usage").GetBoolean());
        Equal(1, json.RootElement.GetProperty("stream_options").EnumerateObject().Count());
        foreach (var message in json.RootElement.GetProperty("messages").EnumerateArray())
        {
            Equal(true, message.EnumerateObject().Select(static property => property.Name).SequenceEqual(["role", "content"]));
        }
    }

    private static void PreservesIdentityAndBrandNamesInDeepSeekPrompt()
    {
        var handler = new RecordingHttpHandler(HttpStatusCode.OK, JsonTranslation("{\"b0\":\"bonjour\"}"));
        var provider = new DeepSeekTranslationProvider(new RecordingCredentialVault("safe"), handler);
        _ = provider.TranslateAsync(
            new TranslationRequest("English", "French", "hello"),
            "alias",
            CancellationToken.None).GetAwaiter().GetResult();
        using var payload = System.Text.Json.JsonDocument.Parse(handler.Body!);
        var system = payload.RootElement.GetProperty("messages")[0].GetProperty("content").GetString()!;

        Equal(true, system.Contains("@handle", StringComparison.Ordinal));
        Equal(true, system.Contains("make the display name readable", StringComparison.Ordinal));
        Equal(true, system.Contains("brand names", StringComparison.Ordinal));
        Equal(true, system.Contains("product names", StringComparison.Ordinal));
        Equal(true, system.Contains("trademark", StringComparison.Ordinal));
        Equal(true, system.Contains("already written in Latin", StringComparison.Ordinal));
        Equal(true, system.Contains("non-target-script proper names", StringComparison.Ordinal));
    }

    private static void UsesLowVarianceSamplingForDeepSeekTranslations()
    {
        var handler = new RecordingHttpHandler(HttpStatusCode.OK, JsonTranslation("{\"b0\":\"bonjour\"}"));
        var provider = new DeepSeekTranslationProvider(new RecordingCredentialVault("safe"), handler);
        _ = provider.TranslateAsync(
            new TranslationRequest("English", "French", "hello"),
            "alias",
            CancellationToken.None).GetAwaiter().GetResult();

        using var payload = System.Text.Json.JsonDocument.Parse(handler.Body!);
        Equal(0.2d, payload.RootElement.GetProperty("temperature").GetDouble());
        Equal(false, payload.RootElement.TryGetProperty("top_p", out _));
    }

    private static void SendsVisualTranslationSemanticsWithoutScreenshotData()
    {
        var regions = new[]
        {
            VisualRegion("r0", "くまの_よなよな @kumano_yonayona·6h", VisualTextRole.Metadata),
            VisualRegion("r1", "Show translation", VisualTextRole.Control),
            VisualRegion("r2", "アイコンの子のまばたき！！", VisualTextRole.Control),
            VisualRegion("r3", "＃イラスト", VisualTextRole.Control),
        };
        var requests = VisualTranslationRequestFactory.Create(regions, "简体中文", DeepSeekModels.Flash);
        string? initialRequestBody = null;
        var handler = new RecordingHttpHandler(async (request, cancellationToken) =>
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            initialRequestBody ??= body;
            using var requestJson = System.Text.Json.JsonDocument.Parse(body);
            var promptText = requestJson.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!;
            using var promptJson = System.Text.Json.JsonDocument.Parse(promptText);
            Dictionary<string, string> translations;
            if (promptJson.RootElement.EnumerateObject().Count() == 1)
            {
                translations = new Dictionary<string, string> { ["b0"] = "头像里的角色眨眼！！" };
            }
            else
            {
                var protectedIdentity = promptJson.RootElement.GetProperty("b0").GetProperty("text").GetString()!;
                var translatedIdentity = protectedIdentity.Replace("くまの_よなよな", "Kumano_Yonayona（くまの_よなよな）", StringComparison.Ordinal)
                    .Replace("6h", "6小时前", StringComparison.Ordinal);
                translations = new Dictionary<string, string>
                {
                    ["b0"] = translatedIdentity,
                    ["b1"] = "显示翻译",
                    ["b2"] = "头像里的角色眨眼！！",
                    ["b3"] = "＃插画",
                };
            }
            var response = System.Text.Json.JsonSerializer.Serialize(translations);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonTranslation(response), Encoding.UTF8, "application/json"),
            };
        });
        var provider = new DeepSeekTranslationProvider(new RecordingCredentialVault("safe"), handler);
        _ = provider.TranslateBlocksAsync(requests, "alias", CancellationToken.None).GetAwaiter().GetResult();

        using var payload = System.Text.Json.JsonDocument.Parse(initialRequestBody!);
        var user = payload.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!;
        using var prompt = System.Text.Json.JsonDocument.Parse(user);
        Equal("social_media", prompt.RootElement.GetProperty("b0").GetProperty("scene").GetString()!);
        Equal("identity_metadata", prompt.RootElement.GetProperty("b0").GetProperty("text_kind").GetString()!);
        Equal("ui_control", prompt.RootElement.GetProperty("b1").GetProperty("text_kind").GetString()!);
        Equal("body", prompt.RootElement.GetProperty("b2").GetProperty("text_kind").GetString()!);
        Equal("hashtag", prompt.RootElement.GetProperty("b3").GetProperty("text_kind").GetString()!);
        Equal(false, handler.Body!.Contains("image", StringComparison.OrdinalIgnoreCase));
        Equal(false, handler.Body.Contains("bounds", StringComparison.OrdinalIgnoreCase));
    }

    private static void UsesReadableVisualTranslationPolicies()
    {
        var regions = new[]
        {
            VisualRegion("r0", "くまの_よなよな @kumano_yonayona·6h", VisualTextRole.Metadata),
            VisualRegion("r1", "Show translation", VisualTextRole.Control),
            VisualRegion("r2", "＃イラスト", VisualTextRole.Control),
        };

        var requests = VisualTranslationRequestFactory.Create(regions, "简体中文", DeepSeekModels.Flash);

        Equal(TranslationTextKind.IdentityMetadata, requests[0].TextKind);
        Equal(true, requests[0].TranslationHint.Contains("Romanize", StringComparison.Ordinal));
        Equal(true, requests[0].TranslationHint.Contains("relative time", StringComparison.Ordinal));
        Equal(TranslationTextKind.UiControl, requests[1].TextKind);
        Equal(true, requests[1].TranslationHint.Contains("UI label", StringComparison.Ordinal));
        Equal(TranslationTextKind.Hashtag, requests[2].TextKind);
        Equal(true, requests[2].TranslationHint.Contains("leading hashtag", StringComparison.Ordinal));
    }

    private static void TreatsShortPunctuatedScreenTextAsBodyContent()
    {
        var request = VisualTranslationRequestFactory.Create(
            [VisualRegion("r0", "アイコンの子のまばたき！！", VisualTextRole.Control)],
            "简体中文",
            DeepSeekModels.Flash)[0];

        Equal(TranslationTextKind.Body, request.TextKind);
        Equal("screen_capture", request.Scene);
    }

    private static void TreatsSeparatedKatakanaNamesAsProperNameHeadings()
    {
        var properName = VisualTranslationRequestFactory.Create(
            [VisualRegion("r0", "コミックス・ウェーブ・フィルム", VisualTextRole.Control)],
            "简体中文",
            DeepSeekModels.Flash)[0];
        var ordinarySentence = VisualTranslationRequestFactory.Create(
            [VisualRegion("r0", "アイコンの子がまばたきしています。", VisualTextRole.Control)],
            "简体中文",
            DeepSeekModels.Flash)[0];

        Equal(TranslationTextKind.Heading, properName.TextKind);
        Equal(true, properName.TranslationHint.Contains("official", StringComparison.OrdinalIgnoreCase));
        Equal(true, properName.TranslationHint.Contains("Latin", StringComparison.OrdinalIgnoreCase));
        Equal(TranslationTextKind.Body, ordinarySentence.TextKind);
        Equal(false, ordinarySentence.TranslationHint.Contains("official", StringComparison.OrdinalIgnoreCase));
    }

    private static void DetectsSocialMediaFromAccountAndCompactAge()
    {
        var requests = VisualTranslationRequestFactory.Create(
            [
                VisualRegion("r0", "くまの @kumano·6h", VisualTextRole.Metadata),
                VisualRegion("r1", "アイコンの子のまばたき！！", VisualTextRole.Control),
            ],
            "简体中文",
            DeepSeekModels.Flash);

        Equal(true, requests.All(static request => request.Scene == "social_media"));
        Equal(TranslationTextKind.Body, requests[1].TextKind);
    }

    private static void ProtectsAccountHandlesBeforeSendingOcrText()
    {
        var handler = new RecordingHttpHandler(HttpStatusCode.OK, JsonTranslation("{\"b0\":\"invalid-without-protected-token\"}"));
        var provider = new DeepSeekTranslationProvider(new RecordingCredentialVault("safe"), handler);
        _ = provider.TranslateAsync(
            new TranslationRequest("auto", "简体中文", "くまの @kumano_yonayona·6h"),
            "alias",
            CancellationToken.None).GetAwaiter().GetResult();

        using var payload = System.Text.Json.JsonDocument.Parse(handler.Body!);
        var prompt = payload.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!;
        Equal(false, prompt.Contains("@kumano_yonayona", StringComparison.Ordinal));
        Equal(true, prompt.Contains("[[CLIPRAY_", StringComparison.Ordinal));
    }

    private static void AcceptsUnchangedStandaloneProtectedAccountHandle()
    {
        var requests = VisualTranslationRequestFactory.Create(
            [VisualRegion("r0", "@kumano_yonayona", VisualTextRole.Control)],
            "简体中文",
            DeepSeekModels.Flash);
        var provider = new DeepSeekTranslationProvider(
            new RecordingCredentialVault("safe"),
            new DynamicProtectedTokenHandler(static protectedText => protectedText));

        var result = provider.TranslateAsync(
            requests[0],
            "alias",
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(true, result.IsSuccess);
        Equal("@kumano_yonayona", result.Text);
    }

    private static void BypassesDeepSeekForProtectedOnlyVisualText()
    {
        var request = VisualTranslationRequestFactory.Create(
            [VisualRegion("r0", "@kumano_yonayona", VisualTextRole.Control)],
            "简体中文",
            DeepSeekModels.Flash)[0];
        var handler = new RecordingHttpHandler((_, _) =>
            throw new InvalidOperationException("Protected-only text must not reach DeepSeek."));
        var provider = new DeepSeekTranslationProvider(new RecordingCredentialVault("safe"), handler);

        var result = provider.TranslateAsync(request, "alias", CancellationToken.None).GetAwaiter().GetResult();

        Equal(true, result.IsSuccess);
        Equal("@kumano_yonayona", result.Text);
        Equal(0, handler.CallCount);
    }

    private static void RejectsAddedTextAroundStandaloneProtectedAccountHandle()
    {
        var requests = VisualTranslationRequestFactory.Create(
            [VisualRegion("r0", "@kumano_yonayona", VisualTextRole.Control)],
            "简体中文",
            DeepSeekModels.Flash);
        var provider = new DeepSeekTranslationProvider(
            new RecordingCredentialVault("safe"),
            new DynamicProtectedTokenHandler(static protectedText => "账号：" + protectedText));

        var result = provider.TranslateAsync(
            requests[0],
            "alias",
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(true, result.IsSuccess);
        Equal("@kumano_yonayona", result.Text);
    }

    private static void DetectsSocialProfileFromAccountAndProfileControls()
    {
        var requests = VisualTranslationRequestFactory.Create(
            [
                VisualRegion("r0", "Follow", VisualTextRole.Control),
                VisualRegion("r1", "くまの_よなよな", VisualTextRole.Control),
                VisualRegion("r2", "@kumano_yonayona", VisualTextRole.Control),
                VisualRegion("r3", "Joined September 2021", VisualTextRole.Metadata),
            ],
            "简体中文",
            DeepSeekModels.Flash);

        Equal(true, requests.All(static request => request.Scene == "social_media"));
    }

    private static void PreservesStandaloneBrandAndIdentifierLabelsWithoutFocusedRetry()
    {
        var requests = VisualTranslationRequestFactory.Create(
            [
                VisualRegion("r0", "X", VisualTextRole.Control),
                VisualRegion("r1", "Grok", VisualTextRole.Control),
                VisualRegion("r2", "SEAF", VisualTextRole.Control),
                VisualRegion("r3", "Home", VisualTextRole.Control),
                VisualRegion("r4", "POST", VisualTextRole.Control),
            ],
            "简体中文",
            DeepSeekModels.Flash);
        var handler = new RecordingHttpHandler(async (request, cancellationToken) =>
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            using var payload = System.Text.Json.JsonDocument.Parse(body);
            var promptText = payload.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!;
            using var prompt = System.Text.Json.JsonDocument.Parse(promptText);
            var translations = prompt.RootElement.EnumerateObject().ToDictionary(
                static property => property.Name,
                static property => property.Value.GetProperty("text").GetString()!);
            translations["b3"] = "主页";
            translations["b4"] = "发帖";
            var response = System.Text.Json.JsonSerializer.Serialize(translations);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonTranslation(response), Encoding.UTF8, "application/json"),
            };
        });
        var provider = new DeepSeekTranslationProvider(new RecordingCredentialVault("safe"), handler);

        var results = provider.TranslateBlocksAsync(requests, "alias", CancellationToken.None).GetAwaiter().GetResult();

        Equal(1, handler.CallCount);
        Equal(true, results.All(static result => result.IsSuccess));
        Equal("X", results[0].Text);
        Equal("Grok", results[1].Text);
        Equal("SEAF", results[2].Text);
        Equal("主页", results[3].Text);
        Equal(TranslationTextKind.UiControl, requests[4].TextKind);
        Equal("发帖", results[4].Text);
    }

    private static void KeepsGroupedSocialUiAndTrendHeadingsOutOfBodyReview()
    {
        var requests = VisualTranslationRequestFactory.Create(
            [
                VisualRegion("r0", "@kumano_yonayona", VisualTextRole.Metadata),
                VisualRegion("r1", "Translated from Japanese Show original\nArtist", VisualTextRole.Body),
                VisualRegion("r2", "ダダダダイナマイツ！！ Follow", VisualTextRole.Control),
                VisualRegion("r3", "EMIBONNIE UNDER THE FIRST MOON", VisualTextRole.Body),
                VisualRegion("r4", "Follow", VisualTextRole.Control),
            ],
            "简体中文",
            DeepSeekModels.Flash);

        Equal(TranslationTextKind.Metadata, requests[1].TextKind);
        Equal(TranslationTextKind.IdentityMetadata, requests[2].TextKind);
        Equal(TranslationTextKind.Heading, requests[3].TextKind);
    }

    private static void KeepsOcrIconNoiseOutOfBodyReview()
    {
        var requests = VisualTranslationRequestFactory.Create(
            [
                VisualRegion("r0", "Q…", VisualTextRole.Body),
                VisualRegion("r1", "口 Bookmarks", VisualTextRole.Body),
                VisualRegion("r2", "Q Grok", VisualTextRole.Control),
                VisualRegion("r3", "Hi!", VisualTextRole.Control),
            ],
            "简体中文",
            DeepSeekModels.Flash);

        Equal(TranslationTextKind.BrandOrIdentifier, requests[0].TextKind);
        Equal(TranslationTextKind.UiControl, requests[1].TextKind);
        Equal(TranslationTextKind.BrandOrIdentifier, requests[2].TextKind);
        Equal(TranslationTextKind.Body, requests[3].TextKind);
    }

    private static void RejectsPunctuationOnlyVisualTranslationChanges()
    {
        var handler = new RecordingHttpHandler(HttpStatusCode.OK, JsonTranslation("{\"b0\":\"#イラスト\"}"));
        var provider = new DeepSeekTranslationProvider(new RecordingCredentialVault("safe"), handler);
        var request = new TranslationRequest(
            "auto",
            "简体中文",
            "＃イラスト",
            DeepSeekModels.Flash,
            TranslationTextKind.Hashtag,
            "social_media",
            "Translate the readable hashtag text and preserve the leading hashtag marker.");

        var result = provider.TranslateAsync(request, "alias", CancellationToken.None).GetAwaiter().GetResult();

        Equal(false, result.IsSuccess);
    }

    private static void RejectsIdentityMetadataThatOnlyLocalizesTime()
    {
        var handler = new RecordingHttpHandler(HttpStatusCode.OK, JsonTranslation("{\"b0\":\"くまの_よなよな [[CLIPRAY_TOKEN]]·6小时\"}"));
        var requests = VisualTranslationRequestFactory.Create(
            [VisualRegion("r0", "くまの_よなよな @kumano_yonayona·6h", VisualTextRole.Metadata)],
            "简体中文",
            DeepSeekModels.Flash);
        var provider = new DeepSeekTranslationProvider(new RecordingCredentialVault("safe"), new DynamicProtectedTokenHandler(
            static protectedText => protectedText.Replace("6h", "6小时", StringComparison.Ordinal)));

        var result = provider.TranslateAsync(requests[0], "alias", CancellationToken.None).GetAwaiter().GetResult();

        Equal(false, result.IsSuccess);
        Equal("DeepSeek returned an unreadable display name.", result.Status);
    }

    private static void LocalizesCompactRelativeTimeDeterministically()
    {
        var requests = VisualTranslationRequestFactory.Create(
            [VisualRegion("r0", "くまの_よなよな @kumano_yonayona·6h", VisualTextRole.Metadata)],
            "简体中文",
            DeepSeekModels.Flash);
        var provider = new DeepSeekTranslationProvider(new RecordingCredentialVault("safe"), new DynamicProtectedTokenHandler(
            static protectedText => protectedText
                .Replace("くまの_よなよな", "Kumano_Yonayona（くまの_よなよな）", StringComparison.Ordinal)
                .Replace("6h", "6小时", StringComparison.Ordinal)));

        var result = provider.TranslateAsync(requests[0], "alias", CancellationToken.None).GetAwaiter().GetResult();

        Equal(true, result.IsSuccess);
        Equal(true, result.Text.EndsWith("·6小时前", StringComparison.Ordinal));
        Equal(true, result.Text.Contains("@kumano_yonayona", StringComparison.Ordinal));

        var cases = new[]
        {
            (Source: "9s", Returned: "9秒", Expected: "9秒前"),
            (Source: "5m", Returned: "5分钟", Expected: "5分钟前"),
            (Source: "2d", Returned: "2天", Expected: "2天前"),
            (Source: "3w", Returned: "3周", Expected: "3周前"),
            (Source: "4mo", Returned: "4个月", Expected: "4个月前"),
            (Source: "1y", Returned: "1年", Expected: "1年前"),
        };
        foreach (var item in cases)
        {
            var itemRequests = VisualTranslationRequestFactory.Create(
                [VisualRegion("r0", $"くまの @kumano·{item.Source}", VisualTextRole.Metadata)],
                "简体中文",
                DeepSeekModels.Flash);
            var itemProvider = new DeepSeekTranslationProvider(new RecordingCredentialVault("safe"), new DynamicProtectedTokenHandler(
                protectedText => Regex.Replace(
                    protectedText.Replace("くまの", "Kumano（くまの）", StringComparison.Ordinal),
                    Regex.Escape(item.Source) + "$",
                    item.Returned,
                    RegexOptions.CultureInvariant)));
            var itemResult = itemProvider.TranslateAsync(itemRequests[0], "alias", CancellationToken.None).GetAwaiter().GetResult();
            Equal(true, itemResult.IsSuccess);
            Equal(true, itemResult.Text.EndsWith("·" + item.Expected, StringComparison.Ordinal));
        }
    }

    private static void KeepsExplicitFlashVisualBatchOnFlash()
    {
        var regions = new[]
        {
            VisualRegion("r0", "\u30a2\u30a4\u30b3\u30f3\u306e\u5b50\u306e\u307e\u3070\u305f\u304d\uff01\uff01", VisualTextRole.Body),
            VisualRegion("r1", "Follow", VisualTextRole.Control),
        };
        var requests = VisualTranslationRequestFactory.Create(regions, "Chinese", DeepSeekModels.Flash);
        Equal(true, requests.All(static request => request.Model == DeepSeekModels.Flash));

        var handler = new RecordingHttpHandler(async (request, cancellationToken) =>
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            using var payload = System.Text.Json.JsonDocument.Parse(body);
            Equal(DeepSeekModels.Flash, payload.RootElement.GetProperty("model").GetString()!);
            var translated = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["b0"] = "\u5934\u50cf\u89d2\u8272\u6b63\u5728\u7728\u773c",
                ["b1"] = "\u5173\u6ce8",
            });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonTranslation(translated), Encoding.UTF8, "application/json"),
            };
        });
        var provider = new DeepSeekTranslationProvider(new RecordingCredentialVault("safe"), handler);

        var results = provider.TranslateBlocksAsync(requests, "alias", CancellationToken.None).GetAwaiter().GetResult();

        Equal(1, handler.CallCount);
        Equal(true, results.All(static result => result.IsSuccess));
    }

    private static void RetriesOnlyFailedVisualTranslationSiblings()
    {
        var requests = VisualTranslationRequestFactory.Create(
            [
                VisualRegion("r0", "Home", VisualTextRole.Control),
                VisualRegion("r1", "Explore", VisualTextRole.Control),
            ],
            "Chinese",
            DeepSeekModels.Flash);
        var handlerCall = 0;
        var handler = new RecordingHttpHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonTranslation(handlerCall++ == 0
                    ? "{\"b0\":\"\\u4e3b\\u9875\",\"b1\":\"Explore\"}"
                    : "{\"b1\":\"\\u63a2\\u7d22\"}"),
                Encoding.UTF8,
                "application/json"),
        }));
        var provider = new DeepSeekTranslationProvider(new RecordingCredentialVault("safe"), handler);

        var results = provider.TranslateBlocksAsync(requests, "alias", CancellationToken.None).GetAwaiter().GetResult();

        Equal(2, handler.CallCount);
        Equal(true, results[0].IsSuccess);
        Equal(true, results[1].IsSuccess);
        Equal("探索", results[1].Text);
    }

    private static void BoundsVisualCloudTranslationToTwelveSeconds()
    {
        Equal(TimeSpan.FromSeconds(12), VisualTranslationExecutionPolicy.CloudBudget);
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var app = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "App.xaml.cs"));
        var runtime = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "Translation", "TranslationApplicationRuntime.cs"));
        Equal(true, app.Contains("VisualTranslationExecutionPolicy.ExecuteCloudAsync", StringComparison.Ordinal));
        Equal(true, runtime.Contains("VisualTranslationExecutionPolicy.ExecuteCloudAsync", StringComparison.Ordinal));
    }

    private static void ReviewsVisualBodyTextInFocusedProBatch()
    {
        var requestBodies = new List<string>();
        var handler = new RecordingHttpHandler(async (request, cancellationToken) =>
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            requestBodies.Add(body);
            using var payload = System.Text.Json.JsonDocument.Parse(body);
            var promptText = payload.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!;
            using var prompt = System.Text.Json.JsonDocument.Parse(promptText);
            Dictionary<string, string> translations;
            if (requestBodies.Count == 1)
            {
                var protectedIdentity = prompt.RootElement.GetProperty("b0").GetProperty("text").GetString()!;
                translations = new Dictionary<string, string>
                {
                    ["b0"] = protectedIdentity.Replace("くまの", "Kumano（くまの）", StringComparison.Ordinal).Replace("6h", "6小时", StringComparison.Ordinal),
                    ["b1"] = "显示翻译",
                    ["b2"] = "图标的孩子真可爱！！",
                    ["b3"] = "#插画",
                };
            }
            else
            {
                translations = new Dictionary<string, string> { ["b0"] = "头像角色的眨眼！！" };
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonTranslation(System.Text.Json.JsonSerializer.Serialize(translations)), Encoding.UTF8, "application/json"),
            };
        });
        var regions = new[]
        {
            VisualRegion("r0", "くまの @kumano·6h", VisualTextRole.Metadata),
            VisualRegion("r1", "Show translation", VisualTextRole.Control),
            VisualRegion("r2", "アイコンの子のまばたき！！", VisualTextRole.Control),
            VisualRegion("r3", "#イラスト", VisualTextRole.Control),
        };
        var requests = VisualTranslationRequestFactory.Create(regions, "简体中文", DeepSeekModels.Flash);
        var provider = new DeepSeekTranslationProvider(new RecordingCredentialVault("safe"), handler);

        var results = provider.TranslateBlocksAsync(requests, "alias", CancellationToken.None).GetAwaiter().GetResult();

        Equal(2, requestBodies.Count);
        using var initialPayload = System.Text.Json.JsonDocument.Parse(requestBodies[0]);
        using var reviewPayload = System.Text.Json.JsonDocument.Parse(requestBodies[1]);
        Equal(DeepSeekModels.Flash, initialPayload.RootElement.GetProperty("model").GetString()!);
        Equal(DeepSeekModels.Flash, reviewPayload.RootElement.GetProperty("model").GetString()!);
        var reviewPromptText = reviewPayload.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!;
        using var reviewPrompt = System.Text.Json.JsonDocument.Parse(reviewPromptText);
        Equal(1, reviewPrompt.RootElement.EnumerateObject().Count());
        Equal("body", reviewPrompt.RootElement.GetProperty("b0").GetProperty("text_kind").GetString()!);
        Equal("头像角色的眨眼！！", results[2].Text);
        Equal(true, results.Where((_, index) => index != 2).All(static result => result.IsSuccess));
    }

    private static void RejectsUnverifiedBodyWhenQualityReviewFails()
    {
        var calls = 0;
        var handler = new RecordingHttpHandler((_, _) =>
        {
            calls++;
            return Task.FromResult(calls == 1
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonTranslation("{\"b0\":\"图标的孩子真可爱！！\"}"), Encoding.UTF8, "application/json"),
                }
                : new HttpResponseMessage(HttpStatusCode.InternalServerError));
        });
        var request = new TranslationRequest(
            "auto",
            "简体中文",
            "アイコンの子のまばたき！！",
            DeepSeekModels.Flash,
            TranslationTextKind.Body,
            "social_media",
            "Translate as natural social-media text.");
        var provider = new DeepSeekTranslationProvider(new RecordingCredentialVault("safe"), handler);

        var result = provider.TranslateAsync(request, "alias", CancellationToken.None).GetAwaiter().GetResult();

        Equal(2, calls);
        Equal(false, result.IsSuccess);
        Equal(string.Empty, result.Text);
    }

    private static void UsesThinkingOnlyForFocusedProBodyReview()
    {
        var bodies = new List<string>();
        var handler = new RecordingHttpHandler(async (request, cancellationToken) =>
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            bodies.Add(body);
            var translated = bodies.Count == 1 ? "图标的孩子真棒！！" : "头像角色的眨眼！！";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonTranslation(System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string> { ["b0"] = translated })), Encoding.UTF8, "application/json"),
            };
        });
        var request = new TranslationRequest(
            "auto", "简体中文", "アイコンの子のまばたき！！", DeepSeekModels.Flash,
            TranslationTextKind.Body, "social_media", "Translate as natural social-media text.");
        var provider = new DeepSeekTranslationProvider(new RecordingCredentialVault("safe"), handler);

        _ = provider.TranslateAsync(request, "alias", CancellationToken.None).GetAwaiter().GetResult();

        using var initial = System.Text.Json.JsonDocument.Parse(bodies[0]);
        using var review = System.Text.Json.JsonDocument.Parse(bodies[1]);
        Equal("disabled", initial.RootElement.GetProperty("thinking").GetProperty("type").GetString()!);
        Equal("enabled", review.RootElement.GetProperty("thinking").GetProperty("type").GetString()!);
    }

    private static void DoesNotReviewBodyAlreadyInTargetLanguage()
    {
        var handler = new RecordingHttpHandler(HttpStatusCode.OK, JsonTranslation("{\"b0\":\"今天的天气很好。\"}"));
        var request = new TranslationRequest(
            "auto", "简体中文", "今天的天气很好。", DeepSeekModels.Flash,
            TranslationTextKind.Body, "screen_capture", "Translate the complete body text naturally and faithfully.");
        var provider = new DeepSeekTranslationProvider(new RecordingCredentialVault("safe"), handler);

        var result = provider.TranslateAsync(request, "alias", CancellationToken.None).GetAwaiter().GetResult();

        Equal(1, handler.CallCount);
        Equal(true, result.IsSuccess);
        Equal("今天的天气很好。", result.Text);
    }

    private static void DoesNotReviewAfterInitialTransportFailure()
    {
        var handler = new RecordingHttpHandler(HttpStatusCode.InternalServerError, "upstream unavailable");
        var request = new TranslationRequest(
            "auto", "简体中文", "アイコンの子のまばたき！！", DeepSeekModels.Flash,
            TranslationTextKind.Body, "social_media", "Translate as natural social-media text.");
        var provider = new DeepSeekTranslationProvider(new RecordingCredentialVault("safe"), handler);

        var result = provider.TranslateAsync(request, "alias", CancellationToken.None).GetAwaiter().GetResult();

        Equal(1, handler.CallCount);
        Equal(false, result.IsSuccess);
    }

    private static void RetriesOnlySemanticallyInvalidProReviewSiblings()
    {
        var calls = 0;
        var handler = new RecordingHttpHandler((_, _) =>
        {
            calls++;
            var content = calls switch
            {
                1 => "{\"b0\":\"第一条初稿\",\"b1\":\"第二条初稿\"}",
                2 => "{\"b0\":\"第一条复核\",\"b1\":\"のほんぶん\"}",
                3 => "{\"b1\":\"第二条复核\"}",
                _ => throw new InvalidOperationException("Unexpected request."),
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonTranslation(content), Encoding.UTF8, "application/json"),
            });
        });
        var requests = new[]
        {
            new TranslationRequest("auto", "简体中文", "最初の本文。", DeepSeekModels.Flash, TranslationTextKind.Body, "screen_capture", "Translate the complete body text naturally and faithfully."),
            new TranslationRequest("auto", "简体中文", "二番目の本文。", DeepSeekModels.Flash, TranslationTextKind.Body, "screen_capture", "Translate the complete body text naturally and faithfully."),
        };
        var provider = new DeepSeekTranslationProvider(new RecordingCredentialVault("safe"), handler);

        var results = provider.TranslateBlocksAsync(requests, "alias", CancellationToken.None).GetAwaiter().GetResult();

        Equal(3, calls);
        Equal("第一条复核", results[0].Text);
        Equal("第二条复核", results[1].Text);
        Equal(true, results.All(static result => result.IsSuccess));
    }

    private static VisualTextRegion VisualRegion(string id, string text, VisualTextRole role) => new(
        id,
        text,
        [new OcrTextBlock(text, new PixelRect(0, 0, 200, 20), [])],
        new PixelRect(0, 0, 200, 20),
        role,
        VisualStructureConfidence.Medium);

    private static void AllowsOnlyFinalV1DeepSeekModelsWithFlashDefault()
    {
        Equal("deepseek-flash", new TranslationRequest("auto", "English", "text").Model);
        Throws<ArgumentException>(() => new TranslationRequest("auto", "English", "text", DeepSeekModels.Pro));
        Throws<ArgumentException>(() => new TranslationRequest("auto", "English", "text", "deepseek-chat"));
    }

    private static void RedactsCredentialFromAllDiagnostics()
    {
        const string secret = "redaction-sentinel-never-print";
        var vault = new RecordingCredentialVault(secret);
        var handler = new RecordingHttpHandler(new HttpRequestException("upstream echoed " + secret));
        var provider = new DeepSeekTranslationProvider(vault, handler, TimeSpan.FromSeconds(2));

        var result = provider.TranslateAsync(new TranslationRequest("auto", "English", "text"), "credential-name", CancellationToken.None)
            .GetAwaiter().GetResult();
        var test = provider.TestConnectionAsync("credential-name", DeepSeekModels.Flash, CancellationToken.None)
            .GetAwaiter().GetResult();
        var diagnostics = string.Join("|", result.Status, result.Text, test.Status);

        Equal(false, diagnostics.Contains(secret, StringComparison.Ordinal));
        Equal(false, diagnostics.Contains("credential-name", StringComparison.Ordinal));
        Equal(false, result.IsSuccess);
        Equal(false, test.IsSuccess);
    }

    private static void PreservesPlaceholdersTagsAndNumbers()
    {
        const string source = "Hello {name}, <b>score</b> 42";
        var result = TranslateWithTokenTransform(source, static text => text.Replace("Hello", "你好", StringComparison.Ordinal).Replace("score", "分数", StringComparison.Ordinal));
        Equal(true, result.IsSuccess);
        Equal("你好 {name}, <b>分数</b> 42", result.Text);
    }

    private static void GroupsOcrWordsDeterministically()
    {
        var words = new[]
        {
            new OcrWord("world", new PixelRect(55, 10, 35, 12)),
            new OcrWord("line", new PixelRect(10, 32, 28, 12)),
            new OcrWord("Hello", new PixelRect(10, 11, 38, 11)),
            new OcrWord("two", new PixelRect(45, 31, 24, 12)),
        };

        var blocks = OcrBlockGrouping.GroupWords(words);

        Equal(2, blocks.Count);
        Equal("Hello world", blocks[0].Text);
        Equal(new PixelRect(10, 10, 80, 12), blocks[0].Bounds);
        Equal("line two", blocks[1].Text);
    }

    private static void JoinsAdjacentCjkOcrWordsWithoutSyntheticSpaces()
    {
        var blocks = OcrBlockGrouping.GroupWords([
            new OcrWord("该", new PixelRect(0, 0, 12, 20)),
            new OcrWord("输", new PixelRect(13, 0, 12, 20)),
            new OcrWord("入", new PixelRect(26, 0, 12, 20)),
            new OcrWord("is", new PixelRect(44, 0, 18, 20)),
        ]);

        Equal("该输入 is", blocks[0].Text);
    }

    private static void SplitsDistantSameRowOcrWordsIntoColumns()
    {
        var blocks = OcrBlockGrouping.GroupWords([
            new OcrWord("left", new PixelRect(10, 10, 32, 12)),
            new OcrWord("column", new PixelRect(50, 10, 48, 12)),
            new OcrWord("right", new PixelRect(500, 10, 36, 12)),
            new OcrWord("column", new PixelRect(544, 10, 48, 12)),
        ]);

        Equal(2, blocks.Count);
        Equal("left column", blocks[0].Text);
        Equal("right column", blocks[1].Text);
    }

    private static void GroupsMultilineBodyTextIntoSemanticRegions()
    {
        var blocks = new[]
        {
            new OcrTextBlock("Use ChatGPT and Codex", new PixelRect(100, 100, 340, 20), []),
            new OcrTextBlock("with your projects and browser workflows", new PixelRect(100, 126, 420, 20), []),
            new OcrTextBlock("on supported Linux systems.", new PixelRect(100, 152, 300, 20), []),
        };

        var regions = VisualTextRegionBuilder.Build(blocks);

        Equal(1, regions.Count);
        Equal("Use ChatGPT and Codex\nwith your projects and browser workflows\non supported Linux systems.", regions[0].SourceText);
        Equal(3, regions[0].Lines.Count);
        Equal(new PixelRect(100, 100, 420, 72), regions[0].Bounds);
        Equal(VisualTextRole.Body, regions[0].Role);
    }

    private static void GroupsWrappedShortJapaneseProfileText()
    {
        var blocks = new[]
        {
            new OcrTextBlock("Follow", new PixelRect(344, 101, 60, 22), []),
            new OcrTextBlock("@saya_albireo", new PixelRect(96, 114, 123, 23), []),
            new OcrTextBlock("はくちょうの尾をたどってこと座の姫", new PixelRect(96, 142, 309, 23), []),
            new OcrTextBlock("の灯り、あの川を渡ればアルタイルに", new PixelRect(97, 167, 307, 22), []),
            new OcrTextBlock("会えるサブ垢→@azumino_114514なう", new PixelRect(95, 190, 319, 24), []),
            new OcrTextBlock("ぷれ→anemoi", new PixelRect(96, 214, 122, 23), []),
        };
        var regions = VisualTextRegionBuilder.Build(blocks);

        var body = regions.Single(region => region.Role == VisualTextRole.Body);
        Equal(4, body.Lines.Count);
        Equal(new PixelRect(95, 142, 319, 95), body.Bounds);
        using var source = CreateBitmap(480, 270, Color.Black);
        var style = SourceVisualStyleEstimator.EstimateRegion(source, body, blocks, Color.Black);
        Equal(SourceTextAlignment.Left, style.Alignment);

        var request = VisualTranslationRequestFactory.Create(
            regions,
            "简体中文",
            DeepSeekModels.Flash).Single(item => item.OcrText.Contains("はくちょう", StringComparison.Ordinal));
        Equal(TranslationTextKind.Body, request.TextKind);
        Equal(true, request.TranslationHint.Contains("wrapped lines", StringComparison.OrdinalIgnoreCase));
        Equal(true, request.TranslationHint.Contains("slang", StringComparison.OrdinalIgnoreCase));
    }

    private static void SeparatesCompactTranslationLinkFromLargerWrappedBody()
    {
        var blocks = new[]
        {
            new OcrTextBlock("Show translation", new PixelRect(117, 58, 215, 24), []),
            new OcrTextBlock("記録的な暴雨により増水した長江の河辺でまどマギの1シーンを撮影する武漢", new PixelRect(116, 88, 779, 29), []),
            new OcrTextBlock("のコスプレイヤーの方", new PixelRect(117, 118, 228, 28), []),
            new OcrTextBlock("ワルプルギスの夜の名シーンを大雨を利用して再現できていて素晴らしいと", new PixelRect(118, 148, 757, 29), []),
        };

        var regions = VisualTextRegionBuilder.Build(blocks);

        Equal(2, regions.Count);
        Equal("Show translation", regions[0].SourceText);
        Equal(false, regions[1].SourceText.Contains("Show translation", StringComparison.Ordinal));
        Equal(3, regions[1].Lines.Count);
    }

    private static void KeepsSameHeightShowTranslationLinkOutOfBodyRegion()
    {
        var blocks = new[]
        {
            new OcrTextBlock("Show translation", new PixelRect(116, 58, 215, 32), []),
            new OcrTextBlock(
                "\u8a18\u9332\u7684\u306a\u8c6a\u96e8\u306b\u3088\u308a\u5897\u6c34\u3057\u305f\u9577\u6c5f\u306e\u6cb3\u8fba\u3067\u307e\u3069\u30de\u30ae\u306e1\u30b7\u30fc\u30f3\u3092\u64ae\u5f71\u3059\u308b\u6b66\u6f22",
                new PixelRect(116, 94, 779, 32),
                []),
        };

        var regions = VisualTextRegionBuilder.Build(blocks);

        Equal(2, regions.Count);
        Equal("Show translation", regions[0].SourceText);
        Equal(VisualTextRole.Control, regions[0].Role);
        Equal(true, regions[1].SourceText.Contains("1\u30b7\u30fc\u30f3", StringComparison.Ordinal));
        Equal(VisualTextRole.Body, regions[1].Role);

        using var source = CreateBitmap(980, 150, Color.Black);
        var sourceControlBounds = new PixelRect(116, 58, 215, 32);
        var sourceBodyBounds = new PixelRect(116, 94, 779, 32);
        PaintSyntheticText(source, sourceControlBounds, Color.FromArgb(255, 29, 155, 240), 16);
        PaintSyntheticText(source, sourceBodyBounds, Color.White, 16);
        var sourceBlocks = new[]
        {
            new OcrTextBlock(
                "Show translation",
                sourceControlBounds,
                [new OcrWord("Show translation", sourceControlBounds)]),
            new OcrTextBlock(blocks[1].Text, sourceBodyBounds, [new OcrWord(blocks[1].Text, sourceBodyBounds)]),
        };
        var sourceRegions = VisualTextRegionBuilder.Build(sourceBlocks, source);
        using var provider = new LocalTranslationProvider(new LocalLmtOptions("missing.gguf", "missing-cli.exe"));
        var requests = VisualTranslationRequestFactory.Create(sourceRegions, "Chinese", DeepSeekModels.Flash);
        var results = provider.TranslateManyAsync(requests, CancellationToken.None).GetAwaiter().GetResult();
        var items = InPlaceLayoutService.BuildRegions(source, sourceRegions, results);
        Equal(1, items.Count);
        Equal("\u663e\u793a\u7ffb\u8bd1", items[0].Text);
    }

    private static void DoesNotColorSplitRecognizedShowTranslationControl()
    {
        using var source = CreateBitmap(220, 48, Color.Black);
        var words = new[]
        {
            new OcrWord("Show", new PixelRect(10, 12, 54, 20)),
            new OcrWord("translation", new PixelRect(68, 12, 104, 20)),
        };
        PaintSyntheticText(source, words[0].Bounds, Color.FromArgb(255, 29, 155, 240), 14);
        PaintSyntheticText(source, words[1].Bounds, Color.FromArgb(255, 140, 70, 220), 14);
        var block = new OcrTextBlock("Show translation", new PixelRect(10, 12, 162, 20), words);

        var regions = VisualTextRegionBuilder.Build([block], source);

        Equal(1, regions.Count);
        Equal("Show translation", regions[0].SourceText);
        Equal(VisualTextRole.Control, regions[0].Role);
    }

    private static void SeparatesSameColorShowTranslationPrefixFromBody()
    {
        using var source = CreateBitmap(520, 48, Color.Black);
        var words = new[]
        {
            new OcrWord("Show", new PixelRect(10, 12, 54, 20)),
            new OcrWord("translation", new PixelRect(68, 12, 104, 20)),
            new OcrWord("これは長い本文です", new PixelRect(180, 12, 180, 20)),
        };
        foreach (var word in words) PaintSyntheticText(source, word.Bounds, Color.FromArgb(255, 29, 155, 240), 14);
        var block = new OcrTextBlock(
            "Show translation これは長い本文です",
            new PixelRect(10, 12, 350, 20),
            words);

        var regions = VisualTextRegionBuilder.Build([block], source);

        Equal(2, regions.Count);
        Equal("Show translation", regions[0].SourceText);
        Equal(VisualTextRole.Control, regions[0].Role);
        Equal(true, regions[1].SourceText.Contains("長い本文", StringComparison.Ordinal));

        var singleWord = new OcrTextBlock(
            block.Text,
            block.Bounds,
            [new OcrWord(block.Text, block.Bounds)]);
        var singleWordRegions = VisualTextRegionBuilder.Build([singleWord], source);
        Equal(2, singleWordRegions.Count);
        Equal("Show translation", singleWordRegions[0].SourceText);
        Equal(true, singleWordRegions[1].SourceText.Contains("長い本文", StringComparison.Ordinal));

        using var provider = new LocalTranslationProvider(new LocalLmtOptions("missing.gguf", "missing-cli.exe"));
        var requests = VisualTranslationRequestFactory.Create(singleWordRegions, "Chinese", DeepSeekModels.Flash);
        var results = provider.TranslateManyAsync(requests, CancellationToken.None).GetAwaiter().GetResult();
        var items = InPlaceLayoutService.BuildRegions(source, singleWordRegions, results);
        Equal(TranslationTextKind.UiControl, requests[0].TextKind);
        Equal(true, results[0].IsSuccess);
        Equal("\u663e\u793a\u7ffb\u8bd1", results[0].Text);
        Equal(true, items.Any(item => item.Text == "\u663e\u793a\u7ffb\u8bd1"));
    }

    private static void SplitsMixedColorOcrLinesIntoStableVisualSpans()
    {
        using var source = CreateBitmap(360, 48, Color.Black);
        var words = new[]
        {
            new OcrWord("Translated", new PixelRect(10, 12, 74, 20)),
            new OcrWord("from", new PixelRect(88, 12, 36, 20)),
            new OcrWord("Japanese", new PixelRect(128, 12, 68, 20)),
            new OcrWord("Show", new PixelRect(204, 12, 40, 20)),
            new OcrWord("original", new PixelRect(248, 12, 64, 20)),
        };
        foreach (var word in words.Take(3)) PaintSyntheticText(source, word.Bounds, Color.FromArgb(255, 92, 100, 108), 14);
        foreach (var word in words.Skip(3)) PaintSyntheticText(source, word.Bounds, Color.FromArgb(255, 29, 155, 240), 14);
        var block = new OcrTextBlock(
            "Translated from Japanese Show original",
            new PixelRect(10, 12, 302, 20),
            [new OcrWord("Translated from Japanese Show original", new PixelRect(10, 12, 302, 20))]);

        var regions = VisualTextRegionBuilder.Build([block], source);

        Equal(2, regions.Count);
        Equal("Translated from Japanese", regions[0].SourceText);
        Equal("Show original", regions[1].SourceText);
        var items = InPlaceLayoutService.BuildRegions(
            source,
            regions,
            [
                new TranslationResult(true, "First", "ok"),
                new TranslationResult(true, "Second", "ok"),
            ]);
        Equal(2, items.Count);

        using var hashtagSource = CreateBitmap(590, 48, Color.Black);
        PaintSyntheticText(hashtagSource, new PixelRect(10, 12, 76, 20), Color.FromArgb(255, 29, 155, 240), 14);
        PaintSyntheticText(hashtagSource, new PixelRect(86, 12, 474, 20), Color.White, 16);
        var hashtag = new OcrTextBlock(
            "#Want to be followed by people who like art",
            new PixelRect(10, 12, 550, 20),
            [new OcrWord("#Want to be followed by people who like art", new PixelRect(10, 12, 550, 20))]);

        var hashtagRegions = VisualTextRegionBuilder.Build([hashtag], hashtagSource);

        Equal(2, hashtagRegions.Count);
        Equal("#Want", hashtagRegions[0].SourceText);
        Equal("to be followed by people who like art", hashtagRegions[1].SourceText);
        Equal(true, hashtagRegions[0].Bounds.Right is >= 82 and <= 90);
        Equal(hashtagRegions[0].Bounds.Right, hashtagRegions[1].Bounds.Left);
        var hashtagItems = InPlaceLayoutService.BuildRegions(
            hashtagSource,
            hashtagRegions,
            [
                new TranslationResult(true, "Wanted", "ok"),
                new TranslationResult(true, "Art fans", "ok"),
            ]);
        Equal(2, hashtagItems.Count);
        Equal(
            true,
            Math.Abs(hashtagItems[0].TextLayout!.FontSizePixels - hashtagItems[1].TextLayout!.FontSizePixels) <= 0.5);
    }

    private static void SplitsAdjacentUiWordsAcrossBackingSurfaces()
    {
        using var source = CreateBitmap(280, 64, Color.Black);
        using (var graphics = Graphics.FromImage(source))
        {
            graphics.FillRectangle(Brushes.White, 172, 8, 96, 44);
        }
        var identity = new OcrWord("Account", new PixelRect(12, 18, 112, 28));
        var follow = new OcrWord("Follow", new PixelRect(186, 18, 66, 28));
        PaintSyntheticText(source, identity.Bounds, Color.White, 20);
        PaintSyntheticText(source, follow.Bounds, Color.Black, 20);
        var block = new OcrTextBlock(
            "Account Follow",
            new PixelRect(12, 8, 256, 44),
            [identity, follow]);

        var regions = VisualTextRegionBuilder.Build([block], source);

        Equal(2, regions.Count);
        Equal("Account", regions[0].SourceText);
        Equal("Follow", regions[1].SourceText);
        Equal(follow.Bounds, regions[1].Bounds);
    }

    private static void KeepsLeadingDecorativeGlyphOutsideTranslation()
    {
        using var source = CreateBitmap(180, 56, Color.Black);
        var glyph = new OcrWord("D+", new PixelRect(10, 10, 34, 34));
        var control = new OcrWord("Follow", new PixelRect(50, 16, 86, 24));
        PaintSyntheticText(source, glyph.Bounds, Color.White, 26);
        PaintSyntheticText(source, control.Bounds, Color.White, 18);
        var block = new OcrTextBlock(
            "D+ Follow",
            new PixelRect(10, 10, 126, 34),
            [glyph, control]);

        var regions = VisualTextRegionBuilder.Build([block], source);

        Equal(1, regions.Count);
        Equal("Follow", regions[0].SourceText);
        Equal(control.Bounds, regions[0].Bounds);
    }

    private static void SplitsMergedTopicChipsWhileDroppingIconNoise()
    {
        var block = new OcrTextBlock(
            "B News θ Sports",
            new PixelRect(86, 59, 275, 33),
            [
                new OcrWord("B", new PixelRect(86, 64, 34, 24)),
                new OcrWord("News", new PixelRect(112, 60, 74, 30)),
                new OcrWord("θ", new PixelRect(253, 65, 24, 22)),
                new OcrWord("Sports", new PixelRect(279, 59, 82, 33)),
            ]);

        var regions = VisualTextRegionBuilder.Build([block]);

        Equal(2, regions.Count);
        var news = regions.Single(static region => region.SourceText == "News");
        var sports = regions.Single(static region => region.SourceText == "Sports");
        Equal(new PixelRect(112, 60, 74, 30), news.Bounds);
        Equal(new PixelRect(279, 59, 82, 33), sports.Bounds);
    }

    private static void KeepsTrailingColoredDecorationOutsideTranslation()
    {
        using var source = CreateBitmap(160, 56, Color.Black);
        var fullBounds = new PixelRect(10, 12, 118, 28);
        PaintSyntheticText(source, new PixelRect(10, 12, 72, 28), Color.White, 20);
        using (var graphics = Graphics.FromImage(source))
        {
            graphics.FillRectangle(Brushes.Gold, 91, 16, 24, 16);
            graphics.FillRectangle(Brushes.HotPink, 101, 25, 19, 10);
        }
        var block = new OcrTextBlock("Artist", fullBounds, [new OcrWord("Artist", fullBounds)]);

        var regions = VisualTextRegionBuilder.Build([block], source);
        var items = InPlaceLayoutService.BuildRegions(
            source,
            regions,
            [new TranslationResult(true, "\u827a\u672f\u5bb6", "ok")]);

        Equal(1, regions.Count);
        Equal(true, regions[0].Bounds.Right <= 86);
        Equal(1, items.Count);
    }

    private static void KeepsLegitimateLeadingOneCharacterLabel()
    {
        var label = new OcrWord("A", new PixelRect(10, 12, 18, 24));
        var text = new OcrWord("Follow", new PixelRect(40, 12, 76, 24));
        var block = new OcrTextBlock(
            "A Follow",
            new PixelRect(10, 12, 106, 24),
            [label, text]);

        var region = VisualTextRegionBuilder.Build([block]).Single();

        Equal("A Follow", region.SourceText);
    }

    private static void DoesNotTrimAttachedMulticolorWord()
    {
        using var source = CreateBitmap(140, 52, Color.Black);
        var bounds = new PixelRect(10, 12, 100, 26);
        using (var graphics = Graphics.FromImage(source))
        {
            graphics.FillRectangle(Brushes.White, 12, 18, 58, 14);
            graphics.FillRectangle(Brushes.Red, 70, 18, 38, 14);
        }
        var block = new OcrTextBlock("Brand", bounds, [new OcrWord("Brand", bounds)]);

        var region = VisualTextRegionBuilder.Build([block], source).Single();

        Equal(bounds, region.Bounds);
    }

    private static void TrimsTouchingDecorationWithIncidentalContact()
    {
        using var source = CreateBitmap(140, 52, Color.Black);
        var bounds = new PixelRect(10, 12, 108, 26);
        using (var graphics = Graphics.FromImage(source))
        {
            graphics.FillRectangle(Brushes.White, 12, 18, 58, 14);
            graphics.FillRectangle(Brushes.Gold, 74, 18, 38, 14);
        }
        source.SetPixel(70, 24, Color.Gold);
        var block = new OcrTextBlock("Artist", bounds, [new OcrWord("Artist", bounds)]);

        var region = VisualTextRegionBuilder.Build([block], source).Single();

        Equal(true, region.Bounds.Right <= 74);
    }

    private static void TranslatesBodyTextWithUnmaskedInlineDecoration()
    {
        using var source = CreateBitmap(320, 64, Color.Black);
        var first = new OcrWord("First sentence", new PixelRect(10, 18, 120, 26));
        var second = new OcrWord("continues here", new PixelRect(190, 18, 112, 26));
        PaintSyntheticText(source, first.Bounds, Color.White, 18);
        PaintSyntheticText(source, second.Bounds, Color.White, 18);
        using (var graphics = Graphics.FromImage(source))
        {
            graphics.FillRectangle(Brushes.Gold, 150, 21, 18, 13);
            graphics.FillRectangle(Brushes.HotPink, 160, 31, 12, 8);
        }
        var line = new OcrTextBlock(
            "First sentence continues here",
            new PixelRect(10, 18, 292, 26),
            [first, second]);
        var region = new VisualTextRegion(
            "r0", line.Text, [line], line.Bounds,
            VisualTextRole.Body, VisualStructureConfidence.High);

        var items = InPlaceLayoutService.BuildRegions(
            source,
            [region],
            [new TranslationResult(true, "\u8fd9\u662f\u8bd1\u6587", "ok")]);

        Equal(1, items.Count);
        Equal("\u8fd9\u662f\u8bd1\u6587", items[0].Text);
        Equal(true, items[0].MaskedPatchPng is { Length: > 0 });
    }

    private static void KeepsGroupedProfileMetadataOutOfBodyTranslation()
    {
        var regions = VisualTextRegionBuilder.Build([
            new OcrTextBlock("Joined September 2021", new PixelRect(40, 100, 230, 22), []),
            new OcrTextBlock("435 Following 630 Followers", new PixelRect(40, 132, 280, 22), []),
            new OcrTextBlock("Not followed by anyone you're following", new PixelRect(40, 164, 370, 22), []),
        ]);

        Equal(true, regions.All(static region => region.Role == VisualTextRole.Metadata));
    }

    private static void KeepsControlsMetadataAndColumnsSeparate()
    {
        var regions = VisualTextRegionBuilder.Build([
            new OcrTextBlock("X", new PixelRect(10, 20, 20, 22), []),
            new OcrTextBlock("Home", new PixelRect(20, 20, 70, 22), []),
            new OcrTextBlock("Use ChatGPT and Codex", new PixelRect(100, 100, 340, 20), []),
            new OcrTextBlock("with your projects and browser workflows", new PixelRect(100, 126, 420, 20), []),
            new OcrTextBlock("10 hours ago", new PixelRect(100, 180, 90, 12), []),
            new OcrTextBlock("Today’s News", new PixelRect(700, 100, 180, 24), []),
        ]);

        Equal(5, regions.Count);
        Equal(VisualTextRole.Control, regions[0].Role);
        Equal(2, regions.Single(region => region.Role == VisualTextRole.Body).Lines.Count);
        Equal(true, regions.Any(region => region.Role == VisualTextRole.Metadata));
        Equal(true, regions.Any(region => region.Bounds.Left >= 700));
    }

    private static void DoesNotMergeHeadingWithSmallerSubtitle()
    {
        var regions = VisualTextRegionBuilder.Build([
            new OcrTextBlock("Machines", new PixelRect(405, 48, 175, 48), []),
            new OcrTextBlock("Manage the devices connected to your tailnet. Learn more", new PixelRect(407, 102, 576, 36), []),
        ]);

        Equal(2, regions.Count);
        Equal("Machines", regions[0].SourceText);
        Equal("Manage the devices connected to your tailnet. Learn more", regions[1].SourceText);
    }

    private static void SeparatesShortCardHeadingFromDistantBody()
    {
        var heading = new OcrTextBlock("Departure notice", new PixelRect(72, 67, 308, 49), []);
        var body = new OcrTextBlock(
            "The blue train leaves at eight. Keep ticket XR-17 until arrival.",
            new PixelRect(69, 140, 857, 46), []);
        var separated = VisualTextRegionBuilder.Build([heading, body]);
        Equal(2, separated.Count);
        Equal("Departure notice", separated[0].SourceText);
        Equal(body.Text, separated[1].SourceText);

        var wrappedBody = body with { Bounds = new PixelRect(69, 123, 857, 46) };
        Equal(1, VisualTextRegionBuilder.Build([heading, wrappedBody]).Count);
    }

    private static void DoesNotMergeWideParagraphAcrossCardColumns()
    {
        var regions = VisualTextRegionBuilder.Build([
            new OcrTextBlock("Add devices to your network", new PixelRect(450, 795, 330, 34), []),
            new OcrTextBlock("Tailscale works best when installed on multiple devices.", new PixelRect(450, 842, 1182, 34), []),
            new OcrTextBlock("Operating systems", new PixelRect(452, 895, 192, 31), []),
            new OcrTextBlock("Cloud providers", new PixelRect(890, 895, 190, 31), []),
            new OcrTextBlock("Containers", new PixelRect(1315, 895, 117, 31), []),
        ]);

        Equal(4, regions.Count);
        var paragraph = regions.Single(region => region.SourceText.Contains("Tailscale works", StringComparison.Ordinal));
        Equal(2, paragraph.Lines.Count);
        Equal(false, paragraph.SourceText.Contains("Cloud providers", StringComparison.Ordinal));
        Equal(false, paragraph.SourceText.Contains("Operating systems", StringComparison.Ordinal));
        Equal(true, regions.Any(region => region.SourceText == "Operating systems"));
        Equal(true, regions.Any(region => region.SourceText == "Cloud providers"));
        Equal(true, regions.Any(region => region.SourceText == "Containers"));
    }

    private static void KeepsParagraphIntactBesideSidebarRow()
    {
        var regions = VisualTextRegionBuilder.Build([
            new OcrTextBlock("Thanks for waiting and you can cancel that MacBook order", new PixelRect(500, 100, 700, 32), []),
            new OcrTextBlock("impatient. It is that good.", new PixelRect(500, 140, 330, 32), []),
            new OcrTextBlock("Bookmarks", new PixelRect(40, 140, 130, 32), []),
        ]);

        Equal(2, regions.Count);
        var paragraph = regions.Single(region => region.Role == VisualTextRole.Body);
        Equal(2, paragraph.Lines.Count);
        Equal(true, paragraph.SourceText.Contains("impatient", StringComparison.Ordinal));
    }

    private static void SeparatesInlineTechnicalIdentityFromBadge()
    {
        var identity = new OcrWord("flowyear.dev@ou...", new PixelRect(70, 195, 209, 33));
        var badge = new OcrWord("Free", new PixelRect(284, 196, 55, 32));
        var regions = VisualTextRegionBuilder.Build([
            new OcrTextBlock("flowyear.dev@ou... Free", new PixelRect(70, 195, 269, 33), [identity, badge]),
        ]);

        Equal(2, regions.Count);
        Equal("flowyear.dev@ou...", regions[0].SourceText);
        Equal("Free", regions[1].SourceText);
        Equal(identity.Bounds, regions[0].Bounds);
        Equal(badge.Bounds, regions[1].Bounds);
    }

    private static void BuildsAtomicInPlaceItemForVisualRegion()
    {
        using var source = CreateBitmap(220, 80, Color.Black);
        var firstWords = new[] { new OcrWord("First sentence", new PixelRect(12, 12, 90, 14)) };
        var secondWords = new[] { new OcrWord("continues here", new PixelRect(12, 32, 84, 14)) };
        foreach (var word in firstWords.Concat(secondWords))
        {
            for (var y = (int)word.Bounds.Top + 3; y < word.Bounds.Bottom - 2; y++)
            for (var x = (int)word.Bounds.Left + 2; x < word.Bounds.Right - 2; x++)
                source.SetPixel(x, y, Color.White);
        }
        var lines = new[]
        {
            new OcrTextBlock("First sentence", new PixelRect(12, 12, 90, 14), firstWords),
            new OcrTextBlock("continues here", new PixelRect(12, 32, 84, 14), secondWords),
        };
        var region = new VisualTextRegion("r0", "First sentence\ncontinues here", lines,
            new PixelRect(12, 12, 90, 34), VisualTextRole.Body, VisualStructureConfidence.High);

        var items = InPlaceLayoutService.BuildRegions(source, [region],
            [new TranslationResult(true, "完整段落译文", "ok")]);

        Equal(1, items.Count);
        Equal("完整段落译文", items[0].Text);
        Equal(region.Bounds, items[0].Bounds);
        Equal("r0", items[0].SourceRegion!.Id);
        Equal(true, items[0].MaskedPatchPng is { Length: > 0 });
    }

    private static void RendersLocalLmtShowTranslationControlWithoutOcrWordBoxes()
    {
        using var source = CreateBitmap(240, 48, Color.Black);
        var bounds = new PixelRect(12, 12, 180, 24);
        var line = new OcrTextBlock("Show translation", bounds, []);
        var region = new VisualTextRegion(
            "r0", line.Text, [line], bounds,
            VisualTextRole.Control, VisualStructureConfidence.High);
        using var provider = new LocalTranslationProvider(new LocalLmtOptions("missing.gguf", "missing-cli.exe"));

        var result = provider.TranslateAsync(
            new TranslationRequest("English", "Chinese", line.Text, textKind: TranslationTextKind.UiControl),
            CancellationToken.None).GetAwaiter().GetResult();
        var items = InPlaceLayoutService.BuildRegions(source, [region], [result]);

        Equal(true, result.IsSuccess);
        Equal(1, items.Count);
        Equal("\u663e\u793a\u7ffb\u8bd1", items[0].Text);
        Equal(true, items[0].MaskedPatchPng is { Length: > 0 });
    }

    private static void RendersLocalLmtControlWithLowConfidenceOcrWordBoxes()
    {
        using var source = CreateBitmap(260, 48, Color.Black);
        var bounds = new PixelRect(12, 10, 215, 27);
        var line = new OcrTextBlock(
            "Show translation",
            bounds,
            [new OcrWord("Show translation", bounds)]);
        var region = new VisualTextRegion(
            "r0", line.Text, [line], bounds,
            VisualTextRole.Control, VisualStructureConfidence.High);

        var plan = InPlaceLayoutService.BuildRegionPlan(
            source,
            [region],
            [new TranslationResult(true, "显示翻译", "ok")]);

        Equal(1, plan.Items.Count);
        Equal(0, plan.Rejections.Count);
        Equal("显示翻译", plan.Items[0].Text);
    }

    private static void UsesConservativeTypographyForUiText()
    {
        using var source = CreateBitmap(140, 40, Color.Black);
        var word = new OcrWord("ordinary", new PixelRect(10, 10, 80, 16));
        for (var y = 13; y < 23; y++)
        for (var x = 12; x < 86; x++) source.SetPixel(x, y, Color.White);
        var line = new OcrTextBlock("ordinary", word.Bounds, [word]);
        var region = new VisualTextRegion("r0", line.Text, [line], line.Bounds,
            VisualTextRole.Body, VisualStructureConfidence.Medium);

        var style = SourceVisualStyleEstimator.EstimateRegion(source, region, [line], Color.Black);

        Equal(false, style.IsItalic);
        Equal(0d, style.RotationDegrees);
    }

    private static void MeasuresTextHeightWithoutNavigationIcon()
    {
        using var source = CreateBitmap(140, 40, Color.Black);
        for (var y = 8; y < 32; y++)
        for (var x = 4; x < 20; x++) source.SetPixel(x, y, Color.White);
        for (var glyph = 0; glyph < 7; glyph++)
        {
            var left = 30 + glyph * 11;
            for (var y = 11; y < 29; y++) source.SetPixel(left, y, Color.White);
            for (var x = left; x < left + 7; x++)
            {
                source.SetPixel(x, 11, Color.White);
                source.SetPixel(x, 28, Color.White);
            }
        }
        var word = new OcrWord("Network", new PixelRect(4, 8, 114, 24));
        var line = new OcrTextBlock("Network", word.Bounds, [word]);
        var region = new VisualTextRegion("r0", line.Text, [line], line.Bounds,
            VisualTextRole.Control, VisualStructureConfidence.Medium);

        var style = SourceVisualStyleEstimator.EstimateRegion(source, region, [line], Color.Black);

        if (style.GlyphHeightPixels is < 15 or > 24)
            throw new InvalidOperationException($"Unexpected icon-safe glyph height {style.GlyphHeightPixels:F1}.");
    }

    private static void PreservesConfidentMutedSourceForeground()
    {
        using var source = CreateBitmap(180, 52, Color.Black);
        var muted = Color.FromArgb(255, 82, 90, 98);
        var bounds = new PixelRect(12, 12, 120, 22);
        PaintSyntheticText(source, bounds, muted, 15);
        var line = new OcrTextBlock("Pinned", bounds, [new OcrWord("Pinned", bounds)]);
        var region = new VisualTextRegion(
            "r0", line.Text, [line], bounds, VisualTextRole.Metadata, VisualStructureConfidence.High);

        var items = InPlaceLayoutService.BuildRegions(
            source,
            [region],
            [new TranslationResult(true, "Pinned translated", "ok")]);

        Equal(1, items.Count);
        Equal(true, ColorDistance(items[0].ForegroundColor, muted) <= 18);
        Equal(true, InPlaceLayoutService.ContrastRatio(items[0].ForegroundColor, Color.Black) < 4.5);
    }

    private static void IgnoresSmallColoredBadgeWhenEstimatingTextForeground()
    {
        using var source = CreateBitmap(250, 48, Color.Black);
        var gray = Color.FromArgb(255, 113, 118, 123);
        for (var y = 12; y < 30; y++)
        for (var x = 12; x < 32; x++) source.SetPixel(x, y, Color.FromArgb(255, 29, 155, 240));
        PaintSyntheticText(source, new PixelRect(42, 10, 180, 22), gray, 15);
        var bounds = new PixelRect(10, 8, 216, 26);
        var line = new OcrTextBlock("@handle · May 20", bounds, [new OcrWord("@handle · May 20", bounds)]);
        var region = new VisualTextRegion(
            "r0", line.Text, [line], bounds, VisualTextRole.Metadata, VisualStructureConfidence.Medium);

        var style = SourceVisualStyleEstimator.EstimateRegion(source, region, [line], Color.Black);

        Equal(true, ColorDistance(style.ForegroundColor, gray) <= 24);
    }

    private static void NormalizesSameRowVisualPeersToOneFontSize()
    {
        using var source = CreateBitmap(480, 60, Color.Black);
        var labels = new[] { "Posts", "Replies", "Highlights", "Media" };
        var translations = new[] { "\u8bd1\u6587", "\u8bd1\u6587", "\u8bd1\u6587", "\u8bd1\u6587" };
        var heights = new[] { 18, 19, 20, 21 };
        var widths = new[] { 100d, 100d, 100d, 28d };
        var regions = new List<VisualTextRegion>();
        for (var index = 0; index < labels.Length; index++)
        {
            var bounds = new PixelRect(10 + index * 115, 14, widths[index], 24);
            PaintSyntheticText(source, bounds, Color.White, heights[index]);
            var line = new OcrTextBlock(labels[index], bounds, [new OcrWord(labels[index], bounds)]);
            regions.Add(new VisualTextRegion(
                $"r{index}", labels[index], [line], bounds,
                VisualTextRole.Control, VisualStructureConfidence.High));
        }

        var items = InPlaceLayoutService.BuildRegions(
            source,
            regions,
            translations.Select(static text => new TranslationResult(true, text, "ok")).ToArray());

        Equal(4, items.Count);
        var sizes = items.Select(static item => item.TextLayout!.FontSizePixels).ToArray();
        Equal(true, sizes.Max() - sizes.Min() <= 0.5);
    }

    private static void KeepsAdjacentInlineColorFragmentsLeftFlowing()
    {
        using var source = CreateBitmap(300, 120, Color.Black);
        var definitions = new[]
        {
            (Text: "#Want", Bounds: new PixelRect(10, 14, 54, 24), Color: Color.FromArgb(255, 29, 155, 240)),
            (Text: "to be followed", Bounds: new PixelRect(64, 14, 196, 24), Color: Color.White),
            (Text: "right peer one", Bounds: new PixelRect(154, 50, 106, 24), Color: Color.White),
            (Text: "right peer two", Bounds: new PixelRect(164, 86, 96, 24), Color: Color.White),
        };
        var regions = new List<VisualTextRegion>();
        for (var index = 0; index < definitions.Length; index++)
        {
            var definition = definitions[index];
            PaintSyntheticText(source, definition.Bounds, definition.Color, 18);
            var line = new OcrTextBlock(
                definition.Text,
                definition.Bounds,
                [new OcrWord(definition.Text, definition.Bounds)]);
            regions.Add(new VisualTextRegion(
                $"r{index}", definition.Text, [line], definition.Bounds,
                index == 0 ? VisualTextRole.Control : VisualTextRole.Body,
                VisualStructureConfidence.High));
        }

        var items = InPlaceLayoutService.BuildRegions(
            source,
            regions,
            [
                new TranslationResult(true, "#\u60f3\u8981", "ok"),
                new TranslationResult(true, "\u88ab\u5173\u6ce8", "ok"),
                new TranslationResult(true, "\u53f3\u4fa7\u4e00", "ok"),
                new TranslationResult(true, "\u53f3\u4fa7\u4e8c", "ok"),
            ]);

        Equal(4, items.Count);
        Equal(SourceTextAlignment.Left, items.Single(item => item.SourceRegion!.Id == "r0").TextLayout!.Alignment);
        Equal(SourceTextAlignment.Left, items.Single(item => item.SourceRegion!.Id == "r1").TextLayout!.Alignment);
    }

    private static void UsesSemanticRegionsInProductionTranslation()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var source = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "Translation", "TranslationApplicationOrchestrator.cs"));
        Equal(true, source.Contains("VisualTextRegionBuilder.Build", StringComparison.Ordinal));
        Equal(true, source.Contains("VisualTranslationRequestFactory.Create", StringComparison.Ordinal));
        Equal(true, source.Contains("InPlaceLayoutService.BuildRegionPlan", StringComparison.Ordinal));
    }

    private static void PlansReversibleSmallTextOcrEnlargement()
    {
        var plan = OcrPreprocessingPlan.Create(240, 80);

        Equal(2d, plan.Scale);
        Equal(new PixelRect(10, 6, 30, 9), plan.Restore(new PixelRect(20, 12, 60, 18)));
    }

    private static void UsesStrongerEnlargementForTinyOcrSelections()
    {
        Equal(4d, OcrPreprocessingPlan.Create(40, 20).Scale);
    }

    private static void BuildsDeterministicInPlaceMappings()
    {
        using var source = CreateBitmap(120, 60, Color.FromArgb(255, 20, 30, 40));
        var before = source.GetPixel(15, 15);
        var blocks = new[]
        {
            new OcrTextBlock("one", new PixelRect(10, 10, 30, 12), []),
            new OcrTextBlock("two", new PixelRect(10, 30, 35, 12), []),
        };

        var first = InPlaceLayoutService.Build(source, blocks, ["一", "二"]);
        var second = InPlaceLayoutService.Build(source, blocks, ["一", "二"]);

        Equal(true, first.SequenceEqual(second));
        Equal(blocks[0], first[0].SourceBlock);
        Equal(new PixelRect(10, 10, 30, 12), first[0].Bounds);
        Equal(before.ToArgb(), source.GetPixel(15, 15).ToArgb());
    }

    private static void ChoosesBackgroundModesFromSourcePixels()
    {
        using var flat = CreateBitmap(40, 40, Color.DarkSlateGray);
        using var complex = new Bitmap(40, 40);
        for (var y = 0; y < complex.Height; y++)
        for (var x = 0; x < complex.Width; x++)
            complex.SetPixel(x, y, (x + y) % 2 == 0 ? Color.Black : Color.White);
        var bounds = new PixelRect(8, 8, 20, 20);

        Equal(InPlaceBackgroundMode.FlatSampled, InPlaceLayoutService.ChooseBackgroundMode(flat, bounds));
        Equal(InPlaceBackgroundMode.ComplexBacking, InPlaceLayoutService.ChooseBackgroundMode(complex, bounds));
    }

    private static void ReconstructsComplexTranslationBacking()
    {
        using var source = new Bitmap(80, 40);
        for (var y = 0; y < source.Height; y++)
        for (var x = 0; x < source.Width; x++)
            source.SetPixel(x, y, Color.FromArgb(x * 3, x * 3, x * 3));
        using (var graphics = Graphics.FromImage(source))
        using (var glyph = new SolidBrush(Color.White))
            graphics.FillRectangle(glyph, 36, 14, 8, 8);

        var item = InPlaceLayoutService.Build(
            source,
            [new OcrTextBlock("source", new PixelRect(20, 10, 40, 18), [])],
            ["translated"])[0];
        Equal(InPlaceBackgroundMode.ComplexBacking, item.BackgroundMode);
        Equal(true, item.BackgroundPatchPng is { Length: > 0 });
        using var patch = new Bitmap(new MemoryStream(item.BackgroundPatchPng!));
        Equal(new Size(40, 18), patch.Size);
        Equal(true, patch.GetPixel(20, 8).R < 180);
        Equal(true, patch.GetPixel(35, 8).R - patch.GetPixel(4, 8).R > 60);
    }

    private static void CancelsTranslationAndKeepsOcrBitmapAlive()
    {
        var provider = new DeepSeekTranslationProvider(
            new RecordingCredentialVault("safe-key"),
            new RecordingHttpHandler(async (_, cancellationToken) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK);
            }),
            TimeSpan.FromSeconds(5));
        using var cancellation = new CancellationTokenSource();
        var translation = provider.TranslateAsync(new TranslationRequest("auto", "English", "text"), "key", cancellation.Token);
        cancellation.Cancel();
        Throws<OperationCanceledException>(() => translation.GetAwaiter().GetResult());

        var runtime = new DelayedOcrRuntime();
        var ocr = new OcrService(() => runtime);
        var source = CreateBitmap(2, 2, Color.CornflowerBlue);
        var recognition = ocr.RecognizeAsync(source, CancellationToken.None);
        source.Dispose();
        runtime.Release();
        var result = recognition.GetAwaiter().GetResult();

        Equal(true, result.IsSuccess);
        Equal(Color.CornflowerBlue.ToArgb(), runtime.ObservedColor.ToArgb());
    }

    private static void DefinesFastOcrRuntime()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var source = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "Ocr", "OcrService.cs"));

        Equal(true, source.Contains("new FastFirstOcrRuntime", StringComparison.Ordinal));
        Equal(true, source.Contains("new RapidOcrRuntime", StringComparison.Ordinal));
        Equal(false, source.Contains("new FastFirstOcrRuntime(new PpOcrRuntime", StringComparison.Ordinal));
        Equal(true, source.Contains("new WindowsOcrRuntime", StringComparison.Ordinal));
    }

    private static void ResolvesLockedRapidOcrModelSet()
    {
        using var workspace = TemporaryWorkspace.Create();
        var modelDirectory = Path.Combine(workspace.Root, "artifacts", "models", "rapidocr");
        Directory.CreateDirectory(modelDirectory);
        File.WriteAllBytes(Path.Combine(modelDirectory, "PP-OCRv6_det_small.onnx"), [1]);
        File.WriteAllBytes(Path.Combine(modelDirectory, "PP-OCRv6_rec_small.onnx"), [2]);
        File.WriteAllText(Path.Combine(modelDirectory, "ppocrv6_small_dict.txt"), "a\n");

        var paths = RapidOcrModelPaths.Resolve(workspace.Root);

        Equal(Path.Combine(modelDirectory, "PP-OCRv6_det_small.onnx"), paths.Detector);
        Equal(Path.Combine(modelDirectory, "PP-OCRv6_rec_small.onnx"), paths.Recognizer);
        Equal(Path.Combine(modelDirectory, "ppocrv6_small_dict.txt"), paths.Dictionary);
    }

    private static void ResolvesKoreanRapidOcrModelSet()
    {
        using var workspace = TemporaryWorkspace.Create();
        var modelDirectory = Path.Combine(workspace.Root, "artifacts", "models", "rapidocr");
        Directory.CreateDirectory(modelDirectory);
        File.WriteAllBytes(Path.Combine(modelDirectory, "korean_PP-OCRv5_rec_mobile.onnx"), [1]);
        File.WriteAllText(Path.Combine(modelDirectory, "ppocrv5_korean_dict.txt"), "가\n");

        var paths = KoreanRapidOcrModelPaths.Resolve(workspace.Root);

        Equal(Path.Combine(modelDirectory, "korean_PP-OCRv5_rec_mobile.onnx"), paths.Recognizer);
        Equal(Path.Combine(modelDirectory, "ppocrv5_korean_dict.txt"), paths.Dictionary);
    }

    private static void MergesKoreanOcrWithoutDuplicatingLatinText()
    {
        OcrWord[] primary =
        [
            new("1.4K", new PixelRect(10, 10, 40, 12)),
            new("0|0大44@HM44", new PixelRect(10, 40, 180, 24)),
            new("Show more", new PixelRect(10, 90, 90, 16)),
        ];
        OcrWord[] korean =
        [
            new("1.4K", new PixelRect(10, 10, 40, 12)),
            new("한국어 본문입니다", new PixelRect(8, 38, 190, 28)),
            new("Show more", new PixelRect(10, 90, 90, 16)),
        ];

        var merged = ScriptSupplementalOcr.Merge(primary, korean, ScriptSupplementalOcr.ContainsHangul);

        Equal(3, merged.Count);
        Equal(true, merged.Any(static word => word.Text == "한국어 본문입니다"));
        Equal(false, merged.Any(static word => word.Text == "0|0大44@HM44"));
        Equal(1, merged.Count(static word => word.Text == "1.4K"));
        Equal(1, merged.Count(static word => word.Text == "Show more"));
    }

    private static void PackagesKoreanOcrAssets()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var project = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "ClipRay.App.csproj"));
        var buildScript = File.ReadAllText(Path.Combine(workspace, "scripts", "Build-Installer.ps1"));

        Equal(true, project.Contains("korean_PP-OCRv5_rec_mobile.onnx", StringComparison.Ordinal));
        Equal(true, project.Contains("ppocrv5_korean_dict.txt", StringComparison.Ordinal));
        Equal(true, project.Contains("ch_PP-OCRv5_mobile_det.onnx", StringComparison.Ordinal));
        Equal(true, buildScript.Contains("korean_PP-OCRv5_rec_mobile.onnx", StringComparison.Ordinal));
        Equal(true, buildScript.Contains("ppocrv5_korean_dict.txt", StringComparison.Ordinal));
        Equal(true, buildScript.Contains("ch_PP-OCRv5_mobile_det.onnx", StringComparison.Ordinal));
    }

    private static void CapsRapidOcrThreadsForInteractiveLatency()
    {
        Equal(12, RapidOcrThreadPolicy.Select(24));
        Equal(8, RapidOcrThreadPolicy.Select(16));
        Equal(2, RapidOcrThreadPolicy.Select(4));
        Equal(1, RapidOcrThreadPolicy.Select(1));
    }

    private static void NormalizesDarkOcrScreenshotsWithoutChangingGeometry()
    {
        using var dark = CreateBitmap(20, 12, Color.FromArgb(20, 20, 20));
        using var light = CreateBitmap(20, 12, Color.FromArgb(235, 235, 235));

        Equal(true, OcrPolarityPlan.ShouldInvert(dark));
        Equal(false, OcrPolarityPlan.ShouldInvert(light));
        using var normalized = OcrPolarityPlan.CreatePreparedBitmap(dark);
        Equal(dark.Size, normalized.Size);
        Equal(true, normalized.GetPixel(0, 0).R > 200);
    }

    private static void UsesPpOcrPathBeforeWindowsOcrFallback()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var source = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "Ocr", "OcrService.cs"));

        Equal(true, source.Contains("new ScriptSupplementalOcrRuntime", StringComparison.Ordinal));
        Equal(true, source.Contains("new RapidOcrRuntime(useKoreanSupplement: true)", StringComparison.Ordinal));
        Equal(true, source.Contains("multilingual PP-OCRv6 with Korean PP-OCRv5 supplement; Windows OCR fallback", StringComparison.Ordinal));
    }

    private static void WarmsPpOcrPrimaryRuntimeBeforeFallback()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var source = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "Ocr", "PpOcrRuntime.cs"));

        Equal(true, source.Contains("fast is IOcrWarmup", StringComparison.Ordinal));
        Equal(false, source.Contains("fallback is IOcrWarmup warmup", StringComparison.Ordinal));
    }

    private static void ProbesWindowsOcrLazily()
    {
        var tempBefore = Directory.GetFiles(Path.GetTempPath(), "ClipRay.Ocr.*.png").Length;
        using var source = CreateBitmap(320, 100, Color.White);
        using (var graphics = Graphics.FromImage(source))
        using (var font = new System.Drawing.Font("Arial", 36, System.Drawing.FontStyle.Bold))
        {
            graphics.DrawString("TEST 42", font, Brushes.Black, new PointF(12, 16));
        }
        var result = new OcrService().RecognizeAsync(source, CancellationToken.None).GetAwaiter().GetResult();
        Equal(true, !string.IsNullOrWhiteSpace(result.Status));
        if (result.IsSuccess)
        {
            Equal(true, result.Blocks.Count > 0);
            foreach (var block in result.Blocks)
            {
                Equal(true, block.Bounds.Left >= 0 && block.Bounds.Top >= 0);
                Equal(true, block.Bounds.Right <= source.Width && block.Bounds.Bottom <= source.Height);
            }
        }
        else
        {
            Equal(true, result.Status.Contains("local", StringComparison.OrdinalIgnoreCase));
        }
        Console.WriteLine($"OCR_PROBE {result.Status}");
        Equal(tempBefore, Directory.GetFiles(Path.GetTempPath(), "ClipRay.Ocr.*.png").Length);
    }

    private static void RedactsHarnessFailureMessages()
    {
        const string sentinel = "fake-secret-must-not-print";
        var formatted = FormatFailure("redaction", new InvalidOperationException(sentinel));
        Equal(false, formatted.Contains(sentinel, StringComparison.Ordinal));
        Equal(true, formatted.Contains(nameof(InvalidOperationException), StringComparison.Ordinal));
    }

    private static void RoutesLocalLmtVerificationThroughActualProvider()
    {
        var localCalls = 0;
        var deepSeekCalls = 0;
        var result = SettingsProviderTestRouter.TestAsync(
                "Local LMT",
                _ => { localCalls++; return Task.FromResult(new TranslationResult(true, "今天天气很好。", "Local LMT completed (Vulkan).")); },
                _ => { deepSeekCalls++; return Task.FromResult(new TranslationResult(true, "unused", "unused")); },
                CancellationToken.None)
            .GetAwaiter().GetResult();

        Equal(true, result.IsSuccess);
        Equal(1, localCalls);
        Equal(0, deepSeekCalls);
    }

    private static void MapsEnterToConfirm()
    {
        Equal(OverlayKeyboardAction.Confirm, OverlayKeyboardPolicy.Resolve(System.Windows.Input.Key.Enter, longCaptureActive: false));
        Equal(OverlayKeyboardAction.Escape, OverlayKeyboardPolicy.Resolve(System.Windows.Input.Key.Escape, longCaptureActive: false));
    }

    private static void StartsLockedSizeMovementFromBorder()
    {
        var selection = new PixelRect(100, 80, 320, 180);
        Equal(true, OverlayMovePolicy.ShouldBeginLockedMove(moveToolActive: true, selection, new PixelPoint(100, 140), borderTolerance: 6));
        Equal(true, OverlayMovePolicy.ShouldBeginLockedMove(moveToolActive: true, selection, new PixelPoint(260, 150), borderTolerance: 6));
        Equal(false, OverlayMovePolicy.ShouldBeginLockedMove(moveToolActive: false, selection, new PixelPoint(100, 140), borderTolerance: 6));
    }

    private static void KeepsMosaicCoverageCircular()
    {
        using var source = new Bitmap(41, 41);
        for (var y = 0; y < source.Height; y++)
        for (var x = 0; x < source.Width; x++)
            source.SetPixel(x, y, Color.FromArgb(255, x * 5 % 255, y * 5 % 255, (x + y) * 3 % 255));

        var layer = new EditLayer(new PixelRect(0, 0, 41, 41));
        layer.AddMosaicStroke(new MosaicStroke([new PixelPoint(19, 19)], brushSize: 20, blockSize: 4));
        using var output = EditRenderer.Composite(source, layer);

        Equal(source.GetPixel(12, 12), output.GetPixel(12, 12));
        Equal(false, source.GetPixel(19, 19).ToArgb() == output.GetPixel(19, 19).ToArgb());
    }

    private static void ComposesActiveMosaicBeforeMouseRelease()
    {
        using var source = new Bitmap(32, 32);
        for (var y = 0; y < source.Height; y++)
        for (var x = 0; x < source.Width; x++)
            source.SetPixel(x, y, Color.FromArgb(255, x * 7 % 255, y * 7 % 255, (x + y) * 5 % 255));

        var layer = new EditLayer(new PixelRect(0, 0, 32, 32));
        var active = new MosaicStroke([new PixelPoint(18, 18)], brushSize: 18, blockSize: 4);
        using var preview = EditRenderer.CompositePreview(source, layer, active);

        Equal(0, layer.MosaicStrokes.Count);
        Equal(false, source.GetPixel(18, 18).ToArgb() == preview.GetPixel(18, 18).ToArgb());
    }

    private static void FormatsExtractedOcrTextByVisualLine()
    {
        var blocks = new[]
        {
            new OcrTextBlock("第一行", new PixelRect(10, 10, 80, 14), []),
            new OcrTextBlock("第二行", new PixelRect(10, 31, 80, 14), []),
            new OcrTextBlock("第三行", new PixelRect(10, 52, 80, 14), []),
        };

        Equal("第一行\r\n第二行\r\n第三行", OcrTextLayout.FormatLines(blocks));
    }

    private static void ResizesSelectionAroundCenter()
    {
        var resized = SelectionSizePolicy.ResizeAroundCenter(
            new PixelRect(100, 100, 400, 200),
            width: 600,
            height: 300,
            new PixelRect(0, 0, 1920, 1080));
        Equal(new PixelRect(0, 50, 600, 300), resized);

        var clamped = SelectionSizePolicy.ResizeAroundCenter(
            new PixelRect(1700, 900, 200, 100),
            width: 500,
            height: 400,
            new PixelRect(0, 0, 1920, 1080));
        Equal(new PixelRect(1420, 680, 500, 400), clamped);
    }

    private static void UndoesRetainedOperationsInDrawingOrder()
    {
        var layer = new EditLayer(new PixelRect(0, 0, 100, 100));
        layer.AddPenStroke(new PenStroke([new PixelPoint(10, 10)], Color.Red, 3));
        layer.AddMosaicStroke(new MosaicStroke([new PixelPoint(20, 20)], 24, 12));

        Equal(true, layer.UndoLast());
        Equal(1, layer.PenStrokes.Count);
        Equal(0, layer.MosaicStrokes.Count);
        Equal(true, layer.UndoLast());
        Equal(0, layer.PenStrokes.Count);
        Equal(false, layer.UndoLast());
    }

    private static void RedoesRetainedEditsAndClearsTheRedoBranch()
    {
        var layer = new EditLayer(new PixelRect(0, 0, 100, 100));
        var pen = new PenStroke([new PixelPoint(10, 10)], Color.Red, 3);
        var mosaic = new MosaicStroke([new PixelPoint(20, 20)], 24, 12);
        layer.AddPenStroke(pen);
        layer.AddMosaicStroke(mosaic);

        Equal(true, layer.UndoLast());
        Equal(true, layer.UndoLast());
        Equal(true, layer.RedoLast());
        Equal(1, layer.PenStrokes.Count);
        Equal(0, layer.MosaicStrokes.Count);
        Equal(true, layer.RedoLast());
        Equal(1, layer.MosaicStrokes.Count);
        Equal(false, layer.RedoLast());

        Equal(true, layer.UndoLast());
        layer.AddPenStroke(new PenStroke([new PixelPoint(30, 30)], Color.Blue, 2));
        Equal(false, layer.RedoLast());
        Equal(2, layer.PenStrokes.Count);
        Equal(0, layer.MosaicStrokes.Count);
    }

    private static void FormatsLivePickerColor()
    {
        var display = ColorDisplay.Format(Color.FromArgb(0x12, 0xA0, 0xFF));
        Equal("#12A0FF", display.Hex);
        Equal("RGB(18, 160, 255)", display.Rgb);
    }

    private static void CapturesHotkeyFromKeyState()
    {
        var hotkey = HotkeyCapturePolicy.Capture(
            System.Windows.Input.Key.A,
            System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Windows);
        Equal("Ctrl+Win+A", hotkey.ToString());
    }

    private static void ResolvesImeAndSystemKeysForHotkeyCapture()
    {
        Equal(
            System.Windows.Input.Key.A,
            HotkeyCapturePolicy.ResolveEventKey(
                System.Windows.Input.Key.ImeProcessed,
                System.Windows.Input.Key.None,
                System.Windows.Input.Key.A,
                System.Windows.Input.Key.None));
        Equal(
            System.Windows.Input.Key.OemQuestion,
            HotkeyCapturePolicy.ResolveEventKey(
                System.Windows.Input.Key.System,
                System.Windows.Input.Key.OemQuestion,
                System.Windows.Input.Key.None,
                System.Windows.Input.Key.None));
        Equal(
            System.Windows.Input.Key.D1,
            HotkeyCapturePolicy.ResolveEventKey(
                System.Windows.Input.Key.DeadCharProcessed,
                System.Windows.Input.Key.None,
                System.Windows.Input.Key.None,
                System.Windows.Input.Key.D1));

        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var settingsSource = File.ReadAllText(Path.Combine(
            workspace, "src", "ClipRay.App", "Settings", "SettingsWindow.xaml.cs"));
        var handler = settingsSource.IndexOf(
            "private void HotkeyBox_PreviewKeyDown", StringComparison.Ordinal);
        var capture = settingsSource.IndexOf(
            "HotkeyCapturePolicy.Capture(key", handler, StringComparison.Ordinal);
        var resolver = settingsSource.IndexOf(
            "HotkeyCapturePolicy.ResolveEventKey(", handler, StringComparison.Ordinal);

        Equal(true, handler >= 0 && resolver > handler && resolver < capture);
    }

    private static void RejectsEveryNonRegisterableWpfKeyFromHotkeys()
    {
        var modifierKeys = new HashSet<System.Windows.Input.Key>
        {
            System.Windows.Input.Key.LeftCtrl,
            System.Windows.Input.Key.RightCtrl,
            System.Windows.Input.Key.LeftShift,
            System.Windows.Input.Key.RightShift,
            System.Windows.Input.Key.LeftAlt,
            System.Windows.Input.Key.RightAlt,
            System.Windows.Input.Key.LWin,
            System.Windows.Input.Key.RWin,
        };

        foreach (var key in Enum.GetValues<System.Windows.Input.Key>().Distinct())
        {
            var isRegisterable = key != System.Windows.Input.Key.None
                && !modifierKeys.Contains(key)
                && key is not System.Windows.Input.Key.System
                    and not System.Windows.Input.Key.ImeProcessed
                    and not System.Windows.Input.Key.DeadCharProcessed
                && System.Windows.Input.KeyInterop.VirtualKeyFromKey(key) != 0;
            if (!isRegisterable)
            {
                Throws<ArgumentException>(() => HotkeyCapturePolicy.Capture(key, System.Windows.Input.ModifierKeys.None));
                continue;
            }

            var hotkey = HotkeyCapturePolicy.Capture(key, System.Windows.Input.ModifierKeys.None);
            Equal(true, hotkey.ToVirtualKey() != 0);
        }
    }

    private static void FormatsPrintableHotkeysWithLayoutSymbols()
    {
        var resolver = new StubHotkeyKeyLabelResolver((key, shift) => (key, shift) switch
        {
            (System.Windows.Input.Key.D1, false) => "1",
            (System.Windows.Input.Key.D1, true) => "!",
            (System.Windows.Input.Key.OemQuestion, false) => "/",
            (System.Windows.Input.Key.OemQuestion, true) => "?",
            (System.Windows.Input.Key.OemPlus, false) => "=",
            (System.Windows.Input.Key.OemPlus, true) => "+",
            _ => null,
        });

        Equal("1", HotkeyDisplayFormatter.Format(GlobalHotkey.Parse("D1"), resolver));
        Equal("Shift+!", HotkeyDisplayFormatter.Format(GlobalHotkey.Parse("Shift+D1"), resolver));
        Equal("Ctrl+/", HotkeyDisplayFormatter.Format(GlobalHotkey.Parse("Ctrl+OemQuestion"), resolver));
        Equal("Ctrl+Shift+?", HotkeyDisplayFormatter.Format(GlobalHotkey.Parse("Ctrl+Shift+OemQuestion"), resolver));
        Equal("Ctrl+Shift++", HotkeyDisplayFormatter.Format(GlobalHotkey.Parse("Ctrl+Shift+OemPlus"), resolver));
    }

    private static void KeepsNumpadHotkeysDistinctFromPrintableSymbols()
    {
        var resolver = new StubHotkeyKeyLabelResolver(static (key, _) =>
            key == System.Windows.Input.Key.Separator ? "," : null);

        Equal("Num 1", HotkeyDisplayFormatter.Format(GlobalHotkey.Parse("NumPad1"), resolver));
        Equal("Ctrl+Num +", HotkeyDisplayFormatter.Format(GlobalHotkey.Parse("Ctrl+Add"), resolver));
        Equal("Num -", HotkeyDisplayFormatter.Format(GlobalHotkey.Parse("Subtract"), resolver));
        Equal("Num /", HotkeyDisplayFormatter.Format(GlobalHotkey.Parse("Divide"), resolver));
        Equal("Num ,", HotkeyDisplayFormatter.Format(GlobalHotkey.Parse("Separator"), resolver));
    }

    private static void HidesWpfEnumNamesInCurrentLayoutHotkeyLabels()
    {
        foreach (var keyName in new[]
                 {
                     "D1",
                     "OemQuestion",
                     "OemSemicolon",
                     "OemPlus",
                     "OemMinus",
                     "OemOpenBrackets",
                     "OemCloseBrackets",
                     "OemPipe",
                     "OemQuotes",
                     "OemTilde",
                 })
        {
            var display = HotkeyDisplayFormatter.Format(GlobalHotkey.Parse($"Ctrl+{keyName}"));
            Equal(false, display.Contains("Oem", StringComparison.OrdinalIgnoreCase));
            Equal(false, display.EndsWith($"+{keyName}", StringComparison.OrdinalIgnoreCase));
        }
    }

    private static void KeepsHotkeyDisplayTextOutOfPersistedSettings()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var source = File.ReadAllText(Path.Combine(
            workspace,
            "src",
            "ClipRay.App",
            "Settings",
            "SettingsWindow.xaml.cs"));

        Equal(true, source.Contains("SetHotkeyValue", StringComparison.Ordinal));
        Equal(true, source.Contains("GetRequiredHotkeyValue", StringComparison.Ordinal));
        Equal(true, source.Contains("GetOptionalHotkeyValue", StringComparison.Ordinal));
        Equal(false, source.Contains("GlobalHotkey.Parse(RegionHotkeyBox.Text)", StringComparison.Ordinal));
        Equal(false, source.Contains("GlobalHotkey.Parse(CurrentHotkeyBox.Text)", StringComparison.Ordinal));
        Equal(false, source.Contains("box.Text = hotkey.ToString()", StringComparison.Ordinal));
    }

    private static void RecoversHeldWindowsKeysWhenWpfOmitsModifier()
    {
        foreach (var (leftWindowsDown, rightWindowsDown) in new[] { (true, false), (false, true) })
        {
            var modifiers = ResolveHotkeyModifiers(
                System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift | System.Windows.Input.ModifierKeys.Alt,
                leftWindowsDown,
                rightWindowsDown);
            var hotkey = HotkeyCapturePolicy.Capture(System.Windows.Input.Key.K, modifiers);

            Equal("Ctrl+Shift+Alt+Win+K", hotkey.ToString());
        }
    }

    private static void DoesNotRetainReleasedWindowsKeys()
    {
        var modifiers = ResolveHotkeyModifiers(
            System.Windows.Input.ModifierKeys.Control,
            leftWindowsDown: false,
            rightWindowsDown: false);
        var hotkey = HotkeyCapturePolicy.Capture(System.Windows.Input.Key.K, modifiers);

        Equal("Ctrl+K", hotkey.ToString());
    }

    private static System.Windows.Input.ModifierKeys ResolveHotkeyModifiers(
        System.Windows.Input.ModifierKeys observedModifiers,
        bool leftWindowsDown,
        bool rightWindowsDown)
    {
        var method = typeof(HotkeyCapturePolicy).GetMethod(
            "ResolveModifiers",
            [typeof(System.Windows.Input.ModifierKeys), typeof(bool), typeof(bool)]);
        if (method is null)
        {
            throw new InvalidOperationException("Hotkey capture does not resolve the physical Windows-key state.");
        }

        return (System.Windows.Input.ModifierKeys)method.Invoke(
            obj: null,
            [observedModifiers, leftWindowsDown, rightWindowsDown])!;
    }

    private static void AllowsOutsideLongCaptureControlWithoutAffinity()
    {
        Equal(true, ScrollCaptureControlReadiness.CanStart(affinityApplied: false, placedOutside: true));
        Equal(true, ScrollCaptureControlReadiness.CanStart(affinityApplied: true, placedOutside: false));
        Equal(false, ScrollCaptureControlReadiness.CanStart(affinityApplied: false, placedOutside: false));
    }

    private static void ExposesVisibleRecordingStopControl()
    {
        var control = new RecordingControlWindow(new Rectangle(100, 100, 640, 360));
        var stopped = false;
        control.StopRequested += (_, _) => stopped = true;
        control.Show();
        control.UpdateLayout();
        var panel = (System.Windows.Controls.StackPanel)((System.Windows.Controls.Border)control.Content).Child;
        ((System.Windows.Controls.Button)panel.Children[2]).RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        Equal(true, stopped);
        control.Close();
    }

    private static void CopiesFinalRecordingAsClipboardFileDrop()
    {
        using var workspace = TemporaryWorkspace.Create();
        var path = Path.Combine(workspace.Root, "capture.mp4");
        File.WriteAllBytes(path, [0, 1, 2, 3]);
        var clipboard = new RecordingFileClipboardSink();

        Equal(true, RecordingResultDelivery.TryCopyFile(path, clipboard));
        Equal(true, string.Equals(Path.GetFullPath(path), clipboard.Path, StringComparison.OrdinalIgnoreCase));
        Equal(false, RecordingResultDelivery.TryCopyFile(Path.Combine(workspace.Root, "missing.mp4"), clipboard));
    }

    private static void NormalizesLocalLmtLanguageNames()
    {
        Equal("Chinese", LocalLmtLanguage.Normalize("简体中文"));
        Equal("Chinese", LocalLmtLanguage.Normalize("zh-CN"));
        Equal("English", LocalLmtLanguage.Normalize("English"));
        Equal("English", LocalLmtLanguage.NormalizeSource("auto", "Hello world"));
        Equal("Japanese", LocalLmtLanguage.NormalizeSource("auto", "ゲームを開始"));
    }

    private static void DetectsDominantScriptForMixedLocalText()
    {
        Equal("English", LocalLmtLanguage.NormalizeSource("auto", "设置 OpenAI will release a new model tomorrow"));
        Equal("Chinese", LocalLmtLanguage.NormalizeSource("auto", "这是中文界面，保留 DeepSeek 品牌名称"));
    }

    private static string FormatFailure(string name, Exception exception) =>
        $"FAIL {name}: {exception.GetType().Name}";

    private static void RejectsRedirectsWithoutSecondOriginReplay()
    {
        using var production = DeepSeekTranslationProvider.CreateProductionHandler();
        Equal(false, production.AllowAutoRedirect);
        foreach (var status in new[] { HttpStatusCode.TemporaryRedirect, HttpStatusCode.PermanentRedirect })
        {
            var handler = new RecordingHttpHandler((_, _) => Task.FromResult(new HttpResponseMessage(status)
            {
                Headers = { Location = new Uri("https://second.invalid/collect") },
            }));
            var vault = new RecordingCredentialVault("redirect-secret");
            var result = new DeepSeekTranslationProvider(vault, handler, TimeSpan.FromSeconds(1))
                .TranslateAsync(new TranslationRequest("English", "简体中文", "private OCR text"), "alias", CancellationToken.None)
                .GetAwaiter().GetResult();
            Equal(false, result.IsSuccess);
            Equal(1, handler.CallCount);
            Equal(new Uri("https://api.deepseek.com/chat/completions"), handler.RequestUri!);
            Equal(false, result.Status.Contains("second.invalid", StringComparison.OrdinalIgnoreCase));
        }

        ProbeProductionRedirect(HttpStatusCode.TemporaryRedirect);
        ProbeProductionRedirect(HttpStatusCode.PermanentRedirect);
    }

    private static void ProbeProductionRedirect(HttpStatusCode status)
    {
        var firstPort = GetFreeTcpPort();
        var secondPort = GetFreeTcpPort();
        using var first = new HttpListener();
        using var second = new HttpListener();
        first.Prefixes.Add($"http://127.0.0.1:{firstPort}/");
        second.Prefixes.Add($"http://127.0.0.1:{secondPort}/");
        first.Start();
        second.Start();
        var firstRequest = first.GetContextAsync();
        var secondRequest = second.GetContextAsync();
        var responder = Task.Run(async () =>
        {
            var context = await firstRequest;
            using var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding);
            var body = await reader.ReadToEndAsync();
            var authorization = context.Request.Headers["Authorization"];
            context.Response.StatusCode = (int)status;
            context.Response.RedirectLocation = $"http://127.0.0.1:{secondPort}/collect";
            context.Response.Close();
            return (body, authorization);
        });
        using var handler = DeepSeekTranslationProvider.CreateProductionHandler();
        using var client = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{firstPort}/start")
        {
            Content = new StringContent("private OCR body", Encoding.UTF8, "text/plain"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "redirect-key");
        using var response = client.Send(request);
        var observed = responder.GetAwaiter().GetResult();
        Equal(status, response.StatusCode);
        Equal("private OCR body", observed.body);
        Equal("Bearer redirect-key", observed.authorization!);
        Equal(false, secondRequest.Wait(TimeSpan.FromMilliseconds(200)));
        second.Stop();
        try { secondRequest.GetAwaiter().GetResult(); } catch { }
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static void RejectsInvalidLocalLmtArtifactsBeforeLaunching()
    {
        var sessions = new RecordingLocalLmtSessionFactory("unused");
        var provider = new LocalTranslationProvider(new LocalLmtOptions("missing.gguf", "missing-cli.exe"), sessions);

        var result = provider.TranslateAsync(new TranslationRequest("English", "Chinese", "hello"), CancellationToken.None).GetAwaiter().GetResult();

        Equal(false, result.IsSuccess);
        Equal("Local LMT model is unavailable or failed verification.", result.Status);
        Equal(true, result.Diagnostic is not null);
        Equal(
            new TranslationDiagnostic(TranslationDiagnosticKind.ModelUnavailable, TranslationDiagnosticStage.Initial),
            result.Diagnostic!);
        Equal(0, sessions.StartCalls);
    }

    private static void VerifiesTranslationFileInventory()
    {
        using var install = TemporaryWorkspace.Create();
        var missing = TranslationFileCheck.Inspect(TranslationRouteKind.Machine, install.Root)!;
        Equal(6, missing.MissingFiles.Count);
        foreach (var path in missing.MissingFiles)
        {
            var full = Path.Combine(install.Root, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, "fixture-only-not-a-runtime");
        }
        var present = TranslationFileCheck.Inspect(TranslationRouteKind.Machine, install.Root)!;
        Equal(0, present.MissingFiles.Count);
        Equal(true, present.Describe("en").Contains("not verified"));
        Equal(true, present.Describe("zh-CN").Contains("未验证"));
        Equal(true, TranslationFileCheck.Inspect(TranslationRouteKind.DeepSeekFlash, install.Root) is null);
        var commonRoot = Path.Combine(install.Root, "machine-translation", "models", "small100");
        Directory.CreateDirectory(commonRoot);
        foreach (var file in new[] { "model.bin", "config.json", "shared_vocabulary.json", "sentencepiece.bpe.model" })
            File.WriteAllText(Path.Combine(commonRoot, file), "fixture");
        var common = TranslationFileCheck.Inspect(TranslationRouteKind.Machine, install.Root)!;
        Equal("small100", common.ModelName);
        Equal(0, common.MissingFiles.Count);
        Console.WriteLine("Translation file inventory checks passed; no runtime readiness inferred.");
    }
    private static void ResolvesAuditedDeveloperLocalLmtModels()
    {
        Equal(string.Empty, LocalLmtModelCatalog.ExpectedSha256For("other-model.gguf"));
        Equal(string.Empty, LocalLmtModelCatalog.ExpectedSha256For(null));
        Equal("Local LMT · 未配置模型", LocalLmtModelCatalog.DisplayNameFor(null));
        Equal("Local LMT · 未识别模型（other-model.gguf）", LocalLmtModelCatalog.DisplayNameFor("other-model.gguf"));
        Equal(LocalLmtModelCatalog.DisplayNameFor("other-model.gguf"), LocalLmtModelCatalog.ProviderChoiceNameFor("other-model.gguf"));
        Equal(
            "9e9326981457f09e6cd66ea57aa9d879dc142edb226dee0feebf6bef317cf6e0",
            LocalLmtModelCatalog.ExpectedSha256For("LMT-60-1.7B-Q4_K_M.gguf"));
        Equal(
            "743c6cdc13294b5c470ec0e89e79d64f96ef0cab94ce1285c78da4dc0f56be5c",
            LocalLmtModelCatalog.ExpectedSha256For("LMT-60-0.6B.Q4_K_M.gguf"));
        Equal(
            "LMT-60 · 0.6B 参数 · Q4_K_M（默认）",
            LocalLmtModelCatalog.DisplayNameFor("C:\\models\\LMT-60-0.6B.Q4_K_M.gguf"));
        var developmentModels = LocalLmtModelCatalog.ResolveDevelopmentModels("E:\\ClipRay");
        Equal(2, developmentModels.Count);
        Equal(
            "E:\\ClipRay\\artifacts\\models\\NiuTrans-LMT-60-0.6B\\LMT-60-0.6B.Q4_K_M.gguf",
            developmentModels[0].Path);
        Equal(
            "E:\\ClipRay\\artifacts\\models\\NiuTrans-LMT-60-1.7B\\LMT-60-1.7B-Q4_K_M.gguf",
            developmentModels[1].Path);
    }

    private static void PreservesExplicitLocalModelWhenMissing()
    {
        using var install = TemporaryWorkspace.Create();
        var packaged = Path.Combine(install.Root, "models", "local-lmt", "LMT-60-0.6B.Q4_K_M.gguf");
        Directory.CreateDirectory(Path.GetDirectoryName(packaged)!);
        File.WriteAllText(packaged, "not a real model; discovery fixture only");
        var selected = Path.Combine(install.Root, "user-choice", "LMT-60-1.7B-Q4_K_M.gguf");
        Equal(selected, LocalLmtOptions.Discover(selected, install.Root).ModelPath);
        Equal(LocalLmtOptions.Q4ModelSha256, LocalLmtOptions.Discover(selected, install.Root).ExpectedModelSha256!);
    }

    private static void ListsPackagedLocalLmtModelForInstalledApplication()
    {
        using var install = TemporaryWorkspace.Create();
        var packagedModelPath = Path.Combine(
            install.Root,
            "models",
            "local-lmt",
            "LMT-60-0.6B.Q4_K_M.gguf");
        Directory.CreateDirectory(Path.GetDirectoryName(packagedModelPath)!);
        File.WriteAllText(packagedModelPath, "fixture-model");

        var method = typeof(LocalLmtModelCatalog).GetMethod(
            "ResolveProviderModels",
            [typeof(string), typeof(string)]);
        Equal(true, method is not null);
        if (method is null) return;

        var options = (IReadOnlyList<LocalLmtDevelopmentModelOption>)method.Invoke(
            null,
            [install.Root, null])!;
        Equal(1, options.Count);
        Equal(packagedModelPath, options[0].Path);
        Equal(true, options[0].IsInstalled);
    }

    private static void UsesTopProviderSelectorAsOnlyLocalModelChoice()
    {
        var workspaceRoot = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var source = File.ReadAllText(Path.Combine(
            workspaceRoot,
            "src", "ClipRay.App", "Settings", "SettingsWindow.xaml.cs"));
        var xaml = File.ReadAllText(Path.Combine(
            workspaceRoot,
            "src", "ClipRay.App", "Settings", "SettingsWindow.xaml"));

        Equal(false, source.Contains("#if DEBUG", StringComparison.Ordinal));
        Equal(true, source.Contains("InitializeDevelopmentLocalModels();", StringComparison.Ordinal));
        Equal(true, source.Contains("SelectProviderChoice(_settings.Provider, LocalLmtModelPathBox.Text);", StringComparison.Ordinal));
        Equal(true, source.Contains("SelectLocalProviderOption(LocalLmtModelPathBox.Text);", StringComparison.Ordinal));
        Equal(false, source.Contains("SelectComboValue(ProviderBox, \"Local LMT\")", StringComparison.Ordinal));
        Equal(true, source.Contains("FirstOrDefault(item => Equals(item.Tag, value) || Equals(item.Content, value))", StringComparison.Ordinal));
        Equal(false, source.Contains("_developmentLocalModelBox", StringComparison.Ordinal));
        Equal(true, source.Contains("ProviderBox.Items.Add(item);", StringComparison.Ordinal));
        Equal(false, source.Contains("LocalModelContentPanel.Children.Insert", StringComparison.Ordinal));
        Equal(false, xaml.Contains("Click=\"ChooseLocalModel_Click\"", StringComparison.Ordinal));
        Equal(false, xaml.Contains("Click=\"AutoDetectLocalModel_Click\"", StringComparison.Ordinal));
        Equal(
            "LMT-60 · 1.7B 参数 · Q4_K_M（备用）",
            LocalLmtModelCatalog.DisplayNameFor("C:\\models\\LMT-60-1.7B-Q4_K_M.gguf"));
        Equal(
            "LMT-60 · 0.6B 参数 · Q4_K_M（默认）",
            LocalLmtModelCatalog.DisplayNameFor("C:\\models\\LMT-60-0.6B.Q4_K_M.gguf"));
        Equal(
            "Local LMT · 1.7B 参数（备用）",
            LocalLmtModelCatalog.ProviderChoiceNameFor("C:\\models\\LMT-60-1.7B-Q4_K_M.gguf"));
        Equal(
            "Local LMT · 0.6B 参数（默认）",
            LocalLmtModelCatalog.ProviderChoiceNameFor("C:\\models\\LMT-60-0.6B.Q4_K_M.gguf"));
    }

    private static void RunsLocalLmtThroughPersistentPrivateSession()
    {
        using var workspace = TemporaryWorkspace.Create();
        var model = Path.Combine(workspace.Root, "LMT-60-1.7B-Q4_K_M.gguf");
        var cli = Path.Combine(workspace.Root, "llama-cli.exe");
        File.WriteAllText(model, "fixture-model");
        File.WriteAllText(cli, "fixture-cli");
        var sessions = new RecordingLocalLmtSessionFactory("今天天气很好。");
        using var provider = new LocalTranslationProvider(new LocalLmtOptions(model, cli, ExpectedModelSha256: LocalLmtArtifactVerifier.Sha256File(model)), sessions);

        var result = provider.TranslateAsync(new TranslationRequest("English", "Chinese", "The weather is pleasant today."), CancellationToken.None).GetAwaiter().GetResult();

        Equal(true, result.IsSuccess);
        Equal("今天天气很好。", result.Text);
        Equal(1, sessions.StartCalls);
        Equal(1, sessions.Prompts.Count);
        Equal(true, sessions.Start!.Arguments.Contains("--conversation", StringComparison.Ordinal));
        Equal(false, sessions.Start.Arguments.Contains("--jinja", StringComparison.Ordinal));
        Equal(true, sessions.Start.Arguments.Contains("--escape", StringComparison.Ordinal));
        Equal(true, sessions.Start.Arguments.Contains("--temp 0", StringComparison.Ordinal));
        Equal(false, sessions.Start.Arguments.Contains("--top-k", StringComparison.Ordinal));
        Equal(false, sessions.Start.Arguments.Contains("--top-p", StringComparison.Ordinal));
        Equal(true, sessions.Start.Arguments.Contains("--reasoning-budget 0", StringComparison.Ordinal));
        Equal(false, sessions.Start.Arguments.Contains("--reasoning off", StringComparison.Ordinal));
        Equal(false, sessions.Start.Arguments.Contains("http", StringComparison.OrdinalIgnoreCase));
        Equal(
            "Translate the following text from English into Chinese:\\nEnglish: The weather is pleasant today.\\nChinese:",
            sessions.Prompts[0]);
        Equal(false, sessions.Prompts[0].Contains("JSON", StringComparison.OrdinalIgnoreCase));
        Equal(false, sessions.Prompts[0].Contains("reasoning", StringComparison.OrdinalIgnoreCase));
    }

    private static void UsesOutputOnlyLocalLmtTemplateOnFirstAttempt()
    {
        using var workspace = TemporaryWorkspace.Create();
        var model = Path.Combine(workspace.Root, "model.gguf");
        var cli = Path.Combine(workspace.Root, "llama-cli.exe");
        File.WriteAllText(model, "fixture-model");
        File.WriteAllText(cli, "fixture-cli");
        var sessions = new RecordingLocalLmtSessionFactory("今天天气很好。");
        using var provider = new LocalTranslationProvider(
            new LocalLmtOptions(model, cli, ExpectedModelSha256: LocalLmtArtifactVerifier.Sha256File(model)),
            sessions);

        var result = provider.TranslateAsync(
            new TranslationRequest("English", "Chinese", "The weather is pleasant today.", textKind: TranslationTextKind.Body),
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(true, result.IsSuccess);
        Equal(
            "Translate the following text from English into Chinese:\\nEnglish: The weather is pleasant today.\\nChinese:",
            sessions.Prompts.Single());
    }

    private static void StripsLocalLmtSessionControlOutput()
    {
        using var workspace = TemporaryWorkspace.Create();
        var model = Path.Combine(workspace.Root, "model.gguf");
        var cli = Path.Combine(workspace.Root, "llama-cli.exe");
        File.WriteAllText(model, "fixture-model");
        File.WriteAllText(cli, "fixture-cli");
        var sessions = new RecordingLocalLmtSessionFactory("Chat history cleared.\n今天天气很好。");
        using var provider = new LocalTranslationProvider(
            new LocalLmtOptions(model, cli, ExpectedModelSha256: LocalLmtArtifactVerifier.Sha256File(model)),
            sessions);

        var result = provider.TranslateAsync(
            new TranslationRequest("English", "Chinese", "The weather is pleasant today.", textKind: TranslationTextKind.Body),
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(true, result.IsSuccess);
        Equal("今天天气很好。", result.Text);
    }

    private static void RecoversOutputOnlyLocalLmtTranslatedTailAfterPromptEcho()
    {
        using var workspace = TemporaryWorkspace.Create();
        var model = Path.Combine(workspace.Root, "model.gguf");
        var cli = Path.Combine(workspace.Root, "llama-cli.exe");
        File.WriteAllText(model, "fixture-model");
        File.WriteAllText(cli, "fixture-cli");
        const string source = "The weather is pleasant today.";
        var sessions = new RecordingLocalLmtSessionFactory(
            $"Translate from English into Chinese. Output only the translated text without any language label. Text: {source}\n今天天气很好。");
        using var provider = new LocalTranslationProvider(
            new LocalLmtOptions(model, cli, ExpectedModelSha256: LocalLmtArtifactVerifier.Sha256File(model)),
            sessions);

        var result = provider.TranslateAsync(
            new TranslationRequest("English", "Chinese", source, textKind: TranslationTextKind.Body),
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(true, result.IsSuccess);
        Equal("今天天气很好。", result.Text);
    }

    private static void UsesBoundedFirstLocalLmtResponseLine()
    {
        using var workspace = TemporaryWorkspace.Create();
        var model = Path.Combine(workspace.Root, "model.gguf");
        var cli = Path.Combine(workspace.Root, "llama-cli.exe");
        File.WriteAllText(model, "fixture-model");
        File.WriteAllText(cli, "fixture-cli");
        var sessions = new RecordingLocalLmtSessionFactory($"今天天气很好。\n{new string('x', 200)}");
        using var provider = new LocalTranslationProvider(
            new LocalLmtOptions(model, cli, ExpectedModelSha256: LocalLmtArtifactVerifier.Sha256File(model)),
            sessions);

        var result = provider.TranslateAsync(
            new TranslationRequest("English", "Chinese", "The weather is pleasant today.", textKind: TranslationTextKind.Body),
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(true, result.IsSuccess);
        Equal("今天天气很好。", result.Text);
    }

    private static void RecoversLocalLmtTranslatedTailAfterPromptEcho()
    {
        using var workspace = TemporaryWorkspace.Create();
        var model = Path.Combine(workspace.Root, "model.gguf");
        var cli = Path.Combine(workspace.Root, "llama-cli.exe");
        File.WriteAllText(model, "fixture-model");
        File.WriteAllText(cli, "fixture-cli");
        var sessions = new RecordingLocalLmtSessionFactory(
            "Translate the following text from English into Chinese: English: The weather is pleasant today. Chinese: 今天天气很好。",
            "Translate the following text from English into Chinese: English: The weather is pleasant today. Chinese: 今天天气很好。");
        using var provider = new LocalTranslationProvider(
            new LocalLmtOptions(model, cli, ExpectedModelSha256: LocalLmtArtifactVerifier.Sha256File(model)),
            sessions);

        var result = provider.TranslateAsync(
            new TranslationRequest("English", "Chinese", "The weather is pleasant today.", textKind: TranslationTextKind.Body),
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(true, result.IsSuccess);
        Equal("今天天气很好。", result.Text);
        Equal(1, sessions.Prompts.Count);
    }

    private static void ClassifiesOversizedLocalLmtOutput()
    {
        using var workspace = TemporaryWorkspace.Create();
        var model = Path.Combine(workspace.Root, "model.gguf");
        var cli = Path.Combine(workspace.Root, "llama-cli.exe");
        File.WriteAllText(model, "fixture-model");
        File.WriteAllText(cli, "fixture-cli");
        var sessions = new RecordingLocalLmtSessionFactory(
            new string('x', 300),
            new string('x', 300),
            new string('x', 300));
        using var provider = new LocalTranslationProvider(
            new LocalLmtOptions(model, cli, ExpectedModelSha256: LocalLmtArtifactVerifier.Sha256File(model)),
            sessions);

        var result = provider.TranslateAsync(
            new TranslationRequest("English", "Chinese", "Short source.", textKind: TranslationTextKind.Body),
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(false, result.IsSuccess);
        Equal(TranslationDiagnosticKind.OutputTooLong, result.Diagnostic!.Kind);
        Equal(string.Empty, result.Text);
    }

    private static void RetriesTargetLabelOnlyLocalLmtResponse()
    {
        using var workspace = TemporaryWorkspace.Create();
        var model = Path.Combine(workspace.Root, "model.gguf");
        var cli = Path.Combine(workspace.Root, "llama-cli.exe");
        File.WriteAllText(model, "fixture-model");
        File.WriteAllText(cli, "fixture-cli");
        var sessions = new RecordingLocalLmtSessionFactory("Chinese:", "今天天气很好。");
        using var provider = new LocalTranslationProvider(
            new LocalLmtOptions(model, cli, ExpectedModelSha256: LocalLmtArtifactVerifier.Sha256File(model)),
            sessions);

        var result = provider.TranslateAsync(
            new TranslationRequest("English", "Chinese", "The weather is pleasant today.", textKind: TranslationTextKind.Body),
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(true, result.IsSuccess);
        Equal("今天天气很好。", result.Text);
        Equal(2, sessions.Prompts.Count);
        Equal(sessions.Prompts[0], sessions.Prompts[1]);
    }

    private static void FallsBackToIsolatedLocalLmtExecution()
    {
        using var workspace = TemporaryWorkspace.Create();
        var model = Path.Combine(workspace.Root, "model.gguf");
        var cli = Path.Combine(workspace.Root, "llama-cli.exe");
        File.WriteAllText(model, "fixture-model");
        File.WriteAllText(cli, "fixture-cli");
        var sessions = new RecordingLocalLmtSessionFactory(string.Empty);
        var oneShot = new RecordingLocalLmtOneShotRunner("今天天气很好。");
        using var provider = new LocalTranslationProvider(
            new LocalLmtOptions(model, cli, ExpectedModelSha256: LocalLmtArtifactVerifier.Sha256File(model)),
            sessions,
            oneShotRunner: oneShot);

        var result = provider.TranslateAsync(
            new TranslationRequest("English", "Chinese", "The weather is pleasant today.", textKind: TranslationTextKind.Body),
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(false, result.IsSuccess);
        Equal(string.Empty, result.Text);
        Equal(0, oneShot.Calls);
    }

    private static void RemovesOneShotLocalLmtPromptEcho()
    {
        var prompt = "Translate the following text from English into Chinese: English: The weather is pleasant today. Chinese:";
        var output = LocalLmtOneShotRunner.RemoveCurrentPromptEcho(
            $"cli wrapper\n> {prompt}\nExiting...\ntranslated answer\nExiting...\n[ Prompt: 12.3 t/s | Generation: 9.1 t/s ]",
            prompt);

        Equal("translated answer", output);
    }

    private static void BuildsFileBackedLocalLmtFirstTurn()
    {
        var start = LocalLmtOneShotRunner.WithPromptFile(
            new LocalLmtSessionStart("llama-cli.exe", "--model model.gguf --single-turn"),
            @"C:\local temp\prompt.txt");

        Equal(true, start.Arguments.Contains("--file \"C:\\local temp\\prompt.txt\"", StringComparison.Ordinal));
        Equal(false, start.Arguments.Contains("Translate", StringComparison.Ordinal));
    }

    private static void DeletesShortLivedLocalLmtPromptFile()
    {
        using var workspace = TemporaryWorkspace.Create();
        string path;
        using (var promptFile = LocalLmtPromptFile.Create("private local text", workspace.Root))
        {
            path = promptFile.Path;
            Equal(true, File.Exists(path));
            Equal("private local text", File.ReadAllText(path, Encoding.UTF8));
        }

        Equal(false, File.Exists(path));
    }

    private static void RoutesProductionLocalLmtThroughFileBackedFirstTurn()
    {
        using var workspace = TemporaryWorkspace.Create();
        var model = Path.Combine(workspace.Root, "model.gguf");
        var cli = Path.Combine(workspace.Root, "llama-cli.exe");
        File.WriteAllText(model, "fixture-model");
        File.WriteAllText(cli, "fixture-cli");
        var sessions = new RecordingLocalLmtSessionFactory("unexpected interactive output");
        var oneShot = new RecordingLocalLmtOneShotRunner("今天天气很好。");
        using var provider = new LocalTranslationProvider(
            new LocalLmtOptions(model, cli, ExpectedModelSha256: LocalLmtArtifactVerifier.Sha256File(model)),
            sessions,
            oneShotRunner: oneShot,
            collectLocalDiagnosticContext: true);

        var result = provider.TranslateAsync(
            new TranslationRequest("English", "Chinese", "The weather is pleasant today.", textKind: TranslationTextKind.Body),
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(true, result.IsSuccess);
        Equal(1, oneShot.Calls);
        Equal(0, sessions.StartCalls);
        Equal(true, oneShot.Start!.Arguments.Contains("--no-conversation --single-turn", StringComparison.Ordinal));
        Equal(false, oneShot.Start.Arguments.Contains("--conversation", StringComparison.Ordinal));
    }

    private static void TranslatesLocalLmtVisualBatchWithOneModelLaunch()
    {
        using var workspace = TemporaryWorkspace.Create();
        var model = Path.Combine(workspace.Root, "model.gguf");
        var cli = Path.Combine(workspace.Root, "llama-cli.exe");
        File.WriteAllText(model, "fixture-model");
        File.WriteAllText(cli, "fixture-cli");
        var sessions = new RecordingLocalLmtSessionFactory("unexpected interactive output");
        var oneShot = new RecordingLocalLmtOneShotRunner("unexpected fallback");
        var batch = new RecordingLocalLmtBatchRunner("今天天气很好。", "欢迎回来。");
        using var provider = new LocalTranslationProvider(
            new LocalLmtOptions(model, cli, ExpectedModelSha256: LocalLmtArtifactVerifier.Sha256File(model)),
            sessions,
            oneShotRunner: oneShot,
            batchRunner: batch,
            collectLocalDiagnosticContext: true);

        var results = provider.TranslateManyAsync(
            [
                new TranslationRequest("English", "Chinese", "The weather is pleasant today.", textKind: TranslationTextKind.Body),
                new TranslationRequest("English", "Chinese", "Welcome back.", textKind: TranslationTextKind.Body),
            ],
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(1, batch.Calls);
        Equal(0, oneShot.Calls);
        Equal(true, results.All(static result => result.IsSuccess));
        Equal("今天天气很好。", results[0].Text);
        Equal("欢迎回来。", results[1].Text);
        Equal(true, batch.Prompts[0].StartsWith(
            "Translate the following text from English into Chinese:",
            StringComparison.Ordinal));
        Equal(true, batch.Prompts[0].Contains("English: The weather is pleasant today.", StringComparison.Ordinal));
        Equal(true, batch.Prompts[1].Contains("English: Welcome back.", StringComparison.Ordinal));
        Equal(true, batch.Start!.Arguments.Contains("--conversation", StringComparison.Ordinal));
        Equal(false, batch.Start.Arguments.Contains("--single-turn", StringComparison.Ordinal));
        Equal(true, batch.Start.ClearAfterEachResponse);
    }

    private static void RetriesOnlyMissingLocalLmtBatchItem()
    {
        using var workspace = TemporaryWorkspace.Create();
        var model = Path.Combine(workspace.Root, "model.gguf");
        var cli = Path.Combine(workspace.Root, "llama-cli.exe");
        File.WriteAllText(model, "fixture-model");
        File.WriteAllText(cli, "fixture-cli");
        var sessions = new RecordingLocalLmtSessionFactory("unexpected interactive output");
        var oneShot = new RecordingLocalLmtOneShotRunner("欢迎回来。");
        var batch = new RecordingLocalLmtBatchRunner("今天天气很好。", string.Empty);
        using var provider = new LocalTranslationProvider(
            new LocalLmtOptions(model, cli, ExpectedModelSha256: LocalLmtArtifactVerifier.Sha256File(model)),
            sessions,
            oneShotRunner: oneShot,
            batchRunner: batch,
            collectLocalDiagnosticContext: true);

        var results = provider.TranslateManyAsync(
            [
                new TranslationRequest("English", "Chinese", "The weather is pleasant today.", textKind: TranslationTextKind.Body),
                new TranslationRequest("English", "Chinese", "Welcome back.", textKind: TranslationTextKind.Body),
            ],
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(1, batch.Calls);
        Equal(1, oneShot.Calls);
        Equal("今天天气很好。", results[0].Text);
        Equal("欢迎回来。", results[1].Text);
        Equal(false, oneShot.Prompts[0].Contains("The weather is pleasant today.", StringComparison.Ordinal));
        Equal(true, oneShot.Prompts[0].Contains("Welcome back.", StringComparison.Ordinal));
    }


    private static void KeepsKoreanSpacesWhenJoiningOcrWrappedLines()
    {
        using var workspace = TemporaryWorkspace.Create();
        var model = Path.Combine(workspace.Root, "model.gguf");
        var cli = Path.Combine(workspace.Root, "llama-cli.exe");
        File.WriteAllText(model, "fixture-model");
        File.WriteAllText(cli, "fixture-cli");
        var sessions = new RecordingLocalLmtSessionFactory("我家的网络延迟很高，系统更新可能是原因。");
        using var provider = new LocalTranslationProvider(
            new LocalLmtOptions(model, cli, ExpectedModelSha256: LocalLmtArtifactVerifier.Sha256File(model)),
            sessions);

        var result = provider.TranslateAsync(
            new TranslationRequest(
                "auto",
                "Chinese",
                "우리 집 인터넷 지연이 높은 진짜 이유\n운영체제 업데이트가 원인일 수 있습니다.",
                textKind: TranslationTextKind.Body),
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(true, result.IsSuccess);
        Equal(true, sessions.Prompts.Single().Contains("진짜 이유 운영체제", StringComparison.Ordinal));
        Equal(false, sessions.Prompts.Single().Contains("진짜 이유운영체제", StringComparison.Ordinal));
    }

    private static void RetriesDenseKoreanBodyTextInBoundedChunks()
    {
        using var workspace = TemporaryWorkspace.Create();
        var model = Path.Combine(workspace.Root, "model.gguf");
        var cli = Path.Combine(workspace.Root, "llama-cli.exe");
        File.WriteAllText(model, "fixture-model");
        File.WriteAllText(cli, "fixture-cli");
        var sessions = new RecordingLocalLmtSessionFactory(
            string.Empty,
            "我家的网络延迟很高，",
            "操作系统更新可能是原因。");
        using var provider = new LocalTranslationProvider(
            new LocalLmtOptions(model, cli, ExpectedModelSha256: LocalLmtArtifactVerifier.Sha256File(model)),
            sessions);
        const string source = "우리 집 기가 인터넷은 지연이 매우 높은 진짜 이유 운영체제 업데이트가 원인일 수 있습니다.";

        var result = provider.TranslateAsync(
            new TranslationRequest("auto", "Chinese", source, textKind: TranslationTextKind.Body),
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(true, result.IsSuccess);
        Equal(3, sessions.Prompts.Count);
        Equal(true, sessions.Prompts.Skip(1).All(static prompt => prompt.Length < 150));
    }

    private static void CancelsLocalLmtSession()
    {
        using var workspace = TemporaryWorkspace.Create();
        var model = Path.Combine(workspace.Root, "LMT-60-1.7B-Q4_K_M.gguf");
        var cli = Path.Combine(workspace.Root, "llama-cli.exe");
        File.WriteAllText(model, "fixture-model");
        File.WriteAllText(cli, "fixture-cli");
        var sessions = new RecordingLocalLmtSessionFactory("unused") { WaitForCancellation = true };
        using var provider = new LocalTranslationProvider(new LocalLmtOptions(model, cli, ExpectedModelSha256: LocalLmtArtifactVerifier.Sha256File(model)), sessions);
        using var cancellation = new CancellationTokenSource();
        var running = provider.TranslateAsync(new TranslationRequest("English", "Chinese", "hello"), cancellation.Token);
        sessions.TranslationEntered.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
        Equal(1, sessions.StartCalls);
        Equal(false, running.IsCompleted);
        cancellation.Cancel();

        Throws<OperationCanceledException>(() => running.GetAwaiter().GetResult());
        Equal(true, sessions.CancellationObserved);
        Equal(true, sessions.SessionDisposed);
    }

    private static void ReusesOneLocalLmtSessionForDirectPerItemTranslation()
    {
        using var workspace = TemporaryWorkspace.Create();
        var model = Path.Combine(workspace.Root, "LMT-60-1.7B-Q4_K_M.gguf");
        var cli = Path.Combine(workspace.Root, "llama-cli.exe");
        File.WriteAllText(model, "fixture-model");
        File.WriteAllText(cli, "fixture-cli");
        var sessions = new RecordingLocalLmtSessionFactory("第一条。", "第二条。");
        using var provider = new LocalTranslationProvider(new LocalLmtOptions(model, cli, ExpectedModelSha256: LocalLmtArtifactVerifier.Sha256File(model)), sessions);

        var results = provider.TranslateManyAsync(
        [
            new TranslationRequest("English", "Chinese", "first"),
            new TranslationRequest("English", "Chinese", "second"),
        ], CancellationToken.None).GetAwaiter().GetResult();

        Equal(1, sessions.StartCalls);
        Equal(2, sessions.Prompts.Count);
        Equal(false, sessions.Prompts.Any(static prompt => prompt.Contains("/exit", StringComparison.Ordinal)));
        Equal(2, results.Count);
        Equal("第一条。", results[0].Text);
        Equal("第二条。", results[1].Text);
        Equal(true, results.All(static result => result.Status.Contains("model kept resident", StringComparison.Ordinal)));
    }

    private static void KeepsOneLocalLmtSessionAcrossSeparateRequests()
    {
        using var workspace = TemporaryWorkspace.Create();
        var model = Path.Combine(workspace.Root, "model.gguf");
        var cli = Path.Combine(workspace.Root, "llama-cli.exe");
        File.WriteAllText(model, "fixture-model");
        File.WriteAllText(cli, "fixture-cli");
        var sessions = new RecordingLocalLmtSessionFactory("第一条", "第二条");
        using var provider = new LocalTranslationProvider(
            new LocalLmtOptions(model, cli, ExpectedModelSha256: LocalLmtArtifactVerifier.Sha256File(model)),
            sessions);

        _ = provider.TranslateAsync(new TranslationRequest("English", "Chinese", "first"), CancellationToken.None).GetAwaiter().GetResult();
        _ = provider.TranslateAsync(new TranslationRequest("English", "Chinese", "second"), CancellationToken.None).GetAwaiter().GetResult();

        Equal(1, sessions.StartCalls);
        Equal(2, sessions.Prompts.Count);
    }

    private static void CachesVerifiedLocalLmtArtifactsForProviderLifetime()
    {
        using var workspace = TemporaryWorkspace.Create();
        var model = Path.Combine(workspace.Root, "model.gguf");
        var cli = Path.Combine(workspace.Root, "llama-cli.exe");
        File.WriteAllText(model, "fixture-model");
        File.WriteAllText(cli, "fixture-cli");
        var expectedHash = LocalLmtArtifactVerifier.Sha256File(model);
        var sessions = new RecordingLocalLmtSessionFactory("第一条", "第二条", "第三条");
        using var provider = new LocalTranslationProvider(
            new LocalLmtOptions(model, cli, ExpectedModelSha256: expectedHash),
            sessions);

        var first = provider.TranslateAsync(new TranslationRequest("English", "Chinese", "first"), CancellationToken.None).GetAwaiter().GetResult();
        var second = provider.TranslateAsync(new TranslationRequest("English", "Chinese", "second"), CancellationToken.None).GetAwaiter().GetResult();
        Equal(true, first.IsSuccess);
        Equal(true, second.IsSuccess);
        Equal(1, sessions.StartCalls);
        Equal(2, sessions.Prompts.Count);
        Equal(true, provider.HasActiveSession);

        File.WriteAllText(model, "changed-after-the-model-was-loaded");
        var changed = provider.TranslateAsync(new TranslationRequest("English", "Chinese", "third"), CancellationToken.None).GetAwaiter().GetResult();
        Equal(false, changed.IsSuccess);
        Equal(new TranslationDiagnostic(TranslationDiagnosticKind.ModelUnavailable, TranslationDiagnosticStage.Initial), changed.Diagnostic!);
        Equal(false, provider.HasActiveSession);
        Equal(1, sessions.DisposeCalls);
        Equal(1, sessions.StartCalls);
        Equal(2, sessions.Prompts.Count);

        File.WriteAllText(model, "fixture-model");
        var restored = provider.TranslateAsync(new TranslationRequest("English", "Chinese", "fourth"), CancellationToken.None).GetAwaiter().GetResult();
        Equal(true, restored.IsSuccess);
        Equal("第三条", restored.Text);
        Equal(true, provider.HasActiveSession);
        Equal(2, sessions.StartCalls);
        Equal(3, sessions.Prompts.Count);
    }

    private static void StopsResidentLocalLmtSessionAfterIdleTimeout()
    {
        using var workspace = TemporaryWorkspace.Create();
        var model = Path.Combine(workspace.Root, "model.gguf");
        var cli = Path.Combine(workspace.Root, "llama-cli.exe");
        File.WriteAllText(model, "fixture-model");
        File.WriteAllText(cli, "fixture-cli");
        var sessions = new RecordingLocalLmtSessionFactory("第一条", "第二条");
        using var provider = new LocalTranslationProvider(
            new LocalLmtOptions(model, cli, ExpectedModelSha256: LocalLmtArtifactVerifier.Sha256File(model)),
            sessions,
            TimeSpan.FromMilliseconds(40));

        _ = provider.TranslateAsync(new TranslationRequest("English", "Chinese", "first"), CancellationToken.None).GetAwaiter().GetResult();
        Equal(true, SpinWait.SpinUntil(() => sessions.DisposeCalls == 1, TimeSpan.FromSeconds(2)));
        _ = provider.TranslateAsync(new TranslationRequest("English", "Chinese", "second"), CancellationToken.None).GetAwaiter().GetResult();

        Equal(2, sessions.StartCalls);
    }

    private static void TranslatesCommonLocalUiControlsWithoutLoadingModel()
    {
        var sessions = new RecordingLocalLmtSessionFactory("unused");
        using var provider = new LocalTranslationProvider(new LocalLmtOptions("missing.gguf", "missing-cli.exe"), sessions);

        var results = provider.TranslateManyAsync([
            new TranslationRequest("English", "Chinese", "Settings", textKind: TranslationTextKind.UiControl),
            new TranslationRequest("English", "Chinese", "Follow", textKind: TranslationTextKind.UiControl),
            new TranslationRequest("English", "Chinese", "Search", textKind: TranslationTextKind.UiControl),
            new TranslationRequest("English", "Chinese", "Memes", textKind: TranslationTextKind.UiControl),
            new TranslationRequest("English", "Chinese", "Show translation", textKind: TranslationTextKind.UiControl),
            new TranslationRequest("English", "Chinese", "Show translation ⚙", textKind: TranslationTextKind.UiControl),
            new TranslationRequest("Japanese", "Chinese", "Show translation ⚙", textKind: TranslationTextKind.UiControl),
        ], CancellationToken.None).GetAwaiter().GetResult();

        Equal(true, results.All(static result => result.IsSuccess));
        Equal("设置", results[0].Text);
        Equal("关注", results[1].Text);
        Equal("搜索", results[2].Text);
        Equal("表情包", results[3].Text);
        Equal("显示翻译", results[4].Text);
        Equal("显示翻译", results[5].Text);
        Equal("显示翻译", results[6].Text);
        Equal(0, sessions.StartCalls);
    }

    private static void RoutesUnknownUiLabelToLocalLmt()
    {
        using var workspace = TemporaryWorkspace.Create();
        var model = Path.Combine(workspace.Root, "model.gguf");
        var cli = Path.Combine(workspace.Root, "llama-cli.exe");
        File.WriteAllText(model, "fixture-model");
        File.WriteAllText(cli, "fixture-cli");
        var sessions = new RecordingLocalLmtSessionFactory("新闻");
        using var provider = new LocalTranslationProvider(
            new LocalLmtOptions(model, cli, ExpectedModelSha256: LocalLmtArtifactVerifier.Sha256File(model)),
            sessions);

        var result = provider.TranslateAsync(
            new TranslationRequest("English", "Chinese", "News", textKind: TranslationTextKind.UiControl),
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(true, result.IsSuccess);
        Equal("新闻", result.Text);
        Equal(1, sessions.StartCalls);
    }

    private static void KeepsLocalProtectedValuesOutOfModelPrompts()
    {
        using var workspace = TemporaryWorkspace.Create();
        var model = Path.Combine(workspace.Root, "model.gguf");
        var cli = Path.Combine(workspace.Root, "llama-cli.exe");
        File.WriteAllText(model, "fixture-model");
        File.WriteAllText(cli, "fixture-cli");
        var sessions = new RecordingLocalLmtSessionFactory("[[K0]] 使用 [[K1]] 个额度：[[K2]] [[K3]] [[K4]]");
        using var provider = new LocalTranslationProvider(
            new LocalLmtOptions(model, cli, ExpectedModelSha256: LocalLmtArtifactVerifier.Sha256File(model)),
            sessions);

        var result = provider.TranslateAsync(
            new TranslationRequest("English", "Chinese", "OpenAI uses 11 credits: https://example.test #AI @moon"),
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(true, result.IsSuccess);
        Equal("OpenAI 使用 11 个额度：https://example.test #AI @moon", result.Text);
        Equal(1, sessions.Prompts.Count);
        foreach (var protectedValue in new[] { "OpenAI", "11", "https://example.test", "#AI", "@moon" })
            Equal(false, sessions.Prompts[0].Contains(protectedValue, StringComparison.Ordinal));
        foreach (var token in new[] { "[[K0]]", "[[K1]]", "[[K2]]", "[[K3]]", "[[K4]]" })
            Equal(true, sessions.Prompts[0].Contains(token, StringComparison.Ordinal));
    }

    private static void ProtectsJapaneseAdjacentDigitBeforeLocalLmtInference()
    {
        const string source = "記録的な豪雨により増水した長江の河辺でマキの1シーンを撮影する武漢のコスプレイヤーの方";
        using var workspace = TemporaryWorkspace.Create();
        var model = Path.Combine(workspace.Root, "model.gguf");
        var cli = Path.Combine(workspace.Root, "llama-cli.exe");
        File.WriteAllText(model, "fixture-model");
        File.WriteAllText(cli, "fixture-cli");
        var sessions = new RecordingLocalLmtSessionFactory("创纪录的暴雨使长江水位上涨，武汉的角色扮演者拍摄了真希的[[K0]]个场景。" );
        using var provider = new LocalTranslationProvider(
            new LocalLmtOptions(model, cli, ExpectedModelSha256: LocalLmtArtifactVerifier.Sha256File(model)),
            sessions);

        var result = provider.TranslateAsync(
            new TranslationRequest("Japanese", "Chinese", source, textKind: TranslationTextKind.Body),
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(true, result.IsSuccess);
        Equal(true, result.Text.Contains('1'));
        Equal(false, sessions.Prompts[0].Contains("1シーン", StringComparison.Ordinal));
        Equal(true, sessions.Prompts[0].Contains("[[K0]]", StringComparison.Ordinal));
        Equal(true, sessions.Prompts[0].Contains("Keep every token", StringComparison.Ordinal));
    }

    private static void PreservesTrailingKaomojiOutsideLocalLmtInference()
    {
        using var workspace = TemporaryWorkspace.Create();
        var model = Path.Combine(workspace.Root, "model.gguf");
        var cli = Path.Combine(workspace.Root, "llama-cli.exe");
        File.WriteAllText(model, "fixture-model");
        File.WriteAllText(cli, "fixture-cli");
        var sessions = new RecordingLocalLmtSessionFactory("早上好〜[[K0]]");
        using var provider = new LocalTranslationProvider(
            new LocalLmtOptions(model, cli, ExpectedModelSha256: LocalLmtArtifactVerifier.Sha256File(model)),
            sessions);

        var result = provider.TranslateAsync(
            new TranslationRequest("Japanese", "Chinese", "おはよ〜(ง ˙ω˙)ว♪", textKind: TranslationTextKind.Body),
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(true, result.IsSuccess);
        Equal("早上好〜(ง ˙ω˙)ว♪", result.Text);
        Equal(false, sessions.Prompts[0].Contains("˙ω˙", StringComparison.Ordinal));
    }

    private static void SplitsAroundMissingProtectedDigit()
    {
        const string source = "\u6620\u753b\u306e1\u30b7\u30fc\u30f3\u3002";
        using var workspace = TemporaryWorkspace.Create();
        var model = Path.Combine(workspace.Root, "model.gguf");
        var cli = Path.Combine(workspace.Root, "llama-cli.exe");
        File.WriteAllText(model, "fixture-model");
        File.WriteAllText(cli, "fixture-cli");
        var sessions = new RecordingLocalLmtSessionFactory(
            "\u8fd9\u662f\u4e00\u4e2a\u573a\u666f\u3002",
            "\u7535\u5f71\u7684",
            "\u573a\u666f\u3002");
        using var provider = new LocalTranslationProvider(
            new LocalLmtOptions(model, cli, ExpectedModelSha256: LocalLmtArtifactVerifier.Sha256File(model)),
            sessions);

        var result = provider.TranslateAsync(
            new TranslationRequest("Japanese", "Chinese", source, textKind: TranslationTextKind.Body),
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(true, result.IsSuccess);
        Equal(true, result.Text.Contains('1'));
        Equal(3, sessions.Prompts.Count);
        Equal(false, sessions.Prompts.Any(static prompt => prompt.Contains("Copy every protected token", StringComparison.Ordinal)));
    }

    private static void SplitsProtectedValuesWithoutInstructionRetry()
    {
        using var workspace = TemporaryWorkspace.Create();
        var model = Path.Combine(workspace.Root, "model.gguf");
        var cli = Path.Combine(workspace.Root, "llama-cli.exe");
        File.WriteAllText(model, "fixture-model");
        File.WriteAllText(cli, "fixture-cli");
        var sessions = new RecordingLocalLmtSessionFactory(
            "你好一个世界",
            "你好 ",
            " 世界");
        using var provider = new LocalTranslationProvider(
            new LocalLmtOptions(model, cli, ExpectedModelSha256: LocalLmtArtifactVerifier.Sha256File(model)),
            sessions);

        var result = provider.TranslateAsync(
            new TranslationRequest("English", "Chinese", "hello 1 world", textKind: TranslationTextKind.Body),
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(true, result.IsSuccess);
        Equal("你好 1 世界", result.Text);
        Equal(3, sessions.Prompts.Count);
    }

    private static void RepairsProtectedDigitCollisionByTranslatingAroundToken()
    {
        const string source = "\u6620\u753b\u306e1\u30b7\u30fc\u30f3\u3002";
        using var workspace = TemporaryWorkspace.Create();
        var model = Path.Combine(workspace.Root, "model.gguf");
        var cli = Path.Combine(workspace.Root, "llama-cli.exe");
        File.WriteAllText(model, "fixture-model");
        File.WriteAllText(cli, "fixture-cli");
        var sessions = new RecordingLocalLmtSessionFactory(
            "\u8fd9\u662f[[K0]]1\u4e2a\u573a\u666f\u3002",
            "\u7535\u5f71",
            "\u573a\u666f\u3002");
        using var provider = new LocalTranslationProvider(
            new LocalLmtOptions(model, cli, ExpectedModelSha256: LocalLmtArtifactVerifier.Sha256File(model)),
            sessions);

        var result = provider.TranslateAsync(
            new TranslationRequest("Japanese", "Chinese", source, textKind: TranslationTextKind.Body),
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(true, result.IsSuccess);
        Equal("\u7535\u5f711\u573a\u666f\u3002", result.Text);
        Equal(3, sessions.Prompts.Count);
    }

    private static void RepairsProtectedDigitCollisionInsideLongBodyChunk()
    {
        var source = new string('a', 69) + ":1," + new string('b', 150);
        using var workspace = TemporaryWorkspace.Create();
        var model = Path.Combine(workspace.Root, "model.gguf");
        var cli = Path.Combine(workspace.Root, "llama-cli.exe");
        File.WriteAllText(model, "fixture-model");
        File.WriteAllText(cli, "fixture-cli");
        var sessions = new RecordingLocalLmtSessionFactory(
            "\u7b2c\u4e00\u5757",
            "\u7b2c\u4e8c\u5757[[K0]]1",
            "\u7b2c\u4e09\u5757",
            "\u7b2c\u56db\u5757",
            "\u7b2c\u4e94\u5757");
        using var provider = new LocalTranslationProvider(
            new LocalLmtOptions(model, cli, ExpectedModelSha256: LocalLmtArtifactVerifier.Sha256File(model)),
            sessions);

        var result = provider.TranslateAsync(
            new TranslationRequest("English", "Chinese", source, textKind: TranslationTextKind.Body),
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(true, result.IsSuccess);
        Equal(true, result.Text.Contains('1'));
        Equal(5, sessions.Prompts.Count);
    }

    private static void RetriesLongBodyInChunksAfterProtectedDigitTokenFailure()
    {
        const string first = "The first passage records 1 scene in enough detail to require a bounded retry.";
        const string second = "The second passage continues the description with additional safely translated context.";
        const string third = "The third passage concludes the report with an evacuation warning for everyone.";
        var source = string.Join('\n', first, second, third);
        using var workspace = TemporaryWorkspace.Create();
        var model = Path.Combine(workspace.Root, "model.gguf");
        var cli = Path.Combine(workspace.Root, "llama-cli.exe");
        File.WriteAllText(model, "fixture-model");
        File.WriteAllText(cli, "fixture-cli");
        var sessions = new RecordingLocalLmtSessionFactory(
            "第一段保留[[K0]]。",
            "第二段译文。",
            "第三段译文。");
        using var provider = new LocalTranslationProvider(
            new LocalLmtOptions(model, cli, ExpectedModelSha256: LocalLmtArtifactVerifier.Sha256File(model)),
            sessions);

        var result = provider.TranslateAsync(
            new TranslationRequest("English", "Chinese", source, textKind: TranslationTextKind.Body),
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(true, result.IsSuccess);
        Equal(true, result.Text.Contains('1'));
        Equal(true, sessions.Prompts.Count > 2);
    }

    private static void KeepsProtectedBrandsOutOfLocalLmtInference()
    {
        using var workspace = TemporaryWorkspace.Create();
        var model = Path.Combine(workspace.Root, "model.gguf");
        var cli = Path.Combine(workspace.Root, "llama-cli.exe");
        File.WriteAllText(model, "fixture-model");
        File.WriteAllText(cli, "fixture-cli");
        var sessions = new RecordingLocalLmtSessionFactory("[[K0]] 使用这个平台。");
        using var provider = new LocalTranslationProvider(
            new LocalLmtOptions(model, cli, ExpectedModelSha256: LocalLmtArtifactVerifier.Sha256File(model)),
            sessions);

        var result = provider.TranslateAsync(
            new TranslationRequest("English", "Chinese", "GitHub uses this platform.", textKind: TranslationTextKind.Body),
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(true, result.IsSuccess);
        Equal("GitHub 使用这个平台。", result.Text);
        Equal(1, sessions.Prompts.Count);
        Equal(false, sessions.Prompts[0].Contains("GitHub", StringComparison.Ordinal));
    }

    private static void UsesFifteenMinuteDefaultLocalLmtIdleTimeout() =>
        Equal(TimeSpan.FromMinutes(15), LocalTranslationProvider.DefaultIdleShutdown);

    private static void LocalizesRelativeTimesBeforeLocalLmtInference()
    {
        using var workspace = TemporaryWorkspace.Create();
        var model = Path.Combine(workspace.Root, "model.gguf");
        var cli = Path.Combine(workspace.Root, "llama-cli.exe");
        File.WriteAllText(model, "fixture-model");
        File.WriteAllText(cli, "fixture-cli");
        var sessions = new RecordingLocalLmtSessionFactory("[[K0]]· 其他· [[K1]] 帖子");
        using var provider = new LocalTranslationProvider(
            new LocalLmtOptions(model, cli, ExpectedModelSha256: LocalLmtArtifactVerifier.Sha256File(model)),
            sessions);

        var compact = provider.TranslateAsync(
            new TranslationRequest("English", "Chinese", "@ns123abc·11h", textKind: TranslationTextKind.IdentityMetadata),
            CancellationToken.None).GetAwaiter().GetResult();
        var worded = provider.TranslateAsync(
            new TranslationRequest("English", "Chinese", "11 hours ago· Other· 4,705 posts", textKind: TranslationTextKind.Metadata),
            CancellationToken.None).GetAwaiter().GetResult();

        Equal("@ns123abc·11小时前", compact.Text);
        Equal("11小时前· 其他· 4,705 帖子", worded.Text);
        Equal(1, sessions.Prompts.Count);
        Equal(false, sessions.Prompts.Any(static prompt => prompt.Contains("11h", StringComparison.OrdinalIgnoreCase)));
        Equal(false, sessions.Prompts.Any(static prompt => prompt.Contains("11 hours ago", StringComparison.OrdinalIgnoreCase)));
        Equal(false, sessions.Prompts.Any(static prompt => prompt.Contains("11小时前", StringComparison.Ordinal)));
    }

    private static void PreservesLocalMagnitudeTokensAndIdentifierDisplayNames()
    {
        using var workspace = TemporaryWorkspace.Create();
        var model = Path.Combine(workspace.Root, "model.gguf");
        var cli = Path.Combine(workspace.Root, "llama-cli.exe");
        File.WriteAllText(model, "fixture-model");
        File.WriteAllText(cli, "fixture-cli");
        var sessions = new RecordingLocalLmtSessionFactory("我曾承诺，[[K1]] 每增加 [[K0]] 活跃用户就重置一次，直到 [[K2]]。");
        using var provider = new LocalTranslationProvider(
            new LocalLmtOptions(model, cli, ExpectedModelSha256: LocalLmtArtifactVerifier.Sha256File(model)),
            sessions);

        var identifier = provider.TranslateAsync(
            new TranslationRequest("Japanese", "Chinese", "くまの_よなよな", textKind: TranslationTextKind.UiControl),
            CancellationToken.None).GetAwaiter().GetResult();
        var body = provider.TranslateAsync(
            new TranslationRequest("English", "Chinese", "I promised a reset for every 1M active users for Codex, until 10M."),
            CancellationToken.None).GetAwaiter().GetResult();

        Equal("くまの_よなよな", identifier.Text);
        Equal(true, body.IsSuccess);
        Equal(true, body.Text.Contains("1M", StringComparison.Ordinal));
        Equal(true, body.Text.Contains("10M", StringComparison.Ordinal));
        Equal(1, sessions.Prompts.Count);
        Equal(false, sessions.Prompts[0].Contains("1M", StringComparison.Ordinal));
        Equal(false, sessions.Prompts[0].Contains("10M", StringComparison.Ordinal));
        Equal(false, sessions.Prompts[0].Contains("Codex", StringComparison.Ordinal));
        Equal(true, sessions.Prompts[0].Contains("[[K0]]", StringComparison.Ordinal));
        Equal(true, sessions.Prompts[0].Contains("[[K1]]", StringComparison.Ordinal));
        Equal(true, sessions.Prompts[0].Contains("[[K2]]", StringComparison.Ordinal));
    }

    private static void RemovesLocalLmtTokenInstructionEcho()
    {
        using var workspace = TemporaryWorkspace.Create();
        var model = Path.Combine(workspace.Root, "model.gguf");
        var cli = Path.Combine(workspace.Root, "llama-cli.exe");
        File.WriteAllText(model, "fixture-model");
        File.WriteAllText(cli, "fixture-cli");
        var sessions = new RecordingLocalLmtSessionFactory(
            "保持每个标记不变，例如[[K0]]。沿着天琴座的尾巴就能找到公主。子账户→[[K0]]。" );
        using var provider = new LocalTranslationProvider(
            new LocalLmtOptions(model, cli, ExpectedModelSha256: LocalLmtArtifactVerifier.Sha256File(model)),
            sessions);

        var result = provider.TranslateAsync(
            new TranslationRequest("Japanese", "Chinese", "尾をたどれば姫に会える。サブ垢→@azumino_114514。", textKind: TranslationTextKind.Body),
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(true, result.IsSuccess);
        Equal("沿着天琴座的尾巴就能找到公主。子账户→@azumino_114514。", result.Text);
    }

    private static void RejectsTranslatedLocalLmtPromptEcho()
    {
        using var workspace = TemporaryWorkspace.Create();
        var model = Path.Combine(workspace.Root, "model.gguf");
        var cli = Path.Combine(workspace.Root, "llama-cli.exe");
        File.WriteAllText(model, "fixture-model");
        File.WriteAllText(cli, "fixture-cli");
        var sessions = new RecordingLocalLmtSessionFactory(
            "将换行文本视为一个段落。翻译每个可读的人类语言短语，包括俚语。保留拉丁账户名称。保持每个类似 [[K0]] 的标记不变。仅输出翻译内容。ぷれ→anemoi",
            "将换行文本视为一个段落。翻译每个可读的人类语言短语，包括俚语。保留拉丁账户名称。保持每个类似 [[K0]] 的标记不变。仅输出翻译内容。ぷれ→anemoi");
        using var provider = new LocalTranslationProvider(
            new LocalLmtOptions(model, cli, ExpectedModelSha256: LocalLmtArtifactVerifier.Sha256File(model)),
            sessions);

        var result = provider.TranslateAsync(
            new TranslationRequest(
                "Japanese",
                "简体中文",
                "はくちょうの尾をたどってこと座の姫の灯り、あの川を渡ればアルタイルに会える サブ垢→@azumino_114514 なうぷれ→anemoi",
                textKind: TranslationTextKind.Body,
                scene: "social_media"),
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(false, result.IsSuccess);
        Equal(string.Empty, result.Text);
        Equal("Local LMT model output failed validation.", result.Status);
        Equal(TranslationDiagnosticKind.PromptEcho, result.Diagnostic!.Kind);
    }

    private static void RejectsUnchangedLocalLmtBodyText()
    {
        using var workspace = TemporaryWorkspace.Create();
        var model = Path.Combine(workspace.Root, "model.gguf");
        var cli = Path.Combine(workspace.Root, "llama-cli.exe");
        File.WriteAllText(model, "fixture-model");
        File.WriteAllText(cli, "fixture-cli");
        const string source = "Open source projects improved their security posture through expert guidance.";
        var sessions = new RecordingLocalLmtSessionFactory(source);
        using var provider = new LocalTranslationProvider(
            new LocalLmtOptions(model, cli, ExpectedModelSha256: LocalLmtArtifactVerifier.Sha256File(model)),
            sessions);

        var result = provider.TranslateAsync(
            new TranslationRequest("English", "Chinese", source, textKind: TranslationTextKind.Body),
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(false, result.IsSuccess);
        Equal(string.Empty, result.Text);
        Equal("Local LMT returned the source text unchanged.", result.Status);
        Equal(true, result.Diagnostic is not null);
        Equal(
            new TranslationDiagnostic(TranslationDiagnosticKind.SourceUnchanged, TranslationDiagnosticStage.Initial),
            result.Diagnostic!);
    }

    private static void RejectsPunctuationOnlyLocalLmtJapaneseBodyChanges()
    {
        using var workspace = TemporaryWorkspace.Create();
        var model = Path.Combine(workspace.Root, "model.gguf");
        var cli = Path.Combine(workspace.Root, "llama-cli.exe");
        File.WriteAllText(model, "fixture-model");
        File.WriteAllText(cli, "fixture-cli");
        const string source = "\u8a18\u9332\u7684\u306a\u8c6a\u96e8\u306b\u3088\u308a\u5897\u6c34\u3057\u305f\u9577\u6c5f\u306e\u6cb3\u8fba\u3067\u3059\u3002";
        var punctuationOnly = source.Replace('\u3002', '!');
        var sessions = new RecordingLocalLmtSessionFactory(punctuationOnly, punctuationOnly, punctuationOnly);
        using var provider = new LocalTranslationProvider(
            new LocalLmtOptions(model, cli, ExpectedModelSha256: LocalLmtArtifactVerifier.Sha256File(model)),
            sessions);

        var result = provider.TranslateAsync(
            new TranslationRequest("Japanese", "Chinese", source, textKind: TranslationTextKind.Body),
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(false, result.IsSuccess);
        Equal(string.Empty, result.Text);
        Equal("Local LMT returned the source text unchanged.", result.Status);
    }

    private static void RejectsJapaneseLocalLmtSourceScriptBodyText()
    {
        using var workspace = TemporaryWorkspace.Create();
        var model = Path.Combine(workspace.Root, "model.gguf");
        var cli = Path.Combine(workspace.Root, "llama-cli.exe");
        File.WriteAllText(model, "fixture-model");
        File.WriteAllText(cli, "fixture-cli");
        const string source = "\u8a18\u9332\u7684\u306a\u8c6a\u96e8\u306b\u3088\u308a\u5897\u6c34\u3057\u305f\u9577\u6c5f\u306e\u6cb3\u8fba\u3067\u3059\u3002";
        var sourceScriptOnly = source.Replace('\u6cb3', '\u5ddd');
        var sessions = new RecordingLocalLmtSessionFactory(sourceScriptOnly, sourceScriptOnly);
        using var provider = new LocalTranslationProvider(
            new LocalLmtOptions(model, cli, ExpectedModelSha256: LocalLmtArtifactVerifier.Sha256File(model)),
            sessions);

        var result = provider.TranslateAsync(
            new TranslationRequest("Japanese", "Chinese", source, textKind: TranslationTextKind.Body),
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(false, result.IsSuccess);
        Equal(string.Empty, result.Text);
        Equal("Local LMT returned a source-language-only body.", result.Status);
    }

    private static void CarriesStructuredLocalTranslationDiagnosticWithoutPrivateText()
    {
        var translationAssembly = typeof(TranslationResult).Assembly;
        var diagnostic = translationAssembly.GetType("ClipRay.App.Translation.TranslationDiagnostic");

        Equal(true, diagnostic is not null);
        Equal(true, diagnostic!.GetProperty("Kind") is not null);
        Equal(true, diagnostic.GetProperty("Stage") is not null);
        Equal(true, diagnostic.GetProperty("RetryChunkIndex") is not null);
        Equal(true, typeof(TranslationResult).GetProperty("Diagnostic") is not null);
    }

    private static void ReportsNumericLocalLmtInvariantChangePrecisely()
    {
        using var workspace = TemporaryWorkspace.Create();
        var model = Path.Combine(workspace.Root, "model.gguf");
        var cli = Path.Combine(workspace.Root, "llama-cli.exe");
        File.WriteAllText(model, "fixture-model");
        File.WriteAllText(cli, "fixture-cli");
        var sessions = new RecordingLocalLmtSessionFactory("场景一", "场景一");
        using var provider = new LocalTranslationProvider(
            new LocalLmtOptions(model, cli, ExpectedModelSha256: LocalLmtArtifactVerifier.Sha256File(model)),
            sessions);

        var result = provider.TranslateAsync(
            new TranslationRequest("Japanese", "Chinese", "シーン1", textKind: TranslationTextKind.Body),
            CancellationToken.None).GetAwaiter().GetResult();

        Console.WriteLine($"NUMERIC_DIAGNOSTIC success={result.IsSuccess} status={result.Status} diagnostic={result.Diagnostic?.Kind} prompts={sessions.Prompts.Count}");
        Equal(false, result.IsSuccess);
        Equal(true, result.Diagnostic is not null);
        Equal("ProtectedNumericChanged", result.Diagnostic!.Kind.ToString());
    }

    private static void MapsLocalLmtProcessFailuresWithoutExceptionText()
    {
        var assembly = typeof(TranslationResult).Assembly;
        var processFailure = assembly.GetType("ClipRay.App.Translation.LocalLmtProcessFailureKind");
        var processException = assembly.GetType("ClipRay.App.Translation.LocalLmtProcessException");
        var diagnostics = assembly.GetType("ClipRay.App.Translation.LocalLmtProcessDiagnostics");
        Equal(true, processFailure is not null);
        Equal(true, processException is not null);
        Equal(true, diagnostics is not null);
        if (processFailure is null || processException is null || diagnostics is null) return;

        var classify = diagnostics.GetMethod("ToDiagnosticKind");
        Equal(true, classify is not null);
        if (classify is null) return;

        var responseBoundary = Enum.Parse(processFailure, "ResponseBoundaryLost");
        Equal("ResponseBoundaryLost", classify.Invoke(null, [responseBoundary])?.ToString() ?? string.Empty);
    }

    private static void ReportsInitialLocalLmtResponseBoundaryFailurePrecisely()
    {
        using var workspace = TemporaryWorkspace.Create();
        var model = Path.Combine(workspace.Root, "model.gguf");
        var cli = Path.Combine(workspace.Root, "llama-cli.exe");
        File.WriteAllText(model, "fixture-model");
        File.WriteAllText(cli, "fixture-cli");
        var sessions = new RecordingLocalLmtSessionFactory("unused")
        {
            FailureKind = LocalLmtProcessFailureKind.ResponseBoundaryLost,
        };
        using var provider = new LocalTranslationProvider(
            new LocalLmtOptions(model, cli, ExpectedModelSha256: LocalLmtArtifactVerifier.Sha256File(model)),
            sessions);

        var result = provider.TranslateAsync(
            new TranslationRequest("English", "Chinese", "Translate this sentence."),
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(false, result.IsSuccess);
        Equal(true, result.Diagnostic is not null);
        Equal(
            new TranslationDiagnostic(
                TranslationDiagnosticKind.ResponseBoundaryLost,
                TranslationDiagnosticStage.Initial),
            result.Diagnostic!);
    }

    private static void ReportsRetryLocalLmtResponseBoundaryFailurePrecisely()
    {
        var source = string.Join('\n', Enumerable.Repeat(
            "This source paragraph is long enough to require bounded chunk retry.",
            3));
        using var workspace = TemporaryWorkspace.Create();
        var model = Path.Combine(workspace.Root, "model.gguf");
        var cli = Path.Combine(workspace.Root, "llama-cli.exe");
        File.WriteAllText(model, "fixture-model");
        File.WriteAllText(cli, "fixture-cli");
        var sessions = new RecordingLocalLmtSessionFactory(source)
        {
            FailureKind = LocalLmtProcessFailureKind.ResponseBoundaryLost,
            FailureAtPrompt = 2,
        };
        using var provider = new LocalTranslationProvider(
            new LocalLmtOptions(model, cli, ExpectedModelSha256: LocalLmtArtifactVerifier.Sha256File(model)),
            sessions);

        var result = provider.TranslateAsync(
            new TranslationRequest("English", "Chinese", source, textKind: TranslationTextKind.Body),
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(false, result.IsSuccess);
        Equal(true, result.Diagnostic is not null);
        Equal(
            new TranslationDiagnostic(
                TranslationDiagnosticKind.ResponseBoundaryLost,
                TranslationDiagnosticStage.RetryChunk,
                1),
            result.Diagnostic!);
    }

    private static void RetriesLongLocalLmtBodyInOriginalLineOrder()
    {
        const string first = "The first passage explains how maintainers improved project security.";
        const string second = "The second passage describes expert guidance and secure workflows.";
        const string third = "The third passage records the final funding outcome for the projects.";
        var source = string.Join('\n', first, second, third);
        using var workspace = TemporaryWorkspace.Create();
        var model = Path.Combine(workspace.Root, "model.gguf");
        var cli = Path.Combine(workspace.Root, "llama-cli.exe");
        File.WriteAllText(model, "fixture-model");
        File.WriteAllText(cli, "fixture-cli");
        var sessions = new RecordingLocalLmtSessionFactory(source, "第一段译文。", "第二段译文。", "第三段译文。");
        using var provider = new LocalTranslationProvider(
            new LocalLmtOptions(model, cli, ExpectedModelSha256: LocalLmtArtifactVerifier.Sha256File(model)),
            sessions);

        var result = provider.TranslateAsync(
            new TranslationRequest("English", "Chinese", source, textKind: TranslationTextKind.Body),
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(true, result.IsSuccess);
        Equal("第一段译文。\n第二段译文。\n第三段译文。", result.Text);
        Equal(4, sessions.Prompts.Count);
    }

    private static void SplitsLongJapaneseImageBodyAtSafeOriginalLineBreak()
    {
        const string first = "記録的な暴雨により増水した長江の河辺でまどマギの1シーンを撮影する武漢";
        const string second = "のコスプレイヤーの方";
        const string third = "ワルプルギスの夜の名シーンを大雨を利用して再現できていて素晴らしいと";
        const string fourth = "思うけど危険だから早く避難してほしいとも思う…";
        var source = string.Join('\n', first, second, third, fourth);
        using var workspace = TemporaryWorkspace.Create();
        var model = Path.Combine(workspace.Root, "model.gguf");
        var cli = Path.Combine(workspace.Root, "llama-cli.exe");
        File.WriteAllText(model, "fixture-model");
        File.WriteAllText(cli, "fixture-cli");
        var sessions = new RecordingLocalLmtSessionFactory(
            "创纪录的暴雨使长江水位上涨，武汉的角色扮演者拍摄了魔法少女小圆的[[K0]]个场景。",
            "她利用大雨再现经典场景，",
            "但情况危险，希望她尽快避难。");
        using var provider = new LocalTranslationProvider(
            new LocalLmtOptions(model, cli, ExpectedModelSha256: LocalLmtArtifactVerifier.Sha256File(model)),
            sessions);

        var result = provider.TranslateAsync(
            new TranslationRequest("Japanese", "Chinese", source, textKind: TranslationTextKind.Body),
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(true, result.IsSuccess);
        Equal(3, sessions.Prompts.Count);
        Equal(false, sessions.Prompts[1].Contains("ワルプルギス", StringComparison.Ordinal));
        Equal(true, sessions.Prompts[2].Contains("ワルプルギス", StringComparison.Ordinal));
    }

    private static void KeepsJapaneseProtectedTokenBodyChunksBounded()
    {
        var lines = Enumerable.Range(0, 8)
            .Select(index => index == 0
                ? "\u65e5\u672c\u8a9e\u306e\u9577\u3044\u672c\u6587\u3067\u30011\u30b7\u30fc\u30f3\u306e\u8aac\u660e\u3068\u80cc\u666f\u3092\u8a73\u7d30\u306b\u8a18\u9332\u3057\u3066\u3044\u308b\u6bb5\u843d\u3067\u3059\u3002"
                : "\u65e5\u672c\u8a9e\u306e\u9577\u3044\u672c\u6587\u3067\u3001\u80cc\u666f\u3068\u767b\u5834\u4eba\u7269\u306e\u884c\u52d5\u3092\u8a73\u7d30\u306b\u8aac\u660e\u3059\u308b\u6bb5\u843d\u3067\u3059\u3002")
            .ToArray();
        var source = string.Join('\n', lines);
        using var workspace = TemporaryWorkspace.Create();
        var model = Path.Combine(workspace.Root, "model.gguf");
        var cli = Path.Combine(workspace.Root, "llama-cli.exe");
        File.WriteAllText(model, "fixture-model");
        File.WriteAllText(cli, "fixture-cli");
        var sessions = new RecordingLocalLmtSessionFactory(
            "\u7b2c\u4e00\u6bb5\u8bf4\u660e\u4e86[[K0]]\u4e2a\u573a\u666f\u3002",
            "\u7b2c\u4e8c\u6bb5\u5df2\u5b8c\u6574\u7ffb\u8bd1\u3002",
            "\u7b2c\u4e09\u6bb5\u5df2\u5b8c\u6574\u7ffb\u8bd1\u3002",
            "\u7b2c\u56db\u6bb5\u5df2\u5b8c\u6574\u7ffb\u8bd1\u3002",
            "\u7b2c\u4e94\u6bb5\u5df2\u5b8c\u6574\u7ffb\u8bd1\u3002",
            "\u7b2c\u516d\u6bb5\u5df2\u5b8c\u6574\u7ffb\u8bd1\u3002",
            "\u7b2c\u4e03\u6bb5\u5df2\u5b8c\u6574\u7ffb\u8bd1\u3002",
            "\u7b2c\u516b\u6bb5\u5df2\u5b8c\u6574\u7ffb\u8bd1\u3002");
        using var provider = new LocalTranslationProvider(
            new LocalLmtOptions(model, cli, ExpectedModelSha256: LocalLmtArtifactVerifier.Sha256File(model)),
            sessions);

        var result = provider.TranslateAsync(
            new TranslationRequest("Japanese", "Chinese", source, textKind: TranslationTextKind.Body),
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(true, result.IsSuccess);
        Equal(true, result.Text.Contains("1\u4e2a\u573a\u666f", StringComparison.Ordinal));
        Equal(true, sessions.Prompts.Count > 3);
    }

    private static void RetriesShortUnchangedLocalLmtBodyWithOfficialTemplate()
    {
        const string source = "A short body remains unchanged.";
        using var workspace = TemporaryWorkspace.Create();
        var model = Path.Combine(workspace.Root, "model.gguf");
        var cli = Path.Combine(workspace.Root, "llama-cli.exe");
        File.WriteAllText(model, "fixture-model");
        File.WriteAllText(cli, "fixture-cli");
        var sessions = new RecordingLocalLmtSessionFactory(
            source,
            "之前的答案没有翻译。只返回完整的翻译。短正文已完成翻译。");
        using var provider = new LocalTranslationProvider(
            new LocalLmtOptions(model, cli, ExpectedModelSha256: LocalLmtArtifactVerifier.Sha256File(model)),
            sessions);

        var result = provider.TranslateAsync(
            new TranslationRequest("English", "Chinese", source, textKind: TranslationTextKind.Body),
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(true, result.IsSuccess);
        Equal("短正文已完成翻译。", result.Text);
        Equal(2, sessions.Prompts.Count);
        Equal(sessions.Prompts[0], sessions.Prompts[1]);
    }

    private static void RetriesShortFailedLocalLmtBodyAtMostTwice()
    {
        const string source = "A short body remains unchanged twice.";
        using var workspace = TemporaryWorkspace.Create();
        var model = Path.Combine(workspace.Root, "model.gguf");
        var cli = Path.Combine(workspace.Root, "llama-cli.exe");
        File.WriteAllText(model, "fixture-model");
        File.WriteAllText(cli, "fixture-cli");
        var sessions = new RecordingLocalLmtSessionFactory(string.Empty, string.Empty, "短正文第三次已完成翻译。");
        using var provider = new LocalTranslationProvider(
            new LocalLmtOptions(model, cli, ExpectedModelSha256: LocalLmtArtifactVerifier.Sha256File(model)),
            sessions);

        var result = provider.TranslateAsync(
            new TranslationRequest("English", "Chinese", source, textKind: TranslationTextKind.Body),
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(true, result.IsSuccess);
        Equal("短正文第三次已完成翻译。", result.Text);
        Equal(3, sessions.Prompts.Count);
        Equal(3, sessions.StartCalls);
        Equal(2, sessions.DisposeCalls);
        Equal(true, sessions.Prompts.All(prompt => prompt == sessions.Prompts[0]));
    }

    private static void BoundsLocalLmtBodyRetryToThreeChunks()
    {
        var source = string.Join('\n', Enumerable.Range(1, 5).Select(index =>
            $"Passage {index} has enough natural language for one bounded retry."));
        using var workspace = TemporaryWorkspace.Create();
        var model = Path.Combine(workspace.Root, "model.gguf");
        var cli = Path.Combine(workspace.Root, "llama-cli.exe");
        File.WriteAllText(model, "fixture-model");
        File.WriteAllText(cli, "fixture-cli");
        var protectedSource = Regex.Replace(source, @"\b\d+\b", match => $"[[K{int.Parse(match.Value) - 1}]]");
        var sessions = new RecordingLocalLmtSessionFactory(protectedSource, "第一块[[K0]]。", "第二块[[K1]]。", "第三块[[K2]] [[K3]] [[K4]]。");
        using var provider = new LocalTranslationProvider(
            new LocalLmtOptions(model, cli, ExpectedModelSha256: LocalLmtArtifactVerifier.Sha256File(model)),
            sessions);

        var result = provider.TranslateAsync(
            new TranslationRequest("English", "Chinese", source, textKind: TranslationTextKind.Body),
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(true, result.IsSuccess);
        Equal(4, sessions.Prompts.Count);
    }

    private static void KeepsLocalLmtProtectedTokensAtomicAcrossChunks()
    {
        var source = new string('a', 69) + ":@moon," + new string('b', 150);
        var protectedSource = new string('a', 69) + ":[[K0]]," + new string('b', 150);
        using var workspace = TemporaryWorkspace.Create();
        var model = Path.Combine(workspace.Root, "model.gguf");
        var cli = Path.Combine(workspace.Root, "llama-cli.exe");
        File.WriteAllText(model, "fixture-model");
        File.WriteAllText(cli, "fixture-cli");
        var sessions = new RecordingLocalLmtSessionFactory(protectedSource, "甲", "乙[[K0]]", "丙", "丁");
        using var provider = new LocalTranslationProvider(
            new LocalLmtOptions(model, cli, ExpectedModelSha256: LocalLmtArtifactVerifier.Sha256File(model)),
            sessions);

        var result = provider.TranslateAsync(
            new TranslationRequest("English", "Chinese", source, textKind: TranslationTextKind.Body),
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(true, result.IsSuccess);
        Equal("甲乙@moon丙丁", result.Text);
        Equal(1, sessions.Prompts.Skip(1).Count(prompt => prompt.Contains("[[K0]]", StringComparison.Ordinal)));
        Equal(false, sessions.Prompts.Skip(1).Any(prompt =>
            prompt.Contains("[[K", StringComparison.Ordinal) && !prompt.Contains("[[K0]]", StringComparison.Ordinal)));
        Equal(false, sessions.Prompts.Skip(1).Any(prompt =>
            prompt.Contains("K0]]", StringComparison.Ordinal) && !prompt.Contains("[[K0]]", StringComparison.Ordinal)));
    }

    private static void CarriesProtectedOnlyLocalLmtRetryChunkWithoutInference()
    {
        var handle = "@moon" + new string('b', 150);
        var source = new string('a', 69) + ":" + handle;
        var protectedSource = new string('a', 69) + ":[[K0]]";
        using var workspace = TemporaryWorkspace.Create();
        var model = Path.Combine(workspace.Root, "model.gguf");
        var cli = Path.Combine(workspace.Root, "llama-cli.exe");
        File.WriteAllText(model, "fixture-model");
        File.WriteAllText(cli, "fixture-cli");
        var sessions = new RecordingLocalLmtSessionFactory(protectedSource, "甲");
        using var provider = new LocalTranslationProvider(
            new LocalLmtOptions(model, cli, ExpectedModelSha256: LocalLmtArtifactVerifier.Sha256File(model)),
            sessions);

        var result = provider.TranslateAsync(
            new TranslationRequest("English", "Chinese", source, textKind: TranslationTextKind.Body),
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(true, result.IsSuccess);
        Equal("甲" + handle, result.Text);
        Equal(2, sessions.Prompts.Count);
    }

    private static void CarriesUnmaskedProtectedLocalLmtBrandWithoutInference()
    {
        var source = new string('a', 69) + ": GitHub";
        using var workspace = TemporaryWorkspace.Create();
        var model = Path.Combine(workspace.Root, "model.gguf");
        var cli = Path.Combine(workspace.Root, "llama-cli.exe");
        File.WriteAllText(model, "fixture-model");
        File.WriteAllText(cli, "fixture-cli");
        var sessions = new RecordingLocalLmtSessionFactory(source, "甲");
        using var provider = new LocalTranslationProvider(
            new LocalLmtOptions(model, cli, ExpectedModelSha256: LocalLmtArtifactVerifier.Sha256File(model)),
            sessions);

        var result = provider.TranslateAsync(
            new TranslationRequest("English", "Chinese", source, textKind: TranslationTextKind.Body),
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(true, result.IsSuccess);
        Equal("甲 GitHub", result.Text);
        Equal(2, sessions.Prompts.Count);
    }

    private static void RetainsWholeLocalLmtBodyWhenOneRetryChunkFails()
    {
        const string first = "The first passage contains enough natural language for one retry chunk.";
        const string second = "The second passage must not be accepted when it remains unchanged.";
        const string third = "The third passage must never be requested after the second one fails.";
        var source = string.Join('\n', first, second, third);
        using var workspace = TemporaryWorkspace.Create();
        var model = Path.Combine(workspace.Root, "model.gguf");
        var cli = Path.Combine(workspace.Root, "llama-cli.exe");
        File.WriteAllText(model, "fixture-model");
        File.WriteAllText(cli, "fixture-cli");
        var sessions = new RecordingLocalLmtSessionFactory(source, "第一块译文。", second, "不应请求第三块");
        using var provider = new LocalTranslationProvider(
            new LocalLmtOptions(model, cli, ExpectedModelSha256: LocalLmtArtifactVerifier.Sha256File(model)),
            sessions);

        var result = provider.TranslateAsync(
            new TranslationRequest("English", "Chinese", source, textKind: TranslationTextKind.Body),
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(false, result.IsSuccess);
        Equal(string.Empty, result.Text);
        Equal("Local LMT returned the source text unchanged.", result.Status);
        Equal(true, result.Diagnostic is not null);
        Equal(
            new TranslationDiagnostic(TranslationDiagnosticKind.SourceUnchanged, TranslationDiagnosticStage.RetryChunk, 2),
            result.Diagnostic!);
        Equal(true, result.ChunkDiagnostics is { Count: >= 2 });
        var failedChunk = result.ChunkDiagnostics!.Last(item => item.ResultCategory == "source-unchanged");
        Equal(2, failedChunk.ChunkIndex);
        Equal(3, sessions.Prompts.Count);
    }

    private static void ReportsMissingProtectedTokenInLocalLmtChunkTwo()
    {
        const string first = "The first passage is long enough to end at this explicit line boundary.";
        const string second = "The second passage records 2 protected scenes and must fail safely.";
        const string third = "The third passage must never be merged after a failed second chunk.";
        var source = string.Join('\n', first, second, third);
        using var workspace = TemporaryWorkspace.Create();
        var model = Path.Combine(workspace.Root, "model.gguf");
        var cli = Path.Combine(workspace.Root, "llama-cli.exe");
        File.WriteAllText(model, "fixture-model");
        File.WriteAllText(cli, "fixture-cli");
        var sessions = new RecordingLocalLmtSessionFactory(
            "第一块译文。",
            "第二块漏掉标记。",
            "第二块严格重试仍漏掉标记。",
            "第二块错误地加入[[K0]]标记。",
            "第二块尾部译文。");
        using var provider = new LocalTranslationProvider(
            new LocalLmtOptions(model, cli, ExpectedModelSha256: LocalLmtArtifactVerifier.Sha256File(model)),
            sessions);

        var result = provider.TranslateAsync(
            new TranslationRequest("English", "Chinese", source, textKind: TranslationTextKind.Body),
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(false, result.IsSuccess);
        Equal(string.Empty, result.Text);
        var chunk = result.ChunkDiagnostics?.Last(item =>
            item.ChunkIndex == 2 && item.ResultCategory == "protected-token-missing");
        Equal("protected-token-missing", chunk!.ResultCategory);
        Equal(true, result.ChunkDiagnostics!.Any(item => item.ChunkIndex == 2 && item.Stage == "strict-retry"));
        Equal(true, chunk.ExpectedProtectedTokenCount > chunk.ReturnedProtectedTokenCount
            || chunk.ReturnedProtectedTokenCount > chunk.ExpectedProtectedTokenCount);
        var chunkReports = result.ChunkDiagnostics!.Select(static item => new TestLabChunkReport(
            item.ChunkIndex,
            item.CharacterCount,
            item.Stage,
            item.ResultCategory,
            item.ExpectedProtectedTokenCount,
            item.ReturnedProtectedTokenCount)).ToArray();
        TestLabReportWriter.Write(workspace.Root, new TestLabRunReport(
            "scripted-chunk-two",
            DateTimeOffset.UnixEpoch,
            "local",
            "failed",
            [],
            [new TestLabCaseReport(
                "scripted",
                "scripted.png",
                "sanitized-hash",
                "scripted\\before.png",
                "all-translated",
                [new TestLabRouteReport(
                    "local", false, false, "translation", "protected-token-missing",
                    1, 0, 1, "scripted\\after-local.png", chunkReports)])]));
        var markdown = File.ReadAllText(Path.Combine(workspace.Root, "scripted-chunk-two.md"));
        Equal(true, markdown.Contains("chunk=2", StringComparison.Ordinal));
        Equal(true, markdown.Contains("protected-token-missing", StringComparison.Ordinal));
    }

    private static void DoesNotRetrySuccessfulLongLocalLmtBody()
    {
        var source = string.Join('\n', Enumerable.Repeat(
            "This long source body should finish after its successful complete translation.",
            3));
        using var workspace = TemporaryWorkspace.Create();
        var model = Path.Combine(workspace.Root, "model.gguf");
        var cli = Path.Combine(workspace.Root, "llama-cli.exe");
        File.WriteAllText(model, "fixture-model");
        File.WriteAllText(cli, "fixture-cli");
        var sessions = new RecordingLocalLmtSessionFactory("这段长正文已经在第一次整段推理中成功翻译。");
        using var provider = new LocalTranslationProvider(
            new LocalLmtOptions(model, cli, ExpectedModelSha256: LocalLmtArtifactVerifier.Sha256File(model)),
            sessions);

        var result = provider.TranslateAsync(
            new TranslationRequest("English", "Chinese", source, textKind: TranslationTextKind.Body),
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(true, result.IsSuccess);
        Equal(1, sessions.Prompts.Count);
    }

    private static void UsesOfficialLocalLmtTemplateForSocialProfilePrompt()
    {
        using var workspace = TemporaryWorkspace.Create();
        var model = Path.Combine(workspace.Root, "model.gguf");
        var cli = Path.Combine(workspace.Root, "llama-cli.exe");
        File.WriteAllText(model, "fixture-model");
        File.WriteAllText(cli, "fixture-cli");
        var sessions = new RecordingLocalLmtSessionFactory("循着天鹅座的尾羽便能看见织女星的光，渡过银河就能遇见牛郎星。小号→[[K0]] 正在播放→anemoi");
        using var provider = new LocalTranslationProvider(
            new LocalLmtOptions(model, cli, ExpectedModelSha256: LocalLmtArtifactVerifier.Sha256File(model)),
            sessions);

        var result = provider.TranslateAsync(
            new TranslationRequest(
                "Japanese",
                "简体中文",
                "はくちょうの尾をたどってこと座の姫\nの灯り、あの川を渡ればアルタイルに\n会える サブ垢→@azumino_114514 なう\nぷれ→anemoi",
                textKind: TranslationTextKind.Body,
                scene: "social_media"),
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(true, result.IsSuccess);
        Equal(true, sessions.Prompts.Single().StartsWith(
            "Translate the following text from Japanese into Chinese:",
            StringComparison.Ordinal));
        Equal(true, sessions.Prompts.Single().Contains("こと座の姫の灯り", StringComparison.Ordinal));
        Equal(true, sessions.Prompts.Single().Contains("なうぷれ→anemoi", StringComparison.Ordinal));
        Equal(false, sessions.Prompts.Single().Contains("complete social-media passage", StringComparison.Ordinal));
        Equal(false, sessions.Prompts.Single().Contains("wrapped lines as one paragraph", StringComparison.Ordinal));
        Equal(false, sessions.Prompts.Single().Contains("including slang", StringComparison.Ordinal));
        Equal(true, sessions.Prompts.Single().EndsWith("\\nChinese:", StringComparison.Ordinal));
        Equal(false, sessions.Prompts.Single().Contains("@azumino_114514", StringComparison.Ordinal));
    }

    private static void DecodesLocalLmtOutputAsUtf8()
    {
        var startInfo = LocalLmtProcessSession.CreateStartInfo(new LocalLmtSessionStart("llama-cli.exe", "--version"));

        Equal(Encoding.UTF8.WebName, startInfo.StandardOutputEncoding!.WebName);
        Equal(Encoding.UTF8.WebName, startInfo.StandardErrorEncoding!.WebName);
    }

    private static void BypassesEquivalentLocalLmtLanguages()
    {
        var sessions = new RecordingLocalLmtSessionFactory("unused");
        using var provider = new LocalTranslationProvider(new LocalLmtOptions("missing.gguf", "missing-cli.exe"), sessions);

        var result = provider.TranslateAsync(
            new TranslationRequest("Chinese", "简体中文", "保持原文 452×600"),
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(true, result.IsSuccess);
        Equal("保持原文 452×600", result.Text);
        Equal(0, sessions.StartCalls);
    }

    private static void RejectsLocalLmtStartupBannerForHeading()
    {
        using var workspace = TemporaryWorkspace.Create();
        var model = Path.Combine(workspace.Root, "model.gguf");
        var cli = Path.Combine(workspace.Root, "llama-cli.exe");
        File.WriteAllText(model, "fixture-model");
        File.WriteAllText(cli, "fixture-cli");
        using var provider = new LocalTranslationProvider(
            new LocalLmtOptions(model, cli, ExpectedModelSha256: LocalLmtArtifactVerifier.Sha256File(model)),
            new RecordingLocalLmtSessionFactory("unexpected interactive output"),
            oneShotRunner: new RecordingLocalLmtOneShotRunner("Loading model..."),
            collectLocalDiagnosticContext: true);
        var result = provider.TranslateManyAsync(
            [new TranslationRequest("English", "Chinese", "Library notice", textKind: TranslationTextKind.Heading)],
            CancellationToken.None).GetAwaiter().GetResult().Single();
        if (result.IsSuccess || result.Text.Contains("Loading model", StringComparison.Ordinal))
            throw new Exception("A local model startup banner was accepted as a translated heading.");
    }

    private static void RecoversLocalModelAfterFailedVerification()
    {
        foreach (var startsMissing in new[] { true, false })
        {
            using var workspace = TemporaryWorkspace.Create();
            var model = Path.Combine(workspace.Root, "model.gguf");
            var cli = Path.Combine(workspace.Root, "llama-cli.exe");
            var reference = Path.Combine(workspace.Root, "reference.gguf");
            File.WriteAllText(reference, "verified fixture model");
            File.WriteAllText(cli, "fixture executable never launched");
            if (!startsMissing) File.WriteAllText(model, "corrupt fixture");
            var sessions = new RecordingLocalLmtSessionFactory("你好", "你好");
            using var provider = new LocalTranslationProvider(new LocalLmtOptions(model, cli,
                ExpectedModelSha256: LocalLmtArtifactVerifier.Sha256File(reference)), sessions);
            var request = new TranslationRequest("English", "简体中文", "hello");
            Equal(false, provider.TranslateAsync(request, CancellationToken.None).GetAwaiter().GetResult().IsSuccess);
            Equal(0, sessions.StartCalls);
            File.WriteAllText(model, "verified fixture model");
            var recovered = provider.TranslateAsync(request, CancellationToken.None).GetAwaiter().GetResult();
            Equal(true, recovered.IsSuccess);
            Equal("你好", recovered.Text);
            Equal(1, sessions.StartCalls);
            File.WriteAllText(model, "changed model contents after successful verification");
            Equal(false, provider.TranslateAsync(request, CancellationToken.None).GetAwaiter().GetResult().IsSuccess);
            Equal(false, provider.HasActiveSession);
            Equal(1, sessions.StartCalls);
            File.WriteAllText(model, "verified fixture model");
            Equal(true, provider.TranslateAsync(request, CancellationToken.None).GetAwaiter().GetResult().IsSuccess);
            Equal(2, sessions.StartCalls);
        }
        Console.WriteLine("Local model verification recovery passed; inference uses a controlled session fixture.");
    }

    private static void CancelsLocalModelVerification()
    {
        using var workspace = TemporaryWorkspace.Create();
        var model = Path.Combine(workspace.Root, "large-fixture.gguf");
        var cli = Path.Combine(workspace.Root, "fixture-cli.exe");
        using (var stream = File.Create(model)) stream.SetLength(64 * 1024 * 1024);
        File.WriteAllText(cli, "never executed");
        var options = new LocalLmtOptions(model, cli, ExpectedModelSha256: new string('0', 64));
        using var cancellation = new CancellationTokenSource();
        var pending = LocalLmtArtifactVerifier.IsVerifiedAsync(options, cancellation.Token);
        cancellation.Cancel();
        try
        {
            pending.GetAwaiter().GetResult();
            throw new Exception("Artifact verification ignored cancellation.");
        }
        catch (OperationCanceledException) { }
        // A completed cancellation must release the file before repair can proceed.
        using (var stream = new FileStream(model, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            stream.SetLength(16);
        options = options with { ExpectedModelSha256 = LocalLmtArtifactVerifier.Sha256File(model) };
        Equal(true, LocalLmtArtifactVerifier.IsVerifiedAsync(options, CancellationToken.None).GetAwaiter().GetResult());
        Console.WriteLine("Artifact cancellation released the file; subsequent real SHA-256 verification passed.");
    }

    private static void TranslatesOnlyRequiredMixedBatchItems()
    {
        using var workspace = TemporaryWorkspace.Create();
        var model = Path.Combine(workspace.Root, "model.gguf");
        var cli = Path.Combine(workspace.Root, "llama-cli.exe");
        File.WriteAllText(model, "fixture-model");
        File.WriteAllText(cli, "fixture-cli");
        var sessions = new RecordingLocalLmtSessionFactory("你好");
        using var provider = new LocalTranslationProvider(
            new LocalLmtOptions(model, cli, ExpectedModelSha256: LocalLmtArtifactVerifier.Sha256File(model)),
            sessions);

        var results = provider.TranslateManyAsync([
            new TranslationRequest("Chinese", "简体中文", "保持原文"),
            new TranslationRequest("English", "简体中文", "hello"),
        ], CancellationToken.None).GetAwaiter().GetResult();

        Equal(1, sessions.StartCalls);
        Equal("保持原文", results[0].Text);
        Equal("你好", results[1].Text);
    }

    private static void ValidatesTranslationInvariantsWithoutPlaceholderInflation()
    {
        const string source = "size 452×600 codec H.264 stop F8 {name}";
        using var workspace = TemporaryWorkspace.Create();
        var model = Path.Combine(workspace.Root, "model.gguf");
        var cli = Path.Combine(workspace.Root, "llama-cli.exe");
        File.WriteAllText(model, "fixture-model");
        File.WriteAllText(cli, "fixture-cli");
        var sessions = new RecordingLocalLmtSessionFactory("尺寸 452×600 编码 H.264 停止 F8 [[TOKEN]]");
        using var provider = new LocalTranslationProvider(
            new LocalLmtOptions(model, cli, ExpectedModelSha256: LocalLmtArtifactVerifier.Sha256File(model)),
            sessions);

        _ = provider.TranslateAsync(new TranslationRequest("English", "Chinese", source), CancellationToken.None).GetAwaiter().GetResult();

        Equal(false, sessions.Prompts[0].Contains("\"id\":\"b0\"", StringComparison.Ordinal));
        Equal(true, TranslationContentIntegrity.HasSameInvariants(source, "尺寸 452×600 编码 H.264 停止 F8 {name}"));
        Equal(false, TranslationContentIntegrity.HasSameInvariants(source, "尺寸 452×600 编码 H.265 停止 F8 {name}"));
        Equal(false, TranslationContentIntegrity.HasSameInvariants(source, "尺寸 452×600 编码 H.264 停止 {name}"));
    }

    private static void ClassifiesFirstChangedTranslationInvariant()
    {
        var method = typeof(TranslationContentIntegrity).GetMethod("FindMismatch");
        Equal(true, method is not null);
        if (method is null) return;

        Equal("Number", method.Invoke(null, ["Scene 1", "场景一"])?.ToString() ?? string.Empty);
        Equal("ModelToken", method.Invoke(null, ["codec H.264", "编码 H-264"])?.ToString() ?? string.Empty);
        Equal("Hotkey", method.Invoke(null, ["Press F8", "按键 F9"])?.ToString() ?? string.Empty);
        Equal(true, method.Invoke(null, ["Scene 1 H.264 F8", "场景 1 H.264 F8"]) is null);
    }

    private static void AcceptsReorderedDeepSeekTranslationBlockIds()
    {
        var response = JsonTranslation("[{\"id\":\"b1\",\"translation\":\"second\"},{\"id\":\"b0\",\"translation\":\"first\"}]");
        var provider = new DeepSeekTranslationProvider(new RecordingCredentialVault("safe"), new RecordingHttpHandler(HttpStatusCode.OK, response));

        var results = provider.TranslateBlocksAsync(
            [new TranslationRequest("English", "Chinese", "one"), new TranslationRequest("English", "Chinese", "two")],
            "alias",
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(true, results.All(static item => item.IsSuccess));
        Equal("first", results[0].Text);
        Equal("second", results[1].Text);
    }

    private static void RejectsDuplicateDeepSeekTranslationBlockIds()
    {
        var response = JsonTranslation("[{\"id\":\"b0\",\"translation\":\"first\"},{\"id\":\"b0\",\"translation\":\"second\"}]");
        var provider = new DeepSeekTranslationProvider(new RecordingCredentialVault("safe"), new RecordingHttpHandler(HttpStatusCode.OK, response));

        var results = provider.TranslateBlocksAsync(
            [new TranslationRequest("English", "Chinese", "one"), new TranslationRequest("English", "Chinese", "two")],
            "alias",
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(true, results.All(static item => !item.IsSuccess));
        Equal("DeepSeek returned an invalid translation block response.", results[0].Status);
    }

    private static void RejectsInvalidDeepSeekTranslationBlockResponses()
    {
        foreach (var content in new[]
        {
            "[{\"id\":\"b0\",\"translation\":\"first\"}]",
            "```json\\n[{\"id\":\"b0\",\"translation\":\"first\"},{\"id\":\"b1\",\"translation\":\"second\"}]\\n```",
        })
        {
            var provider = new DeepSeekTranslationProvider(new RecordingCredentialVault("safe"), new RecordingHttpHandler(HttpStatusCode.OK, JsonTranslation(content)));
            var results = provider.TranslateBlocksAsync(
                [new TranslationRequest("English", "Chinese", "one"), new TranslationRequest("English", "Chinese", "two")],
                "alias",
                CancellationToken.None).GetAwaiter().GetResult();
            Equal(true, results.Any(static item => !item.IsSuccess));
        }
    }

    private static void AcceptsCodePathAndUrlDeepSeekTranslations()
    {
        foreach (var content in new[] { "```csharp\\nreturn code;\\n```", "return code;", "src/ClipRay.App/App.xaml.cs", "https://example.test/docs" })
        {
            var response = JsonTranslation($"[{{\"id\":\"b0\",\"translation\":{System.Text.Json.JsonSerializer.Serialize(content)}}}]");
            var provider = new DeepSeekTranslationProvider(new RecordingCredentialVault("safe"), new RecordingHttpHandler(HttpStatusCode.OK, response));

            var result = provider.TranslateAsync(
                new TranslationRequest("English", "Chinese", "hello"),
                "alias",
                CancellationToken.None).GetAwaiter().GetResult();

            Equal(true, result.IsSuccess);
            Equal(content, result.Text);
        }
    }

    private static void RequiresNonemptyDeepSeekTranslationsInsteadOfCodeEchoes()
    {
        var handler = new RecordingHttpHandler(HttpStatusCode.OK, JsonTranslation("[{\"id\":\"b0\",\"translation\":\"已翻译\"}]"));
        var provider = new DeepSeekTranslationProvider(new RecordingCredentialVault("safe"), handler);

        var result = provider.TranslateAsync(
            new TranslationRequest("English", "简体中文", "src/ClipRay.App/App.xaml.cs"),
            "alias",
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(true, result.IsSuccess);
        using var payload = System.Text.Json.JsonDocument.Parse(handler.Body!);
        var system = payload.RootElement.GetProperty("messages")[0].GetProperty("content").GetString()!;
        Equal(true, system.Contains("complete non-empty translated text", StringComparison.Ordinal));
        Equal(true, system.Contains("do not add labels", StringComparison.Ordinal));
        Equal(true, system.Contains("target_language", StringComparison.Ordinal));
        Equal(true, system.Contains("mixed-language text", StringComparison.Ordinal));
    }

    private static void UsesObjectEnvelopeCompatibleWithDeepSeekJsonMode()
    {
        var handler = new RecordingHttpHandler(
            HttpStatusCode.OK,
            JsonTranslation("{\"items\":[{\"id\":\"b0\",\"translation\":\"你好\"}]}") );
        var provider = new DeepSeekTranslationProvider(new RecordingCredentialVault("safe"), handler);

        var result = provider.TranslateAsync(
            new TranslationRequest("English", "Chinese", "hello"),
            "alias",
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(true, result.IsSuccess);
        using var payload = System.Text.Json.JsonDocument.Parse(handler.Body!);
        Equal("json_object", payload.RootElement.GetProperty("response_format").GetProperty("type").GetString()!);
        var user = payload.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!;
        Equal(true, user.TrimStart().StartsWith("{\"b0\":", StringComparison.Ordinal));
        var system = payload.RootElement.GetProperty("messages")[0].GetProperty("content").GetString()!;
        Equal(true, system.Contains("exactly one JSON object", StringComparison.Ordinal));
        Equal(true, system.Contains("Preserve URLs, file paths, code", StringComparison.Ordinal));
    }

    private static void UsesCompactIdKeyedDeepSeekResponse()
    {
        var provider = new DeepSeekTranslationProvider(
            new RecordingCredentialVault("safe"),
            new RecordingHttpHandler(
                HttpStatusCode.OK,
                JsonTranslation("{\"b1\":\"second\",\"b0\":\"first\"}")));

        var results = provider.TranslateBlocksAsync(
            [new TranslationRequest("auto", "English", "one"), new TranslationRequest("auto", "English", "two")],
            "alias",
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(true, results.All(static item => item.IsSuccess));
        Equal("first", results[0].Text);
        Equal("second", results[1].Text);
    }

    private static void LeavesDeepSeekOutputTokenLimitToService()
    {
        var handler = new RecordingHttpHandler(
            HttpStatusCode.OK,
            JsonTranslation("{\"b0\":\"hello\"}"));
        var provider = new DeepSeekTranslationProvider(new RecordingCredentialVault("safe"), handler);

        _ = provider.TranslateAsync(
            new TranslationRequest("auto", "English", "hello"),
            "alias",
            CancellationToken.None).GetAwaiter().GetResult();

        using var payload = System.Text.Json.JsonDocument.Parse(handler.Body!);
        Equal(false, payload.RootElement.TryGetProperty("max_tokens", out _));
    }

    private static void DoesNotRetryInvalidDeepSeekBatch()
    {
        var handler = new RecordingHttpHandler(HttpStatusCode.OK, JsonTranslation("{}"));
        var provider = new DeepSeekTranslationProvider(new RecordingCredentialVault("safe"), handler);

        var results = provider.TranslateBlocksAsync(
            [new TranslationRequest("auto", "English", "one")],
            "alias",
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(1, handler.CallCount);
        Equal(true, results.All(static item => !item.IsSuccess));
    }

    private static void RejectsUnchangedSourceAsSuccessfulTranslation()
    {
        var provider = new DeepSeekTranslationProvider(
            new RecordingCredentialVault("safe"),
            new RecordingHttpHandler(
                HttpStatusCode.OK,
                JsonTranslation("{\"items\":[{\"id\":\"b0\",\"translation\":\"Translate this sentence\"}]}") ));

        var result = provider.TranslateAsync(
            new TranslationRequest("auto", "简体中文", "Translate this sentence"),
            "alias",
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(false, result.IsSuccess);
        Equal("DeepSeek returned the source text unchanged.", result.Status);
        Equal(TranslationDiagnosticKind.SourceUnchanged, result.Diagnostic!.Kind);
    }

    private static void AcceptsUnchangedOpaquePathBlocks()
    {
        var paths = new[]
        {
            "E:/ExampleApp/config/download-rules.json",
            "src/ClipRay.App/ClipRay.App.csproj",
            "src/ClipRay.App/Ocr/OcrPolarityPlan.cs",
        };
        var response = JsonTranslation(System.Text.Json.JsonSerializer.Serialize(
            paths.Select((path, index) => new { id = $"b{index}", translation = path })));
        var provider = new DeepSeekTranslationProvider(
            new RecordingCredentialVault("safe"),
            new RecordingHttpHandler(HttpStatusCode.OK, response));

        var results = provider.TranslateBlocksAsync(
            paths.Select(path => new TranslationRequest("auto", "简体中文", path)).ToArray(),
            "alias",
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(true, results.All(static result => result.IsSuccess));
        Equal(string.Join('|', paths), string.Join('|', results.Select(static result => result.Text)));
    }

    private static void AcceptsUnchangedSupportedTargetLanguageText()
    {
        var cases = new[]
        {
            (Target: "简体中文", Text: "今天天气很好。"),
            (Target: "English", Text: "The weather is pleasant today."),
            (Target: "日本語", Text: "今日はとても良い天気です。"),
            (Target: "Français", Text: "Le temps est très agréable aujourd'hui."),
            (Target: "Русский", Text: "Сегодня очень хорошая погода."),
        };
        var response = JsonTranslation(System.Text.Json.JsonSerializer.Serialize(
            cases.Select((item, index) => new { id = $"b{index}", translation = item.Text })));
        var provider = new DeepSeekTranslationProvider(
            new RecordingCredentialVault("safe"),
            new RecordingHttpHandler(HttpStatusCode.OK, response));

        var results = provider.TranslateBlocksAsync(
            cases.Select(item => new TranslationRequest("auto", item.Target, item.Text)).ToArray(),
            "alias", CancellationToken.None).GetAwaiter().GetResult();

        Equal(true, results.All(static result => result.IsSuccess));
    }

    private static void RejectsUnchangedMultilingualSourcesForChinese()
    {
        var sources = new[]
        {
            "アイコンの子がまばたきしています。",
            "오늘 날씨가 정말 좋습니다.",
            "Сегодня очень хорошая погода.",
            "Le temps est très agréable aujourd'hui.",
            "الطقس جميل جدًا اليوم.",
            "วันนี้อากาศดีมาก",
        };
        var response = JsonTranslation(System.Text.Json.JsonSerializer.Serialize(
            sources.Select((text, index) => new { id = $"b{index}", translation = text })));
        var provider = new DeepSeekTranslationProvider(
            new RecordingCredentialVault("safe"),
            new RecordingHttpHandler(HttpStatusCode.OK, response));

        var results = provider.TranslateBlocksAsync(
            sources.Select(text => new TranslationRequest("auto", "简体中文", text)).ToArray(),
            "alias", CancellationToken.None).GetAwaiter().GetResult();

        Equal(true, results.All(static result => !result.IsSuccess));
    }

    private static void AcceptsOfficialLatinNameForSeparatedKatakanaProperName()
    {
        var request = VisualTranslationRequestFactory.Create(
            [VisualRegion("r0", "コミックス・ウェーブ・フィルム", VisualTextRole.Control)],
            "简体中文",
            DeepSeekModels.Flash)[0];
        var provider = new DeepSeekTranslationProvider(
            new RecordingCredentialVault("safe"),
            new RecordingHttpHandler(HttpStatusCode.OK, JsonTranslation("{\"b0\":\"CoMix Wave Films\"}")));

        var result = provider.TranslateAsync(request, "alias", CancellationToken.None).GetAwaiter().GetResult();

        Equal(true, result.IsSuccess);
        Equal("CoMix Wave Films", result.Text);
    }

    private static void RejectsUnverifiedLatinRenderingForSeparatedKatakanaText()
    {
        var request = VisualTranslationRequestFactory.Create(
            [VisualRegion("r0", "コピー・ペースト・メニュー", VisualTextRole.Control)],
            "简体中文",
            DeepSeekModels.Flash)[0];
        var provider = new DeepSeekTranslationProvider(
            new RecordingCredentialVault("safe"),
            new RecordingHttpHandler(HttpStatusCode.OK, JsonTranslation("{\"b0\":\"Copy Paste Operation\"}")));

        var result = provider.TranslateAsync(request, "alias", CancellationToken.None).GetAwaiter().GetResult();

        Equal(false, result.IsSuccess);
        Equal("DeepSeek returned text without target-language content.", result.Status);
        Equal(TranslationDiagnosticKind.MissingTargetLanguage, result.Diagnostic!.Kind);
    }

    private static void ReportsUnchangedSeparatedKatakanaProperNamePrecisely()
    {
        const string source = "コミックス・ウェーブ・フィルム";
        var request = VisualTranslationRequestFactory.Create(
            [VisualRegion("r0", source, VisualTextRole.Control)],
            "简体中文",
            DeepSeekModels.Flash)[0];
        var provider = new DeepSeekTranslationProvider(
            new RecordingCredentialVault("safe"),
            new RecordingHttpHandler(HttpStatusCode.OK, JsonTranslation("{\"b0\":\"コミックス・ウェーブ・フィルム\"}")));

        var result = provider.TranslateAsync(request, "alias", CancellationToken.None).GetAwaiter().GetResult();

        Equal(false, result.IsSuccess);
        Equal("DeepSeek returned the source text unchanged.", result.Status);
        Equal("DeepSeek 返回了未翻译的原文。", TranslationStatusText.LocalizeProviderStatus(result.Status));
    }

    private static void ReportsMissingTargetLanguageContentPrecisely()
    {
        var provider = new DeepSeekTranslationProvider(
            new RecordingCredentialVault("safe"),
            new RecordingHttpHandler(HttpStatusCode.OK, JsonTranslation("{\"b0\":\"Browse\"}")));

        var result = provider.TranslateAsync(
            new TranslationRequest("English", "简体中文", "Search", textKind: TranslationTextKind.UiControl),
            "alias",
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(false, result.IsSuccess);
        Equal("DeepSeek returned text without target-language content.", result.Status);
        Equal(TranslationDiagnosticKind.MissingTargetLanguage, result.Diagnostic!.Kind);
        Equal("DeepSeek 返回的译文缺少目标语言内容。", TranslationStatusText.LocalizeProviderStatus(result.Status));
    }

    private static void KeepsValidDeepSeekSiblingsWhenOneBlockIsInvalid()
    {
        var response = JsonTranslation("{\"b0\":\"un\",\"b1\":\"two\",\"b2\":\"trois\"}");
        var handler = new RecordingHttpHandler(HttpStatusCode.OK, response);
        var provider = new DeepSeekTranslationProvider(new RecordingCredentialVault("safe"), handler);

        var results = provider.TranslateBlocksAsync(
            [
                new TranslationRequest("English", "French", "one"),
                new TranslationRequest("English", "French", "two"),
                new TranslationRequest("English", "French", "three"),
            ],
            "alias",
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(2, handler.CallCount);
        Equal(true, results[0].IsSuccess);
        Equal(false, results[1].IsSuccess);
        Equal(true, results[2].IsSuccess);
        Equal("un", results[0].Text);
        Equal("trois", results[2].Text);
    }

    private static void ClassifiesTranslationOverlayBlocks()
    {
        var blocks = new[]
        {
            new OcrTextBlock("hello", new PixelRect(0, 0, 40, 12), []),
            new OcrTextBlock("src/ClipRay.App/App.xaml.cs", new PixelRect(0, 20, 160, 12), []),
            new OcrTextBlock("failed", new PixelRect(0, 40, 40, 12), []),
        };
        var plan = TranslationOverlayPlan.Create(blocks,
        [
            new TranslationResult(true, "bonjour", "ok"),
            new TranslationResult(true, "src/ClipRay.App/App.xaml.cs", "ok"),
            new TranslationResult(false, string.Empty, "provider failure"),
        ]);

        Equal(TranslationBlockState.Changed, plan[0].State);
        Equal(TranslationBlockState.Unchanged, plan[1].State);
        Equal(TranslationBlockState.Failed, plan[2].State);
        Equal("hello", plan[0].SourceBlock.Text);
        Equal("bonjour", plan[0].Text);
    }

    private static void BuildsOverlayOnlyForChangedTranslationBlocks()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var source = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "App.xaml.cs"));
        var methodStart = source.IndexOf("private async void HandleOverlayTranslationRequested", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("private async void HandleTextExtractionRequested", methodStart, StringComparison.Ordinal);
        Equal(true, methodStart >= 0 && methodEnd > methodStart);
        var method = source[methodStart..methodEnd];

        Equal(true, method.Contains("_translationOrchestrator.ExecuteAsync", StringComparison.Ordinal));
        Equal(true, method.Contains("outcome.LayoutItems", StringComparison.Ordinal));
        Equal(true, method.Contains("outcome.LayoutItems.Count", StringComparison.Ordinal));
        Equal(false, method.Contains("results.FirstOrDefault", StringComparison.Ordinal));
    }

    private static void TreatsMixedLanguageOcrAsPartiallyUntranslated()
    {
        var handler = new RecordingHttpHandler(
            HttpStatusCode.OK,
            JsonTranslation("{\"items\":[{\"id\":\"b0\",\"translation\":\"验证结果：DeepSeek\"}]}") );
        var provider = new DeepSeekTranslationProvider(new RecordingCredentialVault("safe"), handler);

        var result = provider.TranslateAsync(
            new TranslationRequest("auto", "简体中文", "验证 result: DeepSeek"),
            "alias",
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(true, result.IsSuccess);
        using var payload = System.Text.Json.JsonDocument.Parse(handler.Body!);
        var system = payload.RootElement.GetProperty("messages")[0].GetProperty("content").GetString()!;
        Equal(true, system.Contains("mixed-language text", StringComparison.Ordinal));
        Equal(true, system.Contains("every human-readable segment", StringComparison.Ordinal));
        Equal(true, system.Contains("already in the target language unchanged", StringComparison.Ordinal));
    }

    private static void AcceptsFencedDeepSeekJsonTransport()
    {
        var content = "```json\n[{\"id\":\"b0\",\"translation\":\"已翻译\"}]\n```";
        var provider = new DeepSeekTranslationProvider(
            new RecordingCredentialVault("safe"),
            new RecordingHttpHandler(HttpStatusCode.OK, JsonTranslation(content)));

        var result = provider.TranslateAsync(
            new TranslationRequest("English", "简体中文", "hello"),
            "alias",
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(true, result.IsSuccess);
        Equal("已翻译", result.Text);
    }

    private static void CompletesStreamingDeepSeekResponseAtFinishReason()
    {
        var stream = string.Join("\n\n", [
            "data: {\"choices\":[{\"delta\":{\"content\":\"[{\\\"id\\\":\\\"b0\\\",\\\"translation\\\":\\\"ok\\\"}]\"},\"finish_reason\":null}]}",
            "data: {\"choices\":[{\"delta\":{\"content\":\"\"},\"finish_reason\":\"stop\"}]}",
        ]);
        var provider = new DeepSeekTranslationProvider(
            new RecordingCredentialVault("safe"),
            new RecordingHttpHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(stream, Encoding.UTF8, "text/event-stream"),
            })));

        var result = provider.TranslateAsync(
            new TranslationRequest("English", "简体中文", "hello"),
            "alias",
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(true, result.IsSuccess);
        Equal("ok", result.Text);
    }

    private static void RequestsAndReadsDeepSeekStreamingUsage()
    {
        var handler = new RecordingHttpHandler(
            HttpStatusCode.OK,
            StreamingJsonTranslationWithUsage("{\"b0\":\"你好\"}", "stop", 1_000, 2_000, 3_000));
        var provider = new DeepSeekTranslationProvider(new RecordingCredentialVault("safe"), handler);

        var result = provider.TranslateAsync(
            new TranslationRequest("English", "简体中文", "hello"),
            "alias",
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(true, result.IsSuccess);
        using var payload = System.Text.Json.JsonDocument.Parse(handler.Body!);
        Equal(true, payload.RootElement.GetProperty("stream_options").GetProperty("include_usage").GetBoolean());
        var estimate = result.GetType().GetProperty("CostEstimate")?.GetValue(result);
        Equal(true, estimate is not null);
        Equal(1_000L, (long)estimate!.GetType().GetProperty("PromptCacheHitTokens")!.GetValue(estimate)!);
        Equal(2_000L, (long)estimate.GetType().GetProperty("PromptCacheMissTokens")!.GetValue(estimate)!);
        Equal(3_000L, (long)estimate.GetType().GetProperty("CompletionTokens")!.GetValue(estimate)!);
    }

    private static void CalculatesCurrentAndScheduledDeepSeekPrices()
    {
        var pricingType = typeof(DeepSeekTranslationProvider).Assembly.GetType("ClipRay.App.Translation.DeepSeekPricing");
        Equal(true, pricingType is not null);
        var estimateMethod = pricingType!.GetMethod("Estimate", [
            typeof(string), typeof(long), typeof(long), typeof(long), typeof(DateTimeOffset),
        ]);
        Equal(true, estimateMethod is not null);

        decimal Estimate(string model, DateTimeOffset at)
        {
            var estimate = estimateMethod!.Invoke(null, [model, 1_000L, 2_000L, 3_000L, at])!;
            return (decimal)estimate.GetType().GetProperty("EstimatedCny")!.GetValue(estimate)!;
        }

        Equal(0.00802m, Estimate(DeepSeekModels.Flash, new DateTimeOffset(2026, 8, 13, 12, 0, 0, TimeSpan.FromHours(8))));
        Equal(0.024025m, Estimate(DeepSeekModels.Pro, new DateTimeOffset(2026, 8, 13, 12, 0, 0, TimeSpan.FromHours(8))));
        Equal(0.0331m, Estimate(DeepSeekModels.Flash, new DateTimeOffset(2026, 8, 17, 10, 0, 0, TimeSpan.FromHours(8))));
        Equal(0.01655m, Estimate(DeepSeekModels.Flash, new DateTimeOffset(2026, 8, 17, 22, 0, 0, TimeSpan.FromHours(8))));
    }

    private static void DistinguishesDeepSeekPeakAndOffPeakPriceWindows()
    {
        var effectiveAt = new DateTimeOffset(2026, 8, 16, 16, 0, 0, TimeSpan.Zero);
        var peak = DeepSeekPricing.Estimate(
            DeepSeekModels.Flash,
            1_000,
            2_000,
            3_000,
            effectiveAt.AddHours(9));
        var offPeak = DeepSeekPricing.Estimate(
            DeepSeekModels.Flash,
            1_000,
            2_000,
            3_000,
            effectiveAt.AddHours(3));
        var proOffPeak = DeepSeekPricing.Estimate(
            DeepSeekModels.Pro,
            1_000,
            2_000,
            3_000,
            effectiveAt.AddHours(3));
        var peakBoundary = DeepSeekPricing.Estimate(
            DeepSeekModels.Flash,
            1_000,
            2_000,
            3_000,
            new DateTimeOffset(2026, 8, 17, 12, 0, 0, TimeSpan.FromHours(8)));

        Equal(0.0331m, peak.EstimatedCny);
        Equal("2026-08-17 起高峰价", peak.PriceSchedule);
        Equal(true, DeepSeekPricing.IsPeakPeriod(new DateTimeOffset(2026, 8, 17, 9, 0, 0, TimeSpan.FromHours(8))));
        Equal(false, DeepSeekPricing.IsPeakPeriod(new DateTimeOffset(2026, 8, 17, 12, 0, 0, TimeSpan.FromHours(8))));
        Equal(0.01655m, offPeak.EstimatedCny);
        Equal("2026-08-17 起低谷价", offPeak.PriceSchedule);
        Equal(0.04965m, proOffPeak.EstimatedCny);
        Equal("2026-08-17 起低谷价", proOffPeak.PriceSchedule);
        Equal("2026-08-17 起低谷价", peakBoundary.PriceSchedule);
    }

    private static void AccumulatesDeepSeekCostAcrossRecoveryRequests()
    {
        var handler = new RecordingHttpHandler(async (request, cancellationToken) =>
        {
            using var payload = System.Text.Json.JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            using var prompt = System.Text.Json.JsonDocument.Parse(
                payload.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!);
            var entries = prompt.RootElement.EnumerateObject().ToArray();
            var content = entries.Length > 1
                ? "{\"b0\""
                : System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string>
                {
                    [entries[0].Name] = $"translated-{entries[0].Value.GetProperty("text").GetString()}",
                });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    StreamingJsonTranslationWithUsage(
                        content,
                        entries.Length > 1 ? "length" : "stop",
                        entries.Length > 1 ? 10 : 5,
                        entries.Length > 1 ? 100 : 50,
                        entries.Length > 1 ? 20 : 10),
                    Encoding.UTF8,
                    "text/event-stream"),
            };
        });
        var provider = new DeepSeekTranslationProvider(new RecordingCredentialVault("safe"), handler);

        var results = provider.TranslateBlocksAsync([
            new TranslationRequest("Chinese", "English", "第一段"),
            new TranslationRequest("Chinese", "English", "第二段"),
        ], "alias", CancellationToken.None).GetAwaiter().GetResult();

        Equal(true, results.All(static result => result.IsSuccess));
        Equal(3, handler.CallCount);
        var estimates = results.Select(result => result.GetType().GetProperty("CostEstimate")?.GetValue(result)).ToArray();
        Equal(true, estimates.All(static estimate => estimate is not null));
        Equal(3, (int)estimates[0]!.GetType().GetProperty("RequestCount")!.GetValue(estimates[0])!);
        Equal(20L, (long)estimates[0]!.GetType().GetProperty("PromptCacheHitTokens")!.GetValue(estimates[0])!);
        Equal(200L, (long)estimates[0]!.GetType().GetProperty("PromptCacheMissTokens")!.GetValue(estimates[0])!);
        Equal(40L, (long)estimates[0]!.GetType().GetProperty("CompletionTokens")!.GetValue(estimates[0])!);
    }

    private static void FormatsDeepSeekCostAsChineseEstimate()
    {
        var pricingType = typeof(DeepSeekTranslationProvider).Assembly.GetType("ClipRay.App.Translation.DeepSeekPricing");
        Equal(true, pricingType is not null);
        var estimate = pricingType!.GetMethod("Estimate", [
            typeof(string), typeof(long), typeof(long), typeof(long), typeof(DateTimeOffset),
        ])!.Invoke(null, [
            DeepSeekModels.Flash,
            1_000L,
            2_000L,
            3_000L,
            new DateTimeOffset(2026, 8, 13, 12, 0, 0, TimeSpan.FromHours(8)),
        ]);
        var method = typeof(TranslationStatusText).GetMethod("FormatCostEstimate");
        Equal(true, method is not null);
        Equal("本次预估费用：约 ¥0.00802（基准价）", method!.Invoke(null, [estimate]) as string ?? string.Empty);
        var overlayStatus = TranslationStatusText.FormatOverlayCompletion(
            true,
            29,
            33,
            (DeepSeekCostEstimate)estimate!);
        Equal(true, overlayStatus.Contains("DeepSeek · 约 ¥0.00802（基准价） · 已替换 29 处", StringComparison.Ordinal));
    }

    private static void PersistsOnlyAggregateDeepSeekCostStatistics()
    {
        using var workspace = TemporaryWorkspace.Create();
        var ledgerType = typeof(DeepSeekPricing).Assembly.GetType("ClipRay.App.Translation.DeepSeekCostLedger");
        Equal(true, ledgerType is not null);
        if (ledgerType is null) return;
        var path = Path.Combine(workspace.Root, "deepseek-cost-summary.json");
        var ledger = Activator.CreateInstance(ledgerType, path)!;
        var estimate = DeepSeekPricing.Estimate(
            DeepSeekModels.Flash,
            100,
            200,
            300,
            new DateTimeOffset(2026, 8, 13, 12, 0, 0, TimeSpan.Zero));
        ledgerType.GetMethod("Record")!.Invoke(ledger, [estimate]);
        ledgerType.GetMethod("Record")!.Invoke(ledger, [estimate]);
        var summary = ledgerType.GetMethod("Load")!.Invoke(ledger, null)!;

        Equal(estimate.EstimatedCny * 2, (decimal)summary.GetType().GetProperty("EstimatedCny")!.GetValue(summary)!);
        Equal(200L, (long)summary.GetType().GetProperty("PromptCacheHitTokens")!.GetValue(summary)!);
        Equal(400L, (long)summary.GetType().GetProperty("PromptCacheMissTokens")!.GetValue(summary)!);
        Equal(600L, (long)summary.GetType().GetProperty("CompletionTokens")!.GetValue(summary)!);
        Equal(2, (int)summary.GetType().GetProperty("ApiRequestCount")!.GetValue(summary)!);
        var persisted = File.ReadAllText(path);
        Equal(false, persisted.Contains("translationText", StringComparison.OrdinalIgnoreCase));
        Equal(false, persisted.Contains("ocrText", StringComparison.OrdinalIgnoreCase));
        Equal(false, persisted.Contains("apiKey", StringComparison.OrdinalIgnoreCase));
    }

    private static void ResetsDeepSeekCostStatisticsAtomically()
    {
        using var workspace = TemporaryWorkspace.Create();
        var path = Path.Combine(workspace.Root, "deepseek-cost-summary.json");
        var ledger = new DeepSeekCostLedger(path);
        ledger.Record(DeepSeekPricing.Estimate(
            DeepSeekModels.Flash,
            100,
            200,
            300,
            new DateTimeOffset(2026, 8, 13, 12, 0, 0, TimeSpan.Zero)));
        var resetAt = new DateTimeOffset(2026, 8, 13, 13, 0, 0, TimeSpan.Zero);

        var reset = ledger.Reset(resetAt);
        var loaded = ledger.Load();

        Equal(0m, reset.EstimatedCny);
        Equal(0L, reset.PromptCacheHitTokens);
        Equal(0L, reset.PromptCacheMissTokens);
        Equal(0L, reset.CompletionTokens);
        Equal(0, reset.ApiRequestCount);
        Equal(resetAt, reset.StartedAtUtc);
        Equal(reset, loaded);
        Equal(false, Directory.EnumerateFiles(workspace.Root, "*.tmp-*", SearchOption.TopDirectoryOnly).Any());
    }

    private static void RecoversTruncatedDeepSeekBatchWithoutLosingOrder()
    {
        var sources = Enumerable.Range(0, 8).Select(index => $"第{index}段长中文").ToArray();
        var calls = new List<int>();
        var handler = new RecordingHttpHandler(async (request, cancellationToken) =>
        {
            using var body = System.Text.Json.JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            using var items = System.Text.Json.JsonDocument.Parse(body.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!);
            var input = items.RootElement;
            calls.Add(input.EnumerateObject().Count());
            if (input.EnumerateObject().Count() > 2)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        StreamingJsonTranslationWithFinishReason("{\"items\":[{\"id\":\"b0\"", "length"),
                        Encoding.UTF8,
                        "text/event-stream"),
                };
            }

            var response = System.Text.Json.JsonSerializer.Serialize(
                input.EnumerateObject().ToDictionary(
                    item => item.Name,
                    item => $"translated-{item.Value.GetProperty("text").GetString()}"));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonTranslation(response), Encoding.UTF8, "application/json"),
            };
        });
        var provider = new DeepSeekTranslationProvider(new RecordingCredentialVault("safe"), handler);

        var results = provider.TranslateBlocksAsync(
            sources.Select(source => new TranslationRequest("Chinese", "English", source)).ToArray(),
            "alias",
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(true, results.All(static result => result.IsSuccess));
        Equal(string.Join('|', sources.Select(source => $"translated-{source}")), string.Join('|', results.Select(static result => result.Text)));
        Equal(true, calls.SequenceEqual([8, 4, 2, 2, 4, 2, 2]));
    }

    private static void ReportsTruncatedSingleDeepSeekItemExplicitly()
    {
        var provider = new DeepSeekTranslationProvider(
            new RecordingCredentialVault("safe"),
            new RecordingHttpHandler(HttpStatusCode.OK, JsonTranslationWithFinishReason("{\"items\":[", "length")));

        var result = provider.TranslateAsync(
            new TranslationRequest("Chinese", "English", "一段很长的中文"),
            "alias",
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(false, result.IsSuccess);
        Equal(true, result.Status.Contains("truncated", StringComparison.OrdinalIgnoreCase));
    }

    private static void RetriesOnlySemanticallyInvalidSiblings()
    {
        var calls = new List<string[]>();
        var handler = new RecordingHttpHandler(async (request, cancellationToken) =>
        {
            using var body = System.Text.Json.JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            using var input = System.Text.Json.JsonDocument.Parse(body.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!);
            var ids = input.RootElement.EnumerateObject().Select(static item => item.Name).ToArray();
            calls.Add(ids);
            var response = ids.Length == 3
                ? "{\"b0\":\"アイコンの子がまばたきしています。\",\"b1\":\"今天天气很好。\",\"b2\":\"这是一个测试。\"}"
                : "{\"b0\":\"图标中的角色正在眨眼。\"}";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonTranslation(response), Encoding.UTF8, "application/json"),
            };
        });
        var provider = new DeepSeekTranslationProvider(new RecordingCredentialVault("safe"), handler);

        var results = provider.TranslateBlocksAsync([
            new TranslationRequest("auto", "简体中文", "アイコンの子がまばたきしています。"),
            new TranslationRequest("auto", "简体中文", "The weather is pleasant today."),
            new TranslationRequest("auto", "简体中文", "This is a test."),
        ], "alias", CancellationToken.None).GetAwaiter().GetResult();

        Equal(true, results.All(static result => result.IsSuccess));
        Equal("图标中的角色正在眨眼。", results[0].Text);
        Equal(true, calls.Select(static call => call.Length).SequenceEqual([3, 1]));
        Equal("b0", calls[1][0]);
    }

    private static void RecoversDeepSeekBatchWithOneInvalidItem()
    {
        var sources = Enumerable.Range(0, 4).Select(index => $"第{index}条混合中文").ToArray();
        var calls = new List<int>();
        var handler = new RecordingHttpHandler(async (request, cancellationToken) =>
        {
            using var body = System.Text.Json.JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            using var items = System.Text.Json.JsonDocument.Parse(body.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!);
            var input = items.RootElement;
            calls.Add(input.EnumerateObject().Count());
            var response = input.EnumerateObject().Count() > 1
                ? "{}"
                : System.Text.Json.JsonSerializer.Serialize(
                    input.EnumerateObject().ToDictionary(
                        item => item.Name,
                        item => $"translated-{item.Value.GetProperty("text").GetString()}"));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonTranslation(response), Encoding.UTF8, "application/json"),
            };
        });
        var provider = new DeepSeekTranslationProvider(new RecordingCredentialVault("safe"), handler);

        var results = provider.TranslateBlocksAsync(
            sources.Select(source => new TranslationRequest("Chinese", "English", source)).ToArray(),
            "alias",
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(true, results.All(static result => result.IsSuccess));
        Equal(string.Join('|', sources.Select(source => $"translated-{source}")), string.Join('|', results.Select(static result => result.Text)));
        Equal(true, calls.SequenceEqual([4, 2, 1, 1, 2, 1, 1]));
    }

    private static void ReportsActionableDeepSeekHttpFailures()
    {
        var handler = new RecordingHttpHandler(HttpStatusCode.TooManyRequests, "{}");
        var provider = new DeepSeekTranslationProvider(
            new RecordingCredentialVault("safe"),
            handler);

        var result = provider.TranslateAsync(
            new TranslationRequest("English", "简体中文", "hello"),
            "alias",
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(false, result.IsSuccess);
        Equal(true, result.Status.Contains("429", StringComparison.Ordinal));
        Equal(true, result.Status.Contains("频率", StringComparison.Ordinal));
        Equal(1, handler.CallCount);
    }

    private static void KeepsActionableDeepSeekConnectionFailureCategories()
    {
        var provider = new DeepSeekTranslationProvider(
            new RecordingCredentialVault("safe"),
            new RecordingHttpHandler(HttpStatusCode.TooManyRequests, "{}"));

        var result = provider.TestConnectionAsync("default", DeepSeekModels.Flash, CancellationToken.None)
            .GetAwaiter().GetResult();

        Equal(false, result.IsSuccess);
        Equal(true, result.Status.Contains("429", StringComparison.Ordinal));
        Equal(true, result.Status.Contains("频率", StringComparison.Ordinal));
    }

    private static void ClassifiesMissingFixedDeepSeekCredential()
    {
        var provider = new DeepSeekTranslationProvider(
            new RecordingCredentialVault(null),
            new RecordingHttpHandler(HttpStatusCode.OK, "{}"));

        var result = provider.TestConnectionAsync(
            CredentialVault.FixedDeepSeekSlot,
            DeepSeekModels.Flash,
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(false, result.IsSuccess);
        Equal("credential-missing", result.Status);
    }

    private static void ClassifiesDeepSeekNetworkFailure()
    {
        var provider = new DeepSeekTranslationProvider(
            new RecordingCredentialVault("safe"),
            new RecordingHttpHandler((_, _) => throw new HttpRequestException("private endpoint detail")));

        var result = provider.TestConnectionAsync(
            CredentialVault.FixedDeepSeekSlot,
            DeepSeekModels.Flash,
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(false, result.IsSuccess);
        Equal("network-failed", result.Status);
    }

    private static void ClassifiesMalformedDeepSeekStreamingDataWithoutExposingIt()
    {
        const string malformedData = "data: {not-json}\n\n";
        var provider = new DeepSeekTranslationProvider(
            new RecordingCredentialVault("safe"),
            new RecordingHttpHandler(HttpStatusCode.OK, malformedData));

        var result = provider.TranslateAsync(
            new TranslationRequest("English", "简体中文", "hello"),
            CredentialVault.FixedDeepSeekSlot,
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(false, result.IsSuccess);
        Equal("DeepSeek returned an invalid translation block response.", result.Status);
        Equal(false, result.Status.Contains("not-json", StringComparison.Ordinal));
    }

    private static void DoesNotSplitMalformedDeepSeekStreamIntoRetryBatches()
    {
        var handler = new RecordingHttpHandler(HttpStatusCode.OK, "data: {not-json}\n\n");
        var provider = new DeepSeekTranslationProvider(new RecordingCredentialVault("safe"), handler);

        var results = provider.TranslateBlocksAsync(
            [
                new TranslationRequest("English", "简体中文", "first"),
                new TranslationRequest("English", "简体中文", "second"),
            ],
            CredentialVault.FixedDeepSeekSlot,
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(1, handler.CallCount);
        Equal(true, results.All(static result => result.Status == "DeepSeek returned an invalid translation block response."));
    }

    private static void DoesNotSplitEmptyDeepSeekStreamIntoRetryBatches()
    {
        var handler = new RecordingHttpHandler(HttpStatusCode.OK, "data: [DONE]\n\n");
        var provider = new DeepSeekTranslationProvider(new RecordingCredentialVault("safe"), handler);

        var results = provider.TranslateBlocksAsync(
            [
                new TranslationRequest("English", "简体中文", "first"),
                new TranslationRequest("English", "简体中文", "second"),
            ],
            CredentialVault.FixedDeepSeekSlot,
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(1, handler.CallCount);
        Equal(true, results.All(static result => result.Status == "DeepSeek returned no translation blocks."));
    }

    private static void PreservesStructuralDeepSeekFailureFamilyWhenRecoveryBudgetIsExhausted()
    {
        var provider = new DeepSeekTranslationProvider(
            new RecordingCredentialVault("safe"),
            new RecordingHttpHandler(HttpStatusCode.OK, JsonTranslation("{}")));
        var requests = Enumerable.Range(0, 8)
            .Select(index => new TranslationRequest("English", "简体中文", "item" + index))
            .ToArray();

        var results = provider.TranslateBlocksAsync(requests, CredentialVault.FixedDeepSeekSlot, CancellationToken.None)
            .GetAwaiter().GetResult();

        Equal(8, results.Count);
        Equal(true, results.All(static result => result.Status == "DeepSeek returned an invalid translation block response."));
    }

    private static void ClassifiesUnexpectedDeepSeekProviderFailureWithoutExposingDetails()
    {
        const string privateDetail = "private provider state";
        var provider = new DeepSeekTranslationProvider(
            new RecordingCredentialVault("safe"),
            new RecordingHttpHandler((_, _) => throw new InvalidOperationException(privateDetail)));

        var result = provider.TranslateAsync(
            new TranslationRequest("English", "简体中文", "hello"),
            CredentialVault.FixedDeepSeekSlot,
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(false, result.IsSuccess);
        Equal("DeepSeek provider internal failure.", result.Status);
        Equal(false, result.Status.Contains(privateDetail, StringComparison.Ordinal));
    }

    private static void ClassifiesCandidateDeepSeekAuthenticationFailure()
    {
        var provider = new DeepSeekTranslationProvider(
            new RecordingCredentialVault(null),
            new RecordingHttpHandler(HttpStatusCode.Unauthorized, "{}"));
        var result = provider.TestCandidateConnectionAsync(
            "sk-10430abcdefghijklmnopfb25",
            DeepSeekModels.Flash,
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(false, result.IsSuccess);
        Equal(true, result.Status.Contains("401", StringComparison.Ordinal));
        Equal(true, result.Status.Contains("凭据", StringComparison.Ordinal));
    }

    private static void DisplaysSanitizedCandidateConnectionCategoriesInSettings()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var source = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "Settings", "SettingsWindow.xaml.cs"));
        Equal(true, source.Contains("ProviderStatus(verified.Status)", StringComparison.Ordinal));
        Equal("未找到本机 DeepSeek 凭据。", TranslationStatusText.LocalizeProviderStatus("credential-missing"));
        Equal("No local DeepSeek credential was found.", TranslationStatusText.EnglishProviderStatus("credential-missing"));
    }

    private static void KeepsLocalLmtIdsOutOfModelPrompts()
    {
        using var workspace = TemporaryWorkspace.Create();
        var model = Path.Combine(workspace.Root, "model.gguf");
        var cli = Path.Combine(workspace.Root, "llama-cli.exe");
        File.WriteAllText(model, "fixture-model");
        File.WriteAllText(cli, "fixture-cli");
        var sessions = new RecordingLocalLmtSessionFactory("一", "二");
        using var provider = new LocalTranslationProvider(new LocalLmtOptions(model, cli, ExpectedModelSha256: LocalLmtArtifactVerifier.Sha256File(model)), sessions);

        var results = provider.TranslateManyAsync(
            [new TranslationRequest("English", "Chinese", "one"), new TranslationRequest("English", "Chinese", "two")],
            CancellationToken.None).GetAwaiter().GetResult();

        Equal(true, results.All(static item => item.IsSuccess));
        Equal("一", results[0].Text);
        Equal("二", results[1].Text);
        Equal(2, sessions.Prompts.Count);
        Equal(false, sessions.Prompts.Any(static prompt => prompt.Contains("\"id\"", StringComparison.Ordinal)));
        Equal(false, sessions.Prompts.Any(static prompt => prompt.Contains("JSON", StringComparison.OrdinalIgnoreCase)));
    }

    private static void SelectsProtocolNonThinkingMode()
    {
        var handler = new RecordingHttpHandler(HttpStatusCode.OK, JsonTranslation("[{\"id\":\"b0\",\"translation\":\"ok\"}]"));
        var provider = new DeepSeekTranslationProvider(new RecordingCredentialVault("safe"), handler);
        _ = provider.TranslateAsync(new TranslationRequest("English", "简体中文", "hello"), "alias", CancellationToken.None).GetAwaiter().GetResult();
        using var json = System.Text.Json.JsonDocument.Parse(handler.Body!);
        Equal(true, json.RootElement.EnumerateObject().Select(static property => property.Name).SequenceEqual([
            "model", "messages", "thinking", "response_format", "temperature", "stream", "stream_options",
        ]));
        Equal("disabled", json.RootElement.GetProperty("thinking").GetProperty("type").GetString()!);
        Equal(1, json.RootElement.GetProperty("thinking").EnumerateObject().Count());
        Equal("json_object", json.RootElement.GetProperty("response_format").GetProperty("type").GetString()!);
    }

    private static void RunsSettingsConnectionThroughProvider()
    {
        var vault = new RecordingCredentialVault("settings-safe");
        var handler = new RecordingHttpHandler(HttpStatusCode.OK, JsonTranslation("[{\"id\":\"b0\",\"translation\":\"ok\"}]"));
        var provider = new DeepSeekTranslationProvider(vault, handler);
        var tester = new SettingsConnectionTester(() => provider);

        var result = tester.TestAsync("alias", DeepSeekModels.Flash, CancellationToken.None).GetAwaiter().GetResult();

        Equal(true, result.IsSuccess);
        Equal(1, vault.GetForUseCalls);
        Equal(0, vault.ListCalls);
        Equal(false, result.Status.Contains("settings-safe", StringComparison.Ordinal));
    }

    private static void VerifiesCandidateDeepSeekCredentialWithoutVaultLookup()
    {
        var vault = new RecordingCredentialVault("stored-value");
        var handler = new RecordingHttpHandler(
            HttpStatusCode.OK,
            JsonTranslation("[{\"id\":\"b0\",\"translation\":\"connection test\"}]"));
        var provider = new DeepSeekTranslationProvider(vault, handler);
        const string candidate = "sk-10430abcdefghijklmnopfb25";

        var result = provider.TestCandidateConnectionAsync(candidate, DeepSeekModels.Flash, CancellationToken.None)
            .GetAwaiter().GetResult();

        Equal(true, result.IsSuccess);
        Equal(0, vault.GetForUseCalls);
        Equal("Bearer", handler.AuthorizationScheme!);
        Equal(candidate.Length, handler.AuthorizationParameterLength);
    }

    private static void ClearsBothCredentialEntryControlsOnlyAfterSave()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var source = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "Settings", "SettingsWindow.xaml.cs"));

        var saveIndex = source.IndexOf("_credentialVault.Save(name, provider, candidate);", StringComparison.Ordinal);
        var clearValueIndex = source.IndexOf("CredentialValueBox.Clear();", StringComparison.Ordinal);
        Equal(true, saveIndex >= 0);
        Equal(true, clearValueIndex > saveIndex);
        Equal(true, source.Contains("_selectedCredentialName = name;", StringComparison.Ordinal));
        Equal(true, source.Contains("CredentialName = CredentialVault.FixedDeepSeekSlot", StringComparison.Ordinal));
    }

    private static void ProtectsPlaceholderTokenIntegrity()
    {
        const string source = "literal [[CLIPRAY_0]] {name} <b>42</b>";
        var translated = TranslateWithTokenTransform(source, static text => "文本：" + text);
        Equal(true, translated.IsSuccess);
        Equal("文本：" + source, translated.Text);

        foreach (var transform in new Func<string, string>[]
        {
            text => new Regex(@"\[\[CLIPRAY_[0-9a-f]{32}_\d+\]\]").Replace(text, string.Empty, 1),
            text => new Regex(@"(\[\[CLIPRAY_[0-9a-f]{32}_\d+\]\])").Replace(text, "$1$1", 1),
            text => new Regex(@"\[\[CLIPRAY_([0-9a-f]{32}_\d+)\]\]").Replace(text, "[[CLIPRAY_MUTATED_$1]]", 1),
        })
        {
            var result = TranslateWithTokenTransform(source, transform);
            Equal(false, result.IsSuccess);
            Equal(string.Empty, result.Text);
        }
    }

    private static TranslationResult TranslateWithTokenTransform(string source, Func<string, string> transform)
    {
        var handler = new RecordingHttpHandler(async (request, cancellationToken) =>
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            using var json = System.Text.Json.JsonDocument.Parse(body);
            var user = json.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!;
            using var blocks = System.Text.Json.JsonDocument.Parse(user);
            var block = blocks.RootElement.GetProperty("b0");
            var content = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["b0"] = transform(block.GetProperty("text").GetString()!),
            });
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonTranslation(content), Encoding.UTF8, "application/json") };
        });
        return new DeepSeekTranslationProvider(new RecordingCredentialVault("safe"), handler)
            .TranslateAsync(new TranslationRequest("English", "简体中文", source), "alias", CancellationToken.None).GetAwaiter().GetResult();
    }

    private static void TimesOutWithSanitizedDiagnostics()
    {
        const string secret = "timeout-secret-never-print";
        var handler = new RecordingHttpHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var result = new DeepSeekTranslationProvider(new RecordingCredentialVault(secret), handler, TimeSpan.FromMilliseconds(20))
            .TranslateAsync(new TranslationRequest("English", "简体中文", "text"), "alias", CancellationToken.None).GetAwaiter().GetResult();
        Equal(false, result.IsSuccess);
        Equal("Translation timed out.", result.Status);
        Equal(false, result.Status.Contains(secret, StringComparison.Ordinal));
    }

    private static void OwnsUiOperations()
    {
        using var owner = new UiOperationOwner();
        var first = owner.Begin();
        var second = owner.Begin();
        Equal(true, first.Token.IsCancellationRequested);
        Equal(false, owner.IsCurrent(first.Version));
        Equal(true, owner.IsCurrent(second.Version));
        owner.CancelPending();
        Equal(true, second.Token.IsCancellationRequested);
        Equal(false, owner.IsCurrent(second.Version));
        var third = owner.Begin();
        Equal(true, owner.IsCurrent(third.Version));
        owner.Close();
        Equal(true, third.Token.IsCancellationRequested);
        Equal(false, owner.IsCurrent(third.Version));
    }

    private static void KeepsShutdownCleanupRunningAfterRecordingStopFailure()
    {
        var continued = false;
        Equal(false, ShutdownSafety.Try(() => throw new IOException("injected stop failure")));
        continued = true;
        Equal(true, continued);

        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var app = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "App.xaml.cs"));
        Equal(true, app.Contains("ShutdownSafety.Try(() => _recordingSession?.StopAsync().GetAwaiter().GetResult())", StringComparison.Ordinal));
    }

    private static void KeepsOverlayTranslationOutOfStandaloneWindow()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var source = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "App.xaml.cs"));
        var start = source.IndexOf("private async void HandleOverlayTranslationRequested", StringComparison.Ordinal);
        var end = source.IndexOf("private async void HandleTextExtractionRequested", start, StringComparison.Ordinal);
        var handler = source[start..end];

        Equal(false, handler.Contains("OpenTranslationWindow", StringComparison.Ordinal));
    }

    private static void KeepsSmallTranslatedSelectionsAtOriginalCoordinates()
    {
        var selection = new PixelRect(400, 300, 120, 60);

        var mapped = OverlayTranslationSequencer.MapToScreen(selection, new PixelRect(12, 8, 40, 20));

        Equal(412d, mapped.X);
        Equal(308d, mapped.Y);
        Equal(40d, mapped.Width);
        Equal(20d, mapped.Height);
    }

    private static void UsesReadableMinimumTranslationFontSize()
    {
        var style = new SourceVisualStyle(
            Color.White,
            6,
            10,
            SourceFontWeight.Normal,
            SourceFontCategory.SansSerif,
            false,
            SourceTextAlignment.Left,
            0,
            VisualStyleConfidence.High);

        var layout = TranslationTextLayoutPolicy.Fit("译", new PixelRect(0, 0, 40, 10), style);

        Equal(true, layout.ShouldRender);
        Equal(true, layout.FontSizePixels > 0);
        Equal(true, layout.FontSizePixels <= 6 / TranslationTextLayoutPolicy.VisibleGlyphRatio + 0.01);
    }

    private static void UsesCjkVisibleGlyphRatio()
    {
        var style = new SourceVisualStyle(
            Color.White, 18, 28, SourceFontWeight.Normal, SourceFontCategory.SansSerif,
            false, SourceTextAlignment.Left, 0, VisualStyleConfidence.High);

        var layout = TranslationTextLayoutPolicy.Fit("机器", new PixelRect(0, 0, 120, 28), style);

        Equal(true, layout.ShouldRender);
        Equal(true, layout.FontSizePixels is >= 24.5 and <= 25.5);
    }

    private static void ShrinksTranslatedTextToFitSourceBounds()
    {
        var style = new SourceVisualStyle(
            Color.White, 14, 18, SourceFontWeight.SemiBold, SourceFontCategory.SansSerif,
            false, SourceTextAlignment.Center, 0, VisualStyleConfidence.High);
        var initial = style.GlyphHeightPixels / TranslationTextLayoutPolicy.VisibleGlyphRatio;

        var layout = TranslationTextLayoutPolicy.Fit(
            "a translated sentence that must wrap",
            new PixelRect(0, 0, 120, 36),
            style);

        Equal(true, layout.ShouldRender);
        Equal(true, layout.FontSizePixels < initial);
        Equal(SourceTextAlignment.Center, layout.Alignment);
        Equal(SourceFontWeight.SemiBold, layout.FontWeight);
    }

    private static void FitsCompactTranslatedMetadata()
    {
        var style = new SourceVisualStyle(
            Color.White, 18, 22, SourceFontWeight.Normal, SourceFontCategory.SansSerif,
            false, SourceTextAlignment.Left, 0, VisualStyleConfidence.High);
        var bounds = new PixelRect(0, 0, 50, 18);

        var ordinary = TranslationTextLayoutPolicy.Fit("大约六小时前", bounds, style);
        var compact = TranslationTextLayoutPolicy.FitCompact("大约六小时前", bounds, style);

        Equal(false, ordinary.ShouldRender);
        Equal(true, compact.ShouldRender);
        Equal(true, compact.FontSizePixels < 18 / 0.9 * 0.55);
    }

    private static void KeepsOriginalWhenTranslationCannotFit()
    {
        var style = new SourceVisualStyle(
            Color.White, 8, 10, SourceFontWeight.Normal, SourceFontCategory.SansSerif,
            false, SourceTextAlignment.Left, 0, VisualStyleConfidence.High);

        var layout = TranslationTextLayoutPolicy.Fit(
            new string('W', 200),
            new PixelRect(0, 0, 18, 8),
            style);

        if (layout.ShouldRender)
        {
            throw new InvalidOperationException($"Unexpected fitted size {layout.FontSizePixels:F2} line {layout.LineHeightPixels:F2}.");
        }
    }

    private static void BuildsOnlySafeChangedInPlaceItems()
    {
        using var source = CreateBitmap(160, 60, Color.Black);
        for (var y = 12; y < 20; y++)
        for (var x = 12; x < 50; x++) source.SetPixel(x, y, Color.White);
        var changed = new OcrTextBlock("hello", new PixelRect(10, 8, 80, 16), []);
        var unchanged = new OcrTextBlock("path", new PixelRect(10, 34, 80, 16), []);
        var plan = new[]
        {
            new TranslationOverlayBlock(changed, "bonjour", TranslationBlockState.Changed, "ok"),
            new TranslationOverlayBlock(unchanged, "path", TranslationBlockState.Unchanged, "ok"),
        };

        var items = InPlaceLayoutService.Build(source, plan);

        Equal(1, items.Count);
        Equal("bonjour", items[0].Text);
        Equal(true, items[0].TextLayout is { ShouldRender: true });
        Equal(true, items[0].SourceStyle is { Confidence: VisualStyleConfidence.High });
    }

    private static void RejectsOverlappingInPlaceReplacements()
    {
        using var source = CreateBitmap(160, 60, Color.Black);
        for (var y = 10; y < 23; y++)
        for (var x = 10; x < 100; x++) source.SetPixel(x, y, Color.White);
        var first = new OcrTextBlock("first", new PixelRect(8, 8, 90, 18), []);
        var second = new OcrTextBlock("second", new PixelRect(20, 10, 90, 18), []);

        var items = InPlaceLayoutService.Build(source,
        [
            new TranslationOverlayBlock(first, "un", TranslationBlockState.Changed, "ok"),
            new TranslationOverlayBlock(second, "deux", TranslationBlockState.Changed, "ok"),
        ]);

        Equal(1, items.Count);
    }

    private static void RendersMeasuredSourceStyleWithoutFixedClamps()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var overlay = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "Overlay", "SelectionOverlayWindow.xaml.cs"));
        var factory = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "Overlay", "TranslationOverlayElementFactory.cs"));

        Equal(true, overlay.Contains("TranslationOverlayElementFactory.CreatePositioned", StringComparison.Ordinal));
        Equal(true, factory.Contains("item.TextLayout", StringComparison.Ordinal));
        Equal(true, factory.Contains("FontSize = layout.FontSizePixels / pixelScaleY", StringComparison.Ordinal));
        Equal(true, factory.Contains("LineHeight = layout.LineHeightPixels / pixelScaleY", StringComparison.Ordinal));
        Equal(true, factory.Contains("RotateTransform(layout.RotationDegrees)", StringComparison.Ordinal));
        Equal(true, factory.Contains("TextAlignment = layout.Alignment", StringComparison.Ordinal));
        Equal(false, factory.Contains("MinimumTranslationFontSize", StringComparison.Ordinal));
        Equal(false, factory.Contains("MaximumTranslationFontSize", StringComparison.Ordinal));
        Equal(false, factory.Contains("border.DesiredSize.Height", StringComparison.Ordinal));
    }

    private static void RendersMaskedRepairWithoutOpaqueRegionBackground()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var overlay = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "Overlay", "SelectionOverlayWindow.xaml.cs"));
        var factory = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "Overlay", "TranslationOverlayElementFactory.cs"));
        var maskedStart = factory.IndexOf("if (item.MaskedPatchPng", StringComparison.Ordinal);
        var maskedEnd = maskedStart < 0 ? -1 : factory.IndexOf("else", maskedStart, StringComparison.Ordinal);
        Equal(true, overlay.Contains("TranslationOverlayElementFactory.CreatePositioned", StringComparison.Ordinal));
        Equal(true, maskedStart >= 0 && maskedEnd > maskedStart);
        var maskedBranch = factory[maskedStart..maskedEnd];

        Equal(true, maskedBranch.Contains("container.Children.Add(new System.Windows.Controls.Image", StringComparison.Ordinal));
        Equal(false, maskedBranch.Contains("container.Background", StringComparison.Ordinal));
        Equal(true, factory.Contains("container.Children.Add(text)", StringComparison.Ordinal));
    }

    private static void PadsSmallOcrCapturesWithoutMovingTranslationOrigin()
    {
        var selection = new PixelRect(100, 100, 40, 20);
        var bounds = new PixelRect(0, 0, 400, 300);

        var capture = SelectionCapturePolicy.ExpandForOcr(selection, bounds);

        Equal(new PixelRect(94, 94, 52, 32), capture);
    }

    private static void QuantizesOcrCaptureBoundsBeforeMapping()
    {
        var selection = new PixelRect(100.4, 80.6, 40.2, 20.2);
        var bounds = new PixelRect(0, 0, 400, 300);

        var capture = SelectionCapturePolicy.CreateOcrCaptureBounds(selection, bounds);

        Equal(new PixelRect(94, 74, 53, 33), capture);
        Equal(new PixelRect(104, 84, 20, 10),
            OverlayTranslationSequencer.MapToScreen(capture, new PixelRect(10, 10, 20, 10)));
    }

    private static void KeepsPaddedOcrContextOutOfTranslation()
    {
        var selection = new PixelRect(100, 100, 100, 50);
        var capture = new PixelRect(94, 94, 112, 62);
        var inside = new OcrWord("inside", new PixelRect(12, 12, 30, 12));
        var crossing = new OcrWord("edge", new PixelRect(1, 16, 8, 12));
        var outside = new OcrWord("outside", new PixelRect(1, 36, 8, 12));
        var nextLine = new OcrWord("next", new PixelRect(12, 50, 30, 12));
        var blocks = new[]
        {
            new OcrTextBlock("outside edge inside next", new PixelRect(1, 12, 41, 50), [outside, crossing, inside, nextLine]),
        };

        var constrained = SelectionCapturePolicy.ConstrainToSelection(blocks, selection, capture);

        Equal(1, constrained.Count);
        Equal("inside", constrained[0].Text);
        Equal(new PixelRect(12, 12, 30, 12), constrained[0].Bounds);
    }

    private static void UsesPaddedCaptureBoundsForOcrAndTranslationMapping()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var source = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "Overlay", "SelectionOverlayWindow.xaml.cs"));
        var translationSource = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "App.xaml.cs"));

        Equal(true, source.Contains("SelectionCapturePolicy.CreateOcrCaptureBounds", StringComparison.Ordinal));
        Equal(true, source.Contains("CaptureBounds", StringComparison.Ordinal));
        Equal(true, translationSource.Contains("capture.CaptureBounds", StringComparison.Ordinal));
    }

    private static void KeepsTranslationToggleInteractiveAndReversible()
    {
        EnsureWpfResources();
        using var bitmap = CreateBitmap(32, 32, Color.Black);
        var overlay = new SelectionOverlayWindow(
            new CapturedFrame(bitmap, new Rectangle(0, 0, 32, 32)),
            new PixelRect(0, 0, 32, 32));

        try
        {
            var toggle = (System.Windows.Controls.Button)overlay.FindName("TranslationToggle");
            var layer = (System.Windows.Controls.Canvas)overlay.FindName("TranslationLayer");
            var isControl = (bool)overlay.GetType()
                .GetMethod("IsControlSource", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(overlay, [toggle])!;
            Equal(true, isControl);

            layer.Visibility = System.Windows.Visibility.Visible;
            toggle.Visibility = System.Windows.Visibility.Visible;
            toggle.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Equal(System.Windows.Visibility.Collapsed, layer.Visibility);
            Equal("显示译文", toggle.Content);

            toggle.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Equal(System.Windows.Visibility.Visible, layer.Visibility);
            Equal("显示原文", toggle.Content);
        }
        finally
        {
            overlay.Close();
        }
    }

    private static void KeepsTranslationItemsStableAcrossToggles()
    {
        var block = new OcrTextBlock("source", new PixelRect(0, 0, 40, 12), []);
        var item = new InPlaceLayoutItem(block, "译文", block.Bounds,
            InPlaceBackgroundMode.FlatSampled, Color.Black, Color.White);
        var initial = new TranslationDisplayState(TranslationDisplayMode.Translated, [item]);

        var original = initial.Toggle();
        var translated = original.Toggle();

        Equal(TranslationDisplayMode.Original, original.Mode);
        Equal("显示译文", original.ToggleLabel);
        Equal(TranslationDisplayMode.Translated, translated.Mode);
        Equal("显示原文", translated.ToggleLabel);
        Equal(true, ReferenceEquals(initial.Items, translated.Items));
        Equal(true, ReferenceEquals(item, translated.Items[0]));
    }

    private static void TranslatedSelectionSavesCurrentlyDisplayedMode()
    {
        var block = new OcrTextBlock("source", new PixelRect(0, 0, 40, 12), []);
        var item = new InPlaceLayoutItem(block, "译文", block.Bounds,
            InPlaceBackgroundMode.FlatSampled, Color.Black, Color.White);
        var state = new TranslationDisplayState(TranslationDisplayMode.Translated, [item]);

        Equal(TranslationSelectionExportMode.Translated, state.ExportMode);
        Equal(TranslationSelectionExportMode.Original, state.Toggle().ExportMode);
        Equal(TranslationSelectionExportMode.Translated, state.Toggle().Toggle().ExportMode);
    }

    private static void TranslatedSelectionRendererExportsOnlySelectedTranslatedPixels()
    {
        using var source = CreateBitmap(30, 20, Color.Blue);
        var selection = new PixelRect(5, 4, 10, 8);
        var insideBlock = new OcrTextBlock("source", new PixelRect(7, 6, 6, 4), []);
        var outsideBlock = new OcrTextBlock("outside", new PixelRect(20, 12, 5, 5), []);
        var inside = new InPlaceLayoutItem(insideBlock, "译", insideBlock.Bounds,
            InPlaceBackgroundMode.FlatSampled, Color.Yellow, Color.Black);
        var outside = new InPlaceLayoutItem(outsideBlock, "外", outsideBlock.Bounds,
            InPlaceBackgroundMode.FlatSampled, Color.Red, Color.White);

        using var output = TranslatedSelectionRenderer.Render(source, selection, [inside, outside]);

        Equal(10, output.Width);
        Equal(8, output.Height);
        Equal(false, Color.Blue.ToArgb() == output.GetPixel(3, 3).ToArgb());
        Equal(Color.Blue.ToArgb(), output.GetPixel(9, 7).ToArgb());
    }

    private static void ConfirmedTranslatedSelectionFollowsDisplayedMode()
    {
        EnsureWpfResources();
        static Bitmap Confirm(bool showOriginal)
        {
            var source = CreateBitmap(240, 180, Color.Blue);
            var selection = new PixelRect(20, 20, 100, 80);
            var overlay = new SelectionOverlayWindow(
                new CapturedFrame(source, new Rectangle(0, 0, source.Width, source.Height)),
                selection);
            var block = new OcrTextBlock("source", new PixelRect(40, 40, 60, 40), []);
            var item = new InPlaceLayoutItem(block, "译", block.Bounds,
                InPlaceBackgroundMode.FlatSampled, Color.Yellow, Color.Black);
            overlay.GetType()
                .GetField("_translationDisplayState", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .SetValue(overlay, new TranslationDisplayState(
                    showOriginal ? TranslationDisplayMode.Original : TranslationDisplayMode.Translated,
                    [item]));
            var edits = new EditLayer(selection);
            edits.AddPenStroke(new PenStroke([new PixelPoint(25, 25)], Color.Red, 3));
            edits.AddMosaicStroke(new MosaicStroke([new PixelPoint(45, 45)], 3, 1));
            overlay.GetType()
                .GetField("_editLayer", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .SetValue(overlay, edits);

            Bitmap? confirmed = null;
            overlay.CaptureConfirmed += (_, bitmap) => confirmed = bitmap;
            overlay.GetType()
                .GetMethod("ConfirmSelection", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(overlay, null);
            return confirmed ?? throw new InvalidOperationException("The selection was not confirmed.");
        }

        using var translated = Confirm(showOriginal: false);
        using var original = Confirm(showOriginal: true);

        Equal(false, Color.Blue.ToArgb() == translated.GetPixel(30, 30).ToArgb());
        Equal(Color.Blue.ToArgb(), original.GetPixel(30, 30).ToArgb());
        Equal(Color.Red.ToArgb(), translated.GetPixel(5, 5).ToArgb());
        Equal(Color.Red.ToArgb(), original.GetPixel(5, 5).ToArgb());
        Equal(Color.Blue.ToArgb(), translated.GetPixel(25, 25).ToArgb());
    }

    private static void DoesNotOpenIndependentFocusWindowForTranslatedSelections()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var source = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "Overlay", "SelectionOverlayWindow.xaml.cs"));

        Equal(false, source.Contains("new FocusTranslationWindow", StringComparison.Ordinal));
        Equal(false, source.Contains("ShowFocusedTranslation", StringComparison.Ordinal));
        Equal(true, source.Contains("OverlayTranslationSequencer.MapToScreen", StringComparison.Ordinal));
    }

    private static void KeepsPpOcrSidecarWarmBetweenRequests()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var runtime = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "Ocr", "PpOcrRuntime.cs"));
        var sidecar = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "Ocr", "ppocr_sidecar.py"));

        Equal(true, runtime.Contains("WriteLineAsync(input)", StringComparison.Ordinal));
        Equal(true, runtime.Contains("ReadLineAsync(cancellationToken)", StringComparison.Ordinal));
        Equal(false, runtime.Contains("new Process", StringComparison.Ordinal) && runtime.Contains("RecognizeAsync", StringComparison.Ordinal) && runtime.Contains("process.WaitForExitAsync", StringComparison.Ordinal));
        Equal(true, runtime.Contains("warmup", StringComparison.Ordinal));
        Equal(true, runtime.Contains("IsReadyResponse", StringComparison.Ordinal));
        Equal(true, runtime.Contains("WriteLineAsync(WarmupRequest)", StringComparison.Ordinal));
        Equal(true, sidecar.Contains("for line in sys.stdin", StringComparison.Ordinal));
        Equal(true, sidecar.Contains("payload.get(\"warmup\")", StringComparison.Ordinal));
        Equal(true, sidecar.Contains("\"ready\": True", StringComparison.Ordinal));
        Equal(true, sidecar.Contains("TextDetection", StringComparison.Ordinal));
        Equal(true, sidecar.Contains("TextRecognition", StringComparison.Ordinal));
        Equal(false, sidecar.Contains("text_recognition_batch_size=1", StringComparison.Ordinal));
        Equal(true, sidecar.Contains("\"enable_mkldnn\": False", StringComparison.Ordinal));
    }

    private static void KeepsExtractionControlsInteractiveOverOverlay()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var source = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "Overlay", "SelectionOverlayWindow.xaml.cs"));

        Equal(true, source.Contains("ReferenceEquals(current, TextExtractionPanel)", StringComparison.Ordinal));
    }

    private static void CopiesExtractionTextThroughClipboardSeam()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var source = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "Overlay", "SelectionOverlayWindow.xaml.cs"));
        var start = source.IndexOf("private void OnCopyExtractedText", StringComparison.Ordinal);
        var end = source.IndexOf("private void OnCloseTextExtraction", start, StringComparison.Ordinal);
        var handler = source[start..end];

        Equal(true, handler.Contains("_clipboardTextSink", StringComparison.Ordinal));
        Equal(true, handler.Contains("catch (ExternalException)", StringComparison.Ordinal));
        Equal(false, handler.Contains("System.Windows.Clipboard.SetText", StringComparison.Ordinal));
    }

    private static void PlacesExtractionResultsBesideSelection()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var source = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "Overlay", "SelectionOverlayWindow.xaml.cs"));
        var policy = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "Overlay", "ExtractionPanelLayout.cs"));

        Equal(true, source.Contains("PositionTextExtractionPanel", StringComparison.Ordinal));
        Equal(true, source.Contains("new PixelRect(selectionDip.X", StringComparison.Ordinal));
        Equal(true, policy.Contains("selection.Right", StringComparison.Ordinal));
        Equal(true, source.Contains("TextExtractionPanel.Margin", StringComparison.Ordinal));
    }

    private static void LeavesEnoughHeightForExtractionPanelChrome()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var source = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "Overlay", "SelectionOverlayWindow.xaml.cs"));
        var policy = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "Overlay", "ExtractionPanelLayout.cs"));

        Equal(true, policy.Contains("MinimumHeight = 168", StringComparison.Ordinal));
        Equal(true, policy.Contains("ChromeHeight = 124", StringComparison.Ordinal));
        Equal(false, source.Contains("lineCount - 1", StringComparison.Ordinal));
        Equal(true, source.Contains("ExtractionTextMeasurement.Measure", StringComparison.Ordinal));
    }

    private static void UsesReadableExtractionPanelTextSize()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var xaml = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "Overlay", "SelectionOverlayWindow.xaml"));
        Equal(true, xaml.Contains("FontSize=\"16\"", StringComparison.Ordinal));
    }

    private static void ExpandsExtractionPanelIntoAvailableSideSpace()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var source = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "Overlay", "SelectionOverlayWindow.xaml.cs"));

        Equal(true, source.Contains("ExtractionPanelLayoutPolicy.Calculate", StringComparison.Ordinal));
        var policy = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "Overlay", "ExtractionPanelLayout.cs"));
        Equal(true, policy.Contains("availableRight", StringComparison.Ordinal));
        Equal(true, policy.Contains("availableLeft", StringComparison.Ordinal));
        Equal(false, policy.Contains("Math.Max(300, maxAvailableWidth)", StringComparison.Ordinal));
    }

    private static void LetsExtractionTextUseAvailableHeightBeforeScrolling()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var xaml = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "Overlay", "SelectionOverlayWindow.xaml"));

        Equal(false, xaml.Contains("MaxHeight=\"440\"", StringComparison.Ordinal));
        Equal(false, xaml.Contains("MaxHeight=\"300\"", StringComparison.Ordinal));
        Equal(true, xaml.Contains("VerticalScrollBarVisibility=\"Auto\"", StringComparison.Ordinal));
    }

    private static void KeepsExtractionPanelWidthReadableOnWideSideSpaces()
    {
        var layout = ExtractionPanelLayoutPolicy.Calculate(
            1920,
            1080,
            new PixelRect(820, 100, 280, 84),
            86);

        Equal(360d, layout.Width);
        Equal(true, layout.IsOnRight);
        Equal(true, layout.X >= 16);
        Equal(true, layout.X + layout.Width <= 1920 - 16);
    }

    private static void ExpandsExtractionPanelForLongOcrLinesBeforeWrapping()
    {
        var layout = ExtractionPanelLayoutPolicy.Calculate(
            1920,
            1080,
            new PixelRect(820, 100, 280, 84),
            86,
            620);

        Equal(656d, layout.Width);
        Equal(true, layout.X >= 16);
        Equal(true, layout.X + layout.Width <= 1920 - 16);
    }

    private static void UsesTheLargerSideWhenSelectionIsNearAnEdge()
    {
        var layout = ExtractionPanelLayoutPolicy.Calculate(
            1920,
            1080,
            new PixelRect(1540, 80, 300, 240),
            86);

        Equal(false, layout.IsOnRight);
        Equal(360d, layout.Width);
        Equal(true, layout.X + layout.Width <= 1540 - 12);
    }

    private static void CapsLongExtractionContentWithoutStretchingTheOverlay()
    {
        var layout = ExtractionPanelLayoutPolicy.Calculate(
            1920,
            1080,
            new PixelRect(820, 200, 280, 84),
            1200);

        Equal(640d, layout.Height);
        Equal(true, layout.Y >= 16);
        Equal(true, layout.Y + layout.Height <= 1080 - 16);
    }

    private static void MeasuresExtractionContentInsteadOfCountingRawLines()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var source = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "Overlay", "SelectionOverlayWindow.xaml.cs"));

        Equal(true, source.Contains("ExtractionTextMeasurement.Measure", StringComparison.Ordinal));
        Equal(false, source.Contains("ExtractedTextBox.Measure", StringComparison.Ordinal));
        Equal(true, source.Contains("contentDesiredHeight", StringComparison.Ordinal));
        Equal(false, source.Contains("lineCount", StringComparison.Ordinal));
    }

    private static void MeasuresWrappedExtractionText()
    {
        var shortText = ExtractionTextMeasurement.Measure("one line", 16, 320, 1);
        var multiline = ExtractionTextMeasurement.Measure("first line\nsecond line\nthird line", 16, 320, 1);
        var longLine = ExtractionTextMeasurement.Measure(new string('W', 80), 16, 180, 1);

        Equal(true, multiline.Height > shortText.Height * 2);
        Equal(true, longLine.Height > shortText.Height);
    }

    private static void GrowsExtractionPanelForFinalContent()
    {
        var loading = ExtractionPanelLayoutPolicy.Calculate(
            1920, 1080, new PixelRect(820, 100, 280, 84), 44, 300);
        var completed = ExtractionPanelLayoutPolicy.Calculate(
            1920, 1080, new PixelRect(820, 100, 280, 84), 70, 520);

        Equal(true, completed.Height > loading.Height);
        Equal(true, completed.Width > loading.Width);
    }

    private static void MeasuresUnwrappedExtractionWidthBeforeCalculatingHeight()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var source = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "Overlay", "SelectionOverlayWindow.xaml.cs"));
        var policy = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "Overlay", "ExtractionPanelLayout.cs"));

        Equal(true, source.Contains("MeasureUnwrappedExtractionTextWidth", StringComparison.Ordinal));
        Equal(true, source.Contains("ExtractionTextMeasurement.Measure", StringComparison.Ordinal));
        Equal(true, source.Contains("100_000", StringComparison.Ordinal));
        Equal(true, source.Contains("contentDesiredWidth", StringComparison.Ordinal));
        Equal(true, policy.Contains("contentDesiredWidth + TextBoxHorizontalMargin", StringComparison.Ordinal));
        Equal(false, policy.Contains("Math.Min(PreferredWidth", StringComparison.Ordinal));
    }

    private static void PlacesToolbarAroundVisibleOcrPanel()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var source = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "Overlay", "SelectionOverlayWindow.xaml.cs"));

        Equal(true, source.Contains("GetToolbarObstacles", StringComparison.Ordinal));
        Equal(true, source.Contains("PlaceToolbar(", StringComparison.Ordinal));
        Equal(true, source.Contains("TextExtractionPanel.Visibility == Visibility.Visible", StringComparison.Ordinal));
    }

    private static void PlacesFloatingTranslationFeedbackOutsideToolbar()
    {
        var toolbar = new PixelRect(90, 17, 568, 52);
        var placement = FloatingOverlayLayout.PlaceAdjacent(
            toolbar,
            panelWidth: 390,
            panelHeight: 36,
            viewportWidth: 1920,
            viewportHeight: 1080,
            gap: 6);

        Equal(true, placement.Top >= toolbar.Bottom + 6);
        Equal(true, toolbar.Bottom <= placement.Top
            || placement.Bottom <= toolbar.Top
            || toolbar.Right <= placement.Left
            || placement.Right <= toolbar.Left);
        Equal(true, placement.Left >= 0 && placement.Right <= 1920);
    }

    private static void KeepsCompletedTranslationFeedbackVisibleOutsideTheTranslatedImage()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var appSource = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "App.xaml.cs"));
        var showTranslation = appSource.IndexOf("overlay.ShowTranslation(", StringComparison.Ordinal);
        var showStatus = appSource.IndexOf("overlay.TryShowTranslationStatus(", showTranslation, StringComparison.Ordinal);
        var toolbar = new PixelRect(90, 17, 568, 52);
        var translatedSelection = new PixelRect(60, 70, 600, 700);
        var placement = FloatingOverlayLayout.PlaceAdjacent(
            toolbar,
            panelWidth: 390,
            panelHeight: 36,
            viewportWidth: 1920,
            viewportHeight: 1080,
            gap: 6,
            forbiddenRegions: [translatedSelection]);

        Equal(true, showTranslation >= 0 && showStatus > showTranslation);
        Equal(false, appSource.Contains("overlay.HideTranslationStatus();", StringComparison.Ordinal));
        Equal(true, placement.Bottom <= translatedSelection.Top
            || translatedSelection.Bottom <= placement.Top
            || placement.Right <= translatedSelection.Left
            || translatedSelection.Right <= placement.Left);
    }

    private static void KeepsFloatingTranslationFeedbackClearOfExpandedToolProperties()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var source = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "Overlay", "SelectionOverlayWindow.xaml.cs"));

        Equal(true, source.Contains("CreateToolFeedbackAnchor", StringComparison.Ordinal));
        Equal(true, source.Contains("ToolProperties.Visibility == Visibility.Visible", StringComparison.Ordinal));
        Equal(true, source.Contains("ToolProperties.DesiredSize", StringComparison.Ordinal));
    }

    private static void ReflowsToolbarAfterExtractionContentChanges()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var source = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "Overlay", "SelectionOverlayWindow.xaml.cs"));
        var methodStart = source.IndexOf("public bool ShowExtractedText", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("public void ShowModernVisualPreview", methodStart, StringComparison.Ordinal);
        Equal(true, methodStart >= 0 && methodEnd > methodStart);
        var method = source[methodStart..methodEnd];
        Equal(true, method.Contains("PositionTextExtractionPanel();", StringComparison.Ordinal));
        Equal(true, method.Contains("UpdateVisuals();", StringComparison.Ordinal));
    }

    private static void SeparatesDeepSeekCompletionStatusFromPrivacyWording()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var appSource = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "App.xaml.cs"));

        Equal("翻译完成 · DeepSeek", TranslationStatusText.DeepSeekCompleted);
        Equal(false, TranslationStatusText.DeepSeekCompleted.Contains("仅", StringComparison.Ordinal));
        Equal(true, TranslationStatusText.DeepSeekPrivacyNotice.Contains("OCR", StringComparison.Ordinal));
        Equal(true, TranslationStatusText.FormatOverlayCompletion(true, 2, 1).Contains("DeepSeek", StringComparison.Ordinal));
        Equal(true, TranslationStatusText.FormatOverlayCompletion(true, 2, 1).Contains("保留原文 1 处", StringComparison.Ordinal));
        Equal(true, appSource.Contains("TranslationStatusText.FormatDeepSeekTooltip(costEstimate)", StringComparison.Ordinal));
        Equal(false, appSource.Contains("overlay.HideTranslationStatus();", StringComparison.Ordinal));
        Equal(false, appSource.Contains("翻译完成；云端仅接收 OCR 文本。", StringComparison.Ordinal));
    }

    private static void ReportsFullTranslationFailureWithOriginalRetention()
    {
        var status = TranslationStatusText.FormatOverlayFailure(
            3,
            "DeepSeek returned an invalid translation block response.");

        Equal(true, status.Contains("翻译未完成", StringComparison.Ordinal));
        Equal(true, status.Contains("保留原文 3 处", StringComparison.Ordinal));
        Equal(false, status.StartsWith("翻译完成", StringComparison.Ordinal));
    }

    private static void ReportsPartialLocalTranslationWithoutClaimingCompletion()
    {
        var status = TranslationStatusText.FormatOverlayCompletion(
            isDeepSeek: false,
            changedCount: 1,
            retainedCount: 2);

        Equal(true, status.StartsWith("翻译部分完成", StringComparison.Ordinal));
        Equal(true, status.Contains("已替换 1 处", StringComparison.Ordinal));
        Equal(true, status.Contains("保留原文 2 处", StringComparison.Ordinal));
    }

    private static void ReportsLocalRetryDiagnosticWithoutPrivateText()
    {
        var method = typeof(TranslationStatusText)
            .GetMethods()
            .SingleOrDefault(static candidate =>
                candidate.Name == "FormatOverlayStatus"
                && candidate.GetParameters() is [{ ParameterType: var first }, ..]
                && first == typeof(bool));
        Equal(true, method is not null);
        if (method is null) return;

        var results = new[]
        {
            new TranslationResult(true, "译文", "Local deterministic translation completed."),
            new TranslationResult(
                false,
                string.Empty,
                "Local LMT returned the source text unchanged.",
                Diagnostic: new TranslationDiagnostic(
                    TranslationDiagnosticKind.SourceUnchanged,
                    TranslationDiagnosticStage.RetryChunk,
                    2)),
        };
        var status = method.Invoke(null, [false, 1, 2, results, null]) as string ?? string.Empty;

        Equal(true, status.StartsWith("翻译部分完成", StringComparison.Ordinal));
        Equal(true, status.Contains("第 2 个分块返回未翻译原文", StringComparison.Ordinal));
        Equal(false, status.Contains("PRIVATE OCR SOURCE", StringComparison.Ordinal));
    }

    private static void StylesTrayMenuWithDarkRenderer()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var source = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "App.xaml.cs"));

        Equal(true, source.Contains("TrayMenuTheme.Apply(menu)", StringComparison.Ordinal));
        Equal(true, File.Exists(Path.Combine(workspace, "src", "ClipRay.App", "TrayMenuTheme.cs")));
    }

    private static void StylesEveryTranslationChooserAndContextMenuForDarkUi()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var windowXaml = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "Translation", "TranslationWindow.xaml"));
        var overlayXaml = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "Overlay", "SelectionOverlayWindow.xaml"));
        var overlaySource = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "Overlay", "SelectionOverlayWindow.xaml.cs"));

        Equal(true, windowXaml.Contains("x:Key=\"DarkComboBoxTemplate\"", StringComparison.Ordinal));
        Equal(true, windowXaml.Contains("x:Name=\"PART_Popup\"", StringComparison.Ordinal));
        Equal(false, windowXaml.Contains("IsEditable=\"True\"", StringComparison.Ordinal));
        Equal(true, overlayXaml.Contains("x:Key=\"DarkContextMenuStyle\"", StringComparison.Ordinal));
        Equal(true, overlayXaml.Contains("x:Key=\"DarkMenuItemStyle\"", StringComparison.Ordinal));
        Equal(true, overlaySource.Contains("FindResource(\"DarkContextMenuStyle\")", StringComparison.Ordinal));
        Equal(true, overlaySource.Contains("FindResource(\"DarkMenuItemStyle\")", StringComparison.Ordinal));
        Equal(true, overlaySource.Contains("PlacementMode.Top", StringComparison.Ordinal));
    }

    private static void KeepsCompactOverlayButtonsReadableWhileHovered()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var xaml = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "Overlay", "SelectionOverlayWindow.xaml"));
        var styleStart = xaml.IndexOf("x:Key=\"MiniButton\"", StringComparison.Ordinal);
        var styleEnd = xaml.IndexOf("</Style>", styleStart, StringComparison.Ordinal);
        Equal(true, styleStart >= 0 && styleEnd > styleStart);
        var style = xaml[styleStart..styleEnd];

        Equal(true, style.Contains("ControlTemplate", StringComparison.Ordinal));
        Equal(true, style.Contains("IsMouseOver", StringComparison.Ordinal));
        Equal(true, style.Contains("IsPressed", StringComparison.Ordinal));
        Equal(true, style.Contains("#303B50", StringComparison.Ordinal));
    }

    private static void ShowsImmediateStandaloneTranslationProgressFeedback()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var xaml = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "Translation", "TranslationWindow.xaml"));
        var source = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "Translation", "TranslationWindow.xaml.cs"));

        Equal(true, xaml.Contains("x:Name=\"TranslateButtonScale\"", StringComparison.Ordinal));
        Equal(true, source.Contains("TranslateButton.Content = T(\"翻译中…\", \"Translating…\")", StringComparison.Ordinal));
        Equal(true, source.Contains("TranslateButton.Content = T(\"开始翻译\", \"Translate\")", StringComparison.Ordinal));
        Equal(true, source.Contains("ProgressStatus.ToolTip", StringComparison.Ordinal));
    }

    private static void UsesRainbowSettingsSuccessAnimation()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var source = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "Settings", "SettingsWindow.xaml.cs"));
        var app = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "App.xaml.cs"));

        Equal(true, source.Contains("CreateStatusRainbowBrush", StringComparison.Ordinal));
        Equal(true, source.Contains("AnimateRainbowSuccess", StringComparison.Ordinal));
        Equal(true, source.Contains("TranslateTransform.XProperty", StringComparison.Ordinal));
        Equal(true, source.Contains("new TranslateTransform(0.65, 0)", StringComparison.Ordinal));
        Equal(true, source.Contains("new DoubleAnimation(0.65, -0.65", StringComparison.Ordinal));
        Equal(true, source.Contains("RepeatBehavior = RepeatBehavior.Forever", StringComparison.Ordinal));
        Equal(true, source.Contains("AutoReverse = true", StringComparison.Ordinal));
        Equal(true, app.Contains("ShowSaveSuccessPreview();", StringComparison.Ordinal));
    }

    private static void SizesSettingsStatusAndRemovesDottedFocus()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var xaml = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "Settings", "SettingsWindow.xaml"));
        var statusStart = xaml.IndexOf("x:Name=\"StatusPanel\"", StringComparison.Ordinal);
        var statusEnd = xaml.IndexOf('>', statusStart);
        Equal(true, statusStart >= 0 && statusEnd > statusStart);
        var status = xaml[statusStart..statusEnd];
        Equal(true, status.Contains("HorizontalAlignment=\"Left\"", StringComparison.Ordinal));
        Equal(true, status.Contains("MaxWidth=", StringComparison.Ordinal));
        Equal(true, xaml.Contains("<Setter Property=\"FocusVisualStyle\" Value=\"{x:Null}\"", StringComparison.Ordinal));
        Equal(true, xaml.Contains("Property=\"IsKeyboardFocused\"", StringComparison.Ordinal));
    }

    private static void DefinesInstallerUxFolderPickerAndStartupTask()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var installer = File.ReadAllText(Path.Combine(workspace, "installer", "ClipRay.iss"));
        var language = File.ReadAllText(Path.Combine(workspace, "installer", "Languages", "ChineseSimplified.isl"));
        // The public installer omits artwork whose redistribution permission is unknown.
        Equal(false, installer.Contains("install-meme.bmp", StringComparison.Ordinal));
        Equal(false, installer.Contains("install-meme.jpg", StringComparison.Ordinal));
        Equal(true, installer.Contains("Source: \"{#StagingDir}\\*\"; DestDir: \"{app}\\app\"; Excludes: \"licenses\\*\"", StringComparison.Ordinal));
        Equal(true, installer.Contains("Source: \"{#StagingDir}\\licenses\\*\"; DestDir: \"{app}\\licenses\"", StringComparison.Ordinal));
        Equal(true, installer.Contains("Name: \"{app}\\Outputs\\LongCaptures\"", StringComparison.Ordinal));
        Equal(true, installer.Contains("Filename: \"{app}\\app\\{#AppExeName}\"", StringComparison.Ordinal));
        Equal(true, installer.Contains("BrowseForFolder(", StringComparison.Ordinal));
        Equal(true, installer.Contains("SelectedPath := '';", StringComparison.Ordinal));
        Equal(true, installer.Contains("BrowseForFolder(CustomMessage('ChooseInstallDirectory'), SelectedPath, True)", StringComparison.Ordinal));
        Equal(true, installer.Contains("chinesesimp.ChooseInstallDirectory=选择安装目录", StringComparison.Ordinal));
        Equal(true, installer.Contains("en.ChooseInstallDirectory=Choose installation folder", StringComparison.Ordinal));
        Equal(false, installer.Contains("ClipRay.FolderPicker.exe", StringComparison.Ordinal));
        Equal(false, installer.Contains("FolderPickerDir", StringComparison.Ordinal));
        Equal(false, installer.Contains("WizardForm.DirBrowseButton.OnClick(WizardForm.DirBrowseButton)", StringComparison.Ordinal));
        Equal(true, installer.Contains("DeveloperLabel.Caption := CustomMessage('DeveloperCaption');", StringComparison.Ordinal));
        Equal(true, installer.Contains("chinesesimp.DeveloperCaption=ClipRay 截光 · 开发者：落花 φ(>ω<*)", StringComparison.Ordinal));
        Equal(true, installer.Contains("en.DeveloperCaption=ClipRay · Developer: 落花 φ(>ω<*)", StringComparison.Ordinal));
        Equal(true, installer.Contains("DeveloperLabel.Parent := WizardForm;", StringComparison.Ordinal));
        Equal(true, installer.Contains("DeveloperLabel.Font.Size := 17", StringComparison.Ordinal));
        Equal(true, installer.Contains("DeveloperLabel.Font.Style := [fsBold]", StringComparison.Ordinal));
        Equal(true, installer.Contains("DeveloperLabel.Left := 68;", StringComparison.Ordinal));
        Equal(true, installer.Contains("DeveloperLabel.Top := 70;", StringComparison.Ordinal));
        Equal(true, installer.Contains("LicenseDescriptionLabel := TNewStaticText(WizardForm.FindComponent('LicenseLabel3'));", StringComparison.Ordinal));
        Equal(true, installer.Contains("LicenseDescriptionLabel.Visible := False;", StringComparison.Ordinal));
        Equal(false, installer.Contains("WizardForm.LicenseMemo.Top :=", StringComparison.Ordinal));
        Equal(false, installer.Contains("WizardForm.LicenseMemo.Height :=", StringComparison.Ordinal));
        Equal(true, installer.Contains("DeveloperLabel.Visible := CurPageID = wpLicense;", StringComparison.Ordinal));
        Equal(true, installer.Contains("RecycleOutputsCheck", StringComparison.Ordinal));
        Equal(true, installer.Contains("将 ClipRay 默认 Outputs 文件夹整体移到回收站", StringComparison.Ordinal));
        Equal(false, installer.Contains("MB_YESNO", StringComparison.Ordinal));
        Equal(true, installer.Contains("ExistingInstallLocation", StringComparison.Ordinal));
        Equal(true, installer.Contains("InitializeSetup", StringComparison.Ordinal));
        Equal(true, installer.Contains("已安装正式版 ClipRay", StringComparison.Ordinal));
        Equal(true, installer.Contains("CreateMutexW@kernel32.dll", StringComparison.Ordinal));
        Equal(true, installer.Contains("ErrorAlreadyExists = 183", StringComparison.Ordinal));
        Equal(true, installer.Contains("IsAnotherClipRaySetupRunning", StringComparison.Ordinal));
        Equal(true, installer.Contains("if IsAnotherClipRaySetupRunning then", StringComparison.Ordinal));
        Equal(false, installer.Contains("SetupMutex=", StringComparison.Ordinal));
        Equal(true, installer.Contains("Name: \"startup\"", StringComparison.Ordinal));
        Equal(true, installer.Contains("Root: HKCU; Subkey: \"Software\\Microsoft\\Windows\\CurrentVersion\\Run\"", StringComparison.Ordinal));
        Equal(true, installer.Contains("ValueName: \"ClipRay\"", StringComparison.Ordinal));
        Equal(true, installer.Contains("Tasks: startup", StringComparison.Ordinal));
        Equal(false, installer.Contains("Name: \"{userstartup}\\ClipRay 截光\"", StringComparison.Ordinal));
        Equal(true, language.Contains("LanguageName=简体中文", StringComparison.Ordinal));
        Equal(false, language.Contains("LicenseLabel3=ClipRay 截光", StringComparison.Ordinal));
    }

    private static void TerminatesClipRayBeforeUninstallCleanupAndClarifiesTheUninstallUi()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var installer = File.ReadAllText(Path.Combine(workspace, "installer", "ClipRay.iss"));

        Equal(true, installer.Contains("UninstallDisplayName=ClipRay 截光", StringComparison.Ordinal));
        Equal(false, installer.Contains("{code:GetRecycleArgument}", StringComparison.Ordinal));
        Equal(true, installer.Contains("procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);", StringComparison.Ordinal));
        Equal(true, installer.Contains("if CurUninstallStep = usUninstall then", StringComparison.Ordinal));
        Equal(true, installer.Contains("CleanupArguments := '--uninstall-cleanup';", StringComparison.Ordinal));
        Equal(true, installer.Contains("CleanupArguments := CleanupArguments + ' --recycle-outputs';", StringComparison.Ordinal));
        Equal(true, installer.Contains("ExpandConstant('{sys}\\taskkill.exe')", StringComparison.Ordinal));
        Equal(true, installer.Contains("ExpandConstant('{app}\\app\\{#AppExeName}')", StringComparison.Ordinal));
        Equal(true, installer.Contains("CleanupExitCode <> 0", StringComparison.Ordinal));
        Equal(true, installer.Contains("Abort;", StringComparison.Ordinal));
        Equal(true, installer.Contains("卸载开始时会先关闭正在运行的 ClipRay。", StringComparison.Ordinal));
        Equal(true, installer.Contains("点击“是”卸载 ClipRay；点击“否”取消。", StringComparison.Ordinal));
        Equal(true, installer.Contains("将 ClipRay 默认 Outputs 文件夹整体移到回收站", StringComparison.Ordinal));
        Equal(true, File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "Infrastructure", "UninstallCleanupService.cs"))
            .Contains("FileSystem.DeleteDirectory", StringComparison.Ordinal));
    }

    private static void BuildsVersionGatedRecordingDiagnosticPatchWithoutBundledModels()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var installer = File.ReadAllText(Path.Combine(workspace, "installer", "ClipRay.RecordingDiagnosticPatch.iss"));
        var buildScript = File.ReadAllText(Path.Combine(workspace, "scripts", "Build-RecordingDiagnosticPatch.ps1"));

        Equal(true, installer.Contains("#define RequiredAppVersion \"1.0.2\"", StringComparison.Ordinal));
        Equal(true, installer.Contains("CreateUninstallRegKey=no", StringComparison.Ordinal));
        Equal(true, installer.Contains("Uninstallable=no", StringComparison.Ordinal));
        Equal(true, installer.Contains("UsePreviousAppDir=no", StringComparison.Ordinal));
        Equal(true, installer.Contains("ExistingInstallLocation", StringComparison.Ordinal));
        Equal(true, installer.Contains("DisplayVersion", StringComparison.Ordinal));
        Equal(true, installer.Contains("ClipRay.App.dll", StringComparison.Ordinal));
        Equal(true, installer.Contains("录屏诊断补丁只适用于已安装的 ClipRay 1.0.2", StringComparison.Ordinal));
        Equal(false, installer.Contains("models", StringComparison.OrdinalIgnoreCase));
        Equal(false, installer.Contains("llama", StringComparison.OrdinalIgnoreCase));
        Equal(true, buildScript.Contains("ClipRay.App.dll", StringComparison.Ordinal));
        Equal(true, buildScript.Contains("installer-staging-recording-patch-", StringComparison.Ordinal));
        Equal(true, buildScript.Contains("finally", StringComparison.Ordinal));
        Equal(false, buildScript.Contains("artifacts\\models", StringComparison.OrdinalIgnoreCase));
        Equal(false, buildScript.Contains("artifacts\\tools", StringComparison.OrdinalIgnoreCase));
    }

    private static void ResolvesOnlyDefaultOutputsForUninstallRecycling()
    {
        using var workspace = TemporaryWorkspace.Create();
        var installRoot = Path.Combine(workspace.Root, "ClipRay");
        var customRoot = Path.Combine(workspace.Root, "shared-custom-output");
        var paths = new ApplicationPathSet(
            installRoot,
            Path.Combine(workspace.Root, "data"),
            Path.Combine(workspace.Root, "data", "tmp"),
            Path.Combine(installRoot, "app", "models", "local-lmt"),
            "release");

        var target = UninstallCleanupService.ResolveDefaultOutputsDirectory(paths);

        Equal(Path.Combine(installRoot, "Outputs"), target);
        Equal(false, string.Equals(target, customRoot, StringComparison.OrdinalIgnoreCase));
    }

    private static void PlacesStartupSettingInLaunchAndScheduleCard()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var xaml = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "Settings", "SettingsWindow.xaml"));
        var launch = xaml.IndexOf("<TextBlock Style=\"{StaticResource SectionTitle}\" settings:SettingsText.Chinese=\"启动与计划\"", StringComparison.Ordinal);
        var startup = xaml.IndexOf("x:Name=\"StartupBox\"", StringComparison.Ordinal);
        var hotkeys = xaml.IndexOf("<TextBlock Style=\"{StaticResource SectionTitle}\" settings:SettingsText.Chinese=\"快捷键\"", startup, StringComparison.Ordinal);

        Equal(true, launch >= 0);
        Equal(true, startup > launch);
        Equal(true, hotkeys > startup);
    }

    private static void ShowsAccumulatedDeepSeekEstimateInSettings()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var xaml = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "Settings", "SettingsWindow.xaml"));
        var source = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "Settings", "SettingsWindow.xaml.cs"));
        var app = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "App.xaml.cs"));

        Equal(true, xaml.Contains("x:Name=\"DeepSeekCostSummaryText\"", StringComparison.Ordinal));
        Equal(true, xaml.Contains("x:Name=\"DeepSeekCostPanel\"", StringComparison.Ordinal));
        Equal(true, xaml.Contains("Foreground=\"#D9C5FF\"", StringComparison.Ordinal));
        Equal(true, xaml.Contains("累计预估费用", StringComparison.Ordinal));
        Equal(true, xaml.Contains("x:Name=\"ClearDeepSeekCostButton\"", StringComparison.Ordinal));
        Equal(true, source.Contains("DeepSeekCostSummaryFormatter.Format", StringComparison.Ordinal));
        Equal(true, source.Contains("ResetDeepSeekCost_Click", StringComparison.Ordinal));
        Equal(true, source.Contains("_resetDeepSeekCostSummary", StringComparison.Ordinal));
        Equal(true, source.Contains("_loadDeepSeekCostSummary", StringComparison.Ordinal));
        Equal(true, source.Contains("RefreshRuntimeData", StringComparison.Ordinal));
        Equal(true, source.Contains("DeepSeekCostPanel.Visibility = deepSeek ? Visibility.Visible : Visibility.Collapsed", StringComparison.Ordinal));
        Equal(true, app.Contains("deepseek-cost-summary.json", StringComparison.Ordinal));
        Equal(true, app.Contains("_deepSeekCostLedger?.Record", StringComparison.Ordinal));
        Equal(true, app.Contains("() => _deepSeekCostLedger!.Load()", StringComparison.Ordinal));
        Equal(true, app.Contains("_deepSeekCostLedger!.Reset(DateTimeOffset.UtcNow)", StringComparison.Ordinal));
    }

    private static void DocumentsTheCurrentRecordingAndEditShortcuts()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var readme = File.ReadAllText(Path.Combine(workspace, "README.md"));
        var releaseNotes = File.ReadAllText(Path.Combine(workspace, "docs", "release", "RELEASE-NOTES.zh-CN.md"));
        var buildGuide = File.ReadAllText(Path.Combine(workspace, "docs", "BUILD.zh-CN.md"));

        Equal(true, readme.Contains("开始录屏 | `Ctrl+Win+Z`", StringComparison.Ordinal));
        Equal(true, readme.Contains("`Ctrl+Z` 撤回", StringComparison.Ordinal));
        Equal(true, readme.Contains("`Ctrl+Shift+Z` 重做", StringComparison.Ordinal));
        Equal(true, releaseNotes.Contains("Ctrl+Win+Z", StringComparison.Ordinal));
        Equal(true, buildGuide.Contains("Ctrl+Win+Z", StringComparison.Ordinal));
        var settingsSource = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "Settings", "SettingsWindow.xaml.cs"));
        Equal(true, settingsSource.Contains("GlobalHotkey.Parse(\"Ctrl+Win+Z\")", StringComparison.Ordinal));
    }

    private static void OffersOnlyConcreteTranslationRoutesAndSelectsConfiguredDefault()
    {
        Equal(false, TranslationRouteCatalog.Options.Any(static option => option.Kind == TranslationRouteKind.FollowSettings));
        Equal(3, TranslationRouteCatalog.Options.Count);
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var app = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "App.xaml.cs"));
        var overlay = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "Overlay", "SelectionOverlayWindow.xaml.cs"));
        Equal(true, app.Contains("ResolveConfiguredTranslationRoute()", StringComparison.Ordinal));
        Equal(true, Regex.IsMatch(app,
            @"new TranslationWindow\s*\(\s*TranslateUsingRouteAsync\s*,\s*ResolveConfiguredTranslationRoute\(\)\s*,\s*_settings!\.InterfaceLanguage\s*,",
            RegexOptions.CultureInvariant));
        Equal(true, overlay.Contains("ResolveConfiguredRoute(_interactionSettings)", StringComparison.Ordinal));
    }

    private static void LocalizesProviderDiagnosticsBeforeDisplay()
    {
        var method = typeof(TranslationStatusText).GetMethod(
            "LocalizeProviderStatus",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        Equal(true, method is not null);
        if (method is null) return;

        Equal("翻译完成。", method.Invoke(null, ["Translation completed."]) as string ?? string.Empty);
        Equal("本地模型翻译失败；未显示任何私密文本。", method.Invoke(null, ["Local LMT failed without exposing private text."]) as string ?? string.Empty);
        Equal("翻译请求超时。", method.Invoke(null, ["Translation timed out."]) as string ?? string.Empty);
    }

    private static void KeepsClarifiedTrayMenuAtDefaultScale()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var appSource = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "App.xaml.cs"));
        var themeSource = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "TrayMenuTheme.cs"));

        Equal(false, themeSource.Contains("1.5f", StringComparison.Ordinal));
        Equal(false, themeSource.Contains("menu.Font", StringComparison.Ordinal));
        Equal(false, themeSource.Contains("item.Padding", StringComparison.Ordinal));
        Equal(true, appSource.Contains("\"全屏截图（含任务栏）\"", StringComparison.Ordinal));
        Equal(true, appSource.Contains("\"全屏截图（不含任务栏）\"", StringComparison.Ordinal));
        Equal(true, appSource.Contains("\"截取全部屏幕\"", StringComparison.Ordinal));
        Equal(false, appSource.Contains("（静默）", StringComparison.Ordinal));
        Equal(true, appSource.Contains("\"设置\"", StringComparison.Ordinal));
        Equal(true, appSource.Contains("\"翻译\"", StringComparison.Ordinal));
        Equal(false, appSource.Contains("设置...", StringComparison.Ordinal));
        Equal(false, appSource.Contains("翻译...", StringComparison.Ordinal));
    }

    private static void FreezesSelectionAndAcceptsOnlyLatest()
    {
        var sequencer = new OverlayTranslationSequencer();
        var original = new PixelRect(100, 200, 300, 100);
        var first = sequencer.Begin(original);
        var second = sequencer.Begin(new PixelRect(500, 600, 300, 100));
        Equal(original, first.Selection);
        Equal(false, sequencer.IsLatest(first.Version));
        Equal(true, sequencer.IsLatest(second.Version));
        Equal(new PixelRect(110, 205, 20, 10), OverlayTranslationSequencer.MapToScreen(first.Selection, new PixelRect(10, 5, 20, 10)));
    }

    private static void DisposesUnownedTranslationCaptures()
    {
        var bitmap = CreateBitmap(2, 2, Color.Red);
        var capture = new TranslationCapture(1, bitmap, new PixelRect(0, 0, 2, 2));
        TranslationCaptureDispatcher.Dispatch(null, new object(), capture);
        Throws<ArgumentException>(() => _ = bitmap.GetPixel(0, 0));
    }

    private static void PreservesOriginalSelectionBesidePaddedCapture()
    {
        using var bitmap = CreateBitmap(10, 10, Color.Red);
        var original = new PixelRect(3, 3, 4, 4);
        var padded = new PixelRect(1, 1, 8, 8);
        using var capture = new TranslationCapture(1, bitmap, original, padded);

        Equal(original, capture.Selection);
        Equal(padded, capture.CaptureBounds);
    }

    private static void SamplesNearbyRingAndContrastsBacking()
    {
        using var source = CreateBitmap(40, 40, Color.Teal);
        for (var y = 10; y < 30; y++)
        for (var x = 10; x < 30; x++) source.SetPixel(x, y, Color.Black);
        var block = new OcrTextBlock("glyph", new PixelRect(10, 10, 20, 20), []);
        var flat = InPlaceLayoutService.Build(source, [block], ["译"])[0];
        Equal(InPlaceBackgroundMode.FlatSampled, flat.BackgroundMode);
        Equal(true, Math.Abs(flat.BackgroundColor.G - Color.Teal.G) < 8);

        using var complex = new Bitmap(40, 40);
        for (var y = 0; y < 40; y++)
        for (var x = 0; x < 40; x++) complex.SetPixel(x, y, (x + y) % 2 == 0 ? Color.White : Color.Gold);
        var item = InPlaceLayoutService.Build(complex, [block], ["译"])[0];
        var actualBacking = InPlaceLayoutService.Composite(item.BackgroundColor, Color.White);
        Equal(true, InPlaceLayoutService.ContrastRatio(item.ForegroundColor, actualBacking) >= 4.5);
    }

    private static void UsesDominantNearbyBackground()
    {
        using var source = CreateBitmap(48, 36, Color.FromArgb(8, 10, 12));
        var bounds = new PixelRect(12, 9, 24, 18);
        for (var x = 9; x < 39; x++)
        {
            source.SetPixel(x, 6, Color.White);
            source.SetPixel(x, 7, Color.White);
        }
        for (var y = 12; y < 24; y++)
        for (var x = 16; x < 32; x += 4) source.SetPixel(x, y, Color.White);

        var item = InPlaceLayoutService.Build(
            source,
            [new OcrTextBlock("source", bounds, [])],
            ["译"])[0];

        Equal(InPlaceBackgroundMode.FlatSampled, item.BackgroundMode);
        Equal(true, item.BackgroundColor.R < 20);
        Equal(true, item.BackgroundColor.G < 20);
        Equal(true, item.BackgroundColor.B < 20);
    }

    private static void KeepsBadgesOutsideTextReplacementMask()
    {
        using var source = CreateBitmap(90, 36, Color.Black);
        for (var y = 12; y < 21; y++)
        {
            for (var x = 8; x < 28; x += 3) source.SetPixel(x, y, Color.White);
            for (var x = 58; x < 80; x += 3) source.SetPixel(x, y, Color.White);
        }
        for (var y = 10; y < 23; y++)
        for (var x = 38; x < 50; x++) source.SetPixel(x, y, Color.Gold);
        var line = new OcrTextBlock(
            "OpenAI account",
            new PixelRect(6, 8, 76, 18),
            [
                new OcrWord("OpenAI", new PixelRect(6, 8, 24, 18)),
                new OcrWord("account", new PixelRect(56, 8, 26, 18)),
            ]);
        var region = new VisualTextRegion(
            "r0", line.Text, [line], line.Bounds,
            VisualTextRole.Control, VisualStructureConfidence.High);

        using var mask = TextReplacementMask.Build(source, region);

        Equal(VisualSafetyConfidence.High, mask.Confidence);
        Equal(0, mask.Mask.GetPixel(42, 16).A);
        Equal(true, mask.Mask.GetPixel(8, 16).A > 0);
        Equal(true, mask.Mask.GetPixel(60, 16).A > 0);
    }

    private static void RepairsGradientsOnlyInsideTextMask()
    {
        using var source = new Bitmap(80, 30, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        for (var y = 0; y < source.Height; y++)
        for (var x = 0; x < source.Width; x++)
        {
            var value = 20 + x * 2;
            source.SetPixel(x, y, Color.FromArgb(255, value, value, value));
        }
        for (var y = 10; y < 19; y++)
        for (var x = 18; x < 60; x += 3) source.SetPixel(x, y, Color.White);
        var word = new OcrWord("gradient", new PixelRect(16, 8, 46, 14));
        var line = new OcrTextBlock(word.Text, word.Bounds, [word]);
        var region = new VisualTextRegion(
            "r0", line.Text, [line], line.Bounds,
            VisualTextRole.Body, VisualStructureConfidence.High);

        using var mask = TextReplacementMask.Build(source, region);
        var repair = MaskedBackgroundRepair.Repair(source, mask);
        Equal(VisualSafetyConfidence.High, repair.Confidence);
        Equal(InPlaceBackgroundMode.ComplexBacking, repair.Mode);
        using var patch = new Bitmap(new MemoryStream(repair.PatchPng!));
        Equal(0, patch.GetPixel(0, 0).A);
        Equal(true, patch.GetPixel(2, 6).A > 0);
        Equal(true, patch.GetPixel(40, 6).R > patch.GetPixel(2, 6).R + 40);
    }

    private static void RejectsUnsafePhotographicBacking()
    {
        using var source = new Bitmap(72, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var colors = new[] { Color.Red, Color.Lime, Color.Blue, Color.Yellow, Color.Magenta, Color.Cyan };
        for (var y = 0; y < source.Height; y++)
        for (var x = 0; x < source.Width; x++) source.SetPixel(x, y, colors[(x * 7 + y * 11) % colors.Length]);
        var word = new OcrWord("photo", new PixelRect(12, 8, 46, 16));
        var line = new OcrTextBlock(word.Text, word.Bounds, [word]);
        var region = new VisualTextRegion(
            "r0", line.Text, [line], line.Bounds,
            VisualTextRole.Body, VisualStructureConfidence.High);

        using var mask = TextReplacementMask.Build(source, region);
        var repair = MaskedBackgroundRepair.Repair(source, mask);

        Equal(VisualSafetyConfidence.Low, repair.Confidence);
        Equal(true, repair.PatchPng is null);
    }

    private static void CarriesSourceGlyphColorIntoInPlaceTranslation()
    {
        var backing = Color.FromArgb(255, 18, 24, 36);
        var glyph = Color.FromArgb(255, 244, 186, 48);
        using var source = CreateBitmap(64, 32, backing);
        using (var graphics = Graphics.FromImage(source))
        using (var brush = new SolidBrush(glyph))
        {
            graphics.FillRectangle(brush, 14, 12, 4, 8);
            graphics.FillRectangle(brush, 24, 12, 18, 3);
            graphics.FillRectangle(brush, 24, 17, 14, 3);
        }

        var item = InPlaceLayoutService.Build(
            source,
            [new OcrTextBlock("colored", new PixelRect(10, 8, 44, 16), [])],
            ["翻译"])[0];

        Equal(true, item.ForegroundColor.R > 220);
        Equal(true, item.ForegroundColor.G is > 155 and < 215);
        Equal(true, item.ForegroundColor.B is > 20 and < 85);
        NotEqual(Color.White.ToArgb(), item.ForegroundColor.ToArgb());
        NotEqual(Color.Black.ToArgb(), item.ForegroundColor.ToArgb());
    }

    private static void MeasuresSourceGlyphColorAndPixelHeight()
    {
        var backing = Color.FromArgb(18, 24, 36);
        var glyph = Color.FromArgb(244, 186, 48);
        using var source = CreateBitmap(80, 40, backing);
        for (var y = 14; y < 20; y++)
        for (var x = 16; x < 42; x++) source.SetPixel(x, y, glyph);
        var block = new OcrTextBlock("source", new PixelRect(10, 8, 50, 20), []);

        var style = SourceVisualStyleEstimator.Estimate(source, block, [block], backing);

        Equal(6d, style.GlyphHeightPixels);
        Equal(true, style.ForegroundColor.R > 220);
        Equal(true, style.ForegroundColor.G is > 155 and < 215);
        Equal(VisualStyleConfidence.High, style.Confidence);
    }

    private static void EstimatesSourceWeightFromInkCoverage()
    {
        using var normal = CreateBitmap(60, 24, Color.Black);
        using var bold = CreateBitmap(60, 24, Color.Black);
        for (var x = 12; x < 48; x += 6)
        for (var y = 7; y < 18; y++)
        {
            normal.SetPixel(x, y, Color.White);
            for (var stroke = 0; stroke < 4; stroke++) bold.SetPixel(x + stroke, y, Color.White);
        }
        var block = new OcrTextBlock("weight", new PixelRect(10, 5, 40, 15), []);

        var normalStyle = SourceVisualStyleEstimator.Estimate(normal, block, [block], Color.Black);
        var boldStyle = SourceVisualStyleEstimator.Estimate(bold, block, [block], Color.Black);

        Equal(SourceFontWeight.Normal, normalStyle.FontWeight);
        Equal(SourceFontWeight.Bold, boldStyle.FontWeight);
    }

    private static void InfersParagraphAlignmentFromNeighboringLines()
    {
        using var source = CreateBitmap(200, 100, Color.Black);
        var left = new[]
        {
            new OcrTextBlock("a", new PixelRect(10, 5, 120, 12), []),
            new OcrTextBlock("b", new PixelRect(10, 25, 90, 12), []),
            new OcrTextBlock("c", new PixelRect(10, 45, 70, 12), []),
        };
        var centered = new[]
        {
            new OcrTextBlock("a", new PixelRect(20, 5, 120, 12), []),
            new OcrTextBlock("b", new PixelRect(35, 25, 90, 12), []),
            new OcrTextBlock("c", new PixelRect(45, 45, 70, 12), []),
        };
        var right = new[]
        {
            new OcrTextBlock("a", new PixelRect(20, 5, 120, 12), []),
            new OcrTextBlock("b", new PixelRect(50, 25, 90, 12), []),
            new OcrTextBlock("c", new PixelRect(70, 45, 70, 12), []),
        };

        Equal(SourceTextAlignment.Left, SourceVisualStyleEstimator.Estimate(source, left[1], left, Color.Black).Alignment);
        Equal(SourceTextAlignment.Center, SourceVisualStyleEstimator.Estimate(source, centered[1], centered, Color.Black).Alignment);
        Equal(SourceTextAlignment.Right, SourceVisualStyleEstimator.Estimate(source, right[1], right, Color.Black).Alignment);
    }

    private static void EstimatesSourceTextRotation()
    {
        using var source = CreateBitmap(120, 60, Color.Black);
        for (var x = 14; x < 100; x++)
        {
            var y = 18 + (int)Math.Round((x - 14) * 0.2);
            source.SetPixel(x, y, Color.White);
            source.SetPixel(x, y + 1, Color.White);
        }
        var block = new OcrTextBlock("rotated", new PixelRect(10, 10, 96, 32), []);

        var style = SourceVisualStyleEstimator.Estimate(source, block, [block], Color.Black);

        Equal(true, style.RotationDegrees is > 8 and < 15);
        Equal(VisualStyleConfidence.High, style.Confidence);
    }

    private static void TreatsUnstableImageTextureAsLowConfidence()
    {
        using var source = CreateBitmap(80, 40, Color.Black);
        var colors = new[] { Color.Red, Color.Lime, Color.Blue, Color.Yellow, Color.Magenta, Color.Cyan };
        for (var y = 8; y < 30; y++)
        for (var x = 10; x < 70; x++) source.SetPixel(x, y, colors[(x * 3 + y * 5) % colors.Length]);
        var block = new OcrTextBlock("photo", new PixelRect(10, 8, 60, 22), []);

        var style = SourceVisualStyleEstimator.Estimate(source, block, [block], Color.Black);

        Equal(VisualStyleConfidence.Low, style.Confidence);
    }

    private static void DetectsConsistentSourceItalicShear()
    {
        using var source = CreateBitmap(90, 36, Color.Black);
        foreach (var baseX in new[] { 14, 30, 46, 62 })
        for (var y = 7; y < 25; y++)
        {
            var x = baseX + (24 - y) / 4;
            source.SetPixel(x, y, Color.White);
            source.SetPixel(x + 1, y, Color.White);
        }
        var block = new OcrTextBlock("italic", new PixelRect(10, 5, 62, 22), []);

        var style = SourceVisualStyleEstimator.Estimate(source, block, [block], Color.Black);

        Equal(true, style.IsItalic);
        Equal(true, Math.Abs(style.RotationDegrees) < 2);
    }

    private static void ChoosesExplicitOcrLanguageCandidates()
    {
        var available = new[] { "en-US", "zh-Hans-CN", "ja-JP" };
        Equal("ja-JP", OcrLanguageSelector.Select(available, "ja-JP", "en-US"));
        Equal("zh-Hans-CN", OcrLanguageSelector.Select(available, "zh", "en-US"));
        Equal("en-US", OcrLanguageSelector.Select(available, null, "en-US"));
    }

    private static void OrdersInstalledOcrLanguagesForAutoDetection()
    {
        var available = new[] { "en-US", "zh-Hans-CN" };
        Equal("zh-Hans-CN,en-US", string.Join(',', OcrLanguageSelector.OrderCandidates(available, "zh-Hans-CN", "en-US")));
        Equal("en-US,zh-Hans-CN", string.Join(',', OcrLanguageSelector.OrderCandidates(available, null, "en-US")));
    }

    private static void RejectsEmptyOcrBeforeTranslation()
    {
        var empty = new OcrRecognitionResult(true, [], "Local Windows OCR completed.");
        var failure = OverlayTranslationGuard.GetFailure(empty);

        Equal(true, failure is not null);
        Equal(true, failure!.Contains("未识别到", StringComparison.Ordinal));
        Equal(true, OverlayTranslationGuard.GetFailure(new OcrRecognitionResult(
            true,
            [new OcrTextBlock("text", new PixelRect(0, 0, 20, 10), [])],
            "ok")) is null);
    }

    private static void EncodesOcrInMemoryOffCallerThread()
    {
        using var source = CreateBitmap(32, 32, Color.White);
        var tempBefore = Directory.GetFiles(Path.GetTempPath(), "ClipRay.Ocr.*.png").Length;
        var callerThread = Environment.CurrentManagedThreadId;
        var encoderThread = callerThread;
        var bytes = OcrBitmapEncoder.EncodePngAsync(source, thread => encoderThread = thread, CancellationToken.None).GetAwaiter().GetResult();
        Equal(true, bytes.Length > 0);
        NotEqual(callerThread, encoderThread);
        Equal(tempBefore, Directory.GetFiles(Path.GetTempPath(), "ClipRay.Ocr.*.png").Length);
    }

    private static void KeepsTranslationRuntimeIdle()
    {
        var providerCreated = 0;
        var ocrCreated = 0;
        var services = new TranslationRuntimeServices(
            () => { providerCreated++; return new DeepSeekTranslationProvider(new RecordingCredentialVault("safe"), new RecordingHttpHandler(HttpStatusCode.OK, JsonTranslation("ok"))); },
            () => { ocrCreated++; return new OcrService(() => new DelayedOcrRuntime()); });
        Equal(0, providerCreated);
        Equal(0, ocrCreated);
        _ = services.Provider;
        Equal(1, providerCreated);
        Equal(0, ocrCreated);
        _ = services.Ocr;
        Equal(1, ocrCreated);
    }

    private static void RejectsLateWindowUpdatesAfterClose()
    {
        EnsureWpfResources();
        var provider = new DeepSeekTranslationProvider(
            new RecordingCredentialVault("safe"),
            new RecordingHttpHandler(HttpStatusCode.OK, JsonTranslation("ok")));
        var window = new TranslationWindow(provider, "alias", DeepSeekModels.Flash);
        window.Show();
        var operation = window.BeginOperation();
        window.Close();
        Equal(true, operation.Token.IsCancellationRequested);
        Equal(false, window.TrySetResult(operation, "input", new TranslationResult(true, "late", "late")));
    }

    private static void StitchesExactVerticalOverlapWithoutChangingSourcePixels()
    {
        using var first = CreatePatternBitmap(6, 8, 0);
        using var second = CreatePatternBitmap(6, 8, 5);
        var stitcher = new FrameStitcher(maximumOverlapRows: 6, minimumOverlapRows: 3);

        var result = stitcher.Analyze(first, second);
        using var stitched = stitcher.Stitch(first, second, result);

        Equal(FrameStitchDisposition.Append, result.Disposition);
        Equal(3, result.OverlapRows);
        Equal(1d, result.Confidence);
        Equal(13, stitched.Height);
        Equal(first.GetPixel(2, 7).ToArgb(), stitched.GetPixel(2, 7).ToArgb());
        Equal(second.GetPixel(4, 7).ToArgb(), stitched.GetPixel(4, 12).ToArgb());
    }

    private static void StitchesUniqueLargeOverlapDespiteSparseDynamicPixels()
    {
        const int width = 452;
        const int frameHeight = 600;
        const int displacement = 120;
        using var content = CreateTallPatternBitmap(width, frameHeight + displacement, 0);
        using var first = content.Clone(new Rectangle(0, 0, width, frameHeight), content.PixelFormat);
        using var next = content.Clone(new Rectangle(0, displacement, width, frameHeight), content.PixelFormat);
        var originalOverlapPixel = first.GetPixel(226, displacement + 240);
        for (var index = 0; index < 36; index++)
        {
            var x = index == 0 ? 226 : (index * 97) % width;
            var y = index == 0 ? 240 : (index * 131) % (frameHeight - displacement);
            next.SetPixel(x, y, Color.FromArgb(255, 255 - index, index, 0xA5));
        }
        var stitcher = new FrameStitcher();

        var result = stitcher.Analyze(first, next);
        using var stitched = stitcher.Stitch(first, next, result);

        Equal(FrameStitchDisposition.Append, result.Disposition);
        Equal(frameHeight - displacement, result.OverlapRows);
        Equal(frameHeight + displacement, stitched.Height);
        Equal(originalOverlapPixel.ToArgb(), stitched.GetPixel(226, displacement + 240).ToArgb());
        Equal(next.GetPixel(451, 599).ToArgb(), stitched.GetPixel(451, frameHeight + displacement - 1).ToArgb());
    }

    private static void RejectsOtherwiseRepeatedLargeFrameWithSparseDynamicPixels()
    {
        using var first = CreateTallPatternBitmap(452, 600, 0);
        using var next = (Bitmap)first.Clone();
        for (var index = 0; index < 24; index++)
        {
            next.SetPixel((index * 83) % next.Width, (index * 137) % next.Height, Color.FromArgb(255, index, 0x5A, 255 - index));
        }

        Equal(false, new FrameStitcher().Analyze(first, next).Disposition == FrameStitchDisposition.Append);
    }

    private static void StitchesThroughChangingEdgesAndSparseDynamicRows()
    {
        const int width = 452;
        const int frameHeight = 600;
        const int displacement = 120;
        using var content = CreateTallPatternBitmap(width, frameHeight + displacement, 0);
        using var first = content.Clone(new Rectangle(0, 0, width, frameHeight), content.PixelFormat);
        using var next = content.Clone(new Rectangle(0, displacement, width, frameHeight), content.PixelFormat);
        for (var y = 0; y < frameHeight - displacement; y++)
        {
            for (var x = 0; x < 42; x++) next.SetPixel(x, y, Color.FromArgb(255, y & 255, x, 0xC0));
            for (var x = width - 42; x < width; x++) next.SetPixel(x, y, Color.FromArgb(255, x & 255, y & 255, 0x30));
        }
        foreach (var dynamicRow in new[] { 71, 183, 296, 407 })
        {
            for (var x = 42; x < width - 42; x++) next.SetPixel(x, dynamicRow, Color.FromArgb(255, dynamicRow & 255, x & 255, 0x7F));
        }

        var result = new FrameStitcher().Analyze(first, next);

        Equal(FrameStitchDisposition.Append, result.Disposition);
        Equal(frameHeight - displacement, result.OverlapRows);
    }

    private static void RejectsLowConfidenceAndRepeatedFrames()
    {
        using var first = CreatePatternBitmap(6, 8, 0);
        using var unrelated = CreateSolidPatternBitmap(6, 8, Color.Magenta);
        using var repeated = (Bitmap)first.Clone(new Rectangle(0, 0, first.Width, first.Height), first.PixelFormat);
        var stitcher = new FrameStitcher(maximumOverlapRows: 6, minimumOverlapRows: 3);

        Equal(FrameStitchDisposition.LowConfidence, stitcher.Analyze(first, unrelated).Disposition);
        Equal(FrameStitchDisposition.Repeated, stitcher.Analyze(first, repeated).Disposition);
    }

    private static void RejectsPartialWidthAndPixelFormatMismatches()
    {
        using var first = CreatePatternBitmap(6, 8, 0);
        using var narrower = CreatePatternBitmap(5, 8, 5);
        using var indexed = new Bitmap(6, 8, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        var stitcher = new FrameStitcher(maximumOverlapRows: 6, minimumOverlapRows: 3);

        Equal(FrameStitchDisposition.Incompatible, stitcher.Analyze(first, narrower).Disposition);
        Equal(FrameStitchDisposition.Incompatible, stitcher.Analyze(first, indexed).Disposition);
    }

    private static void DisposesBoundedLosslessSegmentsAfterManualPartialCapture()
    {
        using var workspace = TemporaryWorkspace.Create();
        using var cancellation = new CancellationTokenSource();
        var capture = new FixtureRegionCapture(CreatePatternBitmap(6, 8, 0), CreatePatternBitmap(6, 8, 5), CreatePatternBitmap(6, 8, 5));
        var input = new RecordingScrollInput();
        using var session = new ScrollCaptureSession(
            new Rectangle(20, 30, 6, 8),
            ScrollCaptureMode.Manual,
            capture,
            input,
            new CancellingSettler(cancellation, 3),
            Path.Combine(workspace.Root, "long-capture-temp"),
            new ScrollCaptureOptions(MaximumFrames: 3, SettleDelay: TimeSpan.Zero),
            new FrameStitcher(maximumOverlapRows: 6, minimumOverlapRows: 3));

        var result = session.RunAsync(cancellation.Token).GetAwaiter().GetResult();
        using var output = result.CreateComposite();

        Equal(ScrollCaptureStopReason.Cancelled, result.StopReason);
        Equal(2, result.AcceptedFrameCount);
        Equal(0, input.WheelCalls.Count);
        Equal(2, Directory.GetFiles(result.TempSessionDirectory, "*.png").Length);
        Equal(13, output.Height);
        Equal(34, output.GetPixel(2, 0).R);
        result.Dispose();
        Equal(false, Directory.Exists(result.TempSessionDirectory));
    }

    private static void CleansOrphanLongCaptureSessionsWhilePreservingActiveSessions()
    {
        using var workspace = TemporaryWorkspace.Create();
        var root = Path.Combine(workspace.Root, "long-capture");
        var orphan = Path.Combine(root, "orphan");
        var active = Path.Combine(root, "active");
        Directory.CreateDirectory(orphan);
        Directory.CreateDirectory(active);
        File.WriteAllText(Path.Combine(orphan, "000.png"), "orphan");
        File.WriteAllText(Path.Combine(active, "000.png"), "active");

        var result = ScrollCaptureTempStore.CleanupOrphans(root, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { active });

        Equal(1, result.DeletedSessionCount);
        Equal(false, Directory.Exists(orphan));
        Equal(true, Directory.Exists(active));
    }

    private static void ComposesThreeDynamicEdgeSegmentsFromAdjacentFrameOverlaps()
    {
        const int width = 64;
        const int frameHeight = 60;
        const int displacement = 15;
        using var workspace = TemporaryWorkspace.Create();
        using var content = CreateTallPatternBitmap(width, frameHeight + displacement * 2, 0);
        var frames = Enumerable.Range(0, 3)
            .Select(index => content.Clone(new Rectangle(0, index * displacement, width, frameHeight), content.PixelFormat))
            .ToArray();
        for (var index = 0; index < frames.Length; index++)
        {
            for (var y = 0; y < frameHeight; y++)
            {
                for (var x = 0; x < 16; x++) frames[index].SetPixel(x, y, Color.FromArgb(255, index * 60, y, x));
                for (var x = width - 16; x < width; x++) frames[index].SetPixel(x, y, Color.FromArgb(255, x, index * 60, y));
            }
        }
        var expectedFirstEdge = frames[0].GetPixel(0, 50).ToArgb();
        var expectedSecondEdge = frames[1].GetPixel(0, 55).ToArgb();
        var expectedThirdEdge = frames[2].GetPixel(0, 55).ToArgb();
        var expectedCenterTail = frames[2].GetPixel(width / 2, frameHeight - 1).ToArgb();
        using var session = new ScrollCaptureSession(
            new Rectangle(0, 0, width, frameHeight),
            ScrollCaptureMode.Manual,
            new FixtureRegionCapture(frames),
            new RecordingScrollInput(),
            new ImmediateScrollSettler(),
            Path.Combine(workspace.Root, "long"),
            new ScrollCaptureOptions(MaximumFrames: 3, SettleDelay: TimeSpan.Zero, MaximumPolls: 3));

        var result = session.RunAsync(CancellationToken.None).GetAwaiter().GetResult();
        using var composite = result.CreateComposite();

        Equal(3, result.AcceptedFrameCount);
        Equal(frameHeight + displacement * 2, composite.Height);
        Equal(expectedFirstEdge, composite.GetPixel(0, 50).ToArgb());
        Equal(expectedSecondEdge, composite.GetPixel(0, 70).ToArgb());
        Equal(expectedThirdEdge, composite.GetPixel(0, 85).ToArgb());
        Equal(expectedCenterTail, composite.GetPixel(width / 2, composite.Height - 1).ToArgb());
    }

    private static void BoundsAutomaticWheelInputAndStopsOnCancellation()
    {
        using var workspace = TemporaryWorkspace.Create();
        using var cancellation = new CancellationTokenSource();
        var input = new RecordingScrollInput(() => cancellation.Cancel());
        var capture = new FixtureRegionCapture(CreatePatternBitmap(6, 8, 0), CreatePatternBitmap(6, 8, 5), CreatePatternBitmap(6, 8, 10));
        using var session = new ScrollCaptureSession(
            new Rectangle(20, 30, 6, 8),
            ScrollCaptureMode.Automatic,
            capture,
            input,
            new ImmediateScrollSettler(),
            Path.Combine(workspace.Root, "long-capture-temp"),
            new ScrollCaptureOptions(MaximumFrames: 3, SettleDelay: TimeSpan.Zero),
            new FrameStitcher(maximumOverlapRows: 6, minimumOverlapRows: 3));

        var result = session.RunAsync(cancellation.Token).GetAwaiter().GetResult();

        Equal(ScrollCaptureStopReason.Cancelled, result.StopReason);
        Equal(1, input.WheelCalls.Count);
        Equal(new Point(23, 34), input.WheelCalls[0].Target);
        Equal(true, Directory.Exists(result.TempSessionDirectory));
    }

    private static void ManualCaptureWaitsThroughUnchangedPollsUntilExplicitlyCancelled()
    {
        using var workspace = TemporaryWorkspace.Create();
        using var cancellation = new CancellationTokenSource();
        var capture = new FixtureRegionCapture(CreatePatternBitmap(6, 8, 0), CreatePatternBitmap(6, 8, 0), CreatePatternBitmap(6, 8, 0));
        using var session = new ScrollCaptureSession(new Rectangle(0, 0, 6, 8), ScrollCaptureMode.Manual, capture, new RecordingScrollInput(), new CancellingSettler(cancellation, 2), Path.Combine(workspace.Root, "long"), new ScrollCaptureOptions(MaximumFrames: 3, SettleDelay: TimeSpan.Zero, MaximumPolls: 8));

        var result = session.RunAsync(cancellation.Token).GetAwaiter().GetResult();

        Equal(ScrollCaptureStopReason.Cancelled, result.StopReason);
        Equal(1, result.AcceptedFrameCount);
    }

    private static void ManualCaptureWaitsThroughLowConfidenceUntilExplicitlyCancelled()
    {
        using var workspace = TemporaryWorkspace.Create();
        using var cancellation = new CancellationTokenSource();
        var capture = new FixtureRegionCapture(
            CreatePatternBitmap(6, 8, 0),
            CreateSolidPatternBitmap(6, 8, Color.Magenta));
        using var session = new ScrollCaptureSession(
            new Rectangle(0, 0, 6, 8),
            ScrollCaptureMode.Manual,
            capture,
            new RecordingScrollInput(),
            new CancellingSettler(cancellation, 2),
            Path.Combine(workspace.Root, "long"),
            new ScrollCaptureOptions(MaximumFrames: 3, SettleDelay: TimeSpan.Zero, MaximumPolls: 8),
            new FrameStitcher(maximumOverlapRows: 6, minimumOverlapRows: 3));

        var result = session.RunAsync(cancellation.Token).GetAwaiter().GetResult();

        Equal(ScrollCaptureStopReason.Cancelled, result.StopReason);
        Equal(1, result.AcceptedFrameCount);
    }

    private static void AutomaticCaptureWaitsForFirstMovementUntilCancelled()
    {
        using var workspace = TemporaryWorkspace.Create();
        using var cancellation = new CancellationTokenSource();
        var input = new RecordingScrollInput();
        using var session = new ScrollCaptureSession(
            new Rectangle(0, 0, 6, 8),
            ScrollCaptureMode.Automatic,
            new FixtureRegionCapture(
                CreatePatternBitmap(6, 8, 0),
                CreatePatternBitmap(6, 8, 0),
                CreatePatternBitmap(6, 8, 0),
                CreatePatternBitmap(6, 8, 0),
                CreatePatternBitmap(6, 8, 0)),
            input,
            new CancellingSettler(cancellation, 4),
            Path.Combine(workspace.Root, "long"),
            new ScrollCaptureOptions(MaximumFrames: 3, SettleDelay: TimeSpan.Zero, MaximumPolls: 1, ConsecutiveRepeatedFramesToStop: 1));

        var result = session.RunAsync(cancellation.Token).GetAwaiter().GetResult();

        Equal(ScrollCaptureStopReason.Cancelled, result.StopReason);
        Equal(true, input.WheelCalls.Count >= 3);
    }

    private static void AutomaticScrollingNeverRepositionsTheUserCursor()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var source = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "Scrolling", "ScrollInputService.cs"));

        Equal(false, source.Contains("SetCursorPos", StringComparison.Ordinal));
        Equal(false, source.Contains("SendInput", StringComparison.Ordinal));
    }

    private static void LongCaptureControlsDefaultToManualMode()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var source = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "Scrolling", "ScrollCaptureControlWindow.cs"));
        var sessionSource = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "Scrolling", "ScrollCaptureSession.cs"));

        Equal(true, source.Contains("ScrollCaptureMode mode = ScrollCaptureMode.Manual", StringComparison.Ordinal));
        Equal(true, sessionSource.Contains("int MaximumFrames = 160", StringComparison.Ordinal));
        var overlaySource = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "Overlay", "SelectionOverlayWindow.xaml.cs"));
        Equal(true, overlaySource.Contains("StartLongCapture(ScrollCaptureMode.Manual);", StringComparison.Ordinal));
    }

    private static void ManualLongCaptureCanToggleAutomaticScrolling()
    {
        using var workspace = TemporaryWorkspace.Create();
        using var session = new ScrollCaptureSession(
            new Rectangle(0, 0, 6, 8),
            ScrollCaptureMode.Manual,
            new FixtureRegionCapture(CreatePatternBitmap(6, 8, 0)),
            new RecordingScrollInput(),
            new ImmediateScrollSettler(),
            Path.Combine(workspace.Root, "long"));

        Equal(false, session.AutomaticScrollingEnabled);
        session.SetAutomaticScrolling(true);
        Equal(true, session.AutomaticScrollingEnabled);
        session.SetAutomaticScrolling(false);
        Equal(false, session.AutomaticScrollingEnabled);

        var controlSource = File.ReadAllText(Path.Combine(WorkspaceLocator.Find(AppContext.BaseDirectory), "src", "ClipRay.App", "Scrolling", "ScrollCaptureControlWindow.cs"));
        Equal(true, controlSource.Contains("启动自动滚动", StringComparison.Ordinal));
        Equal(true, controlSource.Contains("暂停滚动", StringComparison.Ordinal));
        Equal(true, controlSource.Contains("继续滚动", StringComparison.Ordinal));
    }

    private static void LongCaptureResultSupportsFitWidthAndCtrlWheelZoom()
    {
        Equal(0.10d, ScrollCaptureZoomPolicy.Clamp(0.01));
        Equal(4.00d, ScrollCaptureZoomPolicy.Clamp(9));
        Equal(0.50d, ScrollCaptureZoomPolicy.FitWidth(500, 1000));
        Equal(250d, ScrollCaptureZoomPolicy.OffsetForPointer(100, 200, 1, 1.5));

        var source = File.ReadAllText(Path.Combine(WorkspaceLocator.Find(AppContext.BaseDirectory), "src", "ClipRay.App", "Scrolling", "ScrollCaptureResultWindow.cs"));
        Equal(true, source.Contains("PreviewMouseWheel", StringComparison.Ordinal));
        Equal(true, source.Contains("ModifierKeys.Control", StringComparison.Ordinal));
        Equal(true, source.Contains("ApplyFitWidth", StringComparison.Ordinal));
        Equal(true, source.Contains("ToolbarIconKind.FitWidth", StringComparison.Ordinal));
        Equal(true, source.Contains("OnImageMouseLeftButtonDown", StringComparison.Ordinal));
        Equal(true, source.Contains("ScrollCapturePanPolicy.Offset", StringComparison.Ordinal));
        Equal(true, source.Contains("DarkScrollBarStyle", StringComparison.Ordinal));

        var appXaml = File.ReadAllText(Path.Combine(WorkspaceLocator.Find(AppContext.BaseDirectory), "src", "ClipRay.App", "App.xaml"));
        Equal(true, appXaml.Contains("x:Key=\"DarkScrollBarStyle\"", StringComparison.Ordinal));
        Equal(true, appXaml.Contains("Orientation\" Value=\"Horizontal", StringComparison.Ordinal));
    }

    private static void AutomaticCaptureToleratesDelayedFirstScrollBeforeStopping()
    {
        using var workspace = TemporaryWorkspace.Create();
        using var initial = CreatePatternBitmap(6, 8, 0);
        using var moved = CreatePatternBitmap(6, 8, 5);
        using var session = new ScrollCaptureSession(
            new Rectangle(0, 0, 6, 8),
            ScrollCaptureMode.Automatic,
            new FixtureRegionCapture(
                (Bitmap)initial.Clone(),
                (Bitmap)initial.Clone(),
                (Bitmap)moved.Clone(),
                (Bitmap)moved.Clone(),
                (Bitmap)moved.Clone(),
                (Bitmap)moved.Clone()),
            new RecordingScrollInput(),
            new ImmediateScrollSettler(),
            Path.Combine(workspace.Root, "long"),
            new ScrollCaptureOptions(MaximumFrames: 4, SettleDelay: TimeSpan.Zero, MaximumPolls: 5, ConsecutiveRepeatedFramesToStop: 3),
            new FrameStitcher(maximumOverlapRows: 6, minimumOverlapRows: 3));

        var result = session.RunAsync(CancellationToken.None).GetAwaiter().GetResult();

        Equal(2, result.AcceptedFrameCount);
        Equal(ScrollCaptureStopReason.EndOfScroll, result.StopReason);
    }

    private static void AutomaticCaptureSurvivesTransientRepeatedFrameRun()
    {
        using var workspace = TemporaryWorkspace.Create();
        using var initial = CreatePatternBitmap(6, 8, 0);
        using var moved = CreatePatternBitmap(6, 8, 5);
        using var movedAgain = CreatePatternBitmap(6, 8, 10);
        using var session = new ScrollCaptureSession(
            new Rectangle(0, 0, 6, 8),
            ScrollCaptureMode.Automatic,
            new FixtureRegionCapture(
                (Bitmap)initial.Clone(),
                (Bitmap)moved.Clone(),
                (Bitmap)moved.Clone(),
                (Bitmap)moved.Clone(),
                (Bitmap)moved.Clone(),
                (Bitmap)moved.Clone(),
                (Bitmap)movedAgain.Clone()),
            new RecordingScrollInput(),
            new ImmediateScrollSettler(),
            Path.Combine(workspace.Root, "long"),
            new ScrollCaptureOptions(MaximumFrames: 3, SettleDelay: TimeSpan.Zero, MaximumPolls: 10),
            new FrameStitcher(maximumOverlapRows: 6, minimumOverlapRows: 3));

        var result = session.RunAsync(CancellationToken.None).GetAwaiter().GetResult();

        Equal(3, result.AcceptedFrameCount);
        Equal(ScrollCaptureStopReason.FrameLimit, result.StopReason);
    }

    private static void AutomaticCaptureSurvivesTransientLowConfidenceFrame()
    {
        using var workspace = TemporaryWorkspace.Create();
        using var session = new ScrollCaptureSession(
            new Rectangle(0, 0, 6, 8),
            ScrollCaptureMode.Automatic,
            new FixtureRegionCapture(
                CreatePatternBitmap(6, 8, 0),
                CreatePatternBitmap(6, 8, 5),
                CreateSolidPatternBitmap(6, 8, Color.Magenta),
                CreatePatternBitmap(6, 8, 10)),
            new RecordingScrollInput(),
            new ImmediateScrollSettler(),
            Path.Combine(workspace.Root, "long"),
            new ScrollCaptureOptions(MaximumFrames: 3, SettleDelay: TimeSpan.Zero, MaximumPolls: 10),
            new FrameStitcher(maximumOverlapRows: 6, minimumOverlapRows: 3));

        var result = session.RunAsync(CancellationToken.None).GetAwaiter().GetResult();

        Equal(3, result.AcceptedFrameCount);
        Equal(ScrollCaptureStopReason.FrameLimit, result.StopReason);
    }

    private static void RejectsAmbiguousRepeatedTextureOverlaps()
    {
        using var first = CreateRepeatedTextureBitmap([0, 1, 0, 1, 0, 1, 0, 1]);
        using var next = CreateRepeatedTextureBitmap([0, 1, 0, 1, 9, 10, 11, 12]);
        var stitcher = new FrameStitcher(maximumOverlapRows: 6, minimumOverlapRows: 2);

        Equal(FrameStitchDisposition.LowConfidence, stitcher.Analyze(first, next).Disposition);
    }

    private static void RejectsRowRunAmbiguityInLargeRepeatedTexture()
    {
        var firstRows = Enumerable.Range(0, 600).Select(static row => row % 4).ToArray();
        var nextRows = Enumerable.Range(120, 600).Select(static row => row < 600 ? row % 4 : row).ToArray();
        using var first = CreateWideRepeatedTextureBitmap(452, firstRows);
        using var next = CreateWideRepeatedTextureBitmap(452, nextRows);
        for (var index = 0; index < 4; index++)
        {
            next.SetPixel(index, 100 + index * 37, Color.FromArgb(255, 0xD0 + index, 0x20, 0x70));
        }

        Equal(FrameStitchDisposition.LowConfidence, new FrameStitcher().Analyze(first, next).Disposition);
    }

    private static void AutomaticCaptureSettlesBeforeEveryBoundedWheelCapture()
    {
        using var workspace = TemporaryWorkspace.Create();
        var events = new List<string>();
        var capture = new OrderedRegionCapture(events, CreatePatternBitmap(6, 8, 0), CreatePatternBitmap(6, 8, 5), CreatePatternBitmap(6, 8, 10));
        var input = new OrderedInput(events);
        using var session = new ScrollCaptureSession(new Rectangle(0, 0, 6, 8), ScrollCaptureMode.Automatic, capture, input, new OrderedSettler(events), Path.Combine(workspace.Root, "long"), new ScrollCaptureOptions(MaximumFrames: 3, SettleDelay: TimeSpan.Zero), new FrameStitcher(6, 3));

        var result = session.RunAsync(CancellationToken.None).GetAwaiter().GetResult();

        Equal(ScrollCaptureStopReason.FrameLimit, result.StopReason);
        Equal(2, input.WheelCalls.Count);
        Equal("capture,wheel,settle,capture,wheel,settle,capture", string.Join(',', events));
    }

    private static void InputFailurePreservesAComposablePartialCapture()
    {
        using var workspace = TemporaryWorkspace.Create();
        using var session = new ScrollCaptureSession(new Rectangle(0, 0, 6, 8), ScrollCaptureMode.Automatic, new FixtureRegionCapture(CreatePatternBitmap(6, 8, 0)), new ThrowingInput(), new ImmediateScrollSettler(), Path.Combine(workspace.Root, "long"));
        var result = session.RunAsync(CancellationToken.None).GetAwaiter().GetResult();
        using var partial = result.CreateComposite();
        Equal(ScrollCaptureStopReason.InputFailure, result.StopReason);
        Equal(8, partial.Height);
        Equal(true, Directory.Exists(result.TempSessionDirectory));
    }

    private static void CaptureFailurePreservesAcceptedPartialRecovery()
    {
        using var workspace = TemporaryWorkspace.Create();
        using var session = new ScrollCaptureSession(new Rectangle(0, 0, 6, 8), ScrollCaptureMode.Manual, new ThrowingCapture(CreatePatternBitmap(6, 8, 0), CreatePatternBitmap(6, 8, 5)), new RecordingScrollInput(), new ImmediateScrollSettler(), Path.Combine(workspace.Root, "long"), new ScrollCaptureOptions(3, TimeSpan.Zero, 3), new FrameStitcher(6, 3));
        var result = session.RunAsync(CancellationToken.None).GetAwaiter().GetResult();
        using var partial = result.CreateComposite();
        Equal(ScrollCaptureStopReason.CaptureFailure, result.StopReason);
        Equal(13, partial.Height);
        Equal(true, Directory.Exists(result.TempSessionDirectory));
    }

    private static void CaptureControlStopRemainsReachableWithoutDesktopInput()
    {
        var control = new ScrollCaptureControlWindow();
        var stopped = false;
        control.StopRequested += (_, _) => stopped = true;
        control.Show();
        control.UpdateLayout();
        var grid = (System.Windows.Controls.Grid)((System.Windows.Controls.Border)control.Content).Child;
        var panel = grid.Children.OfType<System.Windows.Controls.StackPanel>().Single();
        panel.Children.OfType<System.Windows.Controls.Button>().First(button =>
            string.Equals(
                System.Windows.Automation.AutomationProperties.GetName(button),
                "点击查看结果",
                StringComparison.Ordinal))
            .RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        Equal(true, stopped);
        control.Close();
    }

    private static void RepositionsLongCaptureChoiceWithTheSelectionToolbar()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var code = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "Overlay", "SelectionOverlayWindow.xaml.cs"));
        var choice = code.IndexOf("private void ShowLongCaptureChoice()", StringComparison.Ordinal);
        var reposition = code.IndexOf("UpdateVisuals();", choice, StringComparison.Ordinal);
        var toolbarLayout = code.IndexOf("PositionToolProperties(toolbarDip, toolbarSize);", StringComparison.Ordinal);
        Equal(true, choice >= 0 && reposition > choice && toolbarLayout > choice);
    }

    private static void StitchesATallSmallScrollOverlapBeyond512Rows()
    {
        using var first = CreateTallPatternBitmap(6, 900, 0);
        using var next = CreateTallPatternBitmap(6, 900, 5);
        var stitcher = new FrameStitcher();
        var result = stitcher.Analyze(first, next);
        using var stitched = stitcher.Stitch(first, next, result);
        Equal(FrameStitchDisposition.Append, result.Disposition);
        Equal(895, result.OverlapRows);
        Equal(905, stitched.Height);
        Equal(next.GetPixel(3, 899).ToArgb(), stitched.GetPixel(3, 904).ToArgb());
    }

    private static void PublishesCumulativeLongCapturePreview()
    {
        using var workspace = TemporaryWorkspace.Create();
        var previews = new List<Size>();
        using var session = new ScrollCaptureSession(
            new Rectangle(0, 0, 6, 8),
            ScrollCaptureMode.Manual,
            new FixtureRegionCapture(CreatePatternBitmap(6, 8, 0), CreatePatternBitmap(6, 8, 5)),
            new RecordingScrollInput(),
            new ImmediateScrollSettler(),
            Path.Combine(workspace.Root, "long"),
            new ScrollCaptureOptions(MaximumFrames: 2, SettleDelay: TimeSpan.Zero, MaximumPolls: 2),
            new FrameStitcher(6, 3));
        session.PreviewChanged += (_, preview) =>
        {
            previews.Add(preview.Bitmap.Size);
        };

        var result = session.RunAsync(CancellationToken.None).GetAwaiter().GetResult();

        Equal(2, result.AcceptedFrameCount);
        Equal("6x8,6x13", string.Join(',', previews.Select(static size => $"{size.Width}x{size.Height}")));
    }

    private static void AssociatesFinalSaveWithSameMultiFrameLongCaptureRun()
    {
        using var workspace = TemporaryWorkspace.Create();
        var diagnostics = new LongCaptureRunDiagnostics("fixture-run", "fixture-version");
        using var session = new ScrollCaptureSession(
            new Rectangle(0, 0, 6, 8),
            ScrollCaptureMode.Manual,
            new FixtureRegionCapture(CreatePatternBitmap(6, 8, 0), CreatePatternBitmap(6, 8, 5)),
            new RecordingScrollInput(),
            new ImmediateScrollSettler(),
            Path.Combine(workspace.Root, "long"),
            new ScrollCaptureOptions(MaximumFrames: 2, SettleDelay: TimeSpan.Zero, MaximumPolls: 2),
            new FrameStitcher(6, 3),
            diagnostics);

        using var result = session.RunAsync(CancellationToken.None).GetAwaiter().GetResult();
        using var composite = result.CreateComposite();
        var output = new ImageOutputService(
            Path.Combine(workspace.Root, "output"),
            copyToClipboard: false,
            OutputArtifactKind.LongCapture);
        var outputPath = output.SaveAndCopy(composite, diagnostics);
        var diagnosticPath = LongCaptureRunDiagnosticStore.Write(workspace.Root, diagnostics);
        using var finalPng = new Bitmap(outputPath);

        Equal(2, diagnostics.AcceptedFrameCount);
        Equal(2, diagnostics.SegmentSizes.Count);
        Equal(new LongCaptureImageSize(6, 13), diagnostics.CompositeSize!);
        Equal(diagnostics.CompositeSize!, diagnostics.SaveInputSize!);
        Equal(diagnostics.CompositeSize!, diagnostics.SaveOutputSize!);
        Equal(diagnostics.CompositeSize!, diagnostics.FinalPngSize!);
        Equal(composite.Size, finalPng.Size);
        Equal(true, File.Exists(diagnosticPath));
        var json = File.ReadAllText(diagnosticPath);
        Equal(true, json.Contains("fixture-run", StringComparison.Ordinal));
        Equal(false, json.Contains("ocr", StringComparison.OrdinalIgnoreCase));
        Equal(false, json.Contains(workspace.Root, StringComparison.OrdinalIgnoreCase));
        Equal(false, json.Contains(outputPath, StringComparison.OrdinalIgnoreCase));
        Equal(Path.GetFileName(outputPath), diagnostics.OutputArtifactId);
    }

    private static void SizesAndPlacesLongCapturePreview()
    {
        var workArea = new Rectangle(0, 0, 1920, 1080);
        var selection = new Rectangle(24, 218, 446, 644);
        var initial = ScrollCapturePreviewLayout.Calculate(selection, new Size(446, 644), workArea);
        var tall = ScrollCapturePreviewLayout.Calculate(selection, new Size(446, 1932), workArea);

        Equal(new Size(208, 300), initial.Bounds.Size);
        Equal(selection.Right + 8, initial.Bounds.Left);
        Equal(new Size(69, 300), tall.Bounds.Size);
        Equal(selection.Right + 8, tall.Bounds.Left);
    }

    private static void PlacesCaptureControlsOutsideEdgeAndNegativeCoordinateRegions()
    {
        var workAreas = new[] { new Rectangle(-1920, 0, 1920, 1080), new Rectangle(0, 0, 1920, 1080) };
        var rightEdge = ScrollCapturePlacementPolicy.Place(new Rectangle(1800, 100, 120, 800), new Size(148, 76), workAreas)!.Value.Bounds;
        var negative = ScrollCapturePlacementPolicy.Place(new Rectangle(-1900, 900, 1800, 160), new Size(148, 76), workAreas)!.Value.Bounds;
        Equal(false, rightEdge.IntersectsWith(new Rectangle(1800, 100, 120, 800)));
        Equal(false, negative.IntersectsWith(new Rectangle(-1900, 900, 1800, 160)));
        Equal(true, workAreas.Any(work => work.Contains(rightEdge)));
        Equal(true, workAreas.Any(work => work.Contains(negative)));
        Equal(true, ScrollCapturePlacementPolicy.Place(new Rectangle(5000, 0, 10, 10), new Size(148, 76), workAreas) is null);
    }

    private static void RejectsPreCancelledLargeNearMatchAnalysis()
    {
        using var first = CreateTallPatternBitmap(64, 1400, 0);
        using var next = CreateTallPatternBitmap(64, 1400, 1);
        next.SetPixel(63, 1399, Color.Magenta);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Throws<OperationCanceledException>(() => new FrameStitcher().Analyze(first, next, cancellation.Token));
    }

    private static void TearsDownAnActiveDisplayedControlWithoutDisposedCancellationFault()
    {
        using var cancellation = new CancellationTokenSource();
        var control = new ScrollCaptureControlWindow();
        control.StopRequested += (_, _) => cancellation.Cancel();
        control.Show(); control.UpdateLayout();
        LongCaptureLifetime.Teardown(cancellation, control);
        Equal(true, cancellation.IsCancellationRequested);
        Equal(false, control.IsVisible);
    }

    private static void MapsNegativePhysicalRegionsIntoTheMonitorCrop()
    {
        var monitor = new Rectangle(-1920, -120, 1920, 1080);
        var region = new Rectangle(-1881, -91, 641, 479);
        var mapping = RecordingGeometry.MapToMonitor(region, [monitor]);

        Equal(monitor, mapping.MonitorBounds);
        Equal(new Rectangle(39, 29, 641, 479), mapping.CropInMonitor);
    }

    private static void ChoosesMonitorWithLargestSelectedIntersection()
    {
        var monitors = new[] { new Rectangle(-1280, 0, 1280, 1024), new Rectangle(0, 0, 1920, 1080) };
        var mapping = RecordingGeometry.MapToMonitor(new Rectangle(-40, 100, 160, 240), monitors);

        Equal(monitors[1], mapping.MonitorBounds);
        Equal(new Rectangle(0, 100, 120, 240), mapping.CropInMonitor);
    }

    private static void NormalizesEncoderDimensionsByCroppingOnlyRightAndBottom()
    {
        var normalized = RecordingGeometry.NormalizeForEncoder(new Rectangle(17, 23, 641, 479));

        Equal(new Rectangle(17, 23, 640, 478), normalized);
    }

    private static void UsesOneQpcEpochForAudioAndVideoTimestamps()
    {
        var timeline = new RecordingTimeline(epochQpc: 10_000, qpcFrequency: 10_000_000);

        Equal(500_000L, timeline.GetTimestamp100Nanoseconds(RecordingStream.Video, 510_000));
        Equal(500_000L, timeline.GetTimestamp100Nanoseconds(RecordingStream.Audio, 510_000));
    }

    private static void RejectsNonMonotonicTimestampsPerRecordingStream()
    {
        var timeline = new RecordingTimeline(epochQpc: 100, qpcFrequency: 1_000);
        _ = timeline.GetTimestamp100Nanoseconds(RecordingStream.Video, 120);
        _ = timeline.GetTimestamp100Nanoseconds(RecordingStream.Audio, 115);

        Throws<InvalidOperationException>(() => timeline.GetTimestamp100Nanoseconds(RecordingStream.Video, 119));
        Equal(160_000L, timeline.GetTimestamp100Nanoseconds(RecordingStream.Audio, 116));
    }

    private static void ExposesOnlyDefaultRenderLoopbackAudioCreation()
    {
        var methods = typeof(ISystemAudioCaptureFactory).GetMethods();

        Equal(1, methods.Length);
        Equal("CreateDefaultRenderLoopback", methods[0].Name);
        Equal(0, methods[0].GetParameters().Length);
        Equal(false, methods.Any(method => method.Name.Contains("Microphone", StringComparison.OrdinalIgnoreCase)));
    }

    private static void StopsARecordingSessionExactlyOnceUnderConcurrentCalls()
    {
        var runtime = new FixtureRecordingRuntime();
        var session = new RecordingSession(runtime);
        session.StartAsync(CancellationToken.None).GetAwaiter().GetResult();

        Task.WhenAll(Enumerable.Range(0, 16).Select(_ => session.StopAsync())).GetAwaiter().GetResult();

        Equal(1, runtime.StopCalls);
        Equal(1, runtime.DisposeCalls);
        Equal(RecordingSessionState.Stopped, session.State);
    }

    private static void FormatsPrivacySafeRecordingStartupFailure()
    {
        const string privateDetail = "private audio device name";
        var source = new System.Runtime.InteropServices.COMException(
            privateDetail,
            unchecked((int)0xC00D36B4));
        var failure = new RecordingStartupException(RecordingStartupStage.AudioEncoder, source);

        var text = RecordingFailureText.Format(failure, @"E:\输出\recording.partial.mp4");

        Equal(true, text.Contains("AAC 音频编码", StringComparison.Ordinal));
        Equal(true, text.Contains("0xC00D36B4", StringComparison.Ordinal));
        Equal(true, text.Contains(@"E:\输出\recording.partial.mp4", StringComparison.Ordinal));
        Equal(false, text.Contains(privateDetail, StringComparison.Ordinal));
    }

    private static void WrapsFixedRecordingStartupStage()
    {
        var source = new System.Runtime.InteropServices.COMException(
            "private codec detail",
            unchecked((int)0xC00D36B4));

        var failure = ThrowsAndReturns<RecordingStartupException>(() =>
            RecordingStartupGuard.Execute(RecordingStartupStage.VideoEncoder, () => throw source));

        Equal(RecordingStartupStage.VideoEncoder, failure.Stage);
        Equal(unchecked((int)0xC00D36B4), failure.HResult);
    }

    private static void PreservesFirstRecordingStartupFailureDuringCleanup()
    {
        var primary = new RecordingStartupException(
            RecordingStartupStage.Mp4SinkWriter,
            new System.Runtime.InteropServices.COMException("private sink detail", unchecked((int)0xC00D36B2)));
        var runtime = new FailingStartAndDisposeRecordingRuntime(primary, new IOException("private cleanup detail"));
        var session = new RecordingSession(runtime);

        var failure = ThrowsAndReturns<RecordingStartupException>(() =>
            session.StartAsync(CancellationToken.None).GetAwaiter().GetResult());

        Equal(RecordingStartupStage.Mp4SinkWriter, failure.Stage);
        Equal(RecordingSessionState.Failed, session.State);
    }

    private static void RoutesRecordingStartupUiThroughSafeFormatter()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var app = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "App.xaml.cs"));

        Equal(true, app.Contains("catch (Exception exception)", StringComparison.Ordinal));
        Equal(true, app.Contains(
            "RecordingFailureText.Format(exception, File.Exists(partial) ? partial : null,",
            StringComparison.Ordinal));
    }

    private static void CreatesCollisionFreePartialAndFinalRecordingPaths()
    {
        using var workspace = TemporaryWorkspace.Create();
        var first = RecordingOutputPaths.Create(workspace.Root, new DateTime(2026, 8, 5, 8, 9, 10));
        Directory.CreateDirectory(Path.GetDirectoryName(first.PartialPath)!);
        File.WriteAllText(first.PartialPath, "partial");
        File.WriteAllText(first.FinalPath, "final");
        var second = RecordingOutputPaths.Create(workspace.Root, new DateTime(2026, 8, 5, 8, 9, 10));

        NotEqual(first.PartialPath, second.PartialPath);
        NotEqual(first.FinalPath, second.FinalPath);
        Equal("ClipRay-Recording_2026-08-05_08-09-10-000_2.mp4", Path.GetFileName(second.FinalPath));
        Equal(".partial.mp4", Path.GetExtension(Path.GetFileNameWithoutExtension(second.PartialPath)) + Path.GetExtension(second.PartialPath));
    }

    private static void FinalizesByAnAtomicNoOverwriteMove()
    {
        using var workspace = TemporaryWorkspace.Create();
        var paths = RecordingOutputPaths.Create(workspace.Root, DateTime.UtcNow);
        Directory.CreateDirectory(Path.GetDirectoryName(paths.PartialPath)!);
        File.WriteAllText(paths.PartialPath, "fixture");
        var mover = new RecordingFileMover();

        mover.Finalize(paths);

        Equal(false, File.Exists(paths.PartialPath));
        Equal("fixture", File.ReadAllText(paths.FinalPath));
    }

    private static void PreservesTheExactPartialRecordingAfterFinalizeFailure()
    {
        using var workspace = TemporaryWorkspace.Create();
        var paths = RecordingOutputPaths.Create(workspace.Root, DateTime.UtcNow);
        Directory.CreateDirectory(Path.GetDirectoryName(paths.PartialPath)!);
        File.WriteAllText(paths.PartialPath, "recoverable");
        File.WriteAllText(paths.FinalPath, "collision");

        Throws<IOException>(() => new RecordingFileMover().Finalize(paths));
        Equal("recoverable", File.ReadAllText(paths.PartialPath));
        Equal("collision", File.ReadAllText(paths.FinalPath));
    }

    private static void DefinesANonInteractiveCaptureExcludedRecordingBorder()
    {
        Equal(true, RecordingBorderPolicy.ExtendedWindowStyle.HasFlag(RecordingBorderExtendedStyle.Transparent));
        Equal(true, RecordingBorderPolicy.ExtendedWindowStyle.HasFlag(RecordingBorderExtendedStyle.NoActivate));
        Equal(0x00000011u, RecordingBorderPolicy.DisplayAffinity);
    }

    private static void KeepsAFullScreenRecordingBorderInsideVisibleDisplayBounds()
    {
        var display = new Rectangle(0, 0, 1920, 1080);

        Equal(display, RecordingBorderPlacement.VisibleBounds(display));
    }

    private static void KeepsRecordingAliveWhenOptionalBorderCannotBeCaptureExcluded()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var border = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "Recording", "RecordingBorderWindow.cs"));
        var app = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "App.xaml.cs"));
        var failure = new RecordingStartupException(
            RecordingStartupStage.RecordingUi,
            new System.Runtime.InteropServices.COMException("private UI detail", unchecked((int)0x80004005)));

        Equal(true, RecordingFailureText.Format(failure, null).Contains("录屏控制界面", StringComparison.Ordinal));
        Equal(true, border.Contains("AffinityApplied = SetWindowDisplayAffinity", StringComparison.Ordinal));
        Equal(true, border.Contains("PlacementApplied = SetWindowPos", StringComparison.Ordinal));
        Equal(true, border.Contains("public bool IsCaptureSafe => AffinityApplied && PlacementApplied;", StringComparison.Ordinal));
        Equal(false, border.Contains("throw new Win32Exception", StringComparison.Ordinal));
        Equal(true, app.Contains("RecordingStartupStage.RecordingUi", StringComparison.Ordinal));
        Equal(true, app.Contains("if (!_recordingBorder.IsCaptureSafe)", StringComparison.Ordinal));
        Equal(true, app.Contains("_recordingBorder.Close();", StringComparison.Ordinal));
    }

    private static void WritesPrivacySafeRecordingBorderProbe()
    {
        using var workspace = TemporaryWorkspace.Create();
        var diagnostics = new RecordingBorderDiagnostics(
            AffinityApplied: false,
            AffinityErrorCode: 87,
            PlacementApplied: true,
            PlacementErrorCode: 0,
            DwmCompositionEnabled: true,
            IsLayeredWindow: true,
            ExtendedWindowStyle: 0x000800A0);

        var path = RecordingBorderProbeStore.Write(workspace.Root, diagnostics);
        var json = File.ReadAllText(path);
        var appSource = File.ReadAllText(Path.Combine(
            WorkspaceLocator.Find(AppContext.BaseDirectory),
            "src",
            "ClipRay.App",
            "App.xaml.cs"));
        var borderSource = File.ReadAllText(Path.Combine(
            WorkspaceLocator.Find(AppContext.BaseDirectory),
            "src",
            "ClipRay.App",
            "Recording",
            "RecordingBorderWindow.cs"));

        Equal("recording-border-probe.json", Path.GetFileName(path));
        Equal(true, json.Contains("\"AffinityErrorCode\": 87", StringComparison.Ordinal));
        Equal(true, json.Contains("\"DwmCompositionEnabled\": true", StringComparison.Ordinal));
        Equal(false, json.Contains(Environment.UserName, StringComparison.OrdinalIgnoreCase));
        Equal(true, appSource.Contains("--recording-border-probe", StringComparison.Ordinal));
        Equal(true, appSource.Contains("RecordingBorderProbeStore.Write", StringComparison.Ordinal));
        Equal(true, borderSource.Contains("Marshal.GetLastWin32Error()", StringComparison.Ordinal));
        Equal(true, borderSource.Contains("DwmIsCompositionEnabled", StringComparison.Ordinal));
    }

    private static void PinsOnlyTheApprovedRecordingDependencies()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var project = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "ClipRay.App.csproj"));

        Equal(true, project.Contains("PackageReference Include=\"NAudio.Wasapi\" Version=\"2.3.0\"", StringComparison.Ordinal));
        Equal(true, project.Contains("PackageReference Include=\"Vortice.Direct3D11\" Version=\"3.8.3\"", StringComparison.Ordinal));
        Equal(true, project.Contains("PackageReference Include=\"Vortice.MediaFoundation\" Version=\"3.8.3\"", StringComparison.Ordinal));
        Equal(false, project.Contains("FFmpeg", StringComparison.OrdinalIgnoreCase));
    }

    private static void KeepsRecordingBackendsIdleUntilRuntimeStarts()
    {
        var video = new FixtureVideoCaptureSource();
        var audio = new FixtureSystemAudioCapture();
        var recorder = new FixtureMediaRecorder();
        var runtime = new RegionRecordingRuntime(video, audio, recorder);

        Equal(0, video.StartCalls);
        Equal(0, audio.StartCalls);
        Equal(0, recorder.StartCalls);
        runtime.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        Equal(1, video.StartCalls);
        Equal(1, audio.StartCalls);
        Equal(1, recorder.StartCalls);
        runtime.StopAsync().GetAwaiter().GetResult();
        runtime.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private static void CreatesWasapiCaptureOnlyThroughTheDefaultLoopbackConstructor()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var source = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "Recording", "SystemAudioCapture.cs"));

        Equal(true, source.Contains("new WasapiLoopbackCapture()", StringComparison.Ordinal));
        Equal(false, source.Contains("MMDeviceEnumerator", StringComparison.Ordinal));
        Equal(false, source.Contains("new WasapiCapture(", StringComparison.Ordinal));
    }

    private static void EncodesGeneratedMotionAndAudioFixtureThroughMediaFoundation()
    {
        using var workspace = TemporaryWorkspace.Create();
        var paths = RecordingOutputPaths.Create(workspace.Root, new DateTime(2026, 8, 5, 10, 0, 0));
        var recorder = new MediaFoundationRecorder(paths, 160, 90, new AudioCaptureFormat(48_000, 2, 32, true));
        recorder.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        const long videoDuration = 10_000_000 / 30;
        for (var frameIndex = 0; frameIndex < 30; frameIndex++)
        {
            var pixels = new byte[160 * 90 * 4];
            for (var y = 0; y < 90; y++)
            for (var x = 0; x < 160; x++)
            {
                var offset = (y * 160 + x) * 4;
                pixels[offset] = (byte)((x + frameIndex * 5) & 255);
                pixels[offset + 1] = (byte)((y * 2 + frameIndex * 7) & 255);
                pixels[offset + 2] = (byte)((x + y + frameIndex * 11) & 255);
                pixels[offset + 3] = 255;
            }
            recorder.WriteVideoAsync(new VideoRecordingSample(pixels, 160, 90, frameIndex * videoDuration, videoDuration)).AsTask().GetAwaiter().GetResult();
        }

        const int audioFramesPerPacket = 480;
        const long audioDuration = 100_000;
        for (var packet = 0; packet < 100; packet++)
        {
            var pcm = new byte[audioFramesPerPacket * 2 * sizeof(float)];
            Span<float> floats = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(pcm.AsSpan());
            for (var sample = 0; sample < audioFramesPerPacket; sample++)
            {
                var value = 0.2f * MathF.Sin(2 * MathF.PI * 440 * (packet * audioFramesPerPacket + sample) / 48_000f);
                floats[sample * 2] = value;
                floats[sample * 2 + 1] = value;
            }
            recorder.WriteAudioAsync(new AudioRecordingSample(pcm, 48_000, 2, 32, packet * audioDuration, audioDuration)).AsTask().GetAwaiter().GetResult();
        }
        recorder.StopAsync().GetAwaiter().GetResult();
        recorder.DisposeAsync().AsTask().GetAwaiter().GetResult();

        Equal(false, File.Exists(paths.PartialPath));
        Equal(true, new FileInfo(paths.FinalPath).Length > 1024);
    }

    private static Bitmap CreateTallPatternBitmap(int width, int height, int startRow)
    {
        var bitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var row = startRow + y;
            bitmap.SetPixel(x, y, Color.FromArgb(255, row & 255, (row >> 8) & 255, (x * 41 + row) & 255));
        }
        return bitmap;
    }

    private static Bitmap CreatePatternBitmap(int width, int height, int startRow)
    {
        var bitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var row = startRow + y;
            var red = (row * 31 + x * 17) % 256;
            var green = ((row % 4) * 61 + x * 29) % 256;
            var blue = ((row / 2) * 47 + x * 11) % 256;
            bitmap.SetPixel(x, y, Color.FromArgb(255, red, green, blue));
        }
        return bitmap;
    }

    private static Bitmap CreateSolidPatternBitmap(int width, int height, Color color)
    {
        var bitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(color);
        return bitmap;
    }

    private static Bitmap CreateRepeatedTextureBitmap(IReadOnlyList<int> rows)
    {
        var bitmap = new Bitmap(6, rows.Count, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        for (var y = 0; y < rows.Count; y++)
        for (var x = 0; x < bitmap.Width; x++) bitmap.SetPixel(x, y, Color.FromArgb(255, rows[y] * 20, (x % 3) * 60, (rows[y] + x) * 11));
        return bitmap;
    }

    private static Bitmap CreateWideRepeatedTextureBitmap(int width, IReadOnlyList<int> rows)
    {
        var bitmap = new Bitmap(width, rows.Count, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        for (var y = 0; y < rows.Count; y++)
        for (var x = 0; x < width; x++) bitmap.SetPixel(x, y, Color.FromArgb(255, rows[y] & 255, (x % 7) * 31, ((rows[y] * 13) + x) & 255));
        return bitmap;
    }

    private static string JsonTranslation(string text) => System.Text.Json.JsonSerializer.Serialize(new
    {
        choices = new[] { new { message = new { content = text } } },
    });

    private static string JsonTranslationWithFinishReason(string text, string finishReason) => System.Text.Json.JsonSerializer.Serialize(new
    {
        choices = new[] { new { message = new { content = text }, finish_reason = finishReason } },
    });

    private static string StreamingJsonTranslationWithFinishReason(string text, string finishReason)
    {
        var chunk = System.Text.Json.JsonSerializer.Serialize(new
        {
            choices = new[] { new { delta = new { content = text }, finish_reason = finishReason } },
        });
        return $"data: {chunk}\n\ndata: [DONE]\n";
    }

    private static string StreamingJsonTranslationWithUsage(
        string text,
        string finishReason,
        long promptCacheHitTokens,
        long promptCacheMissTokens,
        long completionTokens)
    {
        var contentChunk = System.Text.Json.JsonSerializer.Serialize(new
        {
            choices = new[] { new { delta = new { content = text }, finish_reason = (string?)null } },
        });
        var finishChunk = System.Text.Json.JsonSerializer.Serialize(new
        {
            choices = new[] { new { delta = new { content = string.Empty }, finish_reason = finishReason } },
        });
        var usageChunk = System.Text.Json.JsonSerializer.Serialize(new
        {
            choices = Array.Empty<object>(),
            usage = new
            {
                prompt_tokens = promptCacheHitTokens + promptCacheMissTokens,
                prompt_cache_hit_tokens = promptCacheHitTokens,
                prompt_cache_miss_tokens = promptCacheMissTokens,
                completion_tokens = completionTokens,
                total_tokens = promptCacheHitTokens + promptCacheMissTokens + completionTokens,
            },
        });
        return $"data: {contentChunk}\n\ndata: {finishChunk}\n\ndata: {usageChunk}\n\ndata: [DONE]\n";
    }

    private static void EnsureWpfResources()
    {
        var application = System.Windows.Application.Current ?? new System.Windows.Application();
        if (!application.Resources.Contains("AppFont"))
        {
            application.Resources["AppFont"] = new System.Windows.Media.FontFamily("Segoe UI, Microsoft YaHei UI");
        }
    }

    private static void SetPrivateField(object target, string name, object value) =>
        target.GetType().GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(target, value);

    private static void InvokePrivate(object target, string name) =>
        target.GetType().GetMethod(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(target, null);

    private static T GetPrivateField<T>(object target, string name) =>
        (T)target.GetType().GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(target)!;

    private static void AssertPreviewMatches(
        SelectionOverlayWindow overlay,
        Bitmap source,
        EditLayer layer,
        Color expectedColor,
        System.Drawing.Point expectedPoint)
    {
        var preview = (System.Windows.Controls.Image)overlay.FindName("EditPreview");
        using var expected = EditRenderer.Composite(source, layer);
        using var actual = ToBitmap((System.Windows.Media.Imaging.BitmapSource)preview.Source);

        Equal(expected.Width, actual.Width);
        Equal(expected.Height, actual.Height);
        Equal(expectedColor.ToArgb(), actual.GetPixel(expectedPoint.X, expectedPoint.Y).ToArgb());
    }

    private static Bitmap ToBitmap(System.Windows.Media.Imaging.BitmapSource source)
    {
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(source));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        stream.Position = 0;
        using var decoded = new Bitmap(stream);
        return new Bitmap(decoded);
    }

    private static Bitmap CreateBitmap(int width, int height, Color color)
    {
        var bitmap = new Bitmap(width, height);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(color);
        return bitmap;
    }

    private static void PaintSyntheticText(Bitmap bitmap, PixelRect bounds, Color color, int glyphHeight)
    {
        var left = Math.Clamp((int)Math.Ceiling(bounds.Left) + 2, 0, bitmap.Width);
        var right = Math.Clamp((int)Math.Floor(bounds.Right) - 2, left, bitmap.Width);
        var top = Math.Clamp(
            (int)Math.Round(bounds.Top + (bounds.Height - glyphHeight) / 2),
            0,
            bitmap.Height);
        var bottom = Math.Clamp(top + glyphHeight, top, bitmap.Height);
        for (var x = left; x < right; x++)
        {
            if ((x - left) % 6 >= 3) continue;
            for (var y = top; y < bottom; y++) bitmap.SetPixel(x, y, color);
        }
    }

    private static double ColorDistance(Color first, Color second)
    {
        var red = first.R - second.R;
        var green = first.G - second.G;
        var blue = first.B - second.B;
        return Math.Sqrt(red * red + green * green + blue * blue);
    }

    private sealed class RecordingClipboardTextSink : IClipboardTextSink
    {
        public string? Text { get; private set; }

        public void SetText(string text) => Text = text;
    }

    private sealed class RecordingColorClipboardTextSink : IColorClipboardTextSink
    {
        public string? Text { get; private set; }
        public int PlainWrites { get; private set; }
        public int ColorWrites { get; private set; }

        public void SetText(string text)
        {
            PlainWrites++;
            Text = text;
        }

        public void SetColorText(string text)
        {
            ColorWrites++;
            Text = text;
        }
    }

    private sealed class RecordingCredentialVault(string? value) : ICredentialVault
    {
        public int GetForUseCalls { get; private set; }
        public int ListCalls { get; private set; }
        public void Save(string name, string provider, string secret) => throw new NotSupportedException();
        public IReadOnlyList<CredentialMetadata> List() { ListCalls++; return []; }
        public void Delete(string name) => throw new NotSupportedException();
        public string? GetForUse(string name) { GetForUseCalls++; return value; }
    }

    private sealed class RecordingLocalLmtSessionFactory(params string[] outputs) : ILocalLmtSessionFactory
    {
        private readonly Queue<string> _outputs = new(outputs);
        private readonly TaskCompletionSource<bool> _translationEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int StartCalls { get; private set; }
        public bool CancellationObserved { get; private set; }
        public int DisposeCalls { get; private set; }
        public bool SessionDisposed => DisposeCalls > 0;
        public bool WaitForCancellation { get; init; }
        public LocalLmtProcessFailureKind? FailureKind { get; init; }
        public int? FailureAtPrompt { get; init; }
        public LocalLmtSessionStart? Start { get; private set; }
        public List<string> Prompts { get; } = [];
        public Task TranslationEntered => _translationEntered.Task;

        public Task<ILocalLmtSession> StartAsync(LocalLmtSessionStart start, CancellationToken cancellationToken)
        {
            StartCalls++;
            Start = start;
            return Task.FromResult<ILocalLmtSession>(new Session(this));
        }

        private sealed class Session(RecordingLocalLmtSessionFactory owner) : ILocalLmtSession
        {
            private bool _disposed;
            public bool IsAlive => !_disposed;

            public async Task<string> TranslateAsync(string prompt, CancellationToken cancellationToken)
            {
                owner.Prompts.Add(prompt);
                owner._translationEntered.TrySetResult(true);
                if (owner.FailureKind is { } failure
                    && (owner.FailureAtPrompt is null || owner.Prompts.Count == owner.FailureAtPrompt))
                    throw new LocalLmtProcessException(failure);
                if (owner.WaitForCancellation)
                {
                    try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
                    catch (OperationCanceledException)
                    {
                        owner.CancellationObserved = true;
                        Dispose();
                        throw;
                    }
                }
                return owner._outputs.Count > 0 ? owner._outputs.Dequeue() : string.Empty;
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                owner.DisposeCalls++;
            }
        }
    }

    private sealed class RecordingLocalLmtOneShotRunner(params string[] outputs) : ILocalLmtOneShotRunner
    {
        private readonly Queue<string> _outputs = new(outputs);
        public int Calls { get; private set; }
        public LocalLmtSessionStart? Start { get; private set; }
        public List<string> Prompts { get; } = [];

        public Task<string> TranslateAsync(LocalLmtSessionStart start, string prompt, CancellationToken cancellationToken)
        {
            Calls++;
            Start = start;
            Prompts.Add(prompt);
            return Task.FromResult(_outputs.Count > 0 ? _outputs.Dequeue() : string.Empty);
        }
    }

    private sealed class RecordingLocalLmtBatchRunner(params string[] outputs) : ILocalLmtBatchRunner
    {
        public int Calls { get; private set; }
        public LocalLmtSessionStart? Start { get; private set; }
        public List<string> Prompts { get; } = [];

        public Task<IReadOnlyList<string>> TranslateAsync(
            LocalLmtSessionStart start,
            IReadOnlyList<string> prompts,
            CancellationToken cancellationToken)
        {
            Calls++;
            Start = start;
            Prompts.AddRange(prompts);
            return Task.FromResult<IReadOnlyList<string>>(outputs);
        }
    }

    private sealed class RecordingHttpHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _send;
        public RecordingHttpHandler(HttpStatusCode status, string content) : this((_, _) => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(content, Encoding.UTF8, "application/json") })) { }
        public RecordingHttpHandler(Exception error) : this((_, _) => Task.FromException<HttpResponseMessage>(error)) { }
        public RecordingHttpHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) => _send = send;
        public Uri? RequestUri { get; private set; }
        public HttpMethod? Method { get; private set; }
        public string? Body { get; private set; }
        public int CallCount { get; private set; }
        public string? AuthorizationScheme { get; private set; }
        public int AuthorizationParameterLength { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            RequestUri = request.RequestUri;
            Method = request.Method;
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationParameterLength = request.Headers.Authorization?.Parameter?.Length ?? 0;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return await _send(request, cancellationToken);
        }
    }

    private sealed class DynamicProtectedTokenHandler(Func<string, string> translate) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            using var payload = System.Text.Json.JsonDocument.Parse(body);
            var promptText = payload.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!;
            using var prompt = System.Text.Json.JsonDocument.Parse(promptText);
            var protectedText = prompt.RootElement.GetProperty("b0").GetProperty("text").GetString()!;
            var response = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["b0"] = translate(protectedText),
            });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonTranslation(response), Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class StubHotkeyKeyLabelResolver(Func<System.Windows.Input.Key, bool, string?> resolve)
        : IHotkeyKeyLabelResolver
    {
        public string? Resolve(System.Windows.Input.Key key, bool shift) => resolve(key, shift);
    }

    private sealed class DelayedOcrRuntime : IOcrRuntime
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Color ObservedColor { get; private set; }
        public void Release() => _release.SetResult();
        public async Task<IReadOnlyList<OcrWord>> RecognizeAsync(Bitmap bitmap, CancellationToken cancellationToken)
        {
            await _release.Task.WaitAsync(cancellationToken);
            ObservedColor = bitmap.GetPixel(0, 0);
            return [new OcrWord("ok", new PixelRect(0, 0, 2, 2))];
        }
    }

    private sealed class FailingClipboardTextSink : IClipboardTextSink
    {
        public int Attempts { get; private set; }

        public void SetText(string text)
        {
            Attempts++;
            throw new ExternalException("clipboard is busy");
        }
    }

    private static WindowSnapCandidate Candidate(
        long handle,
        double x,
        double y,
        double width,
        double height,
        bool isClipRayOwned = false,
        bool isVisible = true,
        bool isMinimized = false,
        bool isCloaked = false,
        bool isToolWindow = false,
        string windowClass = "Application") => new(
        new IntPtr(handle),
        new PixelRect(x, y, width, height),
        isClipRayOwned,
        isVisible,
        isMinimized,
        isCloaked,
        isToolWindow,
        windowClass);

    private static void SwitchesSettingsPrimaryActionOnlyWhenTheDraftChanges()
    {
        var state = new SettingsDraftState("initial");
        Equal(SettingsPrimaryAction.Refresh, state.Action);

        state.Observe("changed");
        Equal(SettingsPrimaryAction.Save, state.Action);

        state.Observe("initial");
        Equal(SettingsPrimaryAction.Refresh, state.Action);

        state.Observe("saved");
        state.MarkSaved("saved");
        Equal(SettingsPrimaryAction.Refresh, state.Action);
    }

    private static void OpticallyAlignsCaptureToolbarGlyphs()
    {
        var xaml = File.ReadAllText(Path.Combine(
            WorkspaceLocator.Find(AppContext.BaseDirectory),
            "src", "ClipRay.App", "Overlay", "SelectionOverlayWindow.xaml"));

        Equal(true, xaml.Contains("<Setter Property=\"SnapsToDevicePixels\" Value=\"True\"", StringComparison.Ordinal));
        Equal(true, xaml.Contains("x:Name=\"RecordGlyphTransform\"", StringComparison.Ordinal));
        Equal(true, xaml.Contains("Y=\"-0.75\"", StringComparison.Ordinal));
    }

    private static void UsesAPurposeBuiltToolbarForRecordingSelection()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var xaml = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "Overlay", "SelectionOverlayWindow.xaml"));
        var source = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "Overlay", "SelectionOverlayWindow.xaml.cs"));

        Equal(true, xaml.Contains("x:Name=\"CaptureEditingTools\"", StringComparison.Ordinal));
        Equal(true, source.Contains("CaptureEditingTools.Visibility = Visibility.Collapsed", StringComparison.Ordinal));
        Equal(true, xaml.Contains("x:Name=\"CancelToolButton\"", StringComparison.Ordinal));
        Equal(true, xaml.Contains("x:Name=\"ConfirmToolButton\"", StringComparison.Ordinal));
        var app = File.ReadAllText(Path.Combine(workspace, "src", "ClipRay.App", "App.xaml.cs"));
        Equal(true, app.Contains("--recording-overlay-preview", StringComparison.Ordinal));
        Equal(true, app.Contains("launchMode: OverlayLaunchMode.Recording", StringComparison.Ordinal));
    }

    private static void MapsMouseGrabMovementToBoundedLongCaptureOffsets()
    {
        Equal(150d, ScrollCapturePanPolicy.Offset(100, 200, 150, 1_000));
        Equal(50d, ScrollCapturePanPolicy.Offset(100, 200, 250, 1_000));
        Equal(0d, ScrollCapturePanPolicy.Offset(10, 200, 400, 1_000));
        Equal(1_000d, ScrollCapturePanPolicy.Offset(990, 200, 0, 1_000));
        Equal(0d, ScrollCapturePanPolicy.Offset(100, 200, 150, -1));
    }

    private static void ClosesLongCaptureChromeBeforeShowingModalResult()
    {
        var source = File.ReadAllText(Path.Combine(
            WorkspaceLocator.Find(AppContext.BaseDirectory),
            "src", "ClipRay.App", "App.xaml.cs"));
        var composite = source.IndexOf("using var bitmap = result.CreateComposite();", StringComparison.Ordinal);
        var closeChrome = source.IndexOf("CloseLongCaptureCaptureUi();", composite, StringComparison.Ordinal);
        var closeOverlay = source.IndexOf("if (overlay.IsLoaded) overlay.Close();", closeChrome, StringComparison.Ordinal);
        var showResult = source.IndexOf("preview.ShowDialog()", closeOverlay, StringComparison.Ordinal);

        Equal(true, composite >= 0 && closeChrome > composite && closeOverlay > closeChrome && showResult > closeOverlay);
    }

    private static void LongCaptureControlsShowOnlyAcceptedFrameCount()
    {
        var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var source = File.ReadAllText(Path.Combine(
            workspace,
            "src", "ClipRay.App", "App.xaml.cs"));
        var control = File.ReadAllText(Path.Combine(workspace,
            "src", "ClipRay.App", "Scrolling", "ScrollCaptureControlWindow.cs"));

        Equal(true, source.Contains("session.ProgressChanged +=", StringComparison.Ordinal));
        Equal(true, source.Contains("controls.SetProgress(progress);", StringComparison.Ordinal));
        Equal(true, control.Contains("Status.Text = T($\"{progress.AcceptedFrameCount} 帧\", $\"{progress.AcceptedFrameCount} frames\")", StringComparison.Ordinal));
        Equal(true, control.Contains("var detail = progress.Status ?? \"\";", StringComparison.Ordinal));
        Equal(false, source.Contains("session.StateChanged +=", StringComparison.Ordinal));
        Equal(false, source.Contains("controls.Status.Text +=", StringComparison.Ordinal));
    }

    private static void ScalesRecordingBitrateWithFrameAreaAndBoundsTheResult()
    {
        Equal(6_000_000u, RecordingBitratePolicy.ForFrameSize(1280, 720, 30));
        Equal(8_500_000u, RecordingBitratePolicy.ForFrameSize(1582, 856, 30));
        Equal(12_500_000u, RecordingBitratePolicy.ForFrameSize(1920, 1080, 30));
        Equal(20_000_000u, RecordingBitratePolicy.ForFrameSize(2560, 1440, 30));
        Equal(20_000_000u, RecordingBitratePolicy.ForFrameSize(int.MaxValue, int.MaxValue, int.MaxValue));
        Equal(4_000_000u, RecordingBitratePolicy.ForFrameSize(640, 360, 30));
        Throws<ArgumentOutOfRangeException>(() => RecordingBitratePolicy.ForFrameSize(0, 720, 30));
        Throws<ArgumentOutOfRangeException>(() => RecordingBitratePolicy.ForFrameSize(1280, -1, 30));
        Throws<ArgumentOutOfRangeException>(() => RecordingBitratePolicy.ForFrameSize(1280, 720, 0));
    }

    private static void Equal<T>(T expected, T actual) where T : notnull
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Expected {expected}, got {actual}");
        }
    }

    private static void NotEqual<T>(T first, T second) where T : notnull
    {
        if (EqualityComparer<T>.Default.Equals(first, second))
        {
            throw new InvalidOperationException($"Expected different values, got {first}");
        }
    }

    private static void Throws<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }

    private static TException ThrowsAndReturns<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException exception)
        {
            return exception;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }

    private sealed class FailingSaveSettingsStore : ISettingsStore
    {
        private readonly Exception _failure;

        public FailingSaveSettingsStore(Exception failure) => _failure = failure;

        public AppSettings Load() => throw new NotSupportedException();

        public SettingsLoadResult LoadRecoverable() => throw new NotSupportedException();

        public void Save(AppSettings settings) => throw _failure;
    }

    private sealed class RecordingSettingsStore : ISettingsStore
    {
        public List<AppSettings> Saved { get; } = [];

        public AppSettings Load() => throw new NotSupportedException();

        public SettingsLoadResult LoadRecoverable() => throw new NotSupportedException();

        public void Save(AppSettings settings) => Saved.Add(settings);
    }

    private sealed class RecordingCleanupScheduleRegistration : ICleanupScheduleRegistration
    {
        private readonly int? _failOnCall;

        public RecordingCleanupScheduleRegistration(int? failOnCall = null) => _failOnCall = failOnCall;

        public List<CleanupScheduleOptions> Options { get; } = [];

        public void Set(CleanupScheduleOptions options, string executablePath)
        {
            Options.Add(options);
            if (Options.Count == _failOnCall)
            {
                throw new InvalidOperationException("injected cleanup schedule failure");
            }
        }
    }

    private sealed class RecordingStartupRegistration : IStartupRegistration
    {
        private readonly int? _failOnCall;

        public RecordingStartupRegistration(int? failOnCall = null) => _failOnCall = failOnCall;

        public List<bool> EnabledStates { get; } = [];

        public void SetEnabled(bool enabled, string executablePath)
        {
            EnabledStates.Add(enabled);
            if (EnabledStates.Count == _failOnCall)
            {
                throw new InvalidOperationException("injected startup rollback failure");
            }
        }
    }

    private sealed class MemoryCredentialBackend : ICredentialBackend
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

        public bool FailNextMetadataWrite { get; set; }

        public bool FailNextMetadataDelete { get; set; }

        public void Write(string target, string value)
        {
            if (FailNextMetadataWrite && target.StartsWith("ClipRay.Metadata.", StringComparison.Ordinal))
            {
                FailNextMetadataWrite = false;
                throw new IOException("injected metadata write failure");
            }
            _values[target] = value;
        }

        public string? Read(string target) => _values.GetValueOrDefault(target);

        public void Delete(string target)
        {
            if (FailNextMetadataDelete && target.StartsWith("ClipRay.Metadata.", StringComparison.Ordinal))
            {
                FailNextMetadataDelete = false;
                throw new IOException("injected metadata delete failure");
            }
            _values.Remove(target);
        }

        public IEnumerable<string> ListTargets(string prefix) =>
            _values.Keys.Where(target => target.StartsWith(prefix, StringComparison.Ordinal)).ToArray();

        public void WriteSecret(string name, string value)
        {
            var id = Convert.ToHexString(Encoding.UTF8.GetBytes(name));
            _values["ClipRay.Credential." + id] = value;
        }
    }

    private sealed class StaticWindowCandidateSource(IReadOnlyList<WindowSnapCandidate> candidates) : IWindowCandidateSource
    {
        public IEnumerable<WindowSnapCandidate> Enumerate() => candidates;
    }

    private sealed class CountingWindowCandidateSource(IReadOnlyList<WindowSnapCandidate> candidates) : IWindowCandidateSource
    {
        public int EnumerationCount { get; private set; }

        public IEnumerable<WindowSnapCandidate> Enumerate()
        {
            EnumerationCount++;
            return candidates;
        }
    }

    private sealed class StaticUiHierarchySource(IReadOnlyList<PixelRect> candidates) : IUiElementCandidateSource
    {
        public IReadOnlyList<PixelRect> GetHierarchy(Point point, IntPtr rootWindow) => candidates;
    }

    private sealed class TemporaryWorkspace : IDisposable
    {
        private TemporaryWorkspace(string root)
        {
            Root = root;
        }

        public string Root { get; }

        public static TemporaryWorkspace Create(bool createSolutionMarker = true)
        {
            var root = Path.Combine(Path.GetTempPath(), $"ClipRay.App.Tests.{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            if (createSolutionMarker) File.WriteAllText(Path.Combine(root, "ClipRay.sln"), string.Empty);
            return new TemporaryWorkspace(root);
        }

        public void Dispose()
        {
            Directory.Delete(Root, recursive: true);
        }
    }

    private sealed class FixtureRegionCapture(params Bitmap[] frames) : IScrollRegionCapture
    {
        private readonly Queue<Bitmap> _frames = new(frames);

        public Bitmap Capture(Rectangle physicalPixelRegion)
        {
            if (_frames.Count == 0) throw new InvalidOperationException("No fixture frame remains.");
            using var source = _frames.Dequeue();
            return new Bitmap(source);
        }
    }

    private sealed class RecordingScrollInput(Action? afterWheel = null) : IScrollInputService
    {
        private readonly Action? _afterWheel = afterWheel;

        public List<(Point Target, int Delta)> WheelCalls { get; } = [];

        public Task SendWheelAsync(Point physicalPixelTarget, int delta, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WheelCalls.Add((physicalPixelTarget, delta));
            _afterWheel?.Invoke();
            return Task.CompletedTask;
        }
    }

    private sealed class ImmediateScrollSettler : IScrollSettler
    {
        public Task WaitForSettleAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private sealed class FixtureRecordingRuntime : IRecordingRuntime
    {
        private int _stopCalls;
        private int _disposeCalls;

        public int StopCalls => _stopCalls;
        public int DisposeCalls => _disposeCalls;

        public Task StartAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            Interlocked.Increment(ref _stopCalls);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposeCalls);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FailingStartAndDisposeRecordingRuntime(
        Exception startFailure,
        Exception disposeFailure) : IRecordingRuntime
    {
        public Task StartAsync(CancellationToken cancellationToken) => Task.FromException(startFailure);

        public Task StopAsync() => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.FromException(disposeFailure);
    }

    private sealed class RecordingFileClipboardSink : IRecordingFileClipboard
    {
        public string? Path { get; private set; }

        public void SetFile(string path) => Path = path;
    }

    private sealed class FixtureVideoCaptureSource : IGraphicsCaptureSource
    {
        public int StartCalls { get; private set; }
        public Task StartAsync(Func<VideoRecordingSample, ValueTask> onSample, CancellationToken cancellationToken)
        {
            StartCalls++;
            return Task.CompletedTask;
        }
        public Task StopAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FixtureSystemAudioCapture : ISystemAudioCapture
    {
        public int StartCalls { get; private set; }
        public AudioCaptureFormat Format { get; } = new(48_000, 2, 32, true);
        public Task StartAsync(Func<AudioRecordingSample, ValueTask> onSample, CancellationToken cancellationToken)
        {
            StartCalls++;
            return Task.CompletedTask;
        }
        public Task StopAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FixtureMediaRecorder : IMediaFoundationRecorder
    {
        public int StartCalls { get; private set; }
        public Task StartAsync(CancellationToken cancellationToken)
        {
            StartCalls++;
            return Task.CompletedTask;
        }
        public ValueTask WriteVideoAsync(VideoRecordingSample sample) => ValueTask.CompletedTask;
        public ValueTask WriteAudioAsync(AudioRecordingSample sample) => ValueTask.CompletedTask;
        public Task StopAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class CancellingSettler(CancellationTokenSource cancellation, int cancelAfter) : IScrollSettler
    {
        private int _count;
        public Task WaitForSettleAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            if (++_count >= cancelAfter) cancellation.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private sealed class OrderedRegionCapture(List<string> events, params Bitmap[] frames) : IScrollRegionCapture
    {
        private readonly Queue<Bitmap> _frames = new(frames);
        public Bitmap Capture(Rectangle physicalPixelRegion)
        {
            events.Add("capture");
            using var source = _frames.Dequeue();
            return new Bitmap(source);
        }
    }

    private sealed class OrderedInput(List<string> events) : IScrollInputService
    {
        public List<(Point Target, int Delta)> WheelCalls { get; } = [];
        public Task SendWheelAsync(Point physicalPixelTarget, int delta, CancellationToken cancellationToken)
        {
            events.Add("wheel");
            WheelCalls.Add((physicalPixelTarget, delta));
            return Task.CompletedTask;
        }
    }

    private sealed class OrderedSettler(List<string> events) : IScrollSettler
    {
        public Task WaitForSettleAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            events.Add("settle");
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingInput : IScrollInputService
    {
        public Task SendWheelAsync(Point physicalPixelTarget, int delta, CancellationToken cancellationToken) => throw new InvalidOperationException("fixture input failure");
    }

    private sealed class ThrowingCapture(params Bitmap[] frames) : IScrollRegionCapture
    {
        private readonly Queue<Bitmap> _frames = new(frames);
        public Bitmap Capture(Rectangle physicalPixelRegion)
        {
            if (_frames.Count == 0) throw new IOException("fixture capture failure");
            using var source = _frames.Dequeue();
            return new Bitmap(source);
        }
    }

    private sealed class RecordingMachineTranslationProvider(
        Func<IReadOnlyList<TranslationRequest>, IReadOnlyList<TranslationResult>> translate)
        : IMachineTranslationProvider
    {
        public int Calls { get; private set; }
        public IReadOnlyList<TranslationRequest> LastRequests { get; private set; } = [];

        public Task<IReadOnlyList<TranslationResult>> TranslateManyAsync(
            IReadOnlyList<TranslationRequest> requests,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            LastRequests = requests.ToArray();
            return Task.FromResult(translate(requests));
        }
    }

    private sealed class RecordingMachineTranslationProcess(
        Func<MachineTranslationProcessRequest, IReadOnlyList<string>> translate)
        : IMachineTranslationProcess
    {
        public int Calls { get; private set; }
        public MachineTranslationProcessRequest LastRequest { get; private set; } = new("", "", []);

        public Task<IReadOnlyList<string>> TranslateAsync(
            MachineTranslationProcessRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            LastRequest = request;
            return Task.FromResult(translate(request));
        }
    }
}
