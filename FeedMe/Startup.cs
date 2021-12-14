using System;
using System.Threading;
using System.Threading.Tasks;
using FeedMe.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FeedMe
{
    public class Startup
    {
        public IConfigurationRoot Configuration { get; }

        public Startup(string[] args)
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("_config.json");

            Configuration = builder.Build();
        }

        public static async Task RunAsync(string[] args)
        {
            var startup = new Startup(args);
            await startup.RunAsync();
        }

        public async Task RunAsync()
        {
            var services = new ServiceCollection()
                .AddSingleton<RedditService>();
            ConfigureServices(services);

            var provider = services.BuildServiceProvider();
            
            await provider.GetRequiredService<RedditService>().StartAsync();
            await Task.Delay(Timeout.Infinite);
        }

        private void ConfigureServices(IServiceCollection services)
        {
            services
            .AddSingleton<RedditService>()
            .AddSingleton(Configuration);
        }
    }
}
