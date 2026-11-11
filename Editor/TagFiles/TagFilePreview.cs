using System;
using System.Collections.Generic;

using LMK.Runtime.GameplayTags;

namespace LMK.Editor.GameplayTags
{
    /// <summary>
    /// What importing a tag into the main registry would do with it.
    /// </summary>
    internal enum TagFileTagStatus
    {
        /// <summary> Not in the main registry: it would be added. </summary>
        New,

        /// <summary> Already in the main registry. Nothing changes. </summary>
        Exists,

        /// <summary> An implicit tag of the main registry: it would become explicit and keep its ID. </summary>
        MakesExplicit,

        /// <summary> Exists with another description, replaced only with "Overwrite descriptions". </summary>
        DescriptionDiffers,

        /// <summary> There is no main registry to compare with. </summary>
        NoMainRegistry,
    }



    /// <summary>
    /// A tag file as a tree, with each tag's status against the main registry. Built from the file each time it's
    /// shown, never stored.
    /// </summary>
    internal sealed class TagFilePreview
    {
        public sealed class Node
        {
            public string Segment;
            public string Path;

            /// <summary> Whether the file lists this path. Otherwise it's only the parent of listed paths. </summary>
            public bool IsListed;

            /// <summary> The file's description, or <see langword="null"/> if its line has none. </summary>
            public string Description;

            /// <summary> The main registry's description, if the tag exists there. </summary>
            public string RegistryDescription;

            public TagFileTagStatus Status;

            public readonly List<Node> Children = new();
        }



        public readonly List<Node> Roots = new();

        public IReadOnlyList<string> Errors { get; private set; } = Array.Empty<string>();
        public IReadOnlyList<string> Warnings { get; private set; } = Array.Empty<string>();

        public bool HasMainRegistry { get; private set; }

        /// <summary> Paths the file lists. </summary>
        public int ListedCount { get; private set; }

        public int NewCount { get; private set; }
        public int NewParentCount { get; private set; }
        public int MakesExplicitCount { get; private set; }
        public int DescriptionDiffersCount { get; private set; }

        public bool CanImport => HasMainRegistry && Errors.Count == 0 && ListedCount > 0;

        public string Summary
        {
            get
            {
                var parts = new List<string> { $"{ListedCount} tag(s)" };
                if (!HasMainRegistry) return parts[0];

                if (NewCount > 0) parts.Add($"{NewCount} new");
                if (NewParentCount > 0) parts.Add($"{NewParentCount} new parent(s)");
                if (MakesExplicitCount > 0) parts.Add($"{MakesExplicitCount} made explicit");
                if (DescriptionDiffersCount > 0) parts.Add($"{DescriptionDiffersCount} description(s) differ");
                if (parts.Count == 1 && ListedCount > 0) parts.Add("all in the main registry");

                return string.Join(" · ", parts);
            }
        }



        /// <summary>
        /// Build the tree of a parsed tag file. Invalid lines are left out and listed in <see cref="Errors"/>.
        /// </summary>
        /// <param name="_Main"> The main registry, or <see langword="null"/> if there is none. </param>
        public static TagFilePreview Build(TagFileParseResult _Parsed, TagRegistrySnapshot.Registry _Main)
        {
            var preview = new TagFilePreview
            {
                Errors = _Parsed.Errors,
                Warnings = _Parsed.Warnings,
                HasMainRegistry = _Main != null,
                ListedCount = _Parsed.Entries.Count,
            };

            var nodes = new Dictionary<string, Node>(StringComparer.Ordinal);

            Node GetOrCreate(string _Path)
            {
                if (nodes.TryGetValue(_Path, out var node)) return node;

                node = new Node { Path = _Path, Segment = TagPath.GetSegment(_Path) };
                nodes.Add(_Path, node);

                string parent = TagPath.GetParent(_Path);
                (parent.Length == 0 ? preview.Roots : GetOrCreate(parent).Children).Add(node);
                return node;
            }

            foreach (var entry in _Parsed.Entries)
            {
                var node = GetOrCreate(entry.Path);
                node.IsListed = true;
                node.Description = entry.Description;
            }

            foreach (var node in nodes.Values)
            {
                node.Status = StatusOf(node, _Main);

                switch (node.Status)
                {
                    case TagFileTagStatus.New when node.IsListed: preview.NewCount++; break;
                    case TagFileTagStatus.New: preview.NewParentCount++; break;
                    case TagFileTagStatus.MakesExplicit: preview.MakesExplicitCount++; break;
                    case TagFileTagStatus.DescriptionDiffers: preview.DescriptionDiffersCount++; break;
                }
            }

            Sort(preview.Roots);
            return preview;
        }

        /// <summary>
        /// The same rules as <see cref="TagFileImport.ApplyTo"/>.
        /// </summary>
        private static TagFileTagStatus StatusOf(Node _Node, TagRegistrySnapshot.Registry _Main)
        {
            if (_Main == null) return TagFileTagStatus.NoMainRegistry;
            if (!_Main.TryFind(_Node.Path, out var id) || !_Main.TryGetRow(id, out var row)) return TagFileTagStatus.New;

            _Node.RegistryDescription = row.description;

            // Parents the file doesn't list stay as they are.
            if (!_Node.IsListed) return TagFileTagStatus.Exists;
            if (row.isImplicit) return TagFileTagStatus.MakesExplicit;

            return _Node.Description != null && _Node.Description != row.description
                ? TagFileTagStatus.DescriptionDiffers
                : TagFileTagStatus.Exists;
        }

        private static void Sort(List<Node> _Nodes)
        {
            _Nodes.Sort((a, b) => string.CompareOrdinal(a.Segment, b.Segment));
            foreach (var node in _Nodes)
                Sort(node.Children);
        }
    }
}
