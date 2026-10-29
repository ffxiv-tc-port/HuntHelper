using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Gui.FlyText;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using HuntHelper.Managers.Hunts.Models;
using HuntHelper.Managers.MapData.Models;
using HuntHelper.Utilities;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Speech.Synthesis;
using System.Text.RegularExpressions;
using System.Threading.Tasks;


namespace HuntHelper.Managers.Hunts;

public class HuntManager : IDisposable
{
    //todo 6 new dawntrail maps? double check this later
    public static int HuntMapCount = 47;
    public readonly string ImageFolderPath;

    private readonly Dictionary<HuntRank, List<Mob>> _arrDict;
    private readonly Dictionary<HuntRank, List<Mob>> _hwDict;
    private readonly Dictionary<HuntRank, List<Mob>> _shbDict;
    private readonly Dictionary<HuntRank, List<Mob>> _sbDict;
    private readonly Dictionary<HuntRank, List<Mob>> _ewDict;
    private readonly Dictionary<HuntRank, List<Mob>> _dtDict;

    // 攤平索引：ModelID (= IBattleNpc.NameId) -> HuntRank，載入資料時建一次。
    // IsHunt/GetHuntRank 每偵對每個 nearby battle npc 都要查一次，逐字典線性掃太貴，所以建 O(1) 查表。
    private readonly Dictionary<uint, HuntRank> _modelIdToRankIndex = new();
    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly IChatGui _chatGui;
    private readonly IFlyTextGui _flyTextGui;
    private readonly TrainManager _trainManager;

    private readonly IObjectTable _objectTable;

    // 🔴 不可跨偵持有 IBattleNpc。
    // Dalamud 的 ObjectTable 對「每個物件表格位 × 每種 ObjectKind」預先配置一個包裝實例,
    // 每次存取時就地改寫該實例的 Address(ObjectTable.CachedEntry.Update())。
    // 包裝存進欄位、下一偵再讀 → 可能已經指到別的怪(靜默換人);格位空掉時包裝還留著舊位址(懸空 → AVE,try/catch 攔不到)。
    // 正解:只保存 GameObjectId 這類純資料,要用活物件時當場重查。
    private readonly record struct TrackedMob(HuntRank Rank, ulong GameObjectId, uint NameId);

    private readonly List<TrackedMob> _currentMobs;

    // 只拿來比對「上一批裡有沒有出現過這個 NameId」(用途:同一隻怪不要重複播 TTS / 洗頻 / 重送浮動字),
    // 所以存 NameId 快照就夠,不需要(也不可以)留住 IBattleNpc。
    private readonly HashSet<uint> _previousMobNameIds;

    /// <summary>
    /// 已經出過聲的怪 → 出聲當下的 <see cref="Environment.TickCount64"/>。
    ///
    /// <para>
    /// 🔴 <b>去重用的是怪物本身的識別（<c>GameObjectId</c>），不是訊息文字。</b>
    /// <c>_previousMobNameIds</c> 那一份只擋得住「這隻怪一直待在附近名單裡」的情形；
    /// 走遠讓它掉出名單、再走回來，對既有的聊天／TTS 來說就是「新發現」會再印一次。
    /// 聊天多印一行還好，語音多念一次很煩，所以這裡另外壓一層時間窗。
    /// </para>
    /// <para>
    /// 📌 用 <c>GameObjectId</c> 而不是 <c>NameId</c>：同一種怪被打掉後重新湧現是<b>另一隻</b>，
    /// 那時候本來就該再叫人一次；而 <c>NameId</c> 會把兩者混為一談。
    /// </para>
    /// </summary>
    private readonly Dictionary<ulong, long> _praisedMobTicks = new();

    /// <summary>同一隻怪在這段時間內不會再出第二次聲。</summary>
    private const long PraiseDedupeWindowMs = 10 * 60 * 1000;

    // 只在 UI 執行緒（MapUI.Draw → AddNearbyMobs）讀寫；刻意不用 EzThrottler（全插件共用、非執行緒安全）。
    private readonly NotifyCooldownTable _notifyCooldowns = new();

    private int _notificationCooldownMinutes = 5;

    /// <summary>同一隻魔物的發現通知冷卻（分鐘），0＝關閉。只擋聊天／語音／浮空文字／塔塔露語音，不擋任何資料面。</summary>
    public int NotificationCooldownMinutes
    {
        get => _notificationCooldownMinutes;
        set => _notificationCooldownMinutes = Math.Clamp(value, 0, 60);
    }

    private ulong? _priorityMobId;
    private HuntRank _highestRank;

    public int ACount;
    public int SCount;
    public int BCount;

    public bool NotAllImagesFound { get; private set; } = false;
    public bool ImageFolderDoesntExist { get; private set; } = false;
    public bool ErrorPopUpVisible = false;
    public string ErrorMessage = string.Empty;



    public bool DontUseSynthesizer = false;
    public SpeechSynthesizer TTS { get; init; } //aint really used anymore except for setting default voice on load
    public string TTSName { get; set; }
    public int TTSVolume { get; set; }

    #region chat/flytext colours - make customizable later? prob not.
    private readonly ushort _aTextColour = 12;
    private readonly ushort _bTextColour = 34;
    private readonly ushort _sTextColour = 506;
    private readonly ushort _aFlyTextColour = 10;
    private readonly ushort _bFlyTextColour = 33;
    private readonly ushort _sFlyTextColour = 16;
    #endregion

