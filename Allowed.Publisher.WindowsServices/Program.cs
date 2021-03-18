using Allowed.Publisher.WindowsServices.Publishers;
using System;

namespace Allowed.Publisher.WindowsServices
{
    class Program
    {
        static void Main(string[] args)
        {
            if(args.Length < 2)
            {
                Console.WriteLine("Welcome to Allowed.Publisher.WindowsServices!");
                Console.WriteLine("Enter \"Publish-Service {ProjectName} {Profile}\" to publish your windows service!");
            }
            else
            {
                ServicePublisher publisher = new(args[0], args[1]);
                publisher.Publish().GetAwaiter().GetResult();
            }
        }
    }
}
