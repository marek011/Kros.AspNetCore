using Microsoft.Extensions.Caching.Memory;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Kros.AspNetCore.Authorization
{
    /// <summary>
    /// Implementation of <see cref="ICacheService"/> using <see cref="IMemoryCache"/>.
    /// </summary>
    internal class MemoryCacheService : ICacheService
    {
        private readonly IMemoryCache _memoryCache;

        public MemoryCacheService(IMemoryCache memoryCache)
        {
            _memoryCache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
        }

        public Task<T> GetAsync<T>(string key, CancellationToken cancellationToken = default)
        {
            _memoryCache.TryGetValue(key, out T value);
            return Task.FromResult(value);
        }

        public Task SetAsync<T>(string key, T value, TimeSpan? absoluteExpiration = null, TimeSpan? slidingExpiration = null, CancellationToken cancellationToken = default)
        {
            var options = new MemoryCacheEntryOptions();

            if (absoluteExpiration.HasValue && absoluteExpiration.Value != TimeSpan.Zero)
            {
                options.SetAbsoluteExpiration(absoluteExpiration.Value);
            }

            if (slidingExpiration.HasValue && slidingExpiration.Value != TimeSpan.Zero)
            {
                options.SetSlidingExpiration(slidingExpiration.Value);
            }

            _memoryCache.Set(key, value, options);
            return Task.CompletedTask;
        }

        public async Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan? absoluteExpiration = null, TimeSpan? slidingExpiration = null, CancellationToken cancellationToken = default)
        {
            if (_memoryCache.TryGetValue(key, out T cachedValue))
            {
                return cachedValue;
            }

            var value = await factory().ConfigureAwait(false);
            await SetAsync(key, value, absoluteExpiration, slidingExpiration, cancellationToken).ConfigureAwait(false);
            return value;
        }
    }
}
