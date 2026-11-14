using UnityEngine;

namespace LMK.Runtime.AbilitySystem
{
    /// <summary>
    /// Plays an <see cref="Animator"/> state on the owner's avatar and completes when it has played for its length, in
    /// world time (UE's <c>PlayMontageAndWait</c>, without montages). True when it played to the end; false when the avatar
    /// has no animator or the state doesn't exist.
    /// </summary>
    /// <remarks>
    /// The length is the given duration, or the state's length once the animator has entered it. An animator that
    /// doesn't update (a disabled one, or the editor outside Play Mode) never reports a length: the task then completes
    /// after the crossfade. Ending the activation doesn't stop the animation: play another state in <c>OnEnd</c>.
    /// </remarks>
    public sealed class PlayAnimationAndWait : AbilityTask<bool>
    {
        private const double UNRESOLVED_GRACE = 0.25;

        private readonly int stateHash;
        private readonly string stateName;
        private readonly int layer;
        private readonly float crossFade;
        private readonly float speed;
        private Animator animator;
        private double start;
        private double end = double.NaN;

        /// <param name="_State"> The state's name or full path, e.g. "Base Layer.Cast". </param>
        /// <param name="_Duration"> Seconds to wait; 0 or less for the state's own length. </param>
        /// <param name="_CrossFade"> Seconds of crossfade into the state. </param>
        /// <param name="_Speed"> The animator's speed while the state plays. </param>
        public PlayAnimationAndWait(string _State, float _Duration = 0f, int _Layer = 0, float _CrossFade = 0.1f, float _Speed = 1f)
        {
            stateName = _State;
            stateHash = Animator.StringToHash(_State);
            layer = _Layer;
            crossFade = Mathf.Max(0f, _CrossFade);
            speed = _Speed > 0f ? _Speed : 1f;
            if (_Duration > 0f) end = _Duration;
        }

        protected internal override bool TicksWithWorld => true;

        /// <summary>
        /// The animator playing the state, once started.
        /// </summary>
        public Animator Animator => animator;

        protected internal override void OnStart()
        {
            start = System.World.Time;
            if (!double.IsNaN(end)) end += start;

            var avatar = Context.ActorInfo.AvatarGameObject;
            animator = avatar != null ? avatar.GetComponentInChildren<Animator>() : null;

            if (animator == null || !animator.isActiveAndEnabled || animator.runtimeAnimatorController == null)
            {
                Complete(false);
                return;
            }

            if (!animator.HasState(layer, stateHash))
            {
                Debug.LogWarning($"[{Context.Ability.name}] {animator.name} has no animator state \"{stateName}\" on layer {layer}.", animator);
                Complete(false);
                return;
            }

            animator.speed = speed;
            animator.CrossFadeInFixedTime(stateHash, crossFade, layer);
        }

        protected internal override void Tick(double _Time, double _DeltaTime)
        {
            if (animator == null)
            {
                Complete(false);
                return;
            }

            if (double.IsNaN(end))
            {
                var info = animator.IsInTransition(layer) ? animator.GetNextAnimatorStateInfo(layer) : animator.GetCurrentAnimatorStateInfo(layer);
                if ((info.fullPathHash == stateHash || info.shortNameHash == stateHash) && info.length > 0f)
                    end = start + info.length / speed;
                else if (_Time - start >= crossFade + UNRESOLVED_GRACE)
                    end = _Time;
            }

            if (!double.IsNaN(end) && _Time >= end - 1e-6)
                Complete(true);
        }
    }
}
