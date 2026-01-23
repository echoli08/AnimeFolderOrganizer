using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace AnimeFolderOrganizer.Models
{
    internal static class ApiPrompt
    {
        /// <summary>
        /// 系統角色提示（設定 AI 專業角色）
        /// </summary>
        public static readonly string SystemPrompt =
@"You are an Anime Metadata Expert.
You specialize in identifying official anime titles and cleaning noisy folder names for database usage.";

        /// <summary>
        /// 動漫資料辨識規則（主任務 Prompt）
        /// </summary>
        public static readonly string IdentificationPrompt =
@"Your task is to identify the OFFICIAL anime series title from folder names.

STRICT RULES:

1. Identify, Do Not Translate:
- Do not literally translate folder names. Recognize the real anime series and return official database titles.
- Do NOT self-translate between languages (JP/CN/TW/EN). Only output official titles as released.

2. Ignore Noise:
Ignore resolution tags, codecs, release groups, years in brackets, rip formats, hashes, and technical labels.
Keep useful disambiguation hints (e.g., season number, movie/OVA keyword, part/cour) only to choose the correct title/type/year, but do not include them inside the title fields.

3. Official Titles:
- titleJP: Official Japanese title (Kanji/Kana).
- titleTW: Official Taiwan licensed release title ONLY (Muse, Aniplex Taiwan, Bahamut, theatrical distributor). If not officially released in Taiwan, leave empty string.
- titleCN: Official mainland China licensed title only. If unknown, leave empty string.
- titleEN: Official English release title if exists. If unknown, leave empty string.
DO NOT guess or self-translate.

4. Year:
Use FIRST official release year (TV broadcast start or theatrical premiere year).

5. Type Rules:
TV = broadcast series
Movie = theatrical release
OVA = direct-to-video release
Special = TV special episode or bonus episode
特別版 = compilation movie or director's cut edition

6. No Guessing:
If identification is uncertain, set all title fields to empty string and set confidence < 0.4. DO NOT guess.

7. Multi Input Rule:
Each input line represents one item.
Preserve input order.
index must start from 0 and match input order.

8. Output Format:
Return ONLY JSON with the exact schema provided. No markdown. No explanations.

9. Confidence Scale:
1.00 = exact official match
0.8-0.95 = high confidence
0.5-0.8 = partial match
<0.5 = uncertain.";

        /// <summary>
        /// 回傳 JSON Schema 規範（固定輸出格式，避免模型亂輸出）
        /// </summary>
        public static readonly string OutputSchemaPrompt =
@"Return ONLY a JSON object with this structure (no markdown, no extra keys, no explanations):

{
  ""items"": [
    {
      ""index"": 0,
      ""id"": """",
      ""titleJP"": """",
      ""titleCN"": """",
      ""titleTW"": """",
      ""titleEN"": """",
      ""type"": ""TV|OVA|特別版|Special|Movie"",
      ""year"": 2024,
      ""confidence"": 0.95
    }
  ]
}";

        /// <summary>
        /// 組合完整 Prompt（建議實際送 API 使用）
        /// folderNames: 每行一筆資料（你可用 string.Join("\n", lines) 傳入）
        /// </summary>
        public static string BuildUserPrompt(string folderNames)
        {
            if (folderNames == null) folderNames = string.Empty;

            return
$@"{IdentificationPrompt}

Folder Names:
{folderNames}

{OutputSchemaPrompt}";
        }

        /// <summary>
        /// 提供 Prompt 組集合（方便切換或單獨測試）
        /// </summary>
        public static Dictionary<string, string> GetAllPrompts()
        {
            return new Dictionary<string, string>
            {
                { "System", SystemPrompt },
                { "Identification", IdentificationPrompt },
                { "OutputSchema", OutputSchemaPrompt }
            };
        }

        /// <summary>
        /// （推薦）由後端生成穩定 ID：以 titleJP + year + type 做 SHA1，取前 12 碼小寫
        /// - 穩定：同樣輸入永遠得到同樣 id
        /// - 可重現：不受模型溫度、上下文影響
        /// </summary>
        public static string GenerateStableId(string titleJP, int year, string type)
        {
            // 依規格：空值也要穩定處理
            titleJP = titleJP ?? string.Empty;
            type = type ?? string.Empty;

            // 組合 key（可依需求調整分隔符號，但建議固定）
            var key = $"{titleJP}|{year}|{type}".Trim();

            // SHA1 計算
            using (var sha1 = SHA1.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(key);
                var hashBytes = sha1.ComputeHash(bytes);

                // 轉 hex 小寫
                var sb = new StringBuilder(hashBytes.Length * 2);
                for (int i = 0; i < hashBytes.Length; i++)
                {
                    sb.Append(hashBytes[i].ToString("x2"));
                }

                // 取前 12 碼（符合你的長度規則）
                return sb.ToString().Substring(0, 12);
            }
        }
    }
}
