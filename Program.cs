using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Open_Rails_Code_Bot.GitHub;

namespace Open_Rails_Code_Bot
{
    class Program
    {
        static void Main(string[] args)
        {
            var config = new CommandLineParser.Arguments.FileArgument('c', "config")
            {
                ForcedDefaultValue = new FileInfo("config.json")
            };

            var commandLineParser = new CommandLineParser.CommandLineParser()
            {
                Arguments = {
                    config,
                }
            };

            try
            {
                commandLineParser.ParseCommandLine(args);

                AsyncMain(new ConfigurationBuilder()
                    .AddJsonFile(config.Value.FullName, true)
                    .Build()).Wait();
            }
            catch (CommandLineParser.Exceptions.CommandLineException e)
            {
                Console.WriteLine(e.Message);
            }
        }

        static async Task AsyncMain(IConfigurationRoot config)
        {
            var gitHubConfig = config.GetSection("github");
            var query = new Query(gitHubConfig["token"]);

            var members = await query.GetTeamMembers(gitHubConfig["organization"], gitHubConfig["team"]);
            Console.WriteLine($"Org '{gitHubConfig["organization"]}' team '{gitHubConfig["team"]}' members ({members.Count})");
            foreach (var member in members)
            {
                Console.WriteLine($"  {member.Login}");
            }
            var memberLogins = members.Select(member => member.Login).ToHashSet();

            var pullRequests = await query.GetOpenPullRequests(gitHubConfig["organization"], gitHubConfig["repository"]);
            var autoMergePullRequests = new List<GraphPullRequest>();
            Console.WriteLine($"Org '{gitHubConfig["organization"]}' repo '{gitHubConfig["repository"]}' open pull requests ({pullRequests.Count})");
            foreach (var pullRequest in pullRequests)
            {
                var autoMerge = memberLogins.Contains(pullRequest.Author.Login)
                    && !pullRequest.Labels.Nodes.Any(label => label.Name == gitHubConfig["excludeLabel"]);
                Console.WriteLine($"  #{pullRequest.Number} {pullRequest.Title}");
                Console.WriteLine($"    By:     {pullRequest.Author.Login}");
                Console.WriteLine($"    Branch: {pullRequest.HeadRef.Name}");
                Console.WriteLine($"    Labels: {String.Join(' ', pullRequest.Labels.Nodes.Select(label => label.Name))}");
                Console.WriteLine($"    Allowed to auto-merge? {autoMerge}");
                if (autoMerge)
                {
                    autoMergePullRequests.Add(pullRequest);
                }
            }

            Console.WriteLine($"Org '{gitHubConfig["organization"]}' repo '{gitHubConfig["repository"]}' auto-merge pull requests ({autoMergePullRequests.Count})");
            foreach (var pullRequest in autoMergePullRequests)
            {
                Console.WriteLine($"  #{pullRequest.Number} {pullRequest.Title}");
            }
        }
    }
}
