using System.Text.RegularExpressions;

namespace LMK.Editor.Utilities
{
    /// <summary>
    /// Helpers for strings that contain rich text tags.
    /// </summary>
    public static class RichTextUtility
    {
        private static readonly Regex tagRegex = new("<[^<>]+>", RegexOptions.Compiled);

        /// <summary>
        /// Remove all rich text tags, e.g. <c>"&lt;color=#fff&gt;A&lt;/color&gt;"</c> becomes <c>"A"</c>.
        /// </summary>
        public static string StripTags(string _Text)
        {
            if (string.IsNullOrEmpty(_Text)) return _Text;

            return tagRegex.Replace(_Text, string.Empty);
        }
    }
}
