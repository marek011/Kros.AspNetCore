using System;
using System.Threading;
using System.Threading.Tasks;

namespace Kros.AspNetCore.Authorization
{
    /// <summary>
    /// Abstraction for caching service used by authorization middleware.
    /// </summary>
    public interface ICacheService
    {
        /// <summary>
        /// Gets the value associated with this key if present.
        /// </summary>
        /// <typeparam name="T">The type of the cached value.</typeparam>
        /// <param name="key">The cache key.</param>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        /// <returns>The cached value or default if not found.</returns>
        Task<T> GetAsync<T>(string key, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sets a cache entry with the given key and value.
        /// </summary>
        /// <typeparam name="T">The type of the value to cache.</typeparam>
        /// <param name="key">The cache key.</param>
        /// <param name="value">The value to cache.</param>
        /// <param name="absoluteExpiration">Absolute expiration time.</param>
        /// <param name="slidingExpiration">Sliding expiration time.</param>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        Task SetAsync<T>(string key, T value, TimeSpan? absoluteExpiration = null, TimeSpan? slidingExpiration = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets a cached value or sets it using the factory function if not present.
        /// </summary>
        /// <typeparam name="T">The type of the cached value.</typeparam>
        /// <param name="key">The cache key.</param>
        /// <param name="factory">Factory function to create the value if not cached.</param>
        /// <param name="absoluteExpiration">Absolute expiration time.</param>
        /// <param name="slidingExpiration">Sliding expiration time.</param>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        /// <returns>The cached or newly created value.</returns>
        Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan? absoluteExpiration = null, TimeSpan? slidingExpiration = null, CancellationToken cancellationToken = default);
    }
}