    public List<HuntTrainMob> HuntTrain { get; init; }
    public List<HuntTrainMob> ImportedTrain { get; init; }

    /// <summary>
    /// 目前附近的狩獵目標。每次存取都重新從物件表解析一次(見 <see cref="TrackedMob"/> 上的說明),
    /// 因此回傳的是一份新清單 —— 想清空追蹤名單請用 <see cref="ClearCurrentMobs"/>,對回傳值 Clear() 沒有作用。
    /// </summary>
    public List<(HuntRank Rank, IBattleNpc Mob)> CurrentMobs => ResolveCurrentMobs();

    /// <summary>追蹤名單裡有沒有東西。每幀只想知道「有沒有」的地方用這個，不必為此解析出一份新清單。</summary>
    public bool HasCurrentMobs => _currentMobs.Count > 0;

    public event Action<HuntTrainMob> MarkSeen;


    public HuntManager(IDalamudPluginInterface pluginInterface, TrainManager trainManager, IChatGui chatGui, IFlyTextGui flyTextGui, IObjectTable objectTable, int ttsVolume)
    {
        _arrDict = new Dictionary<HuntRank, List<Mob>>();
        _hwDict = new Dictionary<HuntRank, List<Mob>>();
        _shbDict = new Dictionary<HuntRank, List<Mob>>();
        _sbDict = new Dictionary<HuntRank, List<Mob>>();
        _ewDict = new Dictionary<HuntRank, List<Mob>>();
        _dtDict = new Dictionary<HuntRank, List<Mob>>();
        _currentMobs = new List<TrackedMob>();
        _previousMobNameIds = new HashSet<uint>();
        _pluginInterface = pluginInterface;
        _chatGui = chatGui;
        _flyTextGui = flyTextGui;
        _objectTable = objectTable;
        _trainManager = trainManager;
        TTSVolume = ttsVolume > 100 ? 100 : ttsVolume < 0 ? 0 : ttsVolume;

        HuntTrain = new List<HuntTrainMob>();
        ImportedTrain = new List<HuntTrainMob>();

        ImageFolderPath = ResolveImageFolderPath(_pluginInterface);

        // Starting this makes the plugin fail to load - on non-windows
        try
        {
            TTS = new SpeechSynthesizer();
            TTSName = TTS.Voice.Name;
        }
        catch
        {
            DontUseSynthesizer = true;
        }

        LoadHuntData();
        CheckImageStatus();
    }

    /// <summary>
    /// 決定下載回來的獵物地圖底圖要放哪裡,並把舊位置的圖搬過來。
    /// </summary>
    /// <remarks>
    /// 放設定目錄(不隨版本更換)而不是組件目錄(<c>installedPlugins\HuntHelper\&lt;版本&gt;\Images\Maps</c>):Dalamud 每次更新外掛
    /// 都會換一個新的版本資料夾,圖放在那裡的話只要一發版,使用者的 47 張底圖就全部消失、地圖變空白,得再下載一次 ~25MB。
    /// 啟動時會從本版組件目錄與其他版本資料夾把既有的圖搬過來,讓已下載過的人不必重下。
    /// </remarks>
    private static string ResolveImageFolderPath(IDalamudPluginInterface pluginInterface)
    {
        var configPath = Path.Combine(pluginInterface.ConfigDirectory.FullName, "Images", "Maps") + Path.DirectorySeparatorChar;

        try
        {
            if (Directory.Exists(configPath) && Directory.EnumerateFiles(configPath).Any()) return configPath;

            var assemblyDir = pluginInterface.AssemblyLocation.Directory;
            if (assemblyDir == null) return configPath;

            var oldFolders = new List<string> { Path.Combine(assemblyDir.FullName, "Images", "Maps") };
            //同一個外掛的其他(舊)版本資料夾 —— Dalamud 更新後舊版可能還沒被清掉
            var versionsRoot = assemblyDir.Parent;
            if (versionsRoot != null && versionsRoot.Exists)
            {
                oldFolders.AddRange(versionsRoot.EnumerateDirectories()
                    .Where(d => !string.Equals(d.FullName, assemblyDir.FullName, StringComparison.OrdinalIgnoreCase))
                    .Select(d => Path.Combine(d.FullName, "Images", "Maps")));
            }

            foreach (var oldFolder in oldFolders)
            {
                if (!Directory.Exists(oldFolder)) continue;
                var files = Directory.GetFiles(oldFolder);
                if (files.Length == 0) continue;

                Directory.CreateDirectory(configPath);
                foreach (var file in files)
                {
                    var destination = Path.Combine(configPath, Path.GetFileName(file));
                    if (!File.Exists(destination)) File.Copy(file, destination);
                }
                PluginLog.Log($"Map background images: migrated {files.Length} file(s) from '{oldFolder}' to '{configPath}'");
                break;
            }
        }
        catch (Exception ex)
        {
            PluginLog.Error($"Map background images: could not migrate old image folder - {ex.Message}");
        }

        return configPath;
    }

    private DateTime _lastImageStatusCheck = DateTime.MinValue;

