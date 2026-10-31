# InlineInstanced

`[InlineInstanced]` replaces the label row of a `[SerializeReference]` field with a type picker. Picking a type creates a new instance of it; "None" clears the field.

```csharp
[SerializeReference, InlineInstanced] private IWeapon weapon;
[SerializeReference, InlineInstanced] private List<Effect> effects = new();
[SerializeReference, InlineInstanced] private Effect[] effectArray;
```

## Which classes are listed

A class is listed when it is **exposed** and **instantiable**.

**Exposed:** the closest `[CanReferenceInline]` or `[CanNotReferenceInline]` in its inheritance chain decides.

```csharp
[Serializable, CanReferenceInline] public abstract class Effect { }  // exposes Effect and all subclasses
[Serializable] public class Burn : Effect { }                        // listed
[Serializable, CanNotReferenceInline] public class DebugEffect : Effect { } // hidden, with its subclasses
```

**Instantiable**, all of:
- not abstract, not an interface, not a struct, not an open generic;
- not a `UnityEngine.Object`;
- marked `[Serializable]`. Unity requires it for `[SerializeReference]` values, and it is **not inherited**, so mark every class.
- has a parameterless constructor (any visibility);
- not defined in an editor-only assembly.

The field type can be the attributed class, any of its base classes or interfaces, an intermediate class, or `object`. If no class can be listed for a field, its row shows a warning.

## Player builds

Every listed class must exist in player builds. Saved data of a class that the player doesn't have can't be skipped, so **the whole component holding it fails to load**.

Editor-only assemblies are filtered out of the list, but a class wrapped in `#if UNITY_EDITOR` inside a runtime assembly can't be seen that way, so builds check for it. The build fails and names each such class, even if no saved data uses it yet. To fix it, do one of the following:

- move the class out of the `#if`;
- move it into an Editor assembly;
- add `[CanNotReferenceInline]`, which also removes it from the picker. Remove any saved data using it first, since the build no longer checks it.

The check covers assemblies compiled from source, not precompiled plugin DLLs.

## Collections

On a `List<T>` or `T[]`, each element gets its own picker, and each selected object in multi-editing gets its own instance when a type is picked.

Unity's **Duplicate Array Element** copies the reference rather than the object, so both elements share one instance and editing one edits the other. The later element shows a warning with a **Make Unique** button, which gives it its own copy, nested references included. The `+` button adds an empty element and isn't affected.

## Missing types

If a class used by saved data is renamed, moved or deleted, the Inspector shows one warning listing the missing classes. Unity doesn't report which field held a missing class, so those fields display **None**.

**Saving can permanently remove the data.** For example, it is lost when a scene that stayed open while the class was deleted is saved. Restore the class before saving. For a rename or move, add `[UnityEngine.Scripting.APIUpdating.MovedFrom]` to the class.

## Notes

- The type list for each field type is built on first use and cached until scripts recompile.
- Changing the type replaces the instance; values of the previous type are not copied. Picking the current type does nothing.
- IMGUI inspectors (custom `OnInspectorGUI`, `EditorGUILayout.PropertyField`) get a simpler fallback with a standard menu. It shows the missing types warning on each empty field, and doesn't detect shared instances.
