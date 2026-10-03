# Github Workflows

## CI

* Should only run in branches for development purposes.
* Existing workflow in `.github\workflows\ci.yml`

## Pull Request

* Check existing tags. If an existing tag matches the version in `./VERSION.txt`, fail the build
* Compile/lint/test all targets, using appropriate runners.
* Build all targets, including installer
* Save all outputs as build artifacts (docker image as OCI) with a 24 hour lifetime.

It should not be possible to merge the pull request if the PR workflow fails.

# Merge to default branch

* Create a github release based on the version in `./VERSION.txt`.
* Release notes to include a summary of commits since the previous release (beginning of time if no previous)
* Build everything in "release" mode. Go service should be built to a static binary where possible.
* Attach release artifacts:
    * NSIS installer as `airframe-setup-windows_amd64.exe
    * Service binary for each platform compressed (ZIP for windows, tarball for others) with name indicating processor architecture.
* Docker image should be pushed to dockerhub. Insert placeholders for dockerhub authentication and report what I need to do to set up the required authentication tokens in github that docker requires.


