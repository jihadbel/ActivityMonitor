using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using System.Threading.Tasks;

namespace ActivityMonitor.Viewer.Services;

/// <summary>
/// حالات خدمة ويندوز المختلفة (Windows Service State).
/// </summary>
public enum ServiceState
{
    /// <summary>
    /// الخدمة تعمل بنشاط.
    /// </summary>
    Running,

    /// <summary>
    /// الخدمة متوقفة تماماً.
    /// </summary>
    Stopped,

    /// <summary>
    /// الخدمة في مرحلة انتقالية (جاري البدء أو جاري الإيقاف).
    /// </summary>
    Transitioning,

    /// <summary>
    /// الخدمة غير مثبتة في النظام.
    /// </summary>
    NotInstalled
}

/// <summary>
/// إدارة خدمات وعمليات نظام ويندوز لتطبيق مراقبة النشاط (ActivityMonitor).
/// يتولى فحص وتشغيل وإيقاف خدمة ويندوز الأساسية (Session 0) وعميل واجهة المستخدم (Agent)، بالإضافة لإدارة الإقلاع التلقائي عبر Registry و Task Scheduler.
/// </summary>
public class WindowsServiceManager
{
    /// <summary>
    /// الأسماء المحتملة لخدمة ويندوز في النظام (قد تسمى بالاسم الكامل أو المختصر).
    /// </summary>
    public static readonly string[] PossibleServiceNames = new[] { "ActivityMonitor.Service", "ActivityMonitorService" };

    /// <summary>
    /// اسم عملية عميل مراقبة النشاط (بدون امتداد .exe).
    /// </summary>
    public const string AgentProcessName = "ActivityMonitor.Agent";

    /// <summary>
    /// البحث عن الاسم الفعلي لخدمة ويندوز المثبتة على الجهاز من بين الأسماء المحتملة.
    /// </summary>
    /// <returns>اسم الخدمة النشط أو الاسم الافتراضي الأول في حال عدم التثبيت.</returns>
    public string GetActiveServiceName()
    {
        // المرور على الأسماء المحتملة لفحص وجود أي منها في النظام
        foreach (var name in PossibleServiceNames)
        {
            try
            {
                using var sc = new ServiceController(name);
                // محاولة قراءة الحالة للتحقق من وجود الخدمة فعلياً
                var _ = sc.Status;
                return name;
            }
            catch
            {
                // إذا رمى استثناء فالخدمة بهذا الاسم غير مسجلة، ننتقل للاسم التالي
            }
        }

        // إرجاع الاسم الافتراضي الأول في حال لم يُعثر على خدمة مسجلة
        return PossibleServiceNames[0];
    }

    /// <summary>
    /// فحص الحالة التشغيلية الحالية لخدمة ويندوز (تعمل، متوقفة، قيد الانتقال، أو غير مثبتة).
    /// </summary>
    /// <returns>حالة الخدمة كقيمة من التعداد <see cref="ServiceState"/>.</returns>
    public ServiceState GetServiceState()
    {
        foreach (var name in PossibleServiceNames)
        {
            try
            {
                using var sc = new ServiceController(name);
                return sc.Status switch
                {
                    ServiceControllerStatus.Running => ServiceState.Running,
                    ServiceControllerStatus.Stopped => ServiceState.Stopped,
                    ServiceControllerStatus.StartPending or ServiceControllerStatus.StopPending => ServiceState.Transitioning,
                    _ => ServiceState.Stopped
                };
            }
            catch
            {
                // الخدمة غير موجودة بهذا الاسم، استمرار الفحص
            }
        }

        // في حال فشل فحص كافة الأسماء، تُعتبر الخدمة غير مثبتة
        return ServiceState.NotInstalled;
    }

