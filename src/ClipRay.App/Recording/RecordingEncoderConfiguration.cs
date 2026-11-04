using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using Vortice.MediaFoundation;
using ResultCode = Vortice.MediaFoundation.ResultCode;

namespace ClipRay.App.Recording;

public sealed record RecordingEncoderPropertyDiagnostic(
    string Name, string RequestedValue, int SupportHResult, int? SetHResult, int? ReadHResult, string? ActualValue);

public sealed record RecordingEncoderTransformDiagnostic(
    int Index, Guid Category, Guid? Clsid, string? Name, bool? IsHardwareAccelerated, bool? IsD3D11Aware,
    RecordingEncoderPropertyDiagnostic[] Properties, string? InspectionError);

public sealed record RecordingEncoderDiagnostics(
    int FramesPerSecond, RecordingQuality Quality, uint Bitrate, string InputFormat, bool LowLatencyRequested,
    RecordingEncoderTransformDiagnostic[] Transforms, string? InspectionError)
{
    public bool GpuInputRequested { get; init; }
    public bool GpuInputEnabled { get; init; }
    public string? GpuFallbackReason { get; init; }
}

/// <summary>Configure the selected encoder, rather than assuming hardware-selection permission selected one.</summary>
internal static class RecordingEncoderConfiguration
{
    // Windows SDK codecapi.h. Vortice 3.8.3 does not expose ICodecAPI.
    private static readonly Guid CodecApiInterface = new("901db4c7-31ce-41a2-85dc-8fa0bf41b8da");
    private static readonly Guid QualityVsSpeed = new("98332df8-03cd-476b-89fa-3f9e442dec9f");
    private const int NoInterface = unchecked((int)0x80004002);

    public static RecordingEncoderDiagnostics Configure(IMFSinkWriter writer, int stream,
        RecordingVideoOptions options, uint bitrate, string inputFormat = "RGB32")
    {
        var transforms = new List<RecordingEncoderTransformDiagnostic>();
        string? inspectionError = null;
        try
        {
            using var extended = writer.QueryInterface<IMFSinkWriterEx>();
            // The chain is finite. MF_E_INVALIDINDEX is the documented end of enumeration.
            for (var index = 0; ; index++)
            {
                IMFTransform transform;
                Guid category;
                try { extended.GetTransformForStream(stream, index, out category, out transform); }
                catch (Exception error) when (error.HResult == ResultCode.Invalidindex.Code) { break; }
                using (transform)
                {
                    Guid? clsid = null;
                    string? name = null;
                    bool? hardware = null, d3d11Aware = null;
                    string? attributeError = null;
                    try
                    {
                        using var attributes = transform.Attributes;
                        if (attributes.GetGUID(TransformAttributeKeys.MftTransformClsidAttribute, out var id).Success) clsid = id;
                        try { name = attributes.GetString(TransformAttributeKeys.MftFriendlyNameAttribute); }
                        catch (Exception error) when (error.HResult < 0) { /* Optional descriptive attribute. */ }
                        try { hardware = !string.IsNullOrEmpty(attributes.GetString(TransformAttributeKeys.MftEnumHardwareUrlAttribute)); }
                        catch (Exception error) when (error.HResult == ResultCode.Attributenotfound.Code) { hardware = false; }
                        if (attributes.GetUInt32(TransformAttributeKeys.D3D11Aware, out var aware).Success) d3d11Aware = aware != 0;
                    }
                    catch (Exception error) { attributeError = Describe(error); }
                    var properties = category == TransformCategoryGuids.VideoEncoder
                        ? ConfigureCodec(transform, options.Quality) : [];
                    transforms.Add(new(index, category, clsid, name, hardware, d3d11Aware, properties, attributeError));
                }
            }
            if (!transforms.Any(item => item.Category == TransformCategoryGuids.VideoEncoder))
                inspectionError = "The Sink Writer did not expose a video encoder transform.";
        }
        catch (Exception error) { inspectionError = Describe(error); }
        // Optional inspection/configuration failures are visible, and never replace encode/finalize failures.
        return new(options.FramesPerSecond, options.Quality, bitrate, inputFormat, true, transforms.ToArray(), inspectionError);
    }

