using UnityEditor;
using UnityEngine;

/// <summary>
/// Contains menu actions for creating Croquet settings assets and adding Croquet Bridge to the scene.
/// </summary>
public class CroquetContextMenuActions
{
    /// <summary>
    /// Creates a new Croquet settings asset.
    /// </summary>
    [MenuItem("Assets/Create/Croquet/New Croquet Settings", false, 1)]
    public static void CreateMyAsset()
    {
        string path = AssetDatabase.GetAssetPath(Selection.activeObject);

        if (string.IsNullOrEmpty(path) || !AssetDatabase.IsValidFolder(path))
        {
            path = "Assets";
        }
        else
        {
            path += "/";
        }

        string assetPathAndName = AssetDatabase.GenerateUniqueAssetPath(path + "CroquetDefaultSettings.asset");
        Debug.Log("Loading CroquetBridge prefab from path: Packages/com.croquet.multiplayer/Prefabs/CroquetBridge.prefab");

        Debug.Log($"Attempting to load CroquetDefaultSettings asset from path: /Packages/com.croquet.multiplayer/Runtime/Settings/CroquetDefaultSettings.asset");
        
        //Find the CroquetDefaultSettings asset in the package
        var allAssetPaths = AssetDatabase.GetAllAssetPaths();
        CroquetSettings settingsAsset = null;
        for (int i = 0; i < allAssetPaths.Length; ++i)
        {
            if (allAssetPaths[i].Contains("CroquetDefaultSettings.asset"))
                settingsAsset = AssetDatabase.LoadAssetAtPath<CroquetSettings>(allAssetPaths[i]);
        }

        if (settingsAsset == null)
        {
            Debug.LogError("Could not load CroquetDefaultSettings asset. Check the path is correct and the asset type matches CroquetSettings.");
        }
        else
        {
            Debug.Log("CroquetDefaultSettings asset loaded successfully.");
        }


        CroquetSettings instance = ScriptableObject.CreateInstance<CroquetSettings>();
        EditorUtility.CopySerialized(settingsAsset, instance);

        AssetDatabase.CreateAsset(instance, assetPathAndName);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.FocusProjectWindow();
        Selection.activeObject = instance;
    }

    /// <summary>
    /// Validates the "New Croquet Settings" menu item.
    /// </summary>
    /// <returns>True if the selected object is a folder in the Assets directory, false otherwise.</returns>
    // Validate the MenuItem
    [MenuItem("Assets/Create/Croquet/New Croquet Settings", true)]
    public static bool CreateMyAssetValidation()
    {
        // This returns true when the selected object is a folder in the Assets directory
        string path = AssetDatabase.GetAssetPath(Selection.activeObject);
        return AssetDatabase.IsValidFolder(path);
    }

    /// <summary>
    /// Adds the Croquet Bridge prefab to the scene.
    /// </summary>
    [MenuItem("GameObject/Croquet/Add Croquet Bridge", false, 1)]
    static void AddCroquetBridgeToScene()
    {
        // Load the CroquetBridge prefab from the package
        var allAssetPaths = AssetDatabase.GetAllAssetPaths();
        GameObject croquetBridgePrefab = null;
        for (int i = 0; i < allAssetPaths.Length; ++i)
        {
            if (allAssetPaths[i].Contains("CroquetBridge.prefab"))
                croquetBridgePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(allAssetPaths[i]);
        }

        if (croquetBridgePrefab == null)
        {
            Debug.LogError("Could not find CroquetBridge prefab in the package.");
            return;
        }

        // Instantiate the prefab into the active scene
        GameObject croquetBridgeInstance = PrefabUtility.InstantiatePrefab(croquetBridgePrefab) as GameObject;

        if (croquetBridgeInstance != null)
        {
            Selection.activeGameObject = croquetBridgeInstance;
            SceneView.FrameLastActiveSceneView();
            // Register the creation in the undo system
            Undo.RegisterCreatedObjectUndo(croquetBridgeInstance, "Add Croquet Bridge");
        }
        else
        {
            Debug.LogError("Failed to instantiate CroquetBridge prefab.");
        }
    }
    

    /// <summary>
    /// Adds the Croquet Bridge prefab as a child of the selected object.
    /// </summary>
    /// <param name="command">The menu command.</param>
    [MenuItem("CONTEXT/Transform/Add Croquet Bridge to Selected Object")]
    private static void AddCroquetBridgeToSelected(MenuCommand command)
    {
        // Load the CroquetBridge prefab
        GameObject croquetBridgePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Packages/com.croquet.multiplayer/Prefabs/CroquetBridge.prefab");
        if (croquetBridgePrefab == null)
        {
            Debug.LogError("Could not find CroquetBridge prefab in the package.");
            return;
        }

        // Instantiate the prefab as a child of the selected object
        GameObject croquetBridgeInstance = PrefabUtility.InstantiatePrefab(croquetBridgePrefab, ((Transform)command.context).gameObject.transform) as GameObject;
        if (croquetBridgeInstance != null)
        {
            // Set the new instance to be the active GameObject
            Selection.activeGameObject = croquetBridgeInstance;

            // Register the creation in the undo system
            Undo.RegisterCreatedObjectUndo(croquetBridgeInstance, "Add Croquet Bridge to Selected Object");
        }
        else
        {
            Debug.LogError("Failed to instantiate CroquetBridge prefab.");
        }
    }

    /// <summary>
    /// Validates the "Add Croquet Bridge" menu item.
    /// </summary>
    /// <returns>True if the Croquet Bridge prefab is available, false otherwise.</returns>
    [MenuItem("GameObject/Croquet/Add Croquet Bridge", true)]
    private static bool ValidateAddCroquetBridgeToScene()
    {
        // Check if the CroquetBridge prefab is available
        var allAssetPaths = AssetDatabase.GetAllAssetPaths();
        GameObject croquetBridgePrefab = null;
        for (int i = 0; i < allAssetPaths.Length; ++i)
        {
            if (allAssetPaths[i].Contains("CroquetBridge.prefab"))
                croquetBridgePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(allAssetPaths[i]);
        }

        return croquetBridgePrefab != null;
    }
}
