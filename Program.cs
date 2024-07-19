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

            Console.WriteLine($"Open Rails Code Bot started at {DateTimeOffset.Now:u}");
            Console.WriteLine($"GitHub organisation: {gitHubConfig["organization"]}");
            Console.WriteLine($"GitHub team:         {gitHubConfig["team"]}");
            Console.WriteLine($"GitHub repository:   {gitHubConfig["repository"]}");
            Console.WriteLine($"GitHub base branch:  {gitHubConfig["baseBranch"]}");
            Console.WriteLine($"GitHub merge branch: {gitHubConfig["mergeBranch"]}");

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
                var isMember = memberLogins.Contains(pullRequest.Author?.Login);
                var isIncluded = pullRequest.Labels.Nodes.Any(label => label.Name == gitHubConfig["includeLabel"]);
                var isExcluded = pullRequest.Labels.Nodes.Any(label => label.Name == gitHubConfig["excludeLabel"]);
                var autoMerge = (isMember && !isExcluded) || isIncluded;
                Console.WriteLine($"  #{pullRequest.Number} {pullRequest.Title}");
                Console.WriteLine($"    By:     {pullRequest.Author?.Login}");
                Console.WriteLine($"    Branch: {pullRequest.HeadRef?.Name}");
                Console.WriteLine($"    Draft:  {pullRequest.IsDraft}");
                Console.WriteLine($"    Labels: {String.Join(' ', pullRequest.Labels.Nodes.Select(label => label.Name))}");
                Console.WriteLine($"    Allowed to auto-merge? {autoMerge}");
                if (autoMerge)
                {
                    autoMergePullRequests.Add(pullRequest);
                }
            }

            // Sort pull requests by draft status (non-draft first), inclusion label (present first), and number
            autoMergePullRequests = autoMergePullRequests.OrderBy(pullRequest =>
            {
                var isIncluded = pullRequest.Labels.Nodes.Any(label => label.Name == gitHubConfig["includeLabel"]);
                return $"{(pullRequest.IsDraft ? "2" : "1")}{(isIncluded ? "1" : "2")}{pullRequest.Number,10}";
            }).ToList();

            Console.WriteLine($"Pull requests suitable for auto-merging ({autoMergePullRequests.Count}):");
            foreach (var pullRequest in autoMergePullRequests)
            {
                Console.WriteLine($"  #{pullRequest.Number} {pullRequest.Title}");
            }

            Console.WriteLine("Preparing repository...");
            var git = new Git.Project(GetGitPath());
            git.Init($"git@github.com:{gitHubConfig["organization"]}/{gitHubConfig["repository"]}.git");
            git.Fetch();
            git.ResetHard();
            git.Clean();
            var baseBranchCommit = git.ParseRef(gitHubConfig["baseBranch"]);
            var mergeBranchCommit = git.ParseRef(gitHubConfig["mergeBranch"]);
            var mergeBranchTree = git.ParseRef($"{mergeBranchCommit}^{{tree}}");
            git.CheckoutDetached(baseBranchCommit);
            var baseBranchVersion = String.Format(gitHubConfig["versionFormat"] ?? "{0}", git.Describe(gitHubConfig["versionDescribeOptions"] ?? ""));
            var mergeBranchParents = new List<string>();
            mergeBranchParents.Add(mergeBranchCommit);
            mergeBranchParents.Add(baseBranchCommit);
            var autoMergePullRequestsSuccess = new List<GraphPullRequest>();
            var autoMergePullRequestsFailure = new List<GraphPullRequest>();
            foreach (var pullRequest in autoMergePullRequests)
            {
                Console.WriteLine($"Merging #{pullRequest.Number} {pullRequest.Title}...");
                var mergeCommit = git.ParseRef($"pull/{pullRequest.Number}/head");
                try
                {
                    git.Merge(mergeCommit);
                    mergeBranchParents.Add(mergeCommit);
                    autoMergePullRequestsSuccess.Add(pullRequest);
                }
                catch (ApplicationException error)
                {
                    autoMergePullRequestsFailure.Add(pullRequest);
                    git.ResetHard();
                    git.Clean();
                    Console.WriteLine($"  Error: {error.Message}");
                }
            }
            var autoMergeCommit = git.ParseRef("HEAD");
            var autoMergeTree = git.ParseRef($"{autoMergeCommit}^{{tree}}");

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

            if (mergeBranchTree == autoMergeTree)
            {
                Console.WriteLine("No changes to push into merge branch");
                git.Checkout(gitHubConfig["mergeBranch"]);
            }
            else
            {
                Console.WriteLine("Creating merge commit...");
                var newMergeBranchMessage = String.Format(gitHubConfig["mergeMessageFormat"],
                    baseBranchVersion,
                    autoMergePullRequestsSuccess.Count,
                    String.Join("", autoMergePullRequestsSuccess.Select(pr => String.Format(
                        gitHubConfig["mergeMessagePRFormat"],
                        pr.Number,
                        pr.Title,
                        git.GetAbbreviatedCommit($"pull/{pr.Number}/head")
                    )))
                );
                var newMergeBranchCommit = git.CommitTree(gitHubConfig["mergeAuthorName"], gitHubConfig["mergeAuthorEmail"], $"{autoMergeCommit}^{{tree}}", mergeBranchParents, newMergeBranchMessage);
                git.SetBranchRef(gitHubConfig["mergeBranch"], newMergeBranchCommit);
                git.Checkout(gitHubConfig["mergeBranch"]);
                var newMergeBranchVersion = String.Format(
                    gitHubConfig["mergeVersionFormat"] ?? gitHubConfig["versionFormat"] ?? "{0}",
                    git.Describe(gitHubConfig["mergeVersionDescribeOptions"] ?? gitHubConfig["versionDescribeOptions"] ?? ""),
                    git.GetCommitDate(newMergeBranchCommit)
                );
                git.Push(gitHubConfig["mergeBranch"]);
                Console.WriteLine("Pushed changes into merge branch:");
                Console.WriteLine($"  Version: {newMergeBranchVersion}");
                Console.WriteLine($"  Message: {newMergeBranchMessage.Split("\n")[0]}");
            }
            Console.WriteLine($"Open Rails Code Bot finished at {DateTimeOffset.Now:u}");
        }

        static string GetGitPath()
        {
            var appFilePath = System.Reflection.Assembly.GetEntryAssembly().Location;
            return Path.Combine(Path.GetDirectoryName(appFilePath), "git");
        }
    }
}
