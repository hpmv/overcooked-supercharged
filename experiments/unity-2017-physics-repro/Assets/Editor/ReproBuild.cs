using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

public static class ReproBuild
{
    public static void BuildWindows32()
    {
        string output = Argument("--buildPath") ?? Argument("-buildPath");
        if (String.IsNullOrEmpty(output))
            throw new ArgumentException("Pass --buildPath with an absolute .exe path.");
        output = Path.GetFullPath(output);
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        const string scenePath = "Assets/ReproScene.unity";
        if (!EditorSceneManager.SaveScene(scene, scenePath))
            throw new InvalidOperationException("Could not save the minimal reproduction scene.");
        string error = BuildPipeline.BuildPlayer(new[] { scenePath }, output,
            BuildTarget.StandaloneWindows, BuildOptions.Development);
        if (!String.IsNullOrEmpty(error))
            throw new InvalidOperationException("Unity player build failed: " + error);
    }

    private static string Argument(string name)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i + 1 < args.Length; i++)
            if (args[i] == name) return args[i + 1];
        return null;
    }
}
