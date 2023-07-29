// Worldcore with Unity
//
// Croquet Corporation, 2023

// note on use of string separators in messages across the bridge
//   \x01 is used to separate the command and string arguments in a message
//   \x02 to separate entire messages in a bundle
//   \x03 to separate the elements in an array-type argument within a message, such as on a property update, say(), or publish()
//   \x04 currently unused
//   \x05 to mark the start of the data argument in a binary-encoded message, such as updateSpatial.


import { ModelService, Actor, RegisterMixin, mix, Pawn, View, ViewRoot, ViewService, GetViewService, StartWorldcore, PawnManager, v3_equals, q_equals, q_normalize } from "@croquet/worldcore-kernel";

globalThis.timedLog = msg => {
    // timing on the message itself is now added when forwarding
    const toLog = `${(globalThis.CroquetViewDate || Date).now() % 100000}: ${msg}`;
    performance.mark(toLog);
    console.log(msg);
};

// globalThis.WC_Left = true; // NB: this can affect behaviour of both models and views
globalThis.CROQUET_NODE = typeof window === 'undefined';


let theGameInputManager, session, sessionOffsetEstimator;

// theGameEngineBridge is a singleton instance of BridgeToUnity, built immediately
// on loading of this file.  it is never rebuilt.
class BridgeToUnity {
    get preloadingView() { return session?.view }

    constructor() {
        this.bridgeIsConnected = false;
        this.startWS();
        // readyP is resolved on receipt of the readyForSession command from
        // Unity, which will have included the apiKey, appId etc needed to
        // join a Croquet session.
        this.readyP = new Promise(resolve => this.setReady = resolve);
        this.measureIndex = 0;
    }

    setCommandHandler(handler) {
        this.commandHandler = handler;
    }

    resetMessageStats() {
        this.msgStats = { outMessageCount: 0, outBundleCount: 0, inBundleCount: 0, inMessageCount: 0, inBundleDelayMS: 0, inProcessingTimeMS: 0, lastMessageDiagnostics: Date.now() };
    }

    startWS() {
        globalThis.timedLog('starting socket client');
        const portStr = (!globalThis.CROQUET_NODE
            ? window.location.port
            : process.argv[2])
            || '5555';
console.log(`PORT ${portStr}`);
        const sock = this.socket = new WebSocket(`ws://127.0.0.1:${portStr}/Bridge`);
        sock.onopen = _evt => {
            // prepare for Unity to ask for some of the JS logs (see 'setJSLogForwarding' below)
            if (!console.q_log) {
                console.q_log = console.log;
                console.q_warn = console.warn;
                console.q_error = console.error;
            }

            globalThis.timedLog('opened socket');
            this.bridgeIsConnected = true;
            this.resetMessageStats();
            sock.onmessage = event => {
                const msg = event.data;
                if (msg !== 'tick') this.handleUnityMessageOrBundle(msg);
                if (ticker) ticker();
            };
        };
        sock.onclose = _evt => {
            globalThis.timedLog('bridge websocket closed');
            this.bridgeIsConnected = false;
            if (session) session.leave();
            if (globalThis.CROQUET_NODE) process.exit(); // if on node, bail out
        };
        sock.onerror = evt => console.error("bridge WebSocket error", evt);
    }

    sendCommand(...args) {
        if (args.findIndex(a => typeof a !== "string") >= 0) {
            console.warn("Command and arguments must be strings; not sending", args);
            return;
        }
        const msg = [...args].join('\x01');
        this.sendToUnity(msg);

        this.msgStats.outMessageCount++; // @@ stats don't really expect non-bundled messages
    }

    sendBundleToUnity(messages) {
        // prepend the current time
        messages.unshift(String(Date.now()));
        const multiMsg = messages.join('\x02');
        this.sendToUnity(multiMsg);

        const { msgStats } = this;
        msgStats.outBundleCount++;
        msgStats.outMessageCount += messages.length;

        return multiMsg.length;
    }

    sendToUnity(msg) {
        if (!this.socket) return; // @@ need to do better than just silently dropping
        // console.log('sending to Unity', msg);
        this.socket.send(msg);
    }

    encodeValueAsString(arg) {
        // when sending a property value as part of a message over the bridge,
        // elements of an array are separated with \x03
        return Array.isArray(arg)
            ? arg.join('\x03')
            : typeof arg === 'boolean'
                ? arg ? 'True' : 'False'
                : String(arg);
    }

    handleUnityMessageOrBundle(msg) {
        // handle a single or multiple message from Unity
        const start = performance.now();
        const { msgStats } = this;
        const msgs = msg.split('\x02');
        if (msgs.length > 1) {
            msgStats.inBundleCount++;
            const sendTime = Number(msgs.shift());
            const diff = Date.now() - sendTime;
            msgStats.inBundleDelayMS += diff;
        }
        msgs.forEach(m => {
            const strings = m.split('\x01');
            const command = strings[0];
            const args = strings.slice(1);
            this.handleUnityCommand(command, args);
            msgStats.inMessageCount++;
        });
        msgStats.inProcessingTimeMS += performance.now() - start;
    }

    handleUnityCommand(command, args) {
        // console.log('command from Unity: ', { command, args });
        switch (command) {
            case 'setJSLogForwarding': {
                // args[0] is comma-separated list of log types (log,warn,error)
                // that are to be sent over to Unity
                const toForward = args[0].split(',');
                this.setJSLogForwarding(toForward);
                break;
            }
            case 'readyForSession': {
                const [apiKey, appId, sessionName, debugLogTypes, waitForUserLaunchStr] = args;
                globalThis.timedLog(`starting session of ${appId} with key ${apiKey}`);
                this.apiKey = apiKey;
                this.appId = appId;
                this.sessionName = sessionName;
                this.debugLogTypes = debugLogTypes; // comma-separated list
                // if this is an auto-launched session (i.e., not an external browser), set up logging so that warnings and errors appear in the Unity console.  for an external browser, we forward nothing.
                // @@ should be configurable (especially so it can be turned off
                // for a build).
                this.setJSLogForwarding(waitForUserLaunchStr === 'false' ? ['warn', 'error'] : []);
                unityDrivenStartSession();
                break;
            }
            case 'requestToLoadScene': {
                // args are
                //   scene name - if different from model's existing scene, request will always be accepted
                //   forceReload - "true" or "false", determining whether init can override *same* scene in model
                //   forceRebuild - "true" or "false", determining whether init can use cached scene details if available
                if (this.preloadingView) {
                    const sceneName = args[0];
                    const forceReload = args[1] === 'true';
                    const forceRebuild = args[2] === 'true';
                    this.preloadingView.publishRequestToLoadScene(sceneName, forceReload, forceRebuild);
                } else console.warn(`requestToLoadScene but no preloadingView!`);
                break;
            }
            case 'defineScene': {
                // args are
                //   scene name - if different from model's existing scene, init will always be accepted
                //   earlySubscriptionTopics
                //   assetManifestString
                //   object string 1 (string  prop1:val1|prop2:val2...)
                //   object string 2
                //   etc
                const [sceneName, ...initStrings] = args;
                // console.log(`defineScene for ${sceneName}`);
                this.readySceneInUnity = sceneName;
                const view = this.preloadingView;
                if (view) {
                    view.readyToBuildSceneInUnity(sceneName);
                    view.attemptToPublishInitialization(sceneName, initStrings);
                } else console.warn(`defineScene but no preloadingView!`);
                break;
            }
            case 'readyToRunScene': {
                // the unity side has read the prefab assets that are available
                // for the specified scene, and is thus ready to make pawns for
                // the scene's actors.
                // tell the PreloadingViewRoot that we're ready to build the
                // real root.
                const sceneName = args[0];
                // console.log(`readyToRunScene for ${sceneName}`);
                this.readySceneInUnity = sceneName;
                const view = this.preloadingView;
                if (view) view.readyToBuildSceneInUnity(sceneName);
                else console.warn(`readyToRunScene but no preloadingView!`);
                break;
            }
            case 'event': {
                // used for all interaction events (keyDown, keyUp, pointerHit etc)
                if (theGameInputManager) theGameInputManager.handleEvent(args);
                break;
            }
            case 'publish': {
                // args[0] is scope
                // args[1] is eventName
                // args[2] - if supplied - is a string describing the format of the next argument:
                //      s - single string
                //      ss - string array
                //      f - single float
                //      ff - float array
                //      b - boolean
                // args[3] - the encoded arg
                const [ scope, eventName, argFormat, argString ] = args;
                // console.log({scope, eventName, argFormat, argString});
                if (argFormat === undefined) this.preloadingView?.publish(scope, eventName);
                else {
                    let eventArgs = argString.split('\x03');
                    switch (argFormat) {
                        case 's': // string
                            eventArgs = eventArgs[0];
                            break;
                        case 'f': // float
                            eventArgs = Number(eventArgs[0]);
                            break;
                        case 'ff': // float array
                            eventArgs = eventArgs.map(Number);
                            break;
                        case 'b': // boolean
                            eventArgs = eventArgs[0] === "True";
                            break;
                        case 'ss': // string array; ok as is
                        default:
                    }
                    this.preloadingView?.publish(scope, eventName, eventArgs);
                }
                break;
            }
            case 'unityPong':
                // args[0] is Date.now() when sent
                globalThis.timedLog(`PONG after ${Date.now() - Number(args[0])}ms`);
                break;
            case 'log':
                // args[0] is loggable string
                globalThis.timedLog(`[Unity] ${args[0]}`);
                break;
            case 'measure': {
                // args are [name, startDateNow, durationMS[, annotation]
                const [markName, startDateNow, durationMS, annotation] = args;
                const startPerf = performance.now() - Date.now() + Number(startDateNow);
                const index = ++this.measureIndex;
                const measureText = `U:${markName}${index} ${annotation || ""}`;
                performance.measure(measureText, { start: startPerf, end: startPerf + Number(durationMS) });
                break;
            }
            case 'simulateNetworkGlitch':
                this.simulateNetworkGlitch(Number(args[0]));
                break;
            case 'shutdown':
                // close any running session, but keep the bridge
                // arg 0 - if present - is a reference (name or buildIndex) to the scene that Unity should switch to after tearing down the session
                globalThis.timedLog('shutdown event received');
                this.postShutdownSceneInUnity = args.length ? args[0] : null;
                shutDownSession();
                break;
            default:
                if (this.commandHandler) this.commandHandler(command, args);
                else globalThis.timedLog(`unknown Unity command: ${command}`);
        }
    }

