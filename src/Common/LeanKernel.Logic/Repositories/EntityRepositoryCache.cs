namespace LeanKernel.Logic.Repositories;

using System.Collections.Concurrent;

internal static class EntityRepositoryCache
{
    internal static readonly ConcurrentDictionary<Type, object> PartitionKeyCache = new();
}