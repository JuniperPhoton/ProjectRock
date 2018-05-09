using System;
using System.IO;
using System.Threading.Tasks;

namespace ProjectRock
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var str = string.Join(",", args);
            Console.WriteLine("Args: " + str);
            
            var arg = args.Length > 0 ? args[0] : null;
            await new App().RunAsync(arg);
        }
    }
}
