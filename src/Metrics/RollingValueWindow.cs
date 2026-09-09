namespace C12ProjetoCiv.Metrics;

internal sealed class RollingValueWindow
{
    private readonly double[] _values;
    private int _nextIndex;
    private double _sum;

    public RollingValueWindow(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _values = new double[capacity];
    }

    public int Count { get; private set; }
    public double Average => Count == 0 ? 0 : _sum / Count;

    public void Add(double value)
    {
        if (Count == _values.Length)
        {
            _sum -= _values[_nextIndex];
        }
        else
        {
            Count++;
        }

        _values[_nextIndex] = value;
        _sum += value;
        _nextIndex = (_nextIndex + 1) % _values.Length;
    }

    public double CalculatePercentile(double percentile)
    {
        if (Count == 0)
        {
            return 0;
        }

        double[] sorted = new double[Count];
        Array.Copy(_values, sorted, Count);
        Array.Sort(sorted);
        int index = (int)Math.Ceiling(percentile * Count) - 1;
        return sorted[Math.Clamp(index, 0, Count - 1)];
    }

    public void Clear()
    {
        Array.Clear(_values);
        Count = 0;
        _nextIndex = 0;
        _sum = 0;
    }
}
