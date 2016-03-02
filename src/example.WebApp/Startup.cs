﻿
using cloudscribe.Web.SimpleAuth.Services;
using cloudscribe.Web.Navigation;
using cloudscribe.Web.Navigation.Caching;
using cloudscribe.Web.SimpleAuth.Models;
using Microsoft.AspNet.Authentication.Cookies;
using Microsoft.AspNet.Builder;
using Microsoft.AspNet.Hosting;
using Microsoft.AspNet.Http;
using Microsoft.AspNet.Http.Internal;
using Microsoft.AspNet.Identity;
using Microsoft.AspNet.Mvc.Razor;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.OptionsModel;
using Microsoft.Extensions.PlatformAbstractions;
using System.Collections.Generic;


namespace example.WebApp
{
    public class Startup
    {
        public Startup(IHostingEnvironment env, IApplicationEnvironment appEnv)
        {
            var builder = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json")
                .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true);

            // you can use whatever file name you like and it is probably a good idea to use a custom file name
            // just an a small extra protection in case hackers try some kind of attack based on knowing the name of the file
            // it should not be possible for anyone to get files outside of wwwroot using http requests
            // but every little thing you can do for stronger security is a good idea
            builder.AddJsonFile("simpleauthsettings.json", optional: true);

            // this file name is ignored by gitignore in our git repo
            // so you can create it and use on your local dev machine
            // remember last config source added wins if it has the same settings
            //builder.AddJsonFile("appsettings.local.overrides.json", optional: true);

            if (env.IsDevelopment())
            {
                // This reads the configuration keys from the secret store.
                // For more details on using the user secret store see http://go.microsoft.com/fwlink/?LinkID=532709
                // UserSecrets is a configuration source you can use to keep settings secret on your dev machine if needed
                builder.AddUserSecrets();
            }

            // note that the order in which configuration sources are added is important
            // if the same settings exist in a source registered later, the later settings win
            // so for example in production or in Azure hosting you might use environment variables
            // while on your dev machine using the json file as above
            builder.AddEnvironmentVariables();

            Configuration = builder.Build();
        }

