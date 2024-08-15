using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class RuntimeEntityManager : MonoBehaviour
{
    public static RuntimeEntityManager Instance { get; private set; }

    // Global pool to track all entities by type
    private Dictionary<string, List<CroquetEntityComponent>> globalEntityPool = new Dictionary<string, List<CroquetEntityComponent>>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
        }
        else
        {
            Instance = this;
        }
    }

    public void RegisterEntity(CroquetEntityComponent entity)
    {
        if (!globalEntityPool.ContainsKey(entity.type))
        {
            globalEntityPool[entity.type] = new List<CroquetEntityComponent>();
        }
        globalEntityPool[entity.type].Add(entity);
        Debug.Log($"Registered entity of type {entity.type}");
    }

    public void UnregisterEntity(CroquetEntityComponent entity)
    {
        if (globalEntityPool.ContainsKey(entity.type))
        {
            globalEntityPool[entity.type].Remove(entity);
            if (globalEntityPool[entity.type].Count == 0)
            {
                globalEntityPool.Remove(entity.type);
            }
        }
        Debug.Log($"Unregistered entity of type {entity.type}");
    }

public CroquetEntityComponent GetUnusedEntityOfType(string type)
{ 
    Debug.Log($"Attempting to get unused entity of type {type}");
    if (globalEntityPool.ContainsKey(type))
    {
        // Find the first entity that matches the specified type and hasn't been assigned yet (cH == -1)
        return globalEntityPool[type].FirstOrDefault(entity => entity.type == type && entity.cH == -1);
    }
    return null;
}

}