    public void CheckImageStatus()
    {
        // Called every frame the map window is open with images enabled (MapUI.Draw), so throttle
        // the directory scan instead of hitting the filesystem on every single frame.
        if (!DownloadingImages && DateTime.UtcNow - _lastImageStatusCheck < TimeSpan.FromSeconds(2))
            return;
        _lastImageStatusCheck = DateTime.UtcNow;

        //PluginLog.Warning($"allfound:{NotAllImagesFound} | folder:{ImageFolderDoesntExist}");
        if (!Directory.Exists(ImageFolderPath))
        {
            ImageFolderDoesntExist = true;
            return;
        }

        var files = Directory.EnumerateFiles(ImageFolderPath).ToList();
        if (files.Count != HuntMapCount)
        {
            if (DownloadingImages) return; //wait until all images downloaded
            if (files.Count > 0) NotAllImagesFound = true;
        }
        else NotAllImagesFound = false;
        //uhh just noticed if imgs deleted so count < huntmapcount but > 0, not error or download popup occurs

    }

    /// <summary>
    /// 依 <see cref="TrackedMob.GameObjectId"/> 當場向物件表重查一次活的 <see cref="IBattleNpc"/>。
    /// 怪已消失(或格位換人)時回 null,不會回傳懸空的包裝。
    /// </summary>
    private IBattleNpc? ResolveMob(ulong gameObjectId)
    {
        if (gameObjectId == 0) return null;
        return _objectTable.SearchById(gameObjectId) as IBattleNpc;
    }

    /// <summary>
    /// 把追蹤名單解析成這一偵可用的活物件清單,保持原本的加入順序。已消失的怪直接略過。
    /// </summary>
    private List<(HuntRank Rank, IBattleNpc Mob)> ResolveCurrentMobs()
    {
        var resolved = new List<(HuntRank Rank, IBattleNpc Mob)>(_currentMobs.Count);
        foreach (var tracked in _currentMobs)
        {
            var mob = ResolveMob(tracked.GameObjectId);
            if (mob != null) resolved.Add((tracked.Rank, mob));
        }
        return resolved;
    }

    /// <summary>
    /// 清空目前追蹤的狩獵目標名單(對 CurrentMobs 的回傳值 Clear() 沒有作用)。
    /// (刻意不順手 ResetPriorityMob():名單空掉時 GetPriorityMob() 本來就會回 null,
    ///  一併重設 _highestRank 會改變既有顯示行為。)
    /// </summary>
    public void ClearCurrentMobs()
    {
        _currentMobs.Clear();
    }

    public (HuntRank Rank, IBattleNpc? Mob) GetPriorityMob()
    {
        if (_priorityMobId is not { } priorityId) return (_highestRank, null);
        if (_currentMobs.Count == 0) return (_highestRank, null);

        var priorityMob = ResolveMob(priorityId);
        if (priorityMob == null || !IsHunt(priorityMob.NameId))
        {
            _highestRank = HuntRank.B;
            _priorityMobId = null;
            foreach (var hunt in ResolveCurrentMobs()) PriorityCheck(hunt.Mob);
            return (_highestRank, _priorityMobId is { } newId ? ResolveMob(newId) : null);
        }
        return (_highestRank, priorityMob);
    }
    public List<(HuntRank Rank, IBattleNpc Mob)> GetAllCurrentMobsWithRank()
    {
        return ResolveCurrentMobs();
    }

    public void AddToTrain(IBattleNpc mob, uint territoryid, uint mapid, uint instance, string mapName, float zoneMapCoordSize)
    {
        //if mob already exists, update last seen - even if not recording
        if (_trainManager.UpdateLastSeen(mob, instance)) return;
        if (!_trainManager.RecordTrain) return;
        //only record A ranks
#if !DEBUG //record all ranks while debugging coz weird ppl still kill ARR A ranks which makes it hard to find hunts to test with.
        if (GetHuntRank(mob.NameId) != HuntRank.A) return;
#endif
        _trainManager.AddMob(mob, territoryid, mapid, instance, mapName, zoneMapCoordSize);
    }

