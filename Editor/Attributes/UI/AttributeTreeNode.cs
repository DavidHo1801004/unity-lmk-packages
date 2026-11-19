using System;
using System.Collections.Generic;
using System.Linq;

using LMK.Editor.Registry;
using LMK.Runtime.Registry;

namespace LMK.Editor.AbilitySystem
{
    /// <summary>
    /// What an <see cref="AttributeTreeNode"/> shows.
    /// </summary>
    internal enum AttributeTreeNodeKind
    {
        Root,
        RegistryHeader,
        Group,
        Attribute,
    }



    /// <summary>
    /// A node of the Attribute Manager tree: a registry header, a group, or an attribute. <br/>
    /// A group is everything before an attribute name's last segment, e.g. "Stats.Vitals" for "Stats.Vitals.Health",
    /// and is a single level: it holds attributes only.
    /// </summary>
    internal sealed class AttributeTreeNode
    {
        private readonly List<AttributeTreeNode> children = new();

        public AttributeTreeNodeKind Kind { get; }

        public RegistryGuid RegistryId { get; }

        public string RegistryName { get; }

        public RegistryOrigin Origin { get; }

        /// <summary>
        /// Whether the node belongs to a registry that can't be edited here.
        /// </summary>
        public bool IsReadOnly { get; }

        /// <summary>
        /// The attribute ID. Zero for other nodes.
        /// </summary>
        public RegistryGuid Id { get; }

        /// <summary>
        /// The attribute's full name, the group's name, or the registry's name.
        /// </summary>
        public string FullName { get; }

        /// <summary>
        /// The text shown in the tree: the attribute's last segment, the group's name, or the registry's name.
        /// </summary>
        public string ShortName { get; }

        /// <summary>
        /// The attribute's group, or empty.
        /// </summary>
        public string Group { get; }

        /// <summary>
        /// The attribute's row. Default for other nodes.
        /// </summary>
        public AttributeRegistryRow Row { get; }

        public AttributeTreeNode Parent { get; private set; }

        public IReadOnlyList<AttributeTreeNode> Children => children;

        public int ChildCount => children.Count;

        public bool IsRoot => Kind == AttributeTreeNodeKind.Root;
        public bool IsRegistryHeader => Kind == AttributeTreeNodeKind.RegistryHeader;
        public bool IsGroup => Kind == AttributeTreeNodeKind.Group;
        public bool IsAttribute => Kind == AttributeTreeNodeKind.Attribute;

        /// <summary>
        /// Unique key across the tree, kept between rebuilds: "registry:{registry}", "group:{registry}:{group}" or
        /// "{registry}:{attribute}".
        /// </summary>
        public string Key { get; }



        private AttributeTreeNode(AttributeTreeNodeKind _Kind, RegistrySnapshot<AttributeRegistryRow>.Registry _Registry,
                                  string _FullName, string _ShortName, string _Group, RegistryGuid _Id, AttributeRegistryRow _Row)
        {
            Kind = _Kind;
            FullName = _FullName ?? string.Empty;
            ShortName = _ShortName ?? string.Empty;
            Group = _Group ?? string.Empty;
            Id = _Id;
            Row = _Row;

            if (_Registry != null)
            {
                RegistryId = _Registry.Id;
                RegistryName = _Registry.Name;
                Origin = _Registry.Origin;
                IsReadOnly = _Registry.IsReadOnly;
            }

            Key = _Kind switch
            {
                AttributeTreeNodeKind.RegistryHeader => KeyOfRegistry(RegistryId),
                AttributeTreeNodeKind.Group => KeyOfGroup(RegistryId, FullName),
                AttributeTreeNodeKind.Attribute => KeyOf(RegistryId, Id),
                _ => "root",
            };
        }

        public static string KeyOfRegistry(RegistryGuid _Registry) => $"registry:{_Registry}";

        public static string KeyOfGroup(RegistryGuid _Registry, string _Group) => $"group:{_Registry}:{_Group}";

        public static string KeyOf(RegistryGuid _Registry, RegistryGuid _Id) => $"{_Registry}:{_Id}";



        /// <summary>
        /// The tree of every registry in <paramref name="_Snapshot"/>: the main registry first, then the others by
        /// name. Under a registry, groups and ungrouped attributes are sorted together by name.
        /// </summary>
        public static AttributeTreeNode Build(RegistrySnapshot<AttributeRegistryRow> _Snapshot)
        {
            var root = new AttributeTreeNode(AttributeTreeNodeKind.Root, null, string.Empty, string.Empty, string.Empty, RegistryGuid.Zero, default);
            if (_Snapshot == null) return root;

            var registries = _Snapshot.Registries
                .OrderBy(e => e != _Snapshot.Main)
                .ThenBy(e => e.Name, StringComparer.Ordinal);

            foreach (var registry in registries)
            {
                var header = new AttributeTreeNode(AttributeTreeNodeKind.RegistryHeader, registry, registry.Name, registry.Name, string.Empty,
                                                   RegistryGuid.Zero, default);
                root.AddChild(header);

                var groups = new Dictionary<string, AttributeTreeNode>(StringComparer.Ordinal);
                foreach (var id in registry.Ids)
                {
                    if (!registry.TryGetRow(id, out var row)) continue;

                    string name = registry.GetName(id) ?? row.name ?? string.Empty;
                    string group = AttributeName.GetGroup(name);

                    var parent = header;
                    if (!string.IsNullOrEmpty(group))
                    {
                        if (!groups.TryGetValue(group, out parent))
                        {
                            parent = new AttributeTreeNode(AttributeTreeNodeKind.Group, registry, group, group, string.Empty, RegistryGuid.Zero, default);
                            groups.Add(group, parent);
                            header.AddChild(parent);
                        }
                    }

                    parent.AddChild(new AttributeTreeNode(AttributeTreeNodeKind.Attribute, registry, name, AttributeName.GetShortName(name), group, id, row));
                }

                header.SortChildren();
                foreach (var group in groups.Values)
                    group.SortChildren();
            }

            return root;
        }

        private void AddChild(AttributeTreeNode _Child)
        {
            _Child.Parent = this;
            children.Add(_Child);
        }

        private void SortChildren() => children.Sort((a, b) => string.CompareOrdinal(a.ShortName, b.ShortName));
    }
}
