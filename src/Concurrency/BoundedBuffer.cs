namespace C12ProjetoCiv.Concurrency;

/// <summary>
/// Buffer limitado clássico do problema produtor-consumidor:
/// um mutex protege a fila, um semáforo conta as vagas livres e outro conta os itens.
/// </summary>
public sealed class BoundedBuffer<T> : IDisposable
{
    private readonly Queue<T> _items;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _freeSlots;
    private readonly SemaphoreSlim _filledSlots;
    private long _producerBlocks;
    private volatile bool _closed;

    public BoundedBuffer(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        Capacity = capacity;
        _items = new Queue<T>(capacity);
        _freeSlots = new SemaphoreSlim(capacity);
        _filledSlots = new SemaphoreSlim(0);
    }

    public int Capacity { get; }
    public bool IsClosed => _closed;

    /// <summary>Quantas vezes um produtor encontrou o buffer cheio e precisou bloquear.</summary>
    public long ProducerBlocks => Interlocked.Read(ref _producerBlocks);

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _items.Count;
            }
        }
    }

    /// <summary>
    /// Produtor: espera uma vaga livre e insere o item. Retorna false se o buffer foi fechado.
    /// </summary>
    public bool TryAdd(T item, CancellationToken cancellationToken)
    {
        if (_closed)
        {
            return false;
        }

        if (!_freeSlots.Wait(0))
        {
            Interlocked.Increment(ref _producerBlocks);
            _freeSlots.Wait(cancellationToken);
        }

        if (_closed)
        {
            return false;
        }

        lock (_gate)
        {
            _items.Enqueue(item);
        }

        _filledSlots.Release();
        return true;
    }

    /// <summary>
    /// Consumidor: espera um item disponível. Retorna false se o buffer foi fechado e esvaziado.
    /// </summary>
    public bool TryTake(out T item, CancellationToken cancellationToken)
    {
        while (true)
        {
            _filledSlots.Wait(cancellationToken);

            lock (_gate)
            {
                if (_items.Count > 0)
                {
                    item = _items.Dequeue();
                    _freeSlots.Release();
                    return true;
                }
            }

            if (_closed)
            {
                item = default!;
                return false;
            }
        }
    }

    /// <summary>
    /// Fecha o buffer e acorda produtores e consumidores bloqueados para que nenhum fique preso.
    /// </summary>
    public void Close()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        _freeSlots.Release(Capacity + 64);
        _filledSlots.Release(Capacity + 64);
    }

    public void Dispose()
    {
        Close();
        _freeSlots.Dispose();
        _filledSlots.Dispose();
    }
}
