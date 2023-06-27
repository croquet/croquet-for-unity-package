using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PresentOnFirstMove : MonoBehaviour
{
    private CroquetSpatialComponent sc;
    
    private void Start()
    {
        sc = GetComponent<CroquetSpatialComponent>();
    }

    private void Update()
    {
        // or
        // if(CroquetSpatialSystem.Instance.hasObjectMoved(gameObject.GetInstanceID()))
        if (sc.hasBeenMoved)
        {
            foreach (var renderer in GetComponentsInChildren<MeshRenderer>())
            {
                renderer.enabled = true;
            }
            Destroy(this);
        }
    }
}
