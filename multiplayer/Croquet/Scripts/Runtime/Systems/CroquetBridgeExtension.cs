using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class CroquetBridgeExtension : MonoBehaviour
{
    public virtual void ProcessCommand(string command, string[] args)
    {
        throw new NotImplementedException();
    }

    public virtual void ProcessCommand(string command, byte[] data, int startIndex)
    {
        throw new NotImplementedException();
    }

    public abstract List<String> KnownCommands { get;}
    //TODO: implement
    //public abstract void OnSessionDisconnect();
}
