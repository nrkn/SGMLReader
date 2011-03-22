using System;
using System.Diagnostics.CodeAnalysis;

namespace SgmlReaderDll.Reader {
  /// <summary>
  /// This stack maintains a high water mark for allocated objects so the client
  /// can reuse the objects in the stack to reduce memory allocations, this is
  /// used to maintain current state of the parser for element stack, and attributes
  /// in each element.
  /// </summary>
  internal class HwStack
  {
    private object[] _items;
    private int _count;
    private readonly int _growth;

    /// <summary>
    /// Initialises a new instance of the HWStack class.
    /// </summary>
    /// <param name="growth">The amount to grow the stack space by, if more space is needed on the stack.</param>
    public HwStack(int growth)
    {
      _growth = growth;
    }

    /// <summary>
    /// The number of items currently in the stack.
    /// </summary>
    public int Count
    {
      get
      {
        return _count;
      }
      set
      {
        _count = value;
      }
    }

    /// <summary>
    /// The size (capacity) of the stack.
    /// </summary>
    [SuppressMessage("Microsoft.Performance", "CA1811", Justification = "Kept for potential future usage.")]
    public int Size { get; private set; }

    /// <summary>
    /// Returns the item at the requested index or null if index is out of bounds
    /// </summary>
    /// <param name="i">The index of the item to retrieve.</param>
    /// <returns>The item at the requested index or null if index is out of bounds.</returns>
    public object this[int i]
    {
      get
      {
        return (i >= 0 && i < Size) ? _items[i] : null;
      }
      set
      {
        _items[i] = value;
      }
    }

    /// <summary>
    /// Removes and returns the item at the top of the stack
    /// </summary>
    /// <returns>The item at the top of the stack.</returns>
    public object Pop()
    {
      _count--;
      return _count > 0 ? _items[_count - 1] : null;
    }

    /// <summary>
    /// Pushes a new slot at the top of the stack.
    /// </summary>
    /// <returns>The object at the top of the stack.</returns>
    /// <remarks>
    /// This method tries to reuse a slot, if it returns null then
    /// the user has to call the other Push method.
    /// </remarks>
    public object Push()
    {
      if (_count == Size)
      {
        var newsize = Size + _growth;
        var newarray = new object[newsize];
        if (_items != null)
          Array.Copy(_items, newarray, Size);

        Size = newsize;
        _items = newarray;
      }
      return _items[_count++];
    }

    /// <summary>
    /// Remove a specific item from the stack.
    /// </summary>
    /// <param name="i">The index of the item to remove.</param>
    [SuppressMessage("Microsoft.Performance", "CA1811", Justification = "Kept for potential future usage.")]
    public void RemoveAt(int i)
    {
      _items[i] = null;
      Array.Copy(_items, i + 1, _items, i, _count - i - 1);
      _count--;
    }
  }
}