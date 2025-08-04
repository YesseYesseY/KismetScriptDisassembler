using System;
using System.Globalization;
using System.Reflection;
using System.Xml.Linq;
using CUE4Parse.Compression;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.MappingsProvider;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Readers;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse.UE4.Readers;
using CUE4Parse.UE4.Versions;
using KismetScriptDisassembler;

string? paksPath = null;
string? mainAes = null;
string? mapFile = null;
string ueVer = "GAME_UE5_LATEST";
List<string> objsToExport = new();

#if DEBUG
#if false
paksPath = "C:\\Users\\Yes\\Desktop\\19.40\\FortniteGame\\Content\\Paks";
mainAes = "0xB30A5DBC657A27FBC9E915AFBFBB13F97A3164034F32B1899DEA714CD979E8C3";
mapFile = "C:\\Users\\Yes\\Desktop\\19.40.usmap";
objsToExport.Add("FortniteGame/Content/Athena/Items/ForagedItems/EnvCampFire/B_BGA_Athena_EnvCampFire.B_BGA_Athena_EnvCampFire_C");
ueVer = "GAME_UE5_NoLargeWorldCoordinates";
#else
paksPath = "C:\\Program Files\\Epic Games\\Fortnite\\FortniteGame\\Content\\Paks";
mainAes = "0x7E0342286BB79D986B204ADF54AE03E066FA5FB41A0D360AFC4E1F48B1CE7EDD";
mapFile = "C:\\Users\\Yes\\Downloads\\FModel\\Output\\.data\\++Fortnite+Release-36.30-CL-44367537-Windows_oo.usmap";
objsToExport.Add("FortniteGame/Content/Athena/Items/ForagedItems/EnvCampFire/B_BGA_Athena_EnvCampFire.B_BGA_Athena_EnvCampFire_C");
objsToExport.Add("FortniteGame/Content/Athena/Items/Weapons/Prototype/PetrolPump/BGA_Petrol_Pickup.BGA_Petrol_Pickup_C");
#endif
#endif

var _out = Path.GetDirectoryName(Assembly.GetEntryAssembly()?.Location);
var outPath = _out is null ? "" : _out;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "-i":
            paksPath = args[++i];
            break;

        case "-aes":
            mainAes = args[++i];
            break;

        case "-map":
            mapFile = args[++i];
            break;

        case "-obj":
            var arg = args[++i];
            var split = arg.Split(",");
            if (split is not null)
                foreach (var thing in split)
                    objsToExport.Add(thing);

            break;

        default:
            break;
    }
}

if (paksPath is null || !Directory.Exists(paksPath))
{
    Console.WriteLine("paksPath is null");
    return;
}

if (mainAes is null)
{
    Console.WriteLine("mainAes is null");
    return;
}

OodleHelper.DownloadOodleDll();
OodleHelper.Initialize(OodleHelper.OODLE_DLL_NAME);

// TODO: Custom version arg
EGame game = EGame.GAME_UE5_LATEST;
if (Enum.TryParse(ueVer, out EGame customgame))
    game = customgame;
var provider = new DefaultFileProvider(paksPath, SearchOption.TopDirectoryOnly, new VersionContainer(game));
if (mapFile is not null && File.Exists(mapFile))
{
    provider.MappingsContainer = new FileUsmapTypeMappingsProvider(mapFile);
}

provider.ReadScriptData = true;
CultureInfo customCulture = (CultureInfo)Thread.CurrentThread.CurrentCulture.Clone();
customCulture.NumberFormat.NumberDecimalSeparator = ".";

Thread.CurrentThread.CurrentCulture = customCulture;

provider.Initialize();
provider.SubmitKey(new FGuid(), new FAesKey(mainAes));

foreach (var objName in objsToExport)
{
    var obj = provider.LoadPackageObject<UClass>(objName);

    new DefaultKismetScriptDisassembler(outPath, obj).Disassemble();
}