    setSceneConstructionProperties(data) {
        const { earlySubscriptionTopics, assetManifestString } = data;
        this.earlySubscriptionTopics = earlySubscriptionTopics;
        this.assetManifestString = assetManifestString;
    }

    tearDownScene() {
        this.readySceneInUnity = null;
        if (this.bridgeIsConnected) this.sendCommand('tearDownScene');
    }

    tearDownSession() {
        this.readySceneInUnity = null; // can't harm
        const sceneStr = this.postShutdownSceneInUnity || "";
        this.postShutdownSceneInUnity = null;
        if (this.bridgeIsConnected) this.sendCommand('tearDownSession', sceneStr);
    }

    setJSLogForwarding(toForward) {
        console.log("categories of JS log forwarded to Unity:", toForward);
        const isNode = globalThis.CROQUET_NODE;
        const timeStamper = logVals => `${(globalThis.CroquetViewDate || Date).now() % 100000}: ` + logVals.map(a => typeof a === 'object' ? JSON.stringify(a) : String(a)).join(' ');
        const forwarder = (logType, logVals) => this.sendCommand('logFromJS', logType, timeStamper(logVals));
        ['log', 'warn', 'error'].forEach(logType => {
            if (isNode) {
                // in Node, everything output to the console is (for now) automatically
                // echoed to Unity.  so anything _not_ in the list needs to be suppressed.
                if (toForward.includes(logType)) console[logType] = (...logVals) => console[`q_${logType}`](timeStamper(logVals)); // use native logger to write the output
                else console[logType] = () => {}; // suppress
            } else {
                // eslint-disable-next-line no-lonely-if
                if (toForward.includes(logType)) console[logType] = (...logVals) => forwarder(logType, logVals);
                else console[logType] = console[`q_${logType}`]; // use system default
            }
        });
    }

    update(_time) {
        // sent by the gameViewManager on each update()
        const now = Date.now();
        if (now - (this.lastTimeAnnouncement || 0) >= 1000) {
            this.announceSessionTime();
            this.lastTimeAnnouncement = now;
        }

        if (now - this.msgStats.lastMessageDiagnostics > 1000) {
            const { inMessageCount, inBundleCount, inBundleDelayMS, inProcessingTimeMS, _outMessageCount, _outBundleCount } = this.msgStats;
            if (inMessageCount || inBundleCount) {
                globalThis.timedLog(`from Unity: ${inMessageCount} messages with ${inBundleCount} bundles (${Math.round(inBundleDelayMS/inBundleCount)}ms avg delay) handled in ${Math.round(inProcessingTimeMS)}ms");`);
            }
            // globalThis.timedLog(`to Unity: ${outMessageCount} messages with ${outBundleCount} bundles`);
            this.resetMessageStats();
        }
    }

    announceSessionTime() {
        // the sessionOffsetEstimator provides an estimate of how far the reflector's
        // raw time is ahead of this client's performance.now().
        // from that, and the current values of performance.now and Date.now, we
        // calculate an estimate of what our Date.now would have been when the
        // reflector's raw time was zero.  that gets sent over the bridge.
        if (!sessionOffsetEstimator?.offsetEstimate) return;

        const perfNow = performance.now();
        const reflectorNow = sessionOffsetEstimator.offsetEstimate + perfNow;
        const dateNowAtReflectorZero = Date.now() - reflectorNow;

        this.sendCommand('croquetTime', String(Math.floor(dateNowAtReflectorZero)));
    }

    simulateNetworkGlitch(milliseconds) {
        console.warn(`simulating network glitch of ${milliseconds}ms`);
        const vm = globalThis.CROQUETVM; // @@ privileged information
        vm.controller.connection.reconnectDelay = milliseconds;
        vm.controller.connection.socket.close(4000, 'simulate glitch');
        timerClient.setTimeout(() => vm.controller.connection.reconnectDelay = 0, 500);
    }

showSetupStats() {
    // pawns keep stats on how long they took to set up.  if this isn't called, the stats will keep building up (but basically harmless).
    console.log(`build: ${Object.entries(buildStats).map(([k, v]) => `${k}:${v}`).join(' ')} total: ${Object.entries(setupStats).map(([k, v]) => `${k}:${v}`).join(' ')}`);
    buildStats.length = setupStats.length = 0;
}
}
export const theGameEngineBridge = new BridgeToUnity();

// InitializationManager is a model service that knows how to instantiate a set of actors from an init chunk
export class InitializationManager extends ModelService {

    init() {
        super.init('InitializationManager');
        this.activeScene = ""; // the scene we're running, or getting ready to run
        this.activeSceneState = ""; // preload, initializing, running
        this.initializingView = ""; // the view that has permission from us to provide the data for activeScene

        this.client = null; // needs to handle onPrepareForInitialization, onInitializationStart, onObjectInitialization
        this.initBufferCollector = [];
        this.lastInitString = null; // if activeSceneState is running, this is the string that was used to initialise it.  we can reload the scene instantly by reusing this.

        this.subscribe(this.sessionId, 'requestToLoadScene', this.handleRequestToLoadScene);
        this.subscribe(this.sessionId, 'requestToInitScene', this.handleRequestToInitScene);
        this.subscribe(this.sessionId, 'sceneInitChunk', this.sceneInitChunk);
        this.subscribe(this.sessionId, 'view-exit', this.handleViewExit);
    }

    setClient(model) {
        this.client = model;
    }

    handleRequestToLoadScene({ sceneName, forceReload, forceRebuild }) {
        // this comes from a view (unity or otherwise), for example when a user presses a button to advance to the next level

        // if sceneName is the same as activeScene, and the state is 'preload' or 'loading', ignore.  it's already being dealt with.
        // else if sceneName is the same as activeScene (so the state must be 'running'), then iff forceFlag is true accept the request and reset state to 'loading', otherwise ignore
        // else (new scene name) accept by setting a new activeScene and state 'preload'

        const { activeScene, activeSceneState } = this;
        if (sceneName === activeScene) {
            if (activeSceneState === 'preload' || activeSceneState === 'loading' || !forceReload) {
                console.log(`denying request to load ${sceneName}; sceneState is "${activeSceneState}"`);
                return;
            }

            this.activeSceneState = forceRebuild ? 'preload' : 'loading';
        } else {
            this.activeScene = sceneName;
            this.lastInitString = null;
            this.activeSceneState = 'preload';
            this.initializingView = null; // cut off any in-progress load for a previous scene
        }
        console.log(`approved request to load ${sceneName}; state now "${this.activeSceneState}"`);

        this.publishSceneState(); // will immediately ditch the main viewRoot
        this.client?.onPrepareForInitialization(); // clear out any non-persistent state from model

        if (this.activeSceneState === 'loading') this.loadFromString(this.lastInitString);
    }

    handleRequestToInitScene({ viewId, sceneName }) {
        // it's possible that this is an out-of-date request to init a scene that we're no longer interested in.
        // if sceneName is not the same as our activeScene, or if activeSceneState is anything other than 'preload', or there is already an initializingView, the request is denied.
        const { activeScene, initializingView } = this;
        let verdict;
        if (sceneName !== activeScene || this.activeSceneState !== 'preload' || initializingView) {
            console.log(`denying ${viewId} permission to init ${sceneName}`);
            verdict = false;
        } else {
            console.log(`granting ${viewId} permission to init ${sceneName}`);
            this.activeSceneState = 'loading';
            this.initializingView = viewId;
            this.publishSceneState();
            verdict = true;
        }
        this.publish(viewId, 'requestToInitVerdict', verdict);
    }

