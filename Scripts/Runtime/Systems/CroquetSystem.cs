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
    protected abstract Dictionary<string, CroquetComponent> components { get; set; }
    
    public virtual void RegisterComponent(CroquetComponent component)
    {
        // Retrieve the necessary identifier
        string gameHandle = component.gameObject.GetComponent<CroquetEntityComponent>().croquetGameHandle;
        
        if (component.GetType() != typeof(CroquetEntityComponent))
        {
            components.Add(gameHandle, component);
        }
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
