using System.Collections.Generic;
using System.Linq;

using UnityEditor;

using UnityEngine;

using LMK.Runtime.AbilitySystem;

namespace LMK.Editor.AbilitySystem
{
    /// <summary>
    /// Shows the units of the scene's ability system worlds while the game runs: attributes, tags, active effects,
    /// abilities and cues. Read-only.
    /// </summary>
    internal sealed class AbilitySystemDebuggerWindow : EditorWindow
    {
        private const string MENU = "Tools/LMK/Ability System/";

        [SerializeField] private string selectedUnit;
        [SerializeField] private bool followSelection = true;
        [SerializeField] private bool showAttributes = true, showTags = true, showEffects = true, showAbilities = true, showCues = true;
        private Vector2 listScroll, detailScroll;

        [MenuItem(MENU + "Debugger", priority = 20)]
        public static void Open() => GetWindow<AbilitySystemDebuggerWindow>("Ability System Debugger").Show();

        private void OnEnable() => EditorApplication.update += RepaintWhilePlaying;

        private void OnDisable() => EditorApplication.update -= RepaintWhilePlaying;

        private double lastRepaint;

        private void RepaintWhilePlaying()
        {
            if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup - lastRepaint < 0.1) return;

            lastRepaint = EditorApplication.timeSinceStartup;
            Repaint();
        }

        private void OnGUI()
        {
            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play Mode to inspect the units of the scene's ability system worlds.", MessageType.Info);
                return;
            }

            var units = FindUnits();
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                followSelection = GUILayout.Toggle(followSelection, "Follow Selection", EditorStyles.toolbarButton, GUILayout.Width(110));
                GUILayout.FlexibleSpace();
                GUILayout.Label($"{units.Count} units", EditorStyles.miniLabel);
            }

            if (followSelection && Selection.activeGameObject != null)
            {
                var component = Selection.activeGameObject.GetComponentInParent<AbilitySystemComponent>();
                if (component != null && component.System != null) selectedUnit = Key(component.System);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                using (var scroll = new EditorGUILayout.ScrollViewScope(listScroll, GUILayout.Width(200)))
                {
                    listScroll = scroll.scrollPosition;
                    foreach (var unit in units)
                    {
                        bool selected = Key(unit) == selectedUnit;
                        if (GUILayout.Toggle(selected, unit.Name, "Button") && !selected)
                            selectedUnit = Key(unit);
                    }
                }

                using (var scroll = new EditorGUILayout.ScrollViewScope(detailScroll))
                {
                    detailScroll = scroll.scrollPosition;
                    var current = units.FirstOrDefault(e => Key(e) == selectedUnit);
                    if (current != null) DrawUnit(current);
                    else EditorGUILayout.HelpBox("Select a unit.", MessageType.None);
                }
            }
        }

        private static string Key(GameplayAbilitySystem _Unit) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(_Unit).ToString();

        private static List<GameplayAbilitySystem> FindUnits()
        {
            var units = new List<GameplayAbilitySystem>();
            foreach (var runner in Object.FindObjectsByType<AbilitySystemWorldRunner>(FindObjectsSortMode.None))
                units.AddRange(runner.World.Systems.Where(e => !e.IsShuttingDown));

            return units;
        }

        private void DrawUnit(GameplayAbilitySystem _Unit)
        {
            EditorGUILayout.LabelField(_Unit.Name, EditorStyles.boldLabel);
            EditorGUILayout.LabelField("World time", _Unit.World.Time.ToString("0.00") + " s");
            if (_Unit.ActorInfo.AvatarGameObject != null)
                EditorGUILayout.ObjectField("Avatar", _Unit.ActorInfo.AvatarGameObject, typeof(GameObject), true);

            if (Section(ref showAttributes, "Attributes"))
                foreach (var attribute in _Unit.GetAttributes())
                {
                    float current = _Unit.GetCurrent(attribute), baseValue = _Unit.GetBase(attribute);
                    EditorGUILayout.LabelField(attribute.Name, Mathf.Approximately(current, baseValue) ? $"{current:0.##}" : $"{current:0.##}  (base {baseValue:0.##})");
                }

            if (Section(ref showTags, "Tags"))
                foreach (var tag in _Unit.Tags.Snapshot())
                    EditorGUILayout.LabelField(tag.Path, $"x{_Unit.GetTagCount(tag)}");

            if (Section(ref showEffects, "Active effects"))
                foreach (var effect in _Unit.GetActiveEffects())
                {
                    var details = new List<string>();
                    if (effect.StackCount > 1) details.Add($"{effect.StackCount} stacks");
                    if (effect.HasDuration) details.Add($"{effect.TimeRemaining():0.0} s left");
                    if (effect.IsPeriodic) details.Add($"every {effect.Spec.Period:0.##} s");
                    if (effect.IsInhibited) details.Add("inhibited");
                    details.Add($"level {effect.Spec.Level:0.##}");
                    EditorGUILayout.LabelField(effect.Def != null ? effect.Def.name : "?", string.Join(", ", details));
                }

            if (Section(ref showAbilities, "Abilities"))
                foreach (var spec in _Unit.GetActivatableAbilities())
                {
                    string state = spec.IsActive ? $"active x{spec.ActiveCount}" : "";
                    double cooldown = spec.Ability != null ? _Unit.GetCooldownRemaining(spec.Ability) : 0;
                    if (cooldown > 0) state += (state.Length > 0 ? ", " : "") + $"cooldown {cooldown:0.0} s";
                    EditorGUILayout.LabelField(spec.Ability != null ? spec.Ability.name : "?", state);
                }

            if (Section(ref showCues, "Loose cues"))
                foreach (var cue in _Unit.LooseCues)
                    EditorGUILayout.LabelField(cue.Path);
        }

        private static bool Section(ref bool _Open, string _Title)
        {
            EditorGUILayout.Space(4);
            _Open = EditorGUILayout.Foldout(_Open, _Title, true, EditorStyles.foldoutHeader);
            return _Open;
        }
    }
}
