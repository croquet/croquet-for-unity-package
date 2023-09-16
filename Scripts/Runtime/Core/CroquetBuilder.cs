using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEngine.Networking;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

// CroquetBuilder is a class with only static methods.  Its responsibility is to manage the bundling of
// the JavaScript code associated with an app that the user wants to play.

[Serializable]
public class PackageJson
{
    public string version; // all we need, for now
}

[Serializable]
public class InstalledToolsRecord
{
    public string packageVersion;
    public int localToolsLevel;
}

[Serializable]
public class JSBuildStateRecord
{
    public string target;
    public int localToolsLevel;
}

/// <summary>
/// Croquet Builder is the primary class for building the JS tools.
/// </summary>
public class CroquetBuilder
{
    private static string INSTALLED_TOOLS_RECORD = "last-installed-tools"; // in .js-build folder (also preceded by .)
    private static string BUILD_STATE_RECORD = ".last-build-state"; // in each CroquetJS/<appname> folder

    public static string JSToolsRecordInEditor =
        Path.GetFullPath(Path.Combine(Application.streamingAssetsPath, "..", "CroquetJS", ".js-build", $".{INSTALLED_TOOLS_RECORD}"));
    // NB: a file name beginning with . won't make it into a build (at least, not on Android)
    // NB: using GetFullPath would add a leading slash that confuses at least an Android UnityWebRequest
    public static string JSToolsRecordInBuild =
        Path.Combine(Application.streamingAssetsPath, "croquet-bridge", INSTALLED_TOOLS_RECORD);
    public static string NodeExeInBuild =
        Path.Combine(Application.streamingAssetsPath, "croquet-bridge", "node", "node.exe");

    private static string sceneName;
    private static CroquetBridge sceneBridgeComponent;
    private static CroquetRunner sceneRunnerComponent;
    private static string sceneAppName;

    public static string StateOfJSBuildTools()
    {
        // return one of four states
        //   "ok" - we appear to be up to date with the package
        //   "needsRefresh" - we have tools, but they are out of step with the package
        //   "needsInstall" - no sign that tools have been installed, but we can go ahead and try
        //   "unavailable" - no way to install: (on Mac) no Node executable found

#if UNITY_EDITOR_OSX
        string nodeExecutable = GetSceneBuildDetails().nodeExecutable;
        if (nodeExecutable == "" || !File.Exists(nodeExecutable))
        {
            Debug.LogError("Cannot build JS on MacOS without a valid path to Node in the Settings object");
            return "unavailable";
        }
#endif

        InstalledToolsRecord toolsRecord = FindJSToolsRecord();
        if (toolsRecord == null)
        {
            return "needsInstall";
        }

        // we don't try to figure out an ordering between package versions.  if the .latest-installed-tools
        // differs from the package version, we raise a warning.
        string croquetVersion = FindCroquetPackageVersion();
        if (toolsRecord.packageVersion != croquetVersion)
        {
            Debug.LogWarning("Updated JS build tools are available; run Croquet => Install JS Build Tools to install");
            return "needsRefresh";
        }

        return "ok";
    }

    public static InstalledToolsRecord FindJSToolsRecord()
    {
        string installRecordContents = "";

#if UNITY_EDITOR
        string installRecordPath = JSToolsRecordInEditor;
        if (!File.Exists(installRecordPath)) return null;

        installRecordContents = File.ReadAllText(installRecordPath);
#else
        // find the file in a build.  Android needs extra care.
        string src = JSToolsRecordInBuild;
  #if UNITY_ANDROID
        var unityWebRequest = UnityWebRequest.Get(src);
        unityWebRequest.SendWebRequest();
        while (!unityWebRequest.isDone) { } // meh
        if (unityWebRequest.result != UnityWebRequest.Result.Success)
        {
            if (unityWebRequest.error != null) UnityEngine.Debug.Log($"{src}: {unityWebRequest.error}");
        }
        else
        {
            byte[] contents = unityWebRequest.downloadHandler.data;
            installRecordContents = Encoding.UTF8.GetString(contents);
        }
        unityWebRequest.Dispose();
  #else
        installRecordContents = File.ReadAllText(src);
  #endif
#endif

        return JsonUtility.FromJson<InstalledToolsRecord>(installRecordContents);
    }

    public static string FindCroquetPackageVersion()
    {
        string packageJsonPath = Path.GetFullPath("Packages/io.croquet.multiplayer/package.json");
        string packageJsonContents = File.ReadAllText(packageJsonPath);
        PackageJson packageJson = JsonUtility.FromJson<PackageJson>(packageJsonContents);
        return packageJson.version;
    }