    //bit much, grew big because I don't plan
    public void AddNearbyMobs(List<IBattleNpc> nearbyMobs, float zoneMapCoordSize, uint territoryId, uint mapid,
        bool aTTS, bool bTTS, bool sTTS, string aTTSmsg, string bTTSmsg, string sTTSmsg,
        bool chatA, bool chatB, bool chatS, string chatAmsg, string chatBmsg, string chatSmsg,
        bool flyTxtA, bool flyTxtB, bool flyTxtS, uint instance,
        // 🔴 刻意不給預設值：有預設值的話新的呼叫端漏傳會靜默變成「永遠不出聲」，
        //    而那跟「使用者自己關掉了」在執行期分不出來。讓編譯器逼每個呼叫端表態。
        bool praiseA, bool praiseB, bool praiseS)
    {
        //compare with old list
        //move old mob set out
        // 上一批的 NameId 快照是在加入當下抄下來的純資料,這裡直接搬過去,
        // 不需要(也不可以)回頭去讀上一偵的 IBattleNpc 包裝。
        _previousMobNameIds.Clear();
        foreach (var tracked in _currentMobs) _previousMobNameIds.Add(tracked.NameId);
        _currentMobs.Clear();
        ResetPriorityMob();

        foreach (var mob in nearbyMobs)
        {   //add in new mobs to current list.
            _currentMobs.Add(new TrackedMob(GetHuntRank(mob.NameId), mob.GameObjectId, mob.NameId));

            PriorityCheck(mob);

            //if exists in old mob set, skip tts + chat
            if (_previousMobNameIds.Contains(mob.NameId)) continue;

            // MarkSeen 是給別的插件（如 TCToolbox 的列車同步）的資料事件，計數也是資料，都不吃通知冷卻。
            MarkSeen?.Invoke(mob.ToHuntTrainMob(territoryId, mapid, instance, MapHelpers.GetMapName(territoryId), zoneMapCoordSize));

            //Do tts and chat stuff
            var rank = GetHuntRank(mob.NameId);
            var notify = PassNotifyCooldown(mob, territoryId, instance);
            switch (rank)
            {
                case HuntRank.A:
                    if (notify)
                    {
                        NewMobFoundTTS(aTTS, aTTSmsg, mob);
                        SendChatMessage(chatA, chatAmsg, territoryId, mapid, instance, mob, zoneMapCoordSize);
                        SendFlyText(rank, mob, flyTxtA);
                        TryPraiseNewMob(praiseA, rank, mob);
                    }
                    ACount++;
                    break;
                case HuntRank.B:
                    if (notify)
                    {
                        NewMobFoundTTS(bTTS, bTTSmsg, mob);
                        SendChatMessage(chatB, chatBmsg, territoryId, mapid, instance, mob, zoneMapCoordSize);
                        SendFlyText(rank, mob, flyTxtB);
                        TryPraiseNewMob(praiseB, rank, mob);
                    }
                    BCount++;
                    break;
                case HuntRank.S:
                case HuntRank.SS: //don't think ss is actually used lol
                    if (notify)
                    {
                        NewMobFoundTTS(sTTS, sTTSmsg, mob);
                        SendChatMessage(chatS, chatSmsg, territoryId, mapid, instance, mob, zoneMapCoordSize);
                        SendFlyText(rank, mob, flyTxtS);
                        TryPraiseNewMob(praiseS, rank, mob);
                    }
                    SCount++;
                    break;
            }
        }
    }

    /// <summary>
    /// 這次「新發現」要不要送使用者看得到／聽得到的通知。通過時當場記下時間，冷卻從上一次真的送出算起。
    /// 換區域／換世界不清表：鍵本身含世界與區域，回到原處時同一隻怪仍在冷卻內就不重複叫。
    /// </summary>
    private bool PassNotifyCooldown(IBattleNpc mob, uint territoryId, uint instance)
    {
        var cooldownMs = (long)_notificationCooldownMinutes * 60_000;
        if (cooldownMs <= 0) return true;

        // id 與世界都在這一幀當場抄成純資料，不留任何包裝。
        var key = new NotifyCooldownKey(_objectTable.LocalPlayer?.CurrentWorld.RowId ?? 0, territoryId, instance, mob.NameId);
        if (_notifyCooldowns.TryPass(key, mob.GameObjectId, Environment.TickCount64, cooldownMs, out var remainingMs, out var firstSuppression))
            return true;

        if (firstSuppression)
            PluginLog.Information($"[發現通知冷卻] {mob.Name}（副本 {instance}）再次進入範圍，冷卻剩 {remainingMs / 1000} 秒，這段時間內不再重複通知。");
        return false;
    }

    #region rework later?

    /// <summary>
    /// 發現新的狩獵目標時請 TataruPraise 念一句。<b>只是第四個通知管道，不改任何既有輸出。</b>
    ///
    /// <para>
    /// 🔴 整條路徑失敗都只會表現成「沒出聲」：對方沒安裝、關著、池裡沒句子、IPC 擲例外，
    /// 全部在 <see cref="TataruPraiseIPC.TryPraise"/> 裡吞掉，狩獵追蹤逐位元不受影響。
    /// </para>
    /// <para>
    /// ⚠️ 冷卻分兩層：<b>節奏</b>由 TataruPraise 自己的逐情境冷卻負責（這邊不重造一套），
    /// 這裡只負責<b>身分去重</b> —— 同一隻怪（<c>GameObjectId</c>）在
    /// <see cref="PraiseDedupeWindowMs"/> 內不會念第二次。
    /// </para>
    /// </summary>
    private void TryPraiseNewMob(bool enabled, HuntRank rank, IBattleNpc mob)
    {
        if (!enabled) return;

        var now = Environment.TickCount64;

        // 過期的先清掉。條目數等於「最近十分鐘看過的獵物」，個位數，直接掃就好。
        if (_praisedMobTicks.Count > 0)
        {
            foreach (var staleId in _praisedMobTicks
                         .Where(kv => now - kv.Value >= PraiseDedupeWindowMs)
                         .Select(kv => kv.Key)
                         .ToList())
            {
                _praisedMobTicks.Remove(staleId);
            }
        }

        // 🔴 id 要在這裡當場抄走 —— mob 是這一偵的包裝，不可以留到之後再解參。
        var id = mob.GameObjectId;
        if (_praisedMobTicks.TryGetValue(id, out var last) && now - last < PraiseDedupeWindowMs) return;

        _praisedMobTicks[id] = now;
        TataruPraiseIPC.TryPraise(_pluginInterface, $"發現 {rank} 階的 {mob.Name}");
    }

