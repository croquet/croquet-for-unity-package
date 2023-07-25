using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class CroquetSystem : MonoBehaviour
{
    /// <summary>
    /// Commands this system understands.
    /// </summary>
    public abstract List<String> KnownCommands { get;}

    /// <summary>
    /// Components that this system will update.
    /// </summary>
    protected abstract Dictionary<int, CroquetComponent> components { get; set; }

    public virtual void RegisterComponent(CroquetComponent component)
    {
        Debug.Log($"register {component.gameObject} in {this}");
        components.Add(component.gameObject.GetInstanceID(), component);
    }

    public virtual void UnregisterComponent(CroquetComponent component)
    {
        components.Remove(component.gameObject.GetInstanceID());
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

    public virtual void ClearPriorToRunningScene()
    {
        components.Clear(); // wipe out anything that registered as the scene came up
    }

    public virtual bool ReadyToRunScene()
    {
        return true;
    }

    public virtual void TearDownScene()
    {
        // by default, just clear the components
        components.Clear();
    }

    public virtual void TearDownSession()
    {
        // by default, just invoke TearDownScene
        TearDownScene();
    }

    public virtual string InitializationStringForInstanceID(int instanceID)
    {
        return "";
    }
}
