using System.ComponentModel.DataAnnotations;

namespace Wasnie.Application.Common.Models;

public class PaginationQuery
{
    [Range(1, int.MaxValue)] public int Page { get; set; } = 1;
    [Range(1, 100)] public int PageSize { get; set; } = 25;
    public string? SortBy { get; set; }
    [RegularExpression("^(asc|desc)$", ErrorMessage = "sortOrder must be 'asc' or 'desc'")] public string SortOrder { get; set; } = "asc";
    public string? Search { get; set; }

    // Flat filter params (Format A: ?status=Active&managerId=...&payeeId=...)
    public string? Status { get; set; }
    public Guid? ManagerId { get; set; }
    public Guid? PayeeId { get; set; }
    public string? Source { get; set; }
    public DateOnly? DateFrom { get; set; }
    public DateOnly? DateTo { get; set; }
}
