namespace OpenIsland;

/// <summary>
/// Чистые вычисления для карточки мониторинга: сжатие загрузки ядер до
/// ограниченного числа столбцов. Без UI — покрывается тестами.
/// </summary>
static class PerfMath
{
    /// <summary>
    /// Сжимает список загрузок ядер до не более чем <paramref name="maxBars"/>
    /// значений, усредняя соседние ядра. Если ядер меньше лимита, список не меняется.
    /// </summary>
    public static IReadOnlyList<double> AggregateCores(IReadOnlyList<double> cores, int maxBars)
    {
        if (cores.Count == 0 || maxBars < 1) return [];

        if (cores.Count <= maxBars)
        {
            double[] copy = new double[cores.Count];
            for (int i = 0; i < cores.Count; i++) copy[i] = cores[i];
            return copy;
        }

        double[] sums = new double[maxBars];
        int[] counts = new int[maxBars];
        for (int i = 0; i < cores.Count; i++)
        {
            int bucket = (int)((long)i * maxBars / cores.Count);
            sums[bucket] += cores[i];
            counts[bucket]++;
        }

        double[] result = new double[maxBars];
        for (int b = 0; b < maxBars; b++)
            result[b] = counts[b] == 0 ? 0 : sums[b] / counts[b];
        return result;
    }
}
