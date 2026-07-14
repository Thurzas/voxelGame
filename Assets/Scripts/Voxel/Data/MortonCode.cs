using System;

namespace VoxelGame.Data
{
    /// <summary>
    /// 3D Morton (Z-order) encoding used to index octree nodes/bricks by a single
    /// sortable 64-bit key. 21 bits per axis (63 bits interleaved) centered on zero
    /// via <see cref="Bias"/> so negative world coordinates are supported.
    /// </summary>
    public static class MortonCode
    {
        public const int BitsPerAxis = 21;
        public const int Bias = 1 << (BitsPerAxis - 1); // 1,048,576
        public const int MinCoordinate = -Bias;
        public const int MaxCoordinate = (1 << BitsPerAxis) - 1 - Bias; // 1,048,575

        public static ulong Encode(int x, int y, int z)
        {
            CheckRange(x);
            CheckRange(y);
            CheckRange(z);

            uint ux = (uint)(x + Bias);
            uint uy = (uint)(y + Bias);
            uint uz = (uint)(z + Bias);

            return SplitBy3(ux) | (SplitBy3(uy) << 1) | (SplitBy3(uz) << 2);
        }

        public static void Decode(ulong code, out int x, out int y, out int z)
        {
            x = (int)CompactBy3(code) - Bias;
            y = (int)CompactBy3(code >> 1) - Bias;
            z = (int)CompactBy3(code >> 2) - Bias;
        }

        private static void CheckRange(int v)
        {
            if (v < MinCoordinate || v > MaxCoordinate)
            {
                throw new ArgumentOutOfRangeException(nameof(v),
                    $"Coordinate {v} is outside the Morton-encodable range [{MinCoordinate}, {MaxCoordinate}].");
            }
        }

        // Spreads the low 21 bits of 'a' so there are two zero bits between each
        // original bit (magic-number bit-spreading, validated by round-trip +
        // collision tests against a naive bit-by-bit reference implementation).
        private static ulong SplitBy3(uint a)
        {
            ulong x = a & 0x1fffffUL;
            x = (x | (x << 32)) & 0x1f00000000ffffUL;
            x = (x | (x << 16)) & 0x1f0000ff0000ffUL;
            x = (x | (x << 8)) & 0x100f00f00f00f00fUL;
            x = (x | (x << 4)) & 0x10c30c30c30c30c3UL;
            x = (x | (x << 2)) & 0x1249249249249249UL;
            return x;
        }

        private static uint CompactBy3(ulong x)
        {
            x &= 0x1249249249249249UL;
            x = (x | (x >> 2)) & 0x10c30c30c30c30c3UL;
            x = (x | (x >> 4)) & 0x100f00f00f00f00fUL;
            x = (x | (x >> 8)) & 0x1f0000ff0000ffUL;
            x = (x | (x >> 16)) & 0x1f00000000ffffUL;
            x = (x | (x >> 32)) & 0x1fffffUL;
            return (uint)x;
        }
    }
}