    public static bool CheckJSBuildState(string appName, string target)
    {
        // check whether we have a build for the given app and target that is up to date with the JS tools
        InstalledToolsRecord installedTools = FindJSToolsRecord(); // caller must have confirmed that this exists
        int installedToolsLevel = installedTools.localToolsLevel;

        string buildRecordPath = Path.GetFullPath(Path.Combine(Application.streamingAssetsPath, "..", "CroquetJS",
            appName, BUILD_STATE_RECORD));
        if (!File.Exists(buildRecordPath)) return false; // failed, or never built

        string buildRecordContents = File.ReadAllText(buildRecordPath).Trim();
        JSBuildStateRecord record = JsonUtility.FromJson<JSBuildStateRecord>(buildRecordContents);

        return record.target == target && record.localToolsLevel == installedToolsLevel;
    }

    public static bool PrepareSceneForBuildTarget(Scene scene, bool buildForWindows)
    {
        CacheSceneComponents(scene);

        bool goodToGo = true;
        if (sceneBridgeComponent.appProperties.apiKey == "" ||
            sceneBridgeComponent.appProperties.apiKey == "PUT_YOUR_API_KEY_HERE")
        {
            Debug.LogWarning("Cannot build without a Croquet API Key in the Settings object");
            goodToGo = false;
        }
        if (sceneRunnerComponent.debugUsingExternalSession)
        {
            Debug.LogWarning("Croquet Runner component's \"Debug Using External Session\" must be off");
            goodToGo = false;
        };
        if (sceneRunnerComponent.forceToUseNodeJS && !buildForWindows)
        {
            Debug.LogWarning($"Croquet Runner component's \"Force to Use Node JS\" is checked, but must be off for a non-Windows build");
            goodToGo = false;
        };
        if (sceneRunnerComponent.runOffline)
        {
            Debug.LogWarning("Croquet Runner component's \"Run Offline\" must be off");
            goodToGo = false;
        };

        return goodToGo;
    }

    public static void CacheSceneComponents(Scene scene)
    {
        CroquetBridge bridgeComp = null;
        CroquetRunner runnerComp = null;
        GameObject[] roots = scene.GetRootGameObjects();

        CroquetBridge bridge = Object.FindObjectOfType<CroquetBridge>();

        if (bridge != null)
        {
            bridgeComp = bridge;
            runnerComp = bridge.gameObject.GetComponent<CroquetRunner>();
        }

        sceneName = scene.name;
        sceneBridgeComponent = bridgeComp;
        sceneRunnerComponent = runnerComp;
    }

    // =========================================================================================
    //              everything from here on is only relevant in the editor
    // =========================================================================================

#if UNITY_EDITOR
    // on MacOS we offer the user the chance to start a webpack watcher that will
    // re-bundle the Croquet app automatically whenever the code is updated.
    // the console output from webpack is shown in the Unity console.  we do not
    // currently support the watcher on Windows, because we have not yet found a way
    // to stream the console output from a long-running webpack process.
    //
    // on both platforms we provide options for explicitly re-bundling by invocation
    // from the Croquet menu (for example, before hitting Build), or automatically
    // whenever the Play button is pressed.
    public static Process oneTimeBuildProcess; // queried by CroquetMenu
    private static string hashedProjectPath = ""; // a hash string representing this project, for use in EditorPrefs keys
    private static bool installingJSTools = false;

    private const string ID_PROP = "JS Builder Id";
    private const string APP_PROP = "JS Builder App";
    private const string TARGET_PROP = "JS Builder Target";
    private const string LOG_PROP = "JS Builder Log";
    private const string BUILD_ON_PLAY = "JS Build on Play";
    private const string HARVEST_SCENES = "Harvest Scene List";

    public static bool BuildOnPlayEnabled
    {
        get { return EditorPrefs.GetBool(ProjectSpecificKey(BUILD_ON_PLAY), true); }
        set { EditorPrefs.SetBool(ProjectSpecificKey(BUILD_ON_PLAY), value); }
    }

    public static string HarvestSceneList
    {
        get { return EditorPrefs.GetString(ProjectSpecificKey(HARVEST_SCENES), ""); }
        set { EditorPrefs.SetString(ProjectSpecificKey(HARVEST_SCENES), value); }
    }

    public static string CroquetBuildToolsInPackage
    {
        get { return Path.GetFullPath("Packages/io.croquet.multiplayer/.JSTools"); }
    }

