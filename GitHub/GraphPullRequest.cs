using System;

namespace Open_Rails_Code_Bot.GitHub
{
    class GraphPullRequest
    {
        public Uri Url;
        public int Number;
        public string Title;
        public DateTimeOffset CreatedAt;
        public GraphPullRequestAuthor Author;
        public GraphPullRequestRef HeadRef;
        public bool IsDraft;
        public GraphPullRequestLabels Labels;
    }

    class GraphPullRequestAuthor
    {
        public Uri Url;
        public string Login;
    }

    class GraphPullRequestRef
    {
        public string Prefix;
        public string Name;
    }

    class GraphPullRequestLabels
    {
        public GraphPullRequestLabelNode[] Nodes;
    }

    class GraphPullRequestLabelNode
    {
        public string Name;
    }
}
