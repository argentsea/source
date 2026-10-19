using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace ArgentSea
{
    public class ShardsValues : ICollection, IEnumerable<ShardParameterValue>
    {
        private readonly object syncRoot = new object();

        public ShardsValues()
        {
            this.Shards = new Dictionary<short, IDictionary<string, object>>();
        }
        public ShardsValues(Dictionary<short, IDictionary<string, object>> shards)
        {
            ArgumentNullException.ThrowIfNull(shards);
            this.Shards = shards;
        }
        public ShardsValues(IList<short> list)
        {
            ArgumentNullException.ThrowIfNull(list);
            this.Shards = new Dictionary<short, IDictionary<string, object>>();
            foreach (var shd in list)
            {
                this.Add(shd);
            }
        }

        public ShardsValues Add(short shardId)
        {
            if (!Shards.ContainsKey(shardId))
            {
                Shards.Add(shardId, new Dictionary<string, object>());
            }
            return this;
        }
        public ShardsValues Add(short shardId, string parameterName, object parameterValue)
        {
            if (Shards.TryGetValue(shardId, out var prms))
            {
                if (prms is null)
                {
                    Shards[shardId] = new Dictionary<string, object>() { { parameterName, parameterValue } };
                }
                else
                {
                    prms.Add(parameterName, parameterValue);
                }
            }
            else
            {
                Shards.Add(shardId, new Dictionary<string, object>() { { parameterName, parameterValue } });

            }
            return this;
        }

        public int Count
        {
            get {
                var parameterCount = 0;
                foreach (var shd in Shards)
                {
                    if (shd.Value is null || shd.Value.Count == 0)
                    {
                        parameterCount++;
                    }
                    else
                    {
                        parameterCount += shd.Value.Count;
                    }
                }
                return parameterCount;
            }
        }

        public bool IsSynchronized => false;

        public object SyncRoot => syncRoot;

        public void CopyTo(Array array, int index)
        {
            ArgumentNullException.ThrowIfNull(array);
            if (index < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }
            if (array.Length - index < this.Count)
            {
                throw new ArgumentException("The destination array is not long enough to copy all the items in the collection.", nameof(array));
            }
            var i = index;
            foreach (var itm in ((IEnumerable<ShardParameterValue>)this))
            {
                array.SetValue(itm, i);
                i++;
            }
        }

        public IEnumerator GetEnumerator() => ((IEnumerable<ShardParameterValue>)this).GetEnumerator();

        IEnumerator<ShardParameterValue> IEnumerable<ShardParameterValue>.GetEnumerator()
        {
            foreach (var shd in Shards)
            {
                if (shd.Value is null || shd.Value.Count == 0)
                {
                    yield return new ShardParameterValue(shd.Key, null, null);
                }
                else
                {
                    foreach (var prm in shd.Value)
                    {
                        yield return new ShardParameterValue(shd.Key, prm.Key, prm.Value);
                    }
                }
            }
        }
        public IDictionary<short, IDictionary<string, object>> Shards { get; private set; }

        public void Merge(IList<short> shardIds)
        {
            ArgumentNullException.ThrowIfNull(shardIds);
            foreach (var shardId in shardIds)
            {
                if (!Shards.ContainsKey(shardId))
                {
                    Shards.Add(shardId, new Dictionary<string, object>());
                }
            }
        }
        public void Merge(ShardsValues values)
        {
            ArgumentNullException.ThrowIfNull(values);
            // Validate before changing anything, so a duplicate parameter name leaves this collection unchanged.
            foreach (var shd in values.Shards)
            {
                if (shd.Value is not null && Shards.TryGetValue(shd.Key, out var existing) && existing is not null)
                {
                    foreach (var kv in shd.Value)
                    {
                        if (existing.ContainsKey(kv.Key))
                        {
                            throw new ArgumentException($"Shard {shd.Key} already has a parameter named '{kv.Key}'.", nameof(values));
                        }
                    }
                }
            }
            foreach (var shd in values.Shards)
            {
                if (!Shards.TryGetValue(shd.Key, out var prms) || prms is null)
                {
                    prms = new Dictionary<string, object>();
                    Shards[shd.Key] = prms;
                }
                if (shd.Value is not null)
                {
                    foreach (var kv in shd.Value)
                    {
                        prms.Add(kv);
                    }
                }
            }
        }
        public void Remove(short shardId)
        {
            if (Shards.ContainsKey(shardId))
            {
                Shards.Remove(shardId);
            }
        }


        #region Foreign ShardKey
        /// <summary>
        /// Given a list of Models with ShardKey keys, returns a distinct list of shard Ids, except for the shard Id specified.
        /// Useful for querying foreign shards after the primary shard has returned results.
        /// </summary>
        /// <param name="shardId">The shard id of the shard to exclude. This is typically the current shard and this function is used to determine if any records are foreign to it.</param>
        /// <param name="records">The list of models to evaluate.</param>
        /// <returns>A ShardsValues collection, with the shards listed. The values dictionary will be null.</returns>
        public static ShardsValues ShardListForeign<TRecord, TModel>(short shardId, List<TModel> records) where TModel : IKeyedModel<TRecord> where TRecord : IComparable
            => ShardsValues.ShardListForeign<TRecord, TModel>(shardId, (IList<TModel>)records);

        /// <summary>
        /// Given a list of Models with ShardKey keys, returns a distinct list of shard Ids, except for the shard Id specified.
        /// Useful for querying foreign shards after the primary shard has returned results.
        /// </summary>
        /// <param name="shardId">The shard id of the shard to exclude. This is typically the current shard and this function is used to determine if any records are foreign to it.</param>
        /// <param name="records">The list of models to evaluate.</param>
        /// <returns>A ShardsValues collection, with the shards listed. The values dictionary will be null.</returns>
        public static ShardsValues ShardListForeign<TRecord, TModel>(short shardId, IList<TModel> records) where TModel : IKeyedModel<TRecord> where TRecord : IComparable
        {
            var result = new ShardsValues();
            foreach (var record in records)
            {
                if (!record.Key.ShardId.Equals(shardId) && !result.Shards.ContainsKey(record.Key.ShardId))
                {
                    result.Add(record.Key.ShardId);
                }
            }
            return result;
        }

        /// <summary>
        /// Given a list of ShardKey values, returns a distinct list of shard Ids, except for the shard Id specified.
        /// Useful for querying foreign shards after the primary shard has returned results.
        /// </summary>
        /// <param name="shardId">The shard id of the shard to exclude. This is typically the current shard and this function is used to determine if any records are foreign to it.</param>
        /// <param name="records">The list of ShardKeys to evaluate.</param>
        /// <returns>A ShardsValues collection, with the shards listed. The values dictionary will be null.</returns>
        public static ShardsValues ShardListForeign<TRecord>(short shardId, List<ShardKey<TRecord>> records) where TRecord : IComparable
            => ShardsValues.ShardListForeign<TRecord>(shardId, (IList<ShardKey<TRecord>>) records);

        /// <summary>
        /// Given a list of ShardKey values, returns a distinct list of shard Ids, except for the shard Id specified.
        /// Useful for querying foreign shards after the primary shard has returned results.
        /// </summary>
        /// <param name="shardId">The shard id of the shard to exclude. This is typically the current shard and this function is used to determine if any records are foreign to it.</param>
        /// <param name="records">The list of ShardKeys to evaluate.</param>
        /// <returns>A ShardsValues collection, with the shards listed. The values dictionary will be null.</returns>
        public static ShardsValues ShardListForeign<TRecord>(short shardId, IList<ShardKey<TRecord>> records) where TRecord : IComparable
        {
            var result = new ShardsValues();
            foreach (var record in records)
            {
                if (!record.ShardId.Equals(shardId) && !result.Shards.ContainsKey(record.ShardId))
                {
                    result.Add(record.ShardId);
                }
            }
            return result;
        }


        /// <summary>
        /// Given a list of Models with ShardKey keys, returns a distinct list of shard Ids, except for the shard Id specified.
        /// Useful for querying foreign shards after the primary shard has returned results.
        /// </summary>
        /// <param name="shardId">The shard id of the shard to exclude. This is typically the current shard and this function is used to determine if any records are foreign to it.</param>
        /// <param name="records">The list of models to evaluate.</param>
        /// <returns>A ShardsValues collection, with the shards listed. The values dictionary will be null.</returns>
        public static ShardsValues ShardListForeign<TRecord, TChild, TModel>(short shardId, List<TModel> records) where TModel : IKeyedModel<TRecord, TChild> where TRecord : IComparable where TChild : IComparable
            => ShardsValues.ShardListForeign<TRecord, TChild, TModel>(shardId, (IList<TModel>)records);

        /// <summary>
        /// Given a list of Models with ShardKey keys, returns a distinct list of shard Ids, except for the shard Id specified.
        /// Useful for querying foreign shards after the primary shard has returned results.
        /// </summary>
        /// <param name="shardId">The shard id of the shard to exclude. This is typically the current shard and this function is used to determine if any records are foreign to it.</param>
        /// <param name="records">The list of models to evaluate.</param>
        /// <returns>A ShardsValues collection, with the shards listed. The values dictionary will be null.</returns>
        public static ShardsValues ShardListForeign<TRecord, TChild, TModel>(short shardId, IList<TModel> records) where TModel : IKeyedModel<TRecord, TChild> where TRecord : IComparable where TChild : IComparable
        {
            var result = new ShardsValues();
            foreach (var record in records)
            {
                if (!record.Key.ShardId.Equals(shardId) && !result.Shards.ContainsKey(record.Key.ShardId))
                {
                    result.Add(record.Key.ShardId);
                }
            }
            return result;
        }

        /// <summary>
        /// Given a list of ShardKey values, returns a distinct list of shard Ids, except for the shard Id specified.
        /// Useful for querying foreign shards after the primary shard has returned results.
        /// </summary>
        /// <param name="shardId">The shard id of the shard to exclude. This is typically the current shard and this function is used to determine if any records are foreign to it.</param>
        /// <param name="records">The list of ShardKeys to evaluate.</param>
        /// <returns>A ShardsValues collection, with the shards listed. The values dictionary will be null.</returns>
        public static ShardsValues ShardListForeign<TRecord, TChild>(short shardId, List<ShardKey<TRecord, TChild>> records) where TRecord : IComparable where TChild : IComparable
            => ShardsValues.ShardListForeign<TRecord, TChild>(shardId, (IList<ShardKey<TRecord, TChild>>)records);

        /// <summary>
        /// Given a list of ShardKey values, returns a distinct list of shard Ids, except for the shard Id specified.
        /// Useful for querying foreign shards after the primary shard has returned results.
        /// </summary>
        /// <param name="shardId">The shard id of the shard to exclude. This is typically the current shard and this function is used to determine if any records are foreign to it.</param>
        /// <param name="records">The list of ShardKeys to evaluate.</param>
        /// <returns>A ShardsValues collection, with the shards listed. The values dictionary will be null.</returns>
        public static ShardsValues ShardListForeign<TRecord, TChild>(short shardId, IList<ShardKey<TRecord, TChild>> records) where TRecord : IComparable where TChild : IComparable
        {
            var result = new ShardsValues();
            foreach (var record in records)
            {
                if (!record.ShardId.Equals(shardId) && !result.Shards.ContainsKey(record.ShardId))
                {
                    result.Add(record.ShardId);
                }
            }
            return result;
        }


        /// <summary>
        /// Given a list of Models with ShardKey keys, returns a distinct list of shard Ids, except for the shard Id specified.
        /// Useful for querying foreign shards after the primary shard has returned results.
        /// </summary>
        /// <param name="shardId">The shard id of the shard to exclude. This is typically the current shard and this function is used to determine if any records are foreign to it.</param>
        /// <param name="records">The list of models to evaluate.</param>
        /// <returns>A ShardsValues collection, with the shards listed. The values dictionary will be null.</returns>
        public static ShardsValues ShardListForeign<TRecord, TChild, TGrandChild, TModel>(short shardId, List<TModel> records) 
            where TModel : IKeyedModel<TRecord, TChild, TGrandChild> 
            where TRecord : IComparable 
            where TChild : IComparable
            where TGrandChild : IComparable
            => ShardsValues.ShardListForeign<TRecord, TChild, TGrandChild, TModel>(shardId, (IList<TModel>)records);

        /// <summary>
        /// Given a list of Models with ShardKey keys, returns a distinct list of shard Ids, except for the shard Id specified.
        /// Useful for querying foreign shards after the primary shard has returned results.
        /// </summary>
        /// <param name="shardId">The shard id of the shard to exclude. This is typically the current shard and this function is used to determine if any records are foreign to it.</param>
        /// <param name="records">The list of models to evaluate.</param>
        /// <returns>A ShardsValues collection, with the shards listed. The values dictionary will be null.</returns>
        public static ShardsValues ShardListForeign<TRecord, TChild, TGrandChild, TModel>(short shardId, IList<TModel> records) 
            where TModel : IKeyedModel<TRecord, TChild, TGrandChild> 
            where TRecord : IComparable 
            where TChild : IComparable
            where TGrandChild : IComparable
        {
            var result = new ShardsValues();
            foreach (var record in records)
            {
                if (!record.Key.ShardId.Equals(shardId) && !result.Shards.ContainsKey(record.Key.ShardId))
                {
                    result.Add(record.Key.ShardId);
                }
            }
            return result;
        }

        /// <summary>
        /// Given a list of ShardKey values, returns a distinct list of shard Ids, except for the shard Id specified.
        /// Useful for querying foreign shards after the primary shard has returned results.
        /// </summary>
        /// <param name="shardId">The shard id of the shard to exclude. This is typically the current shard and this function is used to determine if any records are foreign to it.</param>
        /// <param name="records">The list of ShardKeys to evaluate.</param>
        /// <returns>A ShardsValues collection, with the shards listed. The values dictionary will be null.</returns>
        public static ShardsValues ShardListForeign<TRecord, TChild, TGrandChild>(short shardId, List<ShardKey<TRecord, TChild, TGrandChild>> records) 
            where TRecord : IComparable 
            where TChild : IComparable
            where TGrandChild : IComparable
            => ShardsValues.ShardListForeign<TRecord, TChild, TGrandChild>(shardId, (IList<ShardKey<TRecord, TChild, TGrandChild>>)records);

        /// <summary>
        /// Given a list of ShardKey values, returns a distinct list of shard Ids, except for the shard Id specified.
        /// Useful for querying foreign shards after the primary shard has returned results.
        /// </summary>
        /// <param name="shardId">The shard id of the shard to exclude. This is typically the current shard and this function is used to determine if any records are foreign to it.</param>
        /// <param name="records">The list of ShardKeys to evaluate.</param>
        /// <returns>A ShardsValues collection, with the shards listed. The values dictionary will be null.</returns>
        public static ShardsValues ShardListForeign<TRecord, TChild, TGrandChild>(short shardId, IList<ShardKey<TRecord, TChild, TGrandChild>> records)
            where TRecord : IComparable
            where TChild : IComparable
            where TGrandChild : IComparable
        {
            var result = new ShardsValues();
            foreach (var record in records)
            {
                if (!record.ShardId.Equals(shardId) && !result.Shards.ContainsKey(record.ShardId))
                {
                    result.Add(record.ShardId);
                }
            }
            return result;
        }

        /// <summary>
        /// Given a list of Models with ShardKey keys, returns a distinct list of shard Ids, except for the shard Id specified.
        /// Useful for querying foreign shards after the primary shard has returned results.
        /// </summary>
        /// <param name="shardId">The shard id of the shard to exclude. This is typically the current shard and this function is used to determine if any records are foreign to it.</param>
        /// <param name="records">The list of models to evaluate.</param>
        /// <returns>A ShardsValues collection, with the shards listed. The values dictionary will be null.</returns>
        public static ShardsValues ShardListForeign<TRecord, TChild, TGrandChild, TGreatGrandChild, TModel>(short shardId, List<TModel> records)
            where TModel : IKeyedModel<TRecord, TChild, TGrandChild, TGreatGrandChild>
            where TRecord : IComparable
            where TChild : IComparable
            where TGrandChild : IComparable
            where TGreatGrandChild : IComparable
            => ShardsValues.ShardListForeign<TRecord, TChild, TGrandChild, TGreatGrandChild, TModel>(shardId, (IList<TModel>)records);

        /// <summary>
        /// Given a list of Models with ShardKey keys, returns a distinct list of shard Ids, except for the shard Id specified.
        /// Useful for querying foreign shards after the primary shard has returned results.
        /// </summary>
        /// <param name="shardId">The shard id of the shard to exclude. This is typically the current shard and this function is used to determine if any records are foreign to it.</param>
        /// <param name="records">The list of models to evaluate.</param>
        /// <returns>A ShardsValues collection, with the shards listed. The values dictionary will be null.</returns>
        public static ShardsValues ShardListForeign<TRecord, TChild, TGrandChild, TGreatGrandChild, TModel>(short shardId, IList<TModel> records)
            where TModel : IKeyedModel<TRecord, TChild, TGrandChild, TGreatGrandChild>
            where TRecord : IComparable
            where TChild : IComparable
            where TGrandChild : IComparable
            where TGreatGrandChild : IComparable
        {
            var result = new ShardsValues();
            foreach (var record in records)
            {
                if (!record.Key.ShardId.Equals(shardId) && !result.Shards.ContainsKey(record.Key.ShardId))
                {
                    result.Add(record.Key.ShardId);
                }
            }
            return result;
        }

        /// <summary>
        /// Given a list of ShardKey values, returns a distinct list of shard Ids, except for the shard Id specified.
        /// Useful for querying foreign shards after the primary shard has returned results.
        /// </summary>
        /// <param name="shardId">The shard id of the shard to exclude. This is typically the current shard and this function is used to determine if any records are foreign to it.</param>
        /// <param name="records">The list of ShardKeys to evaluate.</param>
        /// <returns>A ShardsValues collection, with the shards listed. The values dictionary will be null.</returns>
        public static ShardsValues ShardListForeign<TRecord, TChild, TGrandChild, TGreatGrandChild>(short shardId, List<ShardKey<TRecord, TChild, TGrandChild, TGreatGrandChild>> records)
            where TRecord : IComparable
            where TChild : IComparable
            where TGrandChild : IComparable
            where TGreatGrandChild : IComparable
            => ShardsValues.ShardListForeign<TRecord, TChild, TGrandChild, TGreatGrandChild>(shardId, (IList<ShardKey<TRecord, TChild, TGrandChild, TGreatGrandChild>>)records);

        /// <summary>
        /// Given a list of ShardKey values, returns a distinct list of shard Ids, except for the shard Id specified.
        /// Useful for querying foreign shards after the primary shard has returned results.
        /// </summary>
        /// <param name="shardId">The shard id of the shard to exclude. This is typically the current shard and this function is used to determine if any records are foreign to it.</param>
        /// <param name="records">The list of ShardKeys to evaluate.</param>
        /// <returns>A ShardsValues collection, with the shards listed. The values dictionary will be null.</returns>
        public static ShardsValues ShardListForeign<TRecord, TChild, TGrandChild, TGreatGrandChild>(short shardId, IList<ShardKey<TRecord, TChild, TGrandChild, TGreatGrandChild>> records)
            where TRecord : IComparable
            where TChild : IComparable
            where TGrandChild : IComparable
            where TGreatGrandChild : IComparable
        {
            var result = new ShardsValues();
            foreach (var record in records)
            {
                if (!record.ShardId.Equals(shardId) && !result.Shards.ContainsKey(record.ShardId))
                {
                    result.Add(record.ShardId);
                }
            }
            return result;
        }
        #endregion

    }
}
