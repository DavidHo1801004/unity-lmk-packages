using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

using LMK.Editor.Utilities;
using LMK.Runtime.Attributes;

namespace LMK.Editor.Attributes
{
    /// <summary>
    /// Contains exposed derived types and custom typename cache of a <see cref="UnityEngine.SerializeReference"/> field type.
    /// </summary>
    public readonly struct InlineInstancedTypeCacheData
    {
        public static readonly InlineInstancedTypeCacheData Empty = new(Array.Empty<Type>(), Array.Empty<CustomReferencedPathAttribute>());

        public readonly bool isValid;
        public readonly IReadOnlyList<Type> exposedTypes;
        public readonly IReadOnlyList<CustomReferencedPathAttribute> customPathAttributes;

        /// <summary>
        /// Dropdown items: "None" at index 0, then one item per exposed type. Rich text. <br/>
        /// Shared by every drawer of this field type, so never modify them.
        /// </summary>
        internal readonly GUIContent[] menuItems;

        /// <summary>
        /// <see cref="menuItems"/> texts without rich text tags, for IMGUI.
        /// </summary>
        internal readonly string[] plainMenuTexts;

        /// <summary>
        /// Namespace and assembly name per <see cref="menuItems"/> entry, searched with <c>@</c> terms. <br/>
        /// <see langword="null"/> for "None".
        /// </summary>
        internal readonly string[] menuNamespaces;

        private readonly Dictionary<Type, int> indexLookup;

        public int Count => exposedTypes?.Count ?? 0;

        internal InlineInstancedTypeCacheData(
            IReadOnlyList<Type> _ExposedTypes,
            IReadOnlyList<CustomReferencedPathAttribute> _CustomPathAttributes)
        {
            isValid = _ExposedTypes.Count > 0;
            exposedTypes = _ExposedTypes;
            customPathAttributes = _CustomPathAttributes;

            indexLookup = new Dictionary<Type, int>(_ExposedTypes.Count);
            for (int i = 0; i < _ExposedTypes.Count; i++)
                indexLookup[_ExposedTypes[i]] = i;

            menuItems = InlineInstancedReflectionCache.BuildMenuItems(_ExposedTypes, _CustomPathAttributes);
            plainMenuTexts = new string[menuItems.Length];
            for (int i = 0; i < menuItems.Length; i++)
                plainMenuTexts[i] = RichTextUtility.StripTags(menuItems[i].text);

            menuNamespaces = new string[menuItems.Length];
            for (int i = 0; i < _ExposedTypes.Count; i++)
            {
                Type type = _ExposedTypes[i];
                menuNamespaces[i + 1] = $"{type.Namespace} {type.Assembly.GetName().Name}";
            }
        }

        /// <summary>
        /// Index of <paramref name="_Type"/> in <see cref="exposedTypes"/>, or -1 if not exposed.
        /// </summary>
        public int IndexOf(Type _Type)
        {
            if (_Type == null || indexLookup == null) return -1;

            return indexLookup.TryGetValue(_Type, out int index) ? index : -1;
        }
    }

    /// <summary>
    /// Cache which resolves the types that can be instanced into a field of a given type.
    /// </summary>
    /// <remarks>
    /// Entries are built on first request, so field types that are interfaces, <see cref="object"/>,
    /// or intermediate classes are supported. Per-type checks are shared between field types.
    /// The cache is cleared on every domain reload.
    /// </remarks>
    public static class InlineInstancedReflectionCache
    {
        private const string DIMMED_TEXT_ALPHA = "4C"; // 30% alpha, as hex for <alpha=#..>

        private static readonly Dictionary<Type, InlineInstancedTypeCacheData> dataCache = new();

        // Per-type results, shared by every field type whose derived types include them.
        private static readonly Dictionary<Type, bool> exposedCache = new();
        private static readonly Dictionary<Type, bool> instantiableCache = new();
        private static readonly Dictionary<System.Reflection.Assembly, bool> editorOnlyCache = new();

        private static HashSet<string> editorOnlyAssemblyNames;

        /// <summary>
        /// Get the cached instance reference data of the given field <paramref name="_Type"/>.
        /// </summary>
        /// <returns>
        /// The cached data. Never a default struct: if no type is exposed, returns <see cref="InlineInstancedTypeCacheData.Empty"/>.
        /// </returns>
        public static InlineInstancedTypeCacheData Get(Type _Type)
        {
            if (_Type == null) return InlineInstancedTypeCacheData.Empty;

            if (!dataCache.TryGetValue(_Type, out var data))
            {
                data = BuildTypeData(_Type);
                dataCache[_Type] = data;
            }
            return data;
        }

