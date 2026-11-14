using System.Collections.Generic;
using System.Reflection;

using NUnit.Framework;

using UnityEngine;

using LMK.Runtime.AbilitySystem;

using static LMK.Editor.AbilitySystem.Tests.TestAttributes;
using static LMK.Runtime.AbilitySystem.ModifierOp;

namespace LMK.Editor.AbilitySystem.Tests
{
    /// <summary>
    /// The scene layer: <see cref="AbilitySystemWorldRunner"/>, <see cref="AbilitySystemComponent"/>
    /// and <see cref="PlayAnimationAndWait"/>. Edit Mode doesn't run the Unity messages, so the tests call them.
    /// </summary>
    public sealed class UnityLayerTests
    {
        private TestWorld w;

        [SetUp]
        public void SetUp() => w = new TestWorld();

        [TearDown]
        public void TearDown() => w.Dispose();

        private static void Message(MonoBehaviour _Target, string _Name)
        {
            for (var type = _Target.GetType(); type != null; type = type.BaseType)
            {
                var method = type.GetMethod(_Name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (method == null) continue;

                method.Invoke(_Target, null);
                return;
            }

            Assert.Fail($"{_Target.GetType().Name} has no {_Name}.");
        }

        [Test]
        public void EveryAssetAndComponentType_HasAScriptFileOfItsName()
        {
            // Unity serializes a ScriptableObject or MonoBehaviour only when its file is named after it: otherwise assets
            // and prefabs lose the script.
            var missing = new List<string>();
            foreach (var type in typeof(GameplayEffect).Assembly.GetTypes())
            {
                if (type.IsAbstract || type.IsGenericType) continue;

                if (typeof(ScriptableObject).IsAssignableFrom(type))
                {
                    var instance = ScriptableObject.CreateInstance(type);
                    if (UnityEditor.MonoScript.FromScriptableObject(instance) == null) missing.Add(type.Name);
                    Object.DestroyImmediate(instance);
                }
                else if (typeof(MonoBehaviour).IsAssignableFrom(type))
                {
                    var go = new GameObject();
                    var component = (MonoBehaviour)go.AddComponent(type);
                    if (UnityEditor.MonoScript.FromMonoBehaviour(component) == null) missing.Add(type.Name);
                    Object.DestroyImmediate(go);
                }
            }

            CollectionAssert.IsEmpty(missing);
        }

        [Test]
        public void Component_CreatesItsUnitInTheRunnersWorld_AndStartsIt()
        {
            var runnerObject = new GameObject("Runner");
            var hero = new GameObject("Hero");
            try
            {
                var runner = runnerObject.AddComponent<AbilitySystemWorldRunner>();
                Message(runner, "Awake");

                var ability = w.Ability(a => a.Logic(TestLogic.StayActive()));
                var component = hero.AddComponent<AbilitySystemComponent>();
                component.DefaultSets.Add(w.Set((Health, 100f), (Armor, 10f)));
                component.StartupEffects.Add(w.Effect(e => e.Infinite().Mod(Armor, AddBase, 5)));
                component.DefaultAbilities.Add(new DefaultAbility(ability, 2));

                Message(component, "Awake");
                Assert.IsNotNull(component.System);
                Assert.AreSame(runner.World, component.System.World, "the scene's runner");
                Assert.AreEqual("Hero", component.System.Name);

                Message(component, "Start");
                Assert.AreSame(hero, component.System.ActorInfo.AvatarGameObject);
                Assert.AreEqual(15f, component.System.GetCurrent(Armor));
                Assert.AreEqual(2f, component.System.FindAbility(ability).Level);

                runner.Run(0.25);
                Assert.AreEqual(0.25, runner.World.Time, 1e-9);

                var unit = component.System;
                Message(component, "OnDestroy");
                Assert.IsTrue(unit.IsShuttingDown);

                var world = runner.World;
                Message(runner, "OnDestroy");
                Assert.IsTrue(world.IsDisposed);
            }
            finally
            {
                Object.DestroyImmediate(hero);
                Object.DestroyImmediate(runnerObject);
            }
        }

        [Test]
        public void Component_WithoutARunner_CreatesOne()
        {
            var hero = new GameObject("Hero");
            AbilitySystemWorldRunner runner = null;
            try
            {
                var component = hero.AddComponent<AbilitySystemComponent>();
                Message(component, "Awake");

                runner = component.Runner;
                Assert.IsNotNull(runner);
                Assert.AreSame(runner.World, component.System.World);
            }
            finally
            {
                Object.DestroyImmediate(hero);
                if (runner != null)
                {
                    Message(runner, "OnDestroy");
                    Object.DestroyImmediate(runner.gameObject);
                }
            }
        }

        [Test]
        public void Runner_ShowsCuesThroughItsCueManager()
        {
            var runnerObject = new GameObject("Runner");
            var notify = ScriptableObject.CreateInstance<RecordingNotify>();
            var set = ScriptableObject.CreateInstance<GameplayCueSet>();
            try
            {
                notify.cueTag = TestTags.CueHit;
                set.staticNotifies.Add(notify);

                var runner = runnerObject.AddComponent<AbilitySystemWorldRunner>();
                runner.CueSets.Add(set);
                Message(runner, "Awake");

                var unit = runner.World.CreateSystem("Unit");
                unit.ExecuteCue(TestTags.CueHitFire);
                runner.Run(0.01);

                Assert.AreEqual(1, notify.Calls.Count);
                Message(runner, "OnDestroy");
            }
            finally
            {
                Object.DestroyImmediate(runnerObject);
                Object.DestroyImmediate(notify);
                Object.DestroyImmediate(set);
            }
        }

        [Test]
        public void PlayAnimation_WithoutAnAnimator_CompletesFalse()
        {
            var avatar = new GameObject("Avatar");
            try
            {
                bool? result = null;
                var unit = w.MakeUnit();
                unit.InitActorInfo(avatar, avatar);
                var h = unit.GiveAbility(w.Ability(a => a.Logic(ctx => ctx.PlayAnimation("Cast").Completed += e => result = e)));

                unit.TryActivateAbility(h);
                Assert.AreEqual(false, result);
            }
            finally
            {
                Object.DestroyImmediate(avatar);
            }
        }
    }
}
