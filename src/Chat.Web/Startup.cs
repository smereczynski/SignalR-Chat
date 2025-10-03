using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Chat.Web.Hubs;
using Chat.Web.Models;
using Chat.Web.Repositories;
using Chat.Web.Options;
using Chat.Web.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using StackExchange.Redis;

namespace Chat.Web
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            // Options
            services.Configure<CosmosOptions>(Configuration.GetSection("Cosmos"));
            services.Configure<RedisOptions>(Configuration.GetSection("Redis"));
            services.Configure<AcsOptions>(Configuration.GetSection("Acs"));

            // Cosmos required
            var cosmosConn = Configuration["Cosmos:ConnectionString"];
            if (string.IsNullOrWhiteSpace(cosmosConn))
                throw new InvalidOperationException("Cosmos:ConnectionString is required for this app.");
            var cosmosOpts = new CosmosOptions
            {
                ConnectionString = cosmosConn,
                Database = Configuration["Cosmos:Database"] ?? "chat",
                MessagesContainer = Configuration["Cosmos:MessagesContainer"] ?? "messages",
                UsersContainer = Configuration["Cosmos:UsersContainer"] ?? "users",
                RoomsContainer = Configuration["Cosmos:RoomsContainer"] ?? "rooms",
            };
            services.AddSingleton(new CosmosClients(cosmosOpts));
            services.AddSingleton<IUsersRepository, CosmosUsersRepository>();
            services.AddSingleton<IRoomsRepository, CosmosRoomsRepository>();
            services.AddSingleton<IMessagesRepository, CosmosMessagesRepository>();

            // Redis OTP store
            var redisConn = Configuration["Redis:ConnectionString"];
            if (string.IsNullOrWhiteSpace(redisConn))
                throw new InvalidOperationException("Redis:ConnectionString is required for this app.");
            services.AddSingleton<IConnectionMultiplexer>(sp => ConnectionMultiplexer.Connect(redisConn));
            services.AddSingleton<IOtpStore, RedisOtpStore>();
            // Prefer ACS sender if configured, otherwise console
            var acsConn = Configuration["Acs:ConnectionString"];            
            if (!string.IsNullOrWhiteSpace(acsConn))
            {
                var acsOptions = new AcsOptions
                {
                    ConnectionString = acsConn,
                    EmailFrom = Configuration["Acs:EmailFrom"],
                    SmsFrom = Configuration["Acs:SmsFrom"]
                };
                services.AddSingleton<IOtpSender>(sp => new AcsOtpSender(acsOptions));
            }
            else
            {
                services.AddSingleton<IOtpSender, ConsoleOtpSender>();
            }
            services.AddRazorPages();
            services.AddControllers();
            services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                .AddCookie(options =>
                {
                    options.LoginPath = "/"; // the SPA handles login UI
                    options.AccessDeniedPath = "/";
                    options.SlidingExpiration = true;
                });
        // Always use Azure SignalR (no in-memory transport)
        services.AddSignalR()
            .AddAzureSignalR();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles();

            app.UseRouting();

            app.UseAuthentication();
            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapRazorPages();
                endpoints.MapControllers();
                endpoints.MapHub<ChatHub>("/chatHub");
            });
        }
    }
}
