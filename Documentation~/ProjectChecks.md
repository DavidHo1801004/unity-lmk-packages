# Project checks

`LMK.Editor.AbilitySystem.AbilitySystemProjectChecks` checks your project's ability system data the way a build would, so a CI job, or a test you run before pushing, catches broken attributes and assets early.

## The checks

| Check | Fails when |
|---|---|
| `CheckRegistryIntegrity()` | A registry of any kind, attribute registries included, in `Assets/` or in a package you author doesn't parse, is inconsistent, or has a checksum that doesn't match its rows. |
| `CheckBuildContent(warnings)` | An attribute or tag reference in an enabled build scene, or in Resources content, doesn't resolve, and its kind's build policy is *Fail* (*Project Settings → LMK → Registries*). Under *Warn*, those references go to `warnings`. |
| `CheckAuthoredPackages()` | An embedded or local package fails pack validation, e.g. a package attribute bounded by an attribute of the project's main registry. |
| `CheckEffectsAndAbilities(warnings)` | An effect or ability asset has an `EffectValidator` or `AbilityValidator` error. Their warnings go to `warnings`. |

The first three are the registry core's `RegistryProjectChecks`. Each returns a list of problems, empty when the check passes. `CheckEffectsAndAbilities` also runs before every player build, which an error stops, and from *Tools → LMK → Ability System → Validate Effects and Abilities*.

## As edit mode tests

Add a test assembly that references `LMK.AbilitySystem.Editor` (see the gameplay tags package's *Project checks* page for a complete `.asmdef`), and a test class:

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using LMK.Editor.AbilitySystem;

public sealed class AbilitySystemChecks
{
    [Test]
    public void Registries_AreIntact() => AssertNone(AbilitySystemProjectChecks.CheckRegistryIntegrity());

    [Test]
    public void BuildContent_ReferencesResolve() => AssertNone(AbilitySystemProjectChecks.CheckBuildContent(LogWarnings()));

    [Test]
    public void EffectsAndAbilities_AreValid() => AssertNone(AbilitySystemProjectChecks.CheckEffectsAndAbilities(LogWarnings()));

    private static List<string> LogWarnings() => new();

    private static void AssertNone(List<string> _Failures)
        => Assert.IsEmpty(_Failures, "\n- " + string.Join("\n- ", _Failures));
}
```

## From the command line

```
Unity -batchmode -quit -projectPath . -executeMethod LMK.Editor.AbilitySystem.AbilitySystemProjectChecks.RunFromCommandLine
```

The editor logs every problem and exits with 1 if any check fails, 0 otherwise.
