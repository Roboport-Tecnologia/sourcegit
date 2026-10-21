using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;

namespace Avalonia.Collections
{
    /// <summary>
    /// Avalonia's list: an <see cref="ObservableCollection{T}"/> with range
    /// operations. As Avalonia's, a range raises one change for the range,
    /// not one per item, so a view updates its rows once.
    /// </summary>
    public class AvaloniaList<T> : ObservableCollection<T>
    {
        public AvaloniaList() { }

        public AvaloniaList(IEnumerable<T> items) : base(items) { }

        public AvaloniaList(params T[] items) : base(items) { }

        public void AddRange(IEnumerable<T> items) => InsertRange(Count, items);

        public void InsertRange(int index, IEnumerable<T> items)
        {
            CheckReentrancy();
            var list = items.ToList();
            if (list.Count == 0)
                return;

            for (var i = 0; i < list.Count; i++)
                Items.Insert(index + i, list[i]);
            RaiseReset(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, list, index));
        }

        public void RemoveRange(int index, int count)
        {
            CheckReentrancy();
            if (count == 0)
                return;

            var removed = new List<T>(count);
            for (var i = 0; i < count; i++)
            {
                removed.Add(Items[index]);
                Items.RemoveAt(index);
            }
            RaiseReset(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, removed, index));
        }

        /// <summary>Removes each of <paramref name="items"/>, as Avalonia's.</summary>
        public void RemoveAll(IEnumerable<T> items)
        {
            foreach (var item in items.ToList())
                Remove(item);
        }

        private void RaiseReset(NotifyCollectionChangedEventArgs change)
        {
            OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(Count)));
            OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(change);
        }
    }
}
