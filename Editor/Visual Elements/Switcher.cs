using System;

using UnityEngine.UIElements;

namespace LMK.Editor.UIElements
{
    /// <summary>
    /// Enables simple wrapper functionality for switching visibility of child elements.
    /// </summary>
    [UxmlElement("Switcher", libraryPath = "LMK/Layout", visibility = LibraryVisibility.Visible)]
    public partial class Switcher : VisualElement
    {
        public event Action OnAttachedToPanel;

        private bool isValid = false;
        private VisualElement activeElement;

        /// <summary>
        /// If valid, returns the index of the active element. Otherwise, returns -1.
        /// </summary>
        [UxmlAttribute(name = "active-index")]
        public int ActiveElementIndex
        {
            get => isValid ? IndexOf(activeElement) : -1;
            set => SwitchActive(value);
        }



        public Switcher()
        {
            RegisterCallback<AttachToPanelEvent>(OnAttachToPanelEvent);
        }

        public Switcher(params VisualElement[] _Children) : this()
        {
            foreach (var child in _Children)
                Add(child);
        }



        private void OnAttachToPanelEvent(AttachToPanelEvent _Event)
        {
            // If no children are present, this switcher is invalid.
            if (childCount == 0) return;

            isValid = true;

            foreach (var child in Children())
            {
                child.style.display = DisplayStyle.None;
            }

            // Assign the first element as the default active child if no active element
            if (activeElement != null)
                activeElement.style.display = DisplayStyle.Flex;
            else
                SwitchActive(0);

            OnAttachedToPanel?.Invoke();
        }



        /// <inheritdoc cref="VisualElement.Add(VisualElement)"/>
        /// <remarks>
        /// Must be called from a <see cref="Switcher"/> object to properly update active element.
        /// </remarks>
        public new void Add(VisualElement _Element)
        {
            base.Add(_Element);

            isValid = true;

            if (activeElement == null)
            {
                SwitchActive(_Element);
            }
        }

        /// <inheritdoc cref="VisualElement.Remove(VisualElement)"/>
        /// <remarks>
        /// Must be called from a <see cref="Switcher"/> object to properly update active element. <br/>
        /// If the active element is removed
        /// </remarks>
        public new void Remove(VisualElement _Element)
        {
            if (activeElement == _Element)
            {
                base.Remove(_Element);

                if (childCount == 0)
                {
                    activeElement = null;
                    isValid = false;
                    return;
                }

                var firstElement = ElementAt(0);
                SwitchActive(firstElement);
            }
            else
            {
                base.Remove(_Element);
            }
        }

        /// <inheritdoc cref="VisualElement.RemoveAt(int)"/>
        /// <remarks>
        /// Must be called from a <see cref="Switcher"/> object to properly update active element.
        /// </remarks> 
        public new void RemoveAt(int _Index)
        {
            if (ActiveElementIndex == _Index)
            {
                base.RemoveAt(_Index);

                if (childCount == 0)
                {
                    activeElement = null;
                    isValid = false;
                    return;
                }

                var firstElement = ElementAt(0);
                SwitchActive(firstElement);
            }
            else
            {
                base.RemoveAt(_Index);
            }
        }

        /// <inheritdoc cref="VisualElement.Clear()"/>
        /// <remarks>
        /// Must be called from a <see cref="Switcher"/> object to properly update active element.
        /// </remarks> 
        public new void Clear()
        {
            base.Clear();

            isValid = false;
            activeElement = null;
        }



        /// <summary>
        /// Switch the current active element to the given element.
        /// </summary>
        /// <param name="_VisualElement"> The target child element. </param>
        /// <returns>
        /// False if the target element is the same as the active element,
        /// or target element is invalid. Otherwise, true.
        /// </returns>
        public bool SwitchActive(VisualElement _VisualElement)
        {
            if (!isValid) return false;
            if (activeElement == _VisualElement || _VisualElement == null) return false;

            if (activeElement != null)
                activeElement.style.display = DisplayStyle.None;
            activeElement = _VisualElement;
            activeElement.style.display = DisplayStyle.Flex;

            return true;
        }

        /// <inheritdoc cref="SwitchActive(VisualElement)"/>
        /// <param name="_ElementName"> The name of child element. 
        ///                             If multiple children have the same name, return any element with the highest hierarchy. </param>
        public bool SwitchActive(string _ElementName)
        {
            if (!isValid) return false;

            foreach (var child in Children())
            {
                if (child.name != _ElementName) continue;

                return SwitchActive(child);
            }

            return false;
        }

        /// <inheritdoc cref="SwitchActive(VisualElement)"/>
        /// <param name="_Index"> The index of child element. </param>
        public bool SwitchActive(int _Index)
        {
            if (_Index < 0 || _Index >= childCount) return false;

            return SwitchActive(ElementAt(_Index));
        }
    }
}
