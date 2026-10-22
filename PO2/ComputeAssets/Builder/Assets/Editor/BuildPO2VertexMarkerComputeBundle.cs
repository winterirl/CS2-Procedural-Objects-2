using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class BuildPO2VertexMarkerComputeBundle {
    private const string ComputeAssetPath =
        "Assets/PO2VertexMarkerInstances.compute";
    private const string SolidShaderPath = "Assets/PO2Solid.shader";
    private const string SolidComputePath = "Assets/PO2Solid.compute";
    private const string BundleName =
        "po2_vertex_marker_compute.bundle";
    private const string FingerprintSuffix = ".sha256";

    public static void Build() {
        string outputDirectory = Environment.GetEnvironmentVariable(
            "PO2_COMPUTE_BUNDLE_OUTPUT");

        if (string.IsNullOrWhiteSpace(outputDirectory)) {
            throw new InvalidOperationException(
                "PO2_COMPUTE_BUNDLE_OUTPUT was not provided.");
        }

        outputDirectory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(outputDirectory);

        ComputeShader computeShader =
            AssetDatabase.LoadAssetAtPath<ComputeShader>(ComputeAssetPath);
        if (computeShader == null) {
            throw new InvalidOperationException(
                $"Unity could not import compute shader '{ComputeAssetPath}'.");
        }

        var solidCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>(SolidComputePath);
        var solidShader = AssetDatabase.LoadAssetAtPath<Shader>(SolidShaderPath);
        if (solidCompute == null || solidShader == null) throw new InvalidOperationException("Solid GPU assets could not be imported.");
        CheckComputeErrors(computeShader, ComputeAssetPath);
        CheckComputeErrors(solidCompute, SolidComputePath);
        CheckShaderErrors(solidShader, SolidShaderPath);

        Debug.Log(
            $"PO2 mesh-edit compute asset imported: {computeShader.name}; " +
            $"path={AssetDatabase.GetAssetPath(computeShader)}, " +
            $"graphicsDevice={SystemInfo.graphicsDeviceType}, " +
            $"supportsComputeShaders={SystemInfo.supportsComputeShaders}. " +
            "Kernel resolution is intentionally deferred to the CS2 runtime.");

        AssetBundleBuild build = new AssetBundleBuild {
            assetBundleName = BundleName,
            assetNames = new[] { ComputeAssetPath, SolidComputePath, SolidShaderPath },
        };

        AssetBundleManifest manifest = BuildPipeline.BuildAssetBundles(
            outputDirectory,
            new[] { build },
            BuildAssetBundleOptions.ForceRebuildAssetBundle |
                BuildAssetBundleOptions.ChunkBasedCompression,
            BuildTarget.StandaloneWindows64);

        if (manifest == null) {
            throw new InvalidOperationException(
                "Unity did not produce the PO2 mesh-edit compute " +
                "AssetBundle.");
        }
        // A bundle can be produced even when one of its shader programs failed.
        // Do not mark that build as successful and send it into the game.
        CheckComputeErrors(computeShader, ComputeAssetPath);
        CheckComputeErrors(solidCompute, SolidComputePath);
        CheckShaderErrors(solidShader, SolidShaderPath);

        string bundlePath = Path.Combine(outputDirectory, BundleName);
        if (!File.Exists(bundlePath)) {
            throw new FileNotFoundException(
                "Expected PO2 mesh-edit compute AssetBundle was not created.",
                bundlePath);
        }

        string fingerprint = Environment.GetEnvironmentVariable(
            "PO2_COMPUTE_BUILD_FINGERPRINT");
        if (string.IsNullOrWhiteSpace(fingerprint)) {
            throw new InvalidOperationException(
                "PO2_COMPUTE_BUILD_FINGERPRINT was not provided.");
        }

        File.WriteAllText(
            Path.Combine(outputDirectory, BundleName + FingerprintSuffix),
            fingerprint.Trim());

        Debug.Log(
            $"PO2 mesh-edit compute bundle built: {bundlePath}; " +
            $"fingerprint={fingerprint.Trim()}");
    }

    private static void CheckComputeErrors(ComputeShader shader, string path) {
        foreach (var message in ShaderUtil.GetComputeShaderMessages(shader)) {
            if (message.severity.ToString() == "Error") {
                throw new InvalidOperationException($"Shader compilation failed: {path}: {message.message}");
            }
        }
    }

    private static void CheckShaderErrors(Shader shader, string path) {
        foreach (var message in ShaderUtil.GetShaderMessages(shader)) {
            if (message.severity.ToString() == "Error") {
                throw new InvalidOperationException($"Shader compilation failed: {path}: {message.message}");
            }
        }
    }
}
