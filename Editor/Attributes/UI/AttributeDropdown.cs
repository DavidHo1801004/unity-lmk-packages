using System;
using System.Collections.Generic;
using System.Linq;

using UnityEngine;

using LMK.Editor.Registry;
using LMK.Editor.Windows;
using LMK.Runtime.AbilitySystem;
using LMK.Runtime.Registry;

namespace LMK.Editor.AbilitySystem
{
    /// <summary>
    /// A searchable dropdown of the live attributes (<see cref="SearchableDropdown"/>): one folder per registry when there
    /// are several, and one per group segment, e.g. Main/Vitals/Health.
    /// </summary>
    internal static class AttributeDropdown
    {
        private const string NONE = "<alpha=#4C>None";
        private const string CURRENT_MARK = "  ✓";

        /// <summary>
        /// Decides which attributes are offered: (registry, attribute ID, row).
        /// </summary>
        public delegate bool Filter(RegistryGuid _Registry, RegistryGuid _Id, in AttributeRegistryRow _Row);

        /// <summary>
        /// Open the dropdown below <paramref name="_Anchor"/>, in GUI coordinates.
        /// </summary>
        /// <param name="_AllowNone"> Offer "None" first, to clear the field. </param>
        /// <param name="_Filter"> Which attributes to offer, or <see langword="null"/> for all. </param>
        public static void Show(Rect _Anchor, GameplayAttribute _Current, bool _AllowNone, Action<GameplayAttribute> _OnPicked, Filter _Filter = null)
        {
            var attributes = new List<GameplayAttribute>();
            var items = new List<GUIContent>();
            var registries = new List<string>();

            if (_AllowNone)
            {
                attributes.Add(GameplayAttribute.None);
                items.Add(new GUIContent(NONE, "No attribute."));
                registries.Add(null);
            }

            Collect(_Current, _Filter, attributes, items, registries);

            var dropdown = new SearchableDropdown(items.ToArray(), registries);
            dropdown.OnSelectedItemChanged += index =>
            {
                if (index >= 0 && index < attributes.Count)
                    _OnPicked?.Invoke(attributes[index]);
            };

            UnityEditor.PopupWindow.Show(_Anchor, dropdown);
        }

        /// <summary>
        /// Whether any live attribute passes <paramref name="_Filter"/>.
        /// </summary>
        public static bool Any(Filter _Filter)
        {
            var attributes = new List<GameplayAttribute>();
            Collect(GameplayAttribute.None, _Filter, attributes, new List<GUIContent>(), new List<string>());
            return attributes.Count > 0;
        }

        /// <summary>
        /// A filter that leaves out <paramref name="_Taken"/>, e.g. the attributes a list already has.
        /// </summary>
        public static Filter Excluding(ICollection<GameplayAttribute> _Taken)
            => (RegistryGuid _Registry, RegistryGuid _Id, in AttributeRegistryRow _) => !_Taken.Contains(GameplayAttribute.FromId(_Registry, _Id, null));

        // Item paths: [Registry/]Group/Sub/Name. Items are listed main registry first, then by name.
        private static void Collect(GameplayAttribute _Current, Filter _Filter, List<GameplayAttribute> _Attributes, List<GUIContent> _Items, List<string> _Registries)
        {
            var registries = AttributeKind.Instance.Snapshot.Registries
                .OrderBy(e => e.Origin != RegistryOrigin.Main)
                .ThenBy(e => e.Name, StringComparer.Ordinal)
                .ToList();

            bool nested = registries.Count > 1;
            foreach (var registry in registries)
            {
                foreach (var id in registry.Ids.OrderBy(e => registry.GetName(e), StringComparer.Ordinal))
                {
                    if (!registry.TryGetRow(id, out var row)) continue;
                    if (_Filter != null && !_Filter(registry.Id, id, row)) continue;

                    string name = registry.GetName(id) ?? row.name;
                    var attribute = GameplayAttribute.FromId(registry.Id, id, name);

                    string path = name.Replace('.', '/');
                    if (nested) path = $"{registry.Name}/{path}";
                    if (attribute == _Current) path += CURRENT_MARK;

                    _Attributes.Add(attribute);
                    _Items.Add(new GUIContent(path, string.IsNullOrEmpty(row.description) ? name : $"{name}\n{row.description}"));
                    _Registries.Add(registry.Name);
                }
            }
        }
    }
}
