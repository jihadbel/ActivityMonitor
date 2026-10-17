using System.Threading;
using System.Threading.Tasks;
using ActivityMonitor.Core.Models;

namespace ActivityMonitor.Core.Interfaces;

/// <summary>
/// واجهة خدمة فحص وتدقيق سلامة البيانات والتسجيلات وإصلاح الأخطاء تلقائياً.
/// </summary>
public interface IDataIntegrityService
{
    /// <summary>
    /// إجراء تدقيق شامل للبيانات: كشف التسجيلات المعلقة دون إغلاق وإصلاحها،
    /// ووضع علامة حذف على الملفات المفقودة من القرص، وتخطي المسارات غير الآمنة.
    /// </summary>
    /// <param name="ct">رمز الإلغاء.</param>
    /// <returns>ملخص لنتائج الفحص وعمليات الإصلاح المنجزة.</returns>
    Task<DataIntegritySummary> AuditAndRepairAsync(CancellationToken ct = default);
}
