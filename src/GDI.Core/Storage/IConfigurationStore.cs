namespace GDI.Core.Storage;

public interface IConfigurationStore
{
    Task<T> LoadAsync<T>(string fileName, T defaultInstance) where T : class, new();
    Task SaveAsync<T>(string fileName, T instance) where T : class;
    string GetFullPath(string fileName);
}
