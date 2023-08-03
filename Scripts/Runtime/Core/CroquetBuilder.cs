using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.PackageManager;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

// CroquetBuilder is a class with only static methods.  Its responsibility is to manage the bundling of
// the JavaScript code associated with an app that the user wants to play.
public class CroquetBuilder
{
    public static string NodeExeInBuild =
        Path.GetFullPath(Path.Combine(Application.streamingAssetsPath, "croquet-bridge", "node", "node.exe"));

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
    private static string sceneName;
    private static CroquetBridge sceneBridgeComponent;
    private static CroquetRunner sceneRunnerComponent; // used in WIN editor
    private static string sceneAppName;

    private const string ID_PROP = "JS Builder Id";
    private const string APP_PROP = "JS Builder App";
    private const string TARGET_PROP = "JS Builder Target";
    private const string LOG_PROP = "JS Builder Log";
    private const string BUILD_ON_PLAY = "JS Build on Play";
    private const string BUILD_STATE = "JS Build State";

    public static bool BuildOnPlayEnabled
    {
        get { return EditorPrefs.GetBool(ProjectSpecificKey(BUILD_ON_PLAY), false); }
        set { EditorPrefs.SetBool(ProjectSpecificKey(BUILD_ON_PLAY), value); }
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

    public static string CroquetBuildToolsInPackage = Path.GetFullPath("Packages/io.croquet.multiplayer/.JSTools");
    public static string NodeExeInPackage = Path.GetFullPath("Packages/io.croquet.multiplayer/.JSTools/NodeJS/node.exe");

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
#if UNITY_EDITOR_OSX
            string pathToNode = sceneBridgeComponent.appProperties.pathToNode;
#else
            // assume we're in a Windows editor
            string pathToNode = NodeExeInPackage;
            if (!sceneRunnerComponent.waitForUserLaunch && !sceneBridgeComponent.useNodeJS)
            {
                Debug.Log("Switching to Node JS for non-user-launched Croquet");
                sceneBridgeComponent.useNodeJS = true;
            }
#endif
            return new JSBuildDetails(sceneBridgeComponent.appName, sceneBridgeComponent.useNodeJS, pathToNode);
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
        // build for web or node, or for neither
        EditorPrefs.SetString(AppSpecificKey(BUILD_STATE, appName), success ? target : "");
    }

