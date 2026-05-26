using System.ComponentModel.DataAnnotations;

namespace Wasnie.Application.Common.Models;

public class PaginationQuery
{
    [Range(1, int.MaxValue)] public int Page { get; set; } = 1;
    [Range(1, 100)] public int PageSize { get; set; } = 25;
    public string? SortBy { get; set; }
    [RegularExpression("^(asc|desc)$")] public string SortOrder { get; set; } = "asc";
    public string? Search { get; set; }
    public Dictionary<string, string>? Filters { get; set; }
}
