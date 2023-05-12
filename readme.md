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

**Note: for reasons of code organization (see below), for now the Unity project MUST be called 'unity'.**  We will be relaxing this requirement in due course.

Remember to use version `2021.3.19f1`
Select 3D (URP) as the pipeline template.
Select a place on disk for this to live (ideally a git repo).

## Use our suggested gitignore for Unity
TODO: Link sample gitignore files (for Unity and Croquet general files)

## Get the Dependencies and the Package

#### WebSocket
If you do not already have NuGet installed, add this to the package manager:
```
https://github.com/GlitchEnzo/NuGetForUnity
```
Then use the NuGet Menu item to search for and install `WebSocketSharp-netstandard`

#### WebView

In Unity Package Manager add GREE, Inc.'s `unity-webview` package (using "Add package from git URL..."):
```
https://github.com/gree/unity-webview.git?path=/dist/package-nofragment
```

#### Croquet for Unity (C4U)
Now that all dependencies are in place, add our package using this git URL:
```
https://github.com/croquet/croquet-for-unity-package.git
```

## Copy the sample Croquet source files
For now, C4U expects your project to be organized into the following directory structure (including naming the main sub-directories **exactly** `croquet` and `unity`):

```
- (project root - e.g., root of a git repo)
    - croquet
        - (your_app_name_1)
            - Models.js
            - Views-unity.js
        - (your_app_name_2)
            - as above
        - build-tools
        - package.json
    - unity
        - Assets
        - Packages
        - etc
```

On the custom Croquet menu that appears in the Unity Editor once our package is installed, the item "Copy JS Sample Zip" will extract sample files for the JavaScript side of a C4U project.  **This operation will write a file `croquet.zip` into the assumed "project root" - i.e, the directory above your Unity project.**

Unpacking the zip file produces the `croquet` folder, which contains the scripts and code needed to bundle your app's JavaScript for use from Unity.  It also includes sample code (Model and View files) for a trivial app called `template`.  The zip file can be discarded after unpacking.

**DON'T SKIP THIS!**  In the unpacked `(root)/croquet` directory, run
```
npm install
```
to install additional necessary libraries and tools.

## Create a Default Addressable Assets Group
C4U expects to find a default addressable-assets group, which is how we associate particular assets across the bridge for spawning.  For now it is required to be present, even if your apps have no use for it.

The following menu item
```
Window>Asset Management>Group>"Create Asset Group"
```
will create the group; an AddressableAssetsData folder will appear in your project.

## Make your own CroquetSettings
Copy the CroquetDefaultSettings into your project.
Enter your API key.

## Create your JS Model / View Files
Edit the `Models.js` and `Views-unity.js` files to implement the behavior you want for your app.  To get started, you can copy the files from `template` into a directory with an app name of your choice.  Putting this name into the `App Name` field of the Croquet Bridge object in a Unity scene tells C4U where to find the JavaScript source for that scene.


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