    sceneInitChunk({ viewId, sceneName, isFirst, isLast, buf }) {
        // check that we haven't switched to loading something else
        const { activeScene, initializingView } = this;
        if (sceneName !== activeScene || viewId !== initializingView) return;

        if (isFirst) this.initBufferCollector = [];
        this.initBufferCollector.push(buf);
        if (isLast) {
            // turn the array of chunks into a single buffer
            const bufs = this.initBufferCollector;
            const len = bufs.reduce((acc, cur) => acc + cur.length, 0);
            const all = new Uint8Array(len);
            let ind = 0;
            for (let i = 0; i < bufs.length; i++) {
                all.set(bufs[i], ind);
                ind += bufs[i].length;
            }

            const initString = new TextDecoder("utf-8").decode(all);
            console.log(`received string of length ${initString.length}`);
            this.lastInitString = initString;
            this.initBufferCollector = [];
            this.initializingView = null;
            this.loadFromString(initString);
        }
    }

    loadFromString(initString) {
        this.client?.onInitializationStart();

        const abbreviations = [];
        const [_earlySubscriptionTopics, _assetManifestString, ...entities] = initString.split('\x01');

        if (entities.length && !this.client) {
            console.warn("Attempt to initialize scene entities without an appointed AM_InitializationClient object");
            entities.length = 0; // just ignore them
        }

        entities.forEach(entityString => {
            // console.log(entityString);
            const propertyStrings = entityString.split('|');
            let cls;
            const props = {};
            propertyStrings.forEach(token => {
                let propAndValue = token; // unless an abbreviation
                if (token.startsWith('$')) propAndValue = abbreviations[token.slice(1)];
                else abbreviations.push(token);
                const [propName, value] = propAndValue.split(':');
                switch (propName) {
                    case 'ACTOR':
                        try { cls = Actor.classFromID(value) }
                        catch (e) {
                            console.warn(`Actor class not found for init string: ${entityString}`);
                            cls = false; // mark that we tried and failed
                        }
                        break;
                    case 'position':
                        props.translation = value.split(',').map(Number); // note name change
                        break;
                    case 'rotation':
                        props.rotation = q_normalize(value.split(',').map(Number));
                        break;
                    case 'scale':
                        props.scale = value.split(',').map(Number);
                        break;
                    default:
                        props[propName] = value;
                }
            });

            if (!cls) {
                if (cls !== false) console.warn(`No actor specified in init string: ${entityString}`);
                return;
            }

            this.client.onObjectInitialization(cls, props);
        });

        this.activeSceneState = 'running';
        this.publishSceneState();
    }

    handleViewExit(viewId) {
        // if the view that has left was in the middle of sending a scene
        // initialisation, reset the scene to 'preload' and look for another
        // initialiser.
        if (viewId === this.initializingView) {
            this.initializingView = null;
            this.activeSceneState = 'preload';
            this.publishSceneState(); // $$$ probably not enough to trigger a new view to load
        }
    }

    publishSceneState() {
        this.publish(this.sessionId, 'sceneStateUpdated');
    }

    getSceneConstructionProperties() {
        if (this.activeSceneState !== 'running') {
            throw Error("attempt to fetch construction properties for non-running scene");
        }

        const [earlySubscriptionTopics, assetManifestString] = this.lastInitString.split('\x01'); // wasteful to split the whole thing, but doesn't happen often
        return { earlySubscriptionTopics, assetManifestString };
    }

}
InitializationManager.register('InitializationManager');

export const AM_InitializationClient = superclass => class extends superclass {

    init(...args) {
        super.init(...args);
        this.initializationManager = this.service('InitializationManager');
        this.initializationManager.setClient(this);
    }

    onPrepareForInitialization() { }

    onInitializationStart() { }

    onObjectInitialization(_cls, _props) { }

};
RegisterMixin(AM_InitializationClient);


