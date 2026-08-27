# Development

This guide provides information on how to setup development environment on local machine.

It assumes no local tools and empty Windows 10 OS.

## Prerequisites

1. `Windows 10` 1703 or later
2. `Microsoft Visual Studio 2022` or higher, with the following workloads:
    - .NET desktop development
    - `.NET6 SDK` (may included in vs2022)
    - Windows 10 SDK 10.0.17763.0

The build task `Deps` automates entire installation locally (except OS). More details on running tasks are given bellow.


## Build

### Manual

1. Clone repository
2. Open solution in Visual Studio 2022
3. [Restore all NuGet packages](https://docs.microsoft.com/en-us/nuget/consume-packages/package-restore#restore-packages-manually-using-visual-studio)
4. Build

Now you can build solution.

### Command line

Build is automated using [Invoke-Build] PowerShell module which is included in the repository, but can be also [installed in the system](https://github.com/nightroman/Invoke-Build#install-as-module).

1. open `administrative PowerShell`
2. go to repository root
3. run `Set-Alias ib $pwd\Invoke-Build.ps1` (For convenience, set alias to it)
4. run `ib ?` to get list of available tasks (anywhere in the repository directory hierarchy):

```
PS C:\Projects\PRemoteM> ib ?

Name           Jobs Synopsis
----           ---- --------
Deps           {}   Ensure local dependencies
Build          {}   Build the application
BuildInSandbox {}   Build in Windows Sandbox
Clean          {}   Clean generated data

```

Tasks are defined in the [prm.build.ps1] PowerShell script.

For example, to clean any existing builds and then build fresh PRemoteM as portable Win32 application invoke:

```ps1
ib Clean, Build -aReleaseType Release

# Equivalent without setting alias, must be run in root of the repository
./Invoke-Build.ps1 Clean, Build -aReleaseType Release

# Equivalent with system install of Invoke-Build
Invoke-Build Clean, Build -aReleaseType Release
```

Please check out [invoke-build](https://chocolatey.org/packages/invoke-build) package notes on how to enable task auto completion and other tips.

Task `BuildInSandbox` starts [Windows Sandbox] and executes `ib Deps, Build` tasks. This takes some time (~20 minutes) as all dependencies are downloaded from the Internet and installed, using [Chocolatey] package manager, but it guaranties pristine environment. Note that when you close the sandbox entire environment is gone.


## Releases

### Every push to `main` publishes one

`.github/workflows/build-on-dev-push.yml` releases on its own; nothing has to be tagged by hand. A push
to `main` (or `master`) makes the run:

1. call `scripts/Bump-Version.ps1`, which raises `AppVersion.Build` by one and empties
   `AppVersion.PreRelease` — so `scripts/Get-Version.ps1` reports `1.3.0.N` with no `-beta` suffix, and
   `AppVersion.UpdateCheckUrls` points the in-app update check at `/releases/latest`
2. commit that as `chore(release): 1.3.0.N` and push it back to the branch
3. build the net9 and net9-self-contained publish profiles from the bumped source
4. tag the bump commit `v1.3.0.N` and push the tag, only once the build succeeded
5. publish `v1.3.0.N` as a full release — `draft: false`, `prerelease: false` — with both zips attached

The push in step 2 uses `GITHUB_TOKEN`, and a `GITHUB_TOKEN` push does not start another workflow run.
That is what keeps the bump commit from releasing again forever; the `chore(release):` prefix is checked
as well, in case that behaviour ever changes. A workflow-level `concurrency` group keeps two pushes that
land seconds apart from reading the same build number, and `Bump-Version.ps1` skips a number whose tag
already exists.

A build number is therefore burned per push, not per user-visible change, and the version in a working
copy is always one behind whatever `main` last published. Pushing a tag by hand still builds and
publishes that tag, without bumping. `workflow_dispatch` only builds.

### GitHub lists releases by tag name, not by date

The releases page and `GET /repos/:owner/:repo/releases` both order by tag name as a string, in spite of
what the API docs say about reverse chronological order. Once the build number reaches two digits the list
stops looking chronological:

```
v1.3.0.9-beta     <- sorts first, but is not the newest
v1.3.0.8-beta
...
v1.3.0.2-beta
v1.3.0.10-beta    <- the newest build, published hours after v1.3.0.9-beta
v1.3.0.1-beta
```

After the shared `v1.3.0.` prefix the next character decides, so `9` and `2` both beat the `1` that starts
`10`. Nothing on GitHub's side can reorder this: the sort key is the tag name, so the only way to change the
order is to rename the tags. Renaming published tags breaks the download links people already have, so we
live with it and read the list correctly instead — `AboutPageViewModel.CustomCheckMethod` compares every tag
it finds numerically rather than trusting the page order.

Anything else reading that list is subject to the same order and is outside our control. The release badge
in the readme, for one, reports `v1.3.0.9-beta` while `v1.3.0.10-beta` is out, because shields.io takes the
first entry the API returns.

If the order itself ever needs to be right on github.com, it takes a new tag scheme applied going forward,
neither of which is in place today:

- zero-pad the build, `v1.3.0.010-beta`, which sorts correctly as a string up to 999 builds
- move the build into the pre-release part, `v1.3.0-beta.10`, which is what SemVer intends

[Microsoft Visual Studio 2019]: https://visualstudio.microsoft.com/vs
[Windows 10]:       https://www.microsoft.com/en-us/software-download/windows10
[Invoke-Build]:     https://github.com/nightroman/Invoke-Build
[Windows Sandbox]:  https://docs.microsoft.com/en-us/windows/security/threat-protection/windows-sandbox/windows-sandbox-overview
[Chocolatey]:       http://chocolatey.org
[prm.build.ps1]:    https://github.com/VShawn/PRemoteM/blob/dev/prm.build.ps1