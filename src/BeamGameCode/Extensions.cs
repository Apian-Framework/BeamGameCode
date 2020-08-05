using System;
using System.Collections.Generic;

namespace BeamGameCode
{
    public static class DictionaryExtentions
    {
        public static TValue GetValueOrDefault<TKey, TValue>
            (this IDictionary<TKey, TValue> dictionary, TKey key, TValue defaultValue)
        {
            TValue value;
            return dictionary.TryGetValue(key, out value) ? value : defaultValue;
        }
    }


}