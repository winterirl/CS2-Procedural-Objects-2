using System;
using System.IO;
using System.Runtime.InteropServices;

namespace PO2.Rendering.Lod.Native
{
    [Flags]
    internal enum MeshOptimizerSimplifyOptions : uint
    {
        None = 0,
        LockBorder = 1 << 0,
        Sparse = 1 << 1,
        ErrorAbsolute = 1 << 2,
        Prune = 1 << 3,
        Regularize = 1 << 4,
        Permissive = 1 << 5,
        RegularizeLight = 1 << 6,
    }

    internal enum MeshOptimizerStatus : uint
    {
        Success = 0,
        InvalidArgument = 1,
        SizeOverflow = 2,
        AllocationFailed = 3,
        InternalError = 4,
        InvalidOutput = 5,
    }

    internal sealed class MeshOptimizerNative
    {
        internal const uint kExpectedAbiVersion = 3;
        internal const uint kExpectedMeshOptimizerVersion = 1020;
        internal const int kMaxAttributeCount = 32;

        private const string kLibraryFileName = "po2_meshoptimizer.dll";

        private readonly IntPtr m_LibraryHandle;
        private readonly SimplifyDelegate m_Simplify;
        private readonly SimplifyPositionFocusedDelegate m_SimplifyPositionFocused;
        private readonly SimplifyAggressiveDelegate m_SimplifyAggressive;
        private readonly OptimizeVertexCacheDelegate m_OptimizeVertexCache;

        private MeshOptimizerNative(IntPtr libraryHandle, SimplifyDelegate simplify, SimplifyPositionFocusedDelegate simplifyPositionFocused, SimplifyAggressiveDelegate simplifyAggressive, OptimizeVertexCacheDelegate optimizeVertexCache)
        {
            m_LibraryHandle = libraryHandle;
            m_Simplify = simplify;
            m_SimplifyPositionFocused = simplifyPositionFocused;
            m_SimplifyAggressive = simplifyAggressive;
            m_OptimizeVertexCache = optimizeVertexCache;
        }

        // This stays loaded for the process lifetime. Unloading while the LOD
        // worker is inside native code would be a much worse failure mode than
        // retaining one tiny module until the game exits.
        internal IntPtr LibraryHandle => m_LibraryHandle;