    //sent fly text in-game on the player  -- move these sestring colours from here and chatmsg to consts or something
    private void SendFlyText(HuntRank rank, IBattleNpc mob, bool enabled)
    {
        if (!enabled) return;
        var rankSB = new SeStringBuilder();
        var nameSB = new SeStringBuilder();
        switch (rank)
        {
            case HuntRank.A:            //didn't hyphen 'rank' here.. think it looks better
                rankSB.AddUiForeground("A RANK", _aTextColour); // pinkish-red - same as chat msg
                nameSB.AddUiForeground($"{mob.Name}", _aFlyTextColour); //tinted pinkish-red
                _flyTextGui.AddFlyText(FlyTextKind.DamageCritDh, 1, 1, 1, rankSB.BuiltString, nameSB.BuiltString, 16, 2, 0);//last 2 nums don't seem to change anything
                break;
            case HuntRank.B:
                rankSB.AddUiForeground("B RANK", _bTextColour); // blue - same as chat msg
                nameSB.AddUiForeground($"{mob.Name}", _bFlyTextColour); //tinted blue
                _flyTextGui.AddFlyText(FlyTextKind.DamageCritDh, 1, 1, 1, rankSB.BuiltString, nameSB.BuiltString, 16, 2, 0);
                break;
            case HuntRank.S:
            case HuntRank.SS:
                rankSB.AddUiForeground("S RANK", _sFlyTextColour); // dark-red - different from chat msg (goldish) because it stands out more.
                nameSB.AddUiForeground($"{mob.Name}", _sTextColour); //same gold as chat msg
                _flyTextGui.AddFlyText(FlyTextKind.DamageCritDh, 1, 1, 1, rankSB.BuiltString, nameSB.BuiltString, 16, 2, 0);
                break;
        }
    }

    public void SendChatMessage(bool enabled, string msg, uint territoryId, uint mapid, uint instance, IBattleNpc mob, float zoneCoordSize)
    {
        if (!enabled) return;

        var rank = GetHuntRank(mob.NameId);
        var hpp = GetHPP(mob);

        //pattern for matching, (?i) = case insensitive
        string pattern = "(?i)(<flag>|<rank>|<name>|<hpp>" +
                         "|<goldstar>|<silverstar>|<warning>|<nocircle>" +
                         "|<controllerbutton0>|<controllerbutton1>" +
                         "|<priorityworld>|<elementallevel>" +
                         "|<exclamationrectangle>|<notoriousmonster>" +
                         "|<alarm>|<fanfestival>)";
        //splits the string based on above 
        var splitMsg = Regex.Split(msg, pattern);

        var sb = new SeStringBuilder();
        foreach (var s in splitMsg)
        {
            switch (s)
            {
                case "<flag>": //Why doesn't SeStringBuilder.AddMapLink have an overload that takes in placename, while SeString.CreateMapLink does? :( cause null possible?
                    var maplink = SeString.CreateMapLink(territoryId, mapid, MapHelpers.ConvertToMapCoordinate(mob.Position.X, zoneCoordSize),
                        MapHelpers.ConvertToMapCoordinate(mob.Position.Z, zoneCoordSize));
                    sb.AddUiForeground(64); //white
                    sb.Append(maplink);
                    sb.AddUiForegroundOff();
                    break;
                case "<rank>":
                    //idk just test random numbers lmao
                    if (rank == HuntRank.A) sb.AddUiForeground("A-Rank", _aTextColour); //red / pinkish
                    if (rank == HuntRank.B) sb.AddUiForeground("B-Rank", _bTextColour); //blue
                    if (rank == HuntRank.S) sb.AddUiForeground("S-Rank", _sTextColour); //gold 
                    break;
                case "<name>":
                    var name = $"{mob.Name}{LocalizationUtil.GetInstanceGlyph(instance)}";
                    if (rank == HuntRank.A) sb.AddUiForeground(name, _aTextColour); //red / pinkish
                    if (rank == HuntRank.B) sb.AddUiForeground(name, _bTextColour); //blue
                    if (rank == HuntRank.S) sb.AddUiForeground(name, _sTextColour); //gold 
                    break;
                case "<hpp>": //change colour based on initial hp? meh
                    if (Math.Abs(hpp - 100) < 1) sb.AddUiForeground($"{hpp:0}%", 67); //green - ~100% hp
                    if (Math.Abs(hpp - 100) is <= 30 and >= 1) sb.AddUiForeground($"{hpp:0}%", 573); //yellow 70+%
                    if (Math.Abs(hpp - 100) is > 30) sb.AddUiForeground($"{hpp:0}%", 531); //red - below 70%hp
                    break;
                case "<goldstar>":
                    sb.AddIcon(BitmapFontIcon.GoldStar); //think i went a bit overboard lmao
                    break;
                case "<silverstar>":
                    sb.AddIcon(BitmapFontIcon.SilverStar);
                    break;
                case "<warning>":
                    sb.AddIcon(BitmapFontIcon.Warning);
                    break;
                case "<nocircle>":
                    sb.AddIcon(BitmapFontIcon.NoCircle);
                    break;
                case "<controllerbutton0>":
                    sb.AddIcon(BitmapFontIcon.ControllerButton0);
                    break;
                case "<controllerbutton1>":
                    sb.AddIcon(BitmapFontIcon.ControllerButton1);
                    break;
                case "<priorityworld>":
                    sb.AddIcon(BitmapFontIcon.PriorityWorld);
                    break;
                case "<elementallevel>":
                    sb.AddIcon(BitmapFontIcon.ElementalLevel);
                    break;
                case "<exclamationrectangle>":
                    sb.AddIcon(BitmapFontIcon.ExclamationRectangle);
                    break;
                case "<notoriousmonster>":
                    sb.AddIcon(BitmapFontIcon.NotoriousMonster);
                    break;
                case "<alarm>":
                    sb.AddIcon(BitmapFontIcon.Alarm);
                    break;
                case "<fanfestival>":
                    sb.AddIcon(BitmapFontIcon.FanFestival);
                    break;
                default:
                    sb.AddText(s);
                    break;
            }
        }
        _chatGui.Print(sb.BuiltString);
    }