        /// <summary>
        /// Whether <paramref name="_Type"/> is exposed by <see cref="CanReferenceInlineAttribute"/>. <br/>
        /// The closest <see cref="CanReferenceInlineAttribute"/> or <see cref="CanNotReferenceInlineAttribute"/>
        /// declared in the inheritance chain wins.
        /// </summary>
        public static bool IsExposed(Type _Type)
        {
            if (_Type == null) return false;
            if (exposedCache.TryGetValue(_Type, out bool isExposed)) return isExposed;

            // No attribute declared on this type: inherit the result of the base type, which is cached too.
            if (_Type.IsDefined(typeof(CanNotReferenceInlineAttribute), false)) isExposed = false;
            else if (_Type.IsDefined(typeof(CanReferenceInlineAttribute), false)) isExposed = true;
            else isExposed = IsExposed(_Type.BaseType);

            exposedCache[_Type] = isExposed;
            return isExposed;
        }

        /// <summary>
        /// Whether an instance of <paramref name="_Type"/> can be created and stored in a <see cref="UnityEngine.SerializeReference"/> field.
        /// </summary>
        public static bool IsInstantiable(Type _Type)
        {
            if (_Type == null) return false;
            if (!instantiableCache.TryGetValue(_Type, out bool isInstantiable))
            {
                isInstantiable = CheckInstantiable(_Type);
                instantiableCache[_Type] = isInstantiable;
            }
            return isInstantiable;
        }

        private static bool CheckInstantiable(Type _Type)
        {
            if (_Type.IsAbstract || _Type.IsInterface || _Type.IsValueType) return false;
            if (_Type.ContainsGenericParameters) return false;
            if (typeof(UnityEngine.Object).IsAssignableFrom(_Type)) return false;
            if (!IsSerializable(_Type)) return false;
            if (_Type.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, Type.EmptyTypes, null) == null) return false;
            if (IsEditorOnly(_Type.Assembly)) return false;

            return true;
        }

        // SerializeReference values must be [Serializable] (not inherited). Unity also accepts
        // [MakeSerializable], matched by name so this compiles on versions without it.
        private static bool IsSerializable(Type _Type)
        {
            if ((_Type.Attributes & TypeAttributes.Serializable) != 0) return true;

            foreach (var attribute in _Type.GetCustomAttributes(false))
            {
                if (attribute.GetType().Name == "MakeSerializableAttribute") return true;
            }
            return false;
        }

        private static InlineInstancedTypeCacheData BuildTypeData(Type _FieldType)
        {
            var derivedTypes = TypeCache.GetTypesDerivedFrom(_FieldType);
            var entries = new List<(Type type, CustomReferencedPathAttribute attr, string sortKey)>();

            void TryAdd(Type _Type)
            {
                if (!IsExposed(_Type) || !IsInstantiable(_Type)) return;

                var attr = _Type.GetCustomAttribute<CustomReferencedPathAttribute>();
                entries.Add((_Type, attr, RichTextUtility.StripTags(attr?.CustomName) ?? _Type.Name));
            }

            TryAdd(_FieldType);
            foreach (var type in derivedTypes)
                TryAdd(type);

            // Full name breaks ties, so the order is the same on every reload.
            entries.Sort((a, b) =>
            {
                int result = StringComparer.OrdinalIgnoreCase.Compare(a.sortKey, b.sortKey);
                return result != 0 ? result : string.CompareOrdinal(a.type.FullName, b.type.FullName);
            });

            var exposedTypes = new Type[entries.Count];
            var customPathAttributes = new CustomReferencedPathAttribute[entries.Count];
            for (int i = 0; i < entries.Count; i++)
            {
                exposedTypes[i] = entries[i].type;
                customPathAttributes[i] = entries[i].attr;
            }

            return new InlineInstancedTypeCacheData(exposedTypes, customPathAttributes);
        }

