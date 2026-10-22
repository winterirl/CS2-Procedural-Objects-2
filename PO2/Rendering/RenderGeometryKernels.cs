using System;
using System.Runtime.InteropServices;
using Unity.Burst;

namespace PO2.Rendering
{
    // One synchronous batch works on the arrays the renderer already owns.
    // None of these pointers survive the call that pinned them.
    [BurstCompile]
    internal static unsafe class RenderGeometryKernels
    {
        internal const int kMinimumBulkCopies = 2048;
        internal const int kBulkCopyDivisor = 3;
        internal const int kFragmentedCopyDivisor = 8;

        [StructLayout(LayoutKind.Sequential)]
        internal struct Point
        {
            internal float X, Y, Z;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Set
        {
            internal uint* Bits;
            internal int* Words;
            internal int Length, WordCount, Count;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Area
        {
            internal Point* Positions;
            internal Point* Normals;
            internal int* Map;
            internal int Length, Target, Offset, Isolated, Generated;
            internal int PositionCount, NormalCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Target
        {
            internal Point* Positions;
            internal Point* Normals;
            internal Set DirtyPositions, DirtyNormals, Blocks;
            internal int Threshold, FragmentedThreshold, BulkPositions, BulkNormals;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct NoticeContext
        {
            internal int* Indices;
            internal int Count, Error, ValidateValues;
            internal Point* Values;
            internal Set Dirty;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct UpdateContext
        {
            internal Point* Positions;
            internal Point* Normals;
            internal int* Offsets;
            internal RenderGeometryPreparation.Copy* Copies;
            internal Set DirtyPositions, DirtyNormals;
            internal Area* Areas;
            internal Target* Targets;
            internal int AreaCount, Error, SparseRoles;
            internal long SparseWrites, BulkWrites;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct BoundsContext
        {
            internal Point* Positions;
            internal Point* Min;
            internal Point* Max;
            internal byte* Valid;
            internal uint* Nodes;
            internal Set Blocks;
            internal int Count, Leaf, Dense, Validate, Error, Scanned, Reduced;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void NoticeKernel(ref NoticeContext context);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void UpdateKernel(ref UpdateContext context);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void BoundsKernel(ref BoundsContext context);

        private static readonly object s_Lock = new object();
        private static bool s_Initialized;
        private static NoticeKernel s_Notice = Union;
        private static UpdateKernel s_Preflight = Preflight, s_Validate = ValidateCurrent, s_Sparse = CopySparse, s_Bulk = CopyBulk;
        private static BoundsKernel s_Scan = ScanBounds, s_Reduce = ReduceBounds;
        internal static bool UsesBurst { get; private set; }
        internal static string Failure { get; private set; } = string.Empty;

        internal static void Initialize()
        {
            lock (s_Lock)
            {
                if (s_Initialized) return;
                s_Initialized = true;
                try
                {
                    if (BurstCompiler.IsEnabled)
                    {
                        var notice = BurstCompiler.CompileFunctionPointer<NoticeKernel>(Union).Invoke;
                        var preflight = BurstCompiler.CompileFunctionPointer<UpdateKernel>(Preflight).Invoke;
                        var validate = BurstCompiler.CompileFunctionPointer<UpdateKernel>(ValidateCurrent).Invoke;
                        var sparse = BurstCompiler.CompileFunctionPointer<UpdateKernel>(CopySparse).Invoke;
                        var bulk = BurstCompiler.CompileFunctionPointer<UpdateKernel>(CopyBulk).Invoke;
                        var scan = BurstCompiler.CompileFunctionPointer<BoundsKernel>(ScanBounds).Invoke;
                        var reduce = BurstCompiler.CompileFunctionPointer<BoundsKernel>(ReduceBounds).Invoke;
                        s_Notice = notice;
                        s_Preflight = preflight;
                        s_Validate = validate;
                        s_Sparse = sparse;
                        s_Bulk = bulk;
                        s_Scan = scan;
                        s_Reduce = reduce;
                        UsesBurst = true;
                    }
                    else Failure = "Burst is disabled; renderer kernels use their managed entry points.";
                }
                catch (Exception error)
                {
                    Failure = "Renderer Burst initialization failed: " + error.Message;
                }
            }
        }

        internal static void Collect(ref NoticeContext c)
        {
            s_Notice(ref c);
        }

        internal static void Prepare(ref UpdateContext c)
        {
            s_Preflight(ref c);
        }

        internal static void ValidateCurrentValues(ref UpdateContext c)
        {
            s_Validate(ref c);
        }

        internal static void Sparse(ref UpdateContext c)
        {
            s_Sparse(ref c);
        }

        internal static void Bulk(ref UpdateContext c)
        {
            s_Bulk(ref c);
        }

        internal static void Scan(ref BoundsContext c)
        {
            s_Scan(ref c);
        }

        internal static void Reduce(ref BoundsContext c)
        {
            s_Reduce(ref c);
        }

        internal static void Validate(UnityEngine.Vector3[] values)
        {
            if (values == null) return;
            fixed (UnityEngine.Vector3* data = values)
            {
                var context = new NoticeContext { Values = (Point*)data, Count = values.Length, ValidateValues = 1 };
                Collect(ref context);
                if (context.Error != 0) throw new InvalidOperationException("Non-finite renderer geometry.");
            }
        }

        internal static bool Add(ref Set set, int index)
        {
            int word = index >> 5;
            uint bit = 1u << (index & 31);
            if ((set.Bits[word] & bit) != 0) return false;
            if (set.Bits[word] == 0) set.Words[set.WordCount++] = word;
            set.Bits[word] |= bit;
            set.Count++;
            return true;
        }

        internal static int First(uint bits)
        {
            int at = 0;
            if ((bits & 0xffffu) == 0)
            {
                at += 16;
                bits >>= 16;
            }

            if ((bits & 0xffu) == 0)
            {
                at += 8;
                bits >>= 8;
            }

            if ((bits & 0xfu) == 0)
            {
                at += 4;
                bits >>= 4;
            }

            if ((bits & 3u) == 0)
            {
                at += 2;
                bits >>= 2;
            }

            if ((bits & 1u) == 0) at++;
            return at;
        }

        private static bool Finite(float value) => (*(uint*)&value & 0x7f800000u) != 0x7f800000u;
        private static bool Finite(Point value) => Finite(value.X) && Finite(value.Y) && Finite(value.Z);

        [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.Standard)]
        private static void Union(ref NoticeContext c)
        {
            if (c.ValidateValues != 0)
            {
                for (int i = 0; i < c.Count; i++)
                    if (!Finite(c.Values[i]))
                    {
                        c.Error = 1;
                        return;
                    }

                return;
            }

            for (int i = 0; i < c.Count; i++)
            {
                int index = c.Indices[i];
                if ((uint)index >= (uint)c.Dirty.Length)
                {
                    c.Error = 1;
                    return;
                }

                Add(ref c.Dirty, index);
            }
        }

        // Membership may be reused; numerical validation must run every time
        // and complete before any area or combined destination write.
        [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.Standard)]
        private static void ValidateCurrent(ref UpdateContext c)
        {
            for (int role = 0; role < 2; role++)
            {
                Set source = role == 0 ? c.DirtyPositions : c.DirtyNormals;
                Point* values = role == 0 ? c.Positions : c.Normals;
                for (int word = 0; word < source.WordCount; word++)
                {
                    int start = source.Words[word] * 32;
                    uint bits = source.Bits[source.Words[word]];
                    while (bits != 0)
                    {
                        int index = start + First(bits);
                        bits &= bits - 1;
                        if (values == null || !Finite(values[index]))
                        {
                            c.Error = 1;
                            return;
                        }
                    }
                }
            }
        }

        [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.Standard)]
        private static void Preflight(ref UpdateContext c)
        {
            for (int role = 0; role < 2; role++)
            {
                Set source = role == 0 ? c.DirtyPositions : c.DirtyNormals;
                Point* values = role == 0 ? c.Positions : c.Normals;
                for (int word = 0; word < source.WordCount; word++)
                {
                    int start = source.Words[word] * 32;
                    uint bits = source.Bits[source.Words[word]];
                    while (bits != 0)
                    {
                        int index = start + First(bits);
                        bits &= bits - 1;
                        for (int at = c.Offsets[index]; at < c.Offsets[index + 1]; at++)
                        {
                            var copy = c.Copies[at];
                            Area* area = c.Areas + copy.Area;
                            if (role == 1 && area->Normals == null) continue;
                            if (role == 0) area->PositionCount++;
                            else area->NormalCount++;
                            if (area->Generated != 0) continue;
                            Target* target = c.Targets + area->Target;
                            int destination = area->Offset + copy.Vertex;
                            if (role == 0)
                            {
                                Mark(ref target->DirtyPositions, destination, ref target->BulkPositions, target->Threshold, target->FragmentedThreshold);
                                if (target->BulkPositions == 0) Add(ref target->Blocks, destination / RenderBounds.kBlockSize);
                            }
                            else Mark(ref target->DirtyNormals, destination, ref target->BulkNormals, target->Threshold, target->FragmentedThreshold);
                        }
                    }
                }
            }
        }

        private static void Mark(ref Set set, int index, ref int bulk, int threshold, int fragmentedThreshold)
        {
            // CSR mappings contain each area destination exactly once. Source
            // dirtiness is already a union, so counts stay exact after cutoff.
            if (bulk != 0) set.Count++;
            else
            {
                Add(ref set, index);
                if (set.Count >= threshold) bulk = 1;
                else if (set.Count >= fragmentedThreshold && set.WordCount * 2L >= (set.Length + 31L) / 32) bulk = 2;
            }
        }

        [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.Standard)]
        private static void CopySparse(ref UpdateContext c)
        {
            long writes = 0;
            for (int role = 0; role < 2; role++)
            {
                if ((c.SparseRoles & (1 << role)) == 0) continue;
                Set source = role == 0 ? c.DirtyPositions : c.DirtyNormals;
                Point* values = role == 0 ? c.Positions : c.Normals;
                for (int word = 0; word < source.WordCount; word++)
                {
                    int start = source.Words[word] * 32;
                    uint bits = source.Bits[source.Words[word]];
                    while (bits != 0)
                    {
                        int index = start + First(bits);
                        bits &= bits - 1;
                        for (int at = c.Offsets[index]; at < c.Offsets[index + 1]; at++)
                        {
                            var copy = c.Copies[at];
                            Area* area = c.Areas + copy.Area;
                            Target* target = c.Targets + area->Target;
                            Point* local = role == 0 ? area->Positions : area->Normals;
                            if (local == null) continue;
                            if (area->Generated == 0 && (role == 0 ? target->BulkPositions : target->BulkNormals) != 0) continue;
                            Point value = values[index];
                            local[copy.Vertex] = value;
                            writes++;
                            if (area->Generated == 0 && area->Isolated == 0)
                            {
                                Point* output = role == 0 ? target->Positions : target->Normals;
                                output[area->Offset + copy.Vertex] = value;
                                writes++;
                            }
                        }
                    }
                }
            }

            c.SparseWrites = writes;
        }

        [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.Standard)]
        private static void CopyBulk(ref UpdateContext c)
        {
            for (int i = 0; i < c.AreaCount; i++)
            {
                Area* area = c.Areas + i;
                if (area->Generated != 0) continue;
                Target* target = c.Targets + area->Target;
                for (int role = 0; role < 2; role++)
                {
                    if ((role == 0 ? target->BulkPositions : target->BulkNormals) == 0) continue;
                    Point* local = role == 0 ? area->Positions : area->Normals;
                    if (local == null) continue;
                    Point* input = role == 0 ? c.Positions : c.Normals;
                    Point* output = role == 0 ? target->Positions : target->Normals;
                    if (area->Isolated != 0)
                    {
                        for (int vertex = 0; vertex < area->Length; vertex++) local[vertex] = input[area->Map[vertex]];
                        c.BulkWrites += area->Length;
                    }
                    else
                    {
                        int offset = area->Offset;
                        for (int vertex = 0; vertex < area->Length; vertex++)
                        {
                            Point value = input[area->Map[vertex]];
                            local[vertex] = value;
                            output[offset + vertex] = value;
                        }

                        c.BulkWrites += area->Length * 2L;
                    }
                }
            }
        }

        [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.Standard)]
        private static void ScanBounds(ref BoundsContext c)
        {
            if (c.Dense != 0)
            {
                for (int block = 0; block < c.Blocks.Length; block++)
                {
                    ScanBlock(ref c, block);
                    if (c.Error != 0) return;
                }
            }
            else
            {
                for (int word = 0; word < c.Blocks.WordCount; word++)
                {
                    int start = c.Blocks.Words[word] * 32;
                    uint bits = c.Blocks.Bits[c.Blocks.Words[word]];
                    while (bits != 0)
                    {
                        int block = start + First(bits);
                        bits &= bits - 1;
                        ScanBlock(ref c, block);
                        if (c.Error != 0) return;
                        for (int node = (c.Leaf + block) >> 1; node > 0; node >>= 1)
                        {
                            uint bit = 1u << (node & 31);
                            int at = node >> 5;
                            if ((c.Nodes[at] & bit) != 0) break;
                            c.Nodes[at] |= bit;
                        }
                    }
                }
            }
        }

