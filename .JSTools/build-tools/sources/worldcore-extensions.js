// Worldcore with Unity
//
// Croquet Corporation, 2023

import { ModelService } from "@croquet/worldcore";

export class InitializationManager extends ModelService {

    init() {
        super.init('InitializationManager');
        this.subscribe('game', 'initializeFromView', this.initializeFromView);
    }

    initializeFromView({ viewId, entities }) {
        console.log(entities);
    }

}
InitializationManager.register('InitializationManager');