        /// <summary>
        /// Build dropdown items: "None" first, then one item per exposed type, all with unique names.
        /// </summary>
        internal static GUIContent[] BuildMenuItems(
            IReadOnlyList<Type> _ExposedTypes,
            IReadOnlyList<CustomReferencedPathAttribute> _CustomPathAttributes)
        {
            var menuItems = new GUIContent[_ExposedTypes.Count + 1];
            menuItems[0] = new GUIContent($"<alpha=#{DIMMED_TEXT_ALPHA}>None"); // default "clear" option

            // Short type names that appear more than once fall back to full names.
            var keys = new string[_ExposedTypes.Count];
            var nameCounts = new Dictionary<string, int>();
            for (int i = 0; i < _ExposedTypes.Count; i++)
            {
                keys[i] = $"{_CustomPathAttributes[i]?.CustomName}|{_ExposedTypes[i].Name}";
                nameCounts[keys[i]] = nameCounts.TryGetValue(keys[i], out int count) ? count + 1 : 1;
            }

            for (int i = 0; i < _ExposedTypes.Count; i++)
            {
                Type type = _ExposedTypes[i];
                var attr = _CustomPathAttributes[i];

                bool isAmbiguous = nameCounts[keys[i]] > 1;
                string typeName = isAmbiguous ? type.FullName : type.Name;

                GUIContent content = new(typeName);
                if (attr != null)
                {
                    if (!string.IsNullOrWhiteSpace(attr.CustomName))
                    {
                        // A path ending with a branch ("Folder/") uses the type name as its leaf item.
                        content.text = EndsWithBranch(attr.CustomName)
                            ? attr.CustomName.TrimEnd() + typeName
                            : $"{attr.CustomName} <alpha=#{DIMMED_TEXT_ALPHA}>({typeName})";
                    }

                    content.tooltip = attr.ToolTip;
                    content.image = LoadIcon(attr.IconPath, type);
                }
                menuItems[i + 1] = content;
            }

            return menuItems;
        }

        /// <summary>
        /// Load the icon of <see cref="CustomReferencedPathAttribute.IconPath"/>:
        /// a built-in editor icon name, then a <c>Resources</c> path, then an asset path.
        /// </summary>
        private static Texture LoadIcon(string _IconPath, Type _Owner)
        {
            if (string.IsNullOrWhiteSpace(_IconPath)) return null;

            Texture icon = EditorGUIUtility.FindTexture(_IconPath);
            if (!icon) icon = Resources.Load<Texture2D>(_IconPath);
            if (!icon) icon = AssetDatabase.LoadAssetAtPath<Texture2D>(_IconPath);

            if (!icon)
                Debug.LogWarning($"Icon \"{_IconPath}\" of {_Owner.FullName} can't be found as a built-in icon, Resources path or asset path.");

            return icon;
        }

        /// <summary>
        /// Whether <paramref name="_Path"/> has no leaf name after its last path separator, e.g. <c>"Folder/"</c>. <br/>
        /// Rich text tags and whitespace in the leaf are ignored. A '/' right after '&lt;' is a rich text close tag,
        /// not a separator, matching <see cref="LMK.Editor.UIElements.StructuredMenuItem.AddItem"/>.
        /// </summary>
        private static bool EndsWithBranch(string _Path)
        {
            for (int i = _Path.Length - 1; i >= 0; i--)
            {
                if (_Path[i] != '/' || (i > 0 && _Path[i - 1] == '<')) continue;

                return string.IsNullOrWhiteSpace(RichTextUtility.StripTags(_Path[(i + 1)..]));
            }
            return false;
        }

        // Types from editor-only assemblies would become missing types in player builds.
        private static bool IsEditorOnly(System.Reflection.Assembly _Assembly)
        {
            if (editorOnlyCache.TryGetValue(_Assembly, out bool isEditorOnly)) return isEditorOnly;

            if (editorOnlyAssemblyNames == null)
            {
                var playerAssemblies = CompilationPipeline.GetAssemblies(AssembliesType.PlayerWithoutTestAssemblies)
                    .Select(e => e.name);
                editorOnlyAssemblyNames = new HashSet<string>(
                    CompilationPipeline.GetAssemblies(AssembliesType.Editor)
                        .Select(e => e.name)
                        .Except(playerAssemblies));
            }

            isEditorOnly = editorOnlyAssemblyNames.Contains(_Assembly.GetName().Name);
            editorOnlyCache[_Assembly] = isEditorOnly;
            return isEditorOnly;
        }
    }
}
