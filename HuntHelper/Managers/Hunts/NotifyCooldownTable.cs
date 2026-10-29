using System.Collections.Generic;

namespace HuntHelper.Managers.Hunts;

/// <summary>發現通知冷卻的鍵。同一格（世界＋區域＋副本號＋魔物）同時只會有一隻活著，所以一格一筆。</summary>
internal readonly record struct NotifyCooldownKey(uint WorldId, uint TerritoryId, uint Instance, uint NameId);

/// <summary>
/// 同一隻魔物的發現通知冷卻表。純資料、不碰任何遊戲物件。
/// 契約：只在單一執行緒（UI 執行緒）使用，不加鎖；冷卻從上一次「真的通知」算起，被擋下的那幾次不延長冷卻。
/// 值另記 <c>GameObjectId</c>：同一格換了另一隻（被打倒後重新湧現）視為新的，照常通知。
/// </summary>
internal sealed class NotifyCooldownTable
{
    /// <summary>過期清理之後仍達到這個數量就整表清空，防止任何意外讓表無限長。</summary>
    public const int MaxEntries = 512;

    private struct Entry
    {
        public ulong GameObjectId;
        public long NotifiedTick;
        public bool SuppressionReported;
    }

    private readonly Dictionary<NotifyCooldownKey, Entry> _entries = new();

    public int Count => _entries.Count;

    /// <summary>
    /// 回 true＝這次要通知（同時記下 <paramref name="now"/>）。回 false 時 <paramref name="remainingMs"/> 是剩餘冷卻，
    /// <paramref name="firstSuppression"/> 只在這個冷卻窗內第一次被擋時為 true（給呼叫端決定要不要寫一行紀錄）。
    /// <paramref name="cooldownMs"/> ≤ 0 時永遠回 true、不記錄。
    /// </summary>
    public bool TryPass(NotifyCooldownKey key, ulong gameObjectId, long now, long cooldownMs, out long remainingMs, out bool firstSuppression)
    {
        remainingMs = 0;
        firstSuppression = false;
        if (cooldownMs <= 0) return true;

        Prune(now, cooldownMs);

        if (_entries.TryGetValue(key, out var entry)
            && entry.GameObjectId == gameObjectId
            && now - entry.NotifiedTick < cooldownMs)
        {
            remainingMs = cooldownMs - (now - entry.NotifiedTick);
            if (!entry.SuppressionReported)
            {
                firstSuppression = true;
                entry.SuppressionReported = true;
                _entries[key] = entry;
            }
            return false;
        }

        if (_entries.Count >= MaxEntries) _entries.Clear();
        _entries[key] = new Entry { GameObjectId = gameObjectId, NotifiedTick = now };
        return true;
    }

    private void Prune(long now, long cooldownMs)
    {
        if (_entries.Count == 0) return;
        List<NotifyCooldownKey>? expired = null;
        foreach (var kv in _entries)
        {
            if (now - kv.Value.NotifiedTick >= cooldownMs) (expired ??= new()).Add(kv.Key);
        }
        if (expired == null) return;
        foreach (var k in expired) _entries.Remove(k);
    }
}
