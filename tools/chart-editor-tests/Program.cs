using static EditorTestSupport;
using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Shared.Chart;
using DynamiteUniverse.Shared.Chart.V2;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 3 && args[0] == "--verify-persistence-child")
            {
                PackageLifecycleTests.VerifyChild(args[1], args[2]);
                return 0;
            }
            if (args.Length == 2 && args[0] == "--persistence-output")
            {
                PackageLifecycleTests.Run(args[1]);
                return 0;
            }
            StepPathTests.Run();
            PackageLifecycleTests.Run();
            EditorBatchEditTests.Run();
            EditorNoteClipboardTests.Run();
            EditorGeometryTests.TestExactSnap();
            EditorGeometryTests.TestSideTrackStagePlacement();
            EditorGeometryTests.TestAuthoringAxisAndSnap();
            EditorGeometryTests.TestPlacementGuide();
            EditorGeometryTests.TestExactBarTimeText();
            EditorDocumentTests.TestPropertyCatalog();
            EditorDocumentTests.TestDraftCreationAndValidation();
            EditorDocumentTests.TestDraftPersistenceState();
            EditorDocumentTests.TestCommandsAndSnapshots();
            EditorPathTests.TestAllSevenNoteTypes();
            EditorPathTests.TestPathEditingCommands();
            EditorPathTests.TestPathProjection();
            EditorPathTests.TestPathNodeInsertion();
            EditorCommandTests.TestScrollEditingCommands();
            EditorDocumentTests.TestUndoFollowsOwningChart();
            EditorDocumentTests.TestChartLifecycleCommands();
            EditorCommandTests.TestBpmCollisionIsRejected();
            EditorCommandTests.TestDirectEditingCommands();
            EditorCommandTests.TestCanvasAuthoringController();
            EditorCommandTests.TestEditorPropertySteps();
            EditorCommandTests.TestMenuStepCommands();
            EditorCommandTests.TestCanvasDragAnchor();
            EditorGeometryTests.TestEditorTimeConversion();
            PackagePersistenceTests.TestGoldenRoundTrip();
            PackagePersistenceTests.TestStrictPackageOpen();
            PackagePersistenceTests.TestPackageWriter();
            PackageTransactionTests.TestSameDirectoryResourceReplacement();
            PackageTransactionTests.TestSameDirectoryPublicationRollback();
            PackageTransactionTests.TestIncompleteRollbackContinues();
            PackageTransactionTests.TestSameDirectoryCleanupFailure();
            PackagePersistenceTests.TestFirstPackageCreation();
            PackagePersistenceTests.TestEditorSaveSequence();
            AudioProbeTests.Run();
            EditorSessionTests.Run();
            EditorAutoTimelineTests.Run();
            CanvasEditGestureTests.Run();
            CanvasProjectionTests.Run();
            CanvasPlacementDraftTests.Run();
            StagePlacementDraftTests.Run();
            CanvasSelectionStateTests.Run();
            CanvasHitTestingTests.Run();
            CanvasGesturePreviewTests.Run();
            EditorCreateRequestTests.Run();
            Console.WriteLine("EDITOR TESTS PASS");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("EDITOR TESTS FAIL: " + exception);
            return 1;
        }
    }
}