        private static void ScanBlock(ref BoundsContext c, int block)
        {
            int start = block * RenderBounds.kBlockSize;
            int end = start + RenderBounds.kBlockSize;
            if (end > c.Count) end = c.Count;
            Point* positions = c.Positions;
            if (c.Validate != 0)
            {
                for (int i = start; i < end; i++)
                    if (!Finite(positions[i]))
                    {
                        c.Error = 1;
                        return;
                    }
            }

            Point first = positions[start];
            float minX = first.X, minY = first.Y, minZ = first.Z;
            float maxX = minX, maxY = minY, maxZ = minZ;
            for (int i = start + 1; i < end; i++)
            {
                Point v = positions[i];
                if (v.X < minX) minX = v.X;
                if (v.Y < minY) minY = v.Y;
                if (v.Z < minZ) minZ = v.Z;
                if (v.X > maxX) maxX = v.X;
                if (v.Y > maxY) maxY = v.Y;
                if (v.Z > maxZ) maxZ = v.Z;
            }

            int leaf = c.Leaf + block;
            c.Min[leaf] = new Point { X = minX, Y = minY, Z = minZ };
            c.Max[leaf] = new Point { X = maxX, Y = maxY, Z = maxZ };
            c.Valid[leaf] = 1;
            c.Scanned += end - start;
        }

