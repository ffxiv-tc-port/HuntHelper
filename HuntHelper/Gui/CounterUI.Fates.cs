using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Fates;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using HuntHelper.Gui.Resource;
using HuntHelper.Utilities;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;

namespace HuntHelper.Gui;

public unsafe partial class CounterUI : IDisposable
{
    #region Nunni - Fates - Southern Than
    private string _lastFailedFateInfo = string.Empty;
    private readonly int fateRemainingTimeOffset = 0; //changes every patch or something? note to self: this is subtracted from time remaining
    private DateTime _startTime = DateTime.Now;

    // 只存 FateId 與當幀抄下的純量：IFate 包裝的每個屬性（含 Equals／GetHashCode）都解參考 FateContext，
    // 跨幀持有＝F.A.T.E. 結束後讀已釋放的記憶體。以下欄位只在框架執行緒讀寫。
    private readonly List<TrackedFate> _currentFates = new();
    private readonly List<TrackedFate> _fatesToRemove = new();
    private long _fateResortDueTick; // 0＝沒有待辦的重排；換區後等 1 秒再依剩餘時間重排一次

    private sealed class TrackedFate
    {
        public ushort FateId;
        public string Name = string.Empty;
        public FateState State;
        public byte Progress;
        public int StartTimeEpoch;
        public short Duration;
        public Vector3 Position;
        public bool SeenThisFrame;

        // 與 Dalamud IFate.TimeRemaining 同一條式子。
        public long TimeRemaining => StartTimeEpoch + Duration - DateTimeOffset.Now.ToUnixTimeSeconds();
        public long SortKey => TimeRemaining > 0 ? TimeRemaining : long.MaxValue;
    }

    private readonly Vector4 _green = new Vector4(0, 1, 0, 1);
    private readonly Vector4 _red = new Vector4(1, 0, 0, 1);
    private readonly Vector4 _yellow = new Vector4(1, 1, 0, 1);

    private void DrawNunyunuwiCounter()
    {
        var endTime = _startTime.AddHours(1);

        ImGui.PushFont(UiBuilder.MonoFont);

        DrawNunWindowString(endTime);

        ImGuiUtil.ImGui_Separator(6);

        DrawNunCountdownString(endTime);

        ImGuiUtil.ImGui_Separator(6);

        DrawCurrentFateUI();

        ImGuiUtil.ImGui_Separator(6);

        ImGui.TextColored(_red, _lastFailedFateInfo);

        ImGuiUtil.ImGui_Separator(10);
        DrawNunResetButton();

        ImGui.PopFont();
    }

    private void DrawNunWindowString(DateTime endTime)
    {
        var startTimeString = _startTime.ToString("h:mm:ss tt", CultureInfo.InvariantCulture);
        var endTimeString = endTime.ToString("h:mm:ss tt", CultureInfo.InvariantCulture);

        ImGui.Text($"{startTimeString} - {endTimeString}\n");
        ImGui.SameLine();
        Utilities.ImGuiUtil.ImGui_HelpMarker(GuiResources.CounterGuiText["FateMayFinishEarlierToolTip"]);
    }

    private void DrawNunCountdownString(DateTime endTime)
    {
        var countdown = endTime.Subtract(DateTime.Now);
        if (countdown.TotalSeconds < 0) countdown = TimeSpan.Zero;
        var countdownString = (countdown).ToString(@"mm\:ss", CultureInfo.InvariantCulture);

        ImGui.Text(GuiResources.CounterGuiText["FateCountdownLabel"]);
        ImGui.SameLine();
        if (countdown.TotalSeconds > 300) ImGui.Text($"{countdownString}");
        else ImGui.TextColored(_green, $"{countdownString}");
    }

    private void DrawNunResetButton()
    {
        if (ImGui.Button(GuiResources.CounterGuiText["FateResetButton"]))
        {
            _startTime = DateTime.Now;
            _lastFailedFateInfo = string.Empty;
        }
    }

