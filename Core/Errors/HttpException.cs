namespace AlegacyWebPanel.Core.Errors;

public sealed class HttpException(int statusCode, string title, string detail) : Exception(detail)
{
    public int StatusCode { get; } = statusCode;
    public string Title { get; } = title;
}
