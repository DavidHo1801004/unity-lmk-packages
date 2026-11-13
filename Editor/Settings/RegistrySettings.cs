using System;
using System.Collections.Generic;

using UnityEditor;
using UnityEngine;

using LMK.Runtime.Registry;

namespace LMK.Editor.Registry
{
    /// <summary>
    /// What a build does when it finds references that don't resolve.
    /// </summary>
    public enum MissingReferencePolicy
    {
        /// <summary>
        /// Log a warning per reference. Built scenes get None in their place.
        /// </summary>
        Warn,

        /// <summary>
        /// Fail the build with the list of references.
        /// </summary>
        Fail,
    }



    /// <summary>
    /// Project settings shared by every registry kind, in <c>ProjectSettings/LMKRegistrySettings.asset</c>.
    /// Committed with the project.
    /// </summary>
    [FilePath("ProjectSettings/LMKRegistrySettings.asset", FilePathAttribute.Location.ProjectFolder)]
    public sealed class RegistrySettings : ScriptableSingleton<RegistrySettings>
    {
        #region Lock
        // Full name of the RegistryLockProvider type, or empty for NoneLockProvider.
        [SerializeField]
        private string lockProvider = string.Empty;

        /// <summary>
        /// The lock provider types projects can choose: concrete <see cref="RegistryLockProvider"/> classes with a public
        /// parameterless constructor, <see cref="NoneLockProvider"/> first.
        /// </summary>
        public static IReadOnlyList<Type> LockProviderTypes
        {
            get
            {
                var types = new List<Type> { typeof(NoneLockProvider) };
                foreach (var type in TypeCache.GetTypesDerivedFrom<RegistryLockProvider>())
                    if (!type.IsAbstract && !type.IsGenericTypeDefinition && type != typeof(NoneLockProvider) && type.GetConstructor(Type.EmptyTypes) != null)
                        types.Add(type);

                types.Sort(1, types.Count - 1, Comparer<Type>.Create((a, b) => string.CompareOrdinal(a.FullName, b.FullName)));
                return types;
            }
        }

        /// <summary>
        /// The chosen lock provider type. An unknown type (a removed class) reads as <see cref="NoneLockProvider"/>.
        /// </summary>
        public Type LockProviderType
        {
            get
            {
                if (string.IsNullOrEmpty(lockProvider)) return typeof(NoneLockProvider);

                foreach (var type in LockProviderTypes)
                    if (type.FullName == lockProvider)
                        return type;

                return typeof(NoneLockProvider);
            }
        }

        /// <summary>
        /// Choose the lock provider, and use it from now on.
        /// </summary>
        /// <param name="_Save"> Save the settings. Only tests turn it off. </param>
        public void SetLockProvider(Type _Type, bool _Save = true)
        {
            lockProvider = _Type == null || _Type == typeof(NoneLockProvider) ? string.Empty : _Type.FullName;
            ApplyLockProvider();

            if (_Save)
                Save(true);
        }

        /// <summary>
        /// Make <see cref="RegistryLockProvider.Current"/> an instance of the chosen provider.
        /// </summary>
        public void ApplyLockProvider()
        {
            var type = LockProviderType;
            if (RegistryLockProvider.Current.GetType() == type) return;

            try
            {
                RegistryLockProvider.Current = (RegistryLockProvider)Activator.CreateInstance(type);
            }
            catch (Exception exception)
            {
                Debug.LogError($"The registry lock provider {type.FullName} could not be created ({exception.Message}). No lock is used.");
                RegistryLockProvider.Current = null;
            }
        }

        // When the editor loads, not when the settings load: a provider installed since (by a test) stays.
        [InitializeOnLoadMethod]
        private static void ApplyLockProviderOnLoad() => instance.ApplyLockProvider();
        #endregion

        #region Builds
        [Serializable]
        private struct KindPolicy
        {
            public string kind;
            public MissingReferencePolicy policy;
        }

