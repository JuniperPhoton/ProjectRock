using System;
using System.IO;
using System.Threading.Tasks;

namespace ProjectRock
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("Hello World!");
            await new App().Run();
        }
    }
}