    private void FateUpdateLoop()
    {
        PopulateFateList();
        UpdateCurrentFates();
    }

    private TrackedFate? FindTrackedFate(ushort fateId)
    {
        foreach (var tracked in _currentFates)
        {
            if (tracked.FateId == fateId) return tracked;
        }
        return null;
    }

    private void PopulateFateList()
    {
        foreach (var tracked in _currentFates)
            tracked.SeenThisFrame = false;

        var added = false;
        foreach (var fate in _fateTable)
        {
            if (fate == null) continue;

            var fateId = fate.FateId;
            var tracked = FindTrackedFate(fateId);
            if (tracked == null)
            {
                tracked = new TrackedFate { FateId = fateId };
                _currentFates.Add(tracked);
                added = true;
            }

            tracked.State = fate.State;
            tracked.Progress = fate.Progress;
            tracked.StartTimeEpoch = fate.StartTimeEpoch;
            tracked.Duration = fate.Duration;
            tracked.Position = fate.Position;
            if (tracked.Name.Length == 0) tracked.Name = fate.Name.ToString();
            tracked.SeenThisFrame = true;
        }

        var resortDue = _fateResortDueTick != 0 && Environment.TickCount64 >= _fateResortDueTick;
        if (resortDue) _fateResortDueTick = 0;
        if (added || resortDue) SortCurrentFates();

#if DEBUG
        ImGui.PushFont(UiBuilder.MonoFont);
        if (ImGui.CollapsingHeader("DEBUG FATE INFO"))
        {
            ImGui.Text($"{"Name",-35} -> {"Dura",4} | " +
                       $"{"Rem.",5} | " +
                       $"{"Prog",4} : {"State",12} | " +
                       $"{"Start",10} | " +
                       $"{"ID",4} | " +
                       $"{"Pos"}");
            foreach (var fate in _fateTable)
            {
                ImGui.Text($"{fate.Name,-35} -> {fate.Duration,4} | " +
                           $"{new TimeSpan(0, 0, 0, (int)(fate.TimeRemaining - fateRemainingTimeOffset)).ToString(@"mm\:ss"),4} | " +
                           $"{fate.Progress,4} : {fate.State,12} | " +
                           $"{fate.StartTimeEpoch,10} | " +
                           $"{fate.FateId,4} | " +
                           $"{fate.Position}");
            }
        }
        ImGui.PopFont();
#endif
    }

    // 穩定排序（同剩餘時間者保持加入順序）；只在有新 F.A.T.E. 或換區後重排時呼叫。
    private void SortCurrentFates()
    {
        var sorted = _currentFates.OrderBy(f => f.SortKey).ToList();
        _currentFates.Clear();
        _currentFates.AddRange(sorted);
    }

    private void UpdateCurrentFates()
    {
        foreach (var cf in _currentFates)
        {
            if (!cf.SeenThisFrame)
            {
                // 已不在 F.A.T.E. 表：只能用最後一次看到的狀態判斷。
                // (!) fate that required [user activation] was not activated in time and disappeared
                if (cf.State == FateState.Preparation)
                    RecordFateFailure(cf, "FAIL: NOT INITIATED");
                else
                    PluginLog.Log($"[HuntHelper] F.A.T.E.「{cf.Name}」({cf.FateId}) 在狀態 {cf.State}、進度 {cf.Progress}% 時從列表消失，不計為失敗。");
                _fatesToRemove.Add(cf);
                continue;
            }

            if (cf.State == FateState.Ended)
            {
                _fatesToRemove.Add(cf);
                continue;
            }

            if (cf.State == FateState.Failed)
            {
                RecordFateFailure(cf, "FAIL");
                _fatesToRemove.Add(cf);
            }
        }

        if (_fatesToRemove.Count == 0) return;
        foreach (var cf in _fatesToRemove)
            _currentFates.Remove(cf);
        _fatesToRemove.Clear();
    }

