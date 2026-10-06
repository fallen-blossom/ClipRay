using ClipRay.Core.Geometry;
using ClipRay.App.Translation;

namespace ClipRay.App.LiveTranslation;

public sealed record LiveTranslationStartRequest(PixelRect? Region, TranslationRouteKind Route, string TargetLanguage);
