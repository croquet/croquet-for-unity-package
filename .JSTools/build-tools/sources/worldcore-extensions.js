// Worldcore with Unity
//
// Croquet Corporation, 2023

import { ModelService, Actor } from "@croquet/worldcore";

export class InitializationManager extends ModelService {

    init() {
        super.init('InitializationManager');
        this.latestScene = "none"; // we need to send this over the bridge, so defaulting to an empty string doesn't work
        this.initializingView = ""; // a scene init is sent in chunks; once we've started receiving from a given view, we ignore chunks from other views
        this.client = null; // needs to handle onInitializationStart, onObjectInitialization
        this.initBufferCollector = []
        this.subscribe(this.id, 'initFromViewChunk', this.initFromViewChunk);
    }

    setClient(model) {
        this.client = model;
    }

    initFromViewChunk({ viewId, sceneName, forceFlag, isFirst, isLast, buf }) {
        // make sure that the requesting view still has the right to impose
        // a new scene setup (if not, there was a race and some other view has
        // slipped in ahead).
        // also check that there's someone to listen to initialisation requests.
        const { client, latestScene, initializingView } = this;
        if (!client || (sceneName === latestScene && forceFlag !== "force") || (initializingView && initializingView !== viewId)) return;

        if (isFirst) this.initBufferCollector = [];
        this.initBufferCollector.push(buf);
        if (!isLast) {
            this.initializingView = viewId; // TBC
        } else {
            // turn the array of chunks into a single buffer
            const bufs = this.initBufferCollector;
            const len = bufs.reduce((acc, cur) => acc + cur.length, 0);
            const all = new Uint8Array(len);
            let ind = 0;
            for (let i = 0; i < bufs.length; i++) {
                all.set(bufs[i], ind);
                ind += bufs[i].length;
            }

            const result = new TextDecoder("utf-8").decode(all);
console.log(`received string of length ${all.length}`);
            const entities = result.split('\x01');

            this.initializingView = null;
            this.latestScene = sceneName;
            this.initBufferCollector = [];

            this.initFromView(viewId, entities);
        }
    }

    initFromView(viewId, entities) {
        this.client.onInitializationStart(viewId);

        entities.forEach(entityString => {
            // console.log(entityString);
            const propertyStrings = entityString.split('|');
            let cls;
            const props = {};
            props.creatingView = viewId;
            propertyStrings.forEach(propAndValue => {
                const [propName, value] = propAndValue.split(':');
                switch (propName) {
                    case 'ID':
                        props.creatingId = value;
                        break;
                    case 'ACTOR':
                        try { cls = Actor.classFromID(value); }
                        catch(e) {
                            console.warn(`Actor class not found for init string: ${entityString}`);
                            cls = false; // mark that we tried and failed
                        }
                        break;
                    case 'position':
                        props.translation = value.split(',').map(Number); // note name change
                        break;
                    case 'rotation':
                    case 'scale':
                        props[propName] = value.split(',').map(Number);
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
    }

}
InitializationManager.register('InitializationManager');
