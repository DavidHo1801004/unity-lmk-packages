using System;
using System.Collections.Generic;
using System.Text;

using UnityEngine;

namespace LMK.Runtime.GameplayTags
{
    /// <summary>
    /// A set of tags a <see cref="GameplayTagQuery"/> can be matched against, e.g. a <see cref="GameplayTagContainer"/>.
    /// </summary>
    public interface IGameplayTagSet
    {
        /// <summary>
        /// Whether the set has <paramref name="_Tag"/>, or a descendant of it.
        /// </summary>
        bool HasTag(GameplayTag _Tag);

        /// <summary>
        /// Whether the set holds exactly <paramref name="_Tag"/>.
        /// </summary>
        bool HasTagExact(GameplayTag _Tag);
    }



    /// <summary>
    /// What one expression of a <see cref="GameplayTagQuery"/> tests.
    /// </summary>
    public enum GameplayTagQueryOperator
    {
        /// <summary> The set has any of the tags (hierarchical). False without tags. </summary>
        AnyTagsMatch,

        /// <summary> The set has every tag (hierarchical). True without tags. </summary>
        AllTagsMatch,

        /// <summary> The set has none of the tags (hierarchical). True without tags. </summary>
        NoTagsMatch,

        /// <summary> The set holds any of the tags exactly. False without tags. </summary>
        AnyTagsExactMatch,

        /// <summary> The set holds every tag exactly. True without tags. </summary>
        AllTagsExactMatch,

        /// <summary> The set holds none of the tags exactly. True without tags. </summary>
        NoTagsExactMatch,

        /// <summary> Any sub-expression matches. False without sub-expressions. </summary>
        AnyExprMatch,

        /// <summary> Every sub-expression matches. True without sub-expressions. </summary>
        AllExprMatch,

        /// <summary> No sub-expression matches. True without sub-expressions. </summary>
        NoExprMatch,
    }



    /// <summary>
    /// One expression of a tag query, to build a <see cref="GameplayTagQuery"/> from code: either a test of tags, or a
    /// combination of sub-expressions.
    /// </summary>
    /// <example>
    /// <code>
    /// var query = GameplayTagQuery.Build(GameplayTagQueryExpression.All(
    ///     GameplayTagQueryExpression.AnyTags(Tags.Main.Status_Burning, Tags.Main.Status_Frozen),
    ///     GameplayTagQueryExpression.NoTags(Tags.Main.Status_Immune)));
    /// </code>
    /// </example>
    public sealed class GameplayTagQueryExpression
    {
        public GameplayTagQueryOperator Operator { get; }

        /// <summary>
        /// The tags a tag operator tests. Empty for the expression operators.
        /// </summary>
        public IReadOnlyList<GameplayTag> Tags { get; }

        /// <summary>
        /// The sub-expressions an expression operator combines. Empty for the tag operators.
        /// </summary>
        public IReadOnlyList<GameplayTagQueryExpression> Expressions { get; }

        /// <summary>
        /// Whether the operator tests tags, rather than combining sub-expressions.
        /// </summary>
        public bool IsTagOperator => IsTagOperation(Operator);

        private GameplayTagQueryExpression(GameplayTagQueryOperator _Operator, IReadOnlyList<GameplayTag> _Tags, IReadOnlyList<GameplayTagQueryExpression> _Expressions)
        {
            Operator = _Operator;
            Tags = _Tags ?? Array.Empty<GameplayTag>();
            Expressions = _Expressions ?? Array.Empty<GameplayTagQueryExpression>();
        }

        public static GameplayTagQueryExpression OfTags(GameplayTagQueryOperator _Operator, params GameplayTag[] _Tags)
        {
            if (!IsTagOperation(_Operator)) throw new ArgumentException($"{_Operator} combines expressions, not tags.", nameof(_Operator));
            return new GameplayTagQueryExpression(_Operator, Copy(_Tags), null);
        }

