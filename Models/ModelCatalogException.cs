using System;

namespace AnimeFolderOrganizer.Models;

/// <summary>
/// 模型目錄服務擲回的例外類別，包含 API 錯誤的詳細資訊。
/// </summary>
public class ModelCatalogException : Exception
{
    /// <summary>
    /// 取得錯誤來源的 API 提供者 (如 Gemini、CustomApi)。
    /// </summary>
    public ApiProvider Provider { get; }

    /// <summary>
    /// 取得 HTTP 狀態碼 (如有)。
    /// </summary>
    public int? StatusCode { get; }

    /// <summary>
    /// 取得錯誤類型的簡短描述。
    /// </summary>
    public string ErrorType { get; }

    /// <summary>
    /// 初始化 ModelCatalogException 的新執行個體。
    /// </summary>
    /// <param name="provider">API 提供者類型。</param>
    /// <param name="errorType">錯誤類型描述 (繁體中文)。</param>
    /// <param name="message">詳細錯誤訊息。</param>
    /// <param name="statusCode">HTTP 狀態碼 (可為 null)。</param>
    /// <param name="innerException">內部例外。</param>
    public ModelCatalogException(
        ApiProvider provider,
        string errorType,
        string message,
        int? statusCode = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Provider = provider;
        ErrorType = errorType;
        StatusCode = statusCode;
    }
}