// GameViewManager is a new kind of service, created specifically for
// the bridge to Unity, handling the creation and management of Unity-side
// gameObjects that track the Croquet pawns.
// GameViewManager is a ViewService, and is therefore constructed afresh
// on Session.join().  if there is a network glitch, the manager will be destroyed
// on disconnection and then rebuilt when the session re-connects.
export const GameViewManager = class extends ViewService {
    constructor(name) {
        super(name || "GameViewManager");

        this.lastGameHandle = 0;
        this.maxGameHandle = 999999; // our current spatial encoding gives us scope for 2^26 handles - around 67M - but 1M (with recycling) should be plenty
        this.freedGameHandles = []; // handles that can be cleared once the associated destruction messages have definitely been sent to Unity
        this.pawnsByGameHandle = {}; // integer handle => pawn
        this.deferredMessagesByGameHandle = new Map(); // handle => [msgs], iterable in the order the handles are mentioned
        this.deferredGeometriesByGameHandle = {}; // handle => msg; order not important
        this.deferredOtherMessages = []; // events etc, always sent after gameHandle-specific messages

        this.forwardedEventTopics = {}; // topic (scope:eventName) => handler

        this.unityMessageThrottle = 40; // ms between updates sent to Unity (every two updates at 26ms, even if they get a little bunched up)
        this.lastMessageFlush = 0;
        this.assetManifests = {};

        theGameEngineBridge.setCommandHandler(this.handleUnityCommand.bind(this));

        const { earlySubscriptionTopics, assetManifestString } = theGameEngineBridge;
        if (earlySubscriptionTopics) {
            earlySubscriptionTopics.split(',').forEach(topic => this.registerTopicForForwarding(topic));
        }
        if (assetManifestString) {
            const parseArray = str => str.split(',').filter(Boolean); // remove empties
            const manifestStrings = assetManifestString.split('\x03');
            for (let i = 0; i < manifestStrings.length; i += 4) {
                const assetName = manifestStrings[i];
                const mixins = parseArray(manifestStrings[i + 1]);
                const statics = parseArray(manifestStrings[i + 2]);
                const watched = parseArray(manifestStrings[i + 3]);
                this.assetManifests[assetName] = { mixins, statics, watched };
            }
        }
        this.subscribe('__wc', 'say', this.forwardSayToUnity);
    }

    destroy() {
        globalThis.timedLog("GameViewManager destroyed");
        theGameEngineBridge.setCommandHandler(null);
    }

    nextGameHandle() {
        let handle = this.lastGameHandle;
        let tries = 1;
        const max = this.maxGameHandle;
        const pawns = this.pawnsByGameHandle;
        while (true) {
            handle++;
            if (handle > max) handle = 1; // loop back
            if (!pawns[handle]) break; // found one!
            tries++;
            if (tries === max) throw Error("Failed to find available game handle");
        }
        this.lastGameHandle = handle;
        return handle;
    }

    unityId(gameHandle) {
        // currently redundant.  previously checked for reserved handles.
        return gameHandle;
    }

    getPawn(gameHandle) {
        return this.pawnsByGameHandle[gameHandle] || null;
    }

    assetManifestForType(type) {
        return this.assetManifests[type];
    }

    handleUnityCommand(command, args) {
        // console.log('command from Unity: ', { command, args });
        let pawn;
        switch (command) {
            case 'registerForEventTopic':
                // only used for subscribe(), not listen()
                this.registerTopicForForwarding(args[0]);
                break;
            case 'unregisterEventTopic':
                // only used for subscribe(), not listen()
                this.unregisterTopicForForwarding(args[0]);
                break;
            case 'objectCreated': {
                // args[0] is gameHandle
                // args[1] is time when Unity created the pawn
                const [gameHandle, timeStr] = args;
                pawn = this.pawnsByGameHandle[gameHandle];
                if (pawn) pawn.unityViewReady(Number(timeStr));
                break;
            }
            case 'objectMoved': {
                // args[0] is gameHandle
                // remaining args are taken in pairs <property, value>
                // where property is one of "s", "r", "p" for scale, rot, pos
                // followed by a comma-separated list of values for the property
                // i.e., 3 or 4 floats
                pawn = this.pawnsByGameHandle[args[0]];
                if (pawn && pawn.geometryUpdateFromUnity) {
                    try {
                        const update = {};
                        let pos = 1;
                        while (pos < args.length) {
                            const prop = args[pos++];
                            let geomProp;
                            switch (prop) {
                                case 's': geomProp = 'scale'; break;
                                case 'r': geomProp = 'rotation'; break;
                                case 'p': geomProp = 'translation'; break;
                                default:
                            }
                            if (geomProp) {
                                update[geomProp] = args[pos++].split(',').map(Number);
                            }
                        }
                        if (Object.keys(update).length) pawn.geometryUpdateFromUnity(update);

                    } catch (e) {
                        console.error(e);
                    }
                }
                break;
            }
            default:
                globalThis.timedLog(`unknown Unity command: ${command}`);
        }
    }

    registerTopicForForwarding(topic) {
        if (!this.forwardedEventTopics[topic]) {
            // console.log(`registering for "${topic}" events`);
            const [scope, eventName] = topic.split(':');
            const handler = eventArgs => {
                this.forwardEventToUnity(scope, eventName, eventArgs);
            };
            this.subscribe(scope, eventName, handler);
            this.forwardedEventTopics[topic] = handler;
        }
    }

    unregisterTopicForForwarding(topic) {
        const handler = this.forwardedEventTopics[topic];
        if (handler) {
            const [scope, eventName] = topic.split(':');
            // console.log(`unregistering from "${scope}:${eventName}" events`);
            this.unsubscribe(scope, eventName, handler);
            delete this.forwardedEventTopics[topic];
        }
    }

    forwardEventToUnity(scope, eventName, eventArgs) {
        // console.log("forwarding event", { scope, eventName, eventArgs });
        if (eventArgs === undefined) this.sendDeferred(null, 'croquetPub', scope, eventName);
        else {
            const stringArg = theGameEngineBridge.encodeValueAsString(eventArgs);
            this.sendDeferred(null, 'croquetPub', scope, eventName, stringArg);
        }
    }

    forwardSayToUnity(data) {
        const [ actorId, eventName, args ] = data;
        this.forwardEventToUnity(actorId, eventName, args);
    }

    makeGameObject(pawn, unityViewSpec) {
        const gameHandle = unityViewSpec.cH;
        if (pawn) this.registerPawn(pawn, gameHandle);
        this.sendDeferred(gameHandle, 'makeObject', JSON.stringify(unityViewSpec));
        // any time a new object is created, we ensure that there is minimal delay in
        // servicing the deferred messages and updating objects' geometries.
        this.expediteMessageFlush();
        return gameHandle;
    }

    destroyObject(gameHandle) {
        this.unregisterPawn(gameHandle); // will also remove any pending messages for the handle
        this.sendDeferred(gameHandle, 'destroyObject', this.unityId(gameHandle));
    }

    registerPawn(pawn, gameHandle) {
        this.pawnsByGameHandle[gameHandle] = pawn;
    }

    unregisterPawn(gameHandle) {
        this.freedGameHandles.push(gameHandle);
        this.deferredMessagesByGameHandle.delete(gameHandle); // if any
    }

    recycleFreedHandles() {
        this.freedGameHandles.forEach(handle => delete this.pawnsByGameHandle[handle]);
        this.freedGameHandles.length = 0;
    }

    setParent(childHandle, parentHandle) {
        this.sendDeferred(childHandle, 'setParent', this.unityId(childHandle), this.unityId(parentHandle));
    }

    unparent(childHandle) {
        this.sendDeferred(childHandle, 'unparent', this.unityId(childHandle));
    }

    updatePawnGeometry(gameHandle, updateSpec) {
        // opportunistic updates to object geometries.
        // we keep a record of these updates independently from general deferred messages,
        // gathering and sending them as part of the next geometry flush.
        const previousSpec = this.deferredGeometriesByGameHandle[gameHandle];
        if (!previousSpec) this.deferredGeometriesByGameHandle[gameHandle] = updateSpec; // end of story
        else {
            // each of prev and latest can have updates to scale, translation,
            // rotation (or their snap variants).  overwrite previousSpec with any
            // new updates.
            const incompatibles = {
                scale: 'scaleSnap',
                scaleSnap: 'scale',
                rotation: 'rotationSnap',
                rotationSnap: 'rotation',
                translation: 'translationSnap',
                translationSnap: 'translation'
            };
            for (const [prop, value] of Object.entries(updateSpec)) {
                previousSpec[prop] = value;
                delete previousSpec[incompatibles[prop]]; // in case one was there
            }
        }
    }

    ensureDeferredMessages(gameHandle) {
        let messages = this.deferredMessagesByGameHandle.get(gameHandle);
        if (!messages) {
            messages = [];
            this.deferredMessagesByGameHandle.set(gameHandle, messages);
        }
        return messages;
    }

    sendDeferred(gameHandle, command, ...args) {
        const deferredForSame = gameHandle == null
            ? this.deferredOtherMessages
            : this.ensureDeferredMessages(gameHandle);
        deferredForSame.push({ command, args });
    }

    sendDeferredWithOverride(gameHandle, key, command, ...args) {
        // if an existing entry in the deferred messages for this pawn has the
        // specified key, replace its command and arguments with the new ones.
        const deferredForSame = this.ensureDeferredMessages(gameHandle);
        const previous = deferredForSame.find(spec => spec.overrideKey === key);
        if (previous) {
            // console.log(`overriding ${command} on ${gameHandle}`);
            previous.command = command;
            previous.args = args;
        } else {
            deferredForSame.push({ command, args, overrideKey: key });
        }
    }

    sendDeferredFromPawn(gameHandle, command, ...args) {
        // for every command from a pawn, prepend its croquet handle as the first arg
        this.sendDeferred(gameHandle, command, this.unityId(gameHandle), ...args);
    }

    update(time, delta) {
        super.update(time, delta);

        // tick the bridge, which will periodically announce Croquet time to the
        // C# side.
        theGameEngineBridge.update(time);

        const now = Date.now();
        if (now - (this.lastMessageFlush || 0) >= this.unityMessageThrottle) {
            this.lastMessageFlush = now;
            this.flushDeferredMessages();
            this.flushGeometries();
        }
    }

    expediteMessageFlush() {
        // guarantee that messages will flush on next update
        this.lastMessageFlush = null;
    }

    flushDeferredMessages() {
const pNow = performance.now();

        // give each pawn a chance to update watched properties
        Object.values(this.pawnsByGameHandle).forEach(pawn => pawn.forwardPropertiesIfNeeded?.());

        // now that opportunistic updatePawnGeometry is handled separately, there are
        // currently no commands that need special treatment.  but we may as
        // well keep the option available.
        const transformers = {
            default: args => {
                // currently just used to convert arrays to comma-separated strings
                const strings = [];
                args.forEach(arg => {
                    strings.push(Array.isArray(arg)
                        ? arg.map(String).join(',')
                        : arg);
                });
                return strings;
            }
        };

        const messages = [];
        const addMessage = spec => {
            const { command } = spec;
            let { args } = spec;
            if (args.length) {
                const transformer = transformers[command] || transformers.default;
                args = transformer(args);
            }
            messages.push([command, ...args].join('\x01'));
        };
        this.deferredMessagesByGameHandle.forEach(msgSpecs => {
            msgSpecs.forEach(addMessage);
        });
        this.deferredMessagesByGameHandle.clear();
        this.deferredOtherMessages.forEach(addMessage);
        this.deferredOtherMessages.length = 0;

        const numMessages = messages.length; // before sendBundle messes with it
        if (numMessages > 1) {
            const batchLen = theGameEngineBridge.sendBundleToUnity(messages);
this.msgBatch = (this.msgBatch || 0) + 1;
performance.measure(`to U (batch ${this.msgBatch}): ${numMessages} msgs in ${batchLen} chars`, { start: pNow, end: performance.now() });
        } else if (numMessages) {
            theGameEngineBridge.sendToUnity(messages[0]);
        }

        if (numMessages) this.recycleFreedHandles();
    }

    flushGeometries() {
        const toBeMerged = [];

        // it's possible that some pawns will have an explicit deferred update
        // in addition to some changes since then that they now want to propagate.
        // in that situation, we send the explicit update first.
        for (const [gameHandle, update] of Object.entries(this.deferredGeometriesByGameHandle)) {
            toBeMerged.push([this.unityId(gameHandle), update]);
        }
        this.deferredGeometriesByGameHandle = {};

        for (const [gameHandle, pawn] of Object.entries(this.pawnsByGameHandle)) {
            const update = pawn.geometryUpdateIfNeeded?.(); // pawns aren't guaranteed to be spatial
            if (update) toBeMerged.push([this.unityId(gameHandle), update]);
        }

        if (!toBeMerged.length) return;

        const array = new Float32Array(toBeMerged.length * 11); // maximum length needed
        const intArray = new Uint32Array(array.buffer); // integer view into same data

        let pos = 0;
        const writeVector = vec => vec.forEach(val => array[pos++] = val);
        toBeMerged.forEach(([gameHandle, spec]) => {
            const { scale, scaleSnap, translation, translationSnap, rotation, rotationSnap } = spec;
            // first number encodes object gameHandle and (in bits 0 to 5) whether there is an
            // update to each of scale, rotation, translation, and for each one whether
            // it should be snapped.
            const idPos = pos++; // once we know the value
            let encodedId = gameHandle << 6;
            if (scale || scaleSnap) {
                writeVector(scale || scaleSnap);
                encodedId += 32;
                if (scaleSnap) encodedId += 16;
            }
            if (rotation || rotationSnap) {
                writeVector(rotation || rotationSnap);
                encodedId += 8;
                if (rotationSnap) encodedId += 4;
            }
            if (translation || translationSnap) {
                writeVector(translation || translationSnap);
                encodedId += 2;
                if (translationSnap) encodedId += 1;
            }
            intArray[idPos] = encodedId;
        });

        // send as a single binary-bodied message
        const buffer = array.buffer;
        const filledBytes = pos * 4;
        const command = 'updateSpatial';
        const cmdPrefix = `${String(Date.now())}\x02${command}\x05`;
        const message = new Uint8Array(cmdPrefix.length + filledBytes);
        for (let i = 0; i < cmdPrefix.length; i++) message[i] = cmdPrefix.charCodeAt(i);
        message.set(new Uint8Array(buffer).subarray(0, filledBytes), cmdPrefix.length);
        theGameEngineBridge.sendToUnity(message.buffer);
    }
};

