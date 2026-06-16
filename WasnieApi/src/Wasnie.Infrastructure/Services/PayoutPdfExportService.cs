using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.DTOs;

namespace Wasnie.Infrastructure.Services;

public sealed class PayoutPdfExportService : IPayoutPdfExportService
{
    public byte[] GeneratePdf(PayoutDto payout)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(40);
                page.DefaultTextStyle(x => x.FontSize(10).FontFamily("Arial"));

                page.Header().Element(ComposeHeader);
                page.Content().Element(c => ComposeContent(c, payout));
                page.Footer().AlignCenter().Text(x =>
                {
                    x.Span("Generated: ").FontColor(Colors.Grey.Darken2);
                    x.Span(DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm UTC")).FontColor(Colors.Grey.Darken2);
                });
            });
        });

        return document.GeneratePdf();
    }

    private static void ComposeHeader(IContainer container)
    {
        container.Row(row =>
        {
            row.RelativeItem().Column(col =>
            {
                col.Item().Text("Compensation Statement")
                    .FontSize(20).SemiBold().FontColor(Colors.Black);
                col.Item().Text("Sales Performance Management — Wasnie")
                    .FontSize(10).FontColor(Colors.Grey.Darken2);
            });
        });
    }

    private static void ComposeContent(IContainer container, PayoutDto payout)
    {
        container.Column(col =>
        {
            col.Spacing(12);

            // Payee + period block
            col.Item().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(12).Column(info =>
            {
                info.Spacing(4);
                info.Item().Row(r =>
                {
                    r.RelativeItem().Text("Payee").FontSize(9).FontColor(Colors.Grey.Darken2);
                    r.RelativeItem().Text($"{payout.PayeeName}  ({payout.PayeeCode})").SemiBold();
                });
                info.Item().Row(r =>
                {
                    r.RelativeItem().Text("Period").FontSize(9).FontColor(Colors.Grey.Darken2);
                    r.RelativeItem().Text($"{payout.PeriodStart:yyyy-MM-dd}  →  {payout.PeriodEnd:yyyy-MM-dd}").SemiBold();
                });
                info.Item().Row(r =>
                {
                    r.RelativeItem().Text("Status").FontSize(9).FontColor(Colors.Grey.Darken2);
                    r.RelativeItem().Text(payout.Status).SemiBold();
                });
                info.Item().Row(r =>
                {
                    r.RelativeItem().Text("Total Commission").FontSize(9).FontColor(Colors.Grey.Darken2);
                    r.RelativeItem()
                        .Text($"{payout.TotalCommissionAmount:N2} {payout.TotalCommissionCurrency}")
                        .FontSize(14).SemiBold();
                });
            });

            // Audit info
            col.Item().Column(audit =>
            {
                audit.Spacing(2);
                audit.Item().Text($"Calculated by: {payout.CalculatedBy}  at  {payout.CalculatedAt:yyyy-MM-dd HH:mm UTC}")
                    .FontSize(9).FontColor(Colors.Grey.Darken2);
                if (!string.Equals(payout.Status, "Calculated", StringComparison.OrdinalIgnoreCase))
                {
                    audit.Item().Text($"Last updated by: {payout.UpdatedBy}  at  {payout.UpdatedAt:yyyy-MM-dd HH:mm UTC}")
                        .FontSize(9).FontColor(Colors.Grey.Darken2);
                }
            });

            // Lines table
            if (payout.Lines.Count > 0)
            {
                col.Item().Text("Commission Line Detail").FontSize(12).SemiBold();

                col.Item().Table(table =>
                {
                    table.ColumnsDefinition(cols =>
                    {
                        cols.RelativeColumn(3); // Rule
                        cols.RelativeColumn(3); // Source transaction
                        cols.RelativeColumn(2); // Base
                        cols.RelativeColumn(2); // Commission
                    });

                    // Header row
                    table.Header(header =>
                    {
                        header.Cell().Background(Colors.Grey.Lighten3).Padding(6)
                            .Text("Rule").SemiBold().FontSize(9);
                        header.Cell().Background(Colors.Grey.Lighten3).Padding(6)
                            .Text("Source Transaction").SemiBold().FontSize(9);
                        header.Cell().Background(Colors.Grey.Lighten3).Padding(6).AlignRight()
                            .Text("Base Amount").SemiBold().FontSize(9);
                        header.Cell().Background(Colors.Grey.Lighten3).Padding(6).AlignRight()
                            .Text("Commission").SemiBold().FontSize(9);
                    });

                    // Data rows
                    foreach (var line in payout.Lines)
                    {
                        var sourceText = line.TransactionReference is not null
                            ? $"{line.TransactionReference}  {line.TransactionDate:yyyy-MM-dd}"
                            : "—";

                        var rateText = FormatRateText(line.Calculation);

                        table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).Padding(5)
                            .Column(c =>
                            {
                                c.Item().Text(line.RuleName).FontSize(9);
                                if (rateText is not null)
                                    c.Item().Text(rateText).FontSize(8).FontColor(Colors.Grey.Darken2);
                            });
                        table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).Padding(5)
                            .Text(sourceText).FontSize(9).FontColor(
                                line.TransactionReference is not null ? Colors.Black : Colors.Grey.Darken1);
                        table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).Padding(5).AlignRight()
                            .Text($"{line.BaseAmount:N2} {line.BaseCurrency}").FontSize(9);
                        table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).Padding(5)
                            .Column(c =>
                            {
                                c.Item().AlignRight().Text($"{line.CommissionAmount:N2} {line.CommissionCurrency}").FontSize(9);
                                if (line.Calculation?.Modifiers.Count > 0)
                                    c.Item().AlignRight().Text($"({line.Calculation.Modifiers.Count} adj.)").FontSize(8).FontColor(Colors.Grey.Darken2);
                            });
                    }

                    // Total row
                    table.Cell().Padding(5).Text("Total").SemiBold().FontSize(9);
                    table.Cell().Padding(5);
                    table.Cell().Padding(5);
                    table.Cell().Padding(5).AlignRight()
                        .Text($"{payout.TotalCommissionAmount:N2} {payout.TotalCommissionCurrency}")
                        .SemiBold().FontSize(9);
                });
            }
            else
            {
                col.Item().Text("No commission lines recorded for this payout.")
                    .FontColor(Colors.Grey.Darken2).Italic();
            }

            // Modifier chains — one block per line that has adjustments
            var linesWithModifiers = payout.Lines
                .Where(l => l.Calculation?.Modifiers.Count > 0)
                .ToList();

            if (linesWithModifiers.Count > 0)
            {
                col.Item().Text("Calculation Adjustments").FontSize(12).SemiBold();

                foreach (var line in linesWithModifiers)
                {
                    col.Item().Column(lineCol =>
                    {
                        lineCol.Item().Text(line.RuleName)
                            .FontSize(9).SemiBold().FontColor(Colors.Grey.Darken2);

                        lineCol.Item().Table(modTable =>
                        {
                            modTable.ColumnsDefinition(cols =>
                            {
                                cols.RelativeColumn(3); // Modifier name
                                cols.RelativeColumn(1); // Factor
                                cols.RelativeColumn(2); // Before
                                cols.RelativeColumn(2); // After
                            });

                            modTable.Header(h =>
                            {
                                h.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Adjustment").FontSize(8).SemiBold();
                                h.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Factor").FontSize(8).SemiBold();
                                h.Cell().Background(Colors.Grey.Lighten3).Padding(4).AlignRight().Text("Before").FontSize(8).SemiBold();
                                h.Cell().Background(Colors.Grey.Lighten3).Padding(4).AlignRight().Text("After").FontSize(8).SemiBold();
                            });

                            foreach (var mod in line.Calculation!.Modifiers)
                            {
                                modTable.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).Padding(4)
                                    .Text(mod.ModifierName).FontSize(8);
                                modTable.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).Padding(4)
                                    .Text($"×{mod.FactorApplied:F4}").FontSize(8);
                                modTable.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).Padding(4).AlignRight()
                                    .Text($"{mod.AmountBefore:N2} {mod.AmountBeforeCurrency}").FontSize(8);
                                modTable.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).Padding(4).AlignRight()
                                    .Text($"{mod.AmountAfter:N2} {mod.AmountAfterCurrency}").FontSize(8);
                            }
                        });
                    });
                }
            }
        });
    }

    private static string? FormatRateText(LineCalculationDto? calc)
    {
        if (calc is null) return null;
        var rt = calc.RateTable;
        return rt.Type switch
        {
            "Flat" => $"Flat rate: {rt.FlatRate * 100:G}%  ·  Plan v{calc.PlanVersion}",
            "Tiered" when rt.Tiers?.Count > 0 =>
                $"Tiered ({rt.Tiers.Count} tiers)  ·  Plan v{calc.PlanVersion}",
            "AttainmentBased" when rt.AttainmentTiers?.Count > 0 =>
                $"Attainment-based ({rt.AttainmentTiers.Count} tiers)  ·  Plan v{calc.PlanVersion}",
            _ => $"Plan v{calc.PlanVersion}"
        };
    }
}
