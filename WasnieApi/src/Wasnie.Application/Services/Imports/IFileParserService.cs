using Wasnie.Application.Models.Imports;

namespace Wasnie.Application.Services.Imports;

public interface IFileParserService
{
    Task<ParsedFile> ParseAsync(Stream stream, string fileName, int maxRows, CancellationToken cancellationToken = default);
}
