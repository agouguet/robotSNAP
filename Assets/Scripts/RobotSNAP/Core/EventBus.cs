using System;
using System.Collections.Generic;
using UnityEngine;

namespace RobotSNAP.Core
{
    /// <summary>
    /// Bus d'événements global. Les abonnements et publications sont sérialisés,
    /// mais les handlers restent exécutés sur le thread appelant.
    /// </summary>
    public class EventBus : IEventBus
    {
        private static EventBus _instance;
        private static readonly object _lock = new object();
        
        public static EventBus Instance
        {
            get
            {
                lock (_lock)
                {
                    return _instance ??= new EventBus();
                }
            }
        }
        
        private readonly Dictionary<Type, Delegate> _subscribers = new Dictionary<Type, Delegate>();
        private readonly object _subscribersLock = new object();
        
        private EventBus() { }
        
        public void Publish<T>(T evt)
        {
            Type type = typeof(T);
            Delegate handler;
            lock (_subscribersLock)
            {
                _subscribers.TryGetValue(type, out handler);
            }

            // Invoke outside the lock: handlers can safely subscribe/unsubscribe themselves.
            (handler as Action<T>)?.Invoke(evt);
        }
        
        public void Subscribe<T>(Action<T> handler)
        {
            if (handler == null) return;
            Type type = typeof(T);
            lock (_subscribersLock)
            {
                if (_subscribers.ContainsKey(type))
                {
                    _subscribers[type] = Delegate.Combine(_subscribers[type], handler);
                }
                else
                {
                    _subscribers[type] = handler;
                }
            }
        }
        
        public void Unsubscribe<T>(Action<T> handler)
        {
            if (handler == null) return;
            Type type = typeof(T);
            lock (_subscribersLock)
            {
                if (_subscribers.ContainsKey(type))
                {
                    _subscribers[type] = Delegate.Remove(_subscribers[type], handler);
                    if (_subscribers[type] == null)
                    {
                        _subscribers.Remove(type);
                    }
                }
            }
        }
        
        public void Clear()
        {
            lock (_subscribersLock)
            {
                _subscribers.Clear();
            }
        }
    }
}
