using Kros.AspNetCore.Extensions;
using Kros.AspNetCore.ServiceDiscovery;
using Kros.Utils;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Kros.AspNetCore.Authorization
{
    /// <summary>
    /// Middleware for user authorization.
    /// </summary>
    internal class GatewayAuthorizationMiddleware
    {
        /// <summary>
        /// HttpClient name used for communication between ApiGateway and Authorization service.
        /// </summary>
        public const string AuthorizationHttpClientName = "JwtAuthorizationClientName";

        private readonly RequestDelegate _next;
        private readonly GatewayJwtAuthorizationOptions _jwtAuthorizationOptions;
        private static Regex _cacheRegex = null;

        /// <summary>
        /// Ctor.
        /// </summary>
        /// <param name="next">Next middleware.</param>
        /// <param name="jwtAuthorizationOptions">Authorization options.</param>
        public GatewayAuthorizationMiddleware(
            RequestDelegate next,
            GatewayJwtAuthorizationOptions jwtAuthorizationOptions)
        {
            _next = Check.NotNull(next, nameof(next));
            _jwtAuthorizationOptions = Check.NotNull(jwtAuthorizationOptions, nameof(jwtAuthorizationOptions));
            if (!string.IsNullOrWhiteSpace(_jwtAuthorizationOptions.CacheKeyUrlPathRegexPattern))
            {
                _cacheRegex = new Regex(_jwtAuthorizationOptions.CacheKeyUrlPathRegexPattern);
            }
        }

        /// <summary>
        /// HttpContext pipeline processing.
        /// </summary>
        /// <param name="httpContext">Http context.</param>
        /// <param name="httpClientFactory">Http client factory.</param>
        /// <param name="cacheService">Cache service for caching authorization token.</param>
        /// <param name="serviceDiscoveryProvider">The service discovery provider.</param>
        public async Task Invoke(
            HttpContext httpContext,
            IHttpClientFactory httpClientFactory,
            ICacheService cacheService,
            IServiceDiscoveryProvider serviceDiscoveryProvider)
        {
            string userJwt = await GetUserAuthorizationJwtAsync(
                httpContext,
                httpClientFactory,
                cacheService,
                serviceDiscoveryProvider);

            if (!string.IsNullOrEmpty(userJwt))
            {
                AddUserProfileClaimsToIdentityAndHttpHeaders(httpContext, userJwt);
            }

            await _next(httpContext);
        }

        private async Task<string> GetUserAuthorizationJwtAsync(
            HttpContext httpContext,
            IHttpClientFactory httpClientFactory,
            ICacheService cacheService,
            IServiceDiscoveryProvider serviceDiscoveryProvider)
        {
            if (JwtAuthorizationHelper.TryGetTokenValue(httpContext.Request.Headers, out string token))
            {
                CacheHttpHeadersHelper.TryGetValue(
                    httpContext.Request.Headers,
                    _jwtAuthorizationOptions.CacheKeyHttpHeaders,
                    out string cacheKeyPart);
                string urlPathForCache = GetUrlPathForCacheKey(httpContext);
                if (urlPathForCache != null)
                {
                    cacheKeyPart += urlPathForCache;
                }
                string cacheKey = GetKeyAsString(token, cacheKeyPart);

                if (IsCacheAllowed() && !IsRequestPathAllowedForCache(httpContext.Request))
                {
                    string jwtToken = await cacheService.GetOrSetAsync(
                        cacheKey,
                        async () =>
                        {
                            string authUrl = _jwtAuthorizationOptions.GetAuthorizationUrl(serviceDiscoveryProvider) + httpContext.Request.Path.Value;
                            return await GetUserAuthorizationJwtFromServiceAsync(
                                httpContext,
                                httpClientFactory,
                                token,
                                authUrl);
                        },
                        _jwtAuthorizationOptions.CacheAbsoluteExpiration != TimeSpan.Zero ? _jwtAuthorizationOptions.CacheAbsoluteExpiration : null,
                        _jwtAuthorizationOptions.CacheSlidingExpirationOffset != TimeSpan.Zero ? _jwtAuthorizationOptions.CacheSlidingExpirationOffset : null);

                    return jwtToken;
                }
                else
                {
                    // Cache is disabled or path is ignored, get token directly
                    string authUrl = _jwtAuthorizationOptions.GetAuthorizationUrl(serviceDiscoveryProvider) + httpContext.Request.Path.Value;
                    return await GetUserAuthorizationJwtFromServiceAsync(
                        httpContext,
                        httpClientFactory,
                        token,
                        authUrl);
                }
            }
            else if (!string.IsNullOrEmpty(_jwtAuthorizationOptions.HashParameterName)
                && httpContext.Request.Query.TryGetValue(_jwtAuthorizationOptions.HashParameterName, out StringValues hashValue))
            {
                string cacheKey = GetKeyAsString(hashValue.ToString());
                
                if (IsCacheAllowed() && !IsRequestPathAllowedForCache(httpContext.Request))
                {
                    string jwtToken = await cacheService.GetOrSetAsync(
                        cacheKey,
                        async () =>
                        {
                            UriBuilder uriBuilder = new(_jwtAuthorizationOptions.GetHashAuthorization(serviceDiscoveryProvider));
                            uriBuilder.Query = QueryString.Create(
                                _jwtAuthorizationOptions.HashParameterName,
                                hashValue.ToString()).ToUriComponent();
                            return await GetUserAuthorizationJwtFromServiceAsync(
                                httpContext,
                                httpClientFactory,
                                StringValues.Empty,
                                uriBuilder.Uri.ToString());
                        },
                        _jwtAuthorizationOptions.CacheAbsoluteExpiration != TimeSpan.Zero ? _jwtAuthorizationOptions.CacheAbsoluteExpiration : null,
                        _jwtAuthorizationOptions.CacheSlidingExpirationOffset != TimeSpan.Zero ? _jwtAuthorizationOptions.CacheSlidingExpirationOffset : null);

                    return jwtToken;
                }
                else
                {
                    // Cache is disabled or path is ignored, get token directly
                    UriBuilder uriBuilder = new(_jwtAuthorizationOptions.GetHashAuthorization(serviceDiscoveryProvider));
                    uriBuilder.Query = QueryString.Create(
                        _jwtAuthorizationOptions.HashParameterName,
                        hashValue.ToString()).ToUriComponent();
                    return await GetUserAuthorizationJwtFromServiceAsync(
                        httpContext,
                        httpClientFactory,
                        StringValues.Empty,
                        uriBuilder.Uri.ToString());
                }
            }

            return string.Empty;
        }

        private async Task<string> GetUserAuthorizationJwtFromServiceAsync(
            HttpContext httpContext,
            IHttpClientFactory httpClientFactory,
            StringValues authHeader,
            string authorizationUrl)
        {
            using (HttpClient client = httpClientFactory.CreateClient(AuthorizationHttpClientName))
            {
                if (authHeader.Any())
                {
                    client.DefaultRequestHeaders.Add(HeaderNames.Authorization, authHeader.ToString());
                }
                if (_jwtAuthorizationOptions.ForwardedHeaders.Any())
                {
                    AddForwardedHeaders(client, httpContext.Request.Headers);
                }

                string jwtToken = await client.GetStringAndCheckResponseAsync(authorizationUrl,
                    new UnauthorizedAccessException(Properties.Resources.AuthorizationServiceForbiddenRequest));

                return jwtToken;
            }
        }

        private void AddForwardedHeaders(HttpClient client, IHeaderDictionary headers)
        {
            foreach (string headerName in _jwtAuthorizationOptions.ForwardedHeaders)
            {
                if (headers.TryGetValue(headerName, out StringValues value))
                {
                    client.DefaultRequestHeaders.Add(headerName, (IEnumerable<string>)value);
                }
            }
        }

        private bool IsRequestPathAllowedForCache(HttpRequest request)
            => _jwtAuthorizationOptions.IgnoredPathForCache
            .Contains(request.Path.Value.TrimEnd('/'), StringComparer.OrdinalIgnoreCase);

        private bool IsCacheAllowed()
            => _jwtAuthorizationOptions.CacheSlidingExpirationOffset != TimeSpan.Zero
                || _jwtAuthorizationOptions.CacheAbsoluteExpiration != TimeSpan.Zero;

        internal string GetUrlPathForCacheKey(HttpContext httpContext)
        {
            if (!string.IsNullOrWhiteSpace(_jwtAuthorizationOptions.CacheKeyUrlPathRegexPattern)
                && !string.IsNullOrWhiteSpace(httpContext.Request.Path))
            {
                Match match = _cacheRegex.Match(httpContext.Request.Path);
                if (match.Success)
                {
                    return match.Groups.Values.Last().Value;
                }
            }
            return null;
        }

        internal static int GetKey(StringValues value, string additionalKeyPart = null)
            => (additionalKeyPart is null) ? HashCode.Combine(value) : HashCode.Combine(value, additionalKeyPart);

        internal static string GetKeyAsString(StringValues value, string additionalKeyPart = null)
            => GetKey(value, additionalKeyPart).ToString();

        private static void AddUserProfileClaimsToIdentityAndHttpHeaders(HttpContext httpContext, string userJwtToken)
            => httpContext.Request.Headers[HeaderNames.Authorization] = $"{JwtAuthorizationHelper.AuthTokenPrefix} {userJwtToken}";
    }
}
