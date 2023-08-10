using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.IO;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

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
        // if (EditorApplication.isPlayingOrWillChangePlaymode)
        // {
        //     CroquetBuilder.EnteringPlayMode();
        // }

        EditorSceneManager.activeSceneChangedInEditMode += HandleSceneChange;

        EditorApplication.quitting += EditorQuitting;
    }

    private static void HandlePlayModeState(PlayModeStateChange state)
    {
        // PlayModeStateChange.ExitingEditMode (i.e., before entering Play) - if needed - is handled above in the constructor
        if (state == PlayModeStateChange.EnteredEditMode) CroquetBuilder.EnteredEditMode();
    }

    private static void HandleSceneChange(Scene current, Scene next)
    {
        CroquetBuilder.CacheSceneComponents(next);
    }

    private static void EditorQuitting()
    {
#if UNITY_EDITOR_OSX
        CroquetBuilder.StopWatcher(); // if any
#endif
    }
}


public class CroquetMenu
{
    private const string BuildNowItem = "Croquet/Build JS Now";
    private const string HarvestDefinitionsItem = "Croquet/Harvest Scene Definitions Now";
    private const string BuildOnPlayItem = "Croquet/Build JS on Play";

    private const string StarterItem = "Croquet/Start JS Watcher";
    private const string StopperItemHere = "Croquet/Stop JS Watcher (this app)";
    private const string StopperItemOther = "Croquet/Stop JS Watcher (other app)";

    private const string InstallJSToolsItem = "Croquet/Install JS Build Tools";

    [MenuItem(BuildNowItem, false, 100)]
    private static void BuildNow()
    {
        CroquetBuilder.StartBuild(false); // false => no watcher
    }

    [MenuItem(BuildNowItem, true)]
    private static bool ValidateBuildNow()
    {
        // this item is not available if
        //   we don't know how to build for the current scene, or
        //   a watcher for any scene is running (MacOS only), or
        //   a build has been requested and hasn't finished yet
        if (!CroquetBuilder.KnowHowToBuildJS()) return false;

#if !UNITY_EDITOR_WIN
        if (CroquetBuilder.RunningWatcherApp() == CroquetBuilder.GetSceneBuildDetails().appName) return false;
#endif
        if (CroquetBuilder.oneTimeBuildProcess != null) return false;
        return true;
    }

    [MenuItem(HarvestDefinitionsItem, false, 100)]
    private static void HarvestNow()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        // before entering play mode, go through all scenes that will be included in a build and
        // make a list of the scenes and the app associated with each scene.
        // store the list in an EditorPref using the format
        //   scene1:appName1,scene2:appName2...
        List<string> scenesAndApps = new List<string>();
        Scene activeScene = EditorSceneManager.GetActiveScene();
        string previousScenePath = activeScene.path;
        foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
        {
            if (scene.enabled)
            {
                EditorSceneManager.OpenScene(scene.path);
                CroquetBridge[] allObjects = Resources.FindObjectsOfTypeAll<CroquetBridge>();
                foreach (CroquetBridge obj in allObjects)
                {
                    // the collection will contain components from the scene and from any known prefab.
                    // filter out the latter.
                    if (string.IsNullOrEmpty(obj.gameObject.scene.name)) continue; // prefab
                    if (obj.launchThroughMenu || string.IsNullOrEmpty(obj.appName)) continue; // not relevant

                    string sceneName = Path.GetFileNameWithoutExtension(scene.path);
                    scenesAndApps.Add($"{sceneName}:{obj.appName}");
                }
            }
        }
        // return to the scene where we started
        EditorSceneManager.OpenScene(previousScenePath);

