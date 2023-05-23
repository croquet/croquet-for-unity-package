using UnityEngine;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using System.IO;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

class CroquetBuildPreprocess : IPreprocessBuildWithReport
{
    public int callbackOrder { get { return 0; } }
    public void OnPreprocessBuild(BuildReport report)
    {
        // for Windows standalone, we temporarily place a copy of node.exe
        // in the StreamingAssets folder for inclusion in the build.
        BuildTarget target = report.summary.platform;
        if (target == BuildTarget.StandaloneWindows || target == BuildTarget.StandaloneWindows64)
        {
            string src = CroquetBuilder.NodeExeInPackage;
            string dest = "Assets/StreamingAssets/croquet-bridge/node/node.exe";
            FileUtil.CopyFileOrDirectory(src, dest);
        }
    }
}

[InitializeOnLoad]
public static class SceneAndPlayWatcher
{
    // register event handlers when the class is initialized
    static SceneAndPlayWatcher()
    {
        // because this is rebuilt on Play, it turns out that we miss the ExitingEditMode event.
        // but we can detect whether the init is happening because of an imminent state change
        // https://gamedev.stackexchange.com/questions/157266/unity-why-does-playmodestatechanged-get-called-after-start
        EditorApplication.playModeStateChanged += HandlePlayModeState;
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            CroquetBuilder.EnteringPlayMode();
        }

        EditorSceneManager.activeSceneChangedInEditMode += HandleSceneChange;
    }

    private static void HandlePlayModeState(PlayModeStateChange state)
    {
        //if (state == PlayModeStateChange.ExitingEditMode)
        //{
        //    CroquetBuilder.EnteringPlayMode();
        //}
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            CroquetBuilder.EnteredPlayMode();
        }
    }

    private static void HandleSceneChange(Scene current, Scene next)
    {
        CroquetBuilder.CacheSceneComponents(next);
    }
}

class CroquetBuildPostprocess : IPostprocessBuildWithReport
{
    public int callbackOrder { get { return 0; } }
    public void OnPostprocessBuild(BuildReport report)
    {
        // if we temporarily copied node.exe (see above), remove it again
        BuildTarget target = report.summary.platform;
        if (target == BuildTarget.StandaloneWindows || target == BuildTarget.StandaloneWindows64)
        {
            string dest = "Assets/StreamingAssets/croquet-bridge/node/node.exe";
            FileUtil.DeleteFileOrDirectory(dest);
            FileUtil.DeleteFileOrDirectory(dest + ".meta");
        }
    }
}



public class CroquetMenu
{
    private const string BuildNowItem = "Croquet/Build JS Now";
    private const string BuildOnPlayItem = "Croquet/Build JS on Play";

    private const string StarterItem = "Croquet/Start JS Watcher";
    private const string StopperItemHere = "Croquet/Stop JS Watcher (this scene)";
    private const string StopperItemOther = "Croquet/Stop JS Watcher (other scene)";

    private const string CopyJSItem = "Croquet/Copy JS Build Tools";

    [MenuItem(BuildNowItem, false, 100)]
    private static void BuildNow()
    {
        CroquetBuilder.StartBuild(false); // false => no watcher
    }

    [MenuItem(BuildNowItem, true)]
    private static bool ValidateBuildNow()
    {
        // this item is not available if either
        //   we don't know how to build for the current scene, or
        //   a watcher for any scene is running (MacOS only), or
        //   a build has been requested and hasn't finished yet 
        if (!CroquetBuilder.KnowHowToBuildJS()) return false;
        
#if !UNITY_EDITOR_WIN
        if (CroquetBuilder.RunningWatcherApp() != "") return false;
#endif
        if (CroquetBuilder.oneTimeBuildProcess != null) return false;
        return true;
    }

    [MenuItem(BuildOnPlayItem, false, 100)]
    private static void BuildOnPlayToggle()
    {
        CroquetBuilder.BuildOnPlayEnabled = !CroquetBuilder.BuildOnPlayEnabled;
    }

    [MenuItem(BuildOnPlayItem, true)]
    private static bool ValidateBuildOnPlayToggle()
    {
        if (!CroquetBuilder.KnowHowToBuildJS()) return false;

        Menu.SetChecked(BuildOnPlayItem, CroquetBuilder.BuildOnPlayEnabled);
        return true;
    }

#if !UNITY_EDITOR_WIN
    [MenuItem(StarterItem, false, 100)]
    private static void StartWatcher()
    {
        CroquetBuilder.StartBuild(true); // true => start watcher
    }

    [MenuItem(StarterItem, true)]
    private static bool ValidateStartWatcher()
    {
        if (!CroquetBuilder.KnowHowToBuildJS()) return false;

        // Debug.Log($"CroquetBuilder has process: {CroquetBuilder.builderProcess != null}");
        return CroquetBuilder.RunningWatcherApp() == "";
    }

    [MenuItem(StopperItemHere, false, 100)]
    private static void StopWatcherHere()
    {
        CroquetBuilder.StopWatcher();
    }

    [MenuItem(StopperItemHere, true)]
    private static bool ValidateStopWatcherHere()
    {
        if (!CroquetBuilder.KnowHowToBuildJS()) return false;

        return CroquetBuilder.RunningWatcherApp() == CroquetBuilder.GetSceneBuildDetails().appName;
    }

    [MenuItem(StopperItemOther, false, 100)]
    private static void StopWatcherOther()
    {
        CroquetBuilder.StopWatcher();
    }

    [MenuItem(StopperItemOther, true)]
    private static bool ValidateStopWatcherOther()
    {
        if (!CroquetBuilder.KnowHowToBuildJS()) return false;

        string appName = CroquetBuilder.RunningWatcherApp();
        return appName != "" && appName != CroquetBuilder.GetSceneBuildDetails().appName;
    }
#endif
    
    
    [MenuItem(CopyJSItem, false, 200)]
    private static void CopyJS()
    {
        string toolsRoot = CroquetBuilder.CroquetBuildToolsInPackage;
        string unityParentFolder = Path.GetFullPath(Path.Combine(Application.streamingAssetsPath, "..", "..", ".."));
        string jsFolder = Path.GetFullPath(Path.Combine(Application.streamingAssetsPath, "..", "CroquetJS"));

        // package.json and .eslintrc to parent of the entire Unity project
        string[] files = new string[] { "package.json", ".eslintrc.json" };
        foreach (var file in files)
        {
            string fsrc = Path.GetFullPath(Path.Combine(toolsRoot, file));
            string fdest = Path.GetFullPath(Path.Combine(unityParentFolder, file));
            Debug.Log($"replacing {fdest} with {fsrc}");
            FileUtil.ReplaceFile(fsrc, fdest);
        }

        // build-tools to Assets/CroquetJS/
        string dir = "build-tools";
        string dsrc = Path.GetFullPath(Path.Combine(toolsRoot, dir));
        string ddest = Path.GetFullPath(Path.Combine(jsFolder, dir));
        Debug.Log($"replacing {ddest} with {dsrc}");
        FileUtil.ReplaceDirectory(dsrc, ddest);
    }
}
