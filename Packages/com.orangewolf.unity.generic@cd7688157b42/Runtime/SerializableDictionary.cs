using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace OrangeWolf.Generic
{
    [Serializable]
    public class SerializableDictionary<TKey, TValue> : ISerializationCallbackReceiver, IEnumerable<KeyValuePair<TKey,TValue>>
    {
        [field: SerializeField, HideInInspector] public TKey[] Keys;
        [field: SerializeField, HideInInspector] public TValue[] Values;
        private Dictionary<TKey, TValue> _dictionary = new();
        
        public TValue this[TKey key]
        {
            get => _dictionary[key];
            private set => _dictionary[key] = value;
        }
        
        public void Add(TKey key, TValue value)
        {
            _dictionary.Add(key, value);
        }
        
        public bool ContainsKey(TKey key)
        {
            return _dictionary.ContainsKey(key);
        }
        
        public bool Remove(TKey key)
        {
            return _dictionary.Remove(key);
        }
        
        public void Clear()
        {
            _dictionary.Clear();
        }
        
        public void OnBeforeSerialize()
        {
            Keys = new TKey[_dictionary.Count];
            Values = new TValue[_dictionary.Count];
            // For each key/value pair in the dictionary, add the key to the keys list and the value to the values list
            var i = 0;
            foreach (var kvp in _dictionary)
            {
                Keys[i] = kvp.Key;
                Values[i] =kvp.Value;
                i++;
            }
        }

        public void OnAfterDeserialize()
        {
            _dictionary.Clear();
            // Loop through the list of keys and values and add each key/value pair to the dictionary
            for (int i = 0; i != Math.Min(Keys.Length, Values.Length); i++)
                _dictionary.Add(Keys[i], Values[i]);
        }

        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
        {
            return _dictionary.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}