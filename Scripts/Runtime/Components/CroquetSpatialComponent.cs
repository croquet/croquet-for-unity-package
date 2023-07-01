using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using UnityEngine;

[AddComponentMenu("Croquet/SpatialComponent")]
public class CroquetSpatialComponent : CroquetComponent
{
    public override CroquetSystem croquetSystem { get; set; } = CroquetSpatialSystem.Instance;

    [Header("Model Transform (read-only)")]
    public bool hasBeenPlaced = false;
    public bool hasBeenMoved = false;
    public Vector3 position = Vector3.zero;
    public Quaternion rotation = Quaternion.identity ; 
    public Vector3 scale = Vector3.one;
    
    [Header("Options")]
    public bool initializeWithSceneTransform = false;
    [Space(10)]
    public float positionDeltaEpsilon = 0.01f;
    public float rotationDeltaEpsilon = 0.01f;
    public float scaleDeltaEpsilon = 0.01f;
    [Space(10)]
    public bool linearInterpolation = false;
    public float positionLerpFactor = 0.2f;
    public float rotationLerpFactor = 0.2f;
    public float scaleLerpFactor = 0.2f;

    [Header("Ballistic motion")]
    public float desiredLag = 0.05f; // seconds behind 
    public float currentLag = 0f;
    public Vector3? ballisticVelocity = null;

    // public List<string> telemetry = new List<string>(); for testing

    //TODO: lerpCurve support
    
}
