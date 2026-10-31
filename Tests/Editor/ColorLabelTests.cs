using NUnit.Framework;

using UnityEngine;
using UnityEngine.UIElements;

using LMK.Editor.UIElements;

namespace LMK.Editor.Tests
{
    public class ColorLabelTests
    {
        private static string TextOf(ColorLabel _Label) => _Label.Q<Label>().text;

        [Test]
        public void Opaque_ShowsRgb()
        {
            var label = new ColorLabel(Color.red);

            Assert.That(TextOf(label), Is.EqualTo("#FF0000"));
        }

        [Test]
        public void Transparent_ShowsRgba()
        {
            var label = new ColorLabel(new Color(1f, 0f, 0f, 0.5f));

            Assert.That(TextOf(label), Is.EqualTo("#FF000080"));
        }

        [Test]
        public void AlphaThatRoundsToOpaque_ShowsRgb()
        {
            var label = new ColorLabel(new Color(1f, 0f, 0f, 0.999f));

            Assert.That(TextOf(label), Is.EqualTo("#FF0000"));
        }

        [Test]
        public void Value_UpdatesTextAndSwatch()
        {
            var label = new ColorLabel();

            label.Value = Color.blue;

            Assert.That(label.Value, Is.EqualTo(Color.blue));
            Assert.That(TextOf(label), Is.EqualTo("#0000FF"));
            Assert.That(label.Q(className: "color-label__swatch").style.backgroundColor.value, Is.EqualTo(Color.blue));
        }
    }
}
