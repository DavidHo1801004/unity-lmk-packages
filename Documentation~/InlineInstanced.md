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
- not defined in an editor-only assembly, since it would be missing in player builds.

The field type can be the attributed class, any of its base classes or interfaces, an intermediate class, or `object`.

## Collections

On a `List<T>` or `T[]`, each element gets its own picker. Every element, and every selected object in multi-editing, gets its own instance.

## Missing types

If a class used by saved data is renamed, moved or deleted, the field shows **Missing: Namespace.ClassName** with a warning. The data is kept until another type is picked. For a rename or move, add `[UnityEngine.Scripting.APIUpdating.MovedFrom]` to the class to restore it.

## Notes

- The type list for each field type is built on first use and cached until scripts recompile.
- Changing the type replaces the instance; values of the previous type are not copied.
- IMGUI inspectors (custom `OnInspectorGUI`, `EditorGUILayout.PropertyField`) get a simpler fallback with a standard menu.
