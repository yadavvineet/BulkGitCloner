using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace BulkGitCloner
{
    internal class Program
    {
        private static string directory = Assembly.GetExecutingAssembly().Location; // directory of the git repository
        private static string userName;
        private static string pat;
        private static GitCloneConfiguration configuration = null;

        private static void Main(string[] args)
        {
            SetupEnv();
            PrintGlobalOptions();
            Console.WriteLine();
        }

        private static void SetupEnv()
        {
            userName = string.Empty;
            pat = string.Empty;
            configuration = null;
            selectedFile = string.Empty;

            SetConfigFolder();
            SelectConfiguration();
            if (configuration != null && configuration.UpdateGitReposOnConnect)
            {
                RefreshConfigFile();
            }
        }

        private static void PrintGlobalOptions()
        {
            while (true)
            {
                if (configuration.IsGitOrgRepo)
                {
                    Console.WriteLine("r : Refresh repositories from org github");
                }

                Console.WriteLine("pull : Pull All");
                Console.WriteLine("latest : Force pull - Discard all changes, resets to master, remove all local files");
                Console.WriteLine("c : Change WorkDir / file");
                Console.WriteLine("x : Exit");

                var input = Console.ReadLine();
                if (input != null)
                {
                    switch (input.ToLower().Trim())
                    {
                        case "r":
                            RefreshConfigFile();
                            break;
                        case "pull":
                            Console.WriteLine(
                                "Local Changes will not be lost. Pull might fail where local changes exist. Continue [Y/N]: ");
                            SoftPull();
                            break;
                        case "latest":
                            var latestConfirm = false;
                            while (latestConfirm == false)
                            {
                                Console.WriteLine("Local Changes WILL BE lost. Continue [Y/N]: ");
                                var inp = Console.ReadLine();
                                if (string.IsNullOrEmpty(inp))
                                {
                                }
                                else if (inp.Equals("y", StringComparison.InvariantCultureIgnoreCase))
                                {
                                    latestConfirm = true;
                                    HardPull();
                                }
                                else if (inp.Equals("n", StringComparison.InvariantCultureIgnoreCase))
                                {
                                    latestConfirm = true;
                                }
                            }
                            break;
                        case "x":
                            Console.WriteLine("Adios.....");
                            Environment.Exit(1);
                            break;
                        case "c":
                            SetupEnv();
                            break;
                        default:
                            Console.WriteLine("Enter valid option");
                            break;
                    }
                }
            }
        }

        private static void SoftPull()
        {
            ProcessWork(configuration.WorkDir, configuration.Repos.Where(b => !b.Ignore).ToList(), false);
        }

        private static void ProcessWork(string configurationWorkDir,
            IList<GitCloneRepoConfiguration> configurationRepos,
            bool forcePull)
        {
            int workerCount = 4;
            var totalRepos = configurationRepos.Count;
            var firstList = configurationRepos.Skip((totalRepos / workerCount) * 0).Take(totalRepos / workerCount).ToList();
            var secondList = configurationRepos.Skip((totalRepos / workerCount) * 1).Take(totalRepos / workerCount).ToList();
            var thirdList = configurationRepos.Skip((totalRepos / workerCount) * 2).Take(totalRepos / workerCount).ToList();
            var fourthList = configurationRepos.Skip((totalRepos / workerCount) * 3).ToList();
            var worker1 = new BackgroundWorker();
            var worker2 = new BackgroundWorker();
            var worker3 = new BackgroundWorker();
            var worker4 = new BackgroundWorker();
            worker1.DoWork += (sender, args) =>
            {
                DoWork(configurationWorkDir, firstList, forcePull);
            };
            worker2.DoWork += (sender, args) =>
            {
                DoWork(configurationWorkDir, secondList, forcePull);
            };
            worker3.DoWork += (sender, args) =>
            {
                DoWork(configurationWorkDir, thirdList, forcePull);
            };
            worker4.DoWork += (sender, args) =>
            {
                DoWork(configurationWorkDir, fourthList, forcePull);
            };
            worker1.RunWorkerAsync();
            worker2.RunWorkerAsync();
            worker3.RunWorkerAsync();
            worker4.RunWorkerAsync();

        }


        private static void HardPull()
        {
            ProcessWork(configuration.WorkDir, configuration.Repos.Where(b => !b.Ignore).ToList(), true);
        }

        private static readonly string orgs = "user/orgs";
        private static string selectedFile;

        private static void RefreshConfigFile()
        {
            if (configuration.IsGitOrgRepo == false)
            {
                return;
            }

            if (configuration.StoreCredentials)
            {
                Console.WriteLine("Enter Password for secure storage");
                var password = ReadMaskedPassword();
                try
                {
                    pat = EncryptionUtils.Decrypt(configuration.PasswordHash, password);
                }
                catch (CryptographicException e)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Invalid Password");
                    Console.ForegroundColor = ConsoleColor.White;
                    return;
                }
                userName = configuration.Username;
                Console.WriteLine();
                Console.WriteLine("Refreshing Configuration file for change in repos");
                var repos = GetServerRepos(configuration, userName, pat, configuration.Repos);

                configuration.Repos = repos;
                File.WriteAllText(selectedFile, JsonConvert.SerializeObject(configuration, Formatting.Indented));
            }
        }

        private static List<GitCloneRepoConfiguration> GetServerRepos(GitCloneConfiguration config,
            string username,
            string password,
            IList<GitCloneRepoConfiguration> configRepos)
        {
            string authInfo = $"{username}:{password}";
            authInfo = Convert.ToBase64String(Encoding.GetEncoding("ISO-8859-1").GetBytes(authInfo));
            var client = new HttpClient
            {
                //BaseAddress = new Uri($"{config.GitOrgUrl}/api/v3/")
            };
            client.DefaultRequestHeaders.Add("Authorization", "Basic " + authInfo);

            var clientData = client.GetAsync($"{config.GitOrgUrl}/api/v3/{orgs}").Result;
            var data = JsonConvert.DeserializeObject<IList<JsonOrgResponse>>(clientData.Content.ReadAsStringAsync()
                .Result);

            var repos = new List<GitCloneRepoConfiguration>();

            foreach (var item in data)
            {
                client = new HttpClient();
                client.DefaultRequestHeaders.Add("Authorization", "Basic " + authInfo);

                var repoData = client.GetAsync(item.repos_url + "?per_page=100").Result;
                var repoDetails = JsonConvert.DeserializeObject<IList<GitHubRepoModel>>(repoData.Content
                    .ReadAsStringAsync()
                    .Result);

                repos.AddRange(repoDetails.Where(b => b.permissions.pull).Select(b =>
                    new GitCloneRepoConfiguration()
                    {
                        DirectoryName = b.name,
                        GitRepoPath = b.clone_url,
                        RepoName = b.name,
                        Ignore = configRepos.FirstOrDefault(x => x.RepoName == b.name)?.Ignore ?? false
                    }));
            }

            return repos;
        }

        private static string ReadMaskedPassword()
        {
            string pass = "";
            do
            {
                ConsoleKeyInfo key = Console.ReadKey(true);
                // Backspace Should Not Work
                if (key.Key != ConsoleKey.Backspace && key.Key != ConsoleKey.Enter)
                {
                    pass += key.KeyChar;
                    Console.Write("*");
                }
                else
                {
                    if (key.Key == ConsoleKey.Backspace && pass.Length > 0)
                    {
                        pass = pass.Substring(0, (pass.Length - 1));
                        Console.Write("\b \b");
                    }
                    else if (key.Key == ConsoleKey.Enter)
                    {
                        break;
                    }
                }
            } while (true);

            return pass;
        }


        private static void SelectConfiguration()
        {
            while (configuration == null)
            {
                var configurations = Directory.GetFiles(directory, "*.gcconfig");
                Console.WriteLine("Following Configurations File(s) Found. Enter index to select a configuration.");
                for (var index = 0; index < configurations.Length; index++)
                {
                    var configuration = configurations[index];
                    var fileData = File.ReadAllText(configuration);
                    var configData = JsonConvert.DeserializeObject<GitCloneConfiguration>(fileData);
                    Console.WriteLine($"{index} : {configData.ConfigurationName} - {configData.Repos.Count} Repos");
                }
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("x: Create new Configuration");
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine("cd: Change Work Directory");

                var input = Console.ReadLine();
                if (string.IsNullOrEmpty(input))
                {
                    Console.WriteLine("Enter valid option");
                }
                else if (input.Equals("x", StringComparison.InvariantCultureIgnoreCase))
                {
                    CreateConfiguration();
                }
                else if (input.Equals("cd", StringComparison.InvariantCultureIgnoreCase))
                {
                    SetConfigFolder();
                }
                else if (int.TryParse(input, out var selection))
                {
                    if (selection > configurations.Length - 1)
                    {
                        Console.WriteLine("Enter valid option");
                    }
                    else
                    {
                        var selConfig = configurations[selection];
                        var fileData = File.ReadAllText(selConfig);
                        var configData = JsonConvert.DeserializeObject<GitCloneConfiguration>(fileData);
                        configuration = configData;
                        selectedFile = selConfig;
                    }
                }
            }
        }


        private static void CreateConfiguration()
        {
            var goodToGo = false;
            while (goodToGo == false)
            {
                Console.Write("Enter Configuration Name: ");
                var configName = Console.ReadLine();
                Console.Write("Enter Work Dir (will be created if doesn't exist): ");
                var workDir = Console.ReadLine();
                Console.Write("Is this GitHub Org (enterprise github) [Y/N]: ");
                var isGitOrgResponse = Console.ReadLine();
                var isGitOrg = !string.IsNullOrEmpty(isGitOrgResponse) &&
                               (isGitOrgResponse == "y" || isGitOrgResponse == "Y");
                string githubHostname = string.Empty;
                bool storeCredentials = false;
                string username = string.Empty;
                string localPat = string.Empty;
                string localPassword = string.Empty;
                bool refreshConfigOnConnect = false;

                if (isGitOrg)
                {
                    Console.Write("Enter Enterprise github hostname with protocol. for example, for https://abcd.github.com/xyz, enter https://abcd.github.com: ");
                    githubHostname = Console.ReadLine();

                    Console.Write("Refresh Config on connect  [Y/N]: ");
                    var refreshConfigOnConnectResponse = Console.ReadLine();
                    refreshConfigOnConnect = !string.IsNullOrEmpty(refreshConfigOnConnectResponse) &&
                                             (refreshConfigOnConnectResponse == "y" || refreshConfigOnConnectResponse == "Y");
                }

                Console.Write("Does operation requires authentication [Y/N]?");
                var requirePassResponse = Console.ReadLine();
                var requireAuth = !string.IsNullOrEmpty(requirePassResponse) &&
                               (requirePassResponse == "y" || requirePassResponse == "Y");



                if (requireAuth)
                {
                    Console.Write("Enter Username: ");
                    username = Console.ReadLine();
                    var isPassOk = false;
                    do
                    {
                        Console.WriteLine();
                        Console.Write("Enter PAT token: ");
                        var localPat1 = ReadMaskedPassword();
                        Console.WriteLine();
                        Console.Write("Re-enter PAT token: ");
                        var localPat2 = ReadMaskedPassword();
                        Console.WriteLine();

                        isPassOk = localPat1 == localPat2;
                        localPat = localPat1;
                    } while (!isPassOk);

                    Console.Write("Do you want to store credentials. They will be stored as encrypted. You would need a password to use configuration [Y/N]: ");
                    var storePasswordResponse = Console.ReadLine();
                    storeCredentials = !string.IsNullOrEmpty(storePasswordResponse) &&
                                       (storePasswordResponse == "y" || storePasswordResponse == "Y");
                    if (storeCredentials)
                    {
                        isPassOk = false;
                        do
                        {
                            Console.WriteLine();
                            Console.Write("Enter configuration password to encrypt credentials: ");
                            var localPat1 = ReadMaskedPassword();
                            Console.WriteLine();
                            Console.Write("Re-enter configuration password to encrypt credentials: ");
                            var localPat2 = ReadMaskedPassword();
                            Console.WriteLine();

                            isPassOk = !string.IsNullOrEmpty(localPat1) && localPat1 == localPat2 && localPat1.Length > 3;
                            localPassword = localPat1;
                        } while (!isPassOk);
                    }
                }
                var repos = new List<GitCloneRepoConfiguration>();

                if (!isGitOrg)
                {
                    Console.WriteLine("Please start entering git urls. Include git path till .git.");
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("Please enter empty string in gitname to end git entry and save configuration");
                    Console.ForegroundColor = ConsoleColor.White;

                    bool continueGitEntry;
                    do
                    {
                        Console.Write("Enter Git URL: ");
                        var gitUrl = Console.ReadLine();

                        Console.Write("Enter repo name: ");
                        var repoName = Console.ReadLine();
                        //abdc/de/de/fefefg.git
                        //012345678901234567890
                        //10 , 17
                        if (string.IsNullOrEmpty(repoName))
                        {
                            var index = gitUrl.LastIndexOf('/');
                            var index2 = gitUrl.IndexOf(".git");
                            repoName = gitUrl.Substring(index, index2 - index - 1);
                        }

                        Console.Write($"Enter folder name [{repoName}]: ");
                        var folderNameResponse = Console.ReadLine();
                        var folderName = string.IsNullOrEmpty(folderNameResponse)
                            ? repoName
                            : folderNameResponse;

                        repos.Add(new GitCloneRepoConfiguration
                        {
                            GitRepoPath = gitUrl,
                            DirectoryName = folderName,
                            RepoName = repoName,
                            Ignore = false
                        });

                        continueGitEntry = !string.IsNullOrEmpty(gitUrl);
                    } while (continueGitEntry);
                }
                else
                {
                    //fetch from git hub

                }

                Console.WriteLine($"Configuration name: {configName}");
                Console.WriteLine($"WorkDir: {workDir}");
                Console.WriteLine($"Is Github Enterprise (org based): {isGitOrg}");
                if (isGitOrg)
                {
                    Console.WriteLine($"Enterprise github hostname: {githubHostname}");
                    Console.WriteLine($"Refresh Config: {refreshConfigOnConnect}");
                }
                Console.WriteLine($"Require Auth: {requireAuth}");
                if (requireAuth)
                {
                    Console.WriteLine($"Store Credentials: {storeCredentials}");
                    Console.WriteLine($"Username: {username}");
                    Console.WriteLine($"PAT: ************");
                    if (storeCredentials)
                    {
                        Console.WriteLine($"Config password: {localPassword.Substring(0, 2)}**********");
                    }
                }

                if (isGitOrg)
                {
                    Console.WriteLine("Repos to be fetched from server on runtime.");
                }
                else
                {
                    Console.WriteLine("Repos");
                    foreach (var repo in repos)
                    {
                        Console.WriteLine($"-- {repo.GitRepoPath} in {repo.DirectoryName}");
                    }
                }

                Console.Write("Save Configuration [Y/N]: ");

                var saveConfigResponse = Console.ReadLine();
                var saveConfig = !string.IsNullOrEmpty(saveConfigResponse) &&
                                   (saveConfigResponse == "y" || saveConfigResponse == "Y");
                if (saveConfig)
                {
                    var fileName = Path.Combine(directory, $"{configName}.gcconfig");
                    var config = new GitCloneConfiguration
                    {
                        ConfigurationName = configName,
                        IsGitOrgRepo = isGitOrg,
                        Repos = repos,
                        StoreCredentials = storeCredentials,
                        Username = username,
                        PasswordHash = requireAuth && storeCredentials
                            ? EncryptionUtils.Encrypt(localPat, localPassword)
                            : null,
                        WorkDir = workDir,
                        GitOrgUrl = githubHostname.TrimEnd('/'),
                        AuthRequired = requireAuth,
                        UpdateGitReposOnConnect = refreshConfigOnConnect
                    };
                    if (config.IsGitOrgRepo)
                    {
                        config.Repos = GetServerRepos(config, config.Username, localPat, config.Repos);
                    }
                    File.WriteAllText(fileName, JsonConvert.SerializeObject(config, Formatting.Indented));
                    goodToGo = true;
                }

            }

        }

        private static void SetConfigFolder()
        {
            Console.WriteLine($"Enter work dir path [{directory}]: ");
            var data = Console.ReadLine();
            if (!string.IsNullOrEmpty(data))
            {
                if (!Directory.Exists(data))
                {
                    Directory.CreateDirectory(data);
                }
                directory = data;
            }

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"Workdir set to folder: {directory}");
            Console.ForegroundColor = ConsoleColor.White;
        }

        private static void SetEnvironment()
        {
            Console.ForegroundColor = ConsoleColor.Red;


            while (string.IsNullOrEmpty(userName))
            {
                Console.WriteLine("Enter Username: ");
                userName = Console.ReadLine();
            }
;
            while (string.IsNullOrEmpty(pat))
            {
                Console.WriteLine("Enter PAT (Personal Access Token)");
                pat = Console.ReadLine();
            }
            Console.ForegroundColor = ConsoleColor.White;

        }

        private static void DoWork(string baseFolder, IList<GitCloneRepoConfiguration> repos, bool hardPull = false)
        {
            int index = 1;
            if (!Directory.Exists(baseFolder))
            {
                Directory.CreateDirectory(baseFolder);
            }
            foreach (var item in repos)
            {
                Console.WriteLine($"Processing {item.RepoName} -- {index++} of {repos.Count}");
                var repo = item.GitRepoPath;
                var folder = item.DirectoryName;
                try
                {
                    if (Directory.Exists(Path.Combine(baseFolder, folder, ".git")))
                    {
                        Console.WriteLine($"Pulling {repo} in folder {folder}");
                        UpdateRepo(repo, baseFolder, folder, hardPull);
                    }
                    else
                    {
                        Console.WriteLine($"Cloning {repo} in folder {folder}");
                        CreateAndClone(repo, baseFolder, folder);
                    }
                }
                catch (Exception)
                {
                    Console.WriteLine($"An Error Occurred while processing: {repo} in folder {folder}");
                }

                Console.WriteLine();
            }

            Console.WriteLine("Operation Complete");
        }

        private static void CreateAndClone(string repo, string baseFolder, string folder)
        {
            using (var powershell = PowerShell.Create())
            {
                // this changes from the user folder that PowerShell starts up with to your git repository
                powershell.AddScript($"cd {Path.Combine(baseFolder)}");
                powershell.AddScript($@"git clone {repo} {folder}");
                Collection<PSObject> results = powershell.Invoke();
            }
        }

        private static void UpdateRepo(string repo, string baseFolder, string folder, bool hardPull = false)
        {
            using (var powershell = PowerShell.Create())
            {
                // this changes from the user folder that PowerShell starts up with to your git repository
                powershell.AddScript($"cd  {Path.Combine(baseFolder, folder)}");
                if (hardPull)
                {
                    powershell.AddScript($@"git reset --hard");
                    powershell.AddScript($@"git clean -f");
                    powershell.AddScript($@"git clean -fd");
                    powershell.AddScript(@"git checkout master");
                    powershell.AddScript(@"git fetch --all --prune");
                }
                powershell.AddScript(@"git pull");
                Collection<PSObject> results = powershell.Invoke();
            }
        }
    }
}


