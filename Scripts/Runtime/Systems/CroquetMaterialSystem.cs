using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CroquetMaterialSystem : CroquetSystem
{
    public override List<string> KnownCommands { get; } = new List<string>() { };

    protected override Dictionary<int, CroquetComponent> components { get; set; } = new Dictionary<int, CroquetComponent>();
    
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
    //     // Scan scene for all Material Components
    //     foreach (CroquetMaterialComponent materialComponent in FindObjectsOfType<CroquetMaterialComponent>())
    //     {
    //         // Retrieve the necessary identifier
    //         components.Add(id, materialComponent);
    //     }
    }

    public override void ActorPropertySet(GameObject go, string propName)
    {
        // we're being notified that a watched property on an object that we are
        // known to have an interest in has changed.  right now, we only care
        // about color.
        if (propName == "color")
        {
            float[] rgb = Croquet.ReadActorFloatArray(go, "color");
            // as a convention, a red value of -1 means "don't change the color"
            if (rgb[0] == -1)
            {
                return;
            }
            
            Color colorToSet = new Color(rgb[0], rgb[1], rgb[2]);
            go.GetComponentInChildren<MeshRenderer>().materials[0].color = colorToSet;
            // Debug.Log($"color set for {go} to {string.Join<float>(',', rgb)}");
        }
    }
}
