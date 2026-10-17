using System.Runtime.InteropServices;

namespace ActivityMonitor.Infrastructure.Services;

/// <summary>Reads a real video stream and its duration using Windows Media Foundation.</summary>
public static class MediaFileInspector
{
    public static bool TryReadVideoDuration(string filePath, out TimeSpan duration)
    {
        duration = TimeSpan.Zero;
        IMFSourceReader? reader = null;
        var comInitialized = false;
        var mediaInitialized = false;
        try
        {
            var comResult = CoInitializeEx(IntPtr.Zero, 0);
            comInitialized = comResult >= 0;
            if (comResult < 0 && comResult != unchecked((int)0x80010106)) return false;
            if (MFStartup(0x20070, 0) < 0) return false;
            mediaInitialized = true;
            if (MFCreateSourceReaderFromURL(filePath, IntPtr.Zero, out reader) < 0) return false;
            var key = new Guid("6c990d33-bb8e-477a-8598-0d5d96fcd88a");
            if (reader.GetPresentationAttribute(uint.MaxValue, ref key, out var value) < 0) return false;
            long ticks;
            try { ticks = value.Type is 20 or 21 ? value.LongValue : 0; }
            finally { PropVariantClear(ref value); }
            if (ticks <= 0) return false;
            // The first-video-stream constant is 0xFFFFFFFC. An arbitrary large file must fail here.
            if (reader.ReadSample(0xFFFFFFFC, 0, out _, out _, out _, out var sample) < 0 || sample == IntPtr.Zero)
                return false;
            Marshal.Release(sample);
            duration = TimeSpan.FromTicks(ticks);
            return true;
        }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
        finally
        {
            if (reader != null) Marshal.FinalReleaseComObject(reader);
            if (mediaInitialized) MFShutdown();
            if (comInitialized) CoUninitialize();
        }
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropVariant
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public long LongValue;
    }

    [ComImport, Guid("70AE66F2-C809-4E4F-8915-BDCB406B7993"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFSourceReader
    {
        [PreserveSig] int GetStreamSelection(uint index, [MarshalAs(UnmanagedType.Bool)] out bool selected);
        [PreserveSig] int SetStreamSelection(uint index, [MarshalAs(UnmanagedType.Bool)] bool selected);
        [PreserveSig] int GetNativeMediaType(uint index, uint typeIndex, out IntPtr mediaType);
        [PreserveSig] int GetCurrentMediaType(uint index, out IntPtr mediaType);
        [PreserveSig] int SetCurrentMediaType(uint index, IntPtr reserved, IntPtr mediaType);
        [PreserveSig] int SetCurrentPosition(ref Guid format, ref PropVariant position);
        [PreserveSig] int ReadSample(uint index, uint flags, out uint actualIndex, out uint streamFlags, out long timestamp, out IntPtr sample);
        [PreserveSig] int Flush(uint index);
        [PreserveSig] int GetServiceForStream(uint index, ref Guid service, ref Guid iid, out IntPtr value);
        [PreserveSig] int GetPresentationAttribute(uint index, ref Guid attribute, out PropVariant value);
    }

    [DllImport("ole32.dll")] private static extern int CoInitializeEx(IntPtr reserved, uint flags);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
    [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropVariant value);
    [DllImport("mfplat.dll")] private static extern int MFStartup(uint version, uint flags);
    [DllImport("mfplat.dll")] private static extern int MFShutdown();
    [DllImport("mfreadwrite.dll", CharSet = CharSet.Unicode)]
    private static extern int MFCreateSourceReaderFromURL(string url, IntPtr attributes, out IMFSourceReader reader);
}
