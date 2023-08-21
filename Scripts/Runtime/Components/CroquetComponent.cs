using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class CroquetComponent : MonoBehaviour
{
    public abstract CroquetSystem croquetSystem  { get; set; }

    void Awake()
    {
        // if (croquetSystem == null) Debug.Log($"futile attempt to awaken {this}");
        if (croquetSystem != null) croquetSystem.RegisterComponent(this);
    }

}
