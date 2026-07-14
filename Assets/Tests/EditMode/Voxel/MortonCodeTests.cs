using System;
using NUnit.Framework;
using VoxelGame.Data;

namespace VoxelGame.Data.Tests
{
    public class MortonCodeTests
    {
        [TestCase(0, 0, 0)]
        [TestCase(1, 0, 0)]
        [TestCase(0, 1, 0)]
        [TestCase(0, 0, 1)]
        [TestCase(-1, -1, -1)]
        [TestCase(1, -1, 1)]
        [TestCase(12345, -6789, 42)]
        [TestCase(MortonCode.MinCoordinate, MortonCode.MinCoordinate, MortonCode.MinCoordinate)]
        [TestCase(MortonCode.MaxCoordinate, MortonCode.MaxCoordinate, MortonCode.MaxCoordinate)]
        [TestCase(MortonCode.MinCoordinate, MortonCode.MaxCoordinate, 0)]
        public void EncodeDecode_RoundTrips(int x, int y, int z)
        {
            ulong code = MortonCode.Encode(x, y, z);
            MortonCode.Decode(code, out int dx, out int dy, out int dz);

            Assert.AreEqual(x, dx);
            Assert.AreEqual(y, dy);
            Assert.AreEqual(z, dz);
        }

        [Test]
        public void EncodeDecode_RoundTrips_RandomCoverage()
        {
            var rng = new Random(20260714);
            for (int i = 0; i < 5000; i++)
            {
                int x = rng.Next(MortonCode.MinCoordinate, MortonCode.MaxCoordinate + 1);
                int y = rng.Next(MortonCode.MinCoordinate, MortonCode.MaxCoordinate + 1);
                int z = rng.Next(MortonCode.MinCoordinate, MortonCode.MaxCoordinate + 1);

                ulong code = MortonCode.Encode(x, y, z);
                MortonCode.Decode(code, out int dx, out int dy, out int dz);

                Assert.AreEqual(x, dx, $"x mismatch for ({x},{y},{z})");
                Assert.AreEqual(y, dy, $"y mismatch for ({x},{y},{z})");
                Assert.AreEqual(z, dz, $"z mismatch for ({x},{y},{z})");
            }
        }

        [Test]
        public void Encode_IsInjective_OverLocalCube()
        {
            var seen = new System.Collections.Generic.HashSet<ulong>();
            for (int x = -8; x <= 8; x++)
            for (int y = -8; y <= 8; y++)
            for (int z = -8; z <= 8; z++)
            {
                ulong code = MortonCode.Encode(x, y, z);
                Assert.IsTrue(seen.Add(code), $"Collision at ({x},{y},{z})");
            }
        }

        [Test]
        public void Encode_OutOfRange_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => MortonCode.Encode(MortonCode.MaxCoordinate + 1, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => MortonCode.Encode(MortonCode.MinCoordinate - 1, 0, 0));
        }
    }
}