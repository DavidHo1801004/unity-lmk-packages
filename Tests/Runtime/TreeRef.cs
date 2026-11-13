using System;
using System.Collections.Generic;

using UnityEngine;

using LMK.Runtime.Registry;

namespace LMK.Editor.Registry.Tests
{
    /// <summary>
    /// A reference to a row of the first tree test kind, with the frozen reference layout.
    /// </summary>
    [Serializable]
    public struct TreeRef : IRegistryReference
    {
        [SerializeField] private RegistryGuid _registry;
        [SerializeField] private RegistryGuid _id;
        [SerializeField] private string _lastKnownName;

        public RegistryGuid RegistryId => _registry;
        public RegistryGuid Id => _id;
        public string LastKnownName => _lastKnownName ?? string.Empty;

        public TreeRef(RegistryGuid _Registry, RegistryGuid _Id, string _Name)
        {
            _registry = _Registry;
            _id = _Id;
            _lastKnownName = _Name;
        }
    }



    /// <summary>
    /// A reference to a row of the second tree test kind: the same layout under another type name.
    /// </summary>
    [Serializable]
    public struct TreeRefB : IRegistryReference
    {
        [SerializeField] private RegistryGuid _registry;
        [SerializeField] private RegistryGuid _id;
        [SerializeField] private string _lastKnownName;

        public RegistryGuid RegistryId => _registry;
        public RegistryGuid Id => _id;
        public string LastKnownName => _lastKnownName ?? string.Empty;

        public TreeRefB(RegistryGuid _Registry, RegistryGuid _Id, string _Name)
        {
            _registry = _Registry;
            _id = _Id;
            _lastKnownName = _Name;
        }
    }



    [Serializable]
    public struct TreeRefEntry
    {
        public TreeRef reference;
    }

    [Serializable]
    public class TreeRefManaged
    {
        public TreeRef reference;
    }
}
