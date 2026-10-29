using Dalamud.Bindings.ImGui;
using Dalamud.Configuration;
using Dalamud.Plugin;
using Newtonsoft.Json;
using System;
using System.Numerics;

namespace HuntHelper
{
    [Serializable]
    public class Configuration : IPluginConfiguration
    {
        public int Version { get; set; } = 0;

        #region Main Map Window

        [JsonIgnore]
        public bool MapWindowVisible { get; set; } = false;

        //initial window position
        public Vector2 MapWindowPos { get; set; } = new Vector2(25, 25);
        //window sizes
        public int CurrentWindowSize { get; set; } = 512;
        public float MapImageOpacityAsPercentage { get; set; } = 100f;

        //defaults to using client lang file
        public string Language { get; set; } = string.Empty;


        #region icon stuff
        public Vector4 SpawnPointColour { get; set; } = new Vector4(0.24706f, 0.309804f, 0.741176f, 1); //purty blue
        public Vector4 MobColour { get; set; } = new Vector4(0.4f, 1f, 0.567f, 1f); //green
        public Vector4 PlayerIconColour { get; set; } = new Vector4(0f, 0f, 0f, 1f); //black
        public Vector4 PlayerIconBackgroundColour { get; set; } = new Vector4(0.117647f, 0.5647f, 1f, 0.7f); //blue
        public Vector4 DirectionLineColour { get; set; } = new Vector4(1f, 0.3f, 0.3f, 1f); //redish
        public Vector4 DetectionCircleColour { get; set; } = new Vector4(1f, 1f, 0f, 1f); //goldish

        public Vector4 PlayerPathColour = new Vector4(0.117647f, 0.5647f, 1f, 0.4f); //blue
        // icon radius sizes
        public float AllRadiusModifier { get; set; } = 1.0f;
        public float MobIconRadiusModifier { get; set; } = 2f;
        public float SpawnPointRadiusModifier { get; set; } = 1.0f;
        public float PlayerIconRadiusModifier { get; set; } = 1f;
        public float DetectionCircleModifier { get; set; } = 1.0f;
        // mouseover distance modifier for mob icon tooltips
        public float MouseOverDistanceModifier { get; set; } = 2.5f;
        // icon-related thickness
        public float DetectionCircleThickness { get; set; } = 3f;
        public float DirectionLineThickness { get; set; } = 3f;
        #endregion

        #region on-screen text position and colour stuff
        public float ZoneInfoPosXPercentage { get; set; } = 3.5f;
        public float ZoneInfoPosYPercentage { get; set; } = 11.1f;
        public float WorldInfoPosXPercentage { get; set; } = 0.45f;
        public float WorldInfoPosYPercentage { get; set; } = 8.3f;
        public float PriorityMobInfoPosXPercentage { get; set; } = 35f;
        public float PriorityMobInfoPosYPercentage { get; set; } = 2.5f;
        public float NearbyMobListPosXPercentage { get; set; } = .42f;
        public float NearbyMobListPosYPercentage { get; set; } = 69f;
        // text colour
        public Vector4 ZoneTextColour { get; set; } = new Vector4(0.282353f, 0.76863f, 0.69412f, 1f); //blue-greenish
        public Vector4 ZoneTextColourAlt { get; set; } = new Vector4(0, 0.4353f, 0.36863f, 1f); //same but darker
        public Vector4 WorldTextColour { get; set; } = new Vector4(0.77255f, 0.3412f, 0.612f, 1f); //purply
        public Vector4 WorldTextColourAlt = new Vector4(0.51765f, 0, 0.3294f, 1f); //same but darker
        public Vector4 PriorityMobTextColour { get; set; } = Vector4.One;
        public Vector4 PriorityMobTextColourAlt { get; set; } = Vector4.One;
        public Vector4 NearbyMobListColour { get; set; } = Vector4.One;
        public Vector4 NearbyMobListColourAlt { get; set; } = Vector4.One;
        public Vector4 PriorityMobColourBackground { get; set; } = new Vector4(1f, 0.43529411764705883f, 0.5137254901960784f, 0f); //nicely pink :D
        public Vector4 NearbyMobListColourBackground { get; set; } = new Vector4(1f, 0.43529411764705883f, 0.5137254901960784f, 0f); //nicely pink :D
        #endregion

