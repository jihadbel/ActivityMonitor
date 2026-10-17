using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace ActivityMonitor.Viewer.Services;

/// <summary>
/// معلومات جهاز التقاط الصوت (الميكروفون) المعروض في قائمة الإعدادات.
/// </summary>
/// <param name="Id">المعرف التقني لنقطة نهاية الصوت في نظام تشغيل ويندوز (Endpoint ID).</param>
/// <param name="DisplayName">الاسم الودي المعروض للمستخدم شاملاً الرمز التعبيري.</param>
public record AudioDeviceInfo(string Id, string DisplayName);

/// <summary>
/// خدمة مقياس الصوت المباشر (WASAPI Audio Metering Service).
/// توفر استكشاف أجهزة الميكروفون المتاحة في النظام وقراءة مستوى الصوت اللحظي (Peak Value) لاختبار الميكروفون في واجهة الإعدادات عبر واجهات COM الأصلية.
/// </summary>
public class AudioMeterService
{
    // رمز القيمة الافتراضية للجهاز المختار (يدل على استخدام الميكروفون الافتراضي للنظام)
    private const string DefaultDevicePlaceholder = "";

    // المعرفات الفريدة لواجهات Windows Core Audio (WASAPI GUIDs)
    private static readonly Guid IidIAudioClient = new("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2");
    private static readonly Guid IidIAudioMeterInformation = new("C02216F6-8C67-4B5B-9D00-D008E73E0064");
    private static readonly Guid PkeyDeviceFriendlyName = new("A45C2554-DF1E-4EF4-EC91-700C604294BF");

    /// <summary>
    /// حصر وسرد جميع أجهزة التقاط الصوت (الميكروفونات) النشطة في نظام ويندوز.
    /// </summary>
    /// <returns>قائمة بأجهزة الصوت المكتشفة مع الميكروفون الافتراضي للنظام.</returns>
    public static List<AudioDeviceInfo> GetCaptureDevices()
    {
        // تهيئة القائمة بالخيار الافتراضي للنظام
        var list = new List<AudioDeviceInfo>
        {
            new(DefaultDevicePlaceholder, "🎙️ الميكروفون الافتراضي للنظام (Windows Default)")
        };

        try
        {
            // إنشاء كائن معدد أجهزة الوسائط MMDeviceEnumerator
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();

            // تعداد نقاط نهاية التقاط الصوت النشطة (eCapture = 1, DEVICE_STATE_ACTIVE = 1)
            int hr = enumerator.EnumAudioEndpoints(1 /* eCapture */, 1 /* DEVICE_STATE_ACTIVE */, out var pCollection);
            if (hr == 0 && pCollection != IntPtr.Zero)
            {
                // تحويل المؤشر غير المدار إلى واجهة مجموعة الأجهزة
                var collection = (IMMDeviceCollection)Marshal.GetObjectForIUnknown(pCollection);
                collection.GetCount(out uint count);

                // الدوران على كل جهاز صوتي في المجموعة
                for (uint i = 0; i < count; i++)
                {
                    collection.Item(i, out var pDev);
                    if (pDev != IntPtr.Zero)
                    {
                        var dev = (IMMDevice)Marshal.GetObjectForIUnknown(pDev);

                        // استخراج المعرف الفريد للجهاز
                        dev.GetId(out string id);

                        // استخراج الاسم الودي للجهاز أو استبداله بالمعرف
                        string friendly = GetDeviceFriendlyName(dev) ?? id;

                        // إضافة الجهاز إلى القائمة
                        list.Add(new AudioDeviceInfo(id, $"🎤 {friendly}"));

                        // تحرير مرجع الجهاز في COM
                        Marshal.Release(pDev);
                    }
                }

                // تحرير مرجع مجموعة الأجهزة
                Marshal.Release(pCollection);
            }
        }
        catch
        {
            // في حال حدوث أي استثناء بالتعامل مع COM، يتم إرجاع القائمة الافتراضية
        }

        return list;
    }

