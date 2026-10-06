using ClipRay.App.Settings;

namespace ClipRay.App.Security;

public static class CredentialDeletionPolicy
{
    public static bool Confirm(string alias, Func<string, string, bool> confirm, string interfaceLanguage = "zh-CN")
    {
        var normalizedAlias = NormalizeAlias(alias);
        ArgumentNullException.ThrowIfNull(confirm);
        if (!confirm(
                AppInterfaceLanguage.Text(interfaceLanguage,
                    "将立即删除 ClipRay 在本机保存的 API key。DeepSeek 出于安全原因不会再次显示完整 key；"
                    + "如果你没有在其他地方保存，删除后将无法从 ClipRay 找回。"
                    + "删除本地副本不会使 DeepSeek 服务端的 key 失效。",
                    "This will delete the API key saved by ClipRay on this device. DeepSeek will not show the full key again. "
                    + "If you have no other copy, ClipRay cannot recover it. Deleting the local copy does not revoke the key on DeepSeek."),
                AppInterfaceLanguage.Text(interfaceLanguage, "删除本机 API 凭据", "Delete local API credential")))
        {
            return false;
        }

        return confirm(AppInterfaceLanguage.Text(interfaceLanguage,
                $"您确认删除“{normalizedAlias}”吗？", $"Delete “{normalizedAlias}” now?"),
            AppInterfaceLanguage.Text(interfaceLanguage, "最终确认", "Final confirmation"));
    }

    public static string Success(string alias, string interfaceLanguage = "zh-CN") =>
        AppInterfaceLanguage.Text(interfaceLanguage,
            $"已删除 API 凭据“{NormalizeAlias(alias)}”。",
            $"Deleted API credential “{NormalizeAlias(alias)}”.");

    private static string NormalizeAlias(string alias)
    {
        var normalized = alias?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException("请先在凭据列表中选择要删除的 API 凭据。");
        }

        return normalized;
    }
}