const gamePawnMixins = {};

const buildStats = [], setupStats = [];

export const PM_GameRendered = superclass => class extends superclass {
    // getters for gameViewManager and gameHandle allow them to be accessed even from super constructor
    get gameViewManager() { return this._gameViewManager || (this._gameViewManager = GetViewService('GameViewManager' ))}
    get gameHandle() { return this._gameHandle || (this._gameHandle = this.gameViewManager.nextGameHandle()) } // integer
    get componentNames() { return this._componentNames || (this._componentNames = new Set()) }
    get extraStatics() { return this._extraStatics || (this._extraStatics = new Set()) }
    get extraWatched() { return this._extraWatched || (this._extraWatched = new Set()) }

    constructor(actor) {
        super(actor);

        this._throttleFromUnity = 75; // ms between forwarding to the session position updates sent from Unity (e.g., for an avatar).  we expect Unity updates at 25Hz (40ms); for now we aim to forward half of those.
        this._messagesAwaitingCreation = []; // removed once creation is requested
        this._geometryAwaitingCreation = null; // can be written by PM_Spatial and its subclasses
        this._isViewReady = false;
    }

    initialize(actor) {
        // construction is complete, through all mixin layers

        const manifest = this.gameViewManager.assetManifestForType(actor.gamePawnType);
        const statics = new Set(manifest.statics);
        const watched = new Set(manifest.watched);
        this.extraStatics.forEach(prop => statics.add(prop));
        this.extraWatched.forEach(prop => watched.add(prop));
        // gather any statics into an argument on the initialisation message:
        // an array with pairs   propName1, propVal1, propName2,...
        const propertyStrings = [];
        if (watched.size) this._watchedPropertyValues = {}; // propName => [val, stringyVal]
        const merged = new Set([...statics, ...watched]);
        merged.forEach(propName => {
            let actorPropName = propName;
            if (propName === "position") actorPropName = "translation";
            let value = actor[actorPropName];
            if (value === undefined) value = this[propName]; // allow pawn to fill in with custom properties
            if (value === undefined) {
                console.log(`property ${propName} not found on ${actor.constructor.name} (possible prefab/class mismatch)`);
                return;
            }
            const stringyValue = theGameEngineBridge.encodeValueAsString(value);
            propertyStrings.push(propName, stringyValue);
            if (watched.has(propName)) {
                this._watchedPropertyValues[propName] = [value, stringyValue];
            }
        });
        const initArgs = {
            type: actor.gamePawnType,
            propertyValues: propertyStrings,
            watchers: [...watched]
        };
        this.setGameObject(initArgs); // args may be adjusted by mixins
    }

    setGameObject(viewSpec) {
        // analogue of setRenderObject in mixins for THREE.js rendering

        // because pawn creation is asynchronous, it's possible that the
        // actor has already been destroyed by the time we get here.  in
        // that case, don't bother creating the unity gameobject at all.
        if (this.actor.doomed) return;

        if (!viewSpec.confirmCreation) this._isViewReady = true; // not going to wait

        let allComponents = [...this.componentNames].join(',');
        if (viewSpec.extraComponents) allComponents += `,${viewSpec.extraComponents}`;

        this.unityViewP = new Promise(resolve => this.setReady = resolve);
        const unityViewSpec = {
            cH: String(this.gameHandle),
            cN: this.actor.id,
            cC: !!viewSpec.confirmCreation,
            wTP: !!viewSpec.waitToPresent,
            type: viewSpec.type,
            cs: allComponents,
            ps: viewSpec.propertyValues,
            ws: viewSpec.watchers
        };
// every pawn tracks the delay between its creation on the Croquet
// side and receipt of a message from Unity confirming the corresponding
// gameObject's construction.
this.setupTime = Date.now();
// additionally, every hundredth pawn logs this round trip
if (this.gameHandle % 100 === 0) {
    globalThis.timedLog(`pawn ${this.gameHandle} created`);
}

        this.gameViewManager.makeGameObject(this, unityViewSpec);
        this._messagesAwaitingCreation.forEach(cmdAndArgs => {
            this.gameViewManager.sendDeferredFromPawn(...[this.gameHandle, ...cmdAndArgs]);
        });
        delete this._messagesAwaitingCreation;

        // PM_GameSpatial introduces _geometryAwaitingCreation
        if (this._geometryAwaitingCreation) {
            this.gameViewManager.updatePawnGeometry(this.gameHandle, this._geometryAwaitingCreation);
            this._geometryAwaitingCreation = null;
        }
    }

    forwardPropertiesIfNeeded() {
        if (!this._watchedPropertyValues || this.doomed) return;

        const { actor } = this;
        for (const [propName, valueAndString] of Object.entries(this._watchedPropertyValues)) {
            const [value, stringyValue] = valueAndString;

            let actorPropName = propName;
            if (propName === "position") actorPropName = "translation";

            let newValue = actor[actorPropName];
            if (newValue === undefined) newValue = this[propName];
            let changed, newStringyValue;
            if (Array.isArray(value)) {
                // @@ would be nice if we can find a more efficient approach
                newStringyValue = theGameEngineBridge.encodeValueAsString(newValue);
                changed = newStringyValue !== stringyValue;
            } else {
                changed = newValue !== value;
                if (changed) newStringyValue = theGameEngineBridge.encodeValueAsString(newValue);
            }

            if (changed) {
                valueAndString[0] = newValue;
                valueAndString[1] = newStringyValue;
                const setEventName = propName + 'Set';
                this.gameViewManager.sendDeferred(this.gameHandle, 'croquetPub', actor.id, setEventName, newStringyValue);
            }
        }
    }

    unityViewReady(estimatedReadyTime) {
        // unity side has told us that the object is ready for use
        // console.log(`unityViewReady for ${this.gameHandle}`);
        this._isViewReady = true;
        this.setReady();
if (this.gameHandle % 100 === 0) {
    globalThis.timedLog(`pawn ${this.gameHandle} ready`);
}
const buildDelay = Date.now() - estimatedReadyTime;
const buildBucket = Math.round(buildDelay / 20) * 20;
buildStats[buildBucket] = (buildStats[buildBucket] || 0) + 1;
const totalDelay = Date.now() - this.setupTime;
const bucket = Math.round(totalDelay / 20) * 20;
setupStats[bucket] = (setupStats[bucket] || 0) + 1;
    }

    addChild(pawn) {
        super.addChild(pawn);
        this.gameViewManager.setParent(pawn.gameHandle, this.gameHandle);
    }

    removeChild(pawn) {
        super.removeChild(pawn);
        this.gameViewManager.unparent(pawn.gameHandle);
    }

    sendToUnity(command, ...args) {
        if (this._messagesAwaitingCreation) {
            this._messagesAwaitingCreation.push([command, ...args]);
        } else {
            this.gameViewManager.sendDeferredFromPawn(this.gameHandle, command, ...args);
        }
    }

    destroy() {
        // console.log(`pawn ${this.gameHandle} destroyed`);
        this.gameViewManager.destroyObject(this.gameHandle);
        super.destroy();
    }

    makeInteractable(layers = "") {
        this.sendToUnity('makeInteractable', layers);
    }
};
gamePawnMixins.Base = PM_GameRendered;

export const PM_GameMaterial = superclass => class extends superclass {
    constructor(actor) {
        super(actor);
        this.componentNames.add('CroquetMaterialComponent');
    }
};
gamePawnMixins.Material = PM_GameMaterial;

