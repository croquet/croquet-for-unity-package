// Worldcore with Unity
//
// Croquet Corporation, 2023

// note on use of string separators in messages across the bridge
//   \x01 is used to separate the command and string arguments in a message
//   \x02 to separate entire messages in a bundle
//   \x03 to separate the elements in an array-type argument within a message, such as on a property update, say(), or publish()
//   \x04 currently unused
//   \x05 to mark the start of the data argument in a binary-encoded message, such as updateSpatial.

import { mix, Pawn, ViewRoot, ViewService, GetViewService, StartWorldcore, PawnManager, v3_equals, q_equals } from "@croquet/worldcore-kernel";

globalThis.timedLog = msg => {
    const toLog = `${(globalThis.CroquetViewDate || Date).now() % 100000}: ${msg}`;
    performance.mark(toLog);
    console.log(toLog);
};

// globalThis.WC_Left = true; // NB: this can affect behaviour of both models and views
globalThis.CROQUET_NODE = typeof window === 'undefined';

let theGameInputManager;

// theGameEngineBridge is a singleton instance of BridgeToUnity, built immediately
// on loading of this file.  it is never rebuilt.
class BridgeToUnity {
    constructor() {
        this.bridgeIsConnected = false;
        this.startWS();
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
                this.handleUnityMessageOrBundle(msg);
            };
        };
        sock.onclose = _evt => {
            globalThis.timedLog('bridge websocket closed');
            this.bridgeIsConnected = false;
            session.leave();
            if (globalThis.CROQUET_NODE) process.exit(); // if on node, bail out
        };
    }

    sendCommand(...args) {
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
                const forwarder = (logType, logVals) => this.sendCommand('logFromJS', logType, logVals.map(a => typeof a === 'object' ? JSON.stringify(a) : String(a)).join(' '));
                ['log', 'warn', 'error'].forEach(logType => {
                    if (toForward.includes(logType)) console[logType] = (...logVals) => forwarder(logType, logVals);
                    else console[logType] = console[`q_${logType}`];
                });
                break;
            }
            case 'readyForSession': {
                // args are [apiKey, appId, sessionName, assetManifests. earlySubscriptionTopics ]
                const [apiKey, appId, sessionName, assetManifests, earlySubscriptionTopics ] = args;
                globalThis.timedLog(`starting session of ${appId} with key ${apiKey}`);
                this.apiKey = apiKey;
                this.appId = appId;
                this.sessionName = sessionName;
                // console.log({earlySubscriptionTopics});
                this.assetManifests = assetManifests;
                this.earlySubscriptionTopics = earlySubscriptionTopics; // comma-separated list
                this.setReady();
                break;
            }
            case 'event': {
                // DEPRECATED
                // args[0] is event type (currently screenTap, screenDouble)
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
                if (argFormat === undefined) session?.view.publish(scope, eventName);
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
                    session?.view.publish(scope, eventName, eventArgs);
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
                // @@ not sure this will ever make sense
                globalThis.timedLog('shutdown event received');
                session.leave();
                if (globalThis.CROQUET_NODE) process.exit();
                break;
            default:
                if (this.commandHandler) this.commandHandler(command, args);
                else globalThis.timedLog(`unknown Unity command: ${command}`);
        }
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
        const vm = globalThis.CROQUETVM; // @@ privileged information
        vm.controller.connection.reconnectDelay = milliseconds;
        vm.controller.connection.socket.close(4000, 'simulate glitch');
        setTimeout(() => vm.controller.connection.reconnectDelay = 0, 500);
    }

showSetupStats() {
    // pawns keep stats on how long they took to set up.  if this isn't called, the stats will keep building up (but basically harmless).
    console.log(`build: ${Object.entries(buildStats).map(([k, v]) => `${k}:${v}`).join(' ')} total: ${Object.entries(setupStats).map(([k, v]) => `${k}:${v}`).join(' ')}`);
    buildStats.length = setupStats.length = 0;
}
}
export const theGameEngineBridge = new BridgeToUnity();

