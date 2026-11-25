# HuntHelper

[HuntHelper](https://github.com/img02/HuntHelper) 的台服移植版，狩獵雷達地圖、狩獵列車記錄與 S 階級輔助工具。

## 功能

- **狩獵雷達地圖**：顯示周圍狩獵目標位置
- **發現通知**：依 A／B／S 階級分別設定聊天訊息與語音（TTS）播報；也可請 TataruPraise（塔塔露誇獎）念一句提醒（同一隻怪十分鐘只念一次，需另外安裝該插件）
- **通知冷卻**：同一隻魔物在冷卻時間內只通知一次，在偵測範圍邊緣進進出出不會反覆通知（預設 5 分鐘，在通知分頁的設定調整，0＝關閉）；地圖標示與列車紀錄不受影響
- **狩獵列車記錄**：記錄狩獵列車路線與已巡過的重生點，可匯出／匯入分享
- **重生點記錄**：記錄狩獵目標的重生座標
- **S 階級輔助計數器**：依所在地圖切換對應的 S 階級觸發計數規則
- **指令**：`/hh`（開啟主地圖視窗）、`/hh1`／`/hh2`（以預設大小位置開啟地圖）、`/hh1save`／`/hh2save`（將目前視窗大小位置存為預設）、`/hht`（狩獵列車視窗）、`/hhr`（重生點記錄視窗）、`/hhc`（計數器視窗）、`/hhn`（取得列車下一隻目標座標旗標，可設定自動傳送）、`/hhna`（改用最近乙太之光的座標旗標，會覆蓋並停用 `/hhn` 的自動傳送）

## IPC

- `HH.GetTrainList`：取得狩獵列車記錄的目標清單
- `HH.ImportTrainList`：匯入狩獵列車目標清單
- `HH.GetVersion`：取得 IPC 介面版本號
- `HH.Enable` / `HH.Disable`：HuntHelper 啟用/停用時的發布訂閱通知
- `HH.channel.MarkSeen`：列車上的目標被看到時的發布訂閱通知

## 台服版的修改

- 新增發現魔物時請 TataruPraise 出聲提醒（A／B／S 分級各自開關，預設只開 S 階級）。
- 新增同一隻魔物的發現通知冷卻。
- 修正台服 S 階級觸發計數器用錯語言字串表，補上繁中對照。
- 修正南薩納蘭 F.A.T.E. 計時：F.A.T.E. 失敗後的幾秒內不再一直重設倒數，F.A.T.E. 清單依剩餘時間排序。
- 不再跨幀持有遊戲物件，改存識別碼每次重查。
- 多處降低記憶體用量。
- 介面繁體中文化（台服客戶端預設載入繁中語言檔）。

## 安裝

1. 在遊戲內輸入 `/xlsettings` 開啟「Dalamud 設定」，到「試驗性功能」分頁的「第三方插件倉庫」加入
   `https://raw.githubusercontent.com/ffxiv-tc-port/DalamudPluginsTC/main/repo.json`
   並啟用、儲存。
2. 輸入 `/xlplugins` 開啟「插件管理員」，搜尋「Hunt Helper」安裝。

同一個插件只裝這裡的版本，不要同時裝上游版。

## 需要搭配的插件

- 選用：[TataruPraise](https://github.com/ffxiv-tc-port/TataruPraise)（語音提醒）

原作者：[img02](https://github.com/img02/HuntHelper)

授權：MIT（見 LICENSE）
