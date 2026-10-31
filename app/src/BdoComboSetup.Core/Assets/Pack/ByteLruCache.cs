using System.Diagnostics.CodeAnalysis;

namespace BdoComboSetup.Core.Assets.Pack;

/// <summary>
/// A bounded, thread-safe least-recently-used cache of byte arrays, bounded by the total number of bytes it holds.
/// An array larger than the whole budget is not kept.
/// </summary>
public sealed class ByteLruCache
{
    private readonly object _gate = new();
    private readonly LinkedList<CacheItem> _order = new();
    private readonly Dictionary<string, LinkedListNode<CacheItem>> _nodes = new(StringComparer.Ordinal);
    private readonly long _budgetBytes;
    private long _bytes;

    public ByteLruCache(long budgetBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(budgetBytes);
        _budgetBytes = budgetBytes;
    }

    public long BudgetBytes => _budgetBytes;

    public long Bytes
    {
        get
        {
            lock (_gate)
            {
                return _bytes;
            }
        }
    }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _nodes.Count;
            }
        }
    }

    /// <summary>Returns the value and marks it as the most recently used.</summary>
    public bool TryGet(string key, [NotNullWhen(true)] out byte[]? value)
    {
        lock (_gate)
        {
            if (_nodes.TryGetValue(key, out var node))
            {
                _order.Remove(node);
                _order.AddFirst(node);
                value = node.Value.Bytes;
                return true;
            }
        }

        value = null;
        return false;
    }

    /// <summary>Stores the value (replacing an earlier one with the same key) and evicts the least recently used items to fit the budget.</summary>
    public void Add(string key, byte[] value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length > _budgetBytes)
        {
            return;
        }

        lock (_gate)
        {
            if (_nodes.TryGetValue(key, out var existing))
            {
                _bytes -= existing.Value.Bytes.Length;
                _order.Remove(existing);
                _nodes.Remove(key);
            }

            _nodes[key] = _order.AddFirst(new CacheItem(key, value));
            _bytes += value.Length;

            while (_bytes > _budgetBytes && _order.Last is { } oldest)
            {
                _order.RemoveLast();
                _nodes.Remove(oldest.Value.Key);
                _bytes -= oldest.Value.Bytes.Length;
            }
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _order.Clear();
            _nodes.Clear();
            _bytes = 0;
        }
    }

    private sealed record CacheItem(string Key, byte[] Bytes);
}