// GameViewManager is a ViewService, and is therefore constructed afresh
// on Session.join().  if there is a network glitch, the manager will be destroyed
// on disconnection and then rebuilt when the session re-connects.
export const GameViewManager = class extends ViewService {
    constructor(name) {
        super(name || "GameViewManager");

        this.lastGameHandle = 0;
        this.pawnsByGameHandle = {}; // handle => pawn
        this.deferredMessagesByGameHandle = new Map(); // handle => [msgs], iterable in the order the handles are mentioned
        this.deferredGeometriesByGameHandle = {}; // handle => msg; order not important
        this.deferredOtherMessages = []; // events etc, always sent after gameHandle-specific messages

        this.forwardedEventTopics = {}; // topic (scope:eventName) => handler

        this.unityMessageThrottle = 45; // ms (every two updates at 26ms)
        this.unityGeometryThrottle = 90; // ms (every four updates at 26ms)
        this.lastMessageFlush = 0;
        this.lastGeometryFlush = 0;

        this.assetManifests = {};

        theGameEngineBridge.setCommandHandler(this.handleUnityCommand.bind(this));

        const earlySubs = theGameEngineBridge.earlySubscriptionTopics;
        if (earlySubs) {
            earlySubs.split(',').forEach(topic => this.registerTopicForForwarding(topic));
        }

        const { assetManifests } = theGameEngineBridge;
        if (assetManifests) {
            const parseArray = str => str.split(',').filter(Boolean); // remove empties
            const manifestStrings = assetManifests.split('\x03');
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
        if (theGameEngineBridge.bridgeIsConnected) theGameEngineBridge.sendCommand('tearDownSession');
        theGameEngineBridge.setCommandHandler(null);
    }

    nextGameHandle() {
        return ++this.lastGameHandle;
    }

    unityId(gameHandle) {
        // currently redundant.  previously checked for reserved handles.
        return gameHandle;
    }

    getPawn(gameHandle) {
        return this.pawnsByGameHandle[gameHandle] || null;
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
        this.expediteGeometryFlush();
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
        delete this.pawnsByGameHandle[gameHandle];
        this.deferredMessagesByGameHandle.delete(gameHandle); // if any
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
        }

        if (now - (this.lastGeometryFlush || 0) >= this.unityGeometryThrottle) {
            this.lastGeometryFlush = now;
            this.flushGeometries();
        }
    }

    expediteMessageFlush() {
        // guarantee that messages will flush on next update
        this.lastMessageFlush = null;
    }

    expediteGeometryFlush() {
        // guarantee that geometries will flush on next update
        this.lastGeometryFlush = null;
    }

    flushDeferredMessages() {
const pNow = performance.now();

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
    get gameHandle() { return this._gameHandle || (this._gameHandle = this.gameViewManager.nextGameHandle()) }
    get componentNames() { return this._componentNames || (this._componentNames = new Set()) }

    constructor(actor) {
        super(actor);

        this.throttleFromUnity = 100; // ms
        this.messagesAwaitingCreation = []; // removed once creation is requested
        this.geometryAwaitingCreation = null; // can be written by PM_Spatial and its subclasses
        this.isViewReady = false;
    }

    initialize(actor) {
        // construction is complete, through all mixin layers

        const manifest = this.gameViewManager.assetManifests[actor.gamePawnType];
        const { statics, watched } = manifest;
        // gather any statics into an argument on the initialisation message:
        // an array with pairs   propName1, propVal1, propName2,...
        const propertyStrings = [];
        (statics.concat(watched)).forEach(propName => {
            if (actor[propName] === undefined) {
                console.warn(`found undefined value for ${propName}`, this);
            }
            propertyStrings.push(propName, theGameEngineBridge.encodeValueAsString(actor[propName]));
        });
        const initArgs = {
            type: actor.gamePawnType,
            propertyValues: propertyStrings
        };
        this.setGameObject(initArgs); // args may be adjusted by mixins

        // additionally, for the "watched" properties, set up listeners for the
        // automatically generated fooSet events (in the case of property foo)
        watched.forEach(propName => {
            const setEventName = `${propName}Set`;
            console.log(`setting up listener for ${setEventName} on ${this.actor.id}`);
            this.listenOnce(setEventName, ({ value }) => {
                this.forwardWatchedProperty(setEventName, value);
            });
        });
    }

    setGameObject(viewSpec) {
        // analogue of setRenderObject in mixins for THREE.js rendering

        // because pawn creation is asynchronous, it's possible that the
        // actor has already been destroyed by the time we get here.  in
        // that case, don't bother creating the unity gameobject at all.
        if (this.actor.doomed) return;

        if (!viewSpec.confirmCreation) this.isViewReady = true; // not going to wait

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
        this.messagesAwaitingCreation.forEach(cmdAndArgs => {
            this.gameViewManager.sendDeferredFromPawn(...[this.gameHandle, ...cmdAndArgs]);
        });
        delete this.messagesAwaitingCreation;

        // PM_GameSpatial introduces geometryAwaitingCreation
        if (this.geometryAwaitingCreation) {
            this.gameViewManager.updatePawnGeometry(this.gameHandle, this.geometryAwaitingCreation);
            this.geometryAwaitingCreation = null;
        }
    }

    forwardWatchedProperty(setEventName, newVal) {
        const scope = this.actor.id;
        const stringArg = theGameEngineBridge.encodeValueAsString(newVal);
        const overrideKey = setEventName;
        this.gameViewManager.sendDeferredWithOverride(this.gameHandle, overrideKey, 'croquetPub', scope, setEventName, stringArg);
    }

    unityViewReady(estimatedReadyTime) {
        // unity side has told us that the object is ready for use
        // console.log(`unityViewReady for ${this.gameHandle}`);
        this.isViewReady = true;
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
        if (this.messagesAwaitingCreation) {
            this.messagesAwaitingCreation.push([command, ...args]);
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
        this.listen("colorSet", this.onColorSet);
        this.onColorSet();
    }

    onColorSet() {
        this.sendToUnity('setColor', this.actor.color);
    }
};
gamePawnMixins.Material = PM_GameMaterial;

export const PM_GameSpatial = superclass => class extends superclass {

    constructor(actor) {
        super(actor);
        this.componentNames.add('CroquetSpatialComponent');
        this.resetGeometrySnapState();
    }

    get scale() { return this.actor.scale }
    get translation() { return this.actor.translation }
    get rotation() { return this.actor.rotation }
    get local() { return this.actor.local }
    get global() { return this.actor.global }
    get lookGlobal() { return this.global } // Allows objects to have an offset camera position -- obsolete?

    get forward() { return this.actor.forward }
    get up() { return this.actor.up }

    geometryUpdateIfNeeded() {
        if ((this.driving && this.lastSentTranslation) || this.rigidBodyType === 'static' || !this.isViewReady || this.doomed) return null;

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
        this._scaleSnapped = this._rotationSnapped = this._translationSnapped = true;
    }

    updateGeometry(updateSpec) {
        // opportunistic geometry update.
        // if the game pawn hasn't been created yet, store this update to be
        // delivered once the creation has been requested.
        if (this.messagesAwaitingCreation) {
            // not ready yet.  store it (overwriting any previous value)
            this.geometryAwaitingCreation = updateSpec;
        } else {
            this.gameViewManager.updatePawnGeometry(this.gameHandle, updateSpec);
        }
    }

    geometryUpdateFromUnity(update) {
        this.set(update, this.throttleFromUnity);
    }

};
gamePawnMixins.Spatial = PM_GameSpatial;

export const PM_GameSmoothed = superclass => class extends PM_GameSpatial(superclass) {

    constructor(actor) {
        super(actor);
        this.tug = 0.2;
        this.throttle = 100; //ms

        this.listenOnce("scaleSnap", this.onScaleSnap);
        this.listenOnce("rotationSnap", this.onRotationSnap);
        this.listenOnce("translationSnap", this.onTranslationSnap);
    }

    // $$$ should send the tug value across the bridge, and update when it changes
    set tug(t) { this._tug = t }
    get tug() { return this._tug }

    setGameObject(viewSpec) {
        viewSpec.waitToPresent = true;
        const components = viewSpec.extraComponents ? viewSpec.extraComponents.split(',') : [];
        if (!components.includes('PresentOnFirstMove')) {
            viewSpec.extraComponents = (components.concat(['PresentOnFirstMove'])).join(',');
        }
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

export const PM_GameAvatar = superclass => class extends superclass {

    constructor(actor) {
        super(actor);
        this.componentNames.add('CroquetAvatarComponent');
        this.onDriverSet();
        this.listenOnce("driverSet", this.onDriverSet);
    }

    get isMyAvatar() {
        return this.actor.driver === this.viewId;
    }

    onDriverSet() {
        // on creation, this method is sending a message to Unity that precedes
        // the makeGameObject message itself.  however, it will automatically be held
        // back until immediately after makeGameObject has been sent.
        if (this.isMyAvatar) {
            this.driving = true;
            this.drive();
        } else {
            this.driving = false;
            this.park();
        }
    }

    park() { }
    drive() { }

};
gamePawnMixins.Avatar = PM_GameAvatar;

// GamePawnManager is [will be] a specialisation of the standard
// Worldcore PawnManager, with pawn-creation logic that removes the need for
// static declaration of pawn classes.
// GameViewManager is a new kind of service, created specifically for
// the bridge to Unity, handling the creation and management of Unity-side
// gameObjects that track the Croquet pawns.

// $$$ temporary mega-hack: overwrite the newPawn method in the PawnManager
// prototype.
PawnManager.prototype.newPawn = function(actor) {
        // for the unity world, pawn classes are built on the fly using
        // the mixins defined above, based on the pawnMixins list of mixin
        // names provided by the actor.
        // if there are any elements in the list, we prepend the obligatory
        // Base mixin (PM_GameRendered) for a game-rendered pawn.
        // an actor that doesn't want any game pawn still gets a vanilla Pawn,
        // e.g. so it can support having children.

        const { gamePawnType } = actor;
        if (!gamePawnType) {
            // doesn't want a game pawn at all
            const p = new Pawn(actor);
            this.pawns.set(actor.id, p);
            return p;
        }

        if (!this._gameViewManager) this._gameViewManager = GetViewService("GameViewManager");

        const manifest = this._gameViewManager.assetManifests[gamePawnType];
        if (!manifest) {
            console.warn(`no manifest for gamePawnType "${gamePawnType}"`);
            return null;
        }

        const mixinNames = manifest.mixins; // can be empty
        const mixins = ['Base'].concat(mixinNames).map(n => gamePawnMixins[n]);
        const PawnClass = mix(Pawn).with(...mixins);
        const p = new PawnClass(actor);
        p.initialize(actor); // new: 2-phase init, so all constructors run to completion first

        this.pawns.set(actor.id, p);
        return p;
};

export class GameViewRoot extends ViewRoot {

    static viewServices() {
        // $$$ for now, hard-code these two.  figure out later how to
        // customise (presumably based on annotation in the ModelRoot)
        return [GameViewManager, GameInputManager];
    }

    constructor(model) {
        super(model);

        if (sessionOffsetEstimator) sessionOffsetEstimator.initReflectorOffsets();

        // we treat the construction of the view as a signal that the session is
        // ready to talk across the bridge
        theGameEngineBridge.sendCommand('croquetSessionRunning', this.viewId);
        globalThis.timedLog("session running");
    }

}

export class GameInputManager extends ViewService {
    get gameViewManager() { return this._gameViewManager || (this._gameViewManager = GetViewService('GameViewManager')) }

    constructor(name) {
        super(name || "GameInputManager");

        this.customEventHandlers = {};

        theGameInputManager = this;
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

        switch (event) {
            case 'keyDown': {
                const keyCode = args[1];
                this.publish('input', `${keyCode.toLowerCase()}Down`);
                this.publish('input', 'keyDown', { key: keyCode });
                break;
            }
            case 'keyUp': {
                const keyCode = args[1];
                this.publish('input', `${keyCode.toLowerCase()}Up`);
                this.publish('input', 'keyUp', { key: keyCode });
                break;
            }
            case 'pointerDown': {
                const button = Number(args[1]);
                this.publish('input', 'pointerDown', { button });
                break;
            }
            case 'pointerUp': {
                const button = Number(args[1]);
                this.publish('input', 'pointerUp', { button });
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
                        const xyz = [x, y, z].map(Number);
                        hitList.push({ pawn, xyz, layers});
                    }
                }
                if (hitList.length) this.publish('input', 'pointerHit', { hits: hitList });
                break;
            }
            default:
        }
    }
}


// simplified interval handling for game-engine apps

export const TimerClient = class {
    constructor() {
        this.timeouts = {};
        // https://stackoverflow.com/questions/69148796/how-to-load-webworker-from-local-in-wkwebview
        this.timerWorker = new Worker(window.URL.createObjectURL(
            new Blob([document.getElementById("timerWorker").textContent], {
                type: "application/javascript",
            })
        ));
        // this.timerWorker = new Worker(new URL('timer-worker.js', import.meta.url));
        this.timerIntervalSubscribers = {};
        this.timerWorker.onmessage = ({ data: intervalOrId }) => {
            if (intervalOrId === 'interval') {
                Object.values(this.timerIntervalSubscribers).forEach(fn => fn());
            } else {
                const record = this.timeouts[intervalOrId];
                if (record) {
                    const { callback } = record;
                    if (callback) callback();
                    delete this.timeouts[intervalOrId];
                }
            }
        };
    }
    setTimeout(callback, duration) {
        const id = Math.random().toString();
        this.timeouts[id] = { callback };
        this.timerWorker.postMessage({ id, duration });
        return id;
    }
    clearTimeout(id) {
        delete this.timeouts[id];
    }
    setInterval(callback, interval, name = 'interval') {
        // NB: for now, the worker only runs a single interval timer.  all subscribed clients must be happy being triggered with the same period.
        this.timerIntervalSubscribers[name] = callback;
        this.timerWorker.postMessage({ interval });
    }
    clearInterval(name = 'interval') {
        delete this.timerIntervalSubscribers[name];
    }
};

const timerClient = globalThis.CROQUET_NODE ? globalThis : new TimerClient();
if (globalThis.CROQUET_NODE) {
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
}


let reflectorJourneyEstimates;
let reportedOffsetEstimate = null;
let resetTrigger = null;
let pingsProcessed;
class SessionOffsetEstimator {
    // maintain a best guess of the minimum offset between the local wall clock and the reflector's raw time as received on PING messages and their PONG responses.  this is used purely to judge when an event has been held up on one or other leg, and hence to adjust the calculation of the estimated offset between local and reflector time.
    // one problem we have to deal with is network batching of messages, meaning that they often arrive late.  so whenever a reflector event indicates that it raw time is earlier than our current guess, we assume that this is closer to the actual timing.  immediately adjust our estimate.
    // but then, in case the minimum offset is in fact gradually growing - i.e., the local wall clock is gradually gaining on the reflector's - the estimate is continually nudged forwards using a bias that adds 0.2ms per second (12ms per minute) of elapsed time since the last adjustment.  we expect that bias to be overridden every few seconds by an accurately timed event - but if the actual offset really is drifting by a few ms per minute, the bias should ensure that we capture that.
    constructor(session) {
        this.session = session;
        const controller = this.controller = session.view.realm.vm.controller;

        this.offsetEstimate = null;
        this.minRoundtrip = 0;

        controller.connection.pongHook = args => this.handlePong(args);
        this.initReflectorOffsets();
        this.sendPing();
    }

    sendPing() {
        if (session.view) {
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
}


let session, sessionOffsetEstimator;
export async function StartSession(model, view) {
    // console.profile();
    // setTimeout(() => console.profileEnd(), 10000);
    await theGameEngineBridge.readyP;
    globalThis.timedLog("bridge ready");
    const { apiKey, appId, sessionName } = theGameEngineBridge;
    const name = `${sessionName}`;
    const password = 'password';
    session = await StartWorldcore({
        appId,
        apiKey,
        name,
        password,
        step: 'manual',
        tps: 20,
        autoSleep: false,
        expectedSimFPS: 0, // 0 => don't attempt to load-balance simulation
        flags: ['unity', 'rawtime'],
        // debug: globalThis.CROQUET_NODE ? ['session'] : ['session', 'messages'],
        model,
        view,
        progressReporter: ratio => {
            globalThis.timedLog(`join progress: ${ratio}`);
            theGameEngineBridge.sendCommand('joinProgress', String(ratio));
        }
    });

    sessionOffsetEstimator = new SessionOffsetEstimator(session);

    const STEP_DELAY = 26; // aiming to ensure that there will be a new 50ms physics update on every other step
    let stepHandler = null;
    let stepCount = 0;
    timerClient.setInterval(() => {
        performance.mark(`STEP${++stepCount}`);
        if (stepHandler) stepHandler();
    }, STEP_DELAY);

    let lastStep = 0;
    stepHandler = () => {
        if (!session.view) return; // don't try stepping after leaving session (including during a rejoin)

        const now = Date.now();
        // don't try to service ticks that have bunched up
        if (now - lastStep < STEP_DELAY / 2) return;
        lastStep = now;
        session.step(now);
    };
}
