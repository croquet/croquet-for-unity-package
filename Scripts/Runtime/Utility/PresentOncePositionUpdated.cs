using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PresentOncePositionUpdated : MonoBehaviour
{
    public bool waitUntilMove = false;
    private CroquetSpatialComponent sc;
    
    private void Start()
    {
        sc = GetComponent<CroquetSpatialComponent>();
    }

    private void Update()
    {
        // or
        // if(CroquetSpatialSystem.Instance.hasObjectMoved(gameObject.GetInstanceID()))
        if (sc.hasBeenMoved || (!waitUntilMove && sc.hasBeenPlaced))
        {
            foreach (var renderer in GetComponentsInChildren<MeshRenderer>())
            {
                renderer.enabled = true;
            }
            Destroy(this);
        }
    }
}
