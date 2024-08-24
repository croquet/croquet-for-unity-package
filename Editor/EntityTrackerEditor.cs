using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class EntityTrackerEditor
{
    static bool runtime = false;
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
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                runtime = true;
            }
        }
        else
        {
            if (state == PlayModeStateChange.EnteredEditMode)
            {
                runtime = false;
            }
        }
    }

    private static void OnHierarchyChanged()
    {
        if (!runtime)
        {
            AssignUniqueIDsToEntities();
        }
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
