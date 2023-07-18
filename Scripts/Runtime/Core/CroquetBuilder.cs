using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
// using UnityEditor.PackageManager.Requests;
using UnityEditor.PackageManager;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

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
    public static Process oneTimeBuildProcess;
    private static string sceneName;
    private static CroquetBridge sceneBridgeComponent;
    private static CroquetRunner sceneRunnerComponent; // used in WIN editor
    private static string sceneAppName;

    private const string ID_PROP = "JS Builder Id";
    private const string APP_PROP = "JS Builder App";
    private const string LOG_PROP = "JS Builder Log";
    private const string BUILD_ON_PLAY = "JS Build on Play";

    public static bool BuildOnPlayEnabled
    {
        get { return EditorPrefs.GetBool(BUILD_ON_PLAY, false); }
        set { EditorPrefs.SetBool(BUILD_ON_PLAY, value); }
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
        JSBuildDetails details = GetSceneBuildDetails();
        return details.appName != "";
    }

    public static void StartBuild(bool startWatcher)
    {
        if (oneTimeBuildProcess != null) return; // already building

        JSBuildDetails details = GetSceneBuildDetails();
        string appName = details.appName;
        if (appName == "") return; // don't know how to build

        if (Application.platform == RuntimePlatform.OSXEditor && details.nodeExecutable == "")
        {
            Debug.LogError("Cannot build without a path to Node in the Settings object");
            return;
        }

        string builderPath = Path.GetFullPath(Path.Combine(Application.streamingAssetsPath, "..", "CroquetJS", "build-tools"));
        if (!Directory.Exists(builderPath))
        {
            Debug.LogError("Cannot find JS build tools. Did you copy them using the Croquet menu? You must then run 'npm install' in the Unity project's parent directory.");
            return;
        }

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

            LogProcessOutput(output, errors, "JS builder");

            int webpackExit = -1;
            string exitPrefix = "webpack-exit=";
            string[] newLines = output.Split('\n');
            foreach (string line in newLines)
            {
                if (!string.IsNullOrWhiteSpace(line) && line.StartsWith(exitPrefix))
                {
                    webpackExit = int.Parse(line.Substring(exitPrefix.Length));
                }
            }
            if (webpackExit != 0) throw new Exception("JS build failed.");
        }
        else
        {
            string prefix = "webpack=";
            if (output.StartsWith(prefix))
            {
                int processId = int.Parse(output.Substring(prefix.Length));
                Debug.Log($"started JS watcher for {appName} as process {processId}");
                EditorPrefs.SetInt(ID_PROP, processId);
                EditorPrefs.SetString(APP_PROP, appName);
                EditorPrefs.SetString(LOG_PROP, logFile);

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
        string appName = EditorPrefs.GetString(APP_PROP, "");
        long lastFileLength = initialLength;
        // Debug.Log($"watching build log for {appName} from position {lastFileLength}");

        while (true)
        {
            if (EditorPrefs.GetString(LOG_PROP, "") != filePath)
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
                                    if (line.Contains("compiled") && line.Contains("error"))
                                    {
                                        Debug.LogError($"JS watcher ({appName}): " + line);
                                    }
                                    else
                                    {
                                        Debug.Log($"JS watcher ({appName}): " + line);
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

    public static void EnteringPlayMode()
    {
        // get build details, just to run the check that on Windows forces
        // useNodeJS to true unless CroquetRunner is set to wait for user launch
        GetSceneBuildDetails();

        // rebuild-on-Play is only available if a watcher *isn't* running
        string logFile = EditorPrefs.GetString(LOG_PROP, "");
        if (logFile == "" && BuildOnPlayEnabled)
        {
            try
            {
                StartBuild(false); // false => no watcher
            }
            catch (Exception e)
            {
                Debug.LogError(e);
                EditorApplication.ExitPlaymode();
            }
        }
    }

    public static void EnteredEditMode()
    {
        // if there is a watcher, when play stops re-establish the process reporting its logs
        string logFile = EditorPrefs.GetString(LOG_PROP, "");
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
            Debug.Log($"stopping JS watcher for {EditorPrefs.GetString(APP_PROP)}");
            process.Kill();
            process.Dispose();
        }

        string logFile = EditorPrefs.GetString(LOG_PROP, "");
        if (logFile != "") FileUtil.DeleteFileOrDirectory(logFile);

        EditorPrefs.SetInt(ID_PROP, -1);
        EditorPrefs.SetString(APP_PROP, "");
        EditorPrefs.SetString(LOG_PROP, "");
    }

    private static Process RunningWatcherProcess()
    {
        Process process = null;
        int lastBuildId = EditorPrefs.GetInt(ID_PROP, -1);
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
                EditorPrefs.SetInt(ID_PROP, -1);
                EditorPrefs.SetString(APP_PROP, "");
                EditorPrefs.SetString(LOG_PROP, "");
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
        return builderProcess == null ? "" : EditorPrefs.GetString(APP_PROP);
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
        string nodePath = "";
#if UNITY_EDITOR_OSX
        string nodeExecutable = GetSceneBuildDetails().nodeExecutable;
        if (string.IsNullOrWhiteSpace(nodeExecutable) || !File.Exists(nodeExecutable))
        {
            Debug.LogError("Cannot find Node executable; did you remember to set the path in the Settings object?");
            return;
        }
        nodePath = Path.GetDirectoryName(nodeExecutable);
#endif

        string packageVersion = FindCroquetPackageVersion();
        if (packageVersion == "")
        {
            Debug.LogError("Croquet Multiplayer package not found");
            return;
        }

        string unityParentFolder = Path.GetFullPath(Path.Combine(Application.streamingAssetsPath, "..", "..", ".."));

        // $$$ WIP - need some way to decide whether the version has changed.
        // the internal package.json does NOT (currently) include the version.
        // bool doUpdate = forceUpdate;
        // if (true || !doUpdate) // $$$
        // {
        //     string packageJsonPath = Path.Combine(unityParentFolder, "package.json");
        //     if (!File.Exists(packageJsonPath))
        //     {
        //         doUpdate = true;
        //     }
        //     else
        //     {
        //         string packageJsonContents = File.ReadAllText(packageJsonPath);
        //         PackageJson packageJson = JsonUtility.FromJson<PackageJson>(packageJsonContents);
        //         Debug.Log($"prev package version {packageJson.version}");
        //     }
        // }
        //
        // if (!doUpdate) return;

        // copy the various files
        string toolsRoot = CroquetBuildToolsInPackage;
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
        Debug.Log("Running npm install...");

        // introducing even a short delay gives the console a chance to show the logged messages
        await Task.Delay(100);

        Task task = (Application.platform == RuntimePlatform.OSXEditor)
            ? new Task(() => InstallOSX(unityParentFolder, toolsRoot, nodePath))
            : new Task(() => InstallWin(unityParentFolder, toolsRoot));
        task.Start();
        task.Wait();
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

        LogProcessOutput(output, errors, "npm install");
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
        LogProcessOutput(output, errors, "npm install");
    }

    private static void LogProcessOutput(string stdout, string stderr, string prefix)
    {
        string[] newLines = stdout.Split('\n');
        foreach (string line in newLines)
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                string labeledLine = $"{prefix}: {line}";
                if (line.Contains("ERROR")) Debug.LogError(labeledLine);
                else if (line.Contains("WARNING")) Debug.LogWarning(labeledLine);
                else Debug.Log(labeledLine);
            }
        }
        newLines = stderr.Split('\n');
        foreach (string line in newLines)
        {
            if (!string.IsNullOrWhiteSpace(line)) Debug.LogError($"{prefix} error: {line}");
        }

    }
// this whole class is only defined when in the editor
#endif
}

// [System.Serializable]
// public class PackageJson
// {
//     public string version;
// }

