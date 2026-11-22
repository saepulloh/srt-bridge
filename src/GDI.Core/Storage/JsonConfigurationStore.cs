using System.Text.Json;
using GDI.Core.Services;

namespace GDI.Core.Storage;

public sealed class JsonConfigurationStore : IConfigurationStore
{
    private readonly string _baseDirectory;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonConfigurationStore(string? baseDirectory = null)
    {
        if (!string.IsNullOrWhiteSpace(baseDirectory))
        {
            _baseDirectory = Path.GetFullPath(baseDirectory);
        }
        else
        {
            _baseDirectory = GdiEnvironment.GetConfigDirectory();
        }

        Directory.CreateDirectory(_baseDirectory);
    }

    public string GetFullPath(string fileName) => Path.Combine(_baseDirectory, fileName);

    public async Task<T> LoadAsync<T>(string fileName, T defaultInstance) where T : class, new()
    {
        var path = GetFullPath(fileName);
        if (!File.Exists(path))
        {
            await SaveAsync(fileName, defaultInstance).ConfigureAwait(false);
            return defaultInstance;
        }

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var json = await File.ReadAllTextAsync(path).ConfigureAwait(false);
            var result = JsonSerializer.Deserialize<T>(json, _jsonOptions);
            return result ?? defaultInstance;
        }
        catch
        {
            return defaultInstance;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync<T>(string fileName, T instance) where T : class
    {
        var path = GetFullPath(fileName);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var json = JsonSerializer.Serialize(instance, _jsonOptions);
            var tempPath = path + ".tmp";
            await File.WriteAllTextAsync(tempPath, json).ConfigureAwait(false);
            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            _gate.Release();
        }
    }
}
