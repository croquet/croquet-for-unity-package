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

    public virtual void TearDownSession()
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

    //TODO: implement
    //public abstract void OnSessionDisconnect();



}
