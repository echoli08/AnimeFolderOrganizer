# Anime Folder Organizer (動畫資料夾整理工具)

這是一個基於 WPF (.NET 9) 開發的桌面應用程式，利用 AI 服務（Google Gemini / OpenRouter / Groq / DeepSeek）的語言理解能力，協助使用者智慧辨識並自動整理動畫資料夾名稱。

## 專案概述

整理大量動畫資料夾通常是一件繁瑣的工作，資料夾名稱可能包含各種不規則的資訊（如字幕組、解析度、集數等）。本專案旨在解決此問題，透過 AI 分析資料夾名稱，提取出動畫的正式標題（支援多語言）與年份，並根據使用者自訂的格式進行標準化重新命名。

### 主要功能

*   **AI 智慧辨識**：整合 Google Gemini / OpenRouter / Groq / DeepSeek 轉發 API，精準分析資料夾名稱中的動畫資訊。
*   **批量處理**：一次掃描並處理多個資料夾。
*   **自訂命名格式**：支援自訂命名規則（例如：`{Title} ({Year})`），並提供 `{Type}`（TV / OVA / 特別版 / 劇場版）。
*   **多語言支援**：可選擇偏好的命名語言（繁體中文、日文、英文等）。
*   **模型可用性偵測**：自動偵測並快取可用的 AI 模型清單。
*   **作品驗證**：將辨識的日文名稱與 AnimeDB 進行比對，顯示「已辨識 / 驗證失敗」狀態。
*   **官方名稱補正**：整合 TMDB → Bangumi → AniList API，自動補全官方繁體中文、簡體中文與英文標題。
*   **歷史紀錄**：內建 SQLite 資料庫，記錄所有更名操作，並支援還原功能。
*   **預覽功能**：在實際更名更動前，提供新舊名稱對照預覽，並顯示動畫類別。
*   **多選重新辨識**：支援多選資料夾批次重新辨識，減少 API 請求次數。
*   **掃描記錄**：提供掃描與辨識的詳細紀錄，方便除錯。

### 未來預計功能
*   **資料夾歸檔**：自動依照年份或字母將資料夾歸檔。
*   **一般電影支援**：擴充辨識邏輯以支援非動畫類電影。
*   **產生 Metadata**：自動產生 `.nfo` 或 `tvshow.nfo` 檔案以供媒體伺服器（如 Plex, Emby）使用。

### API 注意事項
目前支援 Gemini, DeepSeek, Groq, OpenRouter 等多種 API 來源。
AI 辨識無法保證 100% 準確，建議在執行改名透過預覽功能檢查，或使用內建的「重新辨識」功能進行修正。

## ScreenShot

![Main](ScreenShot/01.jpg)
![Scan](ScreenShot/02.jpg)
![History](ScreenShot/03.jpg)
![Settings](ScreenShot/04.jpg)

## 技術堆疊

*   **平台**：Windows (WPF)
*   **框架**：.NET 9.0
*   **架構**：MVVM (CommunityToolkit.Mvvm)
*   **依賴注入**：Microsoft.Extensions.DependencyInjection
*   **資料庫**：SQLite (Microsoft.Data.Sqlite)
*   **AI 服務**：Google Gemini API / OpenRouter / Groq / DeepSeek Proxy

## 安裝指南

### 事前準備

1.  **作業系統**：Windows 10/11
2.  **執行環境**：需安裝 [.NET 9.0 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/9.0)。
3.  **API Key**：
    *   Google Gemini / OpenRouter / Groq / DeepSeek 轉發 其一（必要）。
    *   TMDB API Key（用於官方名稱補正，可選）。
    *   Gemini 可至 [Google AI Studio](https://aistudio.google.com/app/apikey) 申請；OpenRouter 可至 [OpenRouter](https://openrouter.ai/keys) 申請；Groq 可至 [Groq Console](https://console.groq.com/keys) 申請。
    *   DeepSeek 轉發請參考 [GPT_API_free](https://github.com/chatanywhere/GPT_API_free)，支援切換 Base URL（預設為 `https://api.chatanywhere.org/v1` 或 `https://api.chatanywhere.tech/v1`）。

### 建置步驟

如果您是開發者並希望自行編譯原始碼：

1.  複製專案庫：
    ```bash
    git clone https://github.com/yourusername/AnimeFolderOrganizer.git
    cd AnimeFolderOrganizer
    ```

2.  使用 Visual Studio 2022 開啟 `AnimeFolderOrganizer.sln` 或使用 CLI 建置：
    ```bash
    dotnet build
    ```

3.  執行程式：
    ```bash
    dotnet run --project AnimeFolderOrganizer
    ```

## 使用說明

1.  **初次設定**：
    *   啟動程式後，點擊右上角的「設定」。
    *   **API 金鑰設定**：在對應的頁籤中輸入 API Key，並點擊「偵測可用模型」載入模型清單。
    *   **DeepSeek 設定**：若使用 DeepSeek 轉發，可選擇預設的 Base URL 或自行輸入。
    *   **模型選擇**：選擇 API 來源與模型。
    *   **一般設定**：設定命名格式與偏好語言。
    *   點擊「儲存並關閉」。

2.  **掃描資料夾**：
    *   回到主畫面，點擊「選擇資料夾」按鈕，選取包含動畫資料夾的根目錄。
    *   程式會列出該目錄下的子資料夾。

3.  **掃描與辨識**：
    *   點擊「掃描資料夾」，程式將呼叫 AI API 取得建議名稱。
    *   作品名稱會與 AnimeDB 比對，欄位顯示「已辨識 / 驗證失敗」。

4.  **套用更名**：
    *   確認清單中的建議名稱。
    *   點擊「執行改名」以套用變更。

5.  **快速切換模型**：
    *   主畫面右上方可直接切換 API 與模型。

## 貢獻方式

歡迎任何形式的貢獻！如果您發現 Bug 或有新功能建議，請遵循以下步驟：

1.  Fork 本專案。
2.  建立您的 Feature Branch (`git checkout -b feature/AmazingFeature`)。
3.  提交您的變更 (`git commit -m 'Add some AmazingFeature'`)。
4.  推送到 Branch (`git push origin feature/AmazingFeature`)。
5.  開啟一個 Pull Request。

## 授權資訊

本專案採用 [MIT License](LICENSE) 授權。詳細資訊請參閱 LICENSE 檔案。
