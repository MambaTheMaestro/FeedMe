using System.Threading.Tasks;

namespace FeedMe
{
    class Program
    {
        public static Task Main(string[] args)
        => Startup.RunAsync(args);
    }
}
