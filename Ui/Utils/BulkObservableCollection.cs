using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace _1RM.Utils
{
    /// <summary>
    /// An <see cref="ObservableCollection{T}"/> whose entire contents can be swapped out under a single
    /// change notification.
    ///
    /// Refilling a list one item at a time is quadratic once a sorted, grouped <c>ListCollectionView</c> is
    /// attached to it: every Add makes the view search for an insertion point and shift its internal array.
    /// <c>DeferRefresh</c> is not the way out either - the view forbids touching its source while a refresh is
    /// deferred, and an ObservableCollection raises its notifications synchronously, so the very first Add
    /// throws "cannot change or check the contents of the CollectionView while Refresh is deferred". A single
    /// Reset sidesteps both: the view is told the list is unrecognisable and rebuilds itself exactly once.
    /// </summary>
    public class BulkObservableCollection<T> : ObservableCollection<T>
    {
        public void ReplaceAll(IEnumerable<T> items)
        {
            CheckReentrancy();
            Items.Clear();
            foreach (var item in items)
                Items.Add(item);

            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
            OnPropertyChanged(new PropertyChangedEventArgs(INDEXER_NAME));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }

        /// <summary>The name WPF listens for to know an indexed value changed; not a real property name.</summary>
        private const string INDEXER_NAME = "Item[]";
    }
}
