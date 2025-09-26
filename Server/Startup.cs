using System.Net;
using System.Text;
using AspNetCoreRateLimit;
using LaciSynchroni.Common.SignalR;
using LaciSynchroni.Server.Controllers;
using LaciSynchroni.Server.Hubs;
using LaciSynchroni.Server.Services;
using LaciSynchroni.Shared.Data;
using LaciSynchroni.Shared.Metrics;
using LaciSynchroni.Shared.RequirementHandlers;
using LaciSynchroni.Shared.Services;
using LaciSynchroni.Shared.Utils;
using LaciSynchroni.Shared.Utils.Configuration;
using MessagePack;
using MessagePack.Resolvers;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Prometheus;
using StackExchange.Redis;
using StackExchange.Redis.Extensions.Core.Configuration;
using StackExchange.Redis.Extensions.System.Text.Json;

namespace LaciSynchroni.Server;

public class Startup
{
    private readonly ILogger<Startup> _logger;

    public Startup(IConfiguration configuration, ILogger<Startup> logger)
    {
        Configuration = configuration;
        _logger = logger;
    }

    public IConfiguration Configuration { get; }

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddHttpContextAccessor();

        services.AddTransient(_ => Configuration);

        var config = Configuration.GetRequiredSection("LaciSynchroni");

        services.Configure<ForwardedHeadersOptions>(Configuration.GetSection("ForwardedHeaders"));

        // configure metrics
        ConfigureMetrics(services);

        // configure database
        ConfigureDatabase(services, config);

        // configure authentication and authorization
        ConfigureAuthorization(services);

        // configure rate limiting
        ConfigureIpRateLimiting(services);

        // configure SignalR
        ConfigureSignalR(services, config);

        // configure laci specific services
        ConfigureLaciServices(services, config);

