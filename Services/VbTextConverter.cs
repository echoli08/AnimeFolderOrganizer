using Microsoft.VisualBasic;

namespace AnimeFolderOrganizer.Services;

public class VbTextConverter : ITextConverter
{
    public string? ToTraditional(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;

        try
        {
            return Strings.StrConv(text, VbStrConv.TraditionalChinese, 0);
        }
        catch
        {
            // 轉換失敗則回傳原文
            return text;
        }
    }

    public string? ToSimplified(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;

        try
        {
            return Strings.StrConv(text, VbStrConv.SimplifiedChinese, 0);
        }
        catch
        {
            // 轉換失敗則回傳原文
            return text;
        }
    }
}
