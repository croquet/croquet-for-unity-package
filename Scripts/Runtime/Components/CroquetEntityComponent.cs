using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[AddComponentMenu("Croquet/EntityComponent")]
public class CroquetEntityComponent : CroquetComponent
{
    public override CroquetSystem croquetSystem { get; set; } = CroquetEntitySystem.Instance;
    
    public string croquetActorId = ""; // the actor identifier (M###)
    public string croquetGameHandle = ""; // unique integer ID as a string (agreed Unique across bridge)
    // specify the accompanying actor class here
    // specify addressable name / pawn name
}
