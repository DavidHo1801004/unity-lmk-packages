using System;
using System.Collections.Generic;
using System.Text;

namespace LMK.Editor.Utilities
{
    /// <summary>
    /// Text fields a <see cref="SearchQuery"/> term can search.
    /// </summary>
    public enum SearchField
    {
        /// <summary>
        /// Display name or path. Searched by terms without a prefix.
        /// </summary>
        Name,

        /// <summary>
        /// Namespace and assembly name. Searched by terms prefixed with <c>'@'</c>.
        /// </summary>
        Namespace,

        /// <summary>
        /// Tooltip text. Searched by terms prefixed with <c>'#'</c>.
        /// </summary>
        Tooltip,
    }

    /// <summary>
    /// Provides the text of each <see cref="SearchField"/>, already passed through <see cref="SearchQuery.Normalize"/>.
    /// </summary>
    public interface ISearchFieldProvider
    {
        /// <returns> The normalized field text, or <see langword="null"/> if the field is not available. </returns>
        string GetSearchField(SearchField _Field);
    }

    /// <summary>
    /// Parsed search text with JEI-like syntax: <br/>
    /// - <c>a b</c>: AND, every term must match, in any order. <br/>
    /// - <c>a|b</c>: OR between groups of AND terms. <br/>
    /// - <c>-a</c>: NOT, the term must not match. <br/>
    /// - <c>"a b"</c>: a phrase, the space is part of the term. <br/>
    /// - <c>@a</c>: search the namespace/assembly; <c>#a</c>: search the tooltip. Can follow <c>'-'</c>, e.g. <c>-@a</c>. <br/>
    /// A term matches when its field contains it, ignoring case and rich text tags.
    /// </summary>
    public sealed class SearchQuery
    {
        /// <summary>
        /// Syntax summary, e.g. for a search field tooltip.
        /// </summary>
        public const string SYNTAX_HELP =
            "a b: both terms\n" +
            "a|b: either term\n" +
            "-a: exclude term\n" +
            "\"a b\": exact phrase\n" +
            "@a: namespace or assembly\n" +
            "#a: tooltip";

        private readonly struct Term
        {
            public readonly SearchField field;
            public readonly string text;
            public readonly bool isNegated;

            public Term(SearchField _Field, string _Text, bool _IsNegated)
            {
                field = _Field;
                text = _Text;
                isNegated = _IsNegated;
            }
        }

        public static readonly SearchQuery Empty = new(Array.Empty<Term[]>());

        // OR of groups, each group an AND of terms.
        private readonly Term[][] groups;

        /// <summary>
        /// Whether the query has no terms, so it matches everything.
        /// </summary>
        public bool IsEmpty => groups.Length == 0;

        private SearchQuery(Term[][] _Groups)
        {
            groups = _Groups;
        }



        /// <summary>
        /// Lowercase <paramref name="_Text"/> and remove its rich text tags, so it can be compared with query terms.
        /// </summary>
        public static string Normalize(string _Text)
        {
            if (string.IsNullOrEmpty(_Text)) return string.Empty;

            return RichTextUtility.StripTags(_Text).ToLowerInvariant();
        }

        /// <summary>
        /// Parse <paramref name="_Query"/>. Empty terms and groups are ignored.
        /// </summary>
        public static SearchQuery Parse(string _Query)
        {
            if (string.IsNullOrWhiteSpace(_Query)) return Empty;

            var groups = new List<Term[]>();
            var terms = new List<Term>();
            var token = new StringBuilder();
            bool inQuotes = false;

            void EndToken()
            {
                if (token.Length == 0) return;

                if (TryParseTerm(token.ToString(), out var term))
                    terms.Add(term);
                token.Clear();
            }

            void EndGroup()
            {
                EndToken();
                if (terms.Count > 0)
                    groups.Add(terms.ToArray());
                terms.Clear();
            }

            foreach (char c in _Query)
            {
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                    token.Append(c); // kept, so TryParseTerm can tell a quoted '-' or prefix from an operator
                }
                else if (inQuotes)
                {
                    token.Append(c);
                }
                else if (c == '|')
                {
                    EndGroup();
                }
                else if (char.IsWhiteSpace(c))
                {
                    EndToken();
                }
                else
                {
                    token.Append(c);
                }
            }
            EndGroup();

            return groups.Count == 0 ? Empty : new SearchQuery(groups.ToArray());
        }

        // Operators come first, in order: '-', then the field prefix. Quotes only group text.
        private static bool TryParseTerm(string _Token, out Term _Term)
        {
            int start = 0;

            bool isNegated = _Token.Length > 1 && _Token[0] == '-';
            if (isNegated) start++;

            SearchField field = SearchField.Name;
            if (start < _Token.Length && _Token.Length - start > 1)
            {
                if (_Token[start] == '@') { field = SearchField.Namespace; start++; }
                else if (_Token[start] == '#') { field = SearchField.Tooltip; start++; }
            }

            string text = Normalize(_Token[start..].Replace("\"", string.Empty));
            _Term = new Term(field, text, isNegated);
            return text.Length > 0;
        }



        /// <summary>
        /// Whether the fields of <paramref name="_Provider"/> match this query.
        /// </summary>
        public bool Matches(ISearchFieldProvider _Provider)
        {
            if (IsEmpty) return true;
            if (_Provider == null) return false;

            foreach (var group in groups)
            {
                if (GroupMatches(group, _Provider)) return true;
            }
            return false;
        }

        private static bool GroupMatches(Term[] _Group, ISearchFieldProvider _Provider)
        {
            foreach (var term in _Group)
            {
                string fieldText = _Provider.GetSearchField(term.field) ?? string.Empty;
                bool contains = fieldText.IndexOf(term.text, StringComparison.Ordinal) >= 0;
                if (contains == term.isNegated) return false;
            }
            return true;
        }
    }
}