    private void NewMobFoundTTS(bool enabled, string msg, IBattleNpc mob)
    {
        if (!enabled) return;
        var message = FormatMessageFlags(msg, mob);
        if (DontUseSynthesizer) return;

        //changed to creating a new tts each time because SpeakAsync just queues up to play...
        var tts = new SpeechSynthesizer();
        tts.SelectVoice(TTSName);
        tts.Volume = TTSVolume;
        tts.SpeakAsync(message);
        tts.SpeakCompleted += (o, e) =>
        {
            tts.Dispose();
            PluginLog.Verbose($"Finished message: {message} - tts disposed");
        };
    }

    private string FormatMessageFlags(string msg, IBattleNpc mob)
    {
        msg = msg.Replace("<rank>", $"{GetHuntRank(mob.NameId)}-Rank", true, CultureInfo.InvariantCulture);
        msg = msg.Replace("<name>", $"{mob.Name}", true, CultureInfo.InvariantCulture);
        msg = msg.Replace("<hpp>", $"{GetHPP(mob):0}", true, CultureInfo.InvariantCulture);
        return msg;
    }

    #endregion

    public List<IBattleNpc> GetCurrentMobs()
    {
        return ResolveCurrentMobs().Select(hunt => hunt.Mob).ToList();
    }

    public void LoadHuntData()
    {
        var ARRJsonFiles = new List<string>
        {
            "./data/ARR-A.json",
            "./data/ARR-B.json",
            "./data/ARR-S.json",
        };

        var HWJsonFiles = new List<string>
        {
            "./data/HW-A.json",
            "./data/HW-B.json",
            "./data/HW-S.json",
        };
        var SBJsonFiles = new List<string>
        {
            "./data/SB-A.json",
            "./data/SB-B.json",
            "./data/SB-S.json",
        };
        var ShBJsonFiles = new List<string>
        {
            "./data/ShB-A.json",
            "./data/ShB-B.json",
            "./data/ShB-S.json",
        };
        var EWJsonFiles = new List<string>
        {
            "./data/EW-A.json",
            "./data/EW-B.json",
            "./data/EW-S.json"
        };
        var DTJsonFiles = new List<string>
        {
            "./data/DT-A.json",
            "./data/DT-B.json",
            "./data/DT-S.json"
        };


        LoadFilesIntoDic(new[] {
            (_arrDict, ARRJsonFiles),
            (_hwDict, HWJsonFiles),
            (_sbDict, SBJsonFiles),
            (_shbDict, ShBJsonFiles),
            (_ewDict, EWJsonFiles),
            (_dtDict, DTJsonFiles )
        });

        BuildModelIdToRankIndex();
    }

    // 依搜尋優先順序 (arr, hw, sb, shb, ew, dt) 建索引，
    // 用 TryAdd 讓先出現的 era/rank 優先。
    private void BuildModelIdToRankIndex()
    {
        _modelIdToRankIndex.Clear();
        foreach (var dict in new[] { _arrDict, _hwDict, _sbDict, _shbDict, _ewDict, _dtDict })
        {
            foreach (var kvp in dict)
            {
                foreach (var mob in kvp.Value)
                {
                    _modelIdToRankIndex.TryAdd(mob.ModelID, kvp.Key);
                }
            }
        }
    }

    public float GetMapZoneScale(uint mapID)
    {
        //EVERYTHING EXCEPT HEAVENSWARD HAS A SCALE OF 100, BUT FOR SOME REASON HW HAS 95, WHYYYYYY
        if (mapID is >= 397 and <= 402) return 95f;
        return 100f;
    }
    //override tostring?
    public string GetDatabaseAsString()
    {
        var text = string.Format("{0,-4} | {1,-26} | {2,-5} | {3,5}\n" +
                   "--------------------------------------------------\n", "Rank", "Name", " ID", "Enabled");
        text += DictToString(_arrDict);
        text += DictToString(_hwDict);
        text += DictToString(_sbDict);
        text += DictToString(_shbDict);
        text += DictToString(_ewDict);
        text += DictToString(_dtDict);
        return text;
    }
    public bool IsHunt(uint modelID)
    {
        return _modelIdToRankIndex.ContainsKey(modelID);
    }

    public void Dispose()
    {
        _trainManager.SaveHuntTrainRecord();
        if (!DontUseSynthesizer)
            TTS.Dispose();
    }

    public double GetHPP(IBattleNpc mob)
    {
        return Math.Round(((1.0 * mob.CurrentHp) / mob.MaxHp) * 100, 2);
    }

    private void PriorityCheck(IBattleNpc mob)
    {
        var rank = GetHuntRank(mob.NameId);
        if (rank >= _highestRank)
        {
            _highestRank = rank;
            _priorityMobId = mob.GameObjectId;
        }
    }

    private void ResetPriorityMob()
    {
        _highestRank = HuntRank.B;
        _priorityMobId = null;
    }

