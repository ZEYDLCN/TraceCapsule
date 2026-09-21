# Automatic NuGet publishing

The `Publish NuGet packages` workflow runs when a version tag is pushed. For example,
`v0.1.0-beta.2` builds and publishes all six packages as `0.1.0-beta.2`. Normal branch
pushes continue to run CI without publishing.

The tag overrides `Directory.Build.props` during restore, build, test and pack. You do not
need to edit its version for each release. Previously published versions cannot be replaced.

## One-time account setup

1. In the GitHub repository, open **Settings → Environments → New environment** and create
   an environment named **`nuget`**. If you add required reviewers, publication will wait
   for their approval; leave that rule unset for automatic publication after tests pass.
2. In NuGet.org, open **Trusted Publishing → Create** and use these values:

   | Field | Value |
   | --- | --- |
   | Policy Name | `TraceCapsule-GitHub` |
   | Package Owner | `ZeydAlcan` |
   | Repository Owner | `ZEYDLCN` |
   | Repository | `TraceCapsule` |
   | Workflow File | `nuget-publish.yml` |
   | Environment | `nuget` |
   | Package glob pattern | `TraceCapsule.*` |
   | Scope | Push new packages and package versions |

   Enter only the workflow filename, without `.github/workflows/`. This policy allows the
   matching GitHub workflow to obtain a temporary NuGet credential. No permanent API key
   or repository secret is required. The workflow uses the NuGet profile name `ZeydAlcan`.

See the [official Trusted Publishing instructions](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing).

## Publish a release

First commit and push all intended changes, including the new workflow, scripts, and new
source/test files. A tag points to a commit; it does not include uncommitted files.

```powershell
git push origin master
git tag v0.1.0-beta.2
git push origin v0.1.0-beta.2
```

Choose an unused version for each subsequent release. Tags must use `vMAJOR.MINOR.PATCH`
with an optional prerelease suffix such as `-beta.2`. Build metadata (`+...`) is deliberately
rejected because it cannot distinguish NuGet package versions.

The workflow:

1. Validates the tag and restores/builds the solution with that version.
2. Runs the whole test suite, including compiling and executing generated regression tests.
3. Creates the packages and validates all six package identities, versions, internal
   dependency versions, and the presence of symbol packages.
4. Uploads test results and packages as GitHub Actions artifacts.
5. Starts a separate publishing job in the `nuget` environment, obtains a short-lived
   credential using `NuGet/login`, and publishes packages and symbols to NuGet.org.

Only the publishing job can request an OIDC token. A failed build, test, or package check
prevents publication. Credentials are requested immediately before pushing the packages.

Follow the run under **GitHub → Actions → Publish NuGet packages**. NuGet validation and
indexing happen after the upload. If an upload fails after some packages succeed, use
**Re-run failed jobs** on the same run; `--skip-duplicate` lets already uploaded versions
be skipped. Publish content changes under a new tag/version, rather than moving a tag.

## Local preflight without publishing

```powershell
$releaseVersion = ./.github/scripts/Get-ReleaseVersion.ps1 -Tag v0.1.0-beta.2
dotnet test TraceCapsule.sln -c Release -p:Version=$releaseVersion
dotnet pack TraceCapsule.sln -c Release --no-build -p:Version=$releaseVersion -o ./nupkg/release
./.github/scripts/Test-ReleasePackages.ps1 -Directory ./nupkg/release -Version $releaseVersion
```

Use an output directory containing only this release's packages. Local preflight does not
exercise GitHub OIDC; the one-time NuGet/GitHub account setup must be completed before
pushing the first release tag.
