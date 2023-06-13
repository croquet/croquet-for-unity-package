// reconfigurable import script for an app using Croquet on WebView

import { MyModelRoot } from "__APP_SOURCE__/Models";
import { StartSession, GameViewRoot } from "./unity-bridge";

StartSession(MyModelRoot, GameViewRoot);
