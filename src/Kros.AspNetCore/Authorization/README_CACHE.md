# Gateway Authorization Cache Configuration

The `GatewayAuthorizationMiddleware` supports multiple caching strategies for JWT tokens:

## Default Configuration (MemoryCache)

```csharp
services.AddGatewayJwtAuthorization();
```

This will use the default in-memory cache (`IMemoryCache`) which is suitable for single-instance applications.

## FusionCache Configuration

For multi-instance applications or enhanced caching features, you can use FusionCache with Redis:

### 1. Install FusionCache packages

```xml
<PackageReference Include="ZiggyCreatures.FusionCache" Version="1.0.0" />
<PackageReference Include="ZiggyCreatures.FusionCache.Serialization.SystemTextJson" Version="1.0.0" />
<PackageReference Include="ZiggyCreatures.FusionCache.Backplane.StackExchangeRedis" Version="1.0.0" />
```

### 2. Configure FusionCache with Redis

```csharp
// Configure Redis
services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = "localhost:6379";
});

// Configure FusionCache
services.AddFusionCache()
    .WithDefaultEntryOptions(new FusionCacheEntryOptions
    {
        Duration = TimeSpan.FromMinutes(30),
        IsFailSafeEnabled = true,
        FailSafeMaxDuration = TimeSpan.FromHours(2),
        FailSafeThrottleDuration = TimeSpan.FromSeconds(30)
    })
    .WithDistributedCache()
    .WithBackplane(new RedisBackplane(new RedisBackplaneOptions
    {
        Configuration = "localhost:6379"
    }))
    .WithSerializer(new FusionCacheSystemTextJsonSerializer());

// Configure Gateway Authorization with FusionCache
services.AddGatewayJwtAuthorizationWithFusionCache();
```

### 3. Alternative: Auto-detection (Recommended)

```csharp
// Configure FusionCache first (as above)
services.AddFusionCache()...

// Gateway authorization will automatically detect and use FusionCache
services.AddGatewayJwtAuthorization();
```

## Custom Cache Implementation

You can also provide your own cache implementation:

```csharp
services.AddGatewayJwtAuthorization(serviceProvider =>
{
    // Your custom cache service
    return new MyCacheService();
});
```

## Configuration Options

The cache behavior is controlled by the existing `GatewayJwtAuthorizationOptions`:

```json
{
  "GatewayJwtAuthorization": {
    "CacheSlidingExpirationOffset": "00:30:00",
    "CacheAbsoluteExpiration": "01:00:00",
    "IgnoredPathForCache": ["/health", "/metrics"],
    "CacheKeyHttpHeaders": ["Connection-Id", "Tenant-Id"],
    "CacheKeyUrlPathRegexPattern": "users/([0-9]+)"
  }
}
```

## Benefits of FusionCache

- **Multi-level caching**: L1 (memory) + L2 (distributed Redis)
- **Fail-safe**: Serves stale data if cache is temporarily unavailable
- **Cache stampede protection**: Prevents multiple requests for same data
- **Backplane support**: Cache invalidation across multiple instances
- **Advanced metrics**: Built-in performance monitoring

## Security Considerations

- JWT tokens are stateless and safe to cache
- Cache keys include user-specific data for isolation
- Cache expiration should be shorter than JWT token expiration
- Use Redis AUTH and TLS for production environments
