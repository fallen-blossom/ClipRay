param(
    [ValidateRange(1, 3600)]
    [int]$DurationSeconds = 30,

    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Windows.Forms
$winFormsReferences = @(
    [System.Windows.Forms.NativeWindow].Assembly.Location
    [System.Windows.Forms.Message].Assembly.Location
) | Sort-Object -Unique
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

public sealed class ClipRayClipboardMonitorWindow : NativeWindow, IDisposable
{
    public const int WM_CLIPBOARDUPDATE = 0x031D;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern IntPtr GetClipboardOwner();

    [DllImport("user32.dll")]
    public static extern uint GetClipboardSequenceNumber();

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    public event EventHandler ClipboardChanged;

    public ClipRayClipboardMonitorWindow()
    {
        CreateHandle(new CreateParams());
        if (!AddClipboardFormatListener(Handle))
            throw new InvalidOperationException("clipboard-listener-registration-failed");
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == WM_CLIPBOARDUPDATE)
            ClipboardChanged?.Invoke(this, EventArgs.Empty);
        base.WndProc(ref message);
    }

    public void Dispose()
    {
        if (Handle != IntPtr.Zero)
        {
            RemoveClipboardFormatListener(Handle);
            DestroyHandle();
        }
    }
}
'@ -ReferencedAssemblies $winFormsReferences

function Get-ClipboardOwnerMetadata {
    $ownerHandle = [ClipRayClipboardMonitorWindow]::GetClipboardOwner()
    [uint32]$ownerProcessId = 0
    if ($ownerHandle -ne [IntPtr]::Zero) {
        [void][ClipRayClipboardMonitorWindow]::GetWindowThreadProcessId($ownerHandle, [ref]$ownerProcessId)
    }

    $ownerProcessName = $null
    if ($ownerProcessId -gt 0) {
        try {
            $ownerProcessName = (Get-Process -Id $ownerProcessId -ErrorAction Stop).ProcessName
        }
        catch {
            $ownerProcessName = $null
        }
    }

    [pscustomobject]@{
        ProcessId = $ownerProcessId
        ProcessName = $ownerProcessName
    }
}

function Get-ClipRayClipboardObservation {
    $owner = Get-ClipboardOwnerMetadata
    $hasClipRayMarker = $false
    $textSha256 = $null
    $isExpectedBlack = $false

    try {
        $data = [System.Windows.Forms.Clipboard]::GetDataObject()
        $hasClipRayMarker = $null -ne $data -and $data.GetDataPresent('ClipRay.ColorPicker.v1')
        if ($hasClipRayMarker -and $data.GetDataPresent([System.Windows.Forms.DataFormats]::UnicodeText)) {
            $text = [string]$data.GetData([System.Windows.Forms.DataFormats]::UnicodeText)
            $bytes = [System.Text.Encoding]::UTF8.GetBytes($text)
            $sha256 = [System.Security.Cryptography.SHA256]::Create()
            try {
                $hash = $sha256.ComputeHash($bytes)
                $textSha256 = ([BitConverter]::ToString($hash) -replace '-', '').ToLowerInvariant()
            }
            finally {
                $sha256.Dispose()
            }
            $isExpectedBlack = $text -ceq '#000000'
        }
    }
    catch [System.Runtime.InteropServices.ExternalException] {
        # The clipboard can be transiently locked. Record metadata without retrying or exposing content.
    }

    [pscustomobject]@{
        TimestampUtc = [DateTimeOffset]::UtcNow.ToString('O')
        SequenceNumber = [ClipRayClipboardMonitorWindow]::GetClipboardSequenceNumber()
        OwnerProcessId = $owner.ProcessId
        OwnerProcessName = $owner.ProcessName
        HasClipRayColorMarker = $hasClipRayMarker
        TextSha256 = $textSha256
        IsExpectedBlack = $isExpectedBlack
    }
}

$writer = if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $null
}
else {
    $resolvedOutput = [IO.Path]::GetFullPath($OutputPath)
    $parent = [IO.Path]::GetDirectoryName($resolvedOutput)
    if (-not [string]::IsNullOrWhiteSpace($parent)) {
        [IO.Directory]::CreateDirectory($parent) | Out-Null
    }
    [IO.StreamWriter]::new($resolvedOutput, $true, [Text.UTF8Encoding]::new($false))
}

$window = [ClipRayClipboardMonitorWindow]::new()
$handler = {
    $json = Get-ClipRayClipboardObservation | ConvertTo-Json -Compress
    if ($null -eq $writer) {
        [Console]::Out.WriteLine($json)
    }
    else {
        $writer.WriteLine($json)
        $writer.Flush()
    }
}

try {
    $window.add_ClipboardChanged($handler)
    $deadline = [DateTime]::UtcNow.AddSeconds($DurationSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        [System.Windows.Forms.Application]::DoEvents()
        Start-Sleep -Milliseconds 25
    }
}
finally {
    $window.remove_ClipboardChanged($handler)
    $window.Dispose()
    if ($null -ne $writer) {
        $writer.Dispose()
    }
}