        internal static bool TryLoad(string modRootPath, out MeshOptimizerNative native, out string failureReason)
        {
            native = null;
            failureReason = string.Empty;

            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
            {
                failureReason = "PO2 runtime LOD generation currently supports the " + "Windows game build only.";
                return false;
            }

            if (!Environment.Is64BitProcess)
            {
                failureReason = "PO2 runtime LOD generation requires a 64-bit process.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(modRootPath))
            {
                failureReason = "The PO2 installation path is unavailable.";
                return false;
            }

            string libraryPath;
            try
            {
                libraryPath = Path.GetFullPath(Path.Combine(modRootPath, "Native", "win-x64", kLibraryFileName));
            }
            catch (Exception exception)
            {
                failureReason = "The PO2 meshoptimizer path is invalid: " + exception.Message;
                return false;
            }

            if (!File.Exists(libraryPath))
            {
                failureReason = "The PO2 meshoptimizer library is missing at '" + libraryPath + "'.";
                return false;
            }

            IntPtr libraryHandle = LoadLibraryW(libraryPath);
            if (libraryHandle == IntPtr.Zero)
            {
                failureReason = "Windows could not load the PO2 meshoptimizer library " + "at '" + libraryPath + "' (error " + Marshal.GetLastWin32Error() + ").";
                return false;
            }

            try
            {
                if (!TryResolve(libraryHandle, "po2_meshopt_get_abi_version", out GetVersionDelegate getAbiVersion, out failureReason) || !TryResolve(libraryHandle, "po2_meshopt_get_meshoptimizer_version", out GetVersionDelegate getMeshOptimizerVersion, out failureReason) || !TryResolve(libraryHandle, "po2_meshopt_simplify", out SimplifyDelegate simplify, out failureReason) || !TryResolve(libraryHandle, "po2_meshopt_simplify_position_focused", out SimplifyPositionFocusedDelegate simplifyPositionFocused, out failureReason) || !TryResolve(libraryHandle, "po2_meshopt_simplify_aggressive", out SimplifyAggressiveDelegate simplifyAggressive, out failureReason) || !TryResolve(libraryHandle, "po2_meshopt_optimize_vertex_cache", out OptimizeVertexCacheDelegate optimizeVertexCache, out failureReason))
                {
                    return false;
                }

                uint abiVersion = getAbiVersion();
                if (abiVersion != kExpectedAbiVersion)
                {
                    failureReason = "The PO2 meshoptimizer ABI is incompatible. " + "Expected " + kExpectedAbiVersion + ", found " + abiVersion + ".";
                    return false;
                }

                uint meshOptimizerVersion = getMeshOptimizerVersion();
                if (meshOptimizerVersion != kExpectedMeshOptimizerVersion)
                {
                    failureReason = "The PO2 meshoptimizer source version is " + "incompatible. Expected " + kExpectedMeshOptimizerVersion + ", found " + meshOptimizerVersion + ".";
                    return false;
                }

                native = new MeshOptimizerNative(libraryHandle, simplify, simplifyPositionFocused, simplifyAggressive, optimizeVertexCache);
                libraryHandle = IntPtr.Zero;
                return true;
            }
            catch (Exception exception)
            {
                failureReason = "The PO2 meshoptimizer compatibility check failed: " + exception.Message;
                return false;
            }
            finally
            {
                if (libraryHandle != IntPtr.Zero)
                {
                    FreeLibrary(libraryHandle);
                }
            }
        }

        internal MeshOptimizerStatus Simplify(IntPtr destination, ulong destinationCapacity, IntPtr indices, ulong indexCount, IntPtr vertexPositions, ulong vertexCount, ulong vertexPositionsStride, IntPtr vertexAttributes, ulong vertexAttributesStride, IntPtr attributeWeights, uint attributeCount, IntPtr vertexLock, ulong targetIndexCount, float targetError, MeshOptimizerSimplifyOptions options, out ulong resultIndexCount, out float resultError)
        {
            return m_Simplify(destination, destinationCapacity, indices, indexCount, vertexPositions, vertexCount, vertexPositionsStride, vertexAttributes, vertexAttributesStride, attributeWeights, attributeCount, vertexLock, targetIndexCount, targetError, (uint)options, out resultIndexCount, out resultError);
        }

        internal MeshOptimizerStatus OptimizeVertexCache(IntPtr destination, ulong destinationCapacity, IntPtr indices, ulong indexCount, ulong vertexCount)
        {
            return m_OptimizeVertexCache(destination, destinationCapacity, indices, indexCount, vertexCount);
        }

        internal MeshOptimizerStatus SimplifyPositionFocused(IntPtr destination, ulong destinationCapacity, IntPtr indices, ulong indexCount, IntPtr vertexPositions, ulong vertexCount, ulong vertexPositionsStride, ulong targetIndexCount, float targetError, out ulong resultIndexCount, out float resultError)
        {
            return m_SimplifyPositionFocused(destination, destinationCapacity, indices, indexCount, vertexPositions, vertexCount, vertexPositionsStride, targetIndexCount, targetError, out resultIndexCount, out resultError);
        }

        internal MeshOptimizerStatus SimplifyAggressive(IntPtr destination, ulong destinationCapacity, IntPtr indices, ulong indexCount, IntPtr vertexPositions, ulong vertexCount, ulong vertexPositionsStride, ulong targetIndexCount, float targetError, float pruneError, out ulong prunedIndexCount, out ulong resultIndexCount, out float resultError)
        {
            return m_SimplifyAggressive(destination, destinationCapacity, indices, indexCount, vertexPositions, vertexCount, vertexPositionsStride, targetIndexCount, targetError, pruneError, out prunedIndexCount, out resultIndexCount, out resultError);
        }

        internal static string GetStatusName(MeshOptimizerStatus status)
        {
            switch (status)
            {
                case MeshOptimizerStatus.Success:
                    return "success";
                case MeshOptimizerStatus.InvalidArgument:
                    return "invalid argument";
                case MeshOptimizerStatus.SizeOverflow:
                    return "size overflow";
                case MeshOptimizerStatus.AllocationFailed:
                    return "allocation failed";
                case MeshOptimizerStatus.InternalError:
                    return "internal error";
                case MeshOptimizerStatus.InvalidOutput:
                    return "invalid output";
                default:
                    return "unknown status " + (uint)status;
            }
        }

        private static bool TryResolve<TDelegate>(IntPtr libraryHandle, string exportName, out TDelegate function, out string failureReason) where TDelegate : class
        {
            function = null;
            failureReason = string.Empty;

            IntPtr address = GetProcAddress(libraryHandle, exportName);
            if (address == IntPtr.Zero)
            {
                failureReason = "The PO2 meshoptimizer library is missing export '" + exportName + "'.";
                return false;
            }

            function = Marshal.GetDelegateForFunctionPointer(address, typeof(TDelegate)) as TDelegate;
            if (function == null)
            {
                failureReason = "The PO2 meshoptimizer export '" + exportName + "' has an incompatible signature.";
                return false;
            }

            return true;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate uint GetVersionDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate MeshOptimizerStatus SimplifyDelegate(IntPtr destination, ulong destinationCapacity, IntPtr indices, ulong indexCount, IntPtr vertexPositions, ulong vertexCount, ulong vertexPositionsStride, IntPtr vertexAttributes, ulong vertexAttributesStride, IntPtr attributeWeights, uint attributeCount, IntPtr vertexLock, ulong targetIndexCount, float targetError, uint options, out ulong resultIndexCount, out float resultError);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate MeshOptimizerStatus SimplifyPositionFocusedDelegate(IntPtr destination, ulong destinationCapacity, IntPtr indices, ulong indexCount, IntPtr vertexPositions, ulong vertexCount, ulong vertexPositionsStride, ulong targetIndexCount, float targetError, out ulong resultIndexCount, out float resultError);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate MeshOptimizerStatus SimplifyAggressiveDelegate(IntPtr destination, ulong destinationCapacity, IntPtr indices, ulong indexCount, IntPtr vertexPositions, ulong vertexCount, ulong vertexPositionsStride, ulong targetIndexCount, float targetError, float pruneError, out ulong prunedIndexCount, out ulong resultIndexCount, out float resultError);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate MeshOptimizerStatus OptimizeVertexCacheDelegate(IntPtr destination, ulong destinationCapacity, IntPtr indices, ulong indexCount, ulong vertexCount);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryW(string fileName);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr module, string procedureName);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FreeLibrary(IntPtr module);
    }
}