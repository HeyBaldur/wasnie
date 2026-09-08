using Wasnie.Application.Compensation.DTOs;

namespace Wasnie.Application.Common.Interfaces;

public interface IPayRunExcelExportService
{
    /// <summary>
    /// Three sheets: the runs, the payees inside them, and the commission lines behind those.
    ///
    /// ★★ THE FIRST SHEET ALONE WAS THE BUG. Filtering to Paid and exporting produced four rows that
    /// named nobody: the file said four runs were paid, how much in total and who pressed the button,
    /// and could not say WHO had been paid. The lower two sheets are that answer.
    /// </summary>
    byte[] GenerateExcel(
        IReadOnlyList<PayRunExportRow> rows,
        IReadOnlyList<PayoutExportRow> payees,
        IReadOnlyList<PayoutDetailExportRow> detail,
        string tenantSlug);
}
