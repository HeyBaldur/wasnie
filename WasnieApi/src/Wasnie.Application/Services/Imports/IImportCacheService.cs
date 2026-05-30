using Wasnie.Application.Models.Imports;

namespace Wasnie.Application.Services.Imports;

public interface IImportCacheService
{
    string Store(ParsedFile file, string originalFileName, string resource = "payees");
    (ParsedFile File, string OriginalFileName)? Retrieve(string fileId, string resource = "payees");
    void Remove(string fileId, string resource = "payees");
}