        [SerializeField]
        private MissingReferencePolicy missingReferencesInBuilds = MissingReferencePolicy.Warn;

        [SerializeField]
        private List<KindPolicy> kindPolicies = new();

        /// <summary>
        /// What a build does with references that don't resolve, for every kind without its own policy.
        /// </summary>
        public MissingReferencePolicy MissingReferencesInBuilds
        {
            get => missingReferencesInBuilds;
            set
            {
                if (missingReferencesInBuilds == value) return;

                missingReferencesInBuilds = value;
                Save(true);
            }
        }

        /// <summary>
        /// The policy of a kind: its own, or <see cref="MissingReferencesInBuilds"/>.
        /// </summary>
        public MissingReferencePolicy GetPolicy(string _Kind)
        {
            foreach (var entry in kindPolicies)
                if (entry.kind == _Kind)
                    return entry.policy;

            return missingReferencesInBuilds;
        }

        /// <summary>
        /// Whether a kind has its own policy, rather than <see cref="MissingReferencesInBuilds"/>.
        /// </summary>
        public bool HasOwnPolicy(string _Kind) => kindPolicies.Exists(e => e.kind == _Kind);

        /// <summary>
        /// Give a kind its own policy, or <see langword="null"/> to follow <see cref="MissingReferencesInBuilds"/>.
        /// </summary>
        public void SetPolicy(string _Kind, MissingReferencePolicy? _Policy, bool _Save = true)
        {
            kindPolicies.RemoveAll(e => e.kind == _Kind);
            if (_Policy != null)
                kindPolicies.Add(new KindPolicy { kind = _Kind, policy = _Policy.Value });

            if (_Save)
                Save(true);
        }
        #endregion

        #region Code Generation
        [Serializable]
        private struct KindCode
        {
            public string kind;
            public string codeNamespace;
            public string assembly;
        }

        // Registries that get no generated class, as "kind:registry ID". Stored here rather than in the registry files,
        // so read-only package registries can be turned off too.
        [SerializeField]
        private List<string> codeGenDisabledRegistries = new();

        [SerializeField]
        private List<KindCode> kindCode = new();

        /// <summary>
        /// Whether code generation writes a class for a registry of a kind. On by default.
        /// </summary>
        public bool IsCodeGenerated(string _Kind, RegistryGuid _RegistryId)
            => !codeGenDisabledRegistries.Contains($"{_Kind}:{_RegistryId}");

        /// <summary>
        /// Turn code generation on or off for a registry of a kind.
        /// </summary>
        /// <param name="_Save"> Save the settings and generate the kind's code. Only tests turn it off. </param>
        public void SetCodeGenerated(RegistryKind _Kind, RegistryGuid _RegistryId, bool _Generated, bool _Save = true)
        {
            if (IsCodeGenerated(_Kind.Id, _RegistryId) == _Generated) return;

            string key = $"{_Kind.Id}:{_RegistryId}";
            if (_Generated) codeGenDisabledRegistries.Remove(key);
            else codeGenDisabledRegistries.Add(key);

            if (_Save) SaveAndGenerate(_Kind);
        }

        /// <summary>
        /// Namespace of a kind's generated root class: the project's choice, or the kind's default. Empty for the
        /// global namespace.
        /// </summary>
        public string GetCodeNamespace(RegistryKind _Kind)
        {
            var entry = kindCode.Find(e => e.kind == _Kind.Id);
            return entry.kind != null && entry.codeNamespace != null ? entry.codeNamespace : _Kind.CodeStyle?.DefaultNamespace ?? string.Empty;
        }

        /// <param name="_Save"> Save the settings and generate the kind's code. Only tests turn it off. </param>
        public void SetCodeNamespace(RegistryKind _Kind, string _Namespace, bool _Save = true)
        {
            _Namespace = _Namespace?.Trim() ?? string.Empty;
            if (_Namespace == GetCodeNamespace(_Kind)) return;

            UpdateKindCode(_Kind.Id, e => { e.codeNamespace = _Namespace; return e; });
            if (_Save) SaveAndGenerate(_Kind);
        }

