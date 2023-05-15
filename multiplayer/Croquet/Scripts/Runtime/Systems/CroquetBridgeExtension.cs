using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class CroquetBridgeExtension : MonoBehaviour
{
    public abstract void ProcessCommand(string command, string[] args);

    public List<String> Messages;
    //TODO: implement
    //public abstract void OnSessionDisconnect();
}
