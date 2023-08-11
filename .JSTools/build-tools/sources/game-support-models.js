// Worldcore with Unity
//
// Croquet Corporation, 2023

import { ModelRoot, ModelService, Actor, RegisterMixin, q_normalize } from "@croquet/worldcore-kernel";

// InitializationManager is a model service that knows how to instantiate a set of actors from an init chunk
export class InitializationManager extends ModelService {

    init() {
        super.init('InitializationManager');
        this.activeScene = ""; // the scene we're running, or getting ready to run
        this.activeSceneState = ""; // preload, initializing, running
        this.initializingView = ""; // the view that has permission from us to provide the data for activeScene

        this.client = null; // needs to handle onPrepareForInitialization, onInitializationStart, onObjectInitialization

        // @@ DON'T TRY THIS AT HOME
        // workaround for Constants being frozen
        this.sceneDefinitions = null;
        if (globalThis.GameConstants?.sceneText) { // undefined, or empty string
            this.setSceneDefinitions(globalThis.GameConstants.sceneText);
        }

        this.initBufferCollector = [];
        this.lastInitString = null; // if activeSceneState is running, this is the string that was used to initialise it.  we can reload the scene instantly by reusing this.

        this.subscribe(this.sessionId, 'requestToLoadScene', this.handleRequestToLoadScene);
        this.subscribe(this.sessionId, 'requestToInitScene', this.handleRequestToInitScene);
        this.subscribe(this.sessionId, 'sceneInitChunk', this.sceneInitChunk);
        this.subscribe(this.sessionId, 'view-exit', this.handleViewExit);
    }

    setSceneDefinitions(sceneText) {
        this.sceneDefinitions = {};
        const sceneDefArray = sceneText.split('\x02');
        // the file contains sceneName1 | definition1 | sceneName2 | definition2 etc
        for (let i = 0; i < sceneDefArray.length; i += 2) {
            const sceneName = sceneDefArray[i];
            const definition = sceneDefArray[i + 1];
            console.log(`definition of scene ${sceneName}: ${definition.length} chars`);
            this.sceneDefinitions[sceneName] = definition;
        }
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
            this.initializingView = null; // cut off any in-progress load for a previous scene
            const definition = this.sceneDefinitions?.[sceneName];
            if (!definition || forceRebuild) {
                this.lastInitString = null;
                this.activeSceneState = 'preload';
            } else {
                console.log(`found pre-built definition of ${sceneName}`);
                this.lastInitString = definition;
                this.activeSceneState = 'loading';
            }
        }
        console.log(`approved request to load ${sceneName}; state now "${this.activeSceneState}"`);
        if (forceRebuild) console.warn(`forced to request fresh definition for ${sceneName} from Unity`);

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
                    case 'pos':
                        props.translation = value.split(',').map(Number); // note name change
                        break;
                    case 'rot':
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

export class GameModelRoot extends ModelRoot {
    static modelServices() {
        return [InitializationManager];
    }
}
GameModelRoot.register('GameModelRoot');
