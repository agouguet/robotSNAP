using System;
using System.Collections.Generic;
using UnityEngine;

namespace RobotSNAP.Core
{
    /// <summary>
    /// Bus d'événements global - Singleton thread-safe
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
        
        private EventBus() { }
        
        public void Publish<T>(T evt)
        {
            Type type = typeof(T);
            if (_subscribers.TryGetValue(type, out Delegate handler))
            {
                (handler as Action<T>)?.Invoke(evt);
            }
        }
        
        public void Subscribe<T>(Action<T> handler)
        {
            Type type = typeof(T);
            if (_subscribers.ContainsKey(type))
            {
                _subscribers[type] = Delegate.Combine(_subscribers[type], handler);
            }
            else
            {
                _subscribers[type] = handler;
            }
        }
        
        public void Unsubscribe<T>(Action<T> handler)
        {
            Type type = typeof(T);
            if (_subscribers.ContainsKey(type))
            {
                _subscribers[type] = Delegate.Remove(_subscribers[type], handler);
                if (_subscribers[type] == null)
                {
                    _subscribers.Remove(type);
                }
            }
        }
        
        public void Clear()
        {
            _subscribers.Clear();
        }
    }
}