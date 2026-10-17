using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace ActivityMonitor.Agent.Interop;

/// <summary>
/// فئة مسؤولة عن استدعاء واجهات برمجة تطبيقات نظام تشغيل ويندوز الأصلية (Win32 P/Invoke APIs)،
/// للتعامل مع النوافذ، استخراج العناوين، قياس وقت الخمول، ربط أسطح المكتب، وخطافات الأحداث.
/// </summary>
public static class NativeMethods
{
    // ==========================================
    // ثوابت أحداث نظام ويندوز (WinEvents Constants)
    // ==========================================

    /// <summary>
    /// حدث تغير النافذة النشطة في مقدمة الشاشة (Foreground Window Changed).
    /// </summary>
    public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;

    /// <summary>
    /// حدث تغير اسم أو عنوان كائن نافذة (Window Name Changed).
    /// </summary>
    public const uint EVENT_OBJECT_NAMECHANGE = 0x800C;

    /// <summary>
    /// استقبال الأحداث خارج سياق الخيط الذي أنشأها (Out of context).
    /// </summary>
    public const uint WINEVENT_OUTOFCONTEXT = 0;

    /// <summary>
    /// تخطي الأحداث الناتجة عن العملية الحالية نفسها لتجنب الحلقات اللانهائية.
    /// </summary>
    public const uint WINEVENT_SKIPOWNPROCESS = 2;

    /// <summary>
    /// رسالة إنهاء حلقة الرسائل (WM_QUIT).
    /// </summary>
    public const uint WM_QUIT = 0x0012;

    /// <summary>
    /// فحص الرسائل في الطابور دون إزالتها (PM_NOREMOVE).
    /// </summary>
    public const uint PM_NOREMOVE = 0x0000;

    /// <summary>
    /// معرّف كائن النافذة القياسي في أحداث نظام ويندوز.
    /// </summary>
    public const int OBJID_WINDOW = 0;

    /// <summary>
    /// هيكل بيانات يمثل إحداثيات نقطة ثنائية الأبعاد (X, Y).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        /// <summary>
        /// الإحداثي الأفقي X.
        /// </summary>
        public int X;

