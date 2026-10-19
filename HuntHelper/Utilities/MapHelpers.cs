using Dalamud.Game;
using Dalamud.Plugin.Services;
using HuntHelper.Managers.Hunts.Models;
using Lumina.Data;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Numerics;
using System.Threading.Tasks;
using FFXIVClientStructs.FFXIV.Client.Game.UI;

namespace HuntHelper.Utilities;
/// <summary>
/// Rename this class... DataManagerUtil? ExcelUtil? idk
/// </summary>
public class MapHelpers
{
    // 中文客戶端(國服簡中/台服繁中)的 sqpack 只有中文 EXD,拿不到英文地名;
    // 用地名字典比對的話,台服 PlaceName 是繁中而必定 miss,
    // 所以用 territoryId→英文名的語言無關對照(來源:Data/SpawnPointData.json 的 MapID/MapName,
    // 與獵物地圖底圖檔名一致),簡中/繁中客戶端皆適用。
    private static Dictionary<uint, string> TerritoryToEnglish = new Dictionary<uint, string>()
    {
        {134,"Middle La Noscea"},{135,"Lower La Noscea"},{137,"Eastern La Noscea"},{138,"Western La Noscea"},{139,"Upper La Noscea"},{180,"Outer La Noscea"},//La Noscea
        {148,"Central Shroud"},{152,"East Shroud"},{153,"South Shroud"},{154,"North Shroud"},//Shroud
        {140,"Western Thanalan"},{141,"Central Thanalan"},{145,"Eastern Thanalan"},{146,"Southern Thanalan"},{147,"Northern Thanalan"},//Thanalan
        {155,"Coerthas Central Highlands"},{156,"Mor Dhona"},//2.0
        {401,"The Sea of Clouds"},{402,"Azys Lla"},{398,"The Dravanian Forelands"},{400,"The Churning Mists"},{399,"The Dravanian Hinterlands"},{397,"Coerthas Western Highlands"},//3.0
        {612,"The Fringes"},{613,"The Ruby Sea"},{620,"The Peaks"},{614,"Yanxia"},{621,"The Lochs"},{622,"The Azim Steppe"},//4.0
        {813,"Lakeland"},{814,"Kholusia"},{815,"Amh Araeng"},{816,"Il Mheg"},{817,"The Rak'tika Greatwood"},{818,"The Tempest"},//5.0
        {956,"Labyrinthos"},{957,"Thavnair"},{958,"Garlemald"},{959,"Mare Lamentorum"},{961,"Elpis"},{960,"Ultima Thule"},//6.0
        {1187,"Urqopacha"},{1188,"Kozama'uka"},{1189,"Yak T'el"},{1190,"Shaaloani"},{1191,"Heritage Found"},{1192,"Living Memory"}//7.0
    };

    public static IDataManager DataManager;
    private static IClientState _clientState;

    public static void SetUp(IDataManager dataManager, IClientState clientState)
    {
        DataManager = dataManager;
        _clientState = clientState;
    }

    public static string GetMapName(uint territoryID)
    //map id... territory id... confusing ...
    // KirisameVanilla: Map refers to the texture of a map. Territory refers to the zone of a map, like Lakeland.
    // KirisameVanilla: So in order to avoid confusing i suggest using 'Zone' instead of the word 'Map' in HuntHelper to distinguish texture map and zone map(territory)
    {
        return DataManager.Excel.GetSheet<TerritoryType>()?.GetRowOrDefault(territoryID)?.PlaceName.ValueNullable?.Name.ToString() ?? "location not found";
    }
    public static string GetMapNameInEnglish(uint territoryID)
    {
        /*DataManager.Excel.RemoveSheetFromCache<TerritoryType>();
       return DataManager.Excel.GetSheet<TerritoryType>(Language.English)?.GetRow(territoryID)?.PlaceName?.Value?.Name.ToString() ?? "location not found";        */

        // TC(台服)客戶端在 Dalamud 13.0.0.16 之後回報 ClientLanguage 7(TraditionalChinese),
        // 舊版回報 4(ChineseSimplified)。用數值比較是為了相容列舉沒有 7 這個名字的舊版 Dalamud。
        // 中文客戶端(4/5/7)一律走內建 territoryId 對照;
        // 另外 TC fork 的 Lumina 會把指定語言的 Excel 請求靜默改成 client 語言,指定 Language.English 在台服拿到的其實是繁中。
        if ((int)_clientState.ClientLanguage is 4 or 5 or 7)
        {
            return TerritoryToEnglish.TryGetValue(territoryID, out var value) ? value : "location not found";
        }

        var row = DataManager.Excel.GetSheet<TerritoryType>(Language.English).GetRowOrDefault(territoryID)?.PlaceName.RowId ?? 0;
        return DataManager.Excel.GetSheet<PlaceName>(Language.English).GetRowOrDefault(row)?.Name.ToString() ?? "location not found";
    }

