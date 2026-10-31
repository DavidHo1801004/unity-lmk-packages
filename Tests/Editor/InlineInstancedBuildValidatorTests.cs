using System;
using System.Collections.Generic;

using NUnit.Framework;

namespace LMK.Editor.Attributes.Tests
{
    public class InlineInstancedBuildValidatorTests
    {
        private class Nested { }

        private static string Key(Type _Type)
        {
            return InlineInstancedBuildValidator.GetTypeKey(_Type.Assembly.GetName().Name, _Type.FullName);
        }

        [Test]
        public void FindMissingFromPlayer_ReturnsOnlyTypesAbsentFromThePlayer()
        {
            var playerTypes = new HashSet<string> { Key(typeof(TestLeafA)) };

            var missing = InlineInstancedBuildValidator.FindMissingFromPlayer(
                new[] { typeof(TestLeafA), typeof(TestLeafB) }, playerTypes);

            Assert.That(missing, Is.EqualTo(new[] { typeof(TestLeafB) }));
        }

        [Test]
        public void FindMissingFromPlayer_ComparesAssemblyAsWellAsTypeName()
        {
            var playerTypes = new HashSet<string>
            {
                InlineInstancedBuildValidator.GetTypeKey("OtherAssembly", typeof(TestLeafA).FullName),
            };

            var missing = InlineInstancedBuildValidator.FindMissingFromPlayer(new[] { typeof(TestLeafA) }, playerTypes);

            Assert.That(missing, Is.EqualTo(new[] { typeof(TestLeafA) }));
        }

        [Test]
        public void GetTypeKey_MatchesNestedTypesBetweenCecilAndReflection()
        {
            string reflectionName = typeof(Nested).FullName;
            string cecilName = reflectionName.Replace('+', '/');

            Assert.That(InlineInstancedBuildValidator.GetTypeKey("A", cecilName),
                Is.EqualTo(InlineInstancedBuildValidator.GetTypeKey("A", reflectionName)));
        }

        [Test]
        public void GetFailureMessage_NamesTheTypesAndTheFix()
        {
            string message = InlineInstancedBuildValidator.GetFailureMessage(new[] { typeof(TestLeafA) });

            Assert.That(message, Does.Contain(typeof(TestLeafA).FullName));
            Assert.That(message, Does.Contain("[CanNotReferenceInline]"));
        }
    }
}