export const PM_GameSpatial = superclass => class extends superclass {

    constructor(actor) {
        super(actor);
        this.componentNames.add('CroquetSpatialComponent');
        this.resetGeometrySnapState();

        if (this.spatialOptions) this.extraStatics.add('spatialOptions'); // not an actor property, but will be fed from here
    }

    get scale() { return this.actor.scale }
    get translation() { return this.actor.translation }
    get rotation() { return this.actor.rotation }
    get local() { return this.actor.local }
    get global() { return this.actor.global }
    get lookGlobal() { return this.global } // Allows objects to have an offset camera position -- obsolete?
    get spatialOptions() { return this.actor._spatialOptions }

    get forward() { return this.actor.forward }
    get up() { return this.actor.up }

    geometryUpdateIfNeeded() {
        // for an avatar, filter out all updates other than the very first time,
        // or if some property has been snapped
        const avatarFiltering = this.driving && this.lastSentTranslation && !this._scaleSnapped && !this._rotationSnapped && !this._translationSnapped;
        if (avatarFiltering || this.actor.rigidBodyType === 'static' || !this._isViewReady || this.doomed) return null;

        const updates = {};
        const { scale, rotation, translation } = this; // NB: the actor's direct property values
        // use smallest scale value as a guide to the scale magnitude, triggering on
        // changes > 1%
        const scaleMag = Math.min(...scale.map(Math.abs));
        if (!this.lastSentScale || !v3_equals(this.lastSentScale, scale, scaleMag * 0.01)) {
            const scaleCopy = scale.slice();
            const doSnap = this._scaleSnapped || !this.lastSentScale;
            this.lastSentScale = scaleCopy;
            updates[doSnap ? 'scaleSnap' : 'scale'] = scaleCopy;
        }
        if (!this.lastSentRotation || !q_equals(this.lastSentRotation, rotation, 0.0001)) {
            const rotationCopy = rotation.slice();
            const doSnap = this._rotationSnapped || !this.lastSentRotation;
            this.lastSentRotation = rotationCopy;
            updates[doSnap ? 'rotationSnap' : 'rotation'] = rotationCopy;
        }
        if (!this.lastSentTranslation || !v3_equals(this.lastSentTranslation, translation, 0.01)) {
            const translationCopy = translation.slice();
            const doSnap = this._translationSnapped || !this.lastSentTranslation;
            this.lastSentTranslation = translationCopy;
            updates[doSnap ? 'translationSnap' : 'translation'] = translationCopy;
        }

        this.resetGeometrySnapState();

        return Object.keys(updates).length ? updates : null;
    }

    resetGeometrySnapState() {
        // for Spatial, act as if every update is a snap.  PM_GameSmoothed assumes the opposite.
        this._scaleSnapped = this._rotationSnapped = this._translationSnapped = true;
    }

    updateGeometry(updateSpec) {
        // opportunistic geometry update.
        // if the game pawn hasn't been created yet, store this update to be
        // delivered once the creation has been requested.
        if (this._messagesAwaitingCreation) {
            // not ready yet.  store it (overwriting any previous value)
            this._geometryAwaitingCreation = updateSpec;
        } else {
            this.gameViewManager.updatePawnGeometry(this.gameHandle, updateSpec);
        }
    }

    geometryUpdateFromUnity(update) {
        this.set(update, this._throttleFromUnity);
    }

};
gamePawnMixins.Spatial = PM_GameSpatial;

export const PM_GameSmoothed = superclass => class extends PM_GameSpatial(superclass) {

    constructor(actor) {
        super(actor);
        this.throttle = 100; // ms between updates sent from Croquet view (though we're not expecting there to be any)

        this.componentNames.add('PresentOncePositionUpdated');

        this.listenOnce("scaleSnap", this.onScaleSnap);
        this.listenOnce("rotationSnap", this.onRotationSnap);
        this.listenOnce("translationSnap", this.onTranslationSnap);
    }

    setGameObject(viewSpec) {
        viewSpec.waitToPresent = true;
        super.setGameObject(viewSpec);
    }

    scaleTo(v) {
        this.say("setScale", v, this.throttle);
    }

    rotateTo(q) {
        this.say("setRotation", q, this.throttle);
    }

    translateTo(v) {
        this.say("setTranslation", v, this.throttle);
    }

    positionTo(v, q) {
        this.say("setPosition", [v, q], this.throttle);
    }

    onScaleSnap() {
        this._scaleSnapped = true;
    }

    onRotationSnap() {
        this._rotationSnapped = true;
    }

    onTranslationSnap() {
        this._translationSnapped = true;
    }

    get local() {
        console.warn("attempt to get .local");
        return null;
    }

    get global() {
        console.warn("attempt to get .global");
        return null;
    }

    resetGeometrySnapState() {
        this._scaleSnapped = this._rotationSnapped = this._translationSnapped = false;
    }
};
gamePawnMixins.Smoothed = PM_GameSmoothed;

export const PM_GameBallistic2D = superclass => class extends PM_GameSmoothed(superclass) {

    constructor(actor) {
        super(actor);
        this.extraStatics.add('position').add('rotation').add('scale');
        this.extraWatched.add('ballisticVelocity');
    }

};
gamePawnMixins.Ballistic2D = PM_GameBallistic2D;

export const PM_GameInteractable = superclass => class extends superclass {

    constructor(actor) {
        super(actor);
        this.componentNames.add('CroquetInteractableComponent');
    }

};
gamePawnMixins.Interactable = PM_GameInteractable;

export const PM_GameAvatar = superclass => class extends superclass {

    constructor(actor) {
        super(actor);
        this.componentNames.add('CroquetAvatarComponent');
        this.extraWatched.add('driver');
        this.onDriverSet();
        this.listenOnce("driverSet", this.onDriverSet);
    }

    get isMyAvatar() {
        return this.actor.driver === this.viewId;
    }

    onDriverSet() {
        if (this.isMyAvatar) {
            this.driving = true;
            this.drive();
        } else {
            this.driving = false;
            this.park();
        }
    }

    // park and drive are available for override if needed
    park() { }
    drive() { }

};
gamePawnMixins.Avatar = PM_GameAvatar;

// GamePawnManager is a specialisation of the standard
// Worldcore PawnManager, with pawn-creation logic that removes the need for
// actor-side declaration of pawn classes.
class GamePawnManager extends PawnManager {
    newPawn(actor) {
        return actor.gamePawnType !== undefined ? this.newGamePawn(actor) : super.newPawn(actor);
    }

    newGamePawn(actor) {
        // for the unity world, pawn classes are built on the fly using
        // the mixins defined above, based on the pawnMixins list of mixin
        // names provided by the actor.
        // if there are any elements in the list, we prepend the obligatory
        // Base mixin (PM_GameRendered) for a game-rendered pawn.
        // an actor that doesn't want any game pawn still gets a vanilla Pawn,
        // e.g. so it can support having children.

        if (!this._gameViewManager) this._gameViewManager = GetViewService("GameViewManager");

        let p;

        const { gamePawnType } = actor;
        if (gamePawnType === "") {
            p = new Pawn(actor);
        } else {
            const manifest = this._gameViewManager.assetManifestForType(gamePawnType);
            if (!manifest) {
                console.warn(`no manifest for gamePawnType "${gamePawnType}"`);
                return null;
            }

            const mixinNames = manifest.mixins; // can be empty
            const mixins = ['Base'].concat(mixinNames).map(n => gamePawnMixins[n]);
            const PawnClass = mix(Pawn).with(...mixins);
            p = new PawnClass(actor);
            p.initialize(actor); // new: 2-phase init, so all constructors run to completion first
        }

        this.pawns.set(actor.id, p);
        return p;
    }

    spawnPawn(actor) {
        const p = this.newPawn(actor);
        if (p) p.link();
    }
}

// GameViewRoot is the real root view.  The developer can use it as-is or can
// subclass it and pass the subclass on the StartSession call.  When the session
// is started, the ViewRoot passed into the session is instead the PreloadingViewRoot
// defined below.  The instance of PreloadingViewRoot decides when to load the real
// root, once the Unity side is ready to work with it.
export class GameViewRoot extends ViewRoot {

    static viewServices() {
        // $$$ for now, hard-code these three.  figure out later how to
        // customise (presumably based on presence of System components
        // on the Unity Croquet bridge)
        // note that our PawnManager needs to come last, because it'll
        // immediately get to work on building pawns that might call
        // on the other managers during their creation.
        return [GameViewManager, GameInputManager, GamePawnManager];
    }

    constructor(model) {
        super(model);

        if (sessionOffsetEstimator) sessionOffsetEstimator.initReflectorOffsets();

        const sceneName = this.wellKnownModel('InitializationManager').activeScene;
        theGameEngineBridge.sendCommand('sceneRunning', sceneName);

        this.lastViewCount = null;
        this.announceViewCount();

        globalThis.timedLog("GameViewRoot built");
    }

    destroy() {
        globalThis.timedLog("GameViewRoot destroyed");
        super.destroy();
    }

    announceViewCount() {
        const { viewCount } = this.model;
        if (viewCount !== this.lastViewCount) this.publish("croquet", "viewCount", viewCount);
        this.lastViewCount = viewCount;
    }

    update(time, delta) {
        super.update(time, delta);

        this.announceViewCount();
    }
}

// as described above, PreloadingViewRoot provides a layer between the model and the actual GameViewRoot (or subclass thereof).
class PreloadingViewRoot extends View {
    static viewServices() { return [] }

    constructor(model) {
        console.log("building PreloadingViewRoot");
        super(model);
        this.model = model;
        this.im = this.wellKnownModel('InitializationManager'); // can't use GetModelService, because this isn't a WorldCore ViewRoot

        this.subscribe(this.sessionId, { event: 'sceneStateUpdated', handling: 'immediate'}, this.handleSceneState);
        this.subscribe(this.viewId, 'requestToInitVerdict', this.handleRequestToInitVerdict);

        // we treat the construction of this view as a signal that the session
        // is ready to talk across the bridge.  but without a real viewRoot,
        // the event mechanism is not yet available.
        theGameEngineBridge.sendCommand('sessionRunning', this.viewId);

        // examine the InitializationManager to see what state the session is in
        this.handleSceneState();
    }

    readyToBuildSceneInUnity(sceneName) {
        // the bridge is telling us that the unity side is ready to build
        // the scene (which is the case as soon as it has loaded the scene
        // and fetched the scene-relevant assets).
        const { activeScene } = this.im;
        if (sceneName !== activeScene) {
            console.log(`bridge is ready for scene ${sceneName}, but we're running ${activeScene}`);
            return;
        }

        this.buildRealViewRootIfReady();
    }

