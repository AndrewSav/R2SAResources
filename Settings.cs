using System.Dynamic;
using System.Reflection;
using Microsoft.Win32;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace R2SAResources;

internal partial class Settings
{
    public string Key { get; set; } = "";
    public string MappingsPath { get; set; } = "";
    public string GamePath { get; set; } = "";
    public string OutputPath { get; set; } = "";

    public static Settings Load()
    {
        string path = Path.Join(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
        Settings result = new();
        IDictionary<string, object?>? dx = null;
        if (File.Exists(path))
        {
            dx = JsonSerializer.Deserialize<ExpandoObject>(File.ReadAllText(path));
        }

        bool isOk = true;
        foreach (PropertyInfo propertyInfo in typeof(Settings).GetProperties())
        {
            string name = propertyInfo.Name;
            string environmentVariableName = $"R2SARES_{string.Join('_',RegexSplitAtCapitals().Split(name).Select(x => x.ToUpper()))}";
            string? value = Environment.GetEnvironmentVariable(environmentVariableName);
            value ??= dx![name] as string;
            if (name == "GamePath" && string.IsNullOrEmpty(value))
            {
                value = GetSteamFolder();
                if (value != null)
                {
                    value += "\\steamapps\\common\\Remnant2";
                }
            }
            if (string.IsNullOrEmpty(value))
            {
                isOk = false;
                Console.WriteLine($"Error: {name} is expected but undefined. Either set it settings.json or set {environmentVariableName} environment variable");
            }
            else
            {
                propertyInfo.SetValue(result,value);
                Console.WriteLine($"Using {name}: '{value}'");
            }
        }

        if (!isOk)
        {
            Environment.Exit(1);
        }
        return result;
    }

    private static string? GetSteamFolder()
    {
        if (Registry.GetValue(@"HKEY_CLASSES_ROOT\steam\Shell\Open\Command", null, null) is not string steam)
        {
            return null;
        }
        int i = steam.IndexOf('"', 1);
        return i <= 0 ? null : Path.GetDirectoryName(steam.Substring(1, i - 1));
    }
    [GeneratedRegex("(?<!^)(?=[A-Z])")]
    private static partial Regex RegexSplitAtCapitals();
}