    public HuntRank GetHuntRank(uint modelID)
    {
        //just default to B if for some reason mob can't be found - shouldn't happen tho...
        return _modelIdToRankIndex.TryGetValue(modelID, out var rank) ? rank : HuntRank.B;
    }

    private KeyValuePair<HuntRank, List<Mob>> SearchDictionaryForModelID(Dictionary<HuntRank, List<Mob>> dict,
        uint modelID)
    {
        return dict.FirstOrDefault(kvp => kvp.Value.Any(m => m.ModelID == modelID));
    }

    private string DictToString(Dictionary<HuntRank, List<Mob>> dic)
    {
        var text = string.Empty;

        foreach (var kvp in dic)
        {
            foreach (var mob in kvp.Value)
            {
                text += string.Format($"{mob.Rank,-4} | {mob.Name,-26} | {mob.ModelID,5} | {mob.IsEnabled,5}\n");
            }

            text += "\n";
        }

        return text += "\n--------------------------------------------------\n";
    }

    private void LoadFilesIntoDic((Dictionary<HuntRank, List<Mob>> dict, List<string> filePaths)[] toLoad)
    {
        foreach (var (dict, filePaths) in toLoad) LoadFilesIntoDic(dict, filePaths);
    }

    private void LoadFilesIntoDic(Dictionary<HuntRank, List<Mob>> dict, List<string> filePaths)
    {
        foreach (var path in filePaths)
        {
            if (!File.Exists(Path.Combine(_pluginInterface.AssemblyLocation.Directory?.FullName!, path)))
            {
                ErrorPopUpVisible = true;
                ErrorMessage += $"File {path} missing... Please replace missing file(s).\n";
                return;
            }
        }
        var A = JsonConvert.DeserializeObject<List<Mob>>(File.ReadAllText(Path.Combine(_pluginInterface.AssemblyLocation.Directory?.FullName!, filePaths[0])));
        var B = JsonConvert.DeserializeObject<List<Mob>>(File.ReadAllText(Path.Combine(_pluginInterface.AssemblyLocation.Directory?.FullName!, filePaths[1])));
        var S = JsonConvert.DeserializeObject<List<Mob>>(File.ReadAllText(Path.Combine(_pluginInterface.AssemblyLocation.Directory?.FullName!, filePaths[2])));

        if (A != null) dict.Add(HuntRank.A, A);
        if (B != null) dict.Add(HuntRank.B, B);
        if (S != null) dict.Add(HuntRank.S, S);
    }


    private bool reload = false;

    /// <summary>下載回來的「The Hunt」底圖(<c>&lt;英文區域名&gt;-data.jpg</c>)。</summary>
    public IDalamudTextureWrap? GetMapImage(string mapName)
    {
        // won't reload if image updated but texture used in last 2 secs
        //https://github.com/goatcorp/Dalamud/blob/ee362acf70d47dd30c46da931f55958010fbf502/Dalamud/Interface/Internal/TextureManager.cs#L416
        //https://github.com/goatcorp/Dalamud/blob/ee362acf70d47dd30c46da931f55958010fbf502/Dalamud/Interface/Internal/TextureManager.cs#L425
        //https://github.com/goatcorp/Dalamud/blob/ee362acf70d47dd30c46da931f55958010fbf502/Dalamud/Interface/Internal/TextureManager.cs#L36

        if (reload) return null;

        var fileName = mapName.Replace(" ", "_") + ("-data.jpg");
        //Default because Empty can override user window opacity
        return Plugin.TextureProvider.GetFromFile(ImageFolderPath + fileName).GetWrapOrDefault();
    }

    /// <summary>下載回來的底圖檔名(用來寫進診斷訊息)。</summary>
    public string GetMapImageFileName(string mapName) => mapName.Replace(" ", "_") + "-data.jpg";

    /// <summary>遊戲內建地圖材質這一幀的取得狀況。</summary>
    /// <remarks>
    /// 存在的理由:<c>GetWrapOrDefault()</c> 對「還在非同步載入」與「載入真的失敗」<b>都回 null</b>,
    /// 兩者在呼叫端完全分不出來。而 Dalamud 的 <c>SharedImmediateTexture</c> 只要 2 秒沒被畫到就會
    /// 釋放自我參考(<c>SelfReferenceDurationTicks = 2000</c>),下次再取必然有幾幀是 null ——
    /// 於是「正常的重新載入」被當成「載不到」報出來。
    /// </remarks>
    public enum GameMapTextureState
    {
        /// <summary>拿到材質了。</summary>
        Ready,
        /// <summary>還在非同步載入中。<b>這不是錯誤</b>,每次材質過期重載都會經過這個狀態。</summary>
        Loading,
        /// <summary>這個區域在遊戲資料裡就沒有地圖(<c>TerritoryType.Map</c> 指到 Id 為空的 Map 列)。</summary>
        NoTexture,
        /// <summary>載入真的失敗(材質載入器擲了例外)。</summary>
        Failed,
    }