        services.AddHealthChecks();
        services.AddControllers().ConfigureApplicationPartManager(a =>
        {
            a.FeatureProviders.Remove(a.FeatureProviders.OfType<ControllerFeatureProvider>().First());
            if (config.GetValue<Uri>(nameof(ServerConfiguration.MainServerAddress), defaultValue: null) == null)
            {
                a.FeatureProviders.Add(new AllowedControllersFeatureProvider(
                    typeof(LaciServerConfigurationController),
                    typeof(LaciBaseConfigurationController),
                    typeof(ClientMessageController),
                    typeof(ClientConfigurationController)
                    ));
            }
            else
            {
                a.FeatureProviders.Add(new AllowedControllersFeatureProvider());
            }
        });
    }

    private void ConfigureLaciServices(IServiceCollection services, IConfigurationSection config)
    {
        bool isMainServer = config.GetValue<Uri>(nameof(ServerConfiguration.MainServerAddress), defaultValue: null) == null;

        services.Configure<ServerConfiguration>(config);
        services.Configure<LaciConfigurationBase>(config);

        services.AddSingleton<ServerTokenGenerator>();
        services.AddSingleton<SystemInfoService>();
        services.AddSingleton<OnlineSyncedPairCacheService>();
        services.AddHostedService(provider => provider.GetService<SystemInfoService>());
        // configure services based on main server status
        ConfigureServicesBasedOnShardType(services, config, isMainServer);

        services.AddSingleton(s => new LaciCensus(s.GetRequiredService<ILogger<LaciCensus>>()));
        services.AddHostedService(p => p.GetRequiredService<LaciCensus>());

        if (isMainServer)
        {
            services.AddSingleton<UserCleanupService>();
            services.AddHostedService(provider => provider.GetService<UserCleanupService>());
            services.AddSingleton<CharaDataCleanupService>();
            services.AddHostedService(provider => provider.GetService<CharaDataCleanupService>());
            services.AddHostedService<ClientPairPermissionsCleanupService>();
        }

        services.AddSingleton<GPoseLobbyDistributionService>();
        services.AddHostedService(provider => provider.GetService<GPoseLobbyDistributionService>());
    }

    private static void ConfigureSignalR(IServiceCollection services, IConfigurationSection config)
    {
        services.AddSingleton<IUserIdProvider, IdBasedUserIdProvider>();
        services.AddSingleton<ConcurrencyFilter>();

        var signalRServiceBuilder = services.AddSignalR(hubOptions =>
        {
            hubOptions.MaximumReceiveMessageSize = long.MaxValue;
            hubOptions.EnableDetailedErrors = true;
            hubOptions.MaximumParallelInvocationsPerClient = 10;
            hubOptions.StreamBufferCapacity = 200;

            hubOptions.AddFilter<SignalRLimitFilter>();
            hubOptions.AddFilter<ConcurrencyFilter>();
        }).AddMessagePackProtocol(opt =>
        {
            var resolver = CompositeResolver.Create(StandardResolverAllowPrivate.Instance,
                BuiltinResolver.Instance,
                AttributeFormatterResolver.Instance,
                // replace enum resolver
                DynamicEnumAsStringResolver.Instance,
                DynamicGenericResolver.Instance,
                DynamicUnionResolver.Instance,
                DynamicObjectResolver.Instance,
                PrimitiveObjectResolver.Instance,
                // final fallback(last priority)
                StandardResolver.Instance);

            opt.SerializerOptions = MessagePackSerializerOptions.Standard
                .WithCompression(MessagePackCompression.Lz4Block)
                .WithResolver(resolver);
        });


        // configure redis for SignalR
        var redisConnection = config.GetValue(nameof(ServerConfiguration.RedisConnectionString), string.Empty);
        signalRServiceBuilder.AddStackExchangeRedis(redisConnection, options => { });

        var options = ConfigurationOptions.Parse(redisConnection);

        var endpoint = options.EndPoints[0];
        string address = "";
        int port = 0;
        if (endpoint is DnsEndPoint dnsEndPoint) { address = dnsEndPoint.Host; port = dnsEndPoint.Port; }
        if (endpoint is IPEndPoint ipEndPoint) { address = ipEndPoint.Address.ToString(); port = ipEndPoint.Port; }
        var redisConfiguration = new RedisConfiguration()
        {
            AbortOnConnectFail = true,
            KeyPrefix = "",
            Hosts = new RedisHost[]
            {
                new RedisHost(){ Host = address, Port = port },
            },
            AllowAdmin = true,
            ConnectTimeout = options.ConnectTimeout,
            Database = 0,
            Ssl = false,
            Password = options.Password,
            ServerEnumerationStrategy = new ServerEnumerationStrategy()
            {
                Mode = ServerEnumerationStrategy.ModeOptions.All,
                TargetRole = ServerEnumerationStrategy.TargetRoleOptions.Any,
                UnreachableServerAction = ServerEnumerationStrategy.UnreachableServerActionOptions.Throw,
            },
            MaxValueLength = 1024,
            PoolSize = config.GetValue(nameof(ServerConfiguration.RedisPool), 50),
            SyncTimeout = options.SyncTimeout,
        };

        services.AddStackExchangeRedisExtensions<SystemTextJsonSerializer>(redisConfiguration);
    }

    private void ConfigureIpRateLimiting(IServiceCollection services)
    {
        services.Configure<IpRateLimitOptions>(Configuration.GetSection("IpRateLimiting"));
        services.Configure<IpRateLimitPolicies>(Configuration.GetSection("IpRateLimitPolicies"));
        services.AddSingleton<IRateLimitConfiguration, RateLimitConfiguration>();
        services.AddMemoryCache();
        services.AddInMemoryRateLimiting();
    }

    private static void ConfigureAuthorization(IServiceCollection services)
    {
        services.AddTransient<IAuthorizationHandler, UserRequirementHandler>();
        services.AddTransient<IAuthorizationHandler, ValidTokenRequirementHandler>();
        services.AddTransient<IAuthorizationHandler, ValidTokenHubRequirementHandler>();

        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IConfigurationService<LaciConfigurationBase>>((options, config) =>
            {
                options.TokenValidationParameters = new()
                {
                    ValidateIssuer = false,
                    ValidateLifetime = true,
                    ValidateAudience = false,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(config.GetValue<string>(nameof(LaciConfigurationBase.Jwt))))
                    {
                        KeyId = config.GetValue<string>(nameof(LaciConfigurationBase.JwtKeyId)),
                    },
                };
                options.Events = new JwtBearerEvents
                {
                    OnAuthenticationFailed = context =>
                    {
                        Console.Write($"Auth failed: {context.Exception}");
                        return Task.CompletedTask;
                    }
                };
            });

        services.AddAuthentication(o =>
        {
            o.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            o.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            o.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
        }).AddJwtBearer();

        services.AddAuthorization(options =>
        {
            options.DefaultPolicy = new AuthorizationPolicyBuilder()
                .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser().Build();
            options.AddPolicy("Authenticated", policy =>
            {
                policy.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme);
                policy.RequireAuthenticatedUser();
                policy.AddRequirements(new ValidTokenRequirement());
            });
            options.AddPolicy("Identified", policy =>
            {
                policy.AddRequirements(new UserRequirement(UserRequirements.Identified));
                policy.AddRequirements(new ValidTokenRequirement());

            });
            options.AddPolicy("Admin", policy =>
            {
                policy.AddRequirements(new UserRequirement(UserRequirements.Identified | UserRequirements.Administrator));
                policy.AddRequirements(new ValidTokenRequirement());

            });
            options.AddPolicy("Moderator", policy =>
            {
                policy.AddRequirements(new UserRequirement(UserRequirements.Identified | UserRequirements.Moderator | UserRequirements.Administrator));
                policy.AddRequirements(new ValidTokenRequirement());
            });
            options.AddPolicy("Internal", new AuthorizationPolicyBuilder().RequireClaim(LaciClaimTypes.Internal, "true").Build());
        });
    }

    private void ConfigureDatabase(IServiceCollection services, IConfigurationSection config)
    {
        services.AddDbContextPool<LaciDbContext>(options =>
        {
            options.UseNpgsql(Configuration.GetConnectionString("DefaultConnection"), builder =>
            {
                builder.MigrationsHistoryTable("_efmigrationshistory", "public");
                builder.MigrationsAssembly("LaciSynchroni.Shared");
            }).UseSnakeCaseNamingConvention();
            options.EnableThreadSafetyChecks(false);
        }, config.GetValue(nameof(LaciConfigurationBase.DbContextPoolSize), 1024));
        services.AddDbContextFactory<LaciDbContext>(options =>
        {
            options.UseNpgsql(Configuration.GetConnectionString("DefaultConnection"), builder =>
            {
                builder.MigrationsHistoryTable("_efmigrationshistory", "public");
                builder.MigrationsAssembly("LaciSynchroni.Shared");
            }).UseSnakeCaseNamingConvention();
            options.EnableThreadSafetyChecks(false);
        });
    }

    private static void ConfigureMetrics(IServiceCollection services)
    {
        services.AddSingleton<LaciMetrics>(m => new LaciMetrics(m.GetService<ILogger<LaciMetrics>>(), new List<string>
        {
            MetricsAPI.CounterInitializedConnections,
            MetricsAPI.CounterUserPushData,
            MetricsAPI.CounterUserPushDataTo,
            MetricsAPI.CounterUsersRegisteredDeleted,
            MetricsAPI.CounterAuthenticationCacheHits,
            MetricsAPI.CounterAuthenticationFailures,
            MetricsAPI.CounterAuthenticationRequests,
            MetricsAPI.CounterAuthenticationSuccesses,
            MetricsAPI.CounterUserPairCacheHit,
            MetricsAPI.CounterUserPairCacheMiss,
            MetricsAPI.CounterUserPairCacheNewEntries,
            MetricsAPI.CounterUserPairCacheUpdatedEntries,
        }, new List<string>
        {
            MetricsAPI.GaugeAuthorizedConnections,
            MetricsAPI.GaugeConnections,
            MetricsAPI.GaugePairs,
            MetricsAPI.GaugePairsPaused,
            MetricsAPI.GaugeAvailableIOWorkerThreads,
            MetricsAPI.GaugeAvailableWorkerThreads,
            MetricsAPI.GaugeGroups,
            MetricsAPI.GaugeGroupPairs,
            MetricsAPI.GaugeUsersRegistered,
            MetricsAPI.GaugeAuthenticationCacheEntries,
            MetricsAPI.GaugeUserPairCacheEntries,
            MetricsAPI.GaugeUserPairCacheUsers,
            MetricsAPI.GaugeGposeLobbies,
            MetricsAPI.GaugeGposeLobbyUsers,
            MetricsAPI.GaugeHubConcurrency,
            MetricsAPI.GaugeHubQueuedConcurrency,
        }));
    }

    private static void ConfigureServicesBasedOnShardType(IServiceCollection services, IConfigurationSection config, bool isMainServer)
    {
        if (!isMainServer)
        {
            services.AddSingleton<IConfigurationService<ServerConfiguration>, LaciConfigurationServiceClient<ServerConfiguration>>();
            services.AddSingleton<IConfigurationService<LaciConfigurationBase>, LaciConfigurationServiceClient<LaciConfigurationBase>>();

            services.AddHostedService(p => (LaciConfigurationServiceClient<ServerConfiguration>)p.GetService<IConfigurationService<ServerConfiguration>>());
            services.AddHostedService(p => (LaciConfigurationServiceClient<LaciConfigurationBase>)p.GetService<IConfigurationService<LaciConfigurationBase>>());
        }
        else
        {
            services.AddSingleton<IConfigurationService<ServerConfiguration>, LaciConfigurationServiceServer<ServerConfiguration>>();
            services.AddSingleton<IConfigurationService<LaciConfigurationBase>, LaciConfigurationServiceServer<LaciConfigurationBase>>();
        }
    }

    public void Configure(IApplicationBuilder app, IWebHostEnvironment env, ILogger<Startup> logger)
    {
        logger.LogInformation("Running Configure");

        var config = app.ApplicationServices.GetRequiredService<IConfigurationService<LaciConfigurationBase>>();

        app.UseForwardedHeaders();
        app.UseIpRateLimiting();

        app.UseRouting();

        app.UseWebSockets();
        app.UseHttpMetrics();

        var metricServer = new KestrelMetricServer(config.GetValueOrDefault<int>(nameof(LaciConfigurationBase.MetricsPort), 4980));
        metricServer.Start();

        app.UseAuthentication();
        app.UseAuthorization();

        app.UseEndpoints(endpoints =>
        {
            endpoints.MapHub<ServerHub>(IServerHub.Path, options =>
            {
                options.ApplicationMaxBufferSize = 5242880;
                options.TransportMaxBufferSize = 5242880;
                options.Transports = HttpTransportType.WebSockets | HttpTransportType.ServerSentEvents | HttpTransportType.LongPolling;
            });

            endpoints.MapHealthChecks("/health").AllowAnonymous();
            endpoints.MapControllers();

            foreach (var source in endpoints.DataSources.SelectMany(e => e.Endpoints).Cast<RouteEndpoint>())
            {
                if (source == null) continue;
                _logger.LogInformation("Endpoint: {url} ", source.RoutePattern.RawText);
            }
        });

    }
}
