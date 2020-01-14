using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Mastersign.PanoWall.Client
{
    class Program
    {
        const string baseUrl = "https://panowall.net/";

        static void Main(string[] args)
        {
            var pathRegex = new Regex(@"^(?:\w\d+)+_(?:\d+p)$");
            var cwd = Directory.GetCurrentDirectory();
            var targetFolders = Directory.EnumerateDirectories(cwd)
                .Where(p => pathRegex.IsMatch(Path.GetFileName(p)))
                .ToArray();
            foreach (var targetFolder in targetFolders)
            {
                var setup = Path.GetFileName(targetFolder);
                Console.WriteLine(targetFolder);
            }
        }
    }
}
