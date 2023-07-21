using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CroquetActorManifest : MonoBehaviour
{
    public string defaultActorClass = ""; // only used on pre-load objects
    public string[] mixins;
    public string[] staticProperties;
    public string[] watchedProperties;
}
