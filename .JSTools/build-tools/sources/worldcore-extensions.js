// Worldcore with Unity
//
// Croquet Corporation, 2023

import { ModelService, Actor } from "@croquet/worldcore";

export class InitializationManager extends ModelService {

    init() {
        super.init('InitializationManager');
        this.latestScene = "none"; // empty string doesn't go well over the bridge
        this.client = null; // needs to handle onInitializationStart, onObjectInitialization
        this.subscribe('game', 'initializeFromView', this.initializeFromView);
    }

    setClient(model) {
        this.client = model;
    }

    initializeFromView({ viewId, scene, forceFlag, entities }) {
        // make sure that the requesting view still has the right to impose
        // a new scene setup (if not, there was a race and some other view has
        // slipped in ahead).
        // also check that there's someone to listen to initialisation requests.
        if ((scene === this.latestScene && forceFlag !== "force") || !this.client) return;

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
