using Wasnie.Application.Compensation.DTOs;

namespace Wasnie.Application.Common.Interfaces;

public interface IPayoutExcelExportService
{
    /// <summary>
    /// The accounting workbook: a Summary sheet of one row per payee, and a Detail sheet of one row
    /// per commission line behind those totals.
    ///
    /// ★ THE DETAIL IS NOT OPTIONAL. This file is what somebody pays from, and "where does this figure
    /// come from?" is the first question they ask; answering it used to mean opening every payout on
    /// screen, one payee at a time.
    /// </summary>
    byte[] GenerateExcel(
        IReadOnlyList<PayoutExportRow> rows,
        IReadOnlyList<PayoutDetailExportRow> detail,
        string tenantSlug);

    /// <summary>
    /// ONE payout as a two-sheet workbook: its header, and a row per line.
    ///
    /// It is a separate method rather than an overload of the list export because the two answer
    /// different questions — that one is "which payouts exist", this one is "what is inside this
    /// payout" — and folding them together would give the list export a shape it has no data for.
    /// </summary>
    byte[] GenerateSinglePayoutExcel(PayoutDto payout, string tenantSlug);
}