    private void RecordFateFailure(TrackedFate cf, string failReason)
    {
        _lastFailedFateInfo = $"{failReason}\n" +
                              $"{cf.Name} @ {cf.Progress}%%\n\n" +
                              $"> {DateTime.Now.ToString("hh:mm:ss tt", CultureInfo.InvariantCulture)} <\n\n" +
                              $"Countdown reset.";
        _startTime = DateTime.Now;
    }

    private void DrawCurrentFateUI()
    {
        if (ImGui.CollapsingHeader(GuiResources.CounterGuiText["FatesHeader"] + "###FatesHeader"))
        {
            foreach (var cf in _currentFates)
            {
                DrawFateInfoUI(cf);
            }
        }
        ImGuiUtil.ImGui_HoveredToolTip(GuiResources.CounterGuiText["FateOpenMapToolTip"]);
    }
    private void DrawFateInfoUI(TrackedFate cf)
    {
        var fateText = GetFateInfoString(cf);
        var fateTime = GetFateTimeString(cf);

        ImGui.TextUnformatted(fateText);
        if (ImGui.IsItemClicked()) OpenMapOnFateClick(cf);
        Utilities.ImGuiUtil.ImGui_HoveredToolTip($"{cf.Name}");

        ImGui.SameLine();

        switch (cf.TimeRemaining)
        {
            case > 0 and <= 67:
                ImGui.TextColored(_red, fateTime);
                break;
            case > 67 and < 127:
                ImGui.TextColored(_yellow, fateTime);
                break;
            default:
                ImGui.TextUnformatted(fateTime);
                break;
        }
        if (ImGui.IsItemClicked()) OpenMapOnFateClick(cf);
    }

    private string GetFateInfoString(TrackedFate fate)
    {
        var fateName = fate.Name;
        if (fateName.Length > 16) fateName = fateName.Substring(0, 14) + "..";
        return $"{fateName,-16} {fate.Progress,3}%  -> ";
    }

    private string GetFateTimeString(TrackedFate fate)
    {
        return fate.State != FateState.Preparation
            ? $"{new TimeSpan(0, 0, 0, (int)(fate.TimeRemaining - fateRemainingTimeOffset)).ToString(@"mm\:ss", CultureInfo.InvariantCulture)}"
            : GuiResources.CounterGuiText["FateUpcomingMessage"];
    }

    private void OpenMapOnFateClick(TrackedFate fate)
    {
        try
        {
            // 外面那個 try 只擋得住 AgentMap.Instance() 的其中一種失效模式(特徵碼失配時擲的 InvalidOperationException)。
            // 另一種是回 null:AgentMap 由 [Agent(AgentId.Map)] 產生,內部鏈 AgentModule -> UIModule -> Framework
            // 任一層回 null 整條就回 null(登入前、切場景時是常態),裸解參考是 try/catch 攔不到的 AccessViolationException ⇒ 必須事前判空。
            // fail-closed:取不到就不開地圖(使用者再點一次即可)。
            var ag = AgentMap.Instance();
            if (ag == null)
            {
                // 要使用者回報的診斷:本 repo 的 PluginLog.Log 對應 IPluginLog.Info(Information 等級)。
                PluginLog.Log("[HuntHelper] AgentMap 尚未就緒,這次不開啟地圖。");
                return;
            }

            var mapId = ag->CurrentMapId;
            var territoryId = ag->CurrentTerritoryId;
            var pos = Dalamud.Utility.MapUtil.WorldToMap(fate.Position, ag->CurrentOffsetX, ag->CurrentOffsetY);

            var mapLink = new MapLinkPayload(territoryId, mapId, pos.X, pos.Y);
            _gameGui.OpenMapWithMapLink(mapLink);
        }
        catch (Exception e)
        {
            PluginLog.Error(e.ToString());
        }
    }
    private void ClientState_TerritoryChanged(ushort e)
    {
        _currentFates.Clear();
        _startTime = DateTime.Now;
        _lastFailedFateInfo = string.Empty;
        _fateResortDueTick = Environment.TickCount64 + 1000;
    }

    #endregion

}