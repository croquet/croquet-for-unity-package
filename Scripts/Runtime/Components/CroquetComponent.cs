using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class CroquetComponent : MonoBehaviour
{
    public abstract CroquetSystem croquetSystem  { get; set; }

    void Awake()
    {
        SetCroquetSystem();
    }

    public virtual void SetCroquetSystem()
    {
        if (croquetSystem != null)
        {
            croquetSystem.RegisterComponent(this);
        }
        else
        {
            // Debug.Log($"System not available yet {this}");
        }
    }
}