        public static GameplayTagQueryExpression OfExpressions(GameplayTagQueryOperator _Operator, params GameplayTagQueryExpression[] _Expressions)
        {
            if (IsTagOperation(_Operator)) throw new ArgumentException($"{_Operator} tests tags, not expressions.", nameof(_Operator));

            var expressions = new List<GameplayTagQueryExpression>();
            foreach (var expression in _Expressions ?? Array.Empty<GameplayTagQueryExpression>())
                if (expression != null)
                    expressions.Add(expression);

            return new GameplayTagQueryExpression(_Operator, null, expressions);
        }

        public static GameplayTagQueryExpression AnyTags(params GameplayTag[] _Tags) => OfTags(GameplayTagQueryOperator.AnyTagsMatch, _Tags);
        public static GameplayTagQueryExpression AllTags(params GameplayTag[] _Tags) => OfTags(GameplayTagQueryOperator.AllTagsMatch, _Tags);
        public static GameplayTagQueryExpression NoTags(params GameplayTag[] _Tags) => OfTags(GameplayTagQueryOperator.NoTagsMatch, _Tags);
        public static GameplayTagQueryExpression AnyTagsExact(params GameplayTag[] _Tags) => OfTags(GameplayTagQueryOperator.AnyTagsExactMatch, _Tags);
        public static GameplayTagQueryExpression AllTagsExact(params GameplayTag[] _Tags) => OfTags(GameplayTagQueryOperator.AllTagsExactMatch, _Tags);
        public static GameplayTagQueryExpression NoTagsExact(params GameplayTag[] _Tags) => OfTags(GameplayTagQueryOperator.NoTagsExactMatch, _Tags);

        public static GameplayTagQueryExpression Any(params GameplayTagQueryExpression[] _Expressions) => OfExpressions(GameplayTagQueryOperator.AnyExprMatch, _Expressions);
        public static GameplayTagQueryExpression All(params GameplayTagQueryExpression[] _Expressions) => OfExpressions(GameplayTagQueryOperator.AllExprMatch, _Expressions);
        public static GameplayTagQueryExpression None(params GameplayTagQueryExpression[] _Expressions) => OfExpressions(GameplayTagQueryOperator.NoExprMatch, _Expressions);

        internal static bool IsTagOperation(GameplayTagQueryOperator _Operator) => _Operator <= GameplayTagQueryOperator.NoTagsExactMatch;

        private static GameplayTag[] Copy(GameplayTag[] _Tags)
        {
            var tags = new List<GameplayTag>();
            foreach (var tag in _Tags ?? Array.Empty<GameplayTag>())
                if (!tag.IsNone && !tags.Contains(tag))
                    tags.Add(tag);

            return tags.ToArray();
        }
    }



    /// <summary>
    /// A logical test of a set of tags: "has any of", "has all of", "has none of", exact or hierarchical, combined with
    /// any / all / none of nested expressions. Serialized, so assets can hold one, e.g. an effect's immunity.
    /// </summary>
    /// <remarks>
    /// An empty query matches nothing: check <see cref="IsEmpty"/> where "no query" should mean "no restriction". <br/>
    /// Tags that don't resolve match nothing, as everywhere else: "has none of" an unresolved tag is true. <br/>
    /// The expressions are stored flat, root first, each expression listing its sub-expressions by index, so the
    /// query serializes without depth limits.
    /// </remarks>
    [Serializable]
    public sealed class GameplayTagQuery
    {
        [Serializable]
        internal struct Node
        {
            public GameplayTagQueryOperator op;
            public GameplayTagContainer tags;

            /// <summary>
            /// Indices of the sub-expressions in the node list. Always after this node's own index.
            /// </summary>
            public List<int> children;
        }

        [SerializeField]
        private List<Node> _nodes = new();

        [SerializeField]
        private string _description = string.Empty;

        /// <summary>
        /// Whether the query has no expression. It matches nothing then.
        /// </summary>
        public bool IsEmpty => _nodes == null || _nodes.Count == 0;

