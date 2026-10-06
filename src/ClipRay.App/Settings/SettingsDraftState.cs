namespace ClipRay.App.Settings;

public enum SettingsDraftCloseChoice { Save, Discard, Cancel }

public enum SettingsPrimaryAction
{
    Refresh,
    Save,
}

public sealed class SettingsDraftState
{
    private string _baseline;
    private string _current;

    public SettingsDraftState(string baseline)
    {
        _baseline = baseline ?? throw new ArgumentNullException(nameof(baseline));
        _current = baseline;
    }

    public SettingsPrimaryAction Action => string.Equals(_baseline, _current, StringComparison.Ordinal)
        ? SettingsPrimaryAction.Refresh
        : SettingsPrimaryAction.Save;

    public void Observe(string current) => _current = current ?? throw new ArgumentNullException(nameof(current));

    public bool CanClose(SettingsDraftCloseChoice choice, Func<bool> save) =>
        Action == SettingsPrimaryAction.Refresh || choice == SettingsDraftCloseChoice.Discard
        || (choice == SettingsDraftCloseChoice.Save && save());

    public void MarkSaved(string saved)
    {
        _baseline = saved ?? throw new ArgumentNullException(nameof(saved));
        _current = saved;
    }
}
