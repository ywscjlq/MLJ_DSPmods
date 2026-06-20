
using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

[CompilerGenerated]
internal sealed class _003C_003Ez__ReadOnlyArray<T> : IReadOnlyList<T>, ICollection<T>, IEnumerable<T>, IEnumerable, IReadOnlyCollection<T>, IList<T>, ICollection, IList
{
    private readonly T[] _items;
    public _003C_003Ez__ReadOnlyArray(T[] items) { _items = items; }
    public T this[int index] { get => _items[index]; set => throw new NotSupportedException(); }
    public int Count => _items.Length;
    public bool IsReadOnly => true;
    public bool IsFixedSize => true;
    public bool IsSynchronized => false;
    public object SyncRoot => this;
    object IList.this[int index] { get => _items[index]; set => throw new NotSupportedException(); }
    public IEnumerator<T> GetEnumerator() { foreach (var x in _items) yield return x; }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    public bool Contains(T item) => Array.IndexOf(_items, item) >= 0;
    public void CopyTo(T[] array, int arrayIndex) => Array.Copy(_items, 0, array, arrayIndex, _items.Length);
    public int IndexOf(T item) => Array.IndexOf(_items, item);
    public void Add(T item) => throw new NotSupportedException();
    public void Clear() => throw new NotSupportedException();
    public bool Remove(T item) => throw new NotSupportedException();
    public void Insert(int index, T item) => throw new NotSupportedException();
    public void RemoveAt(int index) => throw new NotSupportedException();
    int IList.Add(object value) => throw new NotSupportedException();
    bool IList.Contains(object value) => value is T t && Contains(t);
    int IList.IndexOf(object value) => value is T t ? IndexOf(t) : -1;
    void IList.Insert(int index, object value) => throw new NotSupportedException();
    void IList.Remove(object value) => throw new NotSupportedException();
    void ICollection.CopyTo(Array array, int index) => Array.Copy(_items, 0, array, index, _items.Length);
}

[CompilerGenerated]
internal sealed class _003C_003Ez__ReadOnlyList<T> : IReadOnlyList<T>, IList<T>, ICollection<T>, IEnumerable<T>, IEnumerable, IReadOnlyCollection<T>, IList, ICollection
{
    private readonly List<T> _items;
    public _003C_003Ez__ReadOnlyList(List<T> items) { _items = items; }
    public T this[int index] { get => _items[index]; set => throw new NotSupportedException(); }
    public int Count => _items.Count;
    public bool IsReadOnly => true;
    public bool IsFixedSize => true;
    public bool IsSynchronized => false;
    public object SyncRoot => this;
    object IList.this[int index] { get => _items[index]; set => throw new NotSupportedException(); }
    public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    public bool Contains(T item) => _items.Contains(item);
    public void CopyTo(T[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);
    public int IndexOf(T item) => _items.IndexOf(item);
    public void Add(T item) => throw new NotSupportedException();
    public void Clear() => throw new NotSupportedException();
    public bool Remove(T item) => throw new NotSupportedException();
    public void Insert(int index, T item) => throw new NotSupportedException();
    public void RemoveAt(int index) => throw new NotSupportedException();
    int IList.Add(object value) => throw new NotSupportedException();
    bool IList.Contains(object value) => value is T t && Contains(t);
    int IList.IndexOf(object value) => value is T t ? IndexOf(t) : -1;
    void IList.Insert(int index, object value) => throw new NotSupportedException();
    void IList.Remove(object value) => throw new NotSupportedException();
    void ICollection.CopyTo(Array array, int index) => _items.CopyTo((T[])array, index);
}

[CompilerGenerated]
internal sealed class _003C_003Ez__ReadOnlySingleElementList<T> : IReadOnlyList<T>, IList<T>, ICollection<T>, IEnumerable<T>, IEnumerable, IReadOnlyCollection<T>, IList, ICollection
{
    private readonly T _item;
    public _003C_003Ez__ReadOnlySingleElementList(T item) { _item = item; }
    public T this[int index] { get { if (index != 0) throw new IndexOutOfRangeException(); return _item; } set => throw new NotSupportedException(); }
    public int Count => 1;
    public bool IsReadOnly => true;
    public bool IsFixedSize => true;
    public bool IsSynchronized => false;
    public object SyncRoot => this;
    object IList.this[int index] { get { if (index != 0) throw new IndexOutOfRangeException(); return _item; } set => throw new NotSupportedException(); }
    public IEnumerator<T> GetEnumerator() { yield return _item; }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    public bool Contains(T item) => EqualityComparer<T>.Default.Equals(_item, item);
    public void CopyTo(T[] array, int arrayIndex) { array[arrayIndex] = _item; }
    public int IndexOf(T item) => EqualityComparer<T>.Default.Equals(_item, item) ? 0 : -1;
    public void Add(T item) => throw new NotSupportedException();
    public void Clear() => throw new NotSupportedException();
    public bool Remove(T item) => throw new NotSupportedException();
    public void Insert(int index, T item) => throw new NotSupportedException();
    public void RemoveAt(int index) => throw new NotSupportedException();
    int IList.Add(object value) => throw new NotSupportedException();
    bool IList.Contains(object value) => value is T t && Contains(t);
    int IList.IndexOf(object value) => value is T t ? IndexOf(t) : -1;
    void IList.Insert(int index, object value) => throw new NotSupportedException();
    void IList.Remove(object value) => throw new NotSupportedException();
    void ICollection.CopyTo(Array array, int index) => array.SetValue(_item, index);
}