    handleSceneState() {
        // NB: this is 'immediate', so synchronous with the publishing of the event
        const { activeScene, activeSceneState } = this.im;

        const announceStateToUnity = () => theGameEngineBridge.sendCommand('sceneStateUpdated', activeScene, activeSceneState);

        if (activeSceneState === 'running') {
            // fetch from the init manager the manifests and early subs, and pass
            // those into the bridge for use in starting up the real viewRoot...
            // though that has to wait until the unity side is ready for (i.e., has
            // the assets for) the scene in question.
            announceStateToUnity();
            const props = this.im.getSceneConstructionProperties();
            theGameEngineBridge.setSceneConstructionProperties(props);
            this.buildRealViewRootIfReady();
        } else {
            this.destroyRealViewRoot(); // if any
            announceStateToUnity();
        }
    }

    handleRequestToInitVerdict(verdict) {
        // the InitializationManager has responded to a requestToInit request from this view
        if (this.onRequestToInitVerdict) {
            this.onRequestToInitVerdict(verdict);
            this.onRequestToInitVerdict = null;
        } else {
            console.warn("unexpected response to requestToInit");
        }
    }

    buildRealViewRootIfReady() {
        if (this.realViewRoot) return; // already running

        const { activeScene, activeSceneState } = this.im;
        if (activeSceneState === 'running' && theGameEngineBridge.readySceneInUnity === activeScene) {
            globalThis.timedLog(`building real ViewRoot for scene ${activeScene}`);
            this.realViewRoot = new ViewRootClass(this.model);
        }
    }

    destroyRealViewRoot() {
        if (this.realViewRoot) {
            globalThis.timedLog("destroying real ViewRoot");
            theGameEngineBridge.tearDownScene();
            this.realViewRoot.destroy();
            this.realViewRoot = null;
        }
    }

    publishRequestToLoadScene(sceneName, forceReload, forceRebuild) {
        this.publish(this.sessionId, 'requestToLoadScene', {sceneName, forceReload, forceRebuild});
    }

    async attemptToPublishInitialization(sceneName, initStrings) {
        // don't interrupt if we're already sending, or awaiting permission
        if (this.publishingInitP) await this.publishingInitP;

        this.publishingInitP = new Promise(resolve => {
            this.onRequestToInitVerdict = verdict => {
                const finalize = () => {
                    this.publishingInitP = null;
                    resolve();
                };

                if (verdict) {
                    // granted
                    this.publishInitializationInChunks(sceneName, initStrings)
                        .then(finalize);
                } else {
                    // denied
                    finalize();
                }

                };
            this.publish(this.sessionId, 'requestToInitScene', { viewId: this.viewId, sceneName });
        });
    }

    async publishInitializationInChunks(sceneName, initStrings) {
        const { viewId } = this;
        console.log("publishing init", { sceneName, viewId });

        // lifted and slightly adapted from code.js in microverse
        const sendString = initStrings.join('\x01');
        const array = new TextEncoder().encode(sendString);
        const CHUNK_SIZE = 2500;
        // we've asked Croquet to let us send up to 50 messages in 1 second.
        // if we'll be sending more than 45 from here, introduce a throttle
        // so the controller doesn't complain.
        const useThrottle = array.length > CHUNK_SIZE * 45;
        let ind = 0;
        let isFirst = true;
        let isLast;
        while (ind < array.length) {
            if (this.im.activeScene !== sceneName) {
                console.log(`abandoning publish of ${sceneName}; ${this.im.activeScene} is now active`);
                return; // bail out
            }
            isLast = ind + CHUNK_SIZE >= array.length;
            const buf = array.slice(ind, ind + CHUNK_SIZE);
            this.publish(this.sessionId, 'sceneInitChunk', { viewId, sceneName, isFirst, isLast, buf });
            ind += CHUNK_SIZE;
            isFirst = false;

            if (useThrottle) await new Promise(resolve => setTimeout(resolve, 20)); // eslint-disable-line no-await-in-loop
        }
    }

    update(time, delta) {
        super.update(time, delta);

        if (this.realViewRoot) this.realViewRoot.update(time, delta);
    }

    detach() {
        // this should only happen on a glitch in the Croquet reflector connection,
        // or a deliberate session.leave().
        console.log("detaching PreloadingViewRoot");

        this.destroyRealViewRoot(); // will send tearDownScene; also important here not to have a defunct view hanging around, in case session is restored
        theGameEngineBridge.tearDownSession();

        super.detach();
    }
}

// GameInputManager is a ViewService, and therefore created and destroyed along with
// the ViewRoot.
export class GameInputManager extends ViewService {
    get gameViewManager() { return this._gameViewManager || (this._gameViewManager = GetViewService('GameViewManager')) }

    constructor(name) {
        super(name || "GameInputManager");

        this.customEventHandlers = {};

        theGameInputManager = this;
    }

    destroy() {
        super.destroy();
        theGameInputManager = null;
    }

    addEventHandlers(handlers) {
        Object.assign(this.customEventHandlers, handlers);
    }

    handleEvent(args) {
        const event = args[0];

        const custom = this.customEventHandlers[event];
        if (custom) {
            custom(args);
            return;
        }

        const { viewId } = this;
        switch (event) {
            case 'keyDown': {
                const keyCode = args[1];
                this.publish('input', `${keyCode.toLowerCase()}Down`, { viewId });
                this.publish('input', 'keyDown', { viewId, key: keyCode });
                break;
            }
            case 'keyUp': {
                const keyCode = args[1];
                this.publish('input', `${keyCode.toLowerCase()}Up`, { viewId });
                this.publish('input', 'keyUp', { viewId, key: keyCode });
                break;
            }
            case 'pointerDown': {
                const button = Number(args[1]);
                this.publish('input', 'pointerDown', { viewId, button });
                break;
            }
            case 'pointerUp': {
                const button = Number(args[1]);
                this.publish('input', 'pointerUp', { viewId, button });
                break;
            }
            case 'pointerHit': {
                // each argument starting at 1 is a comma-separated list defining
                // a hit on a single Croquet-registered game object.  its fields are:
                //   gameHandle
                //   hitPoint x
                //   hitPoint y
                //   hitPoint z
                //   [layer 1]
                //   [layer 2]
                //   etc
                const hitList = [];
                for (let i = 1; i < args.length; i++) {
                    const parsedArg = args[i].split(',');
                    const [gameHandle, x, y, z, ...layers] = parsedArg;
                    const pawn = this.gameViewManager.getPawn(gameHandle);
                    if (pawn) {
                        const { actor } = pawn;
                        const xyz = [x, y, z].map(Number);
                        hitList.push({ actor, xyz, layers });
                    }
                }
                if (hitList.length) this.publish('input', 'pointerHit', { viewId, hits: hitList });
                break;
            }
            default:
        }
    }
}

// a linked list for holding timeout records
class TimeList {
    constructor() {
        this.firstNode = null;
    }

    insert(delay, id) {
        const now = performance.now();
        const newNode = { triggerTime: now + delay, id };
        if (!this.firstNode) {
            this.firstNode = newNode;
            // this.walkList();
            return;
        }

        let n = this.firstNode;
        let prev = null;
        while (n && n.triggerTime <= newNode.triggerTime) {
            prev = n;
            n = n.next;
        }
        // either n is a node with a time > ours, or
        // we've reached the end of the list
        if (prev) prev.next = newNode;
        else this.firstNode = newNode;

        newNode.next = n; // maybe empty

        // this.walkList();
    }

    walkList() {
        // for debug
        const times = [];
        let n = this.firstNode;
        while (n) {
            times.push(Math.round(n.triggerTime));
            n = n.next;
        }
        console.log("times", times.join(","));
    }

    processUpTo(timeLimit, processor) {
        if (!this.firstNode) return;

        let n = this.firstNode;
        while (n && n.triggerTime <= timeLimit) {
            processor(n.id);
            n = n.next;
        }
        this.firstNode = n; // maybe empty
    }
}

class TimerClient {
    constructor() {
        this.timeouts = {};
        this.timeList = new TimeList();

        globalThis.setTimeout = (c, d) => this.setTimeout(c, d);
        globalThis.clearTimeout = id => this.clearTimeout(id);
    }

    setTimeout(callback, duration) {
        return this._setTimeout(callback, duration);

        // const target = Date.now() + duration;
        // return this._setTimeout(() => {
        //     console.log(`duration: ${duration} diff: ${Date.now() - target}`);
        //     callback();
        // }, duration);
    }
    _setTimeout(callback, duration) {
        const id = Math.random().toString();
        this.timeouts[id] = { callback };
        this.timeList.insert(duration, id);
        return id;
    }

    clearTimeout(id) {
        this._clearTimeout(id);
    }
    _clearTimeout(id) {
        delete this.timeouts[id];
    }