    public static BindFlags GetEncoderBindFlags(IMFSinkWriter writer, int stream)
    {
        using var extended = writer.QueryInterface<IMFSinkWriterEx>();
        for (var index = 0; ; index++)
        {
            extended.GetTransformForStream(stream, index, out var category, out var transform);
            using (transform)
            {
                if (category != TransformCategoryGuids.VideoEncoder) continue;
                // This optional attribute is absent on drivers without additional bind requirements.
                try
                {
                    using var attributes = transform.GetInputStreamAttributes(0);
                    if (attributes.GetUInt32(TransformAttributeKeys.D3D11Bindflags, out var flags).Success) return (BindFlags)flags;
                }
                catch (Exception error) when (error.HResult < 0) { }
                return BindFlags.None;
            }
        }
    }

    private static RecordingEncoderPropertyDiagnostic[] ConfigureCodec(IMFTransform transform, RecordingQuality quality)
    {
        var iid = CodecApiInterface;
        var queryResult = Marshal.QueryInterface(transform.NativePointer, in iid, out var pointer);
        if (queryResult < 0 || pointer == IntPtr.Zero)
            return [new("ICodecAPI", "available", queryResult < 0 ? queryResult : NoInterface, null, null, null)];
        try
        {
            var vtable = Marshal.ReadIntPtr(pointer);
            var supported = ReadMethod<IsSupported>(vtable, 3);
            var get = ReadMethod<PropertyValue>(vtable, 8);
            var set = ReadMethod<PropertyValue>(vtable, 9);
            // Preserve the documented default quality/speed balance for the original high-quality choice.
            var qualityVsSpeed = quality == RecordingQuality.High ? 50u : 0u;
            return
            [
                SetProperty("CODECAPI_AVEncCommonQualityVsSpeed", QualityVsSpeed, CodecVariant.UInt32(qualityVsSpeed), qualityVsSpeed == 50 ? "50" : "0", pointer, supported, set, get),
                SetProperty("CODECAPI_AVLowLatencyMode", SinkWriterAttributeKeys.LowLatency, CodecVariant.Boolean(true), "true", pointer, supported, set, get)
            ];
        }
        finally { Marshal.Release(pointer); }
    }

    private static RecordingEncoderPropertyDiagnostic SetProperty(string name, Guid property, CodecVariant requested,
        string requestedText, IntPtr codec, IsSupported supported, PropertyValue set, PropertyValue get)
    {
        var supportResult = supported(codec, ref property);
        // IsSupported returns S_OK only when supported; S_FALSE is not support.
        if (supportResult != 0) return new(name, requestedText, supportResult, null, null, null);
        var setResult = set(codec, ref property, ref requested);
        var actual = default(CodecVariant);
        try
        {
            var readResult = get(codec, ref property, ref actual);
            return new(name, requestedText, supportResult, setResult, readResult, readResult >= 0 ? actual.ToString() : null);
        }
        finally { VariantClear(ref actual); }
    }

    private static T ReadMethod<T>(IntPtr vtable, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(vtable, checked(slot * IntPtr.Size)));

    private static string Describe(Exception error) => $"{error.GetType().Name}:0x{unchecked((uint)error.HResult):X8}:{error.Message}";

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int IsSupported(IntPtr self, ref Guid property);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int PropertyValue(IntPtr self, ref Guid property, ref CodecVariant value);

    // VARIANT is 24 bytes on x64 (the app's win-x64 target); union values begin at byte 8.
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct CodecVariant
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public uint UnsignedValue;
        [FieldOffset(8)] public short BooleanValue;
        public static CodecVariant UInt32(uint value) => new() { Type = 19, UnsignedValue = value };
        public static CodecVariant Boolean(bool value) => new() { Type = 11, BooleanValue = value ? (short)-1 : (short)0 };
        public override readonly string ToString() => Type switch
        {
            19 => UnsignedValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
            11 => BooleanValue != 0 ? "true" : "false",
            _ => $"VARIANT type {Type}"
        };
    }

    [DllImport("oleaut32.dll", ExactSpelling = true)]
    private static extern int VariantClear(ref CodecVariant value);
}
