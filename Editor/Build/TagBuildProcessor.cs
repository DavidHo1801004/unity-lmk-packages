using System.Collections.Generic;
using System.Linq;

using UnityEditor.Build;
using UnityEngine.SceneManagement;

using LMK.Editor.Registry;
using LMK.Runtime.GameplayTags;

namespace LMK.Editor.GameplayTags
{
    /// <summary>
    /// A tag reference in build content that doesn't resolve: a <see cref="MissingReferenceIssue"/> of the tag kind.
    /// </summary>
    internal sealed class MissingTagIssue
    {
        /// <summary>
        /// The issue as the registry core found it.
        /// </summary>
        public MissingReferenceIssue Core { get; }

        public string Location => Core.Location;
        public string PropertyPath => Core.PropertyPath;
        public GameplayTag Tag => TagAssetScanner.ToTag(Core.Reference);
        public TagStatus Status => (TagStatus)Core.Status;

        public MissingTagIssue(MissingReferenceIssue _Core)
        {
            Core = _Core;
        }

        public override string ToString()
            => $"{Location} > {PropertyPath}: \"{Tag.LastKnownName}\" ({Status}, registry {Tag.RegistryId}, id {Tag.Id})";
    }



    /// <summary>
    /// The tag part of the registry core's build steps (<see cref="RegistryBuildProcessor"/>): bake the
    /// <see cref="GameplayTagDatabase"/>, keep it in Preloaded Assets, and report tag references that don't resolve.
    /// The core runs them for every kind in each build.
    /// </summary>
    internal static class TagBuildProcessor
    {
        public const string DATABASE_FOLDER = TagRegistryService.ROOT_FOLDER + "/Generated/Resources";
        public const string DATABASE_PATH = DATABASE_FOLDER + "/" + GameplayTagDatabase.RESOURCE_NAME + ".asset";

        /// <summary>
        /// Write the database of every loaded registry, as saved, to <paramref name="_Path"/>. The asset keeps its GUID, and
        /// is only saved when its content changed.
        /// </summary>
        public static GameplayTagDatabase Bake(string _Path) => (GameplayTagDatabase)RegistryBuildProcessor.Bake(TagKind.Instance, _Path);

        /// <summary>
        /// Find unresolved tag references in assets. Read-only: assets are never changed.
        /// </summary>
        public static List<MissingTagIssue> ValidateAssets(IEnumerable<string> _AssetPaths) => OfTags(RegistryBuildProcessor.ValidateAssets(_AssetPaths));

        /// <summary>
        /// Find unresolved tag references in a scene's components, inactive ones included.
        /// </summary>
        /// <param name="_WriteNone"> Replace each one with None. Only for the copy of a scene going into a build. </param>
        public static List<MissingTagIssue> ProcessScene(Scene _Scene, bool _WriteNone) => OfTags(RegistryBuildProcessor.ProcessScene(_Scene, _WriteNone));

        /// <summary>
        /// Log the issues, or fail the build under <see cref="MissingTagBuildPolicy.Fail"/>.
        /// </summary>
        /// <exception cref="BuildFailedException"> Under <see cref="MissingTagBuildPolicy.Fail"/>, if there is any issue. </exception>
        public static void Report(List<MissingTagIssue> _Issues, string _Context, MissingTagBuildPolicy _Policy)
            => RegistryBuildProcessor.Report(_Issues.Select(e => e.Core).ToList(), _Context, _ => (MissingReferencePolicy)_Policy);

        private static List<MissingTagIssue> OfTags(List<MissingReferenceIssue> _Issues)
            => _Issues.Where(e => e.Reference.Kind == TagKind.Instance).Select(e => new MissingTagIssue(e)).ToList();
    }
}
