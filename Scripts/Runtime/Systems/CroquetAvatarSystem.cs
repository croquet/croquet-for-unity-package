using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CroquetAvatarSystem : CroquetSystem
{
    public override List<string> KnownCommands { get; } = new()
    {
        "registerAsAvatar",
        "unregisterAsAvatar",
    };

    protected override Dictionary<int, CroquetComponent> components { get; set; } =
        new Dictionary<int, CroquetComponent>();
    
    // Create Singleton Reference
    public static CroquetAvatarSystem Instance { get; private set; }

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
    
    public override void ProcessCommand(string command, string[] args)
    {
        if (command.Equals("registerAsAvatar"))
        {
            // enable control through local interaction
            RegisterAsAvatar(args[0]);
        }
        else if (command.Equals("unregisterAsAvatar"))
        {
            // disable control through local interaction
            UnregisterAsAvatar(args[0]);
        }
    }

    void RegisterAsAvatar(string croquetHandle)
    {
        int instanceID = CroquetEntitySystem.Instance.GetGameObjectByCroquetHandle(croquetHandle).GetInstanceID();
        if (components.ContainsKey(instanceID))
        {
            CroquetAvatarComponent component = components[instanceID] as CroquetAvatarComponent;
            component.isActiveAvatar = true;
        }
    }

    void UnregisterAsAvatar(string croquetHandle)
    {
        int instanceID = CroquetEntitySystem.Instance.GetGameObjectByCroquetHandle(croquetHandle).GetInstanceID();
        if (components.ContainsKey(instanceID))
        {
            CroquetAvatarComponent component = components[instanceID] as CroquetAvatarComponent;
            component.isActiveAvatar = false;
        }
    }

}
