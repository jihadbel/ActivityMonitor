using System;

namespace ActivityMonitor.Core.Models;

/// <summary>
/// فئة مساعدة تحتوي على دوال حسابية وتنسيقية رياضية للخط الزمني للأنشطة والتداخلات وحساب فترات الخمول.
/// </summary>
public static class ActivityTimelineMath
{
    public static string FormatPlaybackTime(TimeSpan value) => value.TotalHours >= 1
        ? $"{(int)value.TotalHours:D2}:{value.Minutes:D2}:{value.Seconds:D2}"
        : value.ToString(@"mm\:ss");
    /// <summary>
    /// تحويل تاريخ محدد إلى نطاق زمني كامل يبدأ من بداية اليوم وحتى نهايته بالتوقيت العالمي المنسق (UTC).
    /// </summary>
    /// <param name="date">التاريخ المراد تحويله.</param>
    /// <returns>زوج يمثل بداية اليوم ونهايته غير الشاملة (StartUtc, EndUtcExclusive).</returns>
    public static (DateTime StartUtc, DateTime EndUtcExclusive) GetUtcDayRange(DateTime date)
    {
        // إذا كان التاريخ بالفعل محدد كـ UTC نستخدم بدايته مباشرة
        if (date.Kind == DateTimeKind.Utc)
        {
            var utcStart = date.Date;
            return (utcStart, utcStart.AddDays(1));
        }

        // إذا كان بالتوقيت المحلي، نقوم بتحديده وتحويله إلى UTC بدقة
        var localStart = DateTime.SpecifyKind(date.Date, DateTimeKind.Unspecified);
        var localEnd = localStart.AddDays(1);
        return (
            TimeZoneInfo.ConvertTimeToUtc(localStart, TimeZoneInfo.Local),
            TimeZoneInfo.ConvertTimeToUtc(localEnd, TimeZoneInfo.Local));
    }

    /// <summary>
    /// حساب الوقت الدقيق لبداية فترة الخمول بطرح مدة الخمول المرصودة من وقت الملاحظة الحالي.
    /// </summary>
    /// <param name="observedAtUtc">وقت رصد حالة الخمول (UTC).</param>
    /// <param name="idleTime">المدة الزمنية للخمول المقاسة من النظام.</param>
    /// <returns>التوقيت الفعلي لبداية الخمول (UTC).</returns>
    public static DateTime CalculateIdleStartUtc(DateTime observedAtUtc, TimeSpan idleTime)
    {
        // حماية ضد أي قيم سالبة ناتجة عن خلل في قراءة المؤقتات
        var safeIdleTime = idleTime < TimeSpan.Zero ? TimeSpan.Zero : idleTime;
        return observedAtUtc - safeIdleTime;
    }

    /// <summary>
    /// ضمان ألا يسبق وقت الانتهاء وقت البدء مطلقاً لتجنب الفترات الزمنية السالبة.
    /// </summary>
    /// <param name="startedAtUtc">وقت بدء النشاط (UTC).</param>
    /// <param name="requestedEndUtc">وقت الانتهاء المطلوب تعيينه (UTC).</param>
    /// <returns>التوقيت المضبوط الذي لا يقل عن وقت البدء.</returns>
    public static DateTime ClampEndUtc(DateTime startedAtUtc, DateTime requestedEndUtc) =>
        requestedEndUtc < startedAtUtc ? startedAtUtc : requestedEndUtc;

    /// <summary>
    /// حساب المدة بالثواني بين وقت البدء والانتهاء مع ضمان عدم إرجاع قيمة سالبة.
    /// </summary>
    /// <param name="startedAtUtc">وقت البدء (UTC).</param>
    /// <param name="endedAtUtc">وقت الانتهاء (UTC).</param>
    /// <returns>المدة الإجمالية بالثواني (صفر أو أكثر).</returns>
    public static double CalculateDurationSeconds(DateTime startedAtUtc, DateTime endedAtUtc) =>
        Math.Max(0, (ClampEndUtc(startedAtUtc, endedAtUtc) - startedAtUtc).TotalSeconds);

    /// <summary>
    /// حساب مدة التداخل الزمني المشترك بالثواني بين حدث معين ونطاق زمني مستهدف.
    /// </summary>
    /// <param name="eventStartUtc">وقت بدء الحدث (UTC).</param>
    /// <param name="eventEndUtc">وقت انتهاء الحدث إن وجد (UTC).</param>
    /// <param name="recordedDurationSeconds">المدة المسجلة للحدث بالثواني.</param>
    /// <param name="rangeStartUtc">بداية النطاق الزمني المستهدف (UTC).</param>
    /// <param name="rangeEndUtcExclusive">نهاية النطاق الزمني المستهدف غير الشاملة (UTC).</param>
    /// <returns>عدد ثواني التداخل المشتركة بين الحدث والنطاق.</returns>
    public static double CalculateOverlapSeconds(
        DateTime eventStartUtc,
        DateTime? eventEndUtc,
        double recordedDurationSeconds,
        DateTime rangeStartUtc,
        DateTime rangeEndUtcExclusive)
    {
        // استنتاج وقت نهاية الحدث: إما من الخاصية أو بإضافة المدة المحسوبة لوقت البدء
        var resolvedEventEnd = eventEndUtc ?? eventStartUtc.AddSeconds(Math.Max(0, recordedDurationSeconds));

        // حساب بداية التداخل (الأحدث بين بداية الحدث وبداية النطاق)
        var overlapStart = eventStartUtc > rangeStartUtc ? eventStartUtc : rangeStartUtc;

        // حساب نهاية التداخل (الأقدم بين نهاية الحدث ونهاية النطاق)
        var overlapEnd = resolvedEventEnd < rangeEndUtcExclusive ? resolvedEventEnd : rangeEndUtcExclusive;

        // إذا كانت النهاية قبل البداية فلا يوجد تداخل، وإلا نحسب الفرق بالثواني
        return overlapEnd <= overlapStart ? 0 : (overlapEnd - overlapStart).TotalSeconds;
    }
}