    public static uint GetMapID(uint territoryID) //createmaplink doesn't work with "Mor Dhona" :(
    {
        return DataManager.GetExcelSheet<TerritoryType>().GetRowOrDefault(territoryID)?.Map.RowId ?? 0;
    }

    /// <summary>
    /// 取得該區域「遊戲內建地圖材質」在 sqpack 裡的路徑,取不到時回傳 null。
    /// </summary>
    /// <remarks>
    /// 格式是 <c>ui/map/{Map.Id}/{Map.Id 去掉斜線}_m.tex</c>,例如 s1f1/00 → ui/map/s1f1/00/s1f100_m.tex。
    /// <b>ui/map 底下沒有任何語言子資料夾</b> —— 與 ui/icon 的 <c>tc</c> 子資料夾不同,所以不會踩到「指定語言取圖靜默失敗」那個坑,直接用單一路徑即可。
    /// 台服沒有 <c>_hr1</c> 也沒有 <c>_s</c> 變體;遮罩是另一顆 <c>{base}m_m.tex</c>,本外掛不需要。
    /// 座標對齊:Map.SizeFactor 與 HuntManager.GetMapZoneScale 的假設在獵物區域上一致(HW 六區 95、其餘 100),
    /// 而遊戲材質正好覆蓋 1..MapScaleToMaxCoord(scale) 的完整座標範圍,直接拉滿視窗即與標點/玩家圖示對齊。
    /// </remarks>
    public static string? GetGameMapTexturePath(uint territoryID)
    {
        // 地圖視窗關著也每幀來問（底圖要保持載入）。路徑只由區域決定、遊戲資料表載入後不變，同一區直接回上次的結果。
        var last = _lastTexturePath;
        if (last != null && last.TerritoryId == territoryID) return last.Path;

        var path = ResolveGameMapTexturePath(territoryID);
        _lastTexturePath = new TexturePathEntry(territoryID, path);
        return path;
    }

    private sealed record TexturePathEntry(uint TerritoryId, string? Path);

    private static TexturePathEntry? _lastTexturePath;

    private static string? ResolveGameMapTexturePath(uint territoryID)
    {
        var territory = DataManager.Excel.GetSheet<TerritoryType>().GetRowOrDefault(territoryID);
        if (territory == null) return null;

        var map = territory.Value.Map.ValueNullable;
        if (map == null) return null;

        var mapId = map.Value.Id.ExtractText();
        if (string.IsNullOrWhiteSpace(mapId)) return null;

        return $"ui/map/{mapId}/{mapId.Replace("/", string.Empty)}_m.tex";
    }

    //convert map scale (100/95) to map size (41/43.1)
    public static float MapScaleToMaxCoord(float mapScale) => (-0.42f) * mapScale + 83f;

    public static float ConvertToMapCoordinate(float pos, float mapScale)
    {
        return 2048f / mapScale + pos / 50f + 1f;
    }

    public static Vector2 ConvertToMapCoordinate(Vector3 pos, float mapScale)
    {
        return new Vector2(
            ConvertToMapCoordinate(pos.X, mapScale),
            ConvertToMapCoordinate(pos.Z, mapScale)
        );
    }

    public static void LocaliseMobNames(List<HuntTrainMob> trainList)
    {
        trainList.ForEach(m => m.Name = DataManager.Excel.GetSheet<BNpcName>()?.GetRowOrDefault(m.MobID)?.Singular.ToString() ?? m.Name);
    }

    public static async Task<bool> MapImageVerUpToDate(string currentVersion)
    {
        try
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Cache-Control", "no-cache");

            var ver = await client.GetStringAsync(ImageVerUrl);
            PluginLog.Information($"map images latest ver: {ver} Local ver: {currentVersion}");
            return currentVersion == ver;
        }
        catch (Exception ex)
        {
            PluginLog.Warning("Could not check map image version");
            PluginLog.Error(ex.Message);
        }
        return true;
    }

    public static async Task<string> GetMapImageVer()
    {

        try
        {
            var client = new HttpClient();
            //client.DefaultRequestHeaders.Add("User-Agent", "request");
            client.DefaultRequestHeaders.Add("Cache-Control", "no-cache");
            var ver = await client.GetStringAsync(ImageVerUrl);
            return ver;
        }
        catch (Exception ex)
        {
            PluginLog.Error(ex.Message);
        }
        return "0";
    }
    
    public static unsafe uint CurrentInstance => UIState.Instance()->PublicInstance.InstanceId;

#if DEBUG
    private static readonly string ImageVerUrl = @"https://raw.githubusercontent.com/img02/HuntHelper-Resources/test/version";
#else
    private static readonly string ImageVerUrl = @"https://raw.githubusercontent.com/img02/HuntHelper-Resources/main/version";
#endif
}