# Croquet for Unity Package

This repo contains all Croquet functionality as a unitypackage ready to be dropped into a new project. This is for starting your own project. For usage examples please see our tutorials or other demo repos. 

NOTE: IF YOU HAVE NOT EXPLICITLY BEEN PERMITTED INTO THE CROQUET FOR UNITY BETA, THIS REPO IS STILL AVAILABLE FOR YOU TO HACK AROUND WITH. THERE IS NO GUARANTEE THAT FEATURES WILL NOT INTRODUCE BREAKING CHANGES, UNTIL WE REACH A 1.0 RELEASE. WE WILL BEGIN ADMITTING SMALL BATCHES OF DEVELOPERS INTO THE BETA, AT WHICH POINT WE WILL PROVIDE SUPPORT FOR THOSE DEVELOPERS IN OUR DISCORD. 
If you believe yourself to have a critical need to be in the early beta participants, and can accept that major changes are in progress, please DM Lazarus#7304 via the Croquet Discord.


# Install
Clone the Repo with the following command:
```
git clone https://croquet.github.io/croquet-for-unity-package/croquet-for-unity-package.git
```

# Setup
For a completely setup template (ie, to avoid this manual setup), please check out our template repo.

## Unity Project Setup
Create a new Unity Project via the Unity Hub Application.
Remember to use version `2021.3.19f1`
Select 3D (URP) as the pipeline template.
Select a place on disk for this to live (ideally a git repo).

## Use our suggested gitignore for Unity
TODO: Link Demolition gitignore files (for Unity and Croquet general files)

## Get the Package and Dependencies
In Unity Package Manager add the WebView Package via Github Link:
```
https://github.com/gree/unity-webview.git?path=/dist/package-nofragment
```

Add also our package via this link:
```
https://github.com/croquet/croquet-for-unity-package.git
```

## Get NuGet in order to get the WebSocket
Add this to the package manager:
```
https://github.com/GlitchEnzo/NuGetForUnity
```
Use the NuGet Menu item to search for and install `WebSocketSharp-netstandard`

## Copy the Croquet Source File Stubs
In the Croquet Multiplayer Package (Packages>Croquet Multiplayer) there is a folder called `croquet`.
Copy and paste it outside of the unity project directory, at the root directory of your repo.
This provides you the correct scripts, tools, and file heirarchy for our Croquet menu to continuously build your JS source files.

The overall structure of your repo should look like:
```
- /(git repo root)
    - croquet
        - your_app_code_folder
            - Models.js
            - Views-unity.js
        - your_other_app_code_folder
        - build-tools
    - unity
        - your_unity_project
```

## Create your JS Model / View Files
Place your model code inside of the `/croquet/yourappname/Models.js`
Place your view code inside of the `/croquet/yourappname/Views-unity.js`


## npm install 
```
npm i
``` 
in the `/Croquet` directory

## Create an Default Addressable Assets Group
Window>Asset Management>Group>"Create Asset Group"
Create the default addressable group, this is how we associate particular assets across the bridge for spawning.
It will create an AdressableAssetsData folder in your project.

## Make your own CroquetSettings
Copy the CroqutDefaultSettings into your project.
Enter your API key.

## Enable the Input Handler
We provide a basic keypress and pointer forwarding template that uses Unity's new input system.
See `Croquet/Runtime/UserInputActions` (lightning bolt icon).
Select it and click "Make this the active input map"

Skip this step if you want to use your own completely custom set of input events.


## Create a Unity Scene

Create a new scene
Add a CroquetBridge Prefab from the Packages Prefabs to your scene.
add your App's name (corresponds to the /croquet/yourappname/ folder) to the Croquet Bridge Object.

Associate the CroquetSettings Object created in the last step with the CroquetBridge.


# Usage


# Contribution


# License

