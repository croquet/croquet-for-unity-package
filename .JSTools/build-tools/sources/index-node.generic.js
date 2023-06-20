// reconfigurable startup script for an app using Croquet on NodeJS

import { MyModelRoot } from "__APP_SOURCE__/Models";
import { StartSession, GameViewRoot } from "./unity-bridge";

StartSession(MyModelRoot, GameViewRoot);
