using System.Dynamic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.MappingsProvider;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Assets.Objects.Properties;
using CUE4Parse.UE4.Kismet;
using CUE4Parse.UE4.Localization;
using CUE4Parse.UE4.Objects.Core.i18N;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse.UE4.Readers;
using CUE4Parse.UE4.Versions;
using CUE4Parse_Conversion.Textures;
using lib.remnant2.analyzer;
using Newtonsoft.Json;
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
        DefaultFileProvider p = new(settings.GamePath, SearchOption.AllDirectories, true, new(EGame.GAME_UE5_2))
        {
            MappingsContainer = new FileUsmapTypeMappingsProvider(settings.MappingsPath),
            ReadScriptData = true
        };
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
            var all = p.LoadAllObjects(path).ToList();

            var debug2 = JsonConvert.SerializeObject(all, Formatting.Indented);


            UObject def = all.Single(x => x.Name.StartsWith("Default_"));
            UFunction? inspect = all.SingleOrDefault(x => x.Name== "ModifyInspectInfo") as UFunction;
            ExtractLocalization(def, inspect, localization, settings, itemId, profileId);
            ExtractIcon(def, p, settings.OutputPath, itemId);
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

        Dictionary<string, Dictionary<string, string>> localization = [];

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
                        localizations = [];
                    }

                    localizations[locale] = entry.Value.LocalizedString;
                    localization[entry.Key.Str] = localizations;
                }
            }
        }

        return localization;
    }

    private static void ExtractLocalization(UObject def, UFunction? inspect, Dictionary<string, Dictionary<string, string>> localization, Settings settings, string itemId, string profileId)
    {
        FText? text = def.Properties.SingleOrDefault(x => x.Name.Text == "Label")?.Tag?.GetValue<FText>();
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

        
        Dictionary<string, Dictionary<string, string>> locres = localizations.ToDictionary(x => x.Key, x => new Dictionary<string, string> {{ "Name", x.Value }});
        dynamic result = new ExpandoObject();
        result.LocRes = locres;
        result.Id = itemId;
        result.ProfileId = profileId;

        if (inspect != null)
        {
            foreach (KismetExpression ex in inspect.ScriptBytecode)
            {
                if (ex is EX_Let { Assignment: EX_CallMath { StackNode.Name: "Format" } ass })
                {
                    if (ass.Parameters[0] is EX_TextConst t)
                    {
                        if (t.Value.SourceString is EX_StringConst en)
                        {
                            locres["en"]["Description"] = Format(def, en.Value);
                        }
                        if (t.Value.KeyString is EX_StringConst key)
                        {
                            if (localization.TryGetValue(key.Value, out Dictionary<string, string>? desc))
                            {
                                foreach (string s in desc.Keys)
                                {
                                    if (!locres.ContainsKey(s))
                                    {
                                        locres[s] = new();
                                    }
                                    locres[s]["Description"] = Format(def, desc[s]);
                                }
                            }
                        }
                    }
                }
            }
        }

        string json = JsonConvert.SerializeObject(result, Formatting.Indented);
        string jsonPath = Path.Join(settings.OutputPath, itemId) + ".json";
        File.WriteAllText(jsonPath, json);
    }

    private static void ExtractIcon(UObject def, DefaultFileProvider provider, string outputPath, string itemId)
    {
        StringBuilder sb = new();
        def.Properties.SingleOrDefault(x => x.Name.Text == "Icon")?.Tag?.GetValue<FPackageIndex>()?.ResolvedObject?.GetPathName(true, sb);
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

    private static IEnumerable<string> Tokenize(string s)
    {
        int state = 0;
        StringBuilder sb = new();
        foreach (char c in s)
        {
            switch (state)
            {
                case 0:
                    if (c == '{')
                    {
                        state = 1;
                        sb = new();
                        sb.Append(c);
                    }
                    break;
                case 1:
                    if (c == '}')
                    {
                        state = 0;
                        sb.Append(c);
                        yield return sb.ToString();
                    }
                    else
                    {
                        sb.Append(c);
                    }
                    break;
            }
        }
    }

    private static string Replace(string val, string token, UObject def)
    {
        string var = token.Trim('{', '}');
        FPropertyTag? prop = def.Properties.SingleOrDefault(x => x.Name == var);
        if (prop == null || prop.Tag == null)
        {
            //throw new InvalidOperationException($"Could not find requested property {token}, {def}");
            return val;
        }

        string data = prop.Tag switch
        {
            DoubleProperty dbl => dbl.Value.ToString(CultureInfo.InvariantCulture),
            FloatProperty flt => flt.Value.ToString(CultureInfo.InvariantCulture),
            IntProperty integer => integer.Value.ToString(),
            ByteProperty byt => byt.Value.ToString(),
            NameProperty nam => nam.Value.ToString(),
            TextProperty txt => txt.Value!.ToString(),
            _ => throw new InvalidOperationException($"Unknown property type {prop.Tag.GetType()}{token}, {def}")
        };

        return val.Replace(token, data);
    }

    private static string Format(UObject def, string val)
    {
        foreach (string token in Tokenize(val))
        {
            val = Replace(val, token, def);
        }
        return val;
    }
}
