using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using JetBrains.Annotations;
using UnityEngine;

public class CroquetAvatarSystem : CroquetSystem
{
    public override List<string> KnownCommands { get; } = new() { };

    protected override Dictionary<int, CroquetComponent> components { get; set; } =
        new Dictionary<int, CroquetComponent>();

    // Create Singleton Reference
    public static CroquetAvatarSystem Instance { get; private set; }

    private CroquetAvatarComponent lastKnownActiveAvatar;

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

    void CheckForActiveAvatar()
    {
        // find the avatar component, if any, whose gameObject's actor's "driver" value
        // is equal to our local croquetViewId.
        // this needs to be efficient, so it can be called from update loops if wanted.
        string croquetViewId = CroquetBridge.Instance.croquetViewId;
        if (croquetViewId != "")
        {
            if (lastKnownActiveAvatar != null)
            {
                // we think we know, but check just in case the driver has changed
                string driver = Croquet.ReadActorString(lastKnownActiveAvatar.gameObject, "driver");
                if (driver != croquetViewId)
                {
                    Debug.Log("avatar lost its active status");
                    lastKnownActiveAvatar.isActiveAvatar = false;
                    lastKnownActiveAvatar = null;
                }
            }

            if (lastKnownActiveAvatar == null)
            {
                // TODO: (Critical) We probably need to switch the base class to use generics
                foreach (var kvp in components)
                {
                    CroquetAvatarComponent c = kvp.Value as CroquetAvatarComponent;
                    if (c != null)
                    {
                        if (Croquet.ReadActorString(c.gameObject, "driver") == croquetViewId)
                        {
                            // Debug.Log("found active avatar");
                            c.isActiveAvatar = true;
                            lastKnownActiveAvatar = c;
                            return;
                        }
                    }
                }
            }
        }
    }

    [CanBeNull]
    public CroquetAvatarComponent GetActiveAvatarComponent()
    {
        CheckForActiveAvatar();
        return lastKnownActiveAvatar;
    }
}
