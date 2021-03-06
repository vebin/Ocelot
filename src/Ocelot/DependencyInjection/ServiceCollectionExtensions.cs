﻿using CacheManager.Core;
using IdentityServer4.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Ocelot.Authentication.Handler.Creator;
using Ocelot.Authentication.Handler.Factory;
using Ocelot.Authorisation;
using Ocelot.Cache;
using Ocelot.Claims;
using Ocelot.Configuration.Authentication;
using Ocelot.Configuration.Creator;
using Ocelot.Configuration.File;
using Ocelot.Configuration.Parser;
using Ocelot.Configuration.Provider;
using Ocelot.Configuration.Repository;
using Ocelot.Configuration.Setter;
using Ocelot.Configuration.Validator;
using Ocelot.Controllers;
using Ocelot.DownstreamRouteFinder.Finder;
using Ocelot.DownstreamRouteFinder.UrlMatcher;
using Ocelot.DownstreamUrlCreator;
using Ocelot.DownstreamUrlCreator.UrlTemplateReplacer;
using Ocelot.Headers;
using Ocelot.Infrastructure.Claims.Parser;
using Ocelot.Infrastructure.RequestData;
using Ocelot.LoadBalancer.LoadBalancers;
using Ocelot.Logging;
using Ocelot.Middleware;
using Ocelot.QueryStrings;
using Ocelot.RateLimit;
using Ocelot.Request.Builder;
using Ocelot.Requester;
using Ocelot.Requester.QoS;
using Ocelot.Responder;
using Ocelot.ServiceDiscovery;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using FileConfigurationProvider = Ocelot.Configuration.Provider.FileConfigurationProvider;