    public static string NodeExeInPackage
    {
        get { return Path.GetFullPath("Packages/io.croquet.multiplayer/.JSTools/NodeJS/node.exe"); }
    }

    public struct JSBuildDetails
    {
        public JSBuildDetails(string name, bool useNode, string pathToNode)
        {
            appName = name;
            useNodeJS = useNode;
            nodeExecutable = pathToNode;
        }

        public string appName;
        public bool useNodeJS;
        public string nodeExecutable;
    }

    public static JSBuildDetails GetSceneBuildDetails()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        if (activeScene.name != sceneName)
        {
            // look in the scene for an object with a CroquetBridge component,
            // and if found cache its build details
            CacheSceneComponents(activeScene);
        }

        if (sceneBridgeComponent != null)
        {
            // on Mac, we rely on the user pointing us to an installed NodeJS
            // executable using the settings object.  this is used for running
            // all JS build steps, and can also drive a scene if the user selects
            // the "Use Node JS" option.  it *cannot* be bundled into a build.

            // for Windows, we include a version of node.exe in the package.
            // it can be used for JS building, for running scenes in the editor,
            // and for inclusion in a Windows standalone build.
            bool forceToUseNodeJS = sceneRunnerComponent.forceToUseNodeJS;
            bool useNodeJS = forceToUseNodeJS; // default
#if !UNITY_EDITOR_WIN
            string pathToNode = sceneBridgeComponent.appProperties.pathToNode;
#else
            // we're in a Windows editor
            string pathToNode = NodeExeInPackage;
            // build using Node unless user has set debugUsingExternalSession and has *not* set forceToUseNodeJS
            useNodeJS = !(sceneRunnerComponent.debugUsingExternalSession && !forceToUseNodeJS);
#endif
            return new JSBuildDetails(sceneBridgeComponent.appName, useNodeJS, pathToNode);
        }
        else return new JSBuildDetails("", false, "");
    }

    public static bool KnowHowToBuildJS()
    {
        // used by the Croquet menu to decide which options are valid to show
        JSBuildDetails details = GetSceneBuildDetails();
        return details.appName != "";
    }

    private static string ProjectSpecificKey(string rawKey)
    {
        return $"{KeyPrefixForAppPrefs()}:{rawKey}";
    }

    private static string AppSpecificKey(string rawKey, string appName)
    {
        return $"{KeyPrefixForAppPrefs()}:{appName}:{rawKey}";
    }

    private static void RecordJSBuildState(string appName, string target, bool success)
    {
        // record one of "web", "node", or "" to indicate whether StreamingAssets contains a successful
        // build for web or node, or for neither.
        // also record the tools level, so we can force a rebuild after a tools update.
        string buildRecordPath = Path.GetFullPath(Path.Combine(Application.streamingAssetsPath, "..", "CroquetJS",
            appName, BUILD_STATE_RECORD));
        if (success)
        {
            int toolsLevel = FindJSToolsRecord().localToolsLevel;
            JSBuildStateRecord record = new JSBuildStateRecord()
            {
                target = target,
                localToolsLevel = toolsLevel
            };
            File.WriteAllText(buildRecordPath, JsonUtility.ToJson(record, true));

            // check that the write itself succeeded
            string buildRecordContents = File.ReadAllText(buildRecordPath).Trim(); // will throw if no file
            JSBuildStateRecord newRecord = JsonUtility.FromJson<JSBuildStateRecord>(buildRecordContents);
            if (newRecord != null && newRecord.target == target && newRecord.localToolsLevel == toolsLevel)
            {
                // written correctly
                return;
            }

            Debug.LogError($"failed to write JS build record {buildRecordPath}");
        }

        if (File.Exists(buildRecordPath)) File.Delete(buildRecordPath);
    }

    public static void StartBuild(bool startWatcher)
    {
        // invoked from
        // * Croquet menu "Build JS Now" option, with startWatcher=false
        // * Croquet menu "Start JS Watcher" option, with startWatcher=true
        // * this object's EnsureJSBuildAvailableToPlay method, if BuildOnPlayEnabled is true (see getter above)

        // before invoking this, the caller must have run EnsureJSToolsAvailable (with a successful
        // return code) so that this code can assume that the tools are installed.

        if (oneTimeBuildProcess != null)
        {
            Debug.LogWarning($"JS build already in progress.");
            return;
        }

        JSBuildDetails details = GetSceneBuildDetails(); // includes forcing useNodeJS, if necessary (on Windows)
        string appName = details.appName;
        string builderPath = Path.GetFullPath(Path.Combine(Application.streamingAssetsPath, "..", "CroquetJS", ".js-build", "build-tools"));
        string nodeExecPath;
        string executable;
        string arguments = "";
        string target = details.useNodeJS ? "node" : "web";
        string logFile = "";
        switch (Application.platform)
        {
            case RuntimePlatform.OSXEditor:
                nodeExecPath = details.nodeExecutable;
                executable = Path.Combine(builderPath, "runwebpack.sh");
                break;
            case RuntimePlatform.WindowsEditor:
                nodeExecPath = "\"" + details.nodeExecutable + "\"";
                executable = "cmd.exe";
                arguments = $"/c runwebpack.bat ";
                break;
            default:
                throw new PlatformNotSupportedException("Don't know how to support automatic builds on this platform");
        }

        // record a failed build until we hear otherwise
        RecordJSBuildState(appName, target, false);

        // arguments to the runwebpack script, however it is invoked:
        // 1. full path to the platform-relevant node engine
        // 2. app name
        // 3. build target: 'node' or 'web'
        // 4. (iff starting a watcher) path to a temporary file to be used for output
        arguments += $"{nodeExecPath} {appName} {target} ";
        if (startWatcher)
        {
            logFile = Path.GetTempFileName();
            arguments += logFile;
        }
        else
        {
            Debug.Log($"building {appName} for {target}");
        }

        Process builderProcess = new Process();
        if (!startWatcher) oneTimeBuildProcess = builderProcess;
        builderProcess.StartInfo.UseShellExecute = false;
        builderProcess.StartInfo.RedirectStandardOutput = true;
        builderProcess.StartInfo.RedirectStandardError = true;
        builderProcess.StartInfo.CreateNoWindow = true;
        builderProcess.StartInfo.WorkingDirectory = builderPath;
        builderProcess.StartInfo.FileName = executable;
        builderProcess.StartInfo.Arguments = arguments;
        builderProcess.Start();

        string output = builderProcess.StandardOutput.ReadToEnd();
        string errors = builderProcess.StandardError.ReadToEnd();
        builderProcess.WaitForExit();

        if (!startWatcher)
        {
            // the build process has finished, but that doesn't necessarily mean that it succeeded.
            // webpack provides an exit code as described at https://github.com/webpack/webpack-cli#exit-codes-and-their-meanings.
            // if webpack runs, our script generates a line "webpack-exit=<n>" with that exit code.

            // the expected completion states are therefore:
            //   - failed to run webpack (e.g., because it isn't installed).
            //     should see messages on stderr, and presumably no webpack-exit line.
            //   - able to run webpack, with exit code:
            //     2: "Configuration/options problem or an internal error" (e.g., can't find the config file)
            //        should see messages on stderr.
            //     1: "Errors from webpack" (e.g., syntax error in code, or can't find a module)
            //        typically nothing on stderr.  error diagnosis on stdout.
            //     0: "Success"
            //        log of build on stdout, ending with a "compiled successfully" line.

            oneTimeBuildProcess = null;

            // pre-process the stdout to remove any line purely added by us
            string[] stdoutLines = output.Split('\n');
            List<string> filteredLines = new List<string>();
            int webpackExit = -1;
            string exitPrefix = "webpack-exit=";
            foreach (string line in stdoutLines)
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    if (line.StartsWith(exitPrefix))
                    {
                        webpackExit = int.Parse(line.Substring(exitPrefix.Length));
                    }
                    else filteredLines.Add(line);
                }
            }

            int errorCount = LogProcessOutput(filteredLines.ToArray(), errors.Split('\n'), "JS builder");
            bool success = webpackExit == 0 && errorCount == 0;
            Debug.Log($"recording JS build state: app={appName}, target={target}, success={success}");
            RecordJSBuildState(appName, target, success);
        }
        else
        {
            string prefix = "webpack=";
            if (output.StartsWith(prefix))
            {
                int processId = int.Parse(output.Substring(prefix.Length));
                Debug.Log($"started JS watcher for {appName}, target \"{target}\", as process {processId}");
                EditorPrefs.SetInt(ProjectSpecificKey(ID_PROP), processId);
                EditorPrefs.SetString(ProjectSpecificKey(APP_PROP), appName);
                EditorPrefs.SetString(ProjectSpecificKey(TARGET_PROP), target);
                EditorPrefs.SetString(ProjectSpecificKey(LOG_PROP), logFile);

                WatchLogFile(logFile, 0);
            }
        }
    }

    private static async void WatchLogFile(string filePath, long initialLength)
    {
        string appName = EditorPrefs.GetString(ProjectSpecificKey(APP_PROP), "");
        string target = EditorPrefs.GetString(ProjectSpecificKey(TARGET_PROP));
        long lastFileLength = initialLength;
        bool recordedSuccess = CheckJSBuildState(appName, target);

        // Debug.Log($"watching build log for {appName} from position {lastFileLength}");

        while (true)
        {
            if (EditorPrefs.GetString(ProjectSpecificKey(LOG_PROP), "") != filePath)
            {
                // Debug.Log($"stopping log watcher for {appName}");
                break;
            }

            try
            {
                FileInfo info = new FileInfo(filePath);
                long length = info.Length;
                // Debug.Log($"log file length = {length}");
                if (length > lastFileLength)
                {
                    using (FileStream fs = info.OpenRead())
                    {
                        fs.Seek(lastFileLength, SeekOrigin.Begin);
                        byte[] b = new byte[length - lastFileLength];
                        UTF8Encoding temp = new UTF8Encoding(true);
                        while (fs.Read(b, 0, b.Length) > 0)
                        {
                            string[] newLines = temp.GetString(b).Split('\n');
                            foreach (string line in newLines)
                            {
                                if (!string.IsNullOrWhiteSpace(line))
                                {
                                    string labeledLine = $"JS watcher ({appName}): {line}";
                                    if (line.Contains("ERROR")) Debug.LogError(labeledLine);
                                    else if (line.Contains("compiled") && line.Contains("error"))
                                    {
                                        // end of an errored build
                                        Debug.LogError(labeledLine);
                                        // only record the failure if we previously had success
                                        if (recordedSuccess)
                                        {
                                            Debug.Log($"recording JS build state: app={appName}, target={target}, success=false");
                                            RecordJSBuildState(appName, target, false);
                                            recordedSuccess = false;
                                        }
                                    }
                                    else if (line.Contains("WARNING")) Debug.LogWarning(labeledLine);
                                    else
                                    {
                                        Debug.Log(labeledLine);
                                        if (line.Contains("compiled successfully"))
                                        {
                                            // only record the success if we previously had failure
                                            if (!recordedSuccess)
                                            {
                                                Debug.Log(
                                                    $"recording JS build state: app={appName}, target={target}, success=true");
                                                RecordJSBuildState(appName, target, true);
                                                recordedSuccess = true;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        fs.Close();
                    }

                    lastFileLength = length;
                }
            }
            catch (Exception e)
            {
                Debug.Log($"log watcher error: {e}");
            }
            finally
            {
                await System.Threading.Tasks.Task.Delay(1000);
            }
        }
    }

    public static bool EnsureJSBuildAvailableToPlay()
    {
        // invoked by CroquetBridge.WaitForJSBuild, after first running EnsureJSToolsAvailable.
        // we can therefore be sure that there are tools, but the bridge will not have confirmed
        // that the current scene has the necessary settings, and corresponding source code, to
        // make a build.

        string jsPath = Path.GetFullPath(Path.Combine(Application.streamingAssetsPath, "..", "CroquetJS"));

        // getting build details also sets sceneBridgeComponent and sceneRunnerComponent, and runs
        // the check that on Windows forces useNodeJS to true unless CroquetRunner is set to wait
        // for user launch
        JSBuildDetails details = GetSceneBuildDetails();
        if (sceneBridgeComponent == null)
        {
            Debug.LogError("Failed to find a Croquet Bridge component in the current scene");
            return false;
        }

        string appName = details.appName;
        if (appName == "")
        {
            Debug.LogError("App Name has not been set in Croquet Bridge");
            return false;
        }

        string sourcePath = Path.GetFullPath(Path.Combine(jsPath, appName));
        if (!Directory.Exists(sourcePath))
        {
            Debug.LogError($"Could not find source directory for app \"{appName}\" under CroquetJS");
            return false;
        }

        // at this point we have confirmed that there appears to be source code for making a build.
        // in fact, perhaps it has already been made.
        string target = details.useNodeJS ? "node" : "web";

#if !UNITY_EDITOR_WIN
        if (RunningWatcherApp() == appName)
        {
            // there is a watcher
            bool success = CheckJSBuildState(appName, target);
            if (!success)
            {
                string watcherTarget = EditorPrefs.GetString(ProjectSpecificKey(TARGET_PROP));
                if (watcherTarget != target)
                {
                    Debug.LogError($"We need a JS build for target \"{target}\", but there is a Watcher building for \"{watcherTarget}\"");
                }
                else
                {
                    // it's building for the right target, but hasn't succeeded
                    Debug.LogError($"JS Watcher has not reported a successful build.");
                }
            }
            return success;
        }
#endif

        // no watcher.  are we set up to rebuild on Play?
        if (BuildOnPlayEnabled)
        {
            try
            {
                StartBuild(false); // false => no watcher
                return CheckJSBuildState(appName, target);
            }
            catch (Exception e)
            {
                Debug.LogError(e);
                return false;
            }
        }

        bool alreadyBuilt = CheckJSBuildState(appName, target);
        if (!alreadyBuilt)
        {
            Debug.LogError($"No up-to-date JS build found for app \"{appName}\", target \"{target}\".  For automatic building, set Croquet => Build JS on Play.");
        }

        return alreadyBuilt;
    }

    private static string KeyPrefixForAppPrefs()
    {
        // return a key for EditorPrefs settings that we need to be isolated to this project.

        // our cache of the hash string will be wiped on each Play.  refresh if needed.
        if (hashedProjectPath == "")
        {
            string keyBase = Application.streamingAssetsPath;
            byte[] keyBaseBytes = new UTF8Encoding().GetBytes(keyBase);
            byte[] hash = MD5.Create().ComputeHash(keyBaseBytes);
            StringBuilder sb = new StringBuilder();
            foreach (byte b in hash) sb.Append(b.ToString("X2"));
            hashedProjectPath = sb.ToString().Substring(0, 16); // no point keeping whole thing
        }

        return hashedProjectPath;
    }

    public static void EnteredEditMode()
    {
        // if there is a watcher, when play stops re-establish the process reporting its logs
        string logFile = EditorPrefs.GetString(ProjectSpecificKey(LOG_PROP), "");
        if (logFile != "")
        {
            FileInfo info = new FileInfo(logFile);
            WatchLogFile(logFile, info.Length);
        }
    }

    public static void StopWatcher()
    {
        Process process = RunningWatcherProcess();
        if (process != null)
        {
            string appName = EditorPrefs.GetString(ProjectSpecificKey(APP_PROP));
            string target = EditorPrefs.GetString(ProjectSpecificKey(TARGET_PROP));
            Debug.Log($"stopping JS watcher for {appName}, target \"{target}\"");
            process.Kill();
            process.Dispose();
        }

        string logFile = EditorPrefs.GetString(ProjectSpecificKey(LOG_PROP), "");
        if (logFile != "") FileUtil.DeleteFileOrDirectory(logFile);

        EditorPrefs.SetInt(ProjectSpecificKey(ID_PROP), -1);
        EditorPrefs.SetString(ProjectSpecificKey(APP_PROP), "");
        EditorPrefs.SetString(ProjectSpecificKey(TARGET_PROP), "");
        EditorPrefs.SetString(ProjectSpecificKey(LOG_PROP), "");
    }

    private static Process RunningWatcherProcess()
    {
        Process process = null;
        int lastBuildId = EditorPrefs.GetInt(ProjectSpecificKey(ID_PROP), -1);
        if (lastBuildId != -1)
        {
            try
            {
                // this line will throw if the process is no longer running
                Process builderProcess = Process.GetProcessById(lastBuildId);
                // to reduce the risk that the process id we had is now being used for
                // some random other process (which we therefore shouldn't kill), confirm
                // that it has the name "node" associated with it.
                if (builderProcess.ProcessName == "node" && !builderProcess.HasExited)
                {
                    process = builderProcess;
                }
            }
            catch(Exception e)
            {
                Debug.Log($"process has disappeared ({e})");
            }

            if (process == null)
            {
                // the id we had is no longer valid
                EditorPrefs.SetInt(ProjectSpecificKey(ID_PROP), -1);
                EditorPrefs.SetString(ProjectSpecificKey(APP_PROP), "");
                EditorPrefs.SetString(ProjectSpecificKey(TARGET_PROP), "");
                EditorPrefs.SetString(ProjectSpecificKey(LOG_PROP), "");
            }
        }

        return process;
    }

    public static string RunningWatcherApp()
    {
        // return the app being served by the running builder process, if any.
        // this is the recorded Builder App, as long as the recorded Builder Id
        // corresponds to a running process that has the name "node".
        // if the process was not found, we will have reset both the Path and Id.
        Process builderProcess = RunningWatcherProcess();
        return builderProcess == null ? "" : EditorPrefs.GetString(ProjectSpecificKey(APP_PROP));
    }

    public static async Task<bool> EnsureJSToolsAvailable()
    {
        // invoked from
        // * CroquetBridge.WaitForJSBuild
        // * Croquet menu "Build JS Now" option
        // * Croquet menu "Start JS Watcher" option

        // ensure that JS build tools are available.  return true if tools were already available,
        // or have been successfully installed by this method.

        if (installingJSTools)
        {
            // someone has already invoked this method, and it's in the middle of installing the tools.
            // no additional caller can proceed until that finishes.
            Debug.LogWarning("JS Build Tools installation already in progress");
            return false;
        }

        string state = StateOfJSBuildTools();
        if (state == "unavailable") return false; // explanatory error will already have been logged
        if (state == "needsInstall")
        {
            Debug.LogWarning("No JS build tools found.  Attempting to install...");
            installingJSTools = true;
            bool success = await InstallJSTools(); // uses try..catch to protect against errors
            installingJSTools = false;
            if (!success)
            {
                Debug.LogError("Install of JS build tools failed.");
                return false;
            }

            Debug.Log("Install of JS build tools completed");
        }

        // state is either "needsRefresh" (in which case a warning will already have been logged by
        // StateOfJSBuildTools) or "ok" (perhaps because we just installed here).  caller can go ahead.
        return true;
    }

    public static async Task<bool> InstallJSTools()
    {
        string toolsRoot = CroquetBuildToolsInPackage;
        string croquetJSFolder = Path.GetFullPath(Path.Combine(Application.streamingAssetsPath, "..", "CroquetJS"));
        string jsBuildFolder = Path.GetFullPath(Path.Combine(croquetJSFolder, ".js-build"));
        string installRecord = JSToolsRecordInEditor;

        try
        {
            if (!Directory.Exists(jsBuildFolder)) Directory.CreateDirectory(jsBuildFolder);

            bool needsNPMInstall;
            if (FindJSToolsRecord() == null) needsNPMInstall = true; // nothing installed; run the whole process
            else
            {
                // compare package-lock.json before overwriting, to decide if it will be changing
                string sourcePackageLock = Path.GetFullPath(Path.Combine(toolsRoot, "package-lock.json"));
                string installedPackageLock = Path.GetFullPath(Path.Combine(jsBuildFolder, "package-lock.json"));
                needsNPMInstall = !File.Exists(installedPackageLock) ||
                                  !FileEquals(sourcePackageLock, installedPackageLock);
            }

            // copy various files to CroquetJS
            // dictionary maps sourceFile => destinationPath
            Dictionary<string, string> copyDetails = new Dictionary<string, string>();
            copyDetails["package.json"] = ".js-build/package.json";
            copyDetails["package-lock.json"] = ".js-build/package-lock.json";
            copyDetails[".eslintrc.json"] = ".eslintrc.json";
            copyDetails["tools-gitignore"] = ".gitignore";
            foreach (KeyValuePair<string,string> keyValuePair in copyDetails)
            {
                string from = keyValuePair.Key;
                string to = keyValuePair.Value;
                string fsrc = Path.Combine(toolsRoot, from);
                string fdest = Path.Combine(croquetJSFolder, to);
                Debug.Log($"writing {from} as {to}");
                FileUtil.ReplaceFile(fsrc, fdest);
            }

            string dir = ".js-build/build-tools";
            string dsrc = Path.Combine(toolsRoot, Path.GetFileName(dir));
            string ddest = Path.Combine(croquetJSFolder, dir);
            Debug.Log($"writing directory {dir}");
            FileUtil.ReplaceDirectory(dsrc, ddest);

            int errorCount = 0; // look for errors in logging from npm i
            if (needsNPMInstall)
            {
                // announce that we'll be running the npm install, then introduce a short delay to
                // give the console a chance to display the messages logged so far.
                Debug.Log("Running npm install...");
                await Task.Delay(100);

                // npm has a habit of issuing warnings through stderr.  we filter out some
                // such warnings to avoid handling them as show-stoppers, but there may be
                // others that get through.  if errors are reported, try a second time in
                // case they were in fact just transient warnings.
                int triesRemaining = 2;
                while (triesRemaining > 0)
                {
                    errorCount = RunNPMInstall(jsBuildFolder, toolsRoot);
                    if (errorCount == 0) break;

                    if (--triesRemaining > 0)
                    {
                        Debug.LogWarning($"npm install logged {errorCount} errors; trying again");
                        await Task.Delay(100);
                    }
                }
            }
            else Debug.Log("package-lock.json has not changed; skipping npm install");

            if (errorCount == 0)
            {
                // update our local count of how many times the tools have been updated.  this will invalidate
                // any build made with an earlier level.
                InstalledToolsRecord toolsRecord = FindJSToolsRecord();
                int previousLevel = toolsRecord == null ? 0 : toolsRecord.localToolsLevel;
                int toolsLevel = previousLevel + 1;

                // add a record of which package version, and local copy of the JS tools, the files came from
                string packageVersion = FindCroquetPackageVersion();
                InstalledToolsRecord newRecord = new InstalledToolsRecord()
                {
                    packageVersion = packageVersion,
                    localToolsLevel = toolsLevel
                };
                File.WriteAllText(installRecord, JsonUtility.ToJson(newRecord, true));

                // check that the writing itself succeeded
                InstalledToolsRecord writtenRecord = FindJSToolsRecord();
                if (writtenRecord == null || writtenRecord.packageVersion != packageVersion || writtenRecord.localToolsLevel != toolsLevel)
                {
                    Debug.LogError("failed to write installed-tools record");
                    return false;
                }

                return true; // success!
            }
        }
        catch (Exception e)
        {
            Debug.LogError(e);
        }

        // failed
        if (File.Exists(installRecord)) File.Delete(installRecord); // make clear that the installation failed
        return false;
    }

    private static int RunNPMInstall(string jsBuildFolder, string toolsRoot)
    {
        string nodePath = "";
        bool onOSX = Application.platform == RuntimePlatform.OSXEditor;
        if (onOSX)
        {
            string nodeExecutable = GetSceneBuildDetails().nodeExecutable;
            nodePath = Path.GetDirectoryName(nodeExecutable);
        }

        int errorCount = 0;
        Task task = onOSX
            ? new Task(() => errorCount = InstallOSX(jsBuildFolder, toolsRoot, nodePath))
            : new Task(() => errorCount = InstallWin(jsBuildFolder, toolsRoot));
        task.Start();
        task.Wait();

        return errorCount;
    }
    private static int InstallOSX(string installDir, string toolsRoot, string nodePath) {
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

        return LogProcessOutput(output.Split('\n'), errors.Split('\n'), "npm install");
    }

    private static int InstallWin(string installDir, string toolsRoot)
    {
        string stdoutFile = Path.GetTempFileName();
        string stderrFile = Path.GetTempFileName();
        Process p = new Process();
        p.StartInfo.UseShellExecute = true;
        p.StartInfo.FileName = "cmd.exe";
        p.StartInfo.Arguments = $"/c npm ci 1>\"{stdoutFile}\" 2>\"{stderrFile}\" ";
        p.StartInfo.WorkingDirectory = installDir;
        p.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;

        p.Start();
        p.WaitForExit();

        string output = File.ReadAllText(stdoutFile);
        File.Delete(stdoutFile);
        string errors = File.ReadAllText(stderrFile);
        File.Delete(stderrFile);
        return LogProcessOutput(output.Split('\n'), errors.Split('\n'), "npm install");
    }

    // based on https://www.dotnetperls.com/file-equals
    static bool FileEquals(string path1, string path2)
    {
        byte[] file1 = File.ReadAllBytes(path1);
        byte[] file2 = File.ReadAllBytes(path2);

        if (file1.Length != file2.Length) return false;

        for (int i = 0; i < file1.Length; i++)
        {
            if (file1[i] != file2[i]) return false;
        }
        return true;
    }

    private static int LogProcessOutput(string[] stdoutLines, string[] stderrLines, string prefix)
    {
        int errorCount = 0;
        foreach (string line in stdoutLines)
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                string labeledLine = $"{prefix}: {line}";
                if (line.Contains("ERROR"))
                {
                    errorCount++;
                    Debug.LogError(labeledLine);
                }
                else if (line.Contains("WARNING")) Debug.LogWarning(labeledLine);
                else Debug.Log(labeledLine);
            }
        }

        foreach (string line in stderrLines)
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                // npm tends to throw certain non-error warnings out to stderr.  we handle
                // some telltale signs that a line isn't actually a show-stopping error.
                if (line.Contains("npm notice") || line.Contains("npm WARN"))
                {
                    Debug.LogWarning($"{prefix}: {line}");
                }
                else
                {
                    errorCount++;
                    Debug.LogError($"{prefix} error: {line}");
                }
            }
        }

        return errorCount;
    }
// this whole class (apart from one static string) is only defined when in the editor
#endif
}

