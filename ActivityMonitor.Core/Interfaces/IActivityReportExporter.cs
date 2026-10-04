using System;
using System.Threading;
using System.Threading.Tasks;

namespace ActivityMonitor.Core.Interfaces;

public interface IActivityReportExporter
{
    Task<string> ExportDailyExcelReportAsync(DateTime dateUtc, string? machineId = null, CancellationToken ct = default);
    Task<string> ExportDailyCsvReportAsync(DateTime dateUtc, string? machineId = null, CancellationToken ct = default);
    Task<string> ExportDailySummaryTextAsync(DateTime dateUtc, string? machineId = null, CancellationToken ct = default);
}
