using System.Collections.Generic;

using NUnit.Framework;

using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

using LMK.Editor.UIElements;
using LMK.Editor.Utilities;

namespace LMK.Editor.Tests
{
    public class ObjectLabelTests
    {
        private const string USS_CLASS_NONE = "object-label--none";
        private const string USS_CLASS_MISSING = "object-label--missing";
        private const string TEXTURE_ASSET_PATH = LMKAssetUtility.EDITOR_PACKAGE_ROOT + "/Editor/Visual Elements/Assets/Sprites/search-interface-symbol-white.png";

        private readonly List<Object> created = new();

        private ObjectLabelTestHost CreateHost()
        {
            var host = ScriptableObject.CreateInstance<ObjectLabelTestHost>();
            created.Add(host);
            return host;
        }

        private static string TextOf(ObjectLabel _Label) => _Label.Q<Label>().text;

        private static Texture IconOf(ObjectLabel _Label) => _Label.Q<Image>().image;

        [TearDown]
        public void TearDown()
        {
            foreach (var target in created)
            {
                if (target != null)
                    Object.DestroyImmediate(target);
            }
            created.Clear();
        }

        [Test]
        public void NewLabel_DisplaysNone()
        {
            var label = new ObjectLabel();

            Assert.That(TextOf(label), Is.EqualTo("None"));
            Assert.That(label.ClassListContains(USS_CLASS_NONE), Is.True);
        }

        [Test]
        public void SetValue_Assigned_ShowsNameAndIcon()
        {
            var host = CreateHost();
            host.name = "Target";
            var label = new ObjectLabel();

            label.SetValue(host);

            Assert.That(TextOf(label), Does.StartWith("Target"));
            Assert.That(IconOf(label), Is.Not.Null);
            Assert.That(label.ClassListContains(USS_CLASS_NONE), Is.False);
        }

        [Test]
        public void SetValue_Null_NamesTheGivenType()
        {
            var label = new ObjectLabel();

            label.SetValue(null, typeof(Texture2D));

            Assert.That(TextOf(label), Is.EqualTo("None (Texture2D)"));
            Assert.That(IconOf(label), Is.Null);
        }

        [Test]
        public void SetValue_DestroyedObject_IsNone()
        {
            var host = CreateHost();
            var label = new ObjectLabel();
            Object.DestroyImmediate(host);

            label.SetValue(host);

            Assert.That(label.ClassListContains(USS_CLASS_NONE), Is.True);
        }

        [Test]
        public void SetValue_Property_Unassigned_IsNoneWithFieldType()
        {
            var host = CreateHost();
            using var serializedObject = new SerializedObject(host);
            var label = new ObjectLabel();

            label.SetValue(serializedObject.FindProperty(nameof(ObjectLabelTestHost.texture)));

            Assert.That(TextOf(label), Is.EqualTo("None (Texture2D)"));
            Assert.That(label.ClassListContains(USS_CLASS_NONE), Is.True);
            Assert.That(label.ClassListContains(USS_CLASS_MISSING), Is.False);
        }

        [Test]
        public void SetValue_Property_UserType_NamesIt()
        {
            var host = CreateHost();
            using var serializedObject = new SerializedObject(host);
            var label = new ObjectLabel();

            label.SetValue(serializedObject.FindProperty(nameof(ObjectLabelTestHost.self)));

            Assert.That(TextOf(label), Is.EqualTo($"None ({nameof(ObjectLabelTestHost)})"));
        }

        [Test]
        public void SetValue_Property_Assigned_ShowsName()
        {
            var host = CreateHost();
            var target = CreateHost();
            target.name = "Target";
            host.self = target;
            using var serializedObject = new SerializedObject(host);
            var label = new ObjectLabel();

            label.SetValue(serializedObject.FindProperty(nameof(ObjectLabelTestHost.self)));

            Assert.That(TextOf(label), Does.StartWith("Target"));
        }

        [Test]
        public void SetValue_Property_DestroyedTarget_IsMissing()
        {
            var host = CreateHost();
            host.self = CreateHost();
            using var serializedObject = new SerializedObject(host);
            var property = serializedObject.FindProperty(nameof(ObjectLabelTestHost.self));
            var label = new ObjectLabel();

            Object.DestroyImmediate(host.self);
            serializedObject.Update();
            label.SetValue(property);

            Assert.That(TextOf(label), Is.EqualTo($"Missing ({nameof(ObjectLabelTestHost)})"));
            Assert.That(label.ClassListContains(USS_CLASS_MISSING), Is.True);
            Assert.That(label.ClassListContains(USS_CLASS_NONE), Is.False);
        }

        [Test]
        public void SetValue_Property_NotAnObjectReference_IsNone()
        {
            var host = CreateHost();
            using var serializedObject = new SerializedObject(host);
            var label = new ObjectLabel();

            label.SetValue(serializedObject.FindProperty(nameof(ObjectLabelTestHost.number)));

            Assert.That(TextOf(label), Is.EqualTo("None"));
        }

        [Test]
        public void SetValue_AfterMissing_ClearsState()
        {
            var host = CreateHost();
            host.self = CreateHost();
            using var serializedObject = new SerializedObject(host);
            var property = serializedObject.FindProperty(nameof(ObjectLabelTestHost.self));
            var label = new ObjectLabel();
            Object.DestroyImmediate(host.self);
            serializedObject.Update();
            label.SetValue(property);

            label.SetValue(host);

            Assert.That(label.ClassListContains(USS_CLASS_MISSING), Is.False);
            Assert.That(label.ClassListContains(USS_CLASS_NONE), Is.False);
        }

        [Test]
        public void SetValue_Texture_ShowsItsOwnImage()
        {
            var texture = LMKAssetUtility.Load<Texture2D>(TEXTURE_ASSET_PATH);
            var label = new ObjectLabel();

            label.SetValue(texture);

            Assert.That(IconOf(label), Is.Not.Null);
            Assert.That(IconOf(label), Is.Not.SameAs(AssetPreview.GetMiniTypeThumbnail(typeof(Texture2D))),
                "The icon is the generic Texture2D icon, not the texture's own image.");
        }

        [Test]
        public void SetValue_Sprite_ShowsItsOwnImage()
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(TEXTURE_ASSET_PATH);
            var label = new ObjectLabel();

            label.SetValue(sprite);

            Assert.That(sprite, Is.Not.Null);
            Assert.That(label.Q<Image>().sprite, Is.SameAs(sprite));
            Assert.That(IconOf(label), Is.Null);
        }

        [Test]
        public void SetValue_SwitchingBetweenSources_LogsNothing()
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(TEXTURE_ASSET_PATH);
            var host = CreateHost();
            var label = new ObjectLabel();

            label.SetValue(sprite);
            label.SetValue(host);

            Assert.That(label.Q<Image>().sprite, Is.Null);
            Assert.That(IconOf(label), Is.Not.Null);

            label.SetValue(sprite);
            label.SetValue(null, typeof(Sprite));

            Assert.That(label.Q<Image>().sprite, Is.Null);
            LogAssert.NoUnexpectedReceived();
        }
    }
}
