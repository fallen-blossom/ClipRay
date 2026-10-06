using ClipRay.Acceptance.Testing;

var casesRoot = GetCasesRoot(args);
try
{
    Console.WriteLine(AcceptanceCatalogReport.Format(AcceptanceCatalog.Load(casesRoot)));
    return 0;
}
catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
{
    Console.Error.WriteLine($"CATALOG_INVALID {exception.Message}");
    return 1;
}

static string GetCasesRoot(string[] arguments)
{
    if (arguments.Length == 2 && string.Equals(arguments[0], "--cases", StringComparison.OrdinalIgnoreCase))
        return Path.GetFullPath(arguments[1]);
    if (arguments.Length == 0)
        return Path.Combine(Directory.GetCurrentDirectory(), "tests", "ClipRay.Acceptance", "cases");
    throw new ArgumentException("Usage: ClipRay.Acceptance [--cases <directory>]");
}
