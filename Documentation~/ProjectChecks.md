# Project checks

`LMK.Editor.Registry.RegistryProjectChecks` checks your project's registries the way a build would, for every registry kind (gameplay tags, and any other kind you use), so a CI job, or a test you run before pushing, catches broken tags early. `LMK.Editor.GameplayTags.TagProjectChecks`, from gameplay tags 1.0.0, has the same members and forwards to it.

## The checks

| Check | Fails when |
|---|---|
| `CheckRegistryIntegrity()` | A registry in `Assets/` or in a package you author (embedded or local) doesn't parse, is inconsistent, or has a checksum that doesn't match its rows. |
| `CheckBuildContent(warnings)` | A reference in an enabled build scene, or in Resources content, doesn't resolve, and its kind's build policy is *Fail* (*Project Settings → LMK → Gameplay Tags → Missing Tags In Builds*, or *LMK → Registries* for every kind). Under *Warn*, those references go to `warnings` instead. |
| `CheckAuthoredPackages()` | An embedded or local package fails pack validation (see the README's *Packages* section). Assets in a package's `Tests/` folder are skipped. |

Each returns a list of problems, empty when the check passes. `CheckBuildContent` opens each build scene in turn, then restores the scenes that were open; an untitled scene is replaced by an empty one. When it runs interactively, it first asks to save modified scenes.

## As edit mode tests

Add a test assembly to your project, for example `Assets/Tests/Editor/Project.Tests.Editor.asmdef`:

```json
{
    "name": "Project.Tests.Editor",
    "references": [ "LMK.Registry.Editor", "UnityEngine.TestRunner", "UnityEditor.TestRunner" ],
    "includePlatforms": [ "Editor" ],
    "overrideReferences": true,
    "precompiledReferences": [ "nunit.framework.dll" ],
    "autoReferenced": false,
    "defineConstraints": [ "UNITY_INCLUDE_TESTS" ]
}
```

and a test class next to it:

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using LMK.Editor.Registry;

public sealed class ProjectChecks
{
    [Test]
    public void Registries_AreIntact() => AssertNone(RegistryProjectChecks.CheckRegistryIntegrity());

    [Test]
    public void BuildContent_ReferencesResolve()
    {
        var warnings = new List<string>();
        AssertNone(RegistryProjectChecks.CheckBuildContent(warnings));
        foreach (string warning in warnings)
            Debug.LogWarning(warning);
    }

    [Test]
    public void AuthoredPackages_PassPackValidation() => AssertNone(RegistryProjectChecks.CheckAuthoredPackages());

    private static void AssertNone(List<string> _Failures)
        => Assert.IsEmpty(_Failures, "\n- " + string.Join("\n- ", _Failures));
}
```

The checks then run with the rest of your edit mode tests, in *Window → General → Test Runner* or from the command line:

```
Unity -batchmode -projectPath . -runTests -testPlatform EditMode -testResults results.xml
```

## From the command line

Without a test assembly, run every check at once:

```
Unity -batchmode -projectPath . -executeMethod LMK.Editor.Registry.RegistryProjectChecks.RunFromCommandLine
```

The results go to the log (`-logFile <path>` to choose where). The editor exits with code 0 when every check passed, and 1 otherwise. Don't add `-quit`: the method exits the editor itself.

## Registry ID stability

A registry's ID must never change, since every reference stores it. Unity can't check this, because it means comparing two revisions. In a CI script, compare the `registry` header line of each changed registry with the base revision, for example:

```bash
base="origin/main"
git diff --name-status -M "$base" HEAD -- '*.tagregistry' '*.attrregistry' | while IFS=$'\t' read -r change old new; do
    case "$change" in M*) new="$old" ;; R*) ;; *) continue ;; esac
    before=$(git show "$base:$old" | awk '/^registry[ \t]/ { print $2; exit }')
    after=$(git show "HEAD:$new" | awk '/^registry[ \t]/ { print $2; exit }')
    [ "$before" = "$after" ] || { echo "Registry ID of $new changed from $before to $after"; exit 1; }
done
```

## On a CI service

Running Unity on a hosted CI service, such as GitHub Actions through [GameCI](https://game.ci), needs an activated Unity license there. GameCI's documentation explains how to store one as repository secrets. The checks need nothing beyond a project that opens and compiles.
