using System;

using NUnit.Framework;

using LMK.Runtime.Registry;

namespace LMK.Editor.Registry.Tests
{
    public sealed class RegistryResolutionTests
    {
        [Test]
        public void Live_HoldsItsRow()
        {
            var resolution = RegistryResolution.Live(3);

            Assert.IsTrue(resolution.IsLive);
            Assert.IsFalse(resolution.IsMissing);
            Assert.AreEqual(3, resolution.Row);
        }

        [Test]
        public void Live_RejectsANegativeRow()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => RegistryResolution.Live(-1));
        }

        [Test]
        public void None_IsNeitherLiveNorMissing()
        {
            Assert.IsFalse(RegistryResolution.None.IsLive);
            Assert.IsFalse(RegistryResolution.None.IsMissing);
            Assert.AreEqual(RegistryResolution.NO_ROW, RegistryResolution.None.Row);
        }

        [Test]
        public void DeletedAndMissingRegistry_AreMissing_WithNoRow()
        {
            foreach (var resolution in new[] { RegistryResolution.Deleted, RegistryResolution.MissingRegistry })
            {
                Assert.IsTrue(resolution.IsMissing);
                Assert.IsFalse(resolution.IsLive);
                Assert.AreEqual(RegistryResolution.NO_ROW, resolution.Row);
            }
        }

        [Test]
        public void Equality_ComparesStatusAndRow()
        {
            Assert.AreEqual(RegistryResolution.Live(2), RegistryResolution.Live(2));
            Assert.AreNotEqual(RegistryResolution.Live(2), RegistryResolution.Live(3));
            Assert.AreNotEqual(RegistryResolution.Deleted, RegistryResolution.MissingRegistry);
        }
    }
}
