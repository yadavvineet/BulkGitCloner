using System.Collections.Generic;

namespace BulkGitCloner
{
    internal class GitCloneConfiguration
    {
        public string ConfigurationName { get; set; }
        public string WorkDir { get; set; }
        public bool IsGitOrgRepo { get; set; }
        public string GitOrgUrl { get; set; }
        public bool UpdateGitReposOnConnect { get; set; }
        public bool StoreCredentials { get; set; }
        public string Username { get; set; }
        public string PasswordHash { get; set; }
        public bool AuthRequired { get; set; }

        public IList<GitCloneRepoConfiguration> Repos { get; set; }
    }

    public class GitCloneRepoConfiguration
    {
        public string GitRepoPath { get; set; }
        public string RepoName { get; set; }
        public string DirectoryName { get; set; }
        public bool Ignore { get; set; } = false;
    }
}
