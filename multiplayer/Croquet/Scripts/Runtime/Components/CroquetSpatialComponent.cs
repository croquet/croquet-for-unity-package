using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using UnityEngine;

[AddComponentMenu("Croquet/SpatialComponent")]
public class CroquetSpatialComponent : MonoBehaviour
{
    [Header("Model Transform (read-only)")]
    public Vector3 position;
    public Vector3 rotation;
    public Vector3 scale;
    
    [Header("Options")]
    public bool initializeWithSceneTransform = false;

    public bool linearInterpolation = false;
    public float lerpFactor = 0.2f;
}