    private static string GetJSBuildState(string appName)
    {
        // fetch the build state recorded above, if any
        return EditorPrefs.GetString(AppSpecificKey(BUILD_STATE, appName), "");
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
    public static void StartBuild(bool startWatcher)
    {
        if (oneTimeBuildProcess != null) return; // already building

        JSBuildDetails details = GetSceneBuildDetails(); // includes forcing useNodeJS, if necessary (on Windows)
        string appName = details.appName;
        string builderPath = Path.GetFullPath(Path.Combine(Application.streamingAssetsPath, "..", ".CroquetJS", "build-tools"));
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
                executable = "powershell.exe";
                arguments = $"-NoProfile -file \"runwebpack.ps1\" ";
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

    public static void WaitUntilBuildComplete()
    {
        // if a one-time build is in progress, await its exit.
        // when running a watcher (MacOS only), this function will *not* wait at
        // any point.  it is the user's responsibility when starting the watcher
        // to hold off from any action that needs the build until the console shows
        // that it has completed.  thereafter, rebuilds tend to happen so quickly
        // that there is effectively no chance for an incomplete build to be used.

        // may 2023: because StartBuild is synchronous, and already includes a
        // WaitForExit, this method in fact never has anything to wait for.
        if (oneTimeBuildProcess != null)
        {
            Debug.Log("waiting for one-time build to complete");
            oneTimeBuildProcess.WaitForExit();
        }
    }

    private static async void WatchLogFile(string filePath, long initialLength)
    {
        string appName = EditorPrefs.GetString(ProjectSpecificKey(APP_PROP), "");
        string target = EditorPrefs.GetString(ProjectSpecificKey(TARGET_PROP));
        long lastFileLength = initialLength;
        bool recordedSuccess = GetJSBuildState(appName) == target;

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

    public static bool EnsureJSBuildAvailable(bool inPlayMode)
    {
        if (StateOfJSBuildTools() == "missing") return false; // explanatory error will already have been logged

        string jsPath = Path.GetFullPath(Path.Combine(Application.streamingAssetsPath, "..", ".CroquetJS"));

        // getting build details also sets sceneBridgeComponent and sceneRunnerComponent, and runs
        // the check that on Windows force useNodeJS to true unless CroquetRunner is set to wait
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
            Debug.LogError($"Could not find source directory for app \"{appName}\" under .CroquetJS");
            return false;
        }

        string target = sceneBridgeComponent.useNodeJS ? "node" : "web";

        if (RunningWatcherApp() == appName)
        {
            // there is a watcher
            bool success = GetJSBuildState(appName) == target;
            if (!success) Debug.LogError($"JS Watcher has not reported a successful build for target \"{target}\".");
            return success;
        }

        // no watcher; maybe we should rebuild on Play?
        if (inPlayMode && BuildOnPlayEnabled)
        {
            try
            {
                StartBuild(false); // false => no watcher
                return GetJSBuildState(appName) == target;
            }
            catch (Exception e)
            {
                Debug.LogError(e);
                return false;
            }
        }

        return GetJSBuildState(appName) == target;
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

    private static string StateOfJSBuildTools()
    {
        // return one of three states
        //   "ok" - we appear to be up to date with the package
        //   "needsRefresh" - we have tools, but they are out of step with the package
        //   "missing" - no sign that tools have been installed (possibly because even the Croquet package is missing), or (on Mac) no Node executable found

#if UNITY_EDITOR_OSX
        string nodeExecutable = GetSceneBuildDetails().nodeExecutable;
        if (nodeExecutable == "" || !File.Exists(nodeExecutable))
        {
            Debug.LogError("Cannot build JS on MacOS without a valid path to Node in the Settings object");
            return "missing";
        }
#endif

        string croquetVersion = FindCroquetPackageVersion();
        if (croquetVersion == "")
        {
            Debug.LogError("Cannot find the Croquet Multiplayer dependency");
            return "missing";
        }

        string installedVersion = FindJSToolsPackageVersion();
        if (installedVersion == "")
        {
            Debug.LogError("Need to run Croquet => Install JS Build Tools");
            return "missing";
        }

        // we don't try to figure out an ordering between package versions.  if the .latest-installed-tools
        // differs from the package version, we raise a warning.
        if (installedVersion != croquetVersion)
        {
            Debug.LogWarning("Newer JS build tools are available; run Croquet => Install JS Build Tools to update");
            return "needsRefresh";
        }

        return "ok";
    }

    public static string FindJSToolsPackageVersion()
    {
        string jsFolder = Path.GetFullPath(Path.Combine(Application.streamingAssetsPath, "..", ".CroquetJS"));
        if (!Directory.Exists(jsFolder)) return ""; // nothing installed

        string installRecord = Path.Combine(jsFolder, ".last-installed-tools");
        if (!File.Exists(installRecord)) return "";

        string installRecordContents = File.ReadAllText(installRecord);
        return installRecordContents.Trim();
    }

    public static string FindCroquetPackageVersion()
    {
        string[] packageJsons = AssetDatabase.FindAssets("package");
        foreach (string guid1 in packageJsons)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid1);
            if (path.Contains("croquet.multiplayer"))
            {
                UnityEditor.PackageManager.PackageInfo info = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(path);
                return info.version;
            }
        }
        return "";
    }

    public static async Task InstallJSTools(bool forceUpdate)
    {
        string packageVersion = FindCroquetPackageVersion();
        if (packageVersion == "")
        {
            Debug.LogError("Croquet Multiplayer package not found");
            return;
        }

        string toolsRoot = CroquetBuildToolsInPackage;
        string jsFolder = Path.GetFullPath(Path.Combine(Application.streamingAssetsPath, "..", ".CroquetJS"));
        if (!Directory.Exists(jsFolder)) Directory.CreateDirectory(jsFolder);

        // compare package.json before overwriting, to decide if it will be changing
        string sourcePackageJson = Path.GetFullPath(Path.Combine(toolsRoot, "package.json"));
        string installedPackageJson = Path.GetFullPath(Path.Combine(jsFolder, "package.json"));
        bool needsNpmInstall = !File.Exists(installedPackageJson) || !FileEquals(sourcePackageJson, installedPackageJson);

        // copy the various files to Assets/.CroquetJS/
        string[] files = new string[] { "package.json", ".eslintrc.json", ".gitignore" };
        foreach (var file in files)
        {
            string fsrc = Path.GetFullPath(Path.Combine(toolsRoot, file));
            string fdest = Path.GetFullPath(Path.Combine(jsFolder, file));
            Debug.Log($"writing {fdest}");
            FileUtil.ReplaceFile(fsrc, fdest);
        }

        string dir = "build-tools";
        string dsrc = Path.GetFullPath(Path.Combine(toolsRoot, dir));
        string ddest = Path.GetFullPath(Path.Combine(jsFolder, dir));
        Debug.Log($"writing directory {ddest}");
        FileUtil.ReplaceDirectory(dsrc, ddest);

        // and a record of which package version the files came from
        string installRecord = Path.Combine(jsFolder, ".last-installed-tools");
        File.WriteAllText(installRecord, packageVersion);

        if (needsNpmInstall)
        {
            string nodePath = "";
#if UNITY_EDITOR_OSX
            string nodeExecutable = GetSceneBuildDetails().nodeExecutable;
            nodePath = Path.GetDirectoryName(nodeExecutable);
#endif

            // get ready to start the npm install.
            Debug.Log("Running npm install...");

            // introducing even a short delay gives the console a chance to show the logged messages
            await Task.Delay(100);

            Task task = (Application.platform == RuntimePlatform.OSXEditor)
                ? new Task(() => InstallOSX(jsFolder, toolsRoot, nodePath))
                : new Task(() => InstallWin(jsFolder, toolsRoot));
            task.Start();
            task.Wait();
        }
        else Debug.Log("Not running npm install; package.json has not changed");
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

        LogProcessOutput(output.Split('\n'), errors.Split('\n'), "npm install");
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
        LogProcessOutput(output.Split('\n'), errors.Split('\n'), "npm install");
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
                errorCount++;
                Debug.LogError($"{prefix} error: {line}");
            }
        }

        return errorCount;
    }
// this whole class (apart from one static string) is only defined when in the editor
#endif
}

#if UNITY_EDITOR
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

#endif

