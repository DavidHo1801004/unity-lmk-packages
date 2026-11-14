using System.Collections.Generic;
using System.Linq;

using UnityEngine;

using LMK.Runtime.GameplayTags;

namespace LMK.Runtime.AbilitySystem
{
    /// <summary>
    /// Routes cue events to notifies: the notifies of the cue's own tag, then of each parent tag, stopping
    /// after a notify marked <c>isOverride</c>, as in UE. Actor notifies are pooled, one instance per target and prefab
    /// from <c>OnActive</c> to <c>Removed</c>.
    /// </summary>
    public sealed class GameplayCueManager : IGameplayCueHandler
    {
        private readonly Dictionary<GameplayTag, List<GameplayCueNotifyStatic>> staticNotifies = new();
        private readonly Dictionary<GameplayTag, List<GameplayCueNotifyActor>> actorNotifies = new();
        private readonly Dictionary<(GameplayAbilitySystem, GameplayCueNotifyActor), GameplayCueNotifyActor> active = new();
        private readonly Dictionary<GameplayCueNotifyActor, Stack<GameplayCueNotifyActor>> pools = new();
        private readonly List<(GameplayCueNotifyActor Instance, GameplayCueNotifyActor Prefab, double ReleaseAt)> temporary = new();
        private readonly Transform poolRoot;

        /// <summary>
        /// World time, for temporary instances. Set by the runner each step.
        /// </summary>
        public double Time { get; set; }

        /// <param name="_PoolRoot"> Where pooled actor instances wait, inactive. <see langword="null"/> for the scene root. </param>
        public GameplayCueManager(IEnumerable<GameplayCueSet> _Sets, Transform _PoolRoot = null)
        {
            poolRoot = _PoolRoot;

            foreach (var set in _Sets ?? Enumerable.Empty<GameplayCueSet>())
            {
                if (set == null) continue;

                foreach (var notify in set.staticNotifies)
                    if (notify != null && !notify.cueTag.IsNone)
                        Add(staticNotifies, notify.cueTag, notify);

                foreach (var notify in set.actorNotifies)
                    if (notify != null && !notify.cueTag.IsNone)
                        Add(actorNotifies, notify.cueTag, notify);
            }
        }

        private static void Add<T>(Dictionary<GameplayTag, List<T>> _Map, GameplayTag _Tag, T _Notify)
        {
            if (!_Map.TryGetValue(_Tag, out var list))
                _Map.Add(_Tag, list = new List<T>());
            list.Add(_Notify);
        }



        #region Routing
        public void HandleCue(GameplayAbilitySystem _Target, GameplayTag _CueTag, GameplayCueEvent _Event, GameplayCueParameters _Parameters)
        {
            foreach (var tag in TagAndAncestors(_CueTag))
            {
                bool stop = false;
                var parameters = _Parameters.Copy();
                parameters.MatchedTag = tag;
                parameters.OriginalTag = _CueTag;

                if (staticNotifies.TryGetValue(tag, out var statics))
                    foreach (var notify in statics)
                    {
                        RunStatic(notify, _Target, _Event, parameters);
                        stop |= notify.isOverride;
                    }

                if (actorNotifies.TryGetValue(tag, out var actors))
                    foreach (var prefab in actors)
                    {
                        RunActor(prefab, _Target, _Event, parameters);
                        stop |= prefab.isOverride;
                    }

                if (stop) return;
            }
        }

        private static IEnumerable<GameplayTag> TagAndAncestors(GameplayTag _Tag)
        {
            for (var tag = _Tag; !tag.IsNone; tag = tag.Parent)
                yield return tag;
        }

        private static void RunStatic(GameplayCueNotifyStatic _Notify, GameplayAbilitySystem _Target, GameplayCueEvent _Event, GameplayCueParameters _Parameters)
        {
            switch (_Event)
            {
                case GameplayCueEvent.Executed: _Notify.OnExecute(_Target, _Parameters); break;
                case GameplayCueEvent.OnActive: _Notify.OnActive(_Target, _Parameters); break;
                case GameplayCueEvent.WhileActive: _Notify.WhileActive(_Target, _Parameters); break;
                case GameplayCueEvent.Removed: _Notify.OnRemove(_Target, _Parameters); break;
            }
        }
        #endregion