    /// <summary>
    /// التحقق مما إذا كانت عملية العميل (ActivityMonitor.Agent) قيد التشغيل في جلسة المستخدم الحالية.
    /// </summary>
    /// <returns>صحيح (true) إذا كان العميل نشطاً في جلسة سطح المكتب الحالية؛ وإلا خطأ (false).</returns>
    public bool IsAgentRunning()
    {
        try
        {
            // جلب العمليات الخاصة بجلسة المستخدم الحالية حصراً
            var processes = GetAgentProcessesForCurrentSession();
            try
            {
                // إذا وُجدت عملية واحدة على الأقل فالعميل نشط
                return processes.Length > 0;
            }
            finally
            {
                // تحرير موارد العمليات المسترجعة لتفادي تسريب المقابض
                foreach (var process in processes) process.Dispose();
            }
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// تبديل حالة خدمة ويندوز (تشغيلها إذا كانت متوقفة، أو إيقافها إذا كانت تعمل).
    /// </summary>
    /// <returns>رسالة توضح نتيجة تنفيذ العملية.</returns>
    public async Task<string> ToggleServiceAsync()
    {
        var state = GetServiceState();
        if (state == ServiceState.Running)
        {
            return await StopServiceAsync();
        }
        else if (state == ServiceState.Stopped)
        {
            return await StartServiceAsync();
        }
        else if (state == ServiceState.NotInstalled)
        {
            return "Windows Service is not installed. Use deploy\\Install.ps1 to install it.";
        }
        return "Service is busy transitioning.";
    }

    /// <summary>
    /// تشغيل خدمة ويندوز مع المحاولة عبر ServiceController أولاً، واللجوء لأمر net start بصلاحيات المسؤول عند الحاجة.
    /// </summary>
    /// <returns>رسالة توضح نتيجة بدء تشغيل الخدمة.</returns>
    public async Task<string> StartServiceAsync()
    {
        return await Task.Run(() =>
        {
            var serviceName = GetActiveServiceName();
            try
            {
                // محاولة التشغيل المباشر عبر متحكم الخدمات
                using var sc = new ServiceController(serviceName);
                if (sc.Status == ServiceControllerStatus.Running)
                {
                    return "Service is already running.";
                }

                sc.Start();
                sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(10));
                return "Service started successfully.";
            }
            catch (Exception ex)
            {
                // في حال نقص الصلاحيات، محاولة الاستدعاء عبر أمر النظام بصلاحيات إدارية مرتفعة (UAC RunAs)
                try
                {
                    var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "net.exe"), $"start \"{serviceName}\"")
                    {
                        Verb = "runas",
                        UseShellExecute = true,
                        CreateNoWindow = true
                    };
                    var p = Process.Start(psi);
                    if (p == null || !p.WaitForExit(15000) || p.ExitCode != 0 || GetServiceState() != ServiceState.Running)
                    {
                        return "Failed to start service: elevated command did not complete successfully.";
                    }
                    return "Service started successfully.";
                }
                catch
                {
                    return $"Failed to start service: {ex.Message}";
                }
            }
        });
    }

    /// <summary>
    /// إيقاف خدمة ويندوز مع المحاولة عبر ServiceController أولاً، واللجوء لأمر net stop المرتفع عند الحاجة.
    /// </summary>
    /// <returns>رسالة توضح نتيجة إيقاف الخدمة.</returns>
    public async Task<string> StopServiceAsync()
    {
        return await Task.Run(() =>
        {
            var serviceName = GetActiveServiceName();
            try
            {
                // محاولة الإيقاف المباشر
                using var sc = new ServiceController(serviceName);
                if (sc.Status == ServiceControllerStatus.Stopped)
                {
                    return "Service is already stopped.";
                }

                sc.Stop();
                sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
                return "Service stopped successfully.";
            }
            catch (Exception ex)
            {
                // محاولة الإيقاف المرتفع عبر الأمر الإداري
                try
                {
                    var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "net.exe"), $"stop \"{serviceName}\"")
                    {
                        Verb = "runas",
                        UseShellExecute = true,
                        CreateNoWindow = true
                    };
                    var p = Process.Start(psi);
                    if (p == null || !p.WaitForExit(15000) || p.ExitCode != 0 || GetServiceState() != ServiceState.Stopped)
                    {
                        return "Failed to stop service: elevated command did not complete successfully.";
                    }
                    return "Service stopped successfully.";
                }
                catch
                {
                    return $"Failed to stop service: {ex.Message}";
                }
            }
        });
    }

    /// <summary>
    /// تبديل حالة عميل المراقبة (تشغيله في الخلفية بهدوء أو إيقافه بسلاسة لإنهاء ملفات الفيديو بأمان).
    /// </summary>
    /// <param name="agentExePath">المسار المخصص للملف التنفيذي للعميل (اختياري).</param>
    /// <returns>رسالة نصية توضح نتيجة العملية.</returns>
    public async Task<string> ToggleAgentAsync(string? agentExePath = null)
    {
        return await Task.Run(() =>
        {
            // إذا كان العميل قيد التشغيل، نبدأ إجراءات الإيقاف السلس
            if (IsAgentRunning())
            {
                try
                {
                    // 1. محاولة الإيقاف السلس بإرسال إشارة Named Event لتفريغ وإنهاء تسجيل MP4 بشكل سليم
                    var sessionId = Process.GetCurrentProcess().SessionId;
                    var shutdownEventNameLocal = $"Local\\ActivityMonitor_Agent_Shutdown_{Environment.UserName}_{sessionId}";
                    var shutdownEventNameGlobal = $"Global\\ActivityMonitor_Agent_Shutdown_{Environment.UserName}_{sessionId}";
                    try
                    {
                        if (EventWaitHandle.TryOpenExisting(shutdownEventNameLocal, out var shutdownEvent) ||
                            EventWaitHandle.TryOpenExisting(shutdownEventNameGlobal, out shutdownEvent))
                        {
                            using (shutdownEvent)
                            {
                                // إطلاق حدث الإيقاف لإعلام العميل
                                shutdownEvent.Set();
                            }

                            // إعطاء العميل مهلة كافية (حتى 20 ثانية) لإغلاق مسجل الفيديو وحفظ بيانات SQLite
                            for (int i = 0; i < 600; i++)
                            {
                                if (!IsAgentRunning()) break;
                                System.Threading.Thread.Sleep(100);
                            }
                        }
                    }
                    catch { }

                    // 2. خطوة احتياطية: إنهاء أي عمليات متبقية قسرياً في حال عدم الاستجابة للإشارة
                    var procs = GetAgentProcessesForCurrentSession();
                    var forcedTermination = procs.Length > 0;
                    foreach (var p in procs)
                    {
                        p.Dispose();
                    }
                    return forcedTermination
                        ? "Failed to stop Agent within 60 seconds. It was left running to protect the current recording; inspect its log."
                        : "ActivityMonitor Agent stopped cleanly.";
                }
                catch (Exception ex)
                {
                    return $"Failed to stop Agent: {ex.Message}";
                }
            }
            else
            {
                // إذا كان متوقفاً، نبدأ تشغيله بالوضع الصامت المخفي (--silent)
                try
                {
                    string target = agentExePath ?? FindAgentExePath();
                    if (File.Exists(target))
                    {
                        Process.Start(new ProcessStartInfo(target)
                        {
                            UseShellExecute = true,
                            WindowStyle = ProcessWindowStyle.Hidden,
                            Arguments = "--silent"
                        });
                        return "ActivityMonitor Agent started.";
                    }
                    return "ActivityMonitor.Agent.exe not found.";
                }
                catch (Exception ex)
                {
                    return $"Failed to start Agent: {ex.Message}";
                }
            }
        });
    }

    /// <summary>
    /// إعادة تشغيل عميل المراقبة (إيقافه مؤقتاً ثم إعادة إطلاقه).
    /// </summary>
    /// <returns>رسالة توضح نتيجة إعادة التشغيل.</returns>
    public async Task<string> RestartAgentAsync()
    {
        return await Task.Run(async () =>
        {
            if (IsAgentRunning())
            {
                var stopResult = await ToggleAgentAsync();
                await Task.Delay(1500);
                if (IsAgentRunning())
                {
                    return $"Failed to restart Agent because it is still running. {stopResult}";
                }
            }
            return await ToggleAgentAsync();
        });
    }

    /// <summary>
    /// استرجاع مصفوفة عمليات العميل النشطة الخاصة برقم جلسة تسجيل الدخول الحالية فقط.
    /// </summary>
    /// <returns>مصفوفة بالعمليات المطابقة لجلسة العمل الحالية.</returns>
    private static Process[] GetAgentProcessesForCurrentSession()
    {
        // استخراج معرّف الجلسة لعملية العارض الحالية
        var currentSessionId = Process.GetCurrentProcess().SessionId;

        // فلترة العمليات بالاسم ومعرّف الجلسة
        return Process.GetProcessesByName(AgentProcessName)
            .Where(process =>
            {
                try
                {
                    if (process.SessionId == currentSessionId) return true;
                    process.Dispose(); return false;
                }
                catch
                {
                    process.Dispose();
                    return false;
                }
            })
            .ToArray();
    }

    /// <summary>
    /// البحث الذكي عن مسار الملف التنفيذي للعميل (ActivityMonitor.Agent.exe) في المواقع المحتملة.
    /// </summary>
    /// <returns>المسار الكامل للملف التنفيذي إن وُجد؛ أو أول مسار مرشح.</returns>
    public string FindAgentExePath()
    {
        // قائمة المواقع المحتملة للعميل في بيئة التطوير والإنتاج والتثبيت النهائي
        string[] candidates = new[]
        {
            Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "Agent", "ActivityMonitor.Agent.exe")),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Agent", "ActivityMonitor.Agent.exe"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ActivityMonitor.Agent.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ActivityMonitor", "Agent", "ActivityMonitor.Agent.exe"),
            Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\deploy\dist\Agent\ActivityMonitor.Agent.exe")),
            Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\ActivityMonitor.Agent\bin\Debug\net10.0-windows\ActivityMonitor.Agent.exe"))
        };

        // فحص وجود الملف في كل مسار مرشح
        foreach (var path in candidates)
        {
            if (File.Exists(path)) return path;
        }

        // إرجاع المسار الأول في حال تعذر العثور على أي ملف موجود
        return candidates[0];
    }

    /// <summary>
    /// فحص ما إذا كانت خدمة ويندوز مهيأة للبدء التلقائي (Automatic) مع إقلاع النظام.
    /// </summary>
    /// <returns>صحيح (true) إذا كان نوع البدء Automatic؛ وإلا خطأ (false).</returns>
    public bool IsServiceAutoStart()
    {
        try
        {
            var serviceName = GetActiveServiceName();
            using var sc = new ServiceController(serviceName);
            return sc.StartType == ServiceStartMode.Automatic;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// ضبط وضع بدء تشغيل خدمة ويندوز (تلقائي auto أو يدوي demand) باستخدام أداة sc.exe بصلاحيات المسؤول.
    /// </summary>
    /// <param name="automatic">صحيح للبدء التلقائي، أو خطأ للبدء اليدوي.</param>
    /// <returns>رسالة توضح نتيجة التعديل.</returns>
    public async Task<string> SetServiceAutoStartAsync(bool automatic)
    {
        return await Task.Run(() =>
        {
            try
            {
                var serviceName = GetActiveServiceName();
                var mode = automatic ? "auto" : "demand";

                // استدعاء أداة تكوين الخدمات sc.exe بصلاحيات إدارية مرتفعة
                var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "sc.exe"), $"config \"{serviceName}\" start= {mode}")
                {
                    Verb = "runas",
                    UseShellExecute = true,
                    CreateNoWindow = true
                };
                var p = Process.Start(psi);
                if (p == null || !p.WaitForExit(10000))
                {
                    return "فشل تعديل إعداد بدء الخدمة: انتهت مهلة انتظار sc.exe.";
                }
                if (p.ExitCode != 0)
                {
                    return $"فشل تعديل إعداد بدء الخدمة: sc.exe أعاد الرمز {p.ExitCode}.";
                }
                return automatic ? "تم ضبط الخدمة لتبدأ تلقائياً مع تشغيل ويندوز." : "تم ضبط الخدمة للتشغيل اليدوي.";
            }
            catch (Exception ex)
            {
                return $"فشل تعديل إعداد بدء الخدمة: {ex.Message}";
            }
        });
    }

    /// <summary>
    /// التحقق مما إذا كان عميل المراقبة مسجلاً لبدء التشغيل التلقائي عند تسجيل الدخول (عبر Registry أو Task Scheduler).
    /// </summary>
    /// <returns>صحيح (true) إذا وُجد تسجيل فعال؛ وإلا خطأ (false).</returns>
    public bool IsAgentAutoStart()
    { return ActivityMonitor.Infrastructure.Services.InteractiveStartup.IsEnabled(); }

    /// <summary>
    /// تفعيل أو تعطيل بدء تشغيل العميل تلقائياً عند تسجيل الدخول عبر المهام المجدولة المرتفعة أو مفتاح Run في Registry كبديل.
    /// </summary>
    /// <param name="enable">صحيح للتفعيل؛ أو خطأ للإلغاء.</param>
    /// <returns>رسالة توضح نتيجة الإجراء.</returns>
    public Task<string> SetAgentAutoStartAsync(bool enable)
    {
        var ok = ActivityMonitor.Infrastructure.Services.InteractiveStartup.SetEnabled(enable, FindAgentExePath());
        return Task.FromResult(ok ? (enable ? "تم تفعيل بدء العميل عند دخول المستخدم." : "تم إلغاء بدء العميل لهذا المستخدم.")
            : "فشل تعديل إعداد بدء تشغيل العميل.");
    }
}
