using UnityEngine;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using System.IO;
using System.Diagnostics;
using System.Threading.Tasks;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

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
            string dest = CroquetBuilder.NodeExeInBuild;
            string destDir = Path.GetDirectoryName(dest);
            Directory.CreateDirectory(destDir);
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
        // PlayModeStateChange.ExitingEditMode (i.e., entering Play) is handled above in the constructor
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            CroquetBuilder.EnteredEditMode();
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
            string dest = CroquetBuilder.NodeExeInBuild;
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

    private const string InstallJSToolsItem = "Croquet/Install JS Build Tools";

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

    [MenuItem(InstallJSToolsItem, false, 200)]
    private static void InstallJSTools()
    {
        string nodePath = "";
#if UNITY_EDITOR_OSX
        string nodeExecutable = CroquetBuilder.GetSceneBuildDetails().nodeExecutable;
        if (string.IsNullOrWhiteSpace(nodeExecutable) || !File.Exists(nodeExecutable))
        {
            Debug.LogError("Cannot find Node executable; did you remember to set the path in the Settings object?");
            return;
        }
        nodePath = Path.GetDirectoryName(nodeExecutable);
#endif

        // copy the various files
        string toolsRoot = CroquetBuilder.CroquetBuildToolsInPackage;
        string unityParentFolder = Path.GetFullPath(Path.Combine(Application.streamingAssetsPath, "..", "..", ".."));
        string jsFolder = Path.GetFullPath(Path.Combine(Application.streamingAssetsPath, "..", "CroquetJS"));
        if (!Directory.Exists(jsFolder)) Directory.CreateDirectory(jsFolder);

        // package.json and .eslintrc to parent of the entire Unity project
        string[] files = new string[] { "package.json", ".eslintrc.json" };
        foreach (var file in files)
        {
            string fsrc = Path.GetFullPath(Path.Combine(toolsRoot, file));
            string fdest = Path.GetFullPath(Path.Combine(unityParentFolder, file));
            Debug.Log($"writing {fdest}"); // with {fsrc}");
            FileUtil.ReplaceFile(fsrc, fdest);
        }

        // build-tools to Assets/CroquetJS/
        string dir = "build-tools";
        string dsrc = Path.GetFullPath(Path.Combine(toolsRoot, dir));
        string ddest = Path.GetFullPath(Path.Combine(jsFolder, dir));
        Debug.Log($"writing directory {ddest}"); // with {dsrc}");
        FileUtil.ReplaceDirectory(dsrc, ddest);

        // now get ready to start the npm install.
        // introducing even a minimal delay gives the console a chance to show the above messages
        Debug.Log("Installing JavaScript Build Tools...");
        if (Application.platform == RuntimePlatform.OSXEditor)
        {
            Task.Delay(1).ContinueWith(t => InstallOSX(unityParentFolder, toolsRoot, nodePath));
        }
        else
        {
            Task.Delay(1).ContinueWith(t => InstallWin(unityParentFolder, toolsRoot));
        }
    }

    private static void InstallOSX(string installDir, string toolsRoot, string nodePath) {
        string scriptPath = Path.GetFullPath(Path.Combine(toolsRoot, "runNPM.sh"));
        Process p = new Process();
        p.StartInfo.UseShellExecute = false;
        p.StartInfo.FileName = scriptPath;
        p.StartInfo.Arguments = nodePath;
        p.StartInfo.WorkingDirectory = installDir;

        p.StartInfo.RedirectStandardOutput = true;
        p.StartInfo.RedirectStandardError = true;

        p.Start();

        string output = p.StandardOutput.ReadToEnd();
        string errors = p.StandardError.ReadToEnd();

        p.WaitForExit();

        CroquetBuilder.LogProcessOutput(output, errors, "npm install");
    }

    private static void InstallWin(string installDir, string toolsRoot)
    {
        string scriptPath = Path.GetFullPath(Path.Combine(toolsRoot, "runNPM.ps1"));
        string stdoutFile = Path.GetTempFileName();
        string stderrFile = Path.GetTempFileName();
        Process p = new Process();
        p.StartInfo.UseShellExecute = true;
        p.StartInfo.FileName = "powershell.exe";
        p.StartInfo.Arguments = $"-NoProfile -file \"{scriptPath}\" \"{stdoutFile}\" \"{stderrFile}\" ";
        p.StartInfo.WorkingDirectory = installDir;
        p.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;

        p.Start();
        p.WaitForExit();

        string output = File.ReadAllText(stdoutFile);
        File.Delete(stdoutFile);
        string errors = File.ReadAllText(stderrFile);
        File.Delete(stderrFile);
        CroquetBuilder.LogProcessOutput(output, errors, "npm install");
    }

    [MenuItem(InstallJSToolsItem, true)]
    private static bool ValidateInstallJSTools()
    {
        return CroquetBuilder.KnowHowToBuildJS();
    }
}
