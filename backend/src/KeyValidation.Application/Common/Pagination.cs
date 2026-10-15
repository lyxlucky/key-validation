namespace KeyValidation.Application;
public record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);
public record PageQuery(int Page = 1, int PageSize = 20);
