using SmartX.Core.Models;

namespace SmartX.Core.Services;

public class TelemetryBatchProcessor
{
    private double[][] _rawBatches = Array.Empty<double[]>();

    public void LoadBatches(double[][] batches) => _rawBatches = batches ?? Array.Empty<double[]>();

    public int BatchCount => _rawBatches.Length;

    public List<SensorReading> Flatten(string deviceId, string unit)
    {
        var result = new List<SensorReading>();

        for (int batch = 0; batch < _rawBatches.Length; batch++)
        {
            var row = _rawBatches[batch];
            for (int slot = 0; slot < row.Length; slot++)
            {
                result.Add(new SensorReading($"{deviceId}#b{batch}s{slot}", row[slot], unit));
            }
        }

        return result;
    }

    public double[,] ToFixedGrid(int rows, int cols, double fillValue = double.NaN)
    {
        var grid = new double[rows, cols];

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                grid[r, c] = (r < _rawBatches.Length && c < _rawBatches[r].Length)
                    ? _rawBatches[r][c]
                    : fillValue;
            }
        }

        return grid;
    }
}