    /// <summary>
    /// استخراج الاسم الودي لجهاز الصوت من مستودع الخصائص (PropertyStore).
    /// </summary>
    /// <param name="dev">كائن جهاز الصوت IMMDevice.</param>
    /// <returns>الاسم الودي المقروء للجهاز، أو null إذا تعذر العثور عليه.</returns>
    private static string? GetDeviceFriendlyName(IMMDevice dev)
    {
        try
        {
            // فتح مستودع الخصائص بوضع القراءة فقط (STGM_READ = 0)
            int hrStore = dev.OpenPropertyStore(0 /* STGM_READ */, out var pStore);
            if (hrStore == 0 && pStore != IntPtr.Zero)
            {
                var store = (IPropertyStore)Marshal.GetObjectForIUnknown(pStore);
                store.GetCount(out uint propCount);

                string? result = null;

                // التكرار عبر الخصائص المخزنة للبحث عن خاصية الاسم الودي
                for (uint p = 0; p < propCount; p++)
                {
                    store.GetAt(p, out var k);
                    store.GetValue(ref k, out var val);

                    // التحقق من نوع القيمة النصية ذات السلسلة العريضة (VT_LPWSTR = 31)
                    if (val.vt == 31 /* VT_LPWSTR */ && val.pwszVal != IntPtr.Zero)
                    {
                        var str = Marshal.PtrToStringUni(val.pwszVal);
                        if (!string.IsNullOrWhiteSpace(str))
                        {
                            result = str;

                            // تفضيل الخاصية ذات المعرف 14 (Device Friendly Name)
                            if (k.pid == 14) break;
                        }
                    }
                }

                // تحرير مرجع مستودع الخصائص
                Marshal.Release(pStore);
                return result;
            }
        }
        catch
        {
            // تجاهل أخطاء قراءة الخصائص
        }

        return null;
    }

