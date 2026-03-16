using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MLE_Infobot;

internal static class Extensions
{
    public static IEnumerable<T> Shuffle<T>(this IEnumerable<T> source, Random? rnd = null)
    {
        rnd ??= new();
        return source.OrderBy(item => rnd.Next());
    }

    public static T Pop<T>(this List<T> source, int pos = 0)
    {
        if (pos > source.Count - 1)
        {
            throw new ArgumentOutOfRangeException(paramName: nameof(pos));
        }
        T result = source[pos];
        source.RemoveAt(pos);
        return result;
    }

    public static T Pop<T>(this List<T> source, T element)
    {
        if (!source.Contains(element))
        {
            throw new ArgumentOutOfRangeException(paramName: nameof(element));
        }
        T result = element;
        source.Remove(element);
        return result;
    }

    public static List<(T, int)> WithIndex<T>(this List<T> list)
        => [.. list.Select((value, index) => (value, index))];
}
