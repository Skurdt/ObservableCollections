﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

namespace ObservableCollections.Internal
{
    internal class SynchronizedViewList<T, TView> : ISynchronizedViewList<TView>
    {
        readonly ISynchronizedView<T, TView> parent;
        protected readonly List<TView> listView;
        protected readonly object gate = new object();

        public SynchronizedViewList(ISynchronizedView<T, TView> parent)
        {
            this.parent = parent;
            lock (parent.SyncRoot)
            {
                this.listView = parent.ToList();
                parent.ViewChanged += Parent_ViewChanged;
            }
        }

        private void Parent_ViewChanged(SynchronizedViewChangedEventArgs<T, TView> e)
        {
            lock (gate)
            {
                switch (e.Action)
                {
                    case NotifyViewChangedAction.Add: // Add or Insert
                        if (e.NewViewIndex == -1)
                        {
                            listView.Add(e.NewView);
                        }
                        else
                        {
                            listView.Insert(e.NewViewIndex, e.NewView);
                        }
                        break;
                    case NotifyViewChangedAction.Remove: // Remove
                        if (e.OldViewIndex == -1) // can't gurantee correct remove if index is not provided
                        {
                            listView.Remove(e.OldView);
                        }
                        else
                        {
                            listView.RemoveAt(e.OldViewIndex);
                        }
                        break;
                    case NotifyViewChangedAction.Replace: // Indexer
                        if (e.NewViewIndex == -1)
                        {
                            var index = listView.IndexOf(e.OldView);
                            listView[index] = e.NewView;
                        }
                        else
                        {
                            listView[e.NewViewIndex] = e.NewView;
                        }

                        break;
                    case NotifyViewChangedAction.Move: //Remove and Insert
                        if (e.NewViewIndex == -1)
                        {
                            // do nothing
                        }
                        else
                        {
                            listView.RemoveAt(e.OldViewIndex);
                            listView.Insert(e.NewViewIndex, e.NewView);
                        }
                        break;
                    case NotifyViewChangedAction.Reset: // Clear
                        listView.Clear();
                        break;
                    case NotifyViewChangedAction.FilterReset:
                        listView.Clear();
                        foreach (var item in parent)
                        {
                            listView.Add(item);
                        }
                        break;
                    default:
                        break;
                }

                OnCollectionChanged(e);
            }
        }

        protected virtual void OnCollectionChanged(in SynchronizedViewChangedEventArgs<T, TView> args)
        {
        }

        public TView this[int index]
        {
            get
            {
                lock (gate)
                {
                    return listView[index];
                }
            }
        }

        public int Count
        {
            get
            {
                lock (gate)
                {
                    return listView.Count;
                }
            }
        }

        public IEnumerator<TView> GetEnumerator()
        {
            lock (gate)
            {
                foreach (var item in listView)
                {
                    yield return item;
                }
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return listView.GetEnumerator();
        }

        public void Dispose()
        {
            parent.ViewChanged -= Parent_ViewChanged;
        }
    }

