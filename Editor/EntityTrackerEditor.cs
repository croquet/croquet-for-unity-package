using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class EntityTrackerEditor
{
    static EntityTrackerEditor()
    {
        // Hook into the scene save event to ensure IDs are assigned before the scene is saved
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        EditorApplication.hierarchyChanged += OnHierarchyChanged;
    }

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingEditMode)
        {
            AssignUniqueIDsToEntities();
        }
    }

    private static void OnHierarchyChanged()
    {
        AssignUniqueIDsToEntities();
    }

    public static void AssignUniqueIDsToEntities()
    {
        CroquetEntityComponent[] entities = GameObject.FindObjectsOfType<CroquetEntityComponent>();

        foreach (var entity in entities)
        {
            if (string.IsNullOrEmpty(entity.uniqueID))
            {
                entity.uniqueID = System.Guid.NewGuid().ToString();
                EditorUtility.SetDirty(entity);
            }
        }

        Debug.Log("Unique IDs assigned to all entities.");
    }
}