        /// <summary>
        /// Name of the assembly a kind's generated code is compiled into, or empty for the project's default assembly
        /// (<c>Assembly-CSharp</c>). Code in assembly definitions can only use the constants from their own assembly.
        /// </summary>
        public string GetCodeAssembly(RegistryKind _Kind)
        {
            var entry = kindCode.Find(e => e.kind == _Kind.Id);
            return entry.kind != null ? entry.assembly ?? string.Empty : string.Empty;
        }

        /// <param name="_Save"> Save the settings and generate the kind's code. Only tests turn it off. </param>
        public void SetCodeAssembly(RegistryKind _Kind, string _Assembly, bool _Save = true)
        {
            _Assembly = _Assembly?.Trim() ?? string.Empty;
            if (_Assembly == GetCodeAssembly(_Kind)) return;

            UpdateKindCode(_Kind.Id, e => { e.assembly = _Assembly; return e; });
            if (_Save) SaveAndGenerate(_Kind);
        }

        private void UpdateKindCode(string _Kind, Func<KindCode, KindCode> _Change)
        {
            int index = kindCode.FindIndex(e => e.kind == _Kind);
            var entry = _Change(index >= 0 ? kindCode[index] : new KindCode { kind = _Kind });

            if (index >= 0) kindCode[index] = entry;
            else kindCode.Add(entry);
        }

        private void SaveAndGenerate(RegistryKind _Kind)
        {
            Save(true);

            var result = RegistryCodeGen.Generate(_Kind);
            if (!result.Success)
                Debug.LogWarning($"{_Kind.DisplayName} code was not generated: {result.Error}");
        }
        #endregion



        [Serializable]
        private struct PublishedRegistry
        {
            public string package;
            public string kind;
            public string registry;
        }

        [SerializeField]
        private List<PublishedRegistry> publishedRegistries = new();

        /// <summary>
        /// The registry ID a package's registry of a kind was packed with, so later packs keep it.
        /// </summary>
        public bool TryGetPublishedRegistry(string _Package, string _Kind, out RegistryGuid _RegistryId)
        {
            foreach (var entry in publishedRegistries)
            {
                if (entry.package != _Package || entry.kind != _Kind) continue;

                return RegistryGuid.TryParse(entry.registry, out _RegistryId);
            }

            _RegistryId = RegistryGuid.Zero;
            return false;
        }

        /// <summary>
        /// Record the registry ID a package's registry of a kind was packed with. A zero ID forgets it.
        /// </summary>
        public void SetPublishedRegistry(string _Package, string _Kind, RegistryGuid _RegistryId, bool _Save = true)
        {
            publishedRegistries.RemoveAll(e => e.package == _Package && e.kind == _Kind);
            if (!_RegistryId.IsZero)
                publishedRegistries.Add(new PublishedRegistry { package = _Package, kind = _Kind, registry = _RegistryId.ToString() });

            if (_Save)
                Save(true);
        }



        #region Carry-Over
        // Settings files of older package versions that were read into these settings, by name. Each is read once.
        [SerializeField]
        private List<string> carriedOver = new();

        /// <summary>
        /// Whether the settings named <paramref name="_Source"/>, e.g. a domain package's settings file from before it
        /// used the registry core, were already carried over into these settings.
        /// </summary>
        public bool IsCarriedOver(string _Source) => carriedOver.Contains(_Source);

        /// <summary>
        /// Record that the settings named <paramref name="_Source"/> were carried over, so they are never read again.
        /// </summary>
        public void MarkCarriedOver(string _Source, bool _Save = true)
        {
            if (!carriedOver.Contains(_Source))
                carriedOver.Add(_Source);

            if (_Save)
                Save(true);
        }
        #endregion
    }
}
