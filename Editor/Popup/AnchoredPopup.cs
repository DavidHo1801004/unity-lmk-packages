using System;

using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

using LMK.Editor.Styles;
using LMK.Editor.Utilities;

namespace LMK.Editor.Windows
{
    /// <summary>
    /// Small popup window holding any content, placed under an anchor element. <br/>
    /// Closed by Esc, by losing focus or by <see cref="Close"/>.
    /// </summary>
    /// <remarks>
    /// A popup window rather than an overlay, so it isn't clipped by the host window
    /// and closes when the Editor loses focus.
    /// </remarks>
    public sealed class AnchoredPopup : PopupWindowContent
    {
        /// <summary>
        /// The content given to <see cref="Show"/>.
        /// </summary>
        public VisualElement Content { get; }

        /// <summary>
        /// The element the popup is placed under.
        /// </summary>
        public VisualElement Anchor { get; }

        /// <summary>
        /// Called once when the popup closes, for any reason.
        /// </summary>
        public event Action Closed;

        /// <summary>
        /// The popup's root element, once the window has created it.
        /// </summary>
        internal VisualElement Root { get; private set; }

        internal bool IsClosed { get; private set; }

        internal const string USS_CLASS = "lmk-anchored-popup";

        private const string USS_PATH = LMKAssetUtility.EDITOR_PACKAGE_ROOT + "/Editor/Popup/Assets/AnchoredPopupUSS.uss";

        // Window height used until the content is laid out.
        private const float DEFAULT_HEIGHT = 30f;

        private readonly float width;

        private readonly float minWidth;



        internal AnchoredPopup(VisualElement _Anchor, VisualElement _Content, float _Width)
        {
            Anchor = _Anchor;
            Content = _Content;
            width = Mathf.Max(_Width, 0f);
            minWidth = float.IsNaN(_Anchor.worldBound.width) ? 0f : _Anchor.worldBound.width;
        }



        /// <summary>
        /// Open a popup with <paramref name="_Content"/>, its top-left corner at the bottom-left corner of
        /// <paramref name="_Anchor"/> plus <paramref name="_Offset"/>. Unity moves it to stay on screen. <br/>
        /// Call it from UI Toolkit code, e.g. a button's click handler.
        /// </summary>
        /// <param name="_Anchor"> Element to place the popup under. It gets keyboard focus back when the popup closes. </param>
        /// <param name="_Content"> Content of the popup. Its first focusable element gets keyboard focus. </param>
        /// <param name="_Offset"> Offset from the anchor's bottom-left corner, in pixels. </param>
        /// <param name="_Width"> Popup width, or 0 to size to the content, at least as wide as the anchor. </param>
        /// <returns>
        /// The popup, or <see langword="null"/> if Unity didn't open it:
        /// Unity closes an open <see cref="AnchoredPopup"/> instead of opening a second one,
        /// and doesn't reopen one from the click that just closed it.
        /// </returns>
        public static AnchoredPopup Show(VisualElement _Anchor, VisualElement _Content, Vector2 _Offset = default, float _Width = 0f)
        {
            if (_Anchor == null) throw new ArgumentNullException(nameof(_Anchor));
            if (_Content == null) throw new ArgumentNullException(nameof(_Content));

            var popup = new AnchoredPopup(_Anchor, _Content, _Width);

            var activator = _Anchor.worldBound;
            activator.position += _Offset;

            try
            {
                UnityEditor.PopupWindow.Show(GetActivatorGUIRect(_Anchor, activator), popup);
            }
            catch (ExitGUIException)
            {
                // Unity ends the current IMGUI event after opening a window. The popup is open; nothing to unwind here.
            }

            return popup.editorWindow ? popup : null;
        }

        /// <summary>
        /// Close the popup. Raises <see cref="Closed"/> unless it was already closed.
        /// </summary>
        public void Close()
        {
            if (IsClosed) return;

            if (editorWindow)
                editorWindow.Close(); // calls OnClose

            HandleClosed();
        }



        public override VisualElement CreateGUI()
        {
            Root = new VisualElement();
            LMKTheme.Apply(Root);
            LMKTheme.AddStyleSheet(Root, USS_PATH);
            Root.AddToClassList(USS_CLASS);

            Root.style.minWidth = minWidth;
            if (width > 0f)
                Root.style.width = width;

            Root.Add(Content);

            Root.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);

            // A frame later, once the window has keyboard focus and the content is laid out.
            Root.schedule.Execute(FocusFirstFocusable);

            return Root;
        }

        // Unity sizes a UI Toolkit popup from its content's layout, and follows it when the content changes.
        // This is only the size before the first layout.
        public override Vector2 GetWindowSize()
        {
            if (Root != null && Root.layout.width > 0f && Root.layout.height > 0f)
                return Root.layout.size;

            return new Vector2(width > 0f ? width : Mathf.Max(minWidth, 1f), DEFAULT_HEIGHT);
        }

        public override void OnClose()
        {
            HandleClosed();
        }



        private void HandleClosed()
        {
            if (IsClosed) return;

            IsClosed = true;

            // Focus within the anchor's panel only: if another window took focus, it keeps it.
            if (Anchor.panel != null && Anchor.canGrabFocus)
                Anchor.Focus();

            Closed?.Invoke();
        }

        private void OnKeyDown(KeyDownEvent _Event)
        {
            if (_Event.keyCode != KeyCode.Escape) return;

            Close();
            _Event.StopPropagation();
        }

        internal void FocusFirstFocusable()
        {
            FindFirstFocusable(Content)?.Focus();
        }

        internal static VisualElement FindFirstFocusable(VisualElement _Element)
        {
            if (_Element.focusable && _Element.canGrabFocus)
                return _Element;

            foreach (var child in _Element.hierarchy.Children())
            {
                var found = FindFirstFocusable(child);
                if (found != null)
                    return found;
            }
            return null;
        }

        /// <summary>
        /// <paramref name="_PanelRect"/> in the GUI space of the current IMGUI context,
        /// found through the window hosting <paramref name="_Anchor"/>, so the popup lands in the right place
        /// whatever window is drawing. Falls back to the panel rect if the window can't be found.
        /// </summary>
        private static Rect GetActivatorGUIRect(VisualElement _Anchor, Rect _PanelRect)
        {
            var window = FindHostWindow(_Anchor.panel);
            if (window == null) return _PanelRect;

            // EditorWindow.position is the screen rect of the area rootVisualElement fills.
            var rootOrigin = window.rootVisualElement.worldBound.position;
            var screenRect = new Rect(window.position.position + _PanelRect.position - rootOrigin, _PanelRect.size);

            return GUIUtility.ScreenToGUIRect(screenRect);
        }

        private static EditorWindow FindHostWindow(IPanel _Panel)
        {
            if (_Panel == null) return null;

            if (IsHostOf(EditorWindow.focusedWindow, _Panel)) return EditorWindow.focusedWindow;
            if (IsHostOf(EditorWindow.mouseOverWindow, _Panel)) return EditorWindow.mouseOverWindow;

            foreach (var window in Resources.FindObjectsOfTypeAll<EditorWindow>())
            {
                if (IsHostOf(window, _Panel))
                    return window;
            }
            return null;
        }

        private static bool IsHostOf(EditorWindow _Window, IPanel _Panel)
        {
            return _Window && _Window.rootVisualElement.panel == _Panel;
        }
    }
}
