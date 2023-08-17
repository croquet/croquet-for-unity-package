using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using WebSocketSharp;
using WebSocketSharp.Server;
using WebSocketSharp.Net;

/// <summary>
/// There be dragons.
/// </summary>
public class CroquetBridge : MonoBehaviour
{
    public CroquetSettings appProperties;

    [Header("Session Configuration")]
    public bool useNodeJS;
    public string appName;
    public string defaultSessionName = "ABCDE";
    public string launchViaMenuIntoScene = "";
    private bool waitingForFirstScene = true;
    public bool debugForceSceneRebuild = false;
    public CroquetDebugTypes croquetDebugLogging;
    public CroquetLogForwarding JSLogForwarding;

    [Header("Session State")]
    public string croquetSessionState = "stopped"; // requested, running, stopped
    public string sessionName = "";
    public string croquetViewId;
    public int croquetViewCount;
    public string croquetActiveScene; // the scene currently being handled in the model
    public string croquetActiveSceneState; // the model's scene state (preload, loading, running)
    public string unitySceneState = "preparing"; // our scene state (preparing, ready, running)
    private List<CroquetActorManifest> sceneDefinitionManifests = new List<CroquetActorManifest>();
    private List<string> sceneHarvestList; // joined pairs  sceneName:appName
    private Dictionary<string, List<string>> sceneDefinitionsByApp =
        new Dictionary<string, List<string>>(); // appName to list of scene definitions

    [Header("Network Glitch Simulator")]
    public bool triggerGlitchNow = false;
    public float glitchDuration = 3.0f;

    private static string bridgeState = "stopped"; // needJSBuild, waitingForJSBuild, foundJSBuild, waitingForSocket, waitingForSessionName, waitingForSession, started

    HttpServer ws = null;
    WebSocketBehavior wsb = null; // not currently used
    static WebSocket clientSock = null;
    //static int sockMessagesReceived = 0;
    //static int sockMessagesSent = 0;
    public class QueuedMessage
    {
        public long queueTime;
        public bool isBinary;
        public byte[] rawData;
        public string data;
    }

    static ConcurrentQueue<QueuedMessage> messageQueue = new ConcurrentQueue<QueuedMessage>();
    static long estimatedDateNowAtReflectorZero = -1; // an impossible value

    List<(string,string)> deferredMessages = new List<(string,string)>(); // messages with (optionally) a throttleId for removing duplicates
    // static float messageThrottle = 0.035f; // should result in deferred messages being sent on every other FixedUpdate tick (20ms)
    // static float tickThrottle = 0.015f; // if not a bunch of messages, at least send a tick every 20ms
    // private float lastMessageSend = 0; // realtimeSinceStartup
    // private bool sentOnLastUpdate = false;

    LoadingProgressDisplay loadingProgressDisplay;

    public static CroquetBridge Instance { get; private set; }
    private CroquetRunner croquetRunner;

    public CroquetSystem[] croquetSystems = new CroquetSystem[0];

    private static Dictionary<string, List<(GameObject, Action<string>)>> croquetSubscriptions = new Dictionary<string, List<(GameObject, Action<string>)>>();
    private static Dictionary<GameObject, HashSet<string>> croquetSubscriptionsByGameObject =
        new Dictionary<GameObject, HashSet<string>>();

    // settings for logging and measuring (on JS-side performance log).  absence of an entry for a
    // category is taken as false.
    Dictionary<string, bool> logOptions = new Dictionary<string, bool>();
    static string[] logCategories = new string[] { "info", "session", "diagnostics", "debug", "verbose" };
    Dictionary<string, bool> measureOptions = new Dictionary<string, bool>();
    static string[] measureCategories = new string[] { "update", "bundle", "geom" };

    // TODO: Create Counter System in Metric Class
    // diagnostics counters
    int outMessageCount = 0;
    int outBundleCount = 0;
    int inBundleCount = 0;
    int inMessageCount = 0;
    long inBundleDelayMS = 0;
    float inProcessingTime = 0;
    float lastMessageDiagnostics; // realtimeSinceStartup

    private void SetBridgeState(string state)
    {
        bridgeState = state;
        Log("session", $"bridge state: {bridgeState}");
    }

    void Awake()
    {
        // Create Singleton Accessor
        // If there is an instance, and it's not me, delete myself.
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject); // take responsibility for removing the whole object
        }
        else
        {
            Instance = this;

            Application.runInBackground = true;

            SetCSharpLogOptions("info,session");
            SetCSharpMeasureOptions("bundle"); // for now, just report handling of message batches from Croquet

            croquetRunner = gameObject.GetComponent<CroquetRunner>();

#if UNITY_EDITOR
            string harvestScenes = CroquetBuilder.HarvestSceneList;
            if (harvestScenes != "")
            {
                // the overall string is a comma-separated list of sceneName:appName strings
                sceneHarvestList = new List<string>(harvestScenes.Split(','));
                CroquetBuilder.HarvestSceneList = ""; // clear immediately, in case something goes wrong
                SetBridgeState("needSceneHarvest");
            }
            else
            {
                if (!croquetRunner.runOffline &&
                    (appProperties.apiKey == "" || appProperties.apiKey == "PUT_YOUR_API_KEY_HERE"))
                {
                    Debug.LogWarning(
                        "No API key found in the Settings object; switching Croquet to run in Offline mode.");
                    croquetRunner.runOffline = true;
                }

                SetBridgeState("needJSBuild");
            }
#else
            SetBridgeState("foundJSBuild"); // assume that in a deployed app we always have a JS build
#endif

            DontDestroyOnLoad(gameObject);
            croquetSystems = gameObject.GetComponents<CroquetSystem>();
            Croquet.Subscribe("croquet", "viewCount", HandleViewCount);
        }
    }

    void Start()
    {
        // Frame cap
        Application.targetFrameRate = 60;

        SceneManager.activeSceneChanged += ChangedActiveScene; // in Start, so it's only set up once

        LoadingProgressDisplay loadingObj = FindObjectOfType<LoadingProgressDisplay>();
        if (loadingObj != null)
        {
            DontDestroyOnLoad(loadingObj.gameObject);
            loadingProgressDisplay = loadingObj.GetComponent<LoadingProgressDisplay>();
            loadingProgressDisplay.Hide(); // until it's needed
        }
    }

#if UNITY_EDITOR
    private async void WaitForJSBuild()
    {
        bool success = await CroquetBuilder.EnsureJSToolsAvailable()
                       && await CroquetBuilder.EnsureJSBuildAvailableToPlay();
        if (!success)
        {
            // error(s) will have already been reported
            EditorApplication.ExitPlaymode();
            return;
        }

        SetBridgeState("foundJSBuild"); // assume that in a deployed app we always have a JS build
    }
