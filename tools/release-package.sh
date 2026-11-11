#!/usr/bin/env bash
# Publish an LMK package to this release repository (one branch and one tag prefix per package).
#
# Run it from the root of a development repo that has this repo as remote "releases", in Git Bash:
#   git fetch releases
#   git show releases/main:tools/release-package.sh | bash -s -- <package> [--push]
#
# It snapshots the committed Packages/<package> folder onto upm/<package> and tags <package>/<version>.
# With --push, it also pushes both and starts the sign-release workflow.
#
# Each release is one commit whose tree is the committed package folder, with the previous release as its parent.
# The release branches therefore contain only released versions, never development history.
#
# The version is read from Packages/<package>/package.json, and must have a "## [<version>]" entry in its CHANGELOG.md.
# Release remote: $LMK_RELEASE_REMOTE (default: releases).
set -euo pipefail

REMOTE="${LMK_RELEASE_REMOTE:-releases}"
PACKAGE="${1:-}"
PUSH="${2:-}"

SIGN_REPO="DavidHo1801004/unity-lmk-packages"

fail() { echo "error: $*" >&2; exit 1; }

# Start the signing workflow for tag $1. The tag is already pushed by now, so a failure only warns.
dispatch_signing() {
    local TAG="$1" GH
    GH=$(command -v gh || true)
    if [ -z "$GH" ] && [ -x "/c/Program Files/GitHub CLI/gh.exe" ]; then
        GH="/c/Program Files/GitHub CLI/gh.exe"
    fi

    if [ -n "$GH" ] && "$GH" workflow run sign-release.yml --repo "$SIGN_REPO" -f "tag=$TAG"; then
        echo "Started signing of $TAG: https://github.com/$SIGN_REPO/actions/workflows/sign-release.yml"
    else
        echo "warning: couldn't start signing. Run: gh workflow run sign-release.yml --repo $SIGN_REPO -f tag=$TAG" >&2
    fi
}

# Commit the committed tree of folder $1 onto branch $2 with message $3, unless the branch already has that tree.
snapshot() {
    local SOURCE="$1" BRANCH="$2" MESSAGE="$3" TREE PARENT COMMIT
    git diff --quiet HEAD -- "$SOURCE" && git diff --cached --quiet -- "$SOURCE" || fail "$SOURCE has uncommitted changes"

    TREE=$(git rev-parse "HEAD:$SOURCE")
    PARENT=$(git rev-parse -q --verify "refs/heads/$BRANCH" || true)
    if [ -n "$PARENT" ] && [ "$(git rev-parse "$BRANCH^{tree}")" = "$TREE" ]; then
        return 1
    fi

    COMMIT=$(git commit-tree "$TREE" ${PARENT:+-p "$PARENT"} -m "$MESSAGE")
    git update-ref "refs/heads/$BRANCH" "$COMMIT" ${PARENT:-}
}

cd "$(git rev-parse --show-toplevel)"
[ -n "$PACKAGE" ] || fail "usage: release-package.sh <package> [--push]"
[ -z "$PUSH" ] || [ "$PUSH" = "--push" ] || fail "unknown option '$PUSH'"

DIR="Packages/$PACKAGE"
[ -f "$DIR/package.json" ] || fail "no package at $DIR"

VERSION=$(sed -n 's/^  "version": "\([^"]*\)".*/\1/p' "$DIR/package.json" | head -n 1)
[ -n "$VERSION" ] || fail "can't read version from $DIR/package.json"
NAME=$(sed -n 's/^  "name": "\([^"]*\)".*/\1/p' "$DIR/package.json" | head -n 1)
[ "$NAME" = "$PACKAGE" ] || fail "package.json name '$NAME' doesn't match folder '$PACKAGE'"

TAG="$PACKAGE/$VERSION"
BRANCH="upm/$PACKAGE"

# The local release branch must match the remote one, or the snapshot builds on an old version.
if git ls-remote --exit-code --heads "$REMOTE" "$BRANCH" >/dev/null 2>&1; then
    git fetch -q "$REMOTE" "refs/heads/$BRANCH:refs/heads/$BRANCH" \
        || fail "couldn't update the local $BRANCH from $REMOTE"
fi

# Checks: changelog entry, unused tag, released dependencies, no LFS pointers.
grep -q "^## \[$VERSION\]" "$DIR/CHANGELOG.md" || fail "CHANGELOG.md has no '## [$VERSION]' entry"
if git rev-parse -q --verify "refs/tags/$TAG" >/dev/null; then fail "tag $TAG already exists; bump the version"; fi
if git ls-remote --exit-code --tags "$REMOTE" "refs/tags/$TAG" >/dev/null 2>&1; then fail "tag $TAG already exists on $REMOTE; bump the version"; fi

sed -n 's/^    "\(com\.lmk\.[^"]*\)": "\([^"]*\)".*/\1 \2/p' "$DIR/package.json" | while read -r DEP DEP_VERSION; do
    git rev-parse -q --verify "refs/tags/$DEP/$DEP_VERSION" >/dev/null \
        || echo "warning: dependency $DEP $DEP_VERSION has no tag; release it first" >&2
done

if git lfs ls-files --name-only 2>/dev/null | grep -q "^$DIR/"; then
    fail "$DIR has Git LFS files; packages must store binaries in git (see tools/RELEASING.md)"
fi

snapshot "$DIR" "$BRANCH" "$PACKAGE $VERSION" || fail "$BRANCH already contains this package state"
git tag -a "$TAG" "$BRANCH" -m "$PACKAGE $VERSION"
echo "Tagged $TAG on $BRANCH ($(git rev-parse --short "$BRANCH"))"

if [ -n "$PUSH" ]; then
    git push "$REMOTE" "refs/heads/$BRANCH:refs/heads/$BRANCH" "refs/tags/$TAG:refs/tags/$TAG"
    dispatch_signing "$TAG"
else
    echo "Push with: git push $REMOTE $BRANCH $TAG"
    echo "Then sign: gh workflow run sign-release.yml --repo $SIGN_REPO -f tag=$TAG"
fi
