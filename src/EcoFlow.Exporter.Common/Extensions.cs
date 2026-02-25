namespace EcoFlow.Exporter.Common;

public static class Extensions
{
    public static IEnumerable<int> IndexesOf(this string text, char symbol) => Enumerable.Range(0, text.Length).Where(index => text[index] == symbol);

    public static IEnumerable<T> WhereNotNull<T>(this IEnumerable<T?> items) where T : class => items.Where(item => item is not null).Cast<T>();

    public static IAsyncEnumerable<T> WhereNotNull<T>(this IAsyncEnumerable<T?> items) where T : class => items.Where(item => item is not null).Cast<T>();
}
