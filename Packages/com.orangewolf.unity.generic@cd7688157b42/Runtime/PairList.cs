using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace OrangeWolf.Generic
{
    [Serializable]
    public class PairList<TKey, TValue> : ISerializationCallbackReceiver, IEnumerable<KeyValuePair<TKey,TValue>>
    {
        [field: SerializeField] public TKey[] Keys;
        [field: SerializeField] public TValue[] Values;
        private List<KeyValuePair<TKey,TValue>> _list = new();
        
        public void Add(TKey key, TValue value)
        {
            var pair = new KeyValuePair<TKey, TValue>(key, value);
            _list.Add(pair);
        }
        
        public void AddRange(PairList<TKey, TValue> pairs)
        {
            foreach (var pair in pairs)
            {
                _list.Add(pair);
            }
        }
        
        public bool ContainsPair(KeyValuePair<TKey, TValue> pair)
        {
            return _list.Contains(pair);
        }
        
        public bool Remove(KeyValuePair<TKey, TValue> pair)
        {
            return _list.Remove(pair);
        }
        
        public void Clear()
        {
            _list.Clear();
        }
        
        public void OnBeforeSerialize()
        {
            Keys = new TKey[_list.Count];
            Values = new TValue[_list.Count];
            // For each key/value pair in the dictionary, add the key to the keys list and the value to the values list
            var i = 0;
            foreach (var kvp in _list)
            {
                Keys[i] = kvp.Key;
                Values[i] =kvp.Value;
                i++;
            }
        }

        public void OnAfterDeserialize()
        {
            _list.Clear();
            
            if (Keys == null || Values == null)
                return;
            
            if (Keys.Length != Values.Length)
            {
                Debug.LogError("Keys and Values arrays are not the same length or are null.");
                return;
            }
            // Loop through the list of keys and values and add each key/value pair to the dictionary
            for (int i = 0; i != Math.Min(Keys.Length, Values.Length); i++)
                _list.Add(new KeyValuePair<TKey, TValue>(Keys[i], Values[i]));
        }

        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
        {
            return _list.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
