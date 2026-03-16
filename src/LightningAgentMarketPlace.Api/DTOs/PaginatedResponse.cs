namespace LightningAgentMarketPlace.Api.DTOs;

public class PaginatedResponse<T>
{
    public List<T> Items { get; set; } = new();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public bool HasMore => Page * PageSize < TotalCount;

    /// <summary>
    /// Cursor pointing to the last item in this page. Pass as the <c>cursor</c>
    /// query parameter to fetch the next page (keyset pagination).
    /// Null when there are no more results.
    /// </summary>
    public int? NextCursor { get; set; }
}
