using System;
using System.Linq;

namespace Allowed.Publisher.WindowsServices.Sample
{
    class Program
    {
        static void Main(string[] args)
        {
            PublishServiceCommand command = new()
            {
                Profile = "PublishProfiles/Production.json"
            };

            Console.WriteLine(command.Invoke<string>().First());
        }
    }
}
