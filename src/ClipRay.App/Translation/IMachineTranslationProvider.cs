namespace ClipRay.App.Translation;

public interface IMachineTranslationProvider
{
    Task<IReadOnlyList<TranslationResult>> TranslateManyAsync(
        IReadOnlyList<TranslationRequest> requests,
        CancellationToken cancellationToken);
}