        #region Actor notifies
        private void RunActor(GameplayCueNotifyActor _Prefab, GameplayAbilitySystem _Target, GameplayCueEvent _Event, GameplayCueParameters _Parameters)
        {
            var key = (_Target, _Prefab);
            switch (_Event)
            {
                case GameplayCueEvent.OnActive:
                    if (!active.TryGetValue(key, out var instance))
                        active.Add(key, instance = Acquire(_Prefab, _Target));
                    instance.OnActive(_Target, _Parameters);
                    break;

                case GameplayCueEvent.WhileActive:
                    if (active.TryGetValue(key, out var running))
                        running.WhileActive(_Target, _Parameters);
                    break;

                case GameplayCueEvent.Removed:
                    if (!active.TryGetValue(key, out var removed)) break;
                    active.Remove(key);
                    removed.OnRemove(_Target, _Parameters);
                    Release(_Prefab, removed);
                    break;

                case GameplayCueEvent.Executed:
                    if (active.TryGetValue(key, out var current))
                    {
                        current.OnExecute(_Target, _Parameters);
                        break;
                    }

                    var temporaryInstance = Acquire(_Prefab, _Target);
                    temporaryInstance.OnExecute(_Target, _Parameters);
                    temporary.Add((temporaryInstance, _Prefab, Time + _Prefab.executedLifetime));
                    break;
            }
        }

        private GameplayCueNotifyActor Acquire(GameplayCueNotifyActor _Prefab, GameplayAbilitySystem _Target)
        {
            if (!pools.TryGetValue(_Prefab, out var pool))
                pools.Add(_Prefab, pool = new Stack<GameplayCueNotifyActor>());

            var instance = pool.Count > 0 ? pool.Pop() : Object.Instantiate(_Prefab, poolRoot);
            var avatar = _Target.ActorInfo.AvatarGameObject;

            if (avatar != null)
            {
                if (_Prefab.attachToAvatar) instance.transform.SetParent(avatar.transform, false);
                instance.transform.position = avatar.transform.position;
            }

            instance.Target = _Target;
            instance.gameObject.SetActive(true);
            return instance;
        }

        private void Release(GameplayCueNotifyActor _Prefab, GameplayCueNotifyActor _Instance)
        {
            if (_Instance == null) return;

            _Instance.Target = null;
            _Instance.gameObject.SetActive(false);
            _Instance.transform.SetParent(poolRoot, false);
            pools[_Prefab].Push(_Instance);
        }

        /// <summary>
        /// Return the temporary instances of Executed cues whose lifetime is over.
        /// </summary>
        public void Tick(double _Time)
        {
            Time = _Time;
            for (int i = temporary.Count - 1; i >= 0; i--)
            {
                if (temporary[i].ReleaseAt > _Time) continue;

                Release(temporary[i].Prefab, temporary[i].Instance);
                temporary.RemoveAt(i);
            }
        }

        public void ClearTarget(GameplayAbilitySystem _Target)
        {
            foreach (var key in active.Keys.Where(e => e.Item1 == _Target).ToList())
            {
                var instance = active[key];
                active.Remove(key);
                Release(key.Item2, instance);
            }
        }

        /// <summary>
        /// The actor instance showing a cue on a unit, if any.
        /// </summary>
        public GameplayCueNotifyActor FindActive(GameplayAbilitySystem _Target, GameplayCueNotifyActor _Prefab)
            => active.TryGetValue((_Target, _Prefab), out var instance) ? instance : null;

        /// <summary>
        /// Destroy every pooled and active instance.
        /// </summary>
        public void Dispose()
        {
            foreach (var instance in active.Values.Concat(pools.Values.SelectMany(e => e)).Concat(temporary.Select(e => e.Instance)))
                if (instance != null)
                {
                    if (Application.isPlaying) Object.Destroy(instance.gameObject);
                    else Object.DestroyImmediate(instance.gameObject);
                }

            active.Clear();
            pools.Clear();
            temporary.Clear();
        }
        #endregion
    }
}