        /// <summary>
        /// What the query is for, written by its author. <see cref="ToString"/> describes what it tests.
        /// </summary>
        public string Description
        {
            get => _description ?? string.Empty;
            set => _description = value ?? string.Empty;
        }



        public GameplayTagQuery() { }

        /// <summary>
        /// A query of one expression and its sub-expressions.
        /// </summary>
        public static GameplayTagQuery Build(GameplayTagQueryExpression _Root, string _Description = null)
        {
            var query = new GameplayTagQuery { Description = _Description };
            if (_Root != null)
                query.Add(_Root);

            return query;
        }

        /// <summary>
        /// A query that matches when the set has any tag of <paramref name="_Tags"/> (hierarchical).
        /// </summary>
        public static GameplayTagQuery MatchAnyTags(GameplayTagContainer _Tags) => Build(GameplayTagQueryExpression.AnyTags(ToArray(_Tags)));

        /// <summary>
        /// A query that matches when the set has every tag of <paramref name="_Tags"/> (hierarchical).
        /// </summary>
        public static GameplayTagQuery MatchAllTags(GameplayTagContainer _Tags) => Build(GameplayTagQueryExpression.AllTags(ToArray(_Tags)));

        /// <summary>
        /// A query that matches when the set has no tag of <paramref name="_Tags"/> (hierarchical).
        /// </summary>
        public static GameplayTagQuery MatchNoTags(GameplayTagContainer _Tags) => Build(GameplayTagQueryExpression.NoTags(ToArray(_Tags)));

        private int Add(GameplayTagQueryExpression _Expression)
        {
            int index = _nodes.Count;
            _nodes.Add(new Node
            {
                op = _Expression.Operator,
                tags = new GameplayTagContainer(ToArray(_Expression.Tags)),
                children = new List<int>(),
            });

            foreach (var expression in _Expression.Expressions)
                _nodes[index].children.Add(Add(expression));

            return index;
        }

        private static GameplayTag[] ToArray(IEnumerable<GameplayTag> _Tags)
        {
            var tags = new List<GameplayTag>();
            if (_Tags != null)
                tags.AddRange(_Tags);

            return tags.ToArray();
        }



        #region Matching
        /// <summary>
        /// Whether <paramref name="_Tags"/> passes the query. An empty query matches nothing.
        /// </summary>
        public bool Matches(IGameplayTagSet _Tags)
        {
            if (IsEmpty || _Tags == null) return false;

            return Evaluate(0, _Tags);
        }

        /// <summary>
        /// Whether <paramref name="_Tags"/> passes the query. An empty query matches nothing.
        /// </summary>
        public bool Matches(GameplayTagContainer _Tags) => Matches((IGameplayTagSet)_Tags);

        private bool Evaluate(int _Index, IGameplayTagSet _Tags)
        {
            var node = _nodes[_Index];

            switch (node.op)
            {
                case GameplayTagQueryOperator.AnyTagsMatch: return AnyTag(node, _Tags, false);
                case GameplayTagQueryOperator.AllTagsMatch: return AllTags(node, _Tags, false);
                case GameplayTagQueryOperator.NoTagsMatch: return !AnyTag(node, _Tags, false);
                case GameplayTagQueryOperator.AnyTagsExactMatch: return AnyTag(node, _Tags, true);
                case GameplayTagQueryOperator.AllTagsExactMatch: return AllTags(node, _Tags, true);
                case GameplayTagQueryOperator.NoTagsExactMatch: return !AnyTag(node, _Tags, true);

                case GameplayTagQueryOperator.AnyExprMatch:
                    foreach (int child in Children(_Index))
                        if (Evaluate(child, _Tags)) return true;
                    return false;

                case GameplayTagQueryOperator.AllExprMatch:
                    foreach (int child in Children(_Index))
                        if (!Evaluate(child, _Tags)) return false;
                    return true;

                case GameplayTagQueryOperator.NoExprMatch:
                    foreach (int child in Children(_Index))
                        if (Evaluate(child, _Tags)) return false;
                    return true;

                default:
                    return false;
            }
        }