        if (scenesAndApps.Count == 0)
        {
            Debug.LogError("Found no scenes to harvest from.  Do all your scenes have a Croquet object that specifies its associated App Name?");
            CroquetBuilder.HarvestSceneList = "";        }
        else
        {
            string harvestString = string.Join(',', scenesAndApps.ToArray());
            CroquetBuilder.HarvestSceneList = harvestString;
            EditorApplication.EnterPlaymode();
        }
    }

    [MenuItem(HarvestDefinitionsItem, true)]
    private static bool ValidateHarvestNow()
    {
        if (!CroquetBuilder.KnowHowToBuildJS() || !CroquetBuilder.BuildOnPlayEnabled) return false;
#if !UNITY_EDITOR_WIN
        if (CroquetBuilder.RunningWatcherApp() == CroquetBuilder.GetSceneBuildDetails().appName) return false;
#endif

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
#if !UNITY_EDITOR_WIN
        if (CroquetBuilder.RunningWatcherApp() == CroquetBuilder.GetSceneBuildDetails().appName) return false;
#endif

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
    private static async void InstallJSTools()
    {
        await CroquetBuilder.InstallJSTools();
        Debug.Log("InstallJSTools finished");
    }

    [MenuItem(InstallJSToolsItem, true)]
    private static bool ValidateInstallJSTools()
    {
#if !UNITY_EDITOR_WIN
        if (CroquetBuilder.RunningWatcherApp() != "") return false;
#endif
        return true;
    }
}


class CroquetBuildPreprocess : IPreprocessBuildWithReport
{
    public int callbackOrder { get { return 0; } }
    public void OnPreprocessBuild(BuildReport report)
    {
        BuildTarget target = report.summary.platform;
        bool isWindowsBuild = target == BuildTarget.StandaloneWindows || target == BuildTarget.StandaloneWindows64;
        string jsTarget = isWindowsBuild ? "node" : "web";

        Scene activeScene = EditorSceneManager.GetActiveScene();
        if (!CroquetBuilder.PrepareSceneForBuildTarget(activeScene, jsTarget))
        {
            // reason for refusal will already have been logged
            throw new BuildFailedException("You must fix some settings (see warnings above) before building");
        }

        bool readyToBuild = true;
        string failureMessage = "Missing JS build tools";
        string state = CroquetBuilder.StateOfJSBuildTools(); // ok, needsRefresh, needsInstall, unavailable
        if (state == "unavailable") readyToBuild = false; // explanatory error will already have been logged
        else if (state == "needsInstall")
        {
            Debug.LogError("No JS build tools found.  Use Croquet => Install JS Build Tools to install");
            readyToBuild = false;
        }

        if (readyToBuild) // ok so far
        {
            // find all the appNames that are going into the build
            HashSet<string> appNames = new HashSet<string>();
            string previousScenePath = activeScene.path;
            foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
            {
                if (scene.enabled)
                {
                    EditorSceneManager.OpenScene(scene.path);
                    CroquetBridge[] allObjects = Resources.FindObjectsOfTypeAll<CroquetBridge>();
                    foreach (CroquetBridge obj in allObjects)
                    {
                        // the collection will contain components from the scene and from all known prefabs.
                        // filter out the latter.
                        if (string.IsNullOrEmpty(obj.gameObject.scene.name)) continue;

                        if (obj.gameObject.activeSelf && !String.IsNullOrEmpty(obj.appName)) appNames.Add(obj.appName);
                    }
                }
            }
            // put it back to the scene where we started
            EditorSceneManager.OpenScene(previousScenePath);

            // for each appName, check its build directory to ensure that we
            // have an up-to-date build for the current installed level of the JS build tools.
            foreach (string appName in appNames)
            {
                if (!CroquetBuilder.CheckJSBuildState(appName, jsTarget))
                {
                    Debug.LogError($"Failed to find up-to-date build for \"{appName}\", target \"{jsTarget}\"");
                    failureMessage = "Missing up-to-date JS build(s)";
                    readyToBuild = false;
                }
            }
        }

        if (!readyToBuild) throw new BuildFailedException(failureMessage);

        // everything seems fine.  copy the tools record into the StreamableAssets folder
        CopyJSToolsRecord();
        // and on Windows, copy our pre-supplied node.exe too.
        if (isWindowsBuild) CopyNodeExe();
    }

    private void CopyJSToolsRecord()
    {
        string src = CroquetBuilder.JSToolsRecordInEditor;
        string dest = CroquetBuilder.JSToolsRecordInBuild;
        string destDir = Path.GetDirectoryName(dest);
        Directory.CreateDirectory(destDir);
        FileUtil.ReplaceFile(src, dest);
    }


    private void CopyNodeExe()
    {
        string src = CroquetBuilder.NodeExeInPackage;
        string dest = CroquetBuilder.NodeExeInBuild;
        string destDir = Path.GetDirectoryName(dest);
        Directory.CreateDirectory(destDir);
        FileUtil.ReplaceFile(src, dest);
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

