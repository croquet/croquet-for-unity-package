using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using WebSocketSharp;
using WebSocketSharp.Server;
using WebSocketSharp.Net;

/// <summary>
/// There be dragons.
/// </summary>
public class CroquetBridge : MonoBehaviour
{
    public CroquetSettings appProperties;
    public string appName;
    public bool useNodeJS;
    public bool croquetSessionRunning = false;
    public string croquetViewId;
    
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

    List<string> deferredMessages = new List<string>();
    static float messageThrottle = 0.05f; // 50ms
    float lastMessageSend = 0; // realtimeSinceStartup

    LoadingProgressDisplay loadingProgressDisplay;
    public bool loadingInProgress = true;
    
    public static CroquetBridge Instance { get; private set; }
    private CroquetRunner croquetRunner;

    private CroquetSystem[] croquetSystems = new CroquetSystem[0];
    
    // DEPRECATED
    public static void SendCroquet(params string[] strings)
    {
        Instance.SendToCroquet(strings);    
    }

    // DEPRECATED
    public static void SendCroquetSync(params string[] strings)
    {
        Instance.SendToCroquetSync(strings);   
    }

    private Dictionary<string, List<(GameObject, Action<string>)>> croquetSubscriptions = new Dictionary<string, List<(GameObject, Action<string>)>>();
    private Dictionary<GameObject, HashSet<string>> croquetSubscriptionsByGameObject =
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
    
    
    void Awake()
    {
        // Create Singleton Accessor
        // If there is an instance, and it's not me, delete myself.
        if (Instance != null && Instance != this) 
        { 
            Destroy(this);
        }
        else 
        { 
            Instance = this; 
        } 
        croquetRunner = gameObject.GetComponent<CroquetRunner>();
        LoadingProgressDisplay loadingObj = FindObjectOfType<LoadingProgressDisplay>();
        if (loadingObj != null)
        {
            loadingProgressDisplay = loadingObj.GetComponent<LoadingProgressDisplay>();
        }
        croquetSystems = gameObject.GetComponents<CroquetSystem>();
        
        SetCSharpLogOptions("info,session");
        SetCSharpMeasureOptions("bundle,geom"); // @@ typically useful for development
    }

    public void RegisterSystem(CroquetSystem system)
    {
        croquetSystems.Append(system);
    }

    void Start()
    {
        // Frame cap
        Application.targetFrameRate = 60;

        SetLoadingStage(0, "Starting...");
        
        lastMessageDiagnostics = Time.realtimeSinceStartup;
        
        // StartWS will be called to set up the websocket, and hence the session
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
            // hint from https://github.com/sta/websocket-sharp/issues/236
            clientSock = Context.WebSocket;

            Instance.Log("session", "server socket opened");

            // if not running externally, set JS warnings and errors to be logged here
            if (!Instance.croquetRunner.waitForUserLaunch)
            {
                Instance.SetJSLogForwarding("warn,error");
            }

            string apiKey = Instance.appProperties.apiKey;
            string appId = Instance.appProperties.appPrefix + "." + Instance.appName;
            string sessionName = Instance.sessionNameValue.ToString();
            string assetManifests = CroquetEntitySystem.Instance.assetManifestString;
            string earlySubscriptionTopics = Instance.EarlySubscriptionTopicsAsString();
            
            string[] command = new string[] {
                "readyForSession",
                apiKey,
                appId,
                sessionName,
                assetManifests,
                earlySubscriptionTopics
            };

            // send the message directly (bypassing the deferred-message queue)
            string msg = String.Join('\x01', command);
            clientSock.Send(msg);
            
            Instance.SetLoadingStage(0.50f, "bridge connected");
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

    // TODO: remove sentinel value
    public int sessionNameValue = -999; // @@ when running in the editor it would be good to see this; figure out how to make it read-only
    
    public bool simulateNetworkGlitch = false;
    public float networkGlitchDuration = 3.0f;

    void StartWS()
    {
        // TODO: could try this workaround (effectively disabling Nagel), as suggested at
        // https://github.com/sta/websocket-sharp/issues/327
        //var listener = typeof(WebSocketServer).GetField("_listener", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(ws) as System.Net.Sockets.TcpListener;
        //listener.Server.NoDelay = true;

#if UNITY_EDITOR
        if (appProperties.apiKey == "" || appProperties.apiKey == "PUT_YOUR_API_KEY_HERE")
        {
            Debug.LogError("Cannot play without a valid API key in the Settings object");
            EditorApplication.ExitPlaymode();
            return;
        }
#endif
        
        // recover a specific session name from PlayerPrefs
        sessionNameValue = PlayerPrefs.GetInt("sessionNameValue", 1);
        
        Log("session", "building WS Server on open port");
        int port = appProperties.preferredPort;
        int maximumTries = 9;
        bool goodPortFound = false;
        while (!goodPortFound && maximumTries>0)
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
                Debug.Log($"Exception detected for port {port}:{e}");
                port++;
                maximumTries--;
                wsAttempt.Stop();
            }
        }

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

