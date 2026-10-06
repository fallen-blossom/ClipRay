using System.Drawing;
using ClipRay.App.LiveTranslation;

internal static class LiveTranslationStabilityGateTests
{
    public static void AnimationHasBoundedWaitAndStaticNeighborsProceed()
    {
        var localGate = new LiveTranslationStabilityGate();
        var media = new Rectangle(0, 0, 100, 100);
        var text = new Rectangle(0, 120, 100, 50);
        if (!localGate.Select([media, text], window => window == text, 0,
                allowUnsettledAfterDeadline: false).SequenceEqual([text]))
            throw new Exception("Stable text did not proceed independently from local animation.");
        if (localGate.Select([media], _ => false, 800,
                allowUnsettledAfterDeadline: false).Count != 0)
            throw new Exception("Perpetual local animation scheduled translation by deadline.");
        var gate = new LiveTranslationStabilityGate();
        if (gate.Select([media, text], _ => false, 0).Count != 0)
            throw new Exception("A moving viewport was processed before its settling window.");
        var shiftedMedia = new Rectangle(8, 0, 100, 100);
        var shiftedText = new Rectangle(8, 120, 100, 50);
        if (gate.Select([shiftedMedia, shiftedText], _ => false, 749).Count != 0)
            throw new Exception("Animation skipped the settling interval.");
        if (!gate.Select([shiftedMedia, shiftedText], _ => false, 750)
            .SequenceEqual([shiftedMedia, shiftedText]))
            throw new Exception("Changing crop geometry reset the motion deadline and starved recognition.");
        if (gate.Select([media], _ => false, 751).Count != 0)
            throw new Exception("The same animated crop immediately restarted recognition.");
        gate.Select([], _ => false, 800);
        if (gate.Select([media], _ => false, 900).Count != 0)
            throw new Exception("A newly visible crop inherited an old timeout.");
    }
}
