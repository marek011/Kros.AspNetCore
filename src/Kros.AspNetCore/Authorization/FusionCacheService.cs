using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Kros.AspNetCore.Authorization
{
    /// <summary>
    /// Implementation of <see cref="ICacheService"/> using FusionCache.
    /// This implementation requires FusionCache to be registered in DI container.
    /// </summary>
    internal class FusionCacheService : ICacheService
    {
        private readonly object _fusionCache;
        private readonly MethodInfo _tryGetAsyncMethod;
        private readonly MethodInfo _setAsyncMethod;
        private readonly MethodInfo _getOrSetAsyncMethod;

        public FusionCacheService(object fusionCache)
        {
            _fusionCache = fusionCache ?? throw new ArgumentNullException(nameof(fusionCache));
            var fusionCacheType = fusionCache.GetType();

            // Cache reflection methods for better performance
            _tryGetAsyncMethod = GetGenericMethod(fusionCacheType, "TryGetAsync", typeof(string), typeof(CancellationToken));
            _setAsyncMethod = GetGenericMethod(fusionCacheType, "SetAsync", typeof(string), typeof(object), typeof(TimeSpan), typeof(CancellationToken));
            _getOrSetAsyncMethod = GetGenericMethod(fusionCacheType, "GetOrSetAsync", typeof(string), typeof(Func<CancellationToken, Task<object>>), typeof(TimeSpan), typeof(CancellationToken));
        }

        public async Task<T> GetAsync<T>(string key, CancellationToken cancellationToken = default)
        {
            try
            {
                if (_tryGetAsyncMethod != null)
                {
                    var genericMethod = _tryGetAsyncMethod.MakeGenericMethod(typeof(T));
                    var task = (Task)genericMethod.Invoke(_fusionCache, new object[] { key, cancellationToken });
                    await task.ConfigureAwait(false);
                    
                    var result = task.GetType().GetProperty("Result")?.GetValue(task);
                    
                    // FusionCache returns MaybeValue<T>, check if it has value
                    if (result != null)
                    {
                        var hasValueProperty = result.GetType().GetProperty("HasValue");
                        if (hasValueProperty != null && (bool)hasValueProperty.GetValue(result))
                        {
                            var valueProperty = result.GetType().GetProperty("Value");
                            return (T)valueProperty?.GetValue(result);
                        }
                    }
                }
            }
            catch
            {
                // If FusionCache fails, return default
            }

            return default(T);
        }

        public async Task SetAsync<T>(string key, T value, TimeSpan? absoluteExpiration = null, TimeSpan? slidingExpiration = null, CancellationToken cancellationToken = default)
        {
            try
            {
                if (_setAsyncMethod != null)
                {
                    var duration = absoluteExpiration ?? slidingExpiration ?? TimeSpan.FromMinutes(30);
                    var genericMethod = _setAsyncMethod.MakeGenericMethod(typeof(T));
                    var task = (Task)genericMethod.Invoke(_fusionCache, new object[] { key, value, duration, cancellationToken });
                    await task.ConfigureAwait(false);
                }
            }
            catch
            {
                // If FusionCache fails, ignore silently (cache miss is acceptable in middleware)
            }
        }

        public async Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan? absoluteExpiration = null, TimeSpan? slidingExpiration = null, CancellationToken cancellationToken = default)
        {
            try
            {
                if (_getOrSetAsyncMethod != null)
                {
                    var duration = absoluteExpiration ?? slidingExpiration ?? TimeSpan.FromMinutes(30);
                    var genericMethod = _getOrSetAsyncMethod.MakeGenericMethod(typeof(T));
                    
                    // Create wrapper factory that matches FusionCache signature
                    Func<CancellationToken, Task<T>> wrappedFactory = async (ct) => await factory().ConfigureAwait(false);
                    
                    var task = (Task<T>)genericMethod.Invoke(_fusionCache, new object[] { key, wrappedFactory, duration, cancellationToken });
                    return await task.ConfigureAwait(false);
                }
            }
            catch
            {
                // Fallback to manual get/set if FusionCache fails
                try
                {
                    var cachedValue = await GetAsync<T>(key, cancellationToken).ConfigureAwait(false);
                    if (cachedValue != null && !cachedValue.Equals(default(T)))
                    {
                        return cachedValue;
                    }

                    var value = await factory().ConfigureAwait(false);
                    await SetAsync(key, value, absoluteExpiration, slidingExpiration, cancellationToken).ConfigureAwait(false);
                    return value;
                }
                catch
                {
                    // Final fallback - just call factory
                    return await factory().ConfigureAwait(false);
                }
            }

            // Final fallback
            return await factory().ConfigureAwait(false);
        }

        private static MethodInfo GetGenericMethod(Type type, string methodName, params Type[] parameterTypes)
        {
            try
            {
                return type.GetMethod(methodName, parameterTypes);
            }
            catch
            {
                return null;
            }
        }
    }
}
