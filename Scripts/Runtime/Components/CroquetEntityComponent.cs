using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[AddComponentMenu("Croquet/EntityComponent")]
public class CroquetEntityComponent : CroquetComponent
{
    public override CroquetSystem croquetSystem { get; set; } = CroquetEntitySystem.Instance;
    
    public string croquetActorId = ""; // the actor identifier (M###)
    public string croquetHandle = ""; // unique integer ID as a string (agreed Unique across bridge)
    // specify the accompanying actor class here
    // specify addressable name / pawn name

    // static and watched properties from the Croquet actor (as requested on a
    // CroquetActorManifest script) are held here, and accessible using static
    // methods on the Croquet class:
    //   ReadActorString(prop)
    //   ReadActorStringArray(prop)
    //   ReadActorFloat(prop)
    //   ReadActorFloatArray(prop)
    public Dictionary<string, string> actorProperties = new Dictionary<string, string>();
}