        public Vector4 MapWindowColour { get; set; } = new Vector4(0f, 0f, 0f, .2f); //alpha / w value isn't used
        public float MapWindowOpacityAsPercentage { get; set; } = 20f;

        #region map ui bools
        public bool PriorityMobEnabled { get; set; } = true;
        public bool NearbyMobListEnabled { get; set; } = true;
        public bool ShowZoneName { get; set; } = true;
        public bool ShowWorldName { get; set; } = true;
        public bool SaveSpawnData { get; set; } = true;
        public bool UseMapImages { get; set; } = false;
        // 底圖來源。true(預設)= 直接用遊戲 sqpack 裡的地圖材質 ui/map/{Map.Id}/{...}_m.tex:
        // 不必下載 25MB、離線可用、不會因為外掛更新換版本資料夾而消失,而且材質正好覆蓋完整
        // 地圖座標範圍(SizeFactor 與 GetMapZoneScale 在 47 個獵物區全部一致),與標點對齊。
        // false = 沿用原本從 HuntHelper-Resources 下載的「The Hunt」標註圖。
        public bool UseGameMapTexture { get; set; } = true;
        public bool EnableBackgroundScan { get; set; } = false;
        public bool ShowOptionsWindow { get; set; } = false;
        public bool DisplayA { get; set; } = true;
        public bool DisplayB { get; set; } = true;
        public bool DisplayS { get; set; } = true;

        public bool ughresetdisplaypls = false;
        #endregion

        #region TTS
        public string TTSVoiceName { get; set; } = string.Empty;
        public int TTSVolume { get; set; } = 50;
        public string TTSAMessage { get; set; } = "<rank> Nearby";
        public string TTSBMessage { get; set; } = "<rank> Nearby";
        public string TTSSMessage { get; set; } = "<rank> in zone";
        public string ChatAMessage { get; set; } = "FOUND: <name> @ <flag> ---  <rank>  --  <hpp>";
        public string ChatBMessage { get; set; } = "FOUND: <name> @ <flag> ---  <rank>  --  <hpp>";
        public string ChatSMessage { get; set; } = "FOUND: <name> @ <flag> ---  <rank>  --  <hpp>";
        public bool TTSAEnabled { get; set; } = false;
        public bool TTSBEnabled { get; set; } = false;
        public bool TTSSEnabled { get; set; } = true;
        #endregion

        #region chat notif
        public bool ChatAEnabled { get; set; } = false;
        public bool ChatBEnabled { get; set; } = false;
        public bool ChatSEnabled { get; set; } = true;
        public bool FlyTextAEnabled { get; set; } = false;
        public bool FlyTextBEnabled { get; set; } = false;
        public bool FlyTextSEnabled { get; set; } = true;

        #endregion

        #region 塔塔露誇獎（TataruPraise）語音通知
        // 掛機時被「有聲音」叫回電腦前用的。純通知：不會動到既有的聊天／TTS／浮空文字輸出；對方沒安裝時整條路徑是 no-op。
        // 分級沿用 HuntHelper 自己的 A/B/S，預設值也沿用既有 Chat/TTS/FlyText 三組的形狀（A=false、B=false、S=true）：
        // S 階是「該把人叫回來」的那一種，A/B 在野外會一直遇到，預設開會變成噪音，要的人自己在通知分頁勾。
        public bool TataruPraiseAEnabled { get; set; } = false;
        public bool TataruPraiseBEnabled { get; set; } = false;
        public bool TataruPraiseSEnabled { get; set; } = true;
        #endregion

