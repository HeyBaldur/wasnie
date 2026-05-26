using Wasnie.Application.Models.Imports;

namespace Wasnie.Application.Services.Imports;

public interface IImportCacheService
{
    string Store(ParsedFile file, string originalFileName);
    (ParsedFile File, string OriginalFileName)? Retrieve(string fileId);
    void Remove(string fileId);
}
