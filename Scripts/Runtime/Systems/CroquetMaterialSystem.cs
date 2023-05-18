using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CroquetMaterialSystem : CroquetSystem
{
    public override List<string> KnownCommands { get; } = new List<string>()
    {
        "setColor"
    };

    protected override Dictionary<string, CroquetComponent> components { get; set; } = new Dictionary<string, CroquetComponent>();
    
    public static CroquetMaterialSystem Instance { get; private set; }

    private void Awake()
    {
        // Create Singleton Accessor
        // If there is an instance, and it's not me, delete myself.
        if (Instance != null && Instance != this) 
        {
            Destroy(this);
        }
        else 
        { 
            Instance = this;
        } 
    }
        
    public void Start()
    {
        // Scan scene for all Spatial Components
        foreach (CroquetMaterialComponent materialComponent in FindObjectsOfType<CroquetMaterialComponent>())
        {
            // Retrieve the necessary identifier
            var id = materialComponent.gameObject.GetComponent<CroquetEntityComponent>().croquetActorId;
            components.Add(id, materialComponent);
        }
    }

    public override void ProcessCommand(string command, string[] args)
    {
        if (command == "setColor") SetColor(args);
    }

    private void SetColor(string[] args)
    {
        string id = args[0];
        string[] rgb = args[1].Split(",");
        Color colorToSet = new Color(float.Parse(rgb[0]), float.Parse(rgb[1]), float.Parse(rgb[2]));
        components[id].gameObject.GetComponentInChildren<MeshRenderer>().materials[0].color = colorToSet;
    }
}