    /// <summary>
    /// إنشاء جلسة صوتية مباشرة لمراقبة مستوى الصوت (Live Audio Session) من الجهاز المحدد.
    /// </summary>
    /// <param name="deviceId">معرف جهاز الصوت المطلوب مراقبته، أو null للميكروفون الافتراضي.</param>
    /// <returns>جلسة صوتية نشطة قابلة للمراقبة والتحرير <see cref="LiveAudioSession"/>، أو null في حال الفشل.</returns>
    public static LiveAudioSession? CreateLiveSession(string? deviceId)
    {
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            IntPtr pEndpoint;

            // تحديد نقطة النهاية (الافتراضية أو المحددة بالمعرف)
            if (string.IsNullOrEmpty(deviceId))
            {
                int hr = enumerator.GetDefaultAudioEndpoint(1 /* eCapture */, 0 /* eConsole */, out pEndpoint);
                if (hr != 0 || pEndpoint == IntPtr.Zero) return null;
            }
            else
            {
                int hr = enumerator.GetDevice(deviceId, out pEndpoint);
                if (hr != 0 || pEndpoint == IntPtr.Zero)
                {
                    // محاولة التراجع إلى الجهاز الافتراضي في حال عدم توفر الجهاز المحدد
                    int hrDef = enumerator.GetDefaultAudioEndpoint(1 /* eCapture */, 0 /* eConsole */, out pEndpoint);
                    if (hrDef != 0 || pEndpoint == IntPtr.Zero) return null;
                }
            }

            var dev = (IMMDevice)Marshal.GetObjectForIUnknown(pEndpoint);

            // تفعيل واجهة عميل الصوت IAudioClient
            var clientGuid = IidIAudioClient;
            int hrClient = dev.Activate(ref clientGuid, 1 /* CLSCTX_INPROC_SERVER */, IntPtr.Zero, out var pClient);
            if (hrClient != 0 || pClient == IntPtr.Zero)
            {
                Marshal.Release(pEndpoint);
                return null;
            }

            var audioClient = (IAudioClient)Marshal.GetObjectForIUnknown(pClient);

            // الحصول على التنسيق الصوتي الممزوج وتهيئة العميل بالوضع المشترك (AUDCLNT_SHAREMODE_SHARED = 0)
            audioClient.GetMixFormat(out var pFormat);
            audioClient.Initialize(0 /* AUDCLNT_SHAREMODE_SHARED */, 0, 10_000_000 /* 1s buffer */, 0, pFormat, IntPtr.Zero);

            // بدء تدفق الصوت ليتمكن المقياس من قراءة العينات الحية
            audioClient.Start();

            // تفعيل واجهة مقياس الصوت IAudioMeterInformation
            var meterGuid = IidIAudioMeterInformation;
            int hrMeter = dev.Activate(ref meterGuid, 1 /* CLSCTX_INPROC_SERVER */, IntPtr.Zero, out var pMeter);
            if (hrMeter != 0 || pMeter == IntPtr.Zero)
            {
                // إيقاف العميل وتنظيف الموارد في حال فشل تنشيط واجهة المقياس
                audioClient.Stop();
                if (pFormat != IntPtr.Zero) Marshal.FreeCoTaskMem(pFormat);
                Marshal.Release(pClient);
                Marshal.Release(pEndpoint);
                return null;
            }

            var meter = (IAudioMeterInformation)Marshal.GetObjectForIUnknown(pMeter);

            // إرجاع كائن الجلسة الحية المحتوي على كافة المراجع لإدارتها وتحريرها لاحقاً
            return new LiveAudioSession(pEndpoint, pClient, audioClient, pFormat, pMeter, meter);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// جلسة صوتية مباشرة تحتفظ بمقابض COM وتتيح قراءة قمة مستوى الصوت (Peak Level) دورياً مع دعم IDisposable.
    /// </summary>
    public class LiveAudioSession : IDisposable
    {
        private readonly IntPtr _pEndpoint;
        private readonly IntPtr _pClient;
        private readonly IAudioClient _audioClient;
        private readonly IntPtr _pFormat;
        private readonly IntPtr _pMeter;
        private readonly IAudioMeterInformation _meter;
        private bool _isDisposed;

        /// <summary>
        /// تهيئة كائن الجلسة الصوتية الحية بالمقابض والمؤشرات غير المدارة.
        /// </summary>
        internal LiveAudioSession(
            IntPtr pEndpoint,
            IntPtr pClient,
            IAudioClient audioClient,
            IntPtr pFormat,
            IntPtr pMeter,
            IAudioMeterInformation meter)
        {
            _pEndpoint = pEndpoint;
            _pClient = pClient;
            _audioClient = audioClient;
            _pFormat = pFormat;
            _pMeter = pMeter;
            _meter = meter;
        }

        /// <summary>
        /// قراءة أعلى قيمة لحظية لمستوى الصوت (Peak Level) بين 0.0 و 1.0.
        /// </summary>
        /// <returns>قيمة مستوى الصوت محصورة بين 0.0 (صمت) و 1.0 (أعلى مستوى).</returns>
        public float GetPeakLevel()
        {
            // إذا تم تحرير الجلسة، نعيد صفراً فوراً
            if (_isDisposed) return 0f;
            try
            {
                // قراءة القيمة من واجهة مقياس الصوت في ويندوز
                _meter.GetPeakValue(out float peak);
                return Math.Clamp(peak, 0f, 1f);
            }
            catch
            {
                return 0f;
            }
        }

        /// <summary>
        /// تحرير جميع المقابض والموارد غير المدارة لواجهات COM وإيقاف عميل الصوت بأمان.
        /// </summary>
        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            // إيقاف عميل الصوت
            try { _audioClient.Stop(); } catch { }

            // تحرير ذاكرة بنية التنسيق الصوتي
            try { if (_pFormat != IntPtr.Zero) Marshal.FreeCoTaskMem(_pFormat); } catch { }

            // تحرير مؤشرات واجهات COM
            try { if (_pMeter != IntPtr.Zero) Marshal.Release(_pMeter); } catch { }
            try { if (_pClient != IntPtr.Zero) Marshal.Release(_pClient); } catch { }
            try { if (_pEndpoint != IntPtr.Zero) Marshal.Release(_pEndpoint); } catch { }

            GC.SuppressFinalize(this);
        }
    }

