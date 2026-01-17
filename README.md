# Anime Folder Organizer (動畫資料夾整理工具)

這是一個基於 WPF (.NET 9) 開發的桌面應用程式，利用 Google Gemini AI 的強大語言理解能力，協助使用者智慧辨識並自動整理動畫資料夾名稱。

## 專案概述

整理大量動畫資料夾通常是一件繁瑣的工作，資料夾名稱可能包含各種不規則的資訊（如字幕組、解析度、集數等）。本專案旨在解決此問題，透過 AI 分析資料夾名稱，提取出動畫的正式標題（支援多語言）與年份，並根據使用者自訂的格式進行標準化重新命名。

### 主要功能

*   **AI 智慧辨識**：整合 Google Gemini API，精準分析資料夾名稱中的動畫資訊。
*   **批量處理**：一次掃描並處理多個資料夾。
*   **自訂命名格式**：支援自訂命名規則（例如：`{Title} ({Year})`）。
*   **多語言支援**：可選擇偏好的命名語言（繁體中文、日文、英文等）。
*   **歷史紀錄**：內建 SQLite 資料庫，記錄所有更名操作，方便追蹤。
*   **預覽功能**：在實際更名更動前，提供新舊名稱對照預覽。

## 技術堆疊

*   **平台**：Windows (WPF)
*   **框架**：.NET 9.0
*   **架構**：MVVM (使用 CommunityToolkit.Mvvm)
*   **依賴注入**：Microsoft.Extensions.DependencyInjection
*   **資料庫**：SQLite (Microsoft.Data.Sqlite)
*   **AI 服務**：Google Gemini API

## 安裝指南

### 事前準備

1.  **作業系統**：Windows 10/11
2.  **執行環境**：需安裝 [.NET 9.0 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/9.0)。
3.  **API Key**：您需要一組 Google Gemini API Key。可至 [Google AI Studio](https://aistudio.google.com/app/apikey) 免費申請。

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
    *   啟動程式後，點擊右上角的「設定 (Settings)」圖示。
    *   在設定視窗中輸入您的 **Google Gemini API Key**。
    *   選擇您偏好的 AI 模型（預設為 `gemini-2.5-flash-lite`）與命名格式。
    *   點擊儲存。

2.  **掃描資料夾**：
    *   回到主畫面，點擊「選擇資料夾」按鈕，選取包含動畫資料夾的根目錄。
    *   程式會列出該目錄下的子資料夾。

3.  **執行分析**：
    *   點擊「分析」或「預覽」按鈕，程式將呼叫 Gemini API 取得建議名稱。
    *   確認列表中的建議名稱是否正確。

4.  **套用更更**：
    *   勾選想要變更的項目。
    *   點擊「執行重新命名」以套用變更。

## 貢獻方式

歡迎任何形式的貢獻！如果您發現 Bug 或有新功能建議，請遵循以下步驟：

1.  Fork 本專案。
2.  建立您的 Feature Branch (`git checkout -b feature/AmazingFeature`)。
3.  提交您的變更 (`git commit -m 'Add some AmazingFeature'`)。
4.  推送到 Branch (`git push origin feature/AmazingFeature`)。
5.  開啟一個 Pull Request。

## 授權資訊

本專案採用 [MIT License](LICENSE) 授權。詳細資訊請參閱 LICENSE 檔案。

---
*注意：本專案使用 Google Gemini API，使用量請遵循 Google AI Studio 的配額限制與服務條款。*