        private static bool AnyTag(Node _Node, IGameplayTagSet _Tags, bool _Exact)
        {
            if (_Node.tags == null) return false;

            foreach (var tag in _Node.tags)
                if (_Exact ? _Tags.HasTagExact(tag) : _Tags.HasTag(tag))
                    return true;

            return false;
        }

        private static bool AllTags(Node _Node, IGameplayTagSet _Tags, bool _Exact)
        {
            if (_Node.tags == null) return true;

            foreach (var tag in _Node.tags)
                if (!(_Exact ? _Tags.HasTagExact(tag) : _Tags.HasTag(tag)))
                    return false;

            return true;
        }

        /// <summary>
        /// The valid sub-expressions of a node. Serialized data is not trusted: an index that is out of range, or not
        /// after the node, would allow a cycle, and is skipped.
        /// </summary>
        private IEnumerable<int> Children(int _Index)
        {
            var children = _nodes[_Index].children;
            if (children == null) yield break;

            foreach (int child in children)
                if (child > _Index && child < _nodes.Count)
                    yield return child;
        }
        #endregion



        #region Reading
        /// <summary>
        /// The query as an expression tree, or <see langword="null"/> if it is empty.
        /// </summary>
        public GameplayTagQueryExpression ToExpression() => IsEmpty ? null : ToExpression(0);

        private GameplayTagQueryExpression ToExpression(int _Index)
        {
            var node = _nodes[_Index];
            if (GameplayTagQueryExpression.IsTagOperation(node.op))
                return GameplayTagQueryExpression.OfTags(node.op, ToArray(node.tags));

            var children = new List<GameplayTagQueryExpression>();
            foreach (int child in Children(_Index))
                children.Add(ToExpression(child));

            return GameplayTagQueryExpression.OfExpressions(node.op, children.ToArray());
        }

        /// <summary>
        /// What the query tests, e.g. <c>ALL(ANY(Status.Burning, Status.Frozen), NONE(Status.Immune))</c>.
        /// </summary>
        public override string ToString()
        {
            if (IsEmpty) return "(empty)";

            var builder = new StringBuilder();
            Describe(0, builder);
            return builder.ToString();
        }

        private void Describe(int _Index, StringBuilder _Builder)
        {
            var node = _nodes[_Index];
            _Builder.Append(node.op switch
            {
                GameplayTagQueryOperator.AnyTagsMatch => "ANY",
                GameplayTagQueryOperator.AllTagsMatch => "ALL",
                GameplayTagQueryOperator.NoTagsMatch => "NONE",
                GameplayTagQueryOperator.AnyTagsExactMatch => "ANY EXACT",
                GameplayTagQueryOperator.AllTagsExactMatch => "ALL EXACT",
                GameplayTagQueryOperator.NoTagsExactMatch => "NONE EXACT",
                GameplayTagQueryOperator.AnyExprMatch => "ANY",
                GameplayTagQueryOperator.AllExprMatch => "ALL",
                GameplayTagQueryOperator.NoExprMatch => "NONE",
                _ => node.op.ToString(),
            }).Append('(');

            bool first = true;
            if (GameplayTagQueryExpression.IsTagOperation(node.op))
            {
                if (node.tags != null)
                    foreach (var tag in node.tags)
                    {
                        if (!first) _Builder.Append(", ");
                        _Builder.Append(tag.IsLive ? tag.Path : tag.ToString());
                        first = false;
                    }
            }
            else
            {
                foreach (int child in Children(_Index))
                {
                    if (!first) _Builder.Append(", ");
                    Describe(child, _Builder);
                    first = false;
                }
            }

            _Builder.Append(')');
        }
        #endregion
    }
}
