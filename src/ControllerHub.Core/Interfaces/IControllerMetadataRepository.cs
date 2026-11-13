namespace ControllerHub.Core.Interfaces;

public interface IControllerMetadataRepository
{
    Task<string?> GetCustomNameAsync(string deviceKey);
    Task SetCustomNameAsync(string deviceKey, string customName);
    Task<IReadOnlyDictionary<string, string>> GetAllCustomNamesAsync();
}