    serviceTimeouts() {
        if (!this.timeList.firstNode) return; // nothing to even check

        Promise.resolve().then(() => this.timeList.processUpTo(performance.now(), id => {
            const record = this.timeouts[id];
            if (record) {
                const { callback } = record;
                if (callback) callback();
                delete this.timeouts[id];
            }
        }));
    }
}

let timerClient, ticker, sessionStepper;
if (globalThis.CROQUET_NODE) {
    timerClient = globalThis;

    // until we figure out how to use them on Node.js, disable measure and mark so we
    // don't build up unprocessed measurement records.
    // note: attempting basic reassignment
    //    performance.mark = performance.measure = () => { };
    // raises an error on Node.js v18
    Object.defineProperty(performance, "mark", {
        value: () => { },
        configurable: true,
        writable: true
    });
    Object.defineProperty(performance, "measure", {
        value: () => { },
        configurable: true,
        writable: true
    });
} else {
    // install our home-grown timer, and an interim ticker (will be replaced once
    // the session starts) just to handle timeouts
    timerClient = new TimerClient();
    ticker = () => timerClient.serviceTimeouts(); // very cheap if there aren't any
}


let reflectorJourneyEstimates;
let reportedOffsetEstimate = null;
let resetTrigger = null;
let pingsProcessed;
class SessionOffsetEstimator {
    // maintain a best guess of the minimum offset between the local wall clock and the reflector's raw time as received on PING messages and their PONG responses.  this is used purely to judge when an event has been held up on one or other leg, and hence to adjust the calculation of the estimated offset between local and reflector time.
    // one problem we have to deal with is network batching of messages, meaning that they often arrive late.  so whenever a reflector event indicates that its raw time is earlier than our current guess, we assume that this is closer to the actual timing.  immediately adjust our estimate.
    // but then, in case the minimum offset is in fact gradually growing - i.e., the local wall clock is gradually gaining on the reflector's - the estimate is continually nudged forwards using a bias that adds 0.2ms per second (12ms per minute) of elapsed time since the last adjustment.  we expect that bias to be overridden every few seconds by an accurately timed event - but if the actual offset really is drifting by a few ms per minute, the bias should ensure that we capture that.
    constructor(sess) {
        this.session = sess;
        const controller = this.controller = sess.view.realm.vm.controller;

        this.offsetEstimate = null;
        this.minRoundtrip = 0;

        controller.connection.pongHook = args => this.handlePong(args);
        this.initReflectorOffsets();
        this.sendPing();
    }

    sendPing() {
        if (!this.session) return;

        if (this.session.view) {
            // only actually send pings while there is a view
            const args = { sent: Math.floor(performance.now()) };
            this.controller.connection.PING(args);
        }

        // start with a ping every 150ms, until 30 have been processed.
        // then calm down to one every 300ms.
        const delayToNext = pingsProcessed < 30 ? 150 : 300;
        timerClient.setTimeout(() => this.sendPing(), delayToNext);
    }

    handlePong(args) {
        if (!this.session) return;

        const { sent, rawTime: reflectorRaw } = args;
        const now = Math.floor(performance.now());
        this.estimateReflectorOffset(sent, reflectorRaw, now);
        const { view } = this.session;
        if (view && view.sessionOffsetUpdated) view.sessionOffsetUpdated();
    }

    initReflectorOffsets() {
        reflectorJourneyEstimates = { outbound: { estimate: null, lastEstimateTime: 0 }, inbound: { estimate: null, lastEstimateTime: 0 } };
        this.offsetEstimate = null;
        pingsProcessed = 0;
    }

    creepAndCorrectEstimate(direction, offset) {
        const record = reflectorJourneyEstimates[direction];
        const now = performance.now();
        const { estimate, lastEstimateTime } = record;
        const sinceLastEstimate = now - lastEstimateTime;
        let replace = estimate === null;
        if (!replace) {
            const bias = 0.0002; // 12ms/min
            const estimateWithBias = estimate + sinceLastEstimate * bias;
            // immediately act on any lower value.
            replace = offset < estimateWithBias;
        }
        if (replace) {
            // if (!record.estimate || (Math.abs(estimate - offset) > 0 && sinceLastEstimate > 5000)) console.log(`${direction} from ${estimate} to ${offset} after ${Math.round(sinceLastEstimate / 1000)}s`);
            record.estimate = offset;
            record.lastEstimateTime = now;
        }
        return replace;
    }

    estimateReflectorOffset(sent, reflectorRaw, now) {
        const outbound = reflectorRaw - sent;
        const inbound = now - reflectorRaw;
        const outboundReplaced = this.creepAndCorrectEstimate('outbound', outbound);
        const inboundReplaced = this.creepAndCorrectEstimate('inbound', inbound);

        // only recalculate when we have a fresh estimate for one or the other (or both)
        if (!outboundReplaced && !inboundReplaced) return;

        const excessOutbound = outboundReplaced ? 0 : outbound - reflectorJourneyEstimates.outbound.estimate;
        const adjustedReflectorReceived = reflectorRaw - excessOutbound;

        const excessInbound = inboundReplaced ? 0 : inbound - reflectorJourneyEstimates.inbound.estimate;
        const adjustedAudienceReceived = now - excessInbound;

        // sanity check on the calculation: if the theoretical minimum round trip implied by the actual round trip and the excess values is negative, time either here or on the reflector has jumped in a way that our algorithm isn't accounting for.  in that case, clear the outbound and inbound estimates and restart rapid polling to re-establish reasonable values.
        const impliedMinRoundTrip = now - sent - excessOutbound - excessInbound;
        if (impliedMinRoundTrip < -2) { // a millisecond or two can happen due to legitimate drift
            console.log("resetting reflector offset", { roundTrip: now - sent, excessOutbound, excessInbound, impliedMinRoundTrip });
            resetTrigger = { roundTrip: now - sent, excessOutbound, excessInbound, impliedMinRoundTrip }; // triggers sending of a report
            this.initReflectorOffsets();
            return;
        }

        const reflectorAhead = Math.round((adjustedReflectorReceived + reflectorRaw) / 2 - (sent + adjustedAudienceReceived) / 2);

        const change = this.offsetEstimate === null ? 999 : reflectorAhead - reportedOffsetEstimate;
        // don't report if it could be just a rounding error
        if (Math.abs(change) > 1) {
            console.log(`reflector ahead by ${reflectorAhead}ms`, { excessOutbound, excessInbound });
            reportedOffsetEstimate = reflectorAhead;
        }

        this.offsetEstimate = reflectorAhead;
        pingsProcessed++;
        this.minRoundtrip = impliedMinRoundTrip;
    }

    fetchAndClearResetTrigger() {
        const trigger = resetTrigger;
        resetTrigger = null;
        return trigger;
    }

    shutDown() {
        this.session = null;
    }
}


let ModelRootClass, ViewRootClass;
export async function StartSession(model, view) {
    ModelRootClass = model;
    ViewRootClass = view;
}

async function unityDrivenStartSession() {
    const { apiKey, appId, sessionName, debugLogTypes } = theGameEngineBridge;
    const name = `${sessionName}`;
    const password = 'password';
    session = await StartWorldcore({
        appId,
        apiKey,
        name,
        password,
        step: 'manual',
        tps: 33, // deliberately out of phase with 25Hz ticks from Unity, aiming for decent stepping coverage in WebView sessions
        autoSleep: false,
        expectedSimFPS: 0, // 0 => don't attempt to load-balance simulation
        eventRateLimit: 50, // we need a high rate for distributing scene definitions
        flags: ['unity', 'rawtime'],
        debug: debugLogTypes,
        model: ModelRootClass,
        view: PreloadingViewRoot,
        progressReporter: ratio => {
            globalThis.timedLog(`join progress: ${ratio}`);
            theGameEngineBridge.sendCommand('joinProgress', String(ratio));
        }
    });

    sessionOffsetEstimator = new SessionOffsetEstimator(session);

    const STEP_DELAY = 26; // aiming to ensure that there will be a new 50ms physics update on every other step
    let stepCount = 0;
    let lastStep = 0;
    const stepHandler = () => {
        if (!session?.view) return; // don't try stepping after leaving session (including during a rejoin)

        const now = performance.now() | 0;

        // don't try to service ticks that have bunched up
        if (now - lastStep < STEP_DELAY / 4) return;

        lastStep = now;
        performance.mark(`STEP${++stepCount}`);

        Promise.resolve().then(() => session.step(now));
    };

    if (globalThis.CROQUET_NODE) {
        sessionStepper = setInterval(stepHandler, STEP_DELAY); // as simple as that
    } else {
        ticker = () => {
            // NB: this is called from a tickHook and a websocket message handler -
            // so if there's anything heavy to do, schedule it asynchronously.

            timerClient.serviceTimeouts(); // very cheap if there aren't any

            stepHandler();
        };
        session.view.realm.vm.controller.tickHook = ticker;
    }
}

function shutDownSession() {
    session.leave();
    session = null;
    if (globalThis.CROQUET_NODE) {
        clearInterval(sessionStepper);
        sessionStepper = null;
    } else {
        ticker = () => timerClient.serviceTimeouts();
    }
    sessionOffsetEstimator.shutDown();
    sessionOffsetEstimator = null;
}