        // 同一隻魔物的發現通知冷卻（分鐘），0＝關閉。舊設定檔缺這個鍵時保留這裡的初始值。
        public int NewMobNotificationCooldownMinutes { get; set; } = 5;

        public bool PointToARank { get; set; } = false;
        public bool PointToBRank { get; set; } = false;
        public bool PointToSRank { get; set; } = false;
        public float PointerDiamondSizeModifier { get; set; } = 1f;

        public int SFoundCount { get; set; } = 0;
        public int AFoundCount { get; set; } = 0;
        public int BFoundCount { get; set; } = 0;

        public Preset PresetOne { get; set; } = new Preset() { MapOpacity = 1, UseMap = false, WindowOpacity = 1, WindowSize = 512 };
        public Preset PresetTwo { get; set; } = new Preset() { MapOpacity = 1, UseMap = false, WindowOpacity = 1, WindowSize = 512 };

        #endregion

        #region Hunt Train Window
        public Vector2 HuntTrainWindowSize { get; set; } = new Vector2(250, 400);
        public Vector2 HuntTrainWindowPos { get; set; } = new Vector2(150, 150);
        public bool HuntTrainShowLastSeen { get; set; } = false;
        public bool HuntTrainUseBorder { get; set; } = false;
        public bool HuntTrainNextOpensMap { get; set; } = false;
        public bool HuntTrainNextTeleportMe { get; set; } = false;
        public bool HuntTrainNextTeleportMeOnCommand { get; set; } = false;
        public bool HuntTrainShowTeleportButtons { get; set; } = false;
        public bool HuntTrainTeleportToAetheryte { get; set; } = false;
        public bool HuntTrainShowFlagInChat { get; set; } = true;
        public bool HuntTrainShowUIDuringIPCImport { get; set; } = true;

        public bool HuntTrainShowMapName { get; set; } = false;

        #endregion


        #region Counter Window

        #region spawn point refinement window

        public bool SpawnPointRecordAll { get; set; } = false;

        #endregion
        public Vector2 CounterWindowPos { get; set; } = new Vector2(50, 50);
        public Vector2 CounterWindowSize { get; set; } = new Vector2(250, 50);

        public bool CountInBackground { get; set; } = false;
        #endregion


        //Hunt Window Flag - used for toggling title bar
        public int HuntWindowFlag { get; set; } = (int)(ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoTitleBar);

        #region click-through
        // 穿透模式:游標落在地圖「背景」上時整個視窗當幀不吃滑鼠輸入,點擊直接落到遊戲。
        // 預設關閉 —— 開啟後視窗本體不能再用拖曳移動(只剩標題列、右下角縮放握把與修飾鍵),
        // 對沒主動要求這個功能的人來說是意外行為,所以維持既有行為當預設值。
        public bool MapWindowClickThrough { get; set; } = false;
        // 穿透模式下暫時取回完整控制的修飾鍵:0=無 1=Shift 2=Ctrl 3=Alt
        public int MapWindowClickThroughModifier { get; set; } = 1;
        #endregion

        public string MapImagesVer { get; set; } = "0\n"; //uhh newline cause it's just there by default in github idk idc


        [NonSerialized]
        private IDalamudPluginInterface? _pluginInterface;

        public void Initialize(IDalamudPluginInterface pluginInterface)
        {
            this._pluginInterface = pluginInterface;
        }

        public void Save()
        {
            this._pluginInterface!.SavePluginConfig(this);
        }

        public void SaveMainMapPreset(int windowSize, float windowOpacity, float mapOpacity, bool useMap, Vector2 winPos, int presetNumber)
        {
            if (presetNumber is < 1 or > 2) return;
            var toUpdate = presetNumber == 1 ? PresetOne : PresetTwo;
            toUpdate.WindowOpacity = windowOpacity;
            toUpdate.MapOpacity = mapOpacity;
            toUpdate.UseMap = useMap;
            toUpdate.WindowSize = windowSize;
            toUpdate.WindowPosition = winPos;
        }
    }
}
