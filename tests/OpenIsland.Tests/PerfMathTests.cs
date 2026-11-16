using OpenIsland;
using Xunit;

namespace OpenIsland.Tests;

/// <summary>Сжатие загрузки ядер до ограниченного числа столбцов в карточке мониторинга.</summary>
public class PerfMathTests
{
    [Fact]
    public void AggregateCores_KeepsValuesWhenWithinLimit()
    {
        double[] cores = [1, 2, 3, 4];

        IReadOnlyList<double> result = PerfMath.AggregateCores(cores, 16);

        Assert.Equal(cores, result);
    }

    [Fact]
    public void AggregateCores_AveragesNeighboursIntoBuckets()
    {
        double[] cores = [0, 100, 50, 50];

        IReadOnlyList<double> result = PerfMath.AggregateCores(cores, 2);

        Assert.Equal(new[] { 50.0, 50.0 }, result);
    }

    [Fact]
    public void AggregateCores_ProducesExactlyTheRequestedCount()
    {
        double[] cores = Enumerable.Range(0, 32).Select(index => (double)index).ToArray();

        IReadOnlyList<double> result = PerfMath.AggregateCores(cores, 16);

        Assert.Equal(16, result.Count);
        Assert.Equal(0.5, result[0]);   // среднее 0 и 1
        Assert.Equal(30.5, result[15]); // среднее 30 и 31
    }

    [Fact]
    public void AggregateCores_EmptyOrInvalidLimitGivesEmpty()
    {
        Assert.Empty(PerfMath.AggregateCores([], 16));
        Assert.Empty(PerfMath.AggregateCores([10, 20], 0));
        Assert.Empty(PerfMath.AggregateCores([10, 20], -1));
    }
}
