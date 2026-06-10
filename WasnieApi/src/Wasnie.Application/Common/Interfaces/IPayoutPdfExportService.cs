using Wasnie.Application.Compensation.DTOs;

namespace Wasnie.Application.Common.Interfaces;

public interface IPayoutPdfExportService
{
    byte[] GeneratePdf(PayoutDto payout);
}