    internal class NotifyCollectionChangedSynchronizedView<T, TView> :
        SynchronizedViewList<T, TView>,
        INotifyCollectionChangedSynchronizedView<TView>,
        IList<TView>, IList
    {
        static readonly PropertyChangedEventArgs CountPropertyChangedEventArgs = new("Count");
        static readonly Action<NotifyCollectionChangedEventArgs> raiseChangedEventInvoke = RaiseChangedEvent;

        readonly ICollectionEventDispatcher eventDispatcher;

        public event NotifyCollectionChangedEventHandler? CollectionChanged;
        public event PropertyChangedEventHandler? PropertyChanged;

        public NotifyCollectionChangedSynchronizedView(ISynchronizedView<T, TView> parent, ICollectionEventDispatcher? eventDispatcher)
            : base(parent)
        {
            this.eventDispatcher = eventDispatcher ?? InlineCollectionEventDispatcher.Instance;
        }

        protected override void OnCollectionChanged(in SynchronizedViewChangedEventArgs<T, TView> args)
        {
            if (CollectionChanged == null && PropertyChanged == null) return;

            switch (args.Action)
            {
                case NotifyViewChangedAction.Add:
                    eventDispatcher.Post(new CollectionEventDispatcherEventArgs(NotifyCollectionChangedAction.Add, args.NewView, args.NewViewIndex)
                    {
                        Collection = this,
                        Invoker = raiseChangedEventInvoke,
                        IsInvokeCollectionChanged = true,
                        IsInvokePropertyChanged = true
                    });
                    break;
                case NotifyViewChangedAction.Remove:
                    eventDispatcher.Post(new CollectionEventDispatcherEventArgs(NotifyCollectionChangedAction.Remove, args.OldView, args.OldViewIndex)
                    {
                        Collection = this,
                        Invoker = raiseChangedEventInvoke,
                        IsInvokeCollectionChanged = true,
                        IsInvokePropertyChanged = true
                    });
                    break;
                case NotifyViewChangedAction.Reset:
                    eventDispatcher.Post(new CollectionEventDispatcherEventArgs(NotifyCollectionChangedAction.Reset)
                    {
                        Collection = this,
                        Invoker = raiseChangedEventInvoke,
                        IsInvokeCollectionChanged = true,
                        IsInvokePropertyChanged = true
                    });
                    break;
                case NotifyViewChangedAction.Replace:
                    eventDispatcher.Post(new CollectionEventDispatcherEventArgs(NotifyCollectionChangedAction.Replace, args.NewView, args.OldView, args.NewViewIndex)
                    {
                        Collection = this,
                        Invoker = raiseChangedEventInvoke,
                        IsInvokeCollectionChanged = true,
                        IsInvokePropertyChanged = false
                    });
                    break;
                case NotifyViewChangedAction.Move:
                    eventDispatcher.Post(new CollectionEventDispatcherEventArgs(NotifyCollectionChangedAction.Move, args.NewView, args.NewViewIndex, args.OldViewIndex)
                    {
                        Collection = this,
                        Invoker = raiseChangedEventInvoke,
                        IsInvokeCollectionChanged = true,
                        IsInvokePropertyChanged = false
                    });
                    break;
            }
        }

        static void RaiseChangedEvent(NotifyCollectionChangedEventArgs e)
        {
            var e2 = (CollectionEventDispatcherEventArgs)e;
            var self = (NotifyCollectionChangedSynchronizedView<T, TView>)e2.Collection;

            if (e2.IsInvokeCollectionChanged)
            {
                self.CollectionChanged?.Invoke(self, e);
            }
            if (e2.IsInvokePropertyChanged)
            {
                self.PropertyChanged?.Invoke(self, CountPropertyChangedEventArgs);
            }
        }

        // IList<T>, IList implementation

        TView IList<TView>.this[int index]
        {
            get => ((IReadOnlyList<TView>)this)[index];
            set => throw new NotSupportedException();
        }

        object? IList.this[int index]
        {
            get
            {
                return this[index];
            }
            set => throw new NotSupportedException();
        }

        static bool IsCompatibleObject(object? value)
        {
            return (value is T) || (value == null && default(T) == null);
        }

        public bool IsReadOnly => true;

        public bool IsFixedSize => false;

        public bool IsSynchronized => true;

        public object SyncRoot => gate;

        public void Add(TView item)
        {
            throw new NotSupportedException();
        }

        public int Add(object? value)
        {
            throw new NotImplementedException();
        }

        public void Clear()
        {
            throw new NotSupportedException();
        }

        public bool Contains(TView item)
        {
            lock (gate)
            {
                foreach (var listItem in listView)
                {
                    if (EqualityComparer<TView>.Default.Equals(listItem, item))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        public bool Contains(object? value)
        {
            if (IsCompatibleObject(value))
            {
                return Contains((TView)value!);
            }
            return false;
        }

        public void CopyTo(TView[] array, int arrayIndex)
        {
            throw new NotSupportedException();
        }

        public void CopyTo(Array array, int index)
        {
            throw new NotImplementedException();
        }

        public int IndexOf(TView item)
        {
            lock (gate)
            {
                var index = 0;
                foreach (var listItem in listView)
                {
                    if (EqualityComparer<TView>.Default.Equals(listItem, item))
                    {
                        return index;
                    }
                    index++;
                }
            }
            return -1;
        }

        public int IndexOf(object? item)
        {
            if (IsCompatibleObject(item))
            {
                return IndexOf((TView)item!);
            }
            return -1;
        }

        public void Insert(int index, TView item)
        {
            throw new NotSupportedException();
        }

        public void Insert(int index, object? value)
        {
            throw new NotImplementedException();
        }

        public bool Remove(TView item)
        {
            throw new NotSupportedException();
        }

        public void Remove(object? value)
        {
            throw new NotImplementedException();
        }

        public void RemoveAt(int index)
        {
            throw new NotSupportedException();
        }
    }
}