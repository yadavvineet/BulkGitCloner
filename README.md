# BulkGitCloner

GitBulkCloner is a simple tool that can do bulk git pull operations with few keypresses.

If you are a open source enthusiast or devops person managing multiple repos or manage  several repositories in one or multiple projects, sometimes it becomes tedious to manage all the repositories. Especially when the team is large, it could be difficult to get all the repos to latest state.

Bulk Git Cloner (and updater) solves this problems. You can define configuration files which can hold details for multiple repositories.

**PS: Current code works. Refactoring is planned, not immediately. PRs are welcome**

## What's New

- Organization Mode (Enterprise GitHub) - Now you can clone all your enterprise github repositories directly. Just define the hostname of your enterprise github, generate a [PAT](https://help.github.com/en/github/authenticating-to-github/creating-a-personal-access-token-for-the-command-line) token and you are good to go. The application will fetch all your repos directly. Settings can be defined to update base configuration each time file is selected.
- Soft pull - Pull all your repositories at once.
- Hard pull - Reset all local changes, remove obsolete branches and bring your code to latest head on master branch.
- Secured Credentials Storage - Credentials can now be stored to prevent them entering each time. The password is encrypted by a key string using AES256 bit encryption.
- Multi Threaded to parallelize work loads.



## What's in Pipeline

* Generic improvement for multi threading - Runtime management of parallel threads.
* Remember last work directory used.
* Generic Code refactoring.
* Ability to fire all command sequence from switches as command line arguments to assist in scripting needs.

## Known issues

1. Sometimes the Banner messages show in between directly.
2. Banner should not show while pull operation is in progress.

## Usage

The application is very to work. 

1. Start the application and enter path to your work dir where configuration files are stored.
2. If any existing configuration is present, system will read it, otherwise, you can create a new configuration by entering 'x'.
3. Enter configuration name, 
4. System will ask for Repository work dir. This is the base path where all base repositories will be created.
5. System will ask if this is an Enterprise Github account. Press Y or N.
   1. In case of yes, please enter github hostname along with protocol. For instance if your hosted github is https://abcd.xyz.com/something you need to put in https://abcd.xyz.com
   2. System will ask to refresh configuration on connect. you can press y to be updated on each commit.
   3. If the git requires authentication, please enter yes in next step where it asks for Require Auth. For github enterprise, it is required in current release. so **always press y**
   4. Enter Username with which it should connect.
   5. Generate your PAT Token and enter it here.  [More details about PAT token here](https://help.github.com/en/github/authenticating-to-github/creating-a-personal-access-token-for-the-command-line). [Generate your PAT token from here](https://github.com/settings/tokens)
   6. Enter password to encrypt your PAT token. You will just need this password each time to update repos.
   7. Press Y to save configuration
6. In case of no for enterprise github, please continue and you will be prompted to enter git repos.
   1. enter all repos one by one
   2. To exit entering repos, press enter without typing in repo name to quit.
7. Confirm and save
8. Select your configuration from list by using the index. Next time directly, you will land here.
9. Press pull to pull all repos.