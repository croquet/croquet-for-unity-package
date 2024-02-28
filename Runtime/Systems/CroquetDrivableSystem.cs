using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using JetBrains.Annotations;
using UnityEngine;

public class CroquetDrivableSystem : CroquetSystem
{
    public override List<string> KnownCommands { get; } = new() { };

    protected override Dictionary<int, CroquetComponent> components { get; set; } =
        new Dictionary<int, CroquetComponent>();

    // Create Singleton Reference
    public static CroquetDrivableSystem Instance { get; private set; }

    private CroquetDrivableComponent lastKnownActiveDrivable;

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

    public override void PawnInitializationComplete(GameObject go)
    {
        if (Croquet.HasActorSentProperty(go, "driver"))
        {
            SetDrivenFlag(go);
        }
    }

    public override void ActorPropertySet(GameObject go, string propName)
    {
        // we're being notified that a watched property on an object that we are
        // known to have an interest in has changed (or been set for the first time).
        if (propName == "driver")
        {
            SetDrivenFlag(go);
        }
    }

    private void SetDrivenFlag(GameObject go)
    {
        CroquetDrivableComponent drivable = go.GetComponent<CroquetDrivableComponent>();
        if (drivable != null)
        {
            string driver = Croquet.ReadActorString(go, "driver");
            drivable.isDrivenByThisView = driver == CroquetBridge.Instance.croquetViewId;
        }
    }

    void CheckForActiveDrivable()
    {
        // for the case where only a single gameObject is expected to be drivable, provide a lookup
        // that can be used from any script to find the drivable component - if any - that is set
        // to be driven by the local view.
        // this needs to be efficient, so it can be called from update loops if wanted.
        string croquetViewId = CroquetBridge.Instance.croquetViewId;
        if (croquetViewId != "")
        {
            if (lastKnownActiveDrivable != null)
            {
                // we think we know, but check just in case the driver has changed
                if (!lastKnownActiveDrivable.isDrivenByThisView)
                {
                    Debug.Log("drivable lost its active status");
                    lastKnownActiveDrivable = null;
                }
            }

            if (lastKnownActiveDrivable == null)
            {
                // TODO: for efficiency, we probably need to switch the base class to use generics
                foreach (var kvp in components)
                {
                    CroquetDrivableComponent c = kvp.Value as CroquetDrivableComponent;
                    if (c != null && c.isDrivenByThisView)
                    {
                        // Debug.Log("found active drivable");
                        lastKnownActiveDrivable = c;
                        return;
                    }
                }
            }
        }
    }

    [CanBeNull]
    public CroquetDrivableComponent GetActiveDrivableComponent()
    {
        CheckForActiveDrivable();
        return lastKnownActiveDrivable;
    }
}