        SetLoadingStage(0.25f, "Ready to connect bridge");
        StartCoroutine(croquetRunner.StartCroquetConnection(port, appName, useNodeJS, pathToNode));
    }

    void OnGetHandler(object sender, HttpRequestEventArgs e)
    {
        var req = e.Request;
        var res = e.Response;

        var path = req.Url.LocalPath;

        if (path == "/")
            path += "index.html";

        byte[] contents;

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
            res.StatusCode = (int) HttpStatusCode.NotFound; // whatever the error
        }
        else
        {
            contents = unityWebRequest.downloadHandler.data; // binary
        }
        unityWebRequest.Dispose();
#else
        if (!e.TryReadFile (path, out contents)) {
            res.StatusCode = (int) HttpStatusCode.NotFound;
        }
#endif

        if (path.EndsWith (".html")) {
            res.ContentType = "text/html";
            res.ContentEncoding = Encoding.UTF8;
        }
        else if (path.EndsWith (".js")) {
            res.ContentType = "application/javascript";
            res.ContentEncoding = Encoding.UTF8;
        }
        else if (path.EndsWith (".wasm")) {
            res.ContentType = "application/wasm";
        }

        res.ContentLength64 = contents.LongLength;

        res.Close (contents, true);
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

    public void SendToCroquet(params string[] strings)
    {
        deferredMessages.Add(PackCroquetMessage(strings));
    }

    public void SendToCroquetSync(params string[] strings)
    {
        SendToCroquet(strings);
        SendDeferredMessages();
    }
    
    public string PackCroquetMessage(string[] strings)
    {
        return String.Join('\x01', strings);
    }

    void SendDeferredMessages()
    {
        if (clientSock == null || clientSock.ReadyState != WebSocketState.Open || deferredMessages.Count == 0) return;
        float now = Time.realtimeSinceStartup;
        if (now - lastMessageSend < messageThrottle) return;

        lastMessageSend = now;

        outBundleCount++;
        outMessageCount += deferredMessages.Count;

        // preface every bundle with the current time
        deferredMessages.Insert(0, DateTimeOffset.Now.ToUnixTimeMilliseconds().ToString());
        string[] msgs = deferredMessages.ToArray<string>();
        clientSock.Send(String.Join('\x02', msgs));
        deferredMessages.Clear();
    }

    void Update()
    {
        // before WS has been started, check whether we're ready to do so
        if (sessionNameValue == -999 && CroquetEntitySystem.Instance.addressablesReady)
        {
            StartWS();
        }
        if (!loadingInProgress && croquetSessionRunning)
        {
            if (loadingProgressDisplay)
            {
                // @@ ought to do this just once
                loadingProgressDisplay.Hide();
            }

            if (simulateNetworkGlitch)
            {
                simulateNetworkGlitch = false; // cancel the request

                int milliseconds = (int) (networkGlitchDuration * 1000f);
                if (milliseconds == 0) return;
                
                SendToCroquetSync("simulateNetworkGlitch", milliseconds.ToString());
            }
        }
    }
    
    void FixedUpdate()
    {
        long start = DateTimeOffset.Now.ToUnixTimeMilliseconds(); // in case we'll be reporting to Croquet
        
        ProcessCroquetMessages();

        SendDeferredMessages();

        long duration = DateTimeOffset.Now.ToUnixTimeMilliseconds() - start;
        if (duration == 0) duration++;
        if (croquetSessionRunning) Measure("update", start.ToString(), duration.ToString());

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
                    int count = 0;
                    
                    ProcessCroquetMessage(command, rawData, sepPos + 1);
                    
                    long sendTime = long.Parse(strings[0]);
                    long transmissionDelay = nowWhenQueued - sendTime;
                    long nowAfterProcessing = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                    long processing = nowAfterProcessing - nowWhenDequeued;
                    long totalTime = nowAfterProcessing - sendTime;
                    string annotation = $"{count} objects, {rawData.Length - sepPos - 1} bytes. sock={transmissionDelay}ms, queue={queueDelay}ms, process={processing}ms";
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
        else if (command == "joinProgress") HandleJoinProgress(args[0]);
        else if (command == "croquetSessionRunning") HandleSessionRunning(args);
        else if (command == "tearDownSession") TearDownSession();
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

    public void SubscribeToCroquetEvent(string scope, string eventName, Action<string> handler)
    {
        string topic = scope + ":" + eventName;
        if (!croquetSubscriptions.ContainsKey(topic))
        {
            croquetSubscriptions[topic] = new List<(GameObject, Action<string>)>();
            if (croquetSessionRunning)
            {
                SendToCroquet("registerForEventTopic", topic);
            }
        }
        croquetSubscriptions[topic].Add((null, handler));
    }
    
    public void ListenForCroquetEvent(GameObject subscriber, string scope, string eventName, Action<string> handler)
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
            Debug.Log($"sending {topics.Count} early-subscription topics");
            joinedTopics = string.Join(',', topics.ToArray());
        }
        return joinedTopics;
    }
    
    public void UnsubscribeFromCroquetEvent(GameObject gameObject, string scope, string eventName,
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
                        if (croquetSessionRunning)
                        {
                            SendToCroquet("unregisterEventTopic", topic);
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

    public void UnsubscribeFromCroquetEvent(string scope, string eventName, Action<string> forwarder)
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
                            SendToCroquet("unregisterEventTopic", topic);
                        }
                    }
                }
            }

            croquetSubscriptionsByGameObject.Remove(subscriber);
        }
    }
    
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

    void HandleJoinProgress(string ratio)
    {
        SetLoadingProgress(float.Parse(ratio));
    }

    void HandleSessionRunning(string[] args)
    {
        Log("session", "Croquet session running!");
        croquetSessionRunning = true;
        croquetViewId = args[0];
        loadingInProgress = false;
    }

    void TearDownSession()
    {
        Log("session", "Croquet session teardown now happening!");
        croquetSessionRunning = false;
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
        // arg is a comma-separated list of the measure categories to send
        string[] wanted = options.Split(',');
        foreach (string cat in measureCategories)
        {
            measureOptions[cat] = wanted.Contains(cat);
        }
    }

    void SetJSLogForwarding(string optionString)
    {
        // arg is a comma-separated list of the log types (log,warn,error) that we want
        // the JS side to send for logging here
        string[] cmdAndArgs = { "setJSLogForwarding", optionString };
        SendToCroquet(cmdAndArgs);
    }
    
    void SetLoadingStage(float ratio, string msg)
    {
        if (loadingProgressDisplay == null) return;
        
        loadingProgressDisplay.SetProgress(ratio, msg);
    }

    void SetLoadingProgress(float loadRatio)
    {
        if (loadingProgressDisplay == null) return;

        // fast-forward progress 0=>1 is mapped onto bar 50=>100%
        float barRatio = loadRatio * 0.5f + 0.5f;
        loadingProgressDisplay.SetProgress(barRatio, $"Loading... ({loadRatio * 100:#0.0}%)");
    }

    // OUT: Logging System
    void Log(string category, string msg)
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

