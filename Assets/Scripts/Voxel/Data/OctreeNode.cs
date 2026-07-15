using System;

namespace VoxelGame.Data
{
    /// <summary>
    /// Identifies a node in the linear octree: the Morton code of its brick-space
    /// coordinate plus its level (depth). The level is required alongside the
    /// Morton code because the same Morton code is reused at every level (it only
    /// encodes a position, not a depth).
    /// </summary>
    public readonly struct OctreeNodeKey : IEquatable<OctreeNodeKey>
    {
        public readonly ulong Morton;
        public readonly byte Level;

        public OctreeNodeKey(ulong morton, byte level)
        {
            Morton = morton;
            Level = level;
        }

        public static OctreeNodeKey FromCoordinates(int x, int y, int z, byte level)
        {
            return new OctreeNodeKey(MortonCode.Encode(x, y, z), level);
        }

        /// <summary>
        /// Key of the parent node one level up. The coordinate encoded in
        /// <see cref="Morton"/> is expressed in *this node's own level* grid units
        /// (a level-L node at coordinate (x,y,z) covers the world region
        /// [x*2^L, (x+1)*2^L) per axis) — going up a level halves each coordinate
        /// (floored, not truncated, so this stays correct for negative coordinates).
        /// </summary>
        /// <remarks>
        /// A naive <c>Morton &gt;&gt; 3</c> does NOT work here even though each octree
        /// level consumes exactly 3 interleaved bits: <see cref="MortonCode.Encode"/>
        /// adds <see cref="MortonCode.Bias"/> to every coordinate before
        /// interleaving, so even a coordinate near zero already uses bits near the
        /// top of the 63-bit code — shifting left to descend a level overflows
        /// immediately. Confirmed by an offline round-trip test that failed on 100%
        /// of cases with the naive bit-shift version before switching to this
        /// decode/re-encode approach (roadmap: SVO phase, sous-étape 1).
        /// </remarks>
        public OctreeNodeKey Parent()
        {
            MortonCode.Decode(Morton, out int x, out int y, out int z);
            return new OctreeNodeKey(MortonCode.Encode(FloorDiv2(x), FloorDiv2(y), FloorDiv2(z)), (byte)(Level + 1));
        }

        /// <summary>
        /// Key of one of the 8 children one level down. <paramref name="childIndex"/>
        /// (0..7) packs the child's offset from this node's origin as 3 bits:
        /// bit0 = +X, bit1 = +Y, bit2 = +Z (same axis order as
        /// <see cref="MortonCode.Encode"/>'s interleaving). See <see cref="Parent"/>
        /// for why this goes through decode/re-encode rather than a bit-shift.
        /// </summary>
        public OctreeNodeKey GetChild(int childIndex)
        {
            if ((uint)childIndex >= 8)
            {
                throw new ArgumentOutOfRangeException(nameof(childIndex), "Child index must be in [0, 7].");
            }
            if (Level == 0)
            {
                throw new InvalidOperationException("A level-0 node is a leaf and has no children.");
            }

            MortonCode.Decode(Morton, out int x, out int y, out int z);
            int cx = (x * 2) + (childIndex & 1);
            int cy = (y * 2) + ((childIndex >> 1) & 1);
            int cz = (z * 2) + ((childIndex >> 2) & 1);
            return new OctreeNodeKey(MortonCode.Encode(cx, cy, cz), (byte)(Level - 1));
        }

        private static int FloorDiv2(int v) => v >= 0 ? v / 2 : (v - 1) / 2;

        public bool Equals(OctreeNodeKey other) => Morton == other.Morton && Level == other.Level;

        public override bool Equals(object obj) => obj is OctreeNodeKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                // Hand-rolled FNV-ish combine: avoids System.HashCode, which is not
                // guaranteed Burst-compatible and uses a randomized per-process seed
                // (undesirable for a key whose hash may need to be reproducible).
                ulong h = (Morton * 1099511628211UL) ^ Level;
                return (int)(h ^ (h >> 32));
            }
        }

        public static bool operator ==(OctreeNodeKey a, OctreeNodeKey b) => a.Equals(b);
        public static bool operator !=(OctreeNodeKey a, OctreeNodeKey b) => !a.Equals(b);
    }

    public enum OctreeNodeState : byte
    {
        Empty = 0,    // no data at all (treated as air / fully transparent)
        Constant = 1, // homogeneous region, see ConstantValue
        Brick = 2,    // backed by a dense brick, see BrickIndex
    }

    /// <summary>
    /// Node payload stored per <see cref="OctreeNodeKey"/>. Deliberately does not
    /// embed a <see cref="VoxelBrick"/> (or its NativeArray) directly: Unity native
    /// containers should not be nested inside another native container's value
    /// type. Brick storage/pooling is wired up in a later phase (roadmap phase 5);
    /// BrickIndex is a forward-compatible placeholder for that pool slot.
    /// </summary>
    public struct OctreeNode
    {
        public OctreeNodeState State;
        public Voxel ConstantValue;
        public int BrickIndex;

        public static OctreeNode Empty => new OctreeNode { State = OctreeNodeState.Empty, BrickIndex = -1 };

        public static OctreeNode Constant(Voxel value) =>
            new OctreeNode { State = OctreeNodeState.Constant, ConstantValue = value, BrickIndex = -1 };

        public static OctreeNode WithBrick(int brickIndex) =>
            new OctreeNode { State = OctreeNodeState.Brick, BrickIndex = brickIndex };
    }
}