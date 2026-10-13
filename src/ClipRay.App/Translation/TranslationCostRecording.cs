namespace ClipRay.App.Translation;

public static class TranslationCostRecording
{
    public static bool TryRecord(DeepSeekCostEstimate? estimate, Action<DeepSeekCostEstimate?>? record,
        Action? failed = null)
    {
        try { record?.Invoke(estimate); return true; }
        catch
        {
            // Accounting owns no provider-result or fallback authority.
            try { failed?.Invoke(); } catch { }
            return false;
        }
    }
}