    /// <summary>
    /// 遊戲自帶的地圖材質。不需要下載、離線可用、不受外掛更新換資料夾影響,
    /// 而且正好覆蓋完整地圖座標範圍,與標點/玩家圖示對齊。
    /// </summary>
    /// <param name="territoryId">區域 id。</param>
    /// <param name="state">這一幀的取得狀況 —— 呼叫端要靠它區分「還在載入」與「真的失敗」。</param>
    /// <param name="failReason"><paramref name="state"/> 為 <see cref="GameMapTextureState.Failed"/> 時的例外訊息,否則 null。</param>
    public IDalamudTextureWrap? GetGameMapImage(uint territoryId, out GameMapTextureState state, out string? failReason)
    {
        failReason = null;

        var path = MapHelpers.GetGameMapTexturePath(territoryId);
        if (string.IsNullOrEmpty(path))
        {
            state = GameMapTextureState.NoTexture;
            return null;
        }

        try
        {
            //TryGetWrap 是唯一能把「還在載入」與「載入失敗」分開的 API:
            //回 false 且 exception 為 null ＝ 還在載入;exception 非 null ＝ 真的失敗。
            if (Plugin.TextureProvider.GetFromGame(path).TryGetWrap(out var wrap, out var ex))
            {
                state = GameMapTextureState.Ready;
                return wrap;
            }

            if (ex == null)
            {
                state = GameMapTextureState.Loading;
                return null;
            }

            state = GameMapTextureState.Failed;
            failReason = (ex is AggregateException ae ? ae.InnerException ?? ae : ex).Message;
            return null;
        }
        catch (ObjectDisposedException)
        {
            //外掛卸載途中仍會被畫到最後一幀,此時 TextureProvider 已經 dispose。
            //當成「還在載入」靜靜跳過,不要在卸載時噴一串堆疊。
            state = GameMapTextureState.Loading;
            return null;
        }
    }

    public bool DownloadingImages = false;
    public bool HasDownloadErrors = false;
    public List<string> DownloadErrors = new List<string>();

    // 呼叫端（ImGui 按鈕）不等待；例外在方法內收掉，回傳的 Task 不會失敗。
    public async Task DownloadImages(List<MapSpawnPoints> spawnpointdata, Configuration _configuration)
    {
        DownloadingImages = true;
        try
        {
            try
            {   // if redownloading, delete all files first.
                if (Directory.Exists(ImageFolderPath)) Directory.Delete(ImageFolderPath, true);
                Directory.CreateDirectory(ImageFolderPath);
                ImageFolderDoesntExist = false;
            }
            catch (Exception ex)
            {
                DownloadErrors.Add(ex.Message);
            }

            //use spawnpoint data to get map names and generate urls.. coz lazy to retype
            var names = spawnpointdata.Select(x => x.MapName).ToList();
            var urls = names.Select(n => n = Constants.BaseImageUrl + n.Replace(" ", "_") + "-data.jpg").ToList();

            //then async download each image and save to file
            var downloader = new ImageDownloader(urls, ImageFolderPath);
            var results = await downloader.BeginDownloadAsync();
            DownloadErrors.AddRange(results);
            if (DownloadErrors.Count > 0)
            {
                HasDownloadErrors = true;
                PluginLog.Error("Error:");
                DownloadErrors.ForEach(e => PluginLog.Error(e));
            }
            else
            {
                _ = UpdateImageVer(_configuration);

                // for updating current map image drawn, if redownloaded
                reload = true;
                Task.Run(async () => { await Task.Delay(2222); reload = false; });
            }
        }
        catch (Exception ex)
        {
            PluginLog.Warning(ex, "DownloadImages failed");
        }
        finally
        {
            DownloadingImages = false;
        }
    }

    // 呼叫端不等待；例外在方法內收掉，回傳的 Task 不會失敗。
    private async Task UpdateImageVer(Configuration config)
    {
        try
        {
            if (!Directory.Exists(ImageFolderPath)) return;
            var files = Directory.EnumerateFiles(ImageFolderPath).ToList();

            if (files.Count == HuntMapCount) config.MapImagesVer = await MapHelpers.GetMapImageVer();
        }
        catch (Exception ex)
        {
            PluginLog.Warning(ex, "UpdateImageVer failed");
        }
    }

    public bool IsDawntrailHunt(uint mobId)
    {
        return _dtDict.Any(kvp => kvp.Value.Any(m => m.ModelID == mobId));
    }

    public string GetMobNameInEnglish(uint mobId)
    {
        string searchList(List<Mob> list)
        {
            var m = list.FirstOrDefault(m => m.ModelID == mobId)!.Name;
            //PluginLog.Error(m);
            return m;
        }

        var kvp = SearchDictionaryForModelID(_arrDict, mobId);
        if (!kvp.Equals(default(KeyValuePair<HuntRank, List<Mob>>))) return searchList(kvp.Value);
        kvp = SearchDictionaryForModelID(_hwDict, mobId);
        if (!kvp.Equals(default(KeyValuePair<HuntRank, List<Mob>>))) return searchList(kvp.Value);
        kvp = SearchDictionaryForModelID(_sbDict, mobId);
        if (!kvp.Equals(default(KeyValuePair<HuntRank, List<Mob>>))) return searchList(kvp.Value);
        kvp = SearchDictionaryForModelID(_shbDict, mobId);
        if (!kvp.Equals(default(KeyValuePair<HuntRank, List<Mob>>))) return searchList(kvp.Value);
        kvp = SearchDictionaryForModelID(_ewDict, mobId);
        if (!kvp.Equals(default(KeyValuePair<HuntRank, List<Mob>>))) return searchList(kvp.Value);
        kvp = SearchDictionaryForModelID(_dtDict, mobId);
        if (!kvp.Equals(default(KeyValuePair<HuntRank, List<Mob>>))) return searchList(kvp.Value);

        return "mob id not found";
    }

}