#endif

    public void SetSessionName(string newSessionName)
    {
        if (croquetRunner.runOffline)
        {
            sessionName = "offline";
            Debug.LogWarning("session name overridden for offline run");
        }
        else if (newSessionName == "")
        {
            sessionName = defaultSessionName;
            Log("session", $"session name defaulted to {defaultSessionName}");
        }
        else
        {
            sessionName = newSessionName;
            Log("session", $"session name set to {newSessionName}");
        }

    }

    private void ChangedActiveScene(Scene previous, Scene current)
    {
        // this is triggered when we've already arrived in the "current" scene.
        // either we arrived in the scene to harvest it, or - as long as Croquet is running
        // - we've arrived to play here.
        if (bridgeState == "waitingToHarvest" || croquetSessionState != "stopped")
        {
            ArrivedInGameScene(current);
        }
    }

    private void ArrivedInGameScene(Scene currentScene)
    {
        if (bridgeState != "waitingToHarvest")
        {
            if (currentScene.name != croquetActiveScene)
            {
                Debug.Log($"arrived in scene {currentScene.name} but waiting for {croquetActiveScene}");
                return;
            }

            waitingForFirstScene = false;
        }

        // immediately deactivate all Croquet objects, but keep a record of those that were active
        // in case we're asked to provide a scene definition
        sceneDefinitionManifests.Clear();
        CroquetActorManifest[] croquetObjects = FindObjectsByType<CroquetActorManifest>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (CroquetActorManifest manifest in croquetObjects)
        {
            GameObject go = manifest.gameObject;
            if (go.activeSelf)
            {
                sceneDefinitionManifests.Add(manifest);
                go.SetActive(false); // keep it around but invisible until we've read the manifest
            }
            else{
                Destroy(go); // not part of the definition; ditch it immediately
            }
        }

        // for now, the main thing we want to trigger is the loading of the scene-specific assets.
        // once they are ready, we'll tell Croquet the asset list for this scene, and also any
        // subscriptions set up by early awakening gameObjects in the scene.
        // if the scene is already running in Croquet, that information will be enough to trigger our
        // local bridge to create the ViewRoot, and hence the pawn manager that will tell us all the
        // pawns to make.
        // if the scene is *not* yet running in Croquet (in 'preload' state), we'll also send the
        // details to allow all clients to build the scene: the asset manifests, early subscriptions,
        // and the details of all pre-placed objects.  other clients may be sending the information
        // too; the first to ask gets permission.  the model on every client will initialise
        // itself with the scene's state, and then the client's view will wait for its Unity side
        // to be ready to load the scene; ours will immediately pass that test.
        foreach (CroquetSystem system in croquetSystems)
        {
            system.LoadedScene(currentScene.name);
        }
    }

    private void OnDestroy()
    {
        if (ws != null)
        {
            ws.Stop();
        }
    }

    public class CroquetBridgeWS : WebSocketBehavior
    {

        protected override void OnOpen()
        {
            if (clientSock != null)
            {
                Debug.LogWarning("Rejecting attempt to connect second client");
                Context.WebSocket.Send(String.Join('\x01', new string[]{ "log", "Rejecting attempt to connect second client" }));
                Context.WebSocket.Close(1011, "Rejecting duplicate connection");
                return;
            }

            // hint from https://github.com/sta/websocket-sharp/issues/236
            clientSock = Context.WebSocket;

            Instance.Log("session", "server socket opened");
        }

        protected override void OnMessage(MessageEventArgs e)
        {
            // bridge.Log("verbose", "received message in Unity: " + (e.IsBinary ? "binary" : e.Data));
            HandleMessage(e);
        }

        protected override void OnClose(CloseEventArgs e)
        {
            Instance.Log("session", System.String.Format("server socket closed {0}: {1}", e.Code, e.Reason));
        }
    }

    void StartWS()
    {
        // TODO: could try this workaround (effectively disabling Nagel), as suggested at
        // https://github.com/sta/websocket-sharp/issues/327
        //var listener = typeof(WebSocketServer).GetField("_listener", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(ws) as System.Net.Sockets.TcpListener;
        //listener.Server.NoDelay = true;
        // ...but I don't know how to apply this now that we're using HttpServer (plus WS service)
        // rather than WebSocketServer

        if (launchViaMenuIntoScene == "") SetLoadingStage(0.25f, "Connecting...");

        Log("session", "building WS Server on open port");
        int port = appProperties.preferredPort;
        int remainingTries = 9;
        bool goodPortFound = false;
        while (!goodPortFound && remainingTries > 0)
        {
            HttpServer wsAttempt = null;
            try
            {
                wsAttempt = new HttpServer(port);
                wsAttempt.AddWebSocketService<CroquetBridgeWS>("/Bridge", s => wsb = s);
                wsAttempt.KeepClean = false; // see comment in https://github.com/sta/websocket-sharp/issues/43
                wsAttempt.DocumentRootPath = Application.streamingAssetsPath; // set now, before Start()

                wsAttempt.Start();

                goodPortFound = true;
                ws = wsAttempt;
            }
            catch (Exception e)
            {
                Debug.Log($"Port {port} is not available");
                Log("debug", $"Error on trying port {port}: {e}");

                port++;
                remainingTries--;
                wsAttempt.Stop();
            }
        }

        if (!goodPortFound)
        {
            Debug.LogError("Cannot find an available port for the Croquet bridge");
#if UNITY_EDITOR
            EditorApplication.ExitPlaymode();
#endif
            return;
        }

        ws.OnHead += OnHeadHandler;
        ws.OnGet += OnGetHandler;

        Log("session", $"started HTTP/WS Server on port {port}");

#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
        string pathToNode = appProperties.pathToNode; // doesn't exist on Windows
#elif UNITY_EDITOR_WIN
        string pathToNode = CroquetBuilder.NodeExeInPackage;
#elif UNITY_STANDALONE_WIN || UNITY_WSA
        string pathToNode = CroquetBuilder.NodeExeInBuild;
#else
        string pathToNode = ""; // not available
#endif

        StartCoroutine(croquetRunner.StartCroquetConnection(port, appName, useNodeJS, pathToNode));
    }

    void OnHeadHandler(object sender, HttpRequestEventArgs e)
    {
        // extremely simple response.  always sets a ContentLength64 of zero (because otherwise
        // Chrome complains ERR_EMPTY_RESPONSE if there's no body of that length).  sets status
        // to 200 for a file that is present, and 204 for one that is not found.
        var req = e.Request;
        var res = e.Response;

        var path = req.Url.LocalPath;
        if (path == "/") path += "index.html";

        bool success = TryToGetFile(e, path, out byte[] contents);
        res.ContentLength64 = 0;
        res.StatusCode = success ? (int) HttpStatusCode.OK : (int) HttpStatusCode.NoContent;
    }

    void OnGetHandler(object sender, HttpRequestEventArgs e)
    {
        var req = e.Request;
        var res = e.Response;

        var path = req.Url.LocalPath;
        if (path == "/") path += "index.html";

        bool success = TryToGetFile(e, path, out byte[] contents);
        if (success)
        {
            if (path.EndsWith(".html"))
            {
                res.ContentType = "text/html";
                res.ContentEncoding = Encoding.UTF8;
            }
            else if (path.EndsWith(".js"))
            {
                res.ContentType = "application/javascript";
                res.ContentEncoding = Encoding.UTF8;
            }
            else if (path.EndsWith(".wasm"))
            {
                res.ContentType = "application/wasm";
            }

            res.ContentLength64 = contents.LongLength;

            res.Close(contents, true);
        }
        else
        {
            res.StatusCode = (int) HttpStatusCode.NotFound; // whatever the error
            // res.Close();  no need; will be done for us
        }
    }

    bool TryToGetFile(HttpRequestEventArgs e, string path, out byte[] contents)
    {
        bool success;

#if UNITY_ANDROID && !UNITY_EDITOR
        string src = Application.streamingAssetsPath + path;
        // Debug.Log("attempting to fetch " + src);
        var unityWebRequest = UnityWebRequest.Get(src);
        unityWebRequest.SendWebRequest();
        // until we figure out a way to incorporate an await or yield without
        // accidentally losing the HttpRequest along the way, using a busy-wait
        // is blunt but appears to get the job done.
        // note: "[isDone] will return true both when the UnityWebRequest
        // finishes successfully, or when it encounters a system error."
        while (!unityWebRequest.isDone) { }
        if (unityWebRequest.result != UnityWebRequest.Result.Success)
        {
            if (unityWebRequest.error != null) UnityEngine.Debug.Log(src + ": " + unityWebRequest.error);
            contents = new byte[0];
            success = false;
        }
        else
        {
            contents = unityWebRequest.downloadHandler.data; // binary
            success = true;
        }
        unityWebRequest.Dispose();
#else
        success = e.TryReadFile(path, out contents);
#endif

        return success;
    }

    // WebSocket messages come in on a separate thread.  Put each message on a queue to be
    // read by the main thread.
    // static because called from a class that doesn't know about this instance.
    static void HandleMessage(MessageEventArgs e) // string message)
    {
        // add a time so we can tell how long it sits in the queue
        QueuedMessage qm = new QueuedMessage();
        qm.queueTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
        qm.isBinary = e.IsBinary;
        if (e.IsBinary) qm.rawData = e.RawData;
        else qm.data = e.Data;
        messageQueue.Enqueue(qm);
    }

    void StartCroquetSession()
    {
        SetLoadingStage(0.5f, "Starting...");

        string debugLogTypes = croquetDebugLogging.ToString();
        // issue a warning if Croquet debug logging is enabled when not using an
        // external browser
        if (!croquetRunner.waitForUserLaunch && debugLogTypes != "")
        {
            Debug.LogWarning($"Croquet debug logging is set to \"{debugLogTypes}\"");
        }

        string debugFlags = debugLogTypes; // unless...
        if (croquetRunner.runOffline)
        {
            // @@ minor hack: Croquet treats "offline" as just another debug flag.  since in Unity
            // the option appears in the UI separately from the logging flags, add it in here.
            debugFlags = debugFlags == "" ? "offline" : $"{debugFlags},offline";
        }

        ReadyForSessionProps props = new ReadyForSessionProps()
        {
            apiKey = appProperties.apiKey,
            appId = appProperties.appPrefix + "." + appName,
            appName = appName,
            packageVersion =
                CroquetBuilder.FindJSToolsRecord().packageVersion, // uses different lookups in editor and in a build
            sessionName = sessionName,
            debugFlags = debugFlags
        };
        string propsJson = JsonUtility.ToJson(props);
        string[] command = new string[] {
            "readyForSession",
            propsJson
        };

        // send the message directly (bypassing the deferred-message queue)
        string msg = String.Join('\x01', command);
        clientSock.Send(msg);

        croquetSessionState = "requested";
    }

    [Serializable]
    class ReadyForSessionProps
    {
        public string apiKey;
        public string appId;
        public string appName;
        public string packageVersion;
        public string sessionName;
        public string debugFlags;
    }


    public void SendToCroquet(params string[] strings)
    {
        if (croquetSessionState != "running")
        {
            Debug.LogWarning($"attempt to send when Croquet session is not running: {string.Join(',', strings)}");
            return;
        }
        deferredMessages.Add(("", PackCroquetMessage(strings)));
    }

    public void SendToCroquetSync(params string[] strings)
    {
        // Aug 2023: now that we check for deferred messages every 20ms, this is currently identical to SendToCroquet()
        SendToCroquet(strings);
        // sentOnLastUpdate = false; // force to send on next tick [removed; see note in SendDeferredMessages]
    }

    public void SendThrottledToCroquet(string throttleId, params string[] strings)
    {
        // this simply replaces any message with the same throttleId that is waiting to be sent -
        // thus it only "throttles" to our frequency of processing deferred messages (currently 50Hz),
        int i = 0;
        int foundIndex = -1;
        foreach ((string throttle, string msg) entry in deferredMessages)
        {
            if (entry.throttle == throttleId)
            {
                foundIndex = i;
                break;
            }

            i++;
        }
        if (foundIndex != -1) deferredMessages.RemoveAt(i);

        deferredMessages.Add((throttleId, PackCroquetMessage(strings)));
    }

    public string PackCroquetMessage(string[] strings)
    {
        return String.Join('\x01', strings);
    }

    void SendDeferredMessages()
    {
        if (clientSock == null || clientSock.ReadyState != WebSocketState.Open) return;

        // we expect this to be called 50 times per second.  usually on every other call we send
        // deferred messages if there are any, otherwise send a tick.  expediting message sends
        // is therefore a matter of clearing the sentOnLastUpdate flag.
        // UPDATE (4 Aug 2023): limiting ticks/messages to 25 times per second rather than 50 seems
        // needlessly cautious, given websocket and JS engine capabilities.  see what happens if
        // we send something every time.
        // if (sentOnLastUpdate)
        // {
        //     sentOnLastUpdate = false;
        //     return;
        // }
        // sentOnLastUpdate = true;

        if (deferredMessages.Count == 0)
        {
            clientSock.Send("tick");
            return;
        }

        outBundleCount++;
        outMessageCount += deferredMessages.Count;

        // preface every bundle with the current time
        deferredMessages.Insert(0, ("", DateTimeOffset.Now.ToUnixTimeMilliseconds().ToString()));
        List<string> messageContents = new List<string>(); // strip out the throttle info
        foreach ((string throttle, string msg) entry in deferredMessages)
        {
            messageContents.Add(entry.msg);
        }
        string[] msgs = messageContents.ToArray<string>();
        clientSock.Send(String.Join('\x02', msgs));
        deferredMessages.Clear();
    }

    void Update()
    {
        if (bridgeState != "started") AdvanceBridgeStateWhenReady();
        else if (croquetSessionState == "running")
        {
            if (unitySceneState == "preparing" && SceneManager.GetActiveScene().name == croquetActiveScene)
            {
                bool ready = true;
                foreach (CroquetSystem system in croquetSystems)
                {
                    if (!system.ReadyToRunScene(croquetActiveScene)) ready = false;
                }

                if (ready)
                {
                    // Debug.Log($"ready to run scene \"{croquetActiveScene}\"");
                    unitySceneState = "ready";
                    TellCroquetWeAreReadyForScene();

                    foreach (CroquetSystem system in croquetSystems)
                    {
                        system.ClearSceneBeforeRunning();
                    }
                }
            }

            // things to check periodically while the session is supposedly in full flow
            if (triggerGlitchNow)
            {
                // @@ need to debounce this
                triggerGlitchNow = false; // cancel the request

                int milliseconds = (int) (glitchDuration * 1000f);
                if (milliseconds == 0) return;

                SendToCroquetSync("simulateNetworkGlitch", milliseconds.ToString());
            }
        }
    }

    void AdvanceBridgeStateWhenReady()
    {
        // go through the asynchronous steps involved in starting the bridge
#if UNITY_EDITOR
        if (bridgeState == "needSceneHarvest")
        {
            string sceneAndApp = sceneHarvestList[0];
            string sceneName = sceneAndApp.Split(':')[0];
            SetBridgeState("waitingToHarvest");
            Scene activeScene = SceneManager.GetActiveScene();
            if (activeScene.name == sceneName)
            {
                ArrivedInGameScene(activeScene); // will trigger the systems to initialise for this scene
            }
            else
            {
                SceneManager.LoadScene(sceneName);
            }
        }
        else if (bridgeState == "waitingToHarvest")
        {
            string sceneAndApp = sceneHarvestList[0];
            string sceneName = sceneAndApp.Split(':')[0];
            Scene activeScene = SceneManager.GetActiveScene();
            if (activeScene.name == sceneName)
            {
                bool ready = true;
                foreach (CroquetSystem system in croquetSystems)
                {
                    if (!system.ReadyToRunScene(sceneName)) ready = false;
                }
                if (!ready) return;

                string appName = sceneAndApp.Split(':')[1];
                HarvestSceneDefinition(sceneName, appName);

                // now move on to the next scene, if any
                sceneHarvestList.RemoveAt(0);
                if (sceneHarvestList.Count > 0)
                {
                    SetBridgeState("needSceneHarvest"); // go round again
                }
                else
                {
                    WriteAllSceneDefinitions();
                    EditorApplication.ExitPlaymode();
                }
            }
        }
        else if (bridgeState == "needJSBuild")
        {
            SetBridgeState("waitingForJSBuild");
            WaitForJSBuild();
            return;
        }
#endif

        if (bridgeState == "foundJSBuild")
        {
            SetBridgeState("waitingForSocket");
            StartWS();
        }
        else if (bridgeState == "waitingForSocket" && clientSock != null)
        {
            // configure which logs are forwarded
            SetJSLogForwarding(JSLogForwarding.ToString());

            SetBridgeState("waitingForSessionName");

            // if we're not waiting for a menu to launch the session, set the session name immediately
            if (launchViaMenuIntoScene == "") SetSessionName(""); // use the default name
        }
        else if (bridgeState == "waitingForSessionName" && sessionName != "")
        {
            SetBridgeState("waitingForSession");
            StartCroquetSession();
        }
    }

    void HarvestSceneDefinition(string sceneName, string appName)
    {
        // the scene is ready.  get its definition.
        Debug.Log($"ready to harvest scene \"{sceneName}\"");

        List<string> sceneStrings = new List<string>() {
            EarlySubscriptionTopicsAsString(),
            CroquetEntitySystem.Instance.assetManifestString
        };
        sceneStrings.AddRange(GetSceneDefinitionStrings());
        string sceneFullString = string.Join('\x01', sceneStrings.ToArray());

        // in the list we interleave scene name and scene definition, for convenience of parsing the assembled file
        if (!sceneDefinitionsByApp.ContainsKey(appName)) sceneDefinitionsByApp[appName] = new List<string>();
        sceneDefinitionsByApp[appName].AddRange(new []{ sceneName, sceneFullString });
        Debug.Log($"definition of {sceneFullString.Length} chars for scene {sceneName} in app {appName}");

    }

    void WriteAllSceneDefinitions()
    {
        // $$$$ make sure that if a scene doesn't provide a definition, we remove its def file
        foreach(KeyValuePair<string, List<string>> appScenes in sceneDefinitionsByApp)
        {
            string app = appScenes.Key;
            string appDefinitions = string.Join('\x02', appScenes.Value.ToArray());
            string filePath = Path.GetFullPath(Path.Combine(Application.streamingAssetsPath, "..", "CroquetJS", app, "scene-definitions.txt"));
            File.WriteAllText(filePath, appDefinitions);
        }

        sceneDefinitionsByApp.Clear();
    }

    void TellCroquetWeAreReadyForScene()
    {
        if (croquetActiveSceneState == "preload") SendDefineScene();
        else SendReadyForScene();

        sceneDefinitionManifests.Clear(); // no longer needed
    }

    void SendDefineScene()
    {
        // Debug.Log($"sending defineScene for {SceneManager.GetActiveScene().name}");

        // args to the command across the bridge are
        //   scene name - if different from model's existing scene, init will always be accepted
        //   earlySubscriptionTopics
        //   assetManifests
        //   object string 1
        //   object string 2
        //   etc
        List<string> commandStrings = new List<string>() {
            "defineScene",
            SceneManager.GetActiveScene().name,
            EarlySubscriptionTopicsAsString(),
            CroquetEntitySystem.Instance.assetManifestString
        };

        commandStrings.AddRange(GetSceneDefinitionStrings());

        // send the message directly (bypassing the deferred-message queue)
        string msg = String.Join('\x01', commandStrings.ToArray());
        clientSock.Send(msg);
    }

    List<string> GetSceneDefinitionStrings()
    {
        List<string> definitionStrings = new List<string>();

        Dictionary<string, string> abbreviations = new Dictionary<string, string>();
        int tokens = 0;
        // gather specs for all objects in the scene that have a CroquetActorManifest and are active
        foreach (CroquetActorManifest manifest in sceneDefinitionManifests)
        {
            // the properties for actor.create() are sent as a string prop1:val1|prop2:val2...
            List<string> initStrings = new List<string>();
            initStrings.Add($"ACTOR:{manifest.defaultActorClass}");
            initStrings.Add($"type:{manifest.pawnType}");
            GameObject go = manifest.gameObject;
            foreach (CroquetSystem system in croquetSystems)
            {
                initStrings.AddRange(system.InitializationStringsForObject(go));
            }

            List<string> convertedStrings = new List<string>();
            foreach (string pair in initStrings)
            {
                tokens++;
                if (!abbreviations.ContainsKey(pair))
                {
                    abbreviations.Add(pair, $"${abbreviations.Count}");
                    convertedStrings.Add(pair); // first and last time
                }
                else
                {
                    convertedStrings.Add(abbreviations[pair]);
                }
            }
            string oneObject = String.Join('|', convertedStrings.ToArray());
            definitionStrings.Add(oneObject);

            Destroy(go); // now that we have what we need
        }

        Log("session", $"scene definition with {tokens} tokens ({abbreviations.Count} unique)");
        return definitionStrings;
    }

    void SendReadyForScene()
    {
        // Debug.Log($"sending readyToRunScene for {SceneManager.GetActiveScene().name}");

        string sceneName = SceneManager.GetActiveScene().name;
        string[] command = new string[]
        {
            "readyToRunScene",
            sceneName
        };

        // send the message directly (bypassing the deferred-message queue)
        string msg = String.Join('\x01', command);
        clientSock.Send(msg);
    }

    void FixedUpdate()
    {
        long start = DateTimeOffset.Now.ToUnixTimeMilliseconds(); // in case we'll be reporting to Croquet

        ProcessCroquetMessages();

        SendDeferredMessages();

        long duration = DateTimeOffset.Now.ToUnixTimeMilliseconds() - start;
        if (duration == 0) duration++;
        if (croquetSessionState == "running")
        {
            Measure("update", start.ToString(), duration.ToString());

            float now = Time.realtimeSinceStartup;
            if (now - lastMessageDiagnostics > 1f)
            {
                if (inBundleCount > 0 || inMessageCount > 0)
                {
                    Log("diagnostics", $"from Croquet: {inMessageCount} messages with {inBundleCount} bundles ({Mathf.Round((float)inBundleDelayMS / inBundleCount)}ms avg delay) handled in {Mathf.Round(inProcessingTime * 1000)}ms");
                }

                //Log("diagnostics", $"to Croquet: {outMessageCount} messages with {outBundleCount} bundles");
                lastMessageDiagnostics = now;
                inBundleCount = 0;
                inMessageCount = 0;
                inBundleDelayMS = 0; // long
                inProcessingTime = 0;
                outBundleCount = outMessageCount = 0;
            }
        }
    }

    void ProcessCroquetMessages()
    {
        float start = Time.realtimeSinceStartup;
        QueuedMessage qm;
        while (messageQueue.TryDequeue(out qm))
        {
            long nowWhenQueued = qm.queueTime; // unixTimeMilliseconds
            long nowWhenDequeued = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            long queueDelay = nowWhenDequeued - nowWhenQueued;
            inBundleDelayMS += queueDelay;

            if (qm.isBinary)
            {
                byte[] rawData = qm.rawData;
                int sepPos = Array.IndexOf(rawData, (byte) 5);
                // Debug.Log(BitConverter.ToString(rawData));
                if (sepPos >= 1)
                {
                    byte[] timeAndCmdBytes = new byte[sepPos];
                    Array.Copy(rawData, timeAndCmdBytes, sepPos);
                    string[] strings = System.Text.Encoding.UTF8.GetString(timeAndCmdBytes).Split('\x02');
                    string command = strings[1];

                    ProcessCroquetMessage(command, rawData, sepPos + 1);

                    long sendTime = long.Parse(strings[0]);
                    long transmissionDelay = nowWhenQueued - sendTime;
                    long nowAfterProcessing = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                    long processing = nowAfterProcessing - nowWhenDequeued;
                    long totalTime = nowAfterProcessing - sendTime;
                    string annotation = $"{rawData.Length - sepPos - 1} bytes. sock={transmissionDelay}ms, queue={queueDelay}ms, process={processing}ms";
                    Measure("geom", sendTime.ToString(), totalTime.ToString(), annotation); // @@ assumed to be geometry
                }
                continue;
            }

            string nextMessage = qm.data;
            string[] messages = nextMessage.Split('\x02');
            if (messages.Length > 1)
            {
                // bundle of messages
                inBundleCount++;

                for (int i = 1; i < messages.Length; i++) ProcessCroquetMessage(messages[i]);

                // to measure message-processing performance, we gather
                //  JS now() when message was sent
                //  transmission delay (time until read and queued by C#)
                //  queue delay (time between queuing and dequeuing)
                //  processing time (time between dequeuing and completion)
                long sendTime = long.Parse(messages[0]); // first entry is just the JS Date.now() when sent
                long transmissionDelay = nowWhenQueued - sendTime;
                long nowAfterProcessing = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                long processing = nowAfterProcessing - nowWhenDequeued;
                long totalTime = nowAfterProcessing - sendTime;
                string annotation = $"{messages.Length - 1} msgs in {nextMessage.Length} chars. sock={transmissionDelay}ms, queue={queueDelay}ms, process={processing}ms";
                Measure("bundle", sendTime.ToString(), totalTime.ToString(), annotation);
            }
            else
            {
                // single message
                ProcessCroquetMessage(messages[0]);
            }
        }
        inProcessingTime += Time.realtimeSinceStartup - start;
    }

    /// <summary>
    /// Croquet String Message
    /// </summary>
    /// <param name="msg"></param>
    void ProcessCroquetMessage(string msg)
    {
        // a command message is an array of strings separated by \x01, of which the first is the command
        string[] strings = msg.Split('\x01');
        string command = strings[0]; // or a single piece of text, for logging
        string[] args = strings[1..];
        Log("verbose", command + ": " + String.Join(", ", args));

        if (command == "croquetPub")
        {
            ProcessCroquetPublish(args);
            return;
        }

        bool messageWasProcessed = false;

        foreach (CroquetSystem system in croquetSystems)
        {
            if (system.KnownCommands.Contains(command))
            {
                system.ProcessCommand(command, args);
                messageWasProcessed = true;
            }
        }

        if (command == "logFromJS") HandleLogFromJS(args);
        else if (command == "croquetPing") HandleCroquetPing(args[0]);
        else if (command == "setLogOptions") SetCSharpLogOptions(args[0]);  //OUT:LOGGER
        else if (command == "setMeasureOptions") SetCSharpMeasureOptions(args[0]);//OUT:METRICS
        else if (command == "joinProgress") HandleSessionJoinProgress(args[0]);
        else if (command == "sessionRunning") HandleSessionRunning(args[0]);
        else if (command == "sceneStateUpdated") HandleSceneStateUpdated(args);
        else if (command == "sceneRunning") HandleSceneRunning(args[0]);
        else if (command == "tearDownScene") HandleSceneTeardown();
        else if (command == "tearDownSession") HandleSessionTeardown(args[0]);
        else if (command == "croquetTime") HandleCroquetReflectorTime(args[0]);
        else if (!messageWasProcessed)
        {
            // not a known command; maybe just text for logging
            Log("info", "Unhandled Command From Croquet: " + msg);
        }

        inMessageCount++;
    }

    /// <summary>
    /// Croquet Byte Message
    /// </summary>
    /// <param name="command"></param>
    /// <param name="data"></param>
    /// <param name="startIndex"></param>
    void ProcessCroquetMessage(string command, byte[] data, int startIndex)
    {
        foreach (CroquetSystem system in croquetSystems)
        {
            if (system.KnownCommands.Contains(command))
            {
                system.ProcessCommand(command, data, startIndex);
                return;
            }
        }
    }

    public static void SubscribeToCroquetEvent(string scope, string eventName, Action<string> handler)
    {
        string topic = scope + ":" + eventName;
        if (!croquetSubscriptions.ContainsKey(topic))
        {
            croquetSubscriptions[topic] = new List<(GameObject, Action<string>)>();
            if (Instance != null && Instance.unitySceneState == "running")
            {
                Instance.SendToCroquet("registerForEventTopic", topic);
            }
        }
        croquetSubscriptions[topic].Add((null, handler));
    }

    public static void ListenForCroquetEvent(GameObject subscriber, string scope, string eventName, Action<string> handler)
    {
        // if this has been invoked before the object has its croquetActorId,
        // the scope will be an empty string.  in that case we still record the subscription,
        // but expect that FixUpEarlyListens will be invoked shortly to replace the
        // subscription with the correct (actor id) scope.

        string topic = scope + ":" + eventName;
        if (!croquetSubscriptions.ContainsKey(topic))
        {
            croquetSubscriptions[topic] = new List<(GameObject, Action<string>)>();
        }

        if (!croquetSubscriptionsByGameObject.ContainsKey(subscriber))
        {
            croquetSubscriptionsByGameObject[subscriber] = new HashSet<string>();
        }
        croquetSubscriptionsByGameObject[subscriber].Add(topic);

        croquetSubscriptions[topic].Add((subscriber, handler));
    }

    private string EarlySubscriptionTopicsAsString()
    {
        // gameObjects and scripts that start up before the Croquet view has been built are
        // allowed to request subscriptions to Croquet events.  when the bridge connection is
        // first made, we gather all existing subscriptions that have a null subscriber (i.e.,
        // are not pawn-specific Listens) and tell Croquet to be ready to send those events as
        // soon as the session starts.
        HashSet<string> topics = new HashSet<string>();
        foreach (string topic in croquetSubscriptions.Keys)
        {
            List<(GameObject, Action<string>)> subscriptions = croquetSubscriptions[topic];
            foreach ((GameObject gameObject, Action<string> handler) sub in subscriptions)
            {
                if (sub.gameObject == null)
                {
                    topics.Add(topic);
                }
            }
        }

        string joinedTopics = "";
        if (topics.Count > 0)
        {
            // Debug.Log($"sending {topics.Count} early-subscription topics");
            joinedTopics = string.Join(',', topics.ToArray());
        }
        return joinedTopics;
    }

    public static void UnsubscribeFromCroquetEvent(GameObject gameObject, string scope, string eventName,
        Action<string> forwarder)
    {
        // gameObject will be null for non-Listen subscriptions.
        // if gameObject is *not* null, we need to check whether the removal of this subscription
        // means that the topic can be removed from the list being listened to by this object.
        // that will be the case as long as there aren't subscriptions for the same gameObject and
        // same topic but with different handlers.
        string topic = scope + ":" + eventName;
        if (croquetSubscriptions.ContainsKey(topic))
        {
            int remainingSubscriptionsForSameObject = 0;
            (GameObject, Action<string>)[] subscriptions = croquetSubscriptions[topic].ToArray();
            foreach ((GameObject gameObject, Action<string> handler) sub in subscriptions)
            {
                if (sub.handler.Equals(forwarder))
                {
                    croquetSubscriptions[topic].Remove(sub);
                    if (croquetSubscriptions[topic].Count == 0)
                    {
                        // no remaining subscriptions for this topic at all
                        Debug.Log($"removed last subscription for {topic}");
                        croquetSubscriptions.Remove(topic);
                        if (Instance != null && Instance.unitySceneState == "running")
                        {
                            Instance.SendToCroquet("unregisterEventTopic", topic);
                        }
                    }
                }
                else if (gameObject != null && sub.gameObject.Equals(gameObject))
                {
                    remainingSubscriptionsForSameObject++;
                }
            }

            if (gameObject != null && remainingSubscriptionsForSameObject == 0)
            {
                Debug.Log($"removed {topic} from object's topic list");
                croquetSubscriptionsByGameObject[gameObject].Remove(topic);
            }
        }
    }

    public static void UnsubscribeFromCroquetEvent(string scope, string eventName, Action<string> forwarder)
    {
        UnsubscribeFromCroquetEvent(null, scope, eventName, forwarder);
    }

    public void FixUpEarlyListens(GameObject subscriber, string croquetActorId)
    {
        // in principle we could also use this as the time to send Say() events that were sent
        // before the actor id was known.  for now, those will just have been sent with
        // empty scopes (and therefore presumably ignored).
        if (croquetSubscriptionsByGameObject.ContainsKey(subscriber))
        {
            // Debug.Log($"removing all subscriptions for {gameObject}");
            string[] allTopics = croquetSubscriptionsByGameObject[subscriber].ToArray(); // take a copy
            foreach (string topic in allTopics)
            {
                if (topic.StartsWith(':'))
                {
                    // found a topic that was supposed to be a Listen.
                    // go through and find the relevant subscriptions for this gameObject,
                    // remove them, and make new subscriptions using the right scope.
                    (GameObject, Action<string>)[] subscriptions = croquetSubscriptions[topic].ToArray();
                    foreach ((GameObject gameObject, Action<string> handler) sub in subscriptions)
                    {
                        if (sub.gameObject == subscriber)
                        {
                            string eventName = topic.Split(':')[1];
                            // Debug.Log($"fixing up subscription to {eventName}");
                            ListenForCroquetEvent(subscriber, croquetActorId, eventName, sub.handler);

                            // then remove the dummy subscription
                            croquetSubscriptions[topic].Remove(sub);
                        }
                    }

                    // now remove the dummy topic from the subs by game object
                    croquetSubscriptionsByGameObject[subscriber].Remove(topic);
                }
            }
        }
    }

    public void RemoveCroquetSubscriptionsFor(GameObject subscriber)
    {
        if (croquetSubscriptionsByGameObject.ContainsKey(subscriber))
        {
            // Debug.Log($"removing all subscriptions for {gameObject}");
            foreach (string topic in croquetSubscriptionsByGameObject[subscriber])
            {
                (GameObject, Action<string>)[] subscriptions = croquetSubscriptions[topic].ToArray();
                foreach ((GameObject gameObject, Action<string> handler) sub in subscriptions)
                {
                    if (sub.gameObject == subscriber)
                    {
                        croquetSubscriptions[topic].Remove(sub);
                        if (croquetSubscriptions[topic].Count == 0)
                        {
                            // Debug.Log($"removed last subscription for {topic}");
                            croquetSubscriptions.Remove(topic);
                            if (unitySceneState == "running")
                            {
                                // don't even try to send if this is happening as part of a teardown
                                SendToCroquet("unregisterEventTopic", topic);
                            }
                        }
                    }
                }
            }

            croquetSubscriptionsByGameObject.Remove(subscriber);
        }
    }

    // might be useful at some point
    // void SimulateCroquetPublish(params string[] args)
    // {
    //     ProcessCroquetPublish(args);
    // }

    void ProcessCroquetPublish(string[] args)
    {
        // args are
        //   - scope
        //   - eventName
        //   - [optional]: arguments, encoded as a single string

        string scope = args[0];
        string eventName = args[1];
        string argString = args.Length > 2 ? args[2] : "";
        string topic = $"{scope}:{eventName}";
        if (croquetSubscriptions.ContainsKey(topic))
        {
            foreach ((GameObject gameObject, Action<string> handler) sub in croquetSubscriptions[topic].ToArray()) // take copy in case some mutating happens
            {
                sub.handler(argString);
            }
        }
    }

    void HandleCroquetPing(string time)
    {
        Log("diagnostics", "PING");
        SendToCroquet("unityPong", time);
    }

    void HandleCroquetReflectorTime(string time)
    {
        // this code assumes that JS and C# share system time (Date.now and
        // DateTimeOffset.Now.ToUnixTimeMilliseconds).
        // these messages are sent once per second.
        long newEstimate = long.Parse(time);
        if (estimatedDateNowAtReflectorZero == -1) estimatedDateNowAtReflectorZero = newEstimate;
        else
        {
            long oldEstimate = estimatedDateNowAtReflectorZero;
            int ratio = 50; // weight (percent) for the incoming value
            estimatedDateNowAtReflectorZero =
                (ratio * newEstimate + (100 - ratio) * estimatedDateNowAtReflectorZero) / 100;
            if (Math.Abs(estimatedDateNowAtReflectorZero - oldEstimate) > 10)
            {
                Debug.Log($"CROQUET TIME CHANGE: {estimatedDateNowAtReflectorZero - oldEstimate}ms");
            }
        }
    }

    public float CroquetSessionTime()
    {
        if (estimatedDateNowAtReflectorZero == -1) return -1f;

        return (DateTimeOffset.Now.ToUnixTimeMilliseconds() - estimatedDateNowAtReflectorZero) / 1000f;
    }

    void HandleLogFromJS(string[] args)
    {
        // args[0] is log type (log,warn,error)
        // args[1] is a best-effort single-string concatenation of whatever values were logged
        string type = args[0];
        string logText = args[1];
        switch (type)
        {
            case "log":
                Debug.Log("JS log: " + logText);
                break;
            case "warn":
                Debug.LogWarning("JS warning: " + logText);
                break;
            case "error":
                Debug.LogError("JS error: " + logText);
                break;
        }
    }

    void HandleSessionJoinProgress(string ratio)
    {
        if (unitySceneState == "running") return; // loading has finished; this is probably just a delayed message

        SetLoadingProgress(float.Parse(ratio));
    }

    void HandleSessionRunning(string viewId)
    {
        // this is dispatched from the Croquet session's PreloadingViewRoot constructor, telling us which
        // viewId we have in the session
        croquetViewId = viewId;
        Log("session", "Croquet session running!");
        SetBridgeState("started");
        croquetSessionState = "running";
        lastMessageDiagnostics = Time.realtimeSinceStartup;
        estimatedDateNowAtReflectorZero = -1; // reset, to accept first value from new view

        // when starting directly from a scene that has the debugForceSceneRebuild
        // flag set, we ask Croquet to abandon whatever scene it had running and reload
        // this one.
        if (waitingForFirstScene && debugForceSceneRebuild)
        {
            Croquet.RequestToLoadScene(SceneManager.GetActiveScene().name, true, true);
        }
    }

    void HandleSceneStateUpdated(string[] args)
    {
        // args are [activeScene, activeSceneState]

        // this is sent by the PreloadingViewRoot, under the following circs:
        // - construction of the ViewRoot, forwarding the InitializationManager's current state (which could
        //   be anything, including presence of no scene at all when the session first starts)
        // - on every update in scene name, or of scene state (preload, loading, running)

        // if the state change implies a reboot of the view (a new scene, or a switch from 'running' to
        // 'loading' for a reload), any previous ViewRoot will already have been destroyed, triggering
        // a tearDownScene command that we use to clear out all Croquet-managed gameObjects.

        croquetActiveScene = args[0];
        croquetActiveSceneState = args[1];
        Log("session", $"Croquet scene \"{croquetActiveScene}\", state \"{croquetActiveSceneState}\"");

        if (waitingForFirstScene && debugForceSceneRebuild) {
            // on session startup with debugForceSceneRebuild, the first scene load is triggered in
            // HandleSessionRunning.  when the Croquet session reveals that it has arrived at preload
            // for that scene (whatever it was doing before), we can start preparing the scene here to
            // provide its definition.
            // if the user later moves on to other scenes, and perhaps even comes back to this one,
            // we use the normal state-change handling below.
            if (croquetActiveScene == "") return; // get back to us when you have a scene

            Scene currentScene = SceneManager.GetActiveScene();
            if (croquetActiveScene == currentScene.name && croquetActiveSceneState == "preload")
            {
                ArrivedInGameScene(currentScene); // we were already in the right scene
            }
            return; // nothing more to do here
        }

        // if croquet doesn't have an active scene, propose a switch to the first game-level scene
        if (croquetActiveScene == "")
        {
            // propose to Croquet that we load the initial game scene
            if (launchViaMenuIntoScene == "")
            {
                Debug.Log("No initial scene name set; requesting to load current scene");
                string sceneName = SceneManager.GetActiveScene().name;
                Croquet.RequestToLoadScene(sceneName, debugForceSceneRebuild, debugForceSceneRebuild);
            }
            else
            {
                Croquet.RequestToLoadScene(launchViaMenuIntoScene, debugForceSceneRebuild, debugForceSceneRebuild);
            }
        }
        else if (waitingForFirstScene && croquetActiveScene == SceneManager.GetActiveScene().name)
        {
            // we're just getting started, and Croquet has an active scene - perhaps because
            // we requested it above.  if we're already there, start preparing.
            Scene currentScene = SceneManager.GetActiveScene();
            ArrivedInGameScene(currentScene);
        }
        else if (croquetActiveScene != SceneManager.GetActiveScene().name)
        {
            // Croquet has switched to a scene that we're not currently in.  we need to head
            // over there.
            // Debug.Log($"preparing for scene {croquetActiveScene}");
            unitySceneState = "preparing"; // will trigger repeated checks until we can tell Croquet we're ready (with assets, etc)
            if (loadingProgressDisplay && !loadingProgressDisplay.gameObject.activeSelf)
            {
                // we won't have a chance to set different loading stages.  run smoothly from 0 to 1,
                // and we'll probably have arrived.
                SetLoadingStage(1.0f, "Loading...");
            }
            SceneManager.LoadScene(croquetActiveScene);
        }
        else if (croquetActiveSceneState == "running" && unitySceneState == "preparing")
        {
            // this will happen when recovering from a Croquet network glitch... and maybe no other situation
            ArrivedInGameScene(SceneManager.GetActiveScene());
        }
    }

    public void RequestToLoadScene(string sceneName, bool forceReload, bool forceRebuild)
    {
        string[] cmdAndArgs =
        {
            "requestToLoadScene",
            sceneName,
            forceReload.ToString(),
            forceRebuild.ToString()
        };
        SendToCroquet(cmdAndArgs);
    }

    void HandleSceneRunning(string sceneName)
    {
        // triggered by the startup of a GameRootView
        Log("session", $"Croquet view for scene {sceneName} running");
        if (loadingProgressDisplay != null) loadingProgressDisplay.Hide();
        unitySceneState = "running"; // we're off!
    }

    void HandleSceneTeardown()
    {
        // this is triggered by the PreloadingViewRoot when it destroys the game's running viewRoot as part
        // of a scene switch
        Log("session", "Croquet scene teardown");
        deferredMessages.Clear();
        unitySceneState = "preparing"; // ready to load the next
        foreach (CroquetSystem system in croquetSystems)
        {
            system.TearDownScene();
        }
    }

    void HandleSessionTeardown(string postTeardownScene)
    {
        // this is triggered by the disappearance (temporary or otherwise) of the Croquet session,
        // or the processing of a "shutdown" command sent from here.  in the latter case, we'll have
        // specified which scene is to be loaded locally in order to stay in the game.  typically a
        // menu scene.
        string postTeardownMsg = postTeardownScene == "" ? "" : $" (and jump to {postTeardownScene})";
        Log("session", $"Croquet session teardown{postTeardownMsg}");
        deferredMessages.Clear();
        croquetSessionState = "stopped"; // suppresses sending of any further messages over the bridge
        foreach (CroquetSystem system in croquetSystems)
        {
            system.TearDownSession();
        }

        croquetViewId = "";
        croquetActiveScene = ""; // wait for session to resume and tell us the scene
        croquetActiveSceneState = "";

        if (postTeardownScene != "")
        {
            sessionName = ""; // the session has really gone
            SetBridgeState("waitingForSessionName");

            int buildIndex = int.Parse(postTeardownScene);
            SceneManager.LoadScene(buildIndex);
        }
        else
        {
            // probably a glitch in the Croquet network connection.  should resume shortly.
            if (loadingProgressDisplay && !loadingProgressDisplay.gameObject.activeSelf)
            {
                SetLoadingStage(0.5f, "Reconnecting...");
            }
        }
    }

    void HandleViewCount(float viewCount)
    {
        croquetViewCount = (int)viewCount;
    }

    // OUT: Logger Util
    void SetCSharpLogOptions(string options)
    {
        // logs that the Croquet side wants the C# side to send.
        // arg is a comma-separated list of the log categories to show
        string[] wanted = options.Split(',');
        foreach (string cat in logCategories)
        {
            logOptions[cat] = wanted.Contains(cat);
        }

        // and display options
        logOptions["routeToCroquet"] = wanted.Contains("routeToCroquet");
    }

    // OUT: Metrics system util
    void SetCSharpMeasureOptions(string options)
    {
        // arg is a comma-separated list of the measure categories (currently
        // available are bundle,geom,update) to send to Croquet to appear as
        // marks in a Chrome performance plot.

        string[] wanted = options.Split(',');
        foreach (string cat in measureCategories)
        {
            measureOptions[cat] = wanted.Contains(cat);
        }
    }

    void SetJSLogForwarding(string optionString)
    {
        // first arg is a comma-separated list of the log types (log,warn,error) that we want
        // the JS side to send for logging here
        // second is a stringified boolean of waitForUserLaunch
        string[] cmdAndArgs = { "setJSLogForwarding", optionString, croquetRunner.waitForUserLaunch.ToString() };

        // send the message directly (bypassing the deferred-message queue), because this can
        // be sent regardless of whether a session is running
        string msg = String.Join('\x01', cmdAndArgs);
        clientSock.Send(msg);
    }

    void SetLoadingStage(float ratio, string msg)
    {
        if (loadingProgressDisplay == null) return;

        loadingProgressDisplay.Show(); // make sure it's visible
        loadingProgressDisplay.SetProgress(ratio, msg);
    }

    void SetLoadingProgress(float loadRatio)
    {
        if (loadingProgressDisplay == null) return;

        // fast-forward progress 0=>1 is mapped onto bar 50=>100%
        float barRatio = loadRatio * 0.5f + 0.5f;
        loadingProgressDisplay.Show(); // make sure it's visible (especially on a reload)
        loadingProgressDisplay.SetProgress(barRatio, $"Loading... ({loadRatio * 100:#0.0}%)");
    }

    // OUT: Logging System
    public void Log(string category, string msg)
    {
        bool loggable;
        if (logOptions.TryGetValue(category, out loggable) && loggable)
        {
            string logString = $"{DateTimeOffset.Now.ToUnixTimeMilliseconds() % 100000}: {msg}";
            if (logOptions.TryGetValue("routeToCroquet", out loggable) && loggable)
            {
                SendToCroquet("log", logString);
            }
            else
            {
                Debug.Log(logString);
            }
        }
    }

    // OUT metrics system
    void Measure(params string[] strings)
    {
        string category = strings[0];
        bool loggable;
        if (measureOptions.TryGetValue(category, out loggable) && loggable)
        {
            string[] cmdString = { "measure" };
            string[] cmdAndArgs = cmdString.Concat(strings).ToArray();
            SendToCroquet(cmdAndArgs);
        }
    }

}


[System.Serializable]
public class CroquetDebugTypes
{
    public bool session;
    public bool messages;
    public bool sends;
    public bool snapshot;
    public bool data;
    public bool hashing;
    public bool subscribe;
    public bool classes;
    public bool ticks;

    public override string ToString()
    {
        List<string> flags = new List<string>();
        if (session) flags.Add("session");
        if (messages) flags.Add("messages");
        if (sends) flags.Add("sends");
        if (snapshot) flags.Add("snapshot");
        if (data) flags.Add("data");
        if (hashing) flags.Add("hashing");
        if (subscribe) flags.Add("subscribe");
        if (classes) flags.Add("classes");
        if (ticks) flags.Add("ticks");

        return string.Join(',', flags.ToArray());
    }
}

[System.Serializable]
public class CroquetLogForwarding
{
    public bool log = false;
    public bool warn = true;
    public bool error = true;

    public override string ToString()
    {
        List<string> flags = new List<string>();
        if (log) flags.Add("log");
        if (warn) flags.Add("warn");
        if (error) flags.Add("error");

        return string.Join(',', flags.ToArray());
    }
}