namespace Ocelot.DependencyInjection
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddOcelotOutputCaching(this IServiceCollection services, Action<ConfigurationBuilderCachePart> settings)
        {
            var cacheManagerOutputCache = CacheFactory.Build<HttpResponseMessage>("OcelotOutputCache", settings);
            var ocelotCacheManager = new OcelotCacheManagerCache<HttpResponseMessage>(cacheManagerOutputCache);
            services.TryAddSingleton<ICacheManager<HttpResponseMessage>>(cacheManagerOutputCache);
            services.TryAddSingleton<IOcelotCache<HttpResponseMessage>>(ocelotCacheManager);

            return services;
        }

        public static IServiceCollection AddOcelot(this IServiceCollection services, IConfigurationRoot configurationRoot)
        {
            services.Configure<FileConfiguration>(configurationRoot);
            services.TryAddSingleton<IOcelotConfigurationCreator, FileOcelotConfigurationCreator>();
            services.TryAddSingleton<IOcelotConfigurationRepository, InMemoryOcelotConfigurationRepository>();
            services.TryAddSingleton<IConfigurationValidator, FileConfigurationValidator>();
            services.TryAddSingleton<IBaseUrlFinder, BaseUrlFinder>();
            services.TryAddSingleton<IClaimsToThingCreator, ClaimsToThingCreator>();
            services.TryAddSingleton<IAuthenticationOptionsCreator, AuthenticationOptionsCreator>();
            services.TryAddSingleton<IUpstreamTemplatePatternCreator, UpstreamTemplatePatternCreator>();
            services.TryAddSingleton<IRequestIdKeyCreator, RequestIdKeyCreator>();
            services.TryAddSingleton<IServiceProviderConfigurationCreator,ServiceProviderConfigurationCreator>();
            services.TryAddSingleton<IQoSOptionsCreator, QoSOptionsCreator>();
            services.TryAddSingleton<IReRouteOptionsCreator, ReRouteOptionsCreator>();
            services.TryAddSingleton<IRateLimitOptionsCreator, RateLimitOptionsCreator>();

            var identityServerConfiguration = IdentityServerConfigurationCreator.GetIdentityServerConfiguration();
            
            if(identityServerConfiguration != null)
            {
                services.TryAddSingleton<IIdentityServerConfiguration>(identityServerConfiguration);
                services.TryAddSingleton<IHashMatcher, HashMatcher>();
                services.AddIdentityServer()
                    .AddTemporarySigningCredential()
                    .AddInMemoryApiResources(new List<ApiResource>
                    {
                        new ApiResource
                        {
                            Name = identityServerConfiguration.ApiName,
                            Description = identityServerConfiguration.Description,
                            Enabled = identityServerConfiguration.Enabled,
                            DisplayName = identityServerConfiguration.ApiName,
                            Scopes = identityServerConfiguration.AllowedScopes.Select(x => new Scope(x)).ToList(),
                            ApiSecrets = new List<Secret>
                            {
                                new Secret
                                {
                                    Value = identityServerConfiguration.ApiSecret.Sha256()
                                }
                            }
                        }
                    })
                    .AddInMemoryClients(new List<Client>
                    {
                        new Client
                        {
                            ClientId = identityServerConfiguration.ApiName,
                            AllowedGrantTypes = GrantTypes.ResourceOwnerPassword,
                            ClientSecrets = new List<Secret> {new Secret(identityServerConfiguration.ApiSecret.Sha256())},
                            AllowedScopes = identityServerConfiguration.AllowedScopes,
                            AccessTokenType = identityServerConfiguration.AccessTokenType,
                            Enabled = identityServerConfiguration.Enabled,
                            RequireClientSecret = identityServerConfiguration.RequireClientSecret
                        }
                    }).AddResourceOwnerValidator<OcelotResourceOwnerPasswordValidator>();
            }

            var assembly = typeof(FileConfigurationController).GetTypeInfo().Assembly;

            services.AddMvcCore()
                .AddApplicationPart(assembly)
                .AddControllersAsServices()
                .AddAuthorization()
                .AddJsonFormatters();

            services.AddLogging();
            services.TryAddSingleton<IFileConfigurationRepository, FileConfigurationRepository>();
            services.TryAddSingleton<IFileConfigurationSetter, FileConfigurationSetter>();
            services.TryAddSingleton<IFileConfigurationProvider, FileConfigurationProvider>();
            services.TryAddSingleton<IQosProviderHouse, QosProviderHouse>();
            services.TryAddSingleton<IQoSProviderFactory, QoSProviderFactory>();
            services.TryAddSingleton<IServiceDiscoveryProviderFactory, ServiceDiscoveryProviderFactory>();
            services.TryAddSingleton<ILoadBalancerFactory, LoadBalancerFactory>();
            services.TryAddSingleton<ILoadBalancerHouse, LoadBalancerHouse>();
            services.TryAddSingleton<IOcelotLoggerFactory, AspDotNetLoggerFactory>();
            services.TryAddSingleton<IUrlBuilder, UrlBuilder>();
            services.TryAddSingleton<IRemoveOutputHeaders, RemoveOutputHeaders>();
            services.TryAddSingleton<IOcelotConfigurationProvider, OcelotConfigurationProvider>();
            services.TryAddSingleton<IClaimToThingConfigurationParser, ClaimToThingConfigurationParser>();
            services.TryAddSingleton<IAuthoriser, ClaimsAuthoriser>();
            services.TryAddSingleton<IAddClaimsToRequest, AddClaimsToRequest>();
            services.TryAddSingleton<IAddHeadersToRequest, AddHeadersToRequest>();
            services.TryAddSingleton<IAddQueriesToRequest, AddQueriesToRequest>();
            services.TryAddSingleton<IClaimsParser, ClaimsParser>();
            services.TryAddSingleton<IUrlPathToUrlTemplateMatcher, RegExUrlMatcher>();
            services.TryAddSingleton<IUrlPathPlaceholderNameAndValueFinder, UrlPathPlaceholderNameAndValueFinder>();
            services.TryAddSingleton<IDownstreamPathPlaceholderReplacer, DownstreamTemplatePathPlaceholderReplacer>();
            services.TryAddSingleton<IDownstreamRouteFinder, DownstreamRouteFinder.Finder.DownstreamRouteFinder>();
            services.TryAddSingleton<IHttpRequester, HttpClientHttpRequester>();
            services.TryAddSingleton<IHttpResponder, HttpContextResponder>();
            services.TryAddSingleton<IRequestCreator, HttpRequestCreator>();
            services.TryAddSingleton<IErrorsToHttpStatusCodeMapper, ErrorsToHttpStatusCodeMapper>();
            services.TryAddSingleton<IAuthenticationHandlerFactory, AuthenticationHandlerFactory>();
            services.TryAddSingleton<IAuthenticationHandlerCreator, AuthenticationHandlerCreator>();
            services.TryAddSingleton<IRateLimitCounterHandler, MemoryCacheRateLimitCounterHandler>();
            services.TryAddSingleton<IHttpClientCache, MemoryHttpClientCache>();

            // see this for why we register this as singleton http://stackoverflow.com/questions/37371264/invalidoperationexception-unable-to-resolve-service-for-type-microsoft-aspnetc
            // could maybe use a scoped data repository
            services.TryAddSingleton<IHttpContextAccessor, HttpContextAccessor>();
            services.TryAddScoped<IRequestScopedDataRepository, HttpDataRepository>();
            services.AddMemoryCache();
            return services;
        }
    }
}