        public IConfigurationRoot Configuration { get; set; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            // see this method below and configure your security policy
            ConfigureAuthPolicy(services);

            services.Configure<MultiTenancyOptions>(Configuration.GetSection("MultiTenancy"));
            services.AddMultitenancy<AppTenant, CachingAppTenantResolver>();
            // Hosting doesn't add IHttpContextAccessor by default
            //services.TryAddSingleton<IHttpContextAccessor, HttpContextAccessor>();

            services.Configure<SimpleAuthSettings>(Configuration.GetSection("SimpleAuthSettings"));
            //services.AddScoped<IUserLookupProvider, DefaultUserLookupProvider>(); // single tenant
            services.AddScoped<IUserLookupProvider, AppTenantUserLookupProvider>();
            services.Configure<List<SimpleAuthUser>>(Configuration.GetSection("Users"));
            services.AddScoped<IPasswordHasher<SimpleAuthUser>, PasswordHasher<SimpleAuthUser>>();
            //services.AddScoped<IAuthSettingsResolver, DefaultAuthSettingsResolver>();
            services.AddScoped<IAuthSettingsResolver, AppTenantAuthSettingsResolver>();
            services.AddScoped<SignInManager, SignInManager>();


            // this demo is also using the cloudscribe.Web.Navigation library
            //https://github.com/joeaudette/cloudscribe.Web.Navigation
            services.TryAddScoped<ITreeCache, MemoryTreeCache>();
            services.AddScoped<INavigationTreeBuilder, XmlNavigationTreeBuilder>();
            services.AddScoped<NavigationTreeBuilderService, NavigationTreeBuilderService>();
            services.AddScoped<INodeUrlPrefixProvider, DefaultNodeUrlPrefixProvider>();
            services.AddScoped<INavigationNodePermissionResolver, NavigationNodePermissionResolver>();
            services.Configure<NavigationOptions>(Configuration.GetSection("NavigationOptions"));
            


            services.AddMvc();

            services.Configure<RazorViewEngineOptions>(options =>
            {
                options.ViewLocationExpanders.Add(new TenantViewLocationExpander());
            });


        }

        
        // note that the DI can inject whatever you need into this method signature
        // I added IOptions<SimpleAuthSettings> authSettingsAccessor to the method signature
        // you can add anything you want as long as you register it in ConfigureServices
        public void Configure(
            IApplicationBuilder app, 
            IHostingEnvironment env, 
            ILoggerFactory loggerFactory,
            IOptions<SimpleAuthSettings> authSettingsAccessor  
            )
        {
            loggerFactory.AddConsole(Configuration.GetSection("Logging"));
            loggerFactory.AddDebug();

            if (env.IsDevelopment())
            {
                app.UseBrowserLink();
                app.UseDeveloperExceptionPage();
               
            }
            else
            {
                app.UseExceptionHandler("/Home/Error");
            }

            
            app.UseIISPlatformHandler(options => options.AuthenticationDescriptions.Clear());
            app.UseStaticFiles();

            app.UseMultitenancy<AppTenant>();

            // Add cookie-based authentication to the request pipeline

            //SimpleAuthSettings authSettings = authSettingsAccessor.Value;

            //var ApplicationCookie = new CookieAuthenticationOptions
            //{
            //    AuthenticationScheme = authSettings.AuthenticationScheme,
            //    CookieName = authSettings.AuthenticationScheme,
            //    AutomaticAuthenticate = true,
            //    AutomaticChallenge = true,
            //    LoginPath = new PathString("/Login/Index"),
            //    Events = new CookieAuthenticationEvents
            //    {
            //        //OnValidatePrincipal = SecurityStampValidator.ValidatePrincipalAsync
            //    }
            //};

            //app.UseCookieAuthentication(ApplicationCookie);

            app.UsePerTenant<AppTenant>((ctx, builder) =>
            {
                builder.UseCookieAuthentication(options =>
                {
                    options.AuthenticationScheme = ctx.Tenant.AuthenticationScheme;
                    options.LoginPath = new PathString("/account/login");
                    options.AccessDeniedPath = new PathString("/account/forbidden");
                    options.AutomaticAuthenticate = true;
                    options.AutomaticChallenge = true;
                    options.CookieName = $"{ctx.Tenant.Id}.application";
                    options.Events = new CookieAuthenticationEvents
                    {
                        OnValidatePrincipal = SecurityStampValidator.ValidatePrincipalAsync
                    };
                });

                //builder.UseGoogleAuthentication(options =>
                //{
                //    options.AuthenticationScheme = "Google";
                //    options.SignInScheme = "Cookies";

                //    options.ClientId = Configuration[$"{ctx.Tenant.Id}:GoogleClientId"];
                //    options.ClientSecret = Configuration[$"{ctx.Tenant.Id}:GoogleClientSecret"];
                //});
            });

            // Add MVC to the request pipeline.
            app.UseMvc(routes =>
            {
                routes.MapRoute(
                    name: "default",
                    template: "{controller=Home}/{action=Index}/{id?}");
            });
        }

        private void ConfigureAuthPolicy(IServiceCollection services)
        {
            // read the docs to better understand authorization policy configuration
            //https://docs.asp.net/en/latest/security/authorization/policies.html

            services.AddAuthorization(options =>
            {
                // Note that the navigation menu uses cloudscribe.Web.Navigation
                // which filters the menu based on role names not on policy names
                // so if you change the policy roles you need to update the navigation.xml file
                //

                // see the simpleauthsettings.json file to understand how to configure a users role membership
                
                options.AddPolicy(
                    "AdminPolicy",
                    authBuilder =>
                    {
                        authBuilder.RequireRole("Admins");
                    }
                 );

                options.AddPolicy(
                    "MembersOnlyPolicy",
                    authBuilder =>
                    {
                        authBuilder.RequireRole("Admins", "Members");
                    }
                 );

                // add other policies here 

            });

        }

        public static void Main(string[] args) => WebApplication.Run<Startup>(args);
    }
}
