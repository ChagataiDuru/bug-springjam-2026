using System;
using UnityEngine;

namespace OrangeWolf.Generic
{
    [Serializable]
    public struct Pair<T, T1>
    {
        [SerializeField] public T Key;
        [SerializeField] public T1 Value;
        public T1 GetValue(T key)
        {
            return Value;
        }
    }
}