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

            Console.WriteLine($"GitHub organisation: {gitHubConfig["organization"]}");
            Console.WriteLine($"GitHub team:         {gitHubConfig["team"]}");
            Console.WriteLine($"GitHub repository:   {gitHubConfig["repository"]}");
            Console.WriteLine($"GitHub base branch:  {gitHubConfig["baseBranch"]}");
            Console.WriteLine($"GitHub new branch:   {gitHubConfig["branch"]}");

            var members = await query.GetTeamMembers(gitHubConfig["organization"], gitHubConfig["team"]);
            Console.WriteLine($"Team members ({members.Count}):");
            foreach (var member in members)
            {
                Console.WriteLine($"  {member.Login}");
            }
            var memberLogins = members.Select(member => member.Login).ToHashSet();

            var pullRequests = await query.GetOpenPullRequests(gitHubConfig["organization"], gitHubConfig["repository"]);
            var autoMergePullRequests = new List<GraphPullRequest>();
            Console.WriteLine($"Open pull requests ({pullRequests.Count}):");
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

            Console.WriteLine($"Pull requests suitable for auto-merging ({autoMergePullRequests.Count}):");
            foreach (var pullRequest in autoMergePullRequests)
            {
                Console.WriteLine($"  #{pullRequest.Number} {pullRequest.Title}");
            }

            var git = new Git.Project(GetGitPath(), false);
            git.Init($"https://github.com/{gitHubConfig["organization"]}/{gitHubConfig["repository"]}.git");
            git.Fetch();
            git.ResetHard();
            var baseBranchCommit = git.ParseRef(gitHubConfig["baseBranch"]);
            var mergeBranchCommit = git.ParseRef(gitHubConfig["branch"]);
            git.CheckoutDetached(baseBranchCommit);
            var baseBranchVersion = String.Format(gitHubConfig["versionFormat"] ?? "{0}", git.Describe(gitHubConfig["versionDescribeOptions"] ?? ""));
            var mergeBranchParents = new List<string>();
            mergeBranchParents.Add(baseBranchCommit);
            mergeBranchParents.Add(mergeBranchCommit);
            var autoMergePullRequestsSuccess = new List<GraphPullRequest>();
            var autoMergePullRequestsFailure = new List<GraphPullRequest>();
            foreach (var pullRequest in autoMergePullRequests)
            {
                var mergeCommit = git.ParseRef($"pull/{pullRequest.Number}/head");
                try
                {
                    git.Merge(mergeCommit);
                    mergeBranchParents.Add(mergeCommit);
                    autoMergePullRequestsSuccess.Add(pullRequest);
                }
                catch (ApplicationException)
                {
                    autoMergePullRequestsFailure.Add(pullRequest);
                    git.ResetHard();
                }
            }
            var mergedCommit = git.ParseRef("HEAD");
            var newMergeBranchCommit = git.CommitTree($"{mergedCommit}^{{tree}}", mergeBranchParents, "Auto-merge");
            git.SetBranchRef(gitHubConfig["branch"], newMergeBranchCommit);
            Console.WriteLine($"Base branch commit: {baseBranchCommit}");
            Console.WriteLine($"Base branch version: {baseBranchVersion}");
            Console.WriteLine($"Merge branch commit: {newMergeBranchCommit}");

            Console.WriteLine($"Pull requests successfully auto-merged ({autoMergePullRequestsSuccess.Count}):");
            foreach (var pullRequest in autoMergePullRequestsSuccess)
            {
                Console.WriteLine($"  #{pullRequest.Number} {pullRequest.Title}");
            }

            Console.WriteLine($"Pull requests not auto-merged ({autoMergePullRequestsFailure.Count}):");
            foreach (var pullRequest in autoMergePullRequestsFailure)
            {
                Console.WriteLine($"  #{pullRequest.Number} {pullRequest.Title}");
            }
        }

        static string GetGitPath()
        {
            var appFilePath = System.Reflection.Assembly.GetEntryAssembly().Location;
            return Path.Combine(Path.GetDirectoryName(appFilePath), "git");
        }
    }
}
