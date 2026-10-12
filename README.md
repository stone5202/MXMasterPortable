# MX Master Portable

<p align="center">
  <strong>繁體中文</strong> · <a href="README.en.md">English</a>
</p>

<p align="center">
  <img src="src/app.png" width="128" alt="MX Master Portable app icon">
</p>

MX Master Portable 是一款給 Logitech MX Master 4 用的免安裝 Windows 小工具：單一 EXE、不需要管理員權限、不安裝驅動或服務，就能自訂按鍵、手勢與 Actions Ring 圓環，並調整指標速度、SmartShift 與觸覺回饋。適合無法安裝 Logi Options+ 的電腦。

> 目前版本：1.0 · 介面語言：繁體中文、English（預設跟隨系統）

各版本的更新內容請參閱 [更新紀錄](CHANGELOG.md) 或 [GitHub Releases](https://github.com/stone5202/MXMasterPortable/releases)。更新紀錄也提供[英文版本](CHANGELOG.en.md)。

> 本專案為個人開源作品，與 Logitech 無任何關係，也未獲其認可或贊助。Logitech、MX Master、Logi Options+、Logi Bolt 為 Logitech 的商標。

## Screenshots

<p align="center">
  <img src="docs/screenshots/buttons.zh-TW.png" width="80%" alt="按鈕頁：滑鼠示意圖與按鍵對應清單">
</p>

<p align="center">
  <img src="docs/screenshots/ring.zh-TW.png" width="45%" alt="手勢與圓環頁">
  &nbsp;
  <img src="docs/screenshots/pointer.zh-TW.png" width="45%" alt="指標與滾輪頁">
</p>

<p align="center">
  按鈕對應 · 手勢與圓環 · 指標與滾輪
</p>

## 下載與使用

1. 到 [Releases](https://github.com/stone5202/MXMasterPortable/releases/latest) 下載 `MXMasterPortable.exe`。
2. 放到任何你有寫入權限的資料夾（設定檔會存在 EXE 旁邊），直接執行。
3. 程式預設啟動後直接縮到系統匣；按兩下系統匣圖示開啟設定，要結束請用系統匣圖示的選單。

EXE 沒有數位簽章，第一次執行時 Windows SmartScreen 可能會顯示警告。公司電腦若啟用 WDAC／AppLocker／Smart App Control，可能需要由 IT 核准；本程式不會也不應該用來繞過這些安全政策。

## 功能

- **按鍵對應**：中鍵、拇指手勢鍵、兩個側鍵、拇指滾輪左右、Actions Ring 鍵、滾輪模式鍵，各自選擇動作。
- **內建動作**：上一頁／下一頁、複製／貼上／剪下、復原／重做、工作檢視、顯示桌面、切換虛擬桌面、Alt+Tab、播放／暫停、音量與靜音、Enter、Esc。
- **自訂動作**：任意組合鍵（例如 `Ctrl+Shift+T`、`Win+R`），或啟動程式、開啟檔案／資料夾／網址。
- **Actions Ring 圓環**：按住顯示八方向半透明圓環，移動選擇、放開執行；八個方向都可自訂。
- **拇指四向手勢**：按住手勢鍵並滑動，左右換頁、上工作檢視、下顯示桌面。
- **裝置設定**：顯示電量與韌體版本；調整指標速度（DPI）、SmartShift 與觸覺回饋強度。套用過的數值會保存，下次啟動時自動重新套用。
- **連線方式**：Bluetooth LE 與 Logi Bolt 接收器。
- **其他**：所有設定自動儲存、只會執行一份、可從系統匣暫停、介面可切換中文／英文。
- **工程模式**：檢視滑鼠送出的原始 HID 事件，方便辨識按鍵或回報問題。

## 系統需求

- Windows 10 或 Windows 11
- .NET Framework 4.x（Windows 內建，不需另外安裝）
- Logitech MX Master 4（Bluetooth LE 或 Logi Bolt）

## 已知限制

- 只在 MX Master 4（韌體 `RBM 27.03`）上開發與測試；其他型號可能只有標準按鍵對應能用。
- 不包含 Options+ 的 Flow、Easy‑Switch 管理、逐應用程式設定與韌體更新。
- 拇指四向手勢的方向動作目前是固定的。
- 裝置設定只在程式啟動時重新套用一次；滑鼠在程式執行中斷線重連後不會再套。
- 低階滑鼠攔截無法分辨來源裝置：同時接多隻滑鼠時，標準側鍵與水平滾輪的對應也會套用到其他滑鼠。
- 與 Logi Options+ 同時執行時，兩者可能互相覆寫按鍵改道與裝置設定，建議擇一使用。
- 示意圖上的按鍵位置為示意，尚未逐一對照實體校正。

## 運作方式

- 標準按鍵與水平滾輪：Windows 低階滑鼠攔截（`WH_MOUSE_LL`）＋ `SendInput` 送出鍵盤動作。
- 專用按鍵（拇指手勢鍵 `0x00C3`、Actions Ring 鍵 `0x01A0`）：透過 Windows 內建 HID 介面，以 HID++ 2.0 的 `0x1B04` 功能設定「暫時改道」，再用 Raw Input 接收按鍵通知。程式暫停或結束時會解除改道。
- 裝置設定：HID++ 功能 `0x1004`（電量）、`0x0003`（韌體）、`0x2201`（DPI）、`0x2111`（SmartShift）、`0x19B0`（觸覺回饋）。每次寫入後都會回讀確認。

HID++ 的功能編號與封包格式參考了開源專案 [Solaar](https://github.com/pwr-Solaar/Solaar) 與 [logiops](https://github.com/PixlOne/logiops) 的公開實作。

## 隱私

- 程式不會建立任何網路連線，也不包含分析、廣告或追蹤元件。
- 設定儲存在 EXE 旁邊的 `mxmaster-portable.ini`。
- 只有在工程模式開啟記錄時，才會把滑鼠事件寫入 EXE 旁邊的 `mxmaster-events.log`；記錄內容是按鍵與 HID 封包，不含鍵盤輸入。
- 不安裝驅動、服務、排程工作或開機啟動項目，也不修改登錄檔。

## 從原始碼建置

不需要安裝 Visual Studio 或 .NET SDK，使用 Windows 內建的 .NET Framework C# 編譯器即可：

```powershell
git clone https://github.com/stone5202/MXMasterPortable.git
cd MXMasterPortable
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

輸出為 `build\MXMasterPortable.exe`。整個程式是單一原始檔 `src/Program.cs`（C# 5、WinForms），沒有外部相依套件。

## 專案結構

```text
src/
├── Program.cs        # 全部程式碼
├── app.ico           # EXE 與視窗圖示
└── app.png           # 介面內的 logo
tools/
└── make-icon.ps1     # 產生 app.ico／app.png
docs/
└── screenshots/      # README 用的畫面截圖
build.ps1             # 建置腳本
```

## 參與開發

歡迎提交 issue 或 pull request。回報按鍵或裝置相關問題時，請附上工程模式的記錄檔，以及連線方式（Bluetooth 或 Bolt）與滑鼠韌體版本。

送出變更前，請確認 `build.ps1` 能成功建置且沒有警告，並維持免安裝、免管理員權限、無外部相依的原則。

## License

本專案採用 [MIT License](LICENSE)。
