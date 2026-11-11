# Releasing LMK packages

Packages are developed in their own development repositories under `Packages/com.lmk.*`, and published to this release repository, [`unity-lmk-packages`](https://github.com/DavidHo1801004/unity-lmk-packages), as git remote `releases`:

- Branch `upm/<package>`: the package at the branch root. Each commit is a snapshot of the committed `Packages/<package>` folder for one release, so the branch contains only released versions and no development history.
- Tag `<package>/<version>`, e.g. `com.lmk.editor/0.1.0`: one per released version.
- Branch `main`: the landing page, the signing workflow, and these release tools.

The tools live only here, on `main`. Development repositories run them from `releases/main` and don't keep copies.

Never push development branches to `releases`. The push rules and the `pre-push` hook (see *One-time setup*) block it.

## One-time setup (per development clone)

From the root of the development repository, in Git Bash:

```
git remote add releases https://github.com/DavidHo1801004/unity-lmk-packages.git
git config --add remote.releases.push "refs/heads/upm/*:refs/heads/upm/*"
git fetch releases
git show releases/main:tools/pre-push > .git/hooks/pre-push
```

The hook replaces Git LFS's own `pre-push` hook and calls `git lfs pre-push` itself, so LFS uploads to the development remote keep working. Run the last two lines again after the hook changes here.

Packages must store binaries in git, not Git LFS: OpenUPM and Git URL installs fetch packages with plain git. If the repository sends images to LFS, add a rule after its LFS rules in `.gitattributes`:

```
Packages/com.lmk.*/**/*.png -filter -diff -merge -text
```

Then re-add the files so they're stored as regular blobs: `git rm --cached <files>` and `git add <files>`. Add a similar rule for any other binary type a package uses.

## Releasing a version

1. On the development branch, update the package:
   - `version` in `Packages/<package>/package.json`, following [semver](https://semver.org/). While on 0.x, breaking changes bump the minor version.
   - A `## [<version>] - <date>` entry in its `CHANGELOG.md`.
   - If it depends on another LMK package that changed, update that dependency's version too, and release the dependency **first**.
   - Version numbers in the README install examples.
2. Commit.
3. Snapshot and tag, from the repository root:
   ```
   git fetch releases
   git show releases/main:tools/release-package.sh | bash -s -- <package>
   ```
   The script updates the local `upm/<package>` branch from `releases` first, then checks that:
   - the package folder has no uncommitted changes;
   - the changelog has an entry for the version;
   - the tag doesn't exist yet, locally or on `releases`;
   - every LMK dependency version has a tag;
   - no package file is stored in Git LFS.
4. Check the result: `git log upm/<package>` and `git show <package>/<version>:package.json`.
5. Publish: `git push releases upm/<package> <package>/<version>`, then start the `sign-release` workflow: `gh workflow run sign-release.yml --repo DavidHo1801004/unity-lmk-packages -f tag=<package>/<version>`, or use *Run workflow* in the repo's Actions tab. It builds the signed tarball and publishes the GitHub Release. A signed tarball can't be replaced. Passing `--push` in step 3 does steps 3 and 5 in one go, without the check in step 4.
6. If the landing page changes (a new package, or a new *Latest* version), edit `README.md` on `main` (see *Landing page*).
7. OpenUPM detects the new tag and builds it, usually within 15–30 minutes. Check the package page (below). If a tag was pushed by mistake, delete it from the remote *before* OpenUPM builds it; published versions can't be replaced.

## Landing page

`main` is edited directly. From a development repository:

```
git fetch releases
git worktree add -B release-main ../release-main releases/main
cd ../release-main
# edit README.md, commit
git push releases release-main:main
cd - && git worktree remove ../release-main
```

## OpenUPM

Official documentation:
- Adding a package: <https://openupm.com/docs/adding-upm-package.html>
- Submission form: <https://openupm.com/packages/add/>
- Using OpenUPM in a project (users): <https://openupm.com/docs/getting-started.html> and <https://openupm.com/docs/getting-started-cli.html>

### Requirements
- Public GitHub repository with an open-source license (MIT, SPDX id `MIT`).
- `name` in reverse domain notation with at least three parts (`com.lmk.editor`), not reserved and not already in the Unity registry.
- At least one version tag. The version parsed from the tag must equal `version` in `package.json`.
- Several packages in one repository: each is submitted separately, with `gitTagPrefix` set to its literal tag prefix.

### Submitting a new package (once per package)
1. Release its first version, so its branch and tag exist on GitHub.
2. Open <https://openupm.com/packages/add/> and sign in with GitHub.
3. Repository: `DavidHo1801004/unity-lmk-packages`. Branch: `upm/<package>`. The form reads `package.json` from that branch.
4. Check the generated fields:
   - **name**: the package name;
   - **license**: `MIT`;
   - **gitTagPrefix**: `<package>/`, with the trailing slash. Without it, other packages' tags would be read as this package's versions.
5. Submit. The form forks OpenUPM's repository and opens a pull request adding `data/packages/<package>.yml`. Keep the generated PR title (`chore(data): new package <package>`) so it can be merged automatically.
6. First-time contributors wait for a moderator to approve the CI run, usually within 24 hours.
7. After merging, check `https://openupm.com/packages/<package>/` → *version history* and *build issues*.

Later versions need no new submission: pushing a new tag is enough.

### Verifying an install
In a new Unity 6000.3 project: `openupm add <package>`, or add the scoped registry to `Packages/manifest.json` as in the README. Check that *Package Manager → In Project* lists the package and its LMK dependencies.

## Adding a new package

1. Create `Packages/com.lmk.<name>/` with `package.json`, `README.md`, `CHANGELOG.md`, `LICENSE.md`, `Documentation~/` and asmdefs, with a `.meta` file for every file and folder.
2. Release it as above.
3. Submit it to OpenUPM with gitTagPrefix `com.lmk.<name>/`.
4. Add it to the table in `README.md` on `main`.
5. If existing classes move into the new package under a different assembly name, add `[MovedFrom(true, sourceAssembly: "<old assembly>")]` to them, or saved `[SerializeReference]` data shows them as missing.
