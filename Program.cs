using System.Text;
using System.Text.Json;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.MappingsProvider;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse.UE4.Localization;
using CUE4Parse.UE4.Objects.Core.i18N;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse.UE4.Readers;
using CUE4Parse.UE4.Versions;
using CUE4Parse_Conversion.Textures;
using lib.remnant2.analyzer;
using SkiaSharp;

namespace R2SAResources;

internal class Program
{
    static void Main()
    {
        try
        {
            Run();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex}");
        }
    }

    private static void Run()
    {
        Settings settings = Settings.Load();
        Console.WriteLine($"Making sure '{settings.OutputPath}' exists...");
        Directory.CreateDirectory(settings.OutputPath);
        DefaultFileProvider p = new DefaultFileProvider(settings.GamePath, SearchOption.AllDirectories, true, new(EGame.GAME_UE5_2));
        p.MappingsContainer = new FileUsmapTypeMappingsProvider(settings.MappingsPath);
        Console.WriteLine("Loading data..");
        p.Initialize();
        Console.WriteLine("Decrypting...");
        p.SubmitKey(new FGuid(), new FAesKey(settings.Key));

        var localization = LoadLocalization(p);

        foreach (string profileId in ItemDb.Db.Where(x => x.ContainsKey("ProfileId")).Select( x=> x["ProfileId"]))
        {
            string path = profileId.Split('.')[0];
            string itemId = Path.GetFileName(path);
            Console.WriteLine($"Processing {itemId}...");
            UObject obj = p.LoadAllObjects(path).First(x => x.Name.StartsWith("Default_"));
            ExtractLocalization(obj, localization, settings, itemId, profileId);
            ExtractIcon(obj, p, settings.OutputPath, itemId);
        }
    }

    private static Dictionary<string, Dictionary<string, string>> LoadLocalization(DefaultFileProvider p)
    {
        var availableLocales = new[] {
            "de",
            // "en",
            "es",
            "fr",
            "it",
            "ja",
            "ko",
            "pt-BR",
            "ru",
            "zh-Hans",
            //"zh-Hant"
        };

        Dictionary<string, Dictionary<string, string>> localization = new();

        foreach (string locale in availableLocales)
        {
            Console.WriteLine($"Loading locale {locale}...");
            p.TryCreateReader($"/Game/Localization/Remnant2/{locale}/Remnant2.locres", out FArchive? archive);
            FTextLocalizationResource locres = new(archive);
            foreach (KeyValuePair<FTextKey, Dictionary<FTextKey, FEntry>> part in locres.Entries)
            {
                foreach (KeyValuePair<FTextKey, FEntry> entry in part.Value)
                {
                    if (!localization.TryGetValue(entry.Key.Str, out Dictionary<string, string>? localizations))
                    {
                        localizations = new();
                    }

                    localizations[locale] = entry.Value.LocalizedString;
                    localization[entry.Key.Str] = localizations;
                }
            }
        }

        return localization;
    }

    private static void ExtractLocalization(UObject obj, Dictionary<string, Dictionary<string, string>> localization, Settings settings, string itemId, string profileId)
    {
        FText? text = obj.Properties.SingleOrDefault(x => x.Name.Text == "Label")?.Tag?.GetValue<FText>();
        if (text == null)
        {
            Console.WriteLine("!!!!!!!Warning, could not find item label");
            return;
        }

        string? localizationKey = (text.TextHistory as FTextHistory.Base)?.Key;
        if (localizationKey == null)
        {
            Console.WriteLine("!!!!!!!Warning, could not find localization key");
            return;
        }

        if (!localization.TryGetValue(localizationKey, out Dictionary<string, string>? localizations))
        {
            Console.WriteLine($"!!!!!!!Warning, localization key '{localizationKey}' not found in locres");
            return;
        }

        localizations["en"] = text.Text;
        localizations["Id"] = itemId;
        localizations["ProfileId"] = profileId;
        string json = JsonSerializer.Serialize(localizations, new JsonSerializerOptions { WriteIndented = true });
        string jsonPath = Path.Join(settings.OutputPath, itemId) + ".json";
        File.WriteAllText(jsonPath, json);
    }

    private static void ExtractIcon(UObject obj, DefaultFileProvider provider, string outputPath, string itemId)
    {
        StringBuilder sb = new StringBuilder();
        obj.Properties.SingleOrDefault(x => x.Name.Text == "Icon")?.Tag?.GetValue<FPackageIndex>()?.ResolvedObject?.GetPathName(true, sb);
        if (sb.Length == 0)
        {
            Console.WriteLine("!!!!!!!Warning, could not find icon");
            return;
        }
        var name = sb.ToString().Split('.')[0];
        UTexture2D icon = (provider.LoadAllObjects($"{name}").First() as UTexture2D)!;
        var png = icon.Decode()!.Encode(SKEncodedImageFormat.Png, 100).ToArray();
        string pngPath = Path.Join(outputPath, itemId) + ".png";
        File.WriteAllBytes(pngPath, png);
    }
}