/*
 *
 *  private static void DoWork()
        {

            var configFile = "config.txt";
            var file = Path.Combine(directory, configFile);
            if (!File.Exists(file))
            {
                Console.WriteLine("No Config File.");
                return;
            }

            var lines = File.ReadAllLines(file);
            if (!lines.Any() || lines.Any(b => b.Split(',').Count() != 2))
            {
                Console.WriteLine("Invalid Params");
                return;
            }

            //var folder = "test";
            lines = lines.Where(b => !b.StartsWith("#")).ToArray();
            var current = 1;
            var total = lines.Count();
            foreach (var item in lines)
            {
                Console.WriteLine($"Processing {current++} of {total}");
                var repo = item.Split(',')[0].Trim();
                var folder = item.Split(',')[1].Trim();
                if (!repo.EndsWith(".git"))
                {
                    repo = repo + ".git";
                }

                try
                {
                    if (Directory.Exists(Path.Combine(directory, folder, ".git")))
                    {
                        Console.WriteLine($"Pulling {repo} in folder {folder}");
                        UpdateRepo(repo, directory, folder);
                    }
                    else
                    {
                        Console.WriteLine($"Cloning {repo} in folder {folder}");
                        CreateAndClone(repo, directory, folder);
                    }
                }
                catch (Exception)
                {
                    Console.WriteLine($"An Error Occurred while processing: {repo} in folder {folder}");
                }

                Console.WriteLine();
            }

            Console.WriteLine("All Done... Adios.");
            Console.ReadLine();
        }
 * 
 */
