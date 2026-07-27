namespace Wasnie.Application.Compensation.DTOs;

/// <summary>One row of the enrichment lookup table: (InputField, InputValue) → Category.</summary>
public sealed record CategoryMappingDto(
    Guid Id,
    string InputField,
    string InputValue,
    string Category);
