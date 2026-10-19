using Dalamud.Plugin;
using Dalamud.Plugin.Ipc.Exceptions;
using System;

namespace HuntHelper.Managers
{
    /// <summary>
    /// 單向橋接到「塔塔露誇獎」(TataruPraise)：發現魔物時請它念一句，把掛機的人叫回電腦前。
    /// </summary>
    /// <remarks>
    /// 🔴 <b>零組件相依。</b>HuntHelper 沒有 ECommons，這裡只用 Dalamud 原生 CallGate 的字串契約，
    /// 也不引用 TataruPraise 的任何型別。兩邊裝／移除任一方都不會弄壞另一邊 ——
    /// 對方沒安裝時本檔的每一條路徑都是 no-op。
    /// <para>
    /// 🔴 契約名逐字取自 TataruPraise 的 <c>IpcContract.cs</c>。CallGate 是純字串比對，
    /// 名字打錯不會有任何錯誤訊息，只會永遠得到「這個頻道沒有人註冊」——<b>靜默斷線</b>。
    /// </para>
    /// <para>
    /// 🔴 <b>只能從主執行緒呼叫。</b>IPC 的實作跑在<b>呼叫端</b>的執行緒上。目前唯一的呼叫點是
    /// <c>HuntManager.AddNearbyMobs</c>，而它只從 <c>MapUI.Draw()</c> 進來（開著地圖時、
    /// 以及背景掃描開啟時的隱藏分支），兩條都在 UI 執行緒上。
    /// </para>
    /// <para>
    /// ⚠️ 這是<b>單向通知</b>：回傳值只拿來寫記錄，<b>不影響狩獵追蹤的任何一步</b>，
    /// 也<b>不會動到既有的聊天／TTS／浮空文字輸出</b>。
    /// </para>
    /// </remarks>
    internal static class TataruPraiseIPC
    {
        /// <summary><c>Func&lt;string, bool&gt;</c>：<b>這一個情境</b>現在出不出得了聲（總開關＋這個情境的開關＋這個情境有已合成的語音）。</summary>
        /// <remarks>📌 刻意<b>不</b>看冷卻：冷卻是「這一次剛好不出聲」，不是「不能出聲」。</remarks>
        internal const string TagIsAvailableFor = "TataruPraise.IsAvailableFor";

        /// <summary><c>Func&lt;string, bool&gt;</c>：從指定情境的誇獎池挑一句念。</summary>
        internal const string TagPraise = "TataruPraise.Praise";

        /// <summary>
        /// 送過去的情境字串。
        /// <para>
        /// 🔴 這個鍵是跨外掛約定好的，<b>一個字都不要改</b>。TataruPraise 拿它當池的鍵，
        /// 對不上就<b>靜默不出聲</b>（<c>Praise</c> 回 <c>false</c>，不擲例外、不寫 log）——
        /// 在執行期跟「使用者把這個情境關掉了」完全分不出來。
        /// </para>
        /// <para>
        /// 📌 用「魔物」不是「獵物」：台服的標準用語是魔物。
        /// </para>
        /// </summary>
        internal const string CategoryHuntFound = "發現魔物";

        /// <summary>
        /// 請塔塔露念一句。對方沒裝、關著、或池裡沒東西，這裡都是安靜的 no-op。
        /// </summary>
        /// <param name="pluginInterface">呼叫端自己的 plugin interface。</param>
        /// <param name="reason">寫進記錄用的來源描述，讓 log 分得出是哪一隻怪觸發的。</param>
        /// <returns>對方真的接受了才回 <c>true</c>；任何失敗都回 <c>false</c>，<b>絕不擲例外</b>。</returns>
        internal static bool TryPraise(IDalamudPluginInterface pluginInterface, string reason)
        {
            if (pluginInterface == null) return false;

            try
            {
                // 先問 IsAvailableFor(情境)：問的是「這一個情境」出不出得了聲——總開關關著、
                // 使用者把這個情境關掉、或這個情境一句已合成的都沒有，都在這裡擋掉。
                // 🔴 不要退回去問 IsAvailable：那個問的是「整池」，於是「別的情境有句子、
                //    我這個情境一句都沒有」時它照樣回 true，這道閘門等於白做。
                // 這一步同時兼作「對方在不在」的探測 —— 沒註冊就會在這裡擲 IpcNotReadyError。
                if (!pluginInterface.GetIpcSubscriber<string, bool>(TagIsAvailableFor).InvokeFunc(CategoryHuntFound)) return false;

                var accepted = pluginInterface.GetIpcSubscriber<string, bool>(TagPraise).InvokeFunc(CategoryHuntFound);

                // Information 級：這是「使用者說沒出聲」時唯一問得出真相的一行。
                PluginLog.Information($"[塔塔露誇獎] {reason}：Praise(「{CategoryHuntFound}」) 回傳 {accepted}。");
                return accepted;
            }
            catch (IpcNotReadyError)
            {
                // 對方沒安裝／還沒載入。完全正常的狀態，刻意不寫 log —— 沒裝的人每次發現怪都會走到這裡。
                return false;
            }
            catch (Exception e)
            {
                // 其他狀況（型別不合、對方在自己的回呼裡爆掉之類）記一筆就好，
                // 絕不要讓它往上冒去打斷 AddNearbyMobs 後面的聊天／TTS／浮空文字。
                PluginLog.Information($"[塔塔露誇獎] 呼叫失敗（{reason}）：{e.Message}");
                return false;
            }
        }
    }
}
