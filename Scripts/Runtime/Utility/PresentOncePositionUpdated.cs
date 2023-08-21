using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PresentOncePositionUpdated : MonoBehaviour
{
    public bool waitUntilMove = false;
    public float timeout = 0.1f; // present after this time even if not moved
    private CroquetSpatialComponent sc;
    private float startTime;

    private void Start()
    {
        sc = GetComponent<CroquetSpatialComponent>();
        startTime = Time.realtimeSinceStartup;
    }

    private void Update()
    {
        // or
        // if(CroquetSpatialSystem.Instance.hasObjectMoved(gameObject.GetInstanceID()))
        if (sc.hasBeenMoved || (!waitUntilMove && sc.hasBeenPlaced) || Time.realtimeSinceStartup - startTime >= timeout)
        {
            foreach (var renderer in GetComponentsInChildren<Renderer>())
            {
                renderer.enabled = true;
            }
            Destroy(this);
        }
    }
}
