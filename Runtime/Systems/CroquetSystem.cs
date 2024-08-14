using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class CroquetSystem : MonoBehaviour
{
    /// <summary>
    /// Commands this system understands.
    /// </summary>
    public abstract List<String> KnownCommands { get; }

    /// <summary>
    /// Components that this system will update.
    /// </summary>    
    public static CroquetSystem Instance { get; private set; }

    private void Awake()
    {
        // Create Singleton Accessor
        // If there is an instance, and it's not me, delete myself.
        if (Instance != null && Instance != this)
        {
            Destroy(this);
        }
        else
        {
            Instance = this;
        }
    }
    protected abstract Dictionary<int, CroquetComponent> components { get; set; }
    public void countOfComponents()
    {
        Debug.Log("components count: " + components.Count);
    }
    public virtual void GetComponentFromID(int instanceID)
    {
        components.TryGetValue(instanceID, out CroquetComponent component);
        Debug.Log($"get {component.gameObject} in {this}");
    }
    public virtual void RegisterComponent(CroquetComponent component)
    {
        int instanceID = component.gameObject.GetInstanceID();

        if (components.ContainsKey(instanceID))
        {
            Debug.LogWarning($"Component with instanceID {instanceID} is already registered in {this}. Replacing the existing component.");
            UnregisterComponent(components[instanceID]);
        }

        components[instanceID] = component;
        Debug.Log($"Registered {component.gameObject.name} in {this} with instanceID {instanceID}");

        countOfComponents();
    }

    public virtual void UnregisterComponent(CroquetComponent component)
    {
        components.Remove(component.gameObject.GetInstanceID());
        Debug.Log($"Unregistered {component.gameObject.name} from {this} with instanceID {component.gameObject.GetInstanceID()}");
    }


    public bool KnowsObject(GameObject go)
    {
        return components.ContainsKey(go.GetInstanceID());
    }

    public virtual void PawnInitializationComplete(GameObject go)
    {
        // by default, nothing
    }

    public virtual void ActorPropertySet(GameObject go, string propName)
    {
        // by default, nothing
    }

    public virtual void ProcessCommand(string command, string[] args)
    {
        throw new NotImplementedException();
    }

    public virtual void ProcessCommand(string command, byte[] data, int startIndex)
    {
        throw new NotImplementedException();
    }

    public virtual void LoadedScene(string sceneName)
    {
        // by default, nothing
    }

    public virtual bool ReadyToRunScene(string sceneName)
    {
        return true;
    }

    public virtual void ClearSceneBeforeRunning()
    {
        Debug.Log("ClearSceneBeforeRunning");
        // components.Clear(); // wipe out anything that registered as the scene came up
    }

    public virtual void TearDownScene()
    {
        // by default, just clear the components
        // components.Clear();
    }

    public virtual void TearDownSession()
    {
        // by default, just invoke TearDownScene
        TearDownScene();
    }

    public virtual List<string> InitializationStringsForObject(GameObject go)
    {
        return new List<string>();
    }

}
