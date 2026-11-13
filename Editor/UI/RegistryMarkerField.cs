using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

using LMK.Runtime.Registry;

using ToolbarButton = LMK.Editor.UIElements.ToolbarButton;

namespace LMK.Editor.Registry
{
    /// <summary>
    /// A row's marker: a colour field, empty (transparent) when the row has none, and a button to remove it. Markers
    /// are per user and staged with the registry edits, so they need no lock, and work on read-only rows too.
    /// </summary>
    public sealed class RegistryMarkerField : VisualElement
    {
        public const string USS_CLASS = "registry-marker";

        private readonly RegistryKind kind;
        private readonly RegistryGuid registry;
        private readonly RegistryGuid row;

        public ColorField Field { get; }
        public ToolbarButton ClearButton { get; }

        public RegistryMarkerField(RegistryKind _Kind, RegistryGuid _Registry, RegistryGuid _Row)
        {
            kind = _Kind;
            registry = _Registry;
            row = _Row;

            AddToClassList(USS_CLASS);

            Field = new ColorField { name = "marker-field", showAlpha = true, tooltip = $"Your colour for this {_Kind.RowNoun}, shown on its row. Only you see it." };
            Field.AddToClassList(USS_CLASS + "__field");
            Add(Field);

            ClearButton = new ToolbarButton(false) { name = "marker-clear-button", text = "Clear", tooltip = "Remove the marker." };
            ClearButton.AddToClassList(USS_CLASS + "__clear");
            Add(ClearButton);

            Field.RegisterValueChangedCallback(e => SetColor(e.newValue));

            ClearButton.clicked += ClearMarker;

            Refresh();
        }

        /// <summary>
        /// Stage a picked colour, as the colour field does. A new marker starts transparent, so a picked colour is
        /// made opaque: only Clear removes the marker.
        /// </summary>
        public void SetColor(Color _Color)
        {
            if (_Color.a <= 0f)
                _Color.a = 1f;

            kind.SetMarker(registry, row, _Color);
            Refresh();
        }

        /// <summary>
        /// Remove the row's marker, as the Clear button does.
        /// </summary>
        public void ClearMarker()
        {
            kind.SetMarker(registry, row, Color.clear);
            Refresh();
        }

        /// <summary>
        /// Show the row's staged marker.
        /// </summary>
        public void Refresh()
        {
            bool hasMarker = kind.TryGetMarker(registry, row, out var color);
            Field.SetValueWithoutNotify(hasMarker ? color : Color.clear);
            ClearButton.style.display = hasMarker ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
