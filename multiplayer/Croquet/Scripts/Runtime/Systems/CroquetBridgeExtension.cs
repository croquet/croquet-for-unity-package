using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class CroquetBridgeExtension : MonoBehaviour
{
    public abstract bool ProcessCommand(string command, string[] args);
}
