using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class CroquetComponent : MonoBehaviour
{
    public abstract CroquetSystem croquetSystem  { get; set; }

    void Awake()
    {
        croquetSystem.RegisterComponent(this);
    }

}
