using System;
using System.IO;
using System.Diagnostics;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using Debug = UnityEngine.Debug;

public class CroquetBuilder
{
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
    private static CroquetRunner sceneRunnerComponent;
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
        // we assume that the bridge has the tag Bridge (presumably faster than trying
        // GetComponent() on every object)
        // ...but note that this doesn't guarantee that it has, specifically, a
        // CroquetBridge component.
        GameObject bridge = Array.Find<GameObject>(roots, o => o.CompareTag("Bridge"));
        if (bridge != null)
        {
            bridgeComp = bridge.GetComponent<CroquetBridge>();
            runnerComp = bridge.GetComponent<CroquetRunner>();
        }

        sceneName = scene.name;
        sceneBridgeComponent = bridgeComp;
        sceneRunnerComponent = runnerComp;
    }

    public static string CroquetBuildToolsInPackage = Path.GetFullPath("Packages/com.croquet.multiplayer/.JSTools");
    public static string NodeExeInPackage = Path.GetFullPath("Packages/com.croquet.multiplayer/.JSTools/NodeJS/node.exe");
    public static string NodeExeInBuild =
        Path.GetFullPath(Path.Combine(Application.streamingAssetsPath, "croquet-bridge", "node", "node.exe"));

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
            Debug.LogError("Cannot build without a path to node in the Settings object");
            return;
        }

        string builderPath = Path.GetFullPath(Path.Combine(Application.streamingAssetsPath, "..", "CroquetJS", "build-tools"));
    
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

            int webpackExit = -1;
            string exitPrefix = "webpack-exit=";
            string[] newLines = output.Split('\n');
            foreach (string line in newLines)
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    if (line.StartsWith(exitPrefix)) webpackExit = int.Parse(line.Substring(exitPrefix.Length));
                    else Debug.Log("JS builder: " + line);
                }
            }
            newLines = errors.Split('\n');
            foreach (string line in newLines)
            {
                if (!string.IsNullOrWhiteSpace(line)) Debug.LogError("JS builder error: " + line);
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

    public static void EnteredPlayMode()
    {
        // if there is a watcher, re-establish the process reporting its logs
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
    
#endif
}
