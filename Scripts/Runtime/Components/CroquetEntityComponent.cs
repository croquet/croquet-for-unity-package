using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[AddComponentMenu("Croquet/EntityComponent")]
public class CroquetEntityComponent : CroquetComponent
{
    public override CroquetSystem croquetSystem { get; set; } = CroquetEntitySystem.Instance;
    
    [Header("SET")]
    
    public string actorClassName;
    /// <summary>
    /// The name of the Actor Class that controls this entity's behaviour
    /// </summary>
    
    // NOT TO SET
    [Header("READ-ONLY")]
    public string croquetActorId = ""; // the actor identifier (M###)
    public string croquetHandle = ""; // unique integer ID as a string (agreed Unique across bridge)
    // specify the accompanying actor class here
    // specify addressable name / pawn name
}
