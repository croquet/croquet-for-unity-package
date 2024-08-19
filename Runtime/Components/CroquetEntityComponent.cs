using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[AddComponentMenu("Croquet/EntityComponent")]
public class CroquetEntityComponent : CroquetComponent
{
    public override CroquetSystem croquetSystem { get; set; } = CroquetEntitySystem.Instance;
    [SerializeField]
    public string uniqueID;

    public string croquetActorId = ""; // the actor identifier (M###)
    public int croquetHandle = -1; // unique integer ID assigned by this client's bridge
    // specify the accompanying actor class here
    // specify addressable name / pawn name

    // static and watched properties from the Croquet actor (as requested on a
    // CroquetActorManifest script) are held here, and accessible using static
    // methods on the Croquet class:
    //   ReadActorString(prop)
    //   ReadActorStringArray(prop)
    //   ReadActorFloat(prop)
    //   ReadActorFloatArray(prop)
    public StringStringSerializableDict actorProperties = new StringStringSerializableDict();

    // OnEnable is called when the script is loaded or a value is changed in the Inspector
    private void OnEnable()
    {
        // Assign type based on the GameObject's name
        type = gameObject.name;
    }

    private void Start()
    {
        // Assign type based on the GameObject's name
        type = gameObject.name;
    }

    private void Awake()
    {
        // Assign type based on the GameObject's name
        if (type == null || type == ""){
            type = gameObject.name;
        }
        FindObjectOfType<RuntimeEntityManager>().RegisterEntity(this);
    }
    public int cH; // handle used by this client's Croquet bridge to address this object
    public string cN; // Croquet name (generally, the model id)
    public bool cC; // confirmCreation: whether Croquet is waiting for a confirmCreation message for this
    public bool wTP; // waitToPresent:  whether to make visible immediately
    public string type;
    public string cs; // comma-separated list of extra components
    public string[] ps; // actor properties and their values
    public string[] ws; // actor properties to be watched
}

[Serializable]
public class StringStringSerializableDict : SerializableDictionary<string, string> { }
