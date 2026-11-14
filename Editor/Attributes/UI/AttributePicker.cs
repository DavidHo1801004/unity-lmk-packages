using System;
using System.Collections.Generic;
using System.Linq;

using UnityEditor.IMGUI.Controls;
using UnityEngine;

using LMK.Editor.Registry;
using LMK.Runtime.AbilitySystem;
using LMK.Runtime.Registry;

namespace LMK.Editor.AbilitySystem
{
    /// <summary>
    /// A searchable dropdown of every live attribute: one submenu per registry when there are several, and one per
    /// group segment, e.g. Main > Vitals > Health.
    /// </summary>
    internal sealed class AttributePicker : AdvancedDropdown
    {
        private const string NONE = "None";

        /// <summary>
        /// Decides which attributes are offered: (registry, attribute ID, row).
        /// </summary>
        public delegate bool Filter(RegistryGuid _Registry, RegistryGuid _Id, in AttributeRegistryRow _Row);

        private sealed class Item : AdvancedDropdownItem
        {
            public readonly GameplayAttribute Attribute;

            public Item(string _Name, GameplayAttribute _Attribute) : base(_Name)
            {
                Attribute = _Attribute;
            }
        }

        private readonly GameplayAttribute current;
        private readonly bool allowNone;
        private readonly Filter filter;
        private readonly Action<GameplayAttribute> onPicked;



        private AttributePicker(GameplayAttribute _Current, bool _AllowNone, Filter _Filter, Action<GameplayAttribute> _OnPicked)
            : base(new AdvancedDropdownState())
        {
            current = _Current;
            allowNone = _AllowNone;
            filter = _Filter;
            onPicked = _OnPicked;
            minimumSize = new Vector2(240f, 320f);
        }

        /// <summary>
        /// Open the picker below <paramref name="_Anchor"/>, in GUI coordinates.
        /// </summary>
        /// <param name="_AllowNone"> Offer "None" first, to clear the field. </param>
        /// <param name="_Filter"> Which attributes to offer, or <see langword="null"/> for all. </param>
        public static void Show(Rect _Anchor, GameplayAttribute _Current, bool _AllowNone, Action<GameplayAttribute> _OnPicked, Filter _Filter = null)
            => new AttributePicker(_Current, _AllowNone, _Filter, _OnPicked).Show(_Anchor);

        protected override AdvancedDropdownItem BuildRoot()
        {
            var root = new AdvancedDropdownItem("Gameplay Attribute");
            if (allowNone)
                root.AddChild(new Item(NONE, GameplayAttribute.None));

            var registries = AttributeKind.Instance.Snapshot.Registries
                .OrderBy(e => e.Origin != RegistryOrigin.Main)
                .ThenBy(e => e.Name, StringComparer.Ordinal)
                .ToList();

            bool nested = registries.Count > 1;
            foreach (var registry in registries)
            {
                var parent = nested ? new AdvancedDropdownItem(registry.Name) : root;
                var groups = new Dictionary<string, AdvancedDropdownItem>(StringComparer.Ordinal);
                int added = 0;

                foreach (var id in registry.Ids.OrderBy(e => registry.GetName(e), StringComparer.Ordinal))
                {
                    if (!registry.TryGetRow(id, out var row)) continue;
                    if (filter != null && !filter(registry.Id, id, row)) continue;

                    string name = registry.GetName(id) ?? row.name;
                    var attribute = GameplayAttribute.FromId(registry.Id, id, name);
                    var item = new Item(AttributeName.GetShortName(name), attribute) { enabled = true };
                    if (attribute == current)
                        item.name += "  ✓";

                    GroupItem(parent, groups, AttributeName.GetGroup(name)).AddChild(item);
                    added++;
                }

                if (nested && added > 0)
                    root.AddChild(parent);
            }

            if (!root.children.Any())
                root.AddChild(new AdvancedDropdownItem("No attribute to pick") { enabled = false });

            return root;
        }

        private static AdvancedDropdownItem GroupItem(AdvancedDropdownItem _Root, Dictionary<string, AdvancedDropdownItem> _Groups, string _Group)
        {
            if (string.IsNullOrEmpty(_Group)) return _Root;
            if (_Groups.TryGetValue(_Group, out var item)) return item;

            var parent = GroupItem(_Root, _Groups, AttributeName.GetGroup(_Group));
            item = new AdvancedDropdownItem(AttributeName.GetShortName(_Group));
            parent.AddChild(item);
            _Groups[_Group] = item;
            return item;
        }

        protected override void ItemSelected(AdvancedDropdownItem _Item)
        {
            if (_Item is Item picked)
                onPicked?.Invoke(picked.Attribute);
        }
    }
}
