using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using UnityEngine;

[AddComponentMenu("Croquet/SpatialComponent")]
public class CroquetSpatialComponent : MonoBehaviour
{
    [Header("Model Transform (read-only)")]
    public Vector3 position;
    public Quaternion rotation; // TODO:ARAN: TBD: V3 or Q?
    public Vector3 scale;
    
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
    
    //TODO: lerpCurve support
    
}
