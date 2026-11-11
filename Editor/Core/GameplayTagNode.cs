using System.Collections.Generic;
using System.Linq;

using LMK.Runtime.GameplayTags;

namespace LMK.Editor.GameplayTags
{
    /// <summary>
    /// A node of the editor tag tree: the root, a registry header, or a tag.
    /// </summary>
    /// <remarks>
    /// The root's children are one header node per registry. Each header's children are that registry's top
    /// level tags. Trees are built from a <see cref="TagRegistrySnapshot"/> and never modified.
    /// </remarks>
    public sealed class GameplayTagNode
    {
        private readonly List<GameplayTagNode> children = new();

        internal bool IsRoot => Parent == null;

        /// <summary>
        /// Whether this node stands for a whole registry rather than a tag.
        /// </summary>
        internal bool IsRegistryHeader { get; private set; }

        /// <summary>
        /// The parent node, or <see langword="null"/> for the root.
        /// </summary>
        internal GameplayTagNode Parent { get; private set; }

        internal TagGuid RegistryId { get; private set; }

        /// <summary>
        /// Tag ID. Zero for the root and registry headers.
        /// </summary>
        internal TagGuid Id { get; private set; }

        internal string RegistryName { get; private set; } = string.Empty;

        internal TagRegistryKind RegistryKind { get; private set; }

        /// <summary>
        /// Whether the registry this node belongs to can be edited.
        /// </summary>
        internal bool IsReadOnly { get; private set; } = true;

        /// <summary>
        /// Last segment of the tag, or the registry name for a header.
        /// </summary>
        internal string ShortTag { get; private set; } = string.Empty;

        /// <summary>
        /// Full path of the tag. Empty for the root and registry headers.
        /// </summary>
        internal string FullTag { get; private set; } = string.Empty;

        /// <summary>
        /// Tag description. Empty if none.
        /// </summary>
        internal string Comment { get; private set; } = string.Empty;

        /// <summary>
        /// Whether the tag was added explicitly. Implicit tags exist only as parents of added tags.
        /// </summary>
        internal bool IsExplicit { get; private set; }

        /// <summary>
        /// Unique key of the node across registries, stable while the tag exists.
        /// </summary>
        internal string Key => IsRegistryHeader ? $"registry:{RegistryId}" : $"{RegistryId}:{Id}";

        /// <summary>
        /// Child nodes, sorted by name.
        /// </summary>
        internal IReadOnlyList<GameplayTagNode> Children => children;

        internal int ChildCount => children.Count;

        /// <summary>
        /// A reference to this tag, as an assignment writes it. <see cref="GameplayTag.None"/> for headers.
        /// </summary>
        internal GameplayTag Tag => IsRegistryHeader || IsRoot ? GameplayTag.None : GameplayTag.FromId(RegistryId, Id, FullTag);



        private GameplayTagNode() { }

        /// <summary>
        /// Build the whole tree of a snapshot.
        /// </summary>
        internal static GameplayTagNode CreateTree(TagRegistrySnapshot _Snapshot)
        {
            var root = new GameplayTagNode();
            if (_Snapshot == null) return root;

            foreach (var registry in _Snapshot.Registries)
            {
                var header = new GameplayTagNode
                {
                    Parent = root,
                    IsRegistryHeader = true,
                    RegistryId = registry.Id,
                    RegistryName = registry.Name,
                    RegistryKind = registry.Kind,
                    IsReadOnly = registry.IsReadOnly,
                    ShortTag = registry.Name,
                    IsExplicit = true,
                };

                root.children.Add(header);
                AddChildren(header, registry, TagGuid.Zero);
            }

            return root;
        }

        private static void AddChildren(GameplayTagNode _Node, TagRegistrySnapshot.Registry _Registry, TagGuid _ParentId)
        {
            foreach (var id in _Registry.GetChildren(_ParentId))
            {
                if (!_Registry.TryGetRow(id, out var row)) continue;

                var child = new GameplayTagNode
                {
                    Parent = _Node,
                    RegistryId = _Registry.Id,
                    Id = id,
                    RegistryName = _Registry.Name,
                    RegistryKind = _Registry.Kind,
                    IsReadOnly = _Registry.IsReadOnly,
                    ShortTag = row.segment,
                    FullTag = _Registry.GetPath(id) ?? row.segment,
                    Comment = row.description ?? string.Empty,
                    IsExplicit = !row.isImplicit,
                };

                _Node.children.Add(child);
                AddChildren(child, _Registry, id);
            }
        }



        /// <summary>
        /// Find a tag node by registry and tag ID in the subtree of this node.
        /// </summary>
        internal GameplayTagNode Find(TagGuid _RegistryId, TagGuid _Id)
        {
            foreach (var node in GetDescendants())
                if (!node.IsRegistryHeader && node.RegistryId.Equals(_RegistryId) && node.Id.Equals(_Id))
                    return node;

            return null;
        }

        /// <summary>
        /// Enumerate all descendant nodes of this node, parents before children.
        /// </summary>
        internal IEnumerable<GameplayTagNode> GetDescendants()
            => children.SelectMany(e => e.GetDescendants().Prepend(e));
    }
}
