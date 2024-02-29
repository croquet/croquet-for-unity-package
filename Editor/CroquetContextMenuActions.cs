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
    [MenuItem("Assets/Croquet/New Croquet Settings", false, -1)]
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
    [MenuItem("Assets/Croquet/New Croquet Settings", true)]
    public static bool CreateMyAssetValidation()
    {
        // This returns true when the selected object is a folder in the Assets directory
        string path = AssetDatabase.GetAssetPath(Selection.activeObject);
        return AssetDatabase.IsValidFolder(path);
    }

    /// <summary>
    /// Adds the Croquet Bridge prefab to the scene.
    /// </summary>
    [MenuItem("GameObject/Croquet/Add Croquet Bridge", false, -1)]
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

    // Updated method to find CroquetBridge prefab dynamically
    [MenuItem("GameObject/Add Croquet Bridge to Selected Object", false, 10)]
    private static void AddCroquetBridgeToSelected(MenuCommand command)
    {
        Transform targetTransform = Selection.activeTransform;
        if (targetTransform == null)
        {
            Debug.LogError("No valid target selected.");
            return;
        }

        GameObject croquetBridgePrefab = FindCroquetBridgePrefab();
        if (croquetBridgePrefab == null)
        {
            Debug.LogError("Could not find CroquetBridge prefab in the project.");
            return;
        }

        GameObject croquetBridgeInstance = PrefabUtility.InstantiatePrefab(croquetBridgePrefab, targetTransform) as GameObject;
        if (croquetBridgeInstance != null)
        {
            // If you want the prefab to be a sibling rather than a child, use the following line instead:
            // croquetBridgeInstance.transform.SetParent(targetTransform.parent);

            croquetBridgeInstance.transform.SetAsLastSibling(); // This places it as the last sibling in the hierarchy

            Selection.activeGameObject = croquetBridgeInstance;
            Undo.RegisterCreatedObjectUndo(croquetBridgeInstance, "Add Croquet Bridge to Selected Object");
        }
        else
        {
            Debug.LogError("Failed to instantiate CroquetBridge prefab.");
        }
    }

    [MenuItem("GameObject/Croquet/Add Croquet Bridge", true)]
    private static bool ValidateAddCroquetBridgeToScene()
    {
        return FindCroquetBridgePrefab() != null;
    }

    // Helper method to find the CroquetBridge prefab dynamically
    private static GameObject FindCroquetBridgePrefab()
    {
        var allAssetPaths = AssetDatabase.GetAllAssetPaths();
        foreach (string assetPath in allAssetPaths)
        {
            if (assetPath.EndsWith("CroquetBridge.prefab"))
            {
                return AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            }
        }
        return null;
    }
}