    // =========================================================================
    // تعريفات واجهات وبنى التوافق مع COM (Windows Core Audio API Interop)
    // =========================================================================

    /// <summary>
    /// كائن معدد أجهزة الوسائط MMDeviceEnumerator المعرف في نظام ويندوز.
    /// </summary>
    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    internal class MMDeviceEnumerator { }

    /// <summary>
    /// واجهة معدد أجهزة الوسائط للوصول لنقاط نهاية الصوت.
    /// </summary>
    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int dwStateMask, out IntPtr ppDevices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IntPtr ppEndpoint);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string pwstrId, out IntPtr ppDevice);
    }

    /// <summary>
    /// واجهة مجموعة أجهزة الصوت المسترجعة من المعدد.
    /// </summary>
    [ComImport]
    [Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint pcDevices);
        [PreserveSig] int Item(uint nDevice, out IntPtr ppDevice);
    }

    /// <summary>
    /// واجهة جهاز الصوت المفرد للتحكم وتنشيط الخدمات وقراءة الخصائص.
    /// </summary>
    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams, out IntPtr ppInterface);
        [PreserveSig] int OpenPropertyStore(int stgmAccess, out IntPtr ppProperties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);
        [PreserveSig] int GetState(out int pdwState);
    }

    /// <summary>
    /// واجهة مستودع الخصائص للوصول لبيانات تعريف الجهاز.
    /// </summary>
    [ComImport]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint cProps);
        [PreserveSig] int GetAt(uint iProp, out PropertyKey pkey);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant pv);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant propvar);
        [PreserveSig] int Commit();
    }

    /// <summary>
    /// بنية مفتاح الخاصية المكونة من المعرف الفريد ورقم المعرف الداخلي.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct PropertyKey
    {
        public Guid fmtid;
        public uint pid;
        public PropertyKey(Guid guid, uint id) { fmtid = guid; pid = id; }
    }

    /// <summary>
    /// بنية متغير الخاصية لفك تشفير قيم السلاسل النصية في COM.
    /// </summary>
    [StructLayout(LayoutKind.Explicit)]
    internal struct PropVariant
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(8)] public IntPtr pwszVal;
    }

    /// <summary>
    /// واجهة عميل الصوت الأساسية للتحكم في تدفق وبفر عينات الصوت.
    /// </summary>
    [ComImport]
    [Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioClient
    {
        [PreserveSig] int Initialize(int shareMode, int streamFlags, long hnsBufferDuration, long hnsPeriodicity, IntPtr pFormat, IntPtr audioSessionGuid);
        [PreserveSig] int GetBufferSize(out uint pNumBufferFrames);
        [PreserveSig] int GetStreamLatency(out long phnsLatency);
        [PreserveSig] int GetCurrentPadding(out uint pNumPaddingFrames);
        [PreserveSig] int IsFormatSupported(int shareMode, IntPtr pFormat, out IntPtr ppClosestMatch);
        [PreserveSig] int GetMixFormat(out IntPtr ppDeviceFormat);
        [PreserveSig] int GetDevicePeriod(out long phnsDefaultDevicePeriod, out long phnsMinimumDevicePeriod);
        [PreserveSig] int Start();
        [PreserveSig] int Stop();
        [PreserveSig] int Reset();
        [PreserveSig] int SetEventHandle(IntPtr eventHandle);
        [PreserveSig] int GetService(ref Guid riid, out IntPtr ppv);
    }

    /// <summary>
    /// واجهة مقياس ذروة الصوت لقراءة مستوى الصوت اللحظي.
    /// </summary>
    [ComImport]
    [Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioMeterInformation
    {
        [PreserveSig] int GetPeakValue(out float pfPeak);
        [PreserveSig] int GetMeteringChannelCount(out int pnChannelCount);
        [PreserveSig] int GetChannelsPeakValues(int u32ChannelCount, [In, Out] float[] afPeakValues);
        [PreserveSig] int QueryHardwareSupport(out int pdwHardwareSupportMask);
    }
}
