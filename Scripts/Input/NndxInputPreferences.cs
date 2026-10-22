using System.Text.Json;

namespace Nndx.Scripts;

internal sealed record NndxInputPreferences
{
    public string Keyboard { get; init; } = "C";

    internal static NndxInputPreferences Load(string path) => File.Exists(path)
        ? JsonSerializer.Deserialize<NndxInputPreferences>(File.ReadAllText(path))
          ?? throw new InvalidDataException("输入配置为空。")
        : new();

    internal void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this));
        File.Move(temporary, path, overwrite: true);
    }
}
