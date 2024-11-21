using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace Open_Rails_Code_Bot.Git
{
    public class Project
    {
        readonly string GitPath;

        public Project(string gitPath)
        {
            GitPath = gitPath;
        }

        public void Init(string repository)
        {
            if (!Directory.Exists(GitPath))
            {
                Directory.CreateDirectory(GitPath);
                RunCommand($"init");
                RunCommand($"remote add origin {repository}");
                RunCommand($"config remote.origin.fetch +refs/*:refs/*");
            }
        }

        public void Fetch()
        {
            RunCommand("fetch --update-head-ok");
        }

        public void Checkout(string reference)
        {
            RunCommand($"checkout --quiet {reference}");
        }

        public void CheckoutDetached(string reference)
        {
            RunCommand($"checkout --quiet --detach {reference}");
        }

        public void ResetHard()
        {
            RunCommand("reset --hard");
        }

        public void Clean()
        {
            RunCommand("clean --force -d -x");
        }

        public void Merge(string reference)
        {
            RunCommand($"merge --quiet --no-edit --no-ff -Xignore-space-change {reference}");
        }

        public void Push(string reference)
        {
            RunCommand($"push origin {reference}");
        }

        public string ParseRef(string reference)
        {
            foreach (var line in GetCommandOutput($"rev-parse {reference}"))
            {
                return line;
            }
            throw new ApplicationException("Unable to find ref");
        }

        public string GetAbbreviatedCommit(string reference)
        {
            foreach (var line in GetCommandOutput($"log --format=%h -1 {reference}"))
            {
                return line;
            }
            throw new ApplicationException("Unable to find ref");
        }

        public string Describe(string options)
        {
            foreach (var line in GetCommandOutput($"describe {options}"))
            {
                return line;
            }
            throw new ApplicationException("Unable to describe commit");
        }

        public string CommitTree(string authorName, string authorEmail, string treeRef, IEnumerable<string> parentRefs, string message)
        {
            var tempFile = Path.GetTempFileName();
            File.WriteAllText(tempFile, message);
            try
            {
                Environment.SetEnvironmentVariable("GIT_AUTHOR_NAME", authorName);
                Environment.SetEnvironmentVariable("GIT_AUTHOR_EMAIL", authorEmail);
                Environment.SetEnvironmentVariable("GIT_COMMITTER_NAME", authorName);
                Environment.SetEnvironmentVariable("GIT_COMMITTER_EMAIL", authorEmail);
                var parents = String.Join(" ", parentRefs.Select(parentRef => $"-p {parentRef}"));
                foreach (var line in GetCommandOutput($"commit-tree {treeRef} {parents} -F {tempFile}"))
                {
                    return line;
                }
                throw new ApplicationException("Unable to commit tree");
            }
            finally
            {
                File.Delete(tempFile);
            }
        }

        public DateTimeOffset GetCommitDate(string reference)
        {
            foreach (var line in GetCommandOutput($"cat-file -p {reference}"))
            {
                if (line.StartsWith("author "))
                {
                    return DateTimeOffset.FromUnixTimeSeconds(long.Parse(line.Split("> ")[1].Split(" ")[0]));
                }
            }
            throw new ApplicationException("Unable to get commit date");
        }

        public void SetBranchRef(string branch, string reference)
        {
            RunCommand($"branch -f {branch} {reference}");
        }

        void RunCommand(string arguments)
        {
            foreach (var line in GetCommandOutput(arguments, true))
            {
            }
        }

        IEnumerable<string> GetCommandOutput(string arguments, bool printOutput = false)
        {
            arguments = $"--no-pager {arguments}";
            if (printOutput)
                Console.WriteLine($"  > git {arguments}");
            var lines = new List<string>();
            var git = new Process();
            git.StartInfo.WorkingDirectory = GitPath;
            git.StartInfo.FileName = "git";
            git.StartInfo.Arguments = arguments;
            git.StartInfo.UseShellExecute = false;
            git.StartInfo.RedirectStandardOutput = true;
            git.StartInfo.RedirectStandardError = true;
            git.StartInfo.StandardOutputEncoding = Encoding.UTF8;
            git.StartInfo.StandardErrorEncoding = Encoding.UTF8;
            git.OutputDataReceived += (sender, e) => lines.Add($"  < {e.Data}");
            git.ErrorDataReceived += (sender, e) => lines.Add($"  ! {e.Data}");
            git.Start();
            git.BeginOutputReadLine();
            git.BeginErrorReadLine();
            git.WaitForExit();
            foreach (var line in lines)
            {
                if (printOutput && line.Length > 4)
                    Console.WriteLine(line);
                yield return line[4..];
            }
            if (git.ExitCode != 0)
            {
                throw new ApplicationException($"git {arguments} failed: {git.ExitCode}");
            }
        }
    }
}
