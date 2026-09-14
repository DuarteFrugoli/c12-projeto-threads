namespace C12ProjetoCiv.Metrics;

public enum TimelineSegmentKind : byte
{
    CivilizationA = 0,
    CivilizationB = 1,
    CivilizationC = 2,
    CivilizationD = 3,
    WaitingForWorkers,
    Rendering,
    Arbitration,
}

public readonly record struct TimelineSegment(
    long StartTimestamp,
    long EndTimestamp,
    TimelineSegmentKind Kind);

/// <summary>
/// Histórico circular dos intervalos em que uma thread esteve ocupada. Cada thread grava
/// na sua própria linha do tempo e a thread principal lê para desenhar o gráfico de Gantt.
/// O lock é necessário porque o árbitro grava enquanto a thread principal desenha.
/// </summary>
public sealed class ThreadTimeline
{
    private readonly object _gate = new();
    private readonly TimelineSegment[] _segments;
    private int _nextIndex;
    private int _count;

    public ThreadTimeline(string label, int capacity = 8_192)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        Label = label;
        _segments = new TimelineSegment[capacity];
    }

    public string Label { get; }

    public static TimelineSegmentKind KindForCivilization(int civilizationId)
    {
        return (TimelineSegmentKind)(civilizationId % 4);
    }

    public void Record(long startTimestamp, long endTimestamp, TimelineSegmentKind kind)
    {
        lock (_gate)
        {
            _segments[_nextIndex] = new TimelineSegment(startTimestamp, endTimestamp, kind);
            _nextIndex = (_nextIndex + 1) % _segments.Length;
            _count = Math.Min(_count + 1, _segments.Length);
        }
    }

    public long LatestTimestamp
    {
        get
        {
            lock (_gate)
            {
                if (_count == 0)
                {
                    return 0;
                }

                int lastIndex = (_nextIndex - 1 + _segments.Length) % _segments.Length;
                return _segments[lastIndex].EndTimestamp;
            }
        }
    }

    /// <summary>
    /// Copia para <paramref name="destination"/> os segmentos que tocam o intervalo informado.
    /// </summary>
    public void CopySegments(long fromTimestamp, long toTimestamp, List<TimelineSegment> destination)
    {
        destination.Clear();

        lock (_gate)
        {
            int oldestIndex = (_nextIndex - _count + _segments.Length) % _segments.Length;

            for (int offset = 0; offset < _count; offset++)
            {
                TimelineSegment segment = _segments[(oldestIndex + offset) % _segments.Length];

                if (segment.EndTimestamp >= fromTimestamp && segment.StartTimestamp <= toTimestamp)
                {
                    destination.Add(segment);
                }
            }
        }
    }

    /// <summary>
    /// Fração do intervalo em que a thread esteve ocupada. Considera apenas o trecho coberto
    /// pelo histórico, para não subestimar quando o buffer circular já descartou amostras.
    /// </summary>
    public double BusyFraction(long fromTimestamp, long toTimestamp)
    {
        lock (_gate)
        {
            if (_count == 0 || toTimestamp <= fromTimestamp)
            {
                return 0;
            }

            int oldestIndex = (_nextIndex - _count + _segments.Length) % _segments.Length;
            long coveredFrom = Math.Max(fromTimestamp, _segments[oldestIndex].StartTimestamp);
            long busyTicks = 0;

            for (int offset = 0; offset < _count; offset++)
            {
                TimelineSegment segment = _segments[(oldestIndex + offset) % _segments.Length];

                if (segment.Kind == TimelineSegmentKind.WaitingForWorkers)
                {
                    continue;
                }

                long start = Math.Max(segment.StartTimestamp, coveredFrom);
                long end = Math.Min(segment.EndTimestamp, toTimestamp);

                if (end > start)
                {
                    busyTicks += end - start;
                }
            }

            long coveredTicks = toTimestamp - coveredFrom;
            return coveredTicks <= 0 ? 0 : Math.Clamp((double)busyTicks / coveredTicks, 0, 1);
        }
    }
}
