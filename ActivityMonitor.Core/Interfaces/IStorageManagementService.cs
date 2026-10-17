using System.Threading;
using System.Threading.Tasks;
using ActivityMonitor.Core.Models;

namespace ActivityMonitor.Core.Interfaces;

/// <summary>
/// واجهة خدمة إدارة التخزين ومراقبة استهلاك مساحة القرص وتطبيق سياسات الاستبقاء والحذف.
/// </summary>
public interface IStorageManagementService
{
    /// <summary>
    /// حساب ملخص استهلاك التخزين الحالي للملفات (قاعدة البيانات، التسجيلات، التقارير، والسجلات) والمساحة المتاحة على القرص.
    /// </summary>
    /// <param name="ct">رمز الإلغاء.</param>
    /// <returns>ملخص شامل لمساحات التخزين.</returns>
    Task<StorageUsageSummary> GetStorageUsageAsync(CancellationToken ct = default);

    /// <summary>
    /// تطبيق سياسات الاحتفاظ بالبيانات وحذف الملفات القديمة التي تجاوزت المدد المحددة أو تجاوزت الحد الأقصى المسموح للحجم.
    /// </summary>
    /// <param name="ct">رمز الإلغاء.</param>
    /// <returns>عدد الملفات أو المقاطع التي تم حذفها لتحرير المساحة.</returns>
    Task<int> EnforceRetentionPoliciesAsync(CancellationToken ct = default);

    /// <summary>
    /// التحقق من توفر المساحة الكافية على القرص لبدء التسجيل وتفريغ مساحة فورية إذا لزم الأمر.
    /// </summary>
    /// <param name="ct">رمز الإلغاء.</param>
    /// <returns>القيمة true إذا كانت المساحة المتوفرة كافية وآمنة؛ وإلا false.</returns>
    Task<bool> EnsureDiskSpaceAvailableAsync(CancellationToken ct = default);
}