        /// <summary>
        /// الإحداثي الرأسي Y.
        /// </summary>
        public int Y;
    }

    /// <summary>
    /// هيكل بيانات يمثل رسالة ويندوز في طابور الرسائل (Windows Message).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        /// <summary>
        /// مقبض النافذة المستهدفة بالرسالة.
        /// </summary>
        public IntPtr HWnd;

        /// <summary>
        /// معرّف الرسالة.
        /// </summary>
        public uint Message;

        /// <summary>
        /// المعامل الإضافي الأول للرسالة (WParam).
        /// </summary>
        public UIntPtr WParam;

        /// <summary>
        /// المعامل الإضافي الثاني للرسالة (LParam).
        /// </summary>
        public IntPtr LParam;

        /// <summary>
        /// الوقت الزمني الذي أُرسلت فيه الرسالة.
        /// </summary>
        public uint Time;

        /// <summary>
        /// موقع مؤشر الفأرة عند إرسال الرسالة.
        /// </summary>
        public POINT Point;

        /// <summary>
        /// حقل خاص محجوز للنظام.
        /// </summary>
        public uint Private;
    }

    /// <summary>
    /// دالة رد النداء (Delegate) المستدعاة عند وقوع حدث من أحداث خطافات ويندوز (WinEvents).
    /// </summary>
    public delegate void WinEventDelegate(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime);

    /// <summary>
    /// الحصول على مقبض النافذة التي تركز عليها واجهة المستخدم في المقدمة حالياً.
    /// </summary>
    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    /// <summary>
    /// الحصول على معرّف العملية (Process ID) وخيط المعالجة التابع لهما مقبض نافذة معين.
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    /// <summary>
    /// قراءة نص عنوان شريط النافذة بنظام Unicode.
    /// </summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    /// <summary>
    /// الحصول على طول نص عنوان النافذة بالأحرف.
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetWindowTextLength(IntPtr hWnd);

    /// <summary>
    /// هيكل بيانات يحتوي على وقت آخر إدخال حدث من المستخدم في النظام.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct LASTINPUTINFO
    {
        /// <summary>
        /// حجم الهيكل بالبايت.
        /// </summary>
        public uint cbSize;

        /// <summary>
        /// الوقت الزمني (Tick Count) لآخر إدخال مسجل.
        /// </summary>
        public uint dwTime;
    }

    /// <summary>
    /// استعلام النظام عن توقيت آخر إدخال صدر من المستخدم عبر الفأرة أو لوحة المفاتيح.
    /// </summary>
    [DllImport("user32.dll")]
    public static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    /// <summary>
    /// فتح مقبض لسطح مكتب محدد بالاسم (مثل default).
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr OpenDesktop(string lpszDesktop, uint dwFlags, bool fInherit, uint dwDesiredAccess);

    /// <summary>
    /// فتح مقبض لسطح مكتب الإدخال التفاعلي النشط حالياً.
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr OpenInputDesktop(uint dwFlags, bool fInherit, uint dwDesiredAccess);

    /// <summary>
    /// ربط الخيط الحالي بسطح مكتب معين ليتمكن من استدعاء دوال الواجهة الرسومية له.
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetThreadDesktop(IntPtr hDesktop);

    /// <summary>
    /// إغلاق وتحرير مقبض سطح المكتب.
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool CloseDesktop(IntPtr hDesktop);

    /// <summary>
    /// استرجاع مقبض سطح المكتب المرتبط بخيط معالجة محدد.
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr GetThreadDesktop(uint dwThreadId);

    /// <summary>
    /// الحصول على المعرّف الرقمي للخيط الحالي الجاري تنفيذه.
    /// </summary>
    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    /// <summary>
    /// صلاحيات الوصول الكامل لسطح المكتب في ويندوز.
    /// </summary>
    public const uint DESKTOP_ALL_ACCESS = 0x01FF;

    /// <summary>
    /// تثبيت خطاف للاستماع لأحداث نظام ويندوز ضمن نطاق معين.
    /// </summary>
    [DllImport("user32.dll")]
    public static extern IntPtr SetWinEventHook(
        uint eventMin,
        uint eventMax,
        IntPtr hmodWinEventProc,
        WinEventDelegate lpfnWinEventProc,
        uint idProcess,
        uint idThread,
        uint dwFlags);

    /// <summary>
    /// إزالة وإلغاء تثبيت خطاف أحداث ويندوز.
    /// </summary>
    [DllImport("user32.dll")]
    public static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    /// <summary>
    /// سحب الرسالة التالية من طابور رسائل الخيط (دالة حاجزة حتى وصول رسالة).
    /// </summary>
    [DllImport("user32.dll")]
    public static extern int GetMessage(out MSG message, IntPtr window, uint filterMin, uint filterMax);

    /// <summary>
    /// ترجمة ضغطات المفاتيح إلى رسائل أحرف.
    /// </summary>
    [DllImport("user32.dll")]
    public static extern bool TranslateMessage(in MSG message);

    /// <summary>
    /// توجيه وإرسال الرسالة إلى معالج إجراءات النافذة الخاص بها (Window Procedure).
    /// </summary>
    [DllImport("user32.dll")]
    public static extern IntPtr DispatchMessage(in MSG message);

    /// <summary>
    /// إرسال رسالة إلى طابور رسائل خيط معالجة محدد بواسطة معرّفه.
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool PostThreadMessage(uint threadId, uint message, UIntPtr wParam, IntPtr lParam);

    /// <summary>
    /// فحص طابور الرسائل دون حظر الخيط لمعرفة ما إذا كانت هناك رسائل تنتظر المعالجة.
    /// </summary>
    [DllImport("user32.dll")]
    public static extern bool PeekMessage(out MSG message, IntPtr window, uint filterMin, uint filterMax, uint removeMessage);

    /// <summary>
    /// فتح مقبض لعملية معينة بصلاحيات محددة.
    /// </summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, uint processId);

    /// <summary>
    /// الاستعلام عن المسار الكامل للملف التنفيذي للعملية عبر مقبضها المفتوح.
    /// </summary>
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(IntPtr hProcess, int flags, StringBuilder lpExeName, ref int lpdwSize);

    /// <summary>
    /// إغلاق مقبض نظام مفتوح (Kernel Object Handle).
    /// </summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    /// <summary>
    /// صلاحية قراءة المعلومات الأساسية والمحدودة عن العملية (تعمل حتى مع العمليات ذات الصلاحيات المرتفعة).
    /// </summary>
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    /// <summary>
    /// الحصول على عدد المللي ثواني التي مرت منذ بدء تشغيل نظام التشغيل.
    /// </summary>
    [DllImport("kernel32.dll")]
    public static extern uint GetTickCount();

    /// <summary>
    /// التحقق مما إذا كان تطبيق النافذة في حالة عدم استجابة (Hung / Not Responding).
    /// </summary>
    [DllImport("user32.dll")]
    public static extern bool IsHungAppWindow(IntPtr hWnd);

    /// <summary>
    /// إرسال رسالة إلى نافذة مع تحديد مهلة زمنية للانتظار لتجنب تجميد التطبيق.
    /// </summary>
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern IntPtr SendMessageTimeout(
        IntPtr hWnd,
        uint msg,
        UIntPtr wParam,
        StringBuilder lParam,
        uint fuFlags,
        uint uTimeout,
        out UIntPtr lpdwResult);

    /// <summary>
    /// رسالة طلب استخراج نص النافذة (WM_GETTEXT).
    /// </summary>
    public const uint WM_GETTEXT = 0x000D;

    /// <summary>
    /// إلغاء الطلب فوراً إذا كانت النافذة غير مستجيبة لمنع حظر الخيط (SMTO_ABORTIFHUNG).
    /// </summary>
    public const uint SMTO_ABORTIFHUNG = 0x0002;

    /// <summary>
    /// استخراج عنوان شريط النافذة بأمان، مع التعامل مع حالات عدم الاستجابة وتطبيق مهلة زمنية سريعة.
    /// </summary>
    /// <param name="hWnd">مقبض النافذة المراد قراءة عنوانها.</param>
    /// <returns>عنوان النافذة أو "Not Responding" في حالة التجميد، أو نص فارغ.</returns>
    public static string GetWindowTitle(IntPtr hWnd)
    {
        // التحقق من صلاحية المقبض
        if (hWnd == IntPtr.Zero) return string.Empty;

        try
        {
            // إذا كانت النافذة معلقة ولا تستجيب نتجنب مراسلتها لتفادي تجميد الخيط
            if (IsHungAppWindow(hWnd))
            {
                return "Not Responding";
            }

            // محاولة جلب العنوان عبر SendMessageTimeout بمهلة 250 ميلي ثانية
            var sb = new StringBuilder(512);
            var result = SendMessageTimeout(hWnd, WM_GETTEXT, (UIntPtr)sb.Capacity, sb, SMTO_ABORTIFHUNG, 250, out _);
            if (result != IntPtr.Zero && sb.Length > 0)
            {
                return sb.ToString();
            }

            // حل بديل باستخدام GetWindowText القياسية
            int length = GetWindowTextLength(hWnd);
            if (length > 0)
            {
                var fallbackSb = new StringBuilder(Math.Min(length + 1, 512));
                if (GetWindowText(hWnd, fallbackSb, fallbackSb.Capacity) > 0)
                {
                    return fallbackSb.ToString();
                }
            }
        }
        catch
        {
            // في حال حدوث أي استثناء نرجع نصاً فارغاً بأمان
        }

        return string.Empty;
    }

    /// <summary>
    /// قياس فترة خمول المستخدم (المدة المنقضية منذ آخر تفاعل مع الفأرة أو لوحة المفاتيح).
    /// </summary>
    /// <returns>الفترة الزمنية للخمول كـ TimeSpan.</returns>
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetUserObjectInformation(IntPtr handle, int index, StringBuilder information, uint length, out uint needed);

    public static bool IsInteractiveDesktopAvailable()
    {
        var desktop = OpenInputDesktop(0, false, 0x0001);
        if (desktop == IntPtr.Zero) return false;
        try
        {
            var name = new StringBuilder(256);
            return GetUserObjectInformation(desktop, 2, name, (uint)(name.Capacity * sizeof(char)), out _) &&
                   string.Equals(name.ToString(), "Default", StringComparison.OrdinalIgnoreCase);
        }
        finally { CloseDesktop(desktop); }
    }

    public static TimeSpan GetIdleTime()
    {
        // تجهيز هيكل بيانات آخر إدخال
        var lastInput = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };

        // الاستعلام من النظام عن وقت آخر إدخال
        if (GetLastInputInfo(ref lastInput))
        {
            // حساب الفرق بين وقت النظام الحالي ووقت آخر إدخال
            uint tickCount = GetTickCount();
            uint idleTicks = unchecked(tickCount - lastInput.dwTime);

            // فحص أمان لتجنب أي طفح رقمي غير منطقي (أكثر من 14 يوماً)
            if (idleTicks <= 14 * 24 * 60 * 60 * 1000)
            {
                return TimeSpan.FromMilliseconds(idleTicks);
            }
        }

        return TimeSpan.Zero;
    }

    /// <summary>
    /// استخراج المسار الكامل لملف البرنامج التنفيذي للعملية عبر معرّفها (PID).
    /// </summary>
    /// <param name="processId">معرّف العملية في النظام.</param>
    /// <returns>المسار الكامل للملف التنفيذي أو null في حال تعذر الوصول.</returns>
    public static string? GetProcessExecutablePath(int processId)
    {
        if (processId <= 0) return null;

        // الطريقة الأولى: فتح العملية بصلاحية الاستعلام المحدودة (تعمل حتى مع برامج المدير والمحمية)
        IntPtr hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)processId);
        if (hProcess != IntPtr.Zero)
        {
            try
            {
                var sb = new StringBuilder(1024);
                int size = sb.Capacity;
                if (QueryFullProcessImageName(hProcess, 0, sb, ref size))
                {
                    return sb.ToString();
                }
            }
            finally
            {
                // إغلاق مقبض العملية فور الانتهاء لمنع تسريب الموارد
                CloseHandle(hProcess);
            }
        }

        // الطريقة الثانية البديلة: استخدام فئة Process في دوت نت
        try
        {
            using var proc = Process.GetProcessById(processId);
            return proc.MainModule?.FileName;
        }
        catch
        {
            // في حال رفض الصلاحيات أو انتهاء العملية نرجع null
            return null;
        }
    }
}