        private static Point Min(Point a, Point b) => new Point { X = a.X < b.X ? a.X : b.X, Y = a.Y < b.Y ? a.Y : b.Y, Z = a.Z < b.Z ? a.Z : b.Z };
        private static Point Max(Point a, Point b) => new Point { X = a.X > b.X ? a.X : b.X, Y = a.Y > b.Y ? a.Y : b.Y, Z = a.Z > b.Z ? a.Z : b.Z };

        private static void Join(ref BoundsContext c, int node)
        {
            int left = node * 2, right = left + 1;
            c.Valid[node] = (byte)(c.Valid[left] | c.Valid[right]);
            if (c.Valid[left] == 0)
            {
                c.Min[node] = c.Min[right];
                c.Max[node] = c.Max[right];
            }
            else if (c.Valid[right] == 0)
            {
                c.Min[node] = c.Min[left];
                c.Max[node] = c.Max[left];
            }
            else
            {
                c.Min[node] = Min(c.Min[left], c.Min[right]);
                c.Max[node] = Max(c.Max[left], c.Max[right]);
            }

            c.Reduced++;
        }

        [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.Standard)]
        private static void ReduceBounds(ref BoundsContext c)
        {
            if (c.Dense != 0)
            {
                for (int node = c.Leaf - 1; node > 0; node--) Join(ref c, node);
                for (int word = 0; word < (c.Leaf + 31) / 32; word++) c.Nodes[word] = 0;
            }
            else
            {
                // Heap children have larger indices. Descending words/bits give
                // a child-first order without sorting or rejoining ancestors.
                for (int word = (c.Leaf - 1) >> 5; word >= 0; word--)
                {
                    uint bits = c.Nodes[word];
                    c.Nodes[word] = 0;
                    if (bits == 0) continue;
                    for (int bit = 31; bit >= 0; bit--)
                        if ((bits & (1u << bit)) != 0)
                            Join(ref c, word * 32 + bit);
                }
            }
        }
    }
}