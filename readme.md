# Croquet for Unity Package

This repo contains all Croquet functionality as a unitypackage ready to be dropped into a new project. This is for starting your own project. For usage examples please see our tutorials or other demo repos.

NOTE: IF YOU HAVE NOT EXPLICITLY BEEN PERMITTED INTO THE CROQUET FOR UNITY BETA, THIS REPO IS STILL AVAILABLE FOR YOU TO HACK AROUND WITH. THERE IS NO GUARANTEE THAT FEATURES WILL NOT INTRODUCE BREAKING CHANGES, UNTIL WE REACH A 1.0 RELEASE. WE WILL BEGIN ADMITTING SMALL BATCHES OF DEVELOPERS INTO THE BETA, AT WHICH POINT WE WILL PROVIDE SUPPORT FOR THOSE DEVELOPERS IN OUR DISCORD.
If you believe yourself to have a critical need to be in the early beta participants, and can accept that major changes are in progress, please DM Lazarus#7304 via the Croquet Discord.


# Setup

## Unity Project
Create a new Unity Project via the Unity Hub Application.

So far, the Croquet for Unity (C4U) package has been built with and tested on projects using Unity editor version `2021.3.19f1`. The smoothest path to integration (and tracking of updates) would be into a project built for the same version - but if you are confident in managing a bit of divergence, this is not a strong restriction.

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
Now that all dependencies are in place, add the C4U package using this git URL:
```
https://github.com/croquet/croquet-for-unity-package.git
```

## Copy the Croquet JavaScript build tools
Within the `Assets` folder of your Unity project, make a folder `CroquetJS`. This is where your JavaScript code will reside, along with the build tools that package the JavaScript code for execution and deployment.

As part of the installation of the C4U package, the Unity editor will have been given a `Croquet` menu. On this menu, now invoke the option `Copy JS Build Tools`. This will copy some files into the `Assets/CroquetJS` that you just created, and others into the root of the repository (i.e., the parent directory of the Unity project itself).

**DON'T SKIP THIS!** Now install the dependencies, in the repository root:

```
cd <repo root directory>
npm install
```

**Note: whenever you download an update to the "Croquet Multiplayer" package using the package manager, you should immediately repeat this copy action and dependency installation.**

C4U expects the `CroquetJS` folder of your project to be organized into the following directory structure:

```
- (unity project root)
    - Assets
        - CroquetJS
            - (your_app_name_1)
                - Models.js
            - (your_app_name_2)
                - Models.js
            - build-tools
    - Packages
    - etc
```

The `your_app_name` subdirectories can be used for independent apps - for example, in our `croquet-for-unity-tutorials` repository, there are independent directories for nine introductory apps. Each app is expected to be associated with a scene, as will be explained below.

## Create a Default Addressable Assets Group
C4U expects to find a default addressable-assets group, which is how we associate particular assets across the bridge for spawning.  For now it is required to be present, even if your apps have no use for it.

The following menu item
```
Window => Asset Management => Group => "Create Asset Group"
```
will create the group; an `AddressableAssetsData` folder will appear in your project.

## Create and fill in a CroquetSettings asset
Find the `CroquetDefaultSettings` asset within the C4U package, by going to `Packages/Croquet Multiplayer/Scripts/Runtime/Settings`. Copy the settings into your project - for example, into an `Assets/Settings` directory.

The most important field to set up in the settings asset is the **Api Key**, which is a token of around 40 characters that you can create for yourself at https://croquet.io/account.  It provides access to the Croquet infrastructure.

The **App Prefix** is the way of identifying with your organization the Croquet apps that you develop and run.  The combination of this prefix and the App Name provided on the Croquet Bridge component in each scene (see below) is a full App ID - for example, `io.croquet.worldcore.guardians`.  For running applications within one of Croquet's repositories (`tutorials`, `guardians` etc), it is fine to leave this prefix as is, but when you develop your own apps you must change the prefix so that the App ID is a globally unique identifier.  The ID must follow the Android reverse domain naming convention - i.e., each dot-separated segment must start with a letter, and only letters, digits, and underscores are allowed.

**For MacOS:** Find the Path to your Node executable, by going to a terminal and running
```
which node
```
On the Settings asset, fill in the **Path to Node** field with the path.

**For Windows:** Your system may complain about "Script Execution Policy" which will prevent our setup scripts from running. The following command allows script execution on Windows for the current user (respond **Yes to [A]ll** when prompted):
```
Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser
```

## Create a Unity Scene

Create a new scene _(note: a scene's name is used in our package to tie the scene to its assets and other build aspects; these features have not yet been tested with names containing white space, punctuation etc)_

From the `Croquet Multiplayer` package's `Prefabs` folder drag a `CroquetBridge` object to your scene. Configure the bridge object as follows:

Associate the **App Properties** field with the `CroquetSettings` object that you created in the last step.

Set the **App Name** to the `your_app_name` part of the path, illustrated above, to the directory holding the JavaScript source that belongs with this scene. For example, a name `myGame` would connect this scene to the code inside `Assets/CroquetJS/myGame`.

## Create your JS Model File(s)
Create a file `Models.js` that implements the behavior you want for your app.  To get started, you can copy any Models file from under the `CroquetJS` folder of one of our demonstration repositories.

**TBC...**

## Enable the Input Handler
We provide a basic keypress and pointer forwarding template that uses Unity's new input system.
See `Croquet/Runtime/UserInputActions` (lightning bolt icon).
Select it and click "Make this the active input map"

Skip this step if you want to use your own completely custom set of input events.

# Usage


# Contribution


# License

