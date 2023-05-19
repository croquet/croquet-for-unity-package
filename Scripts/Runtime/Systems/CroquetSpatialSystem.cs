using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CroquetSpatialSystem : CroquetSystem
{
    public override List<String> KnownCommands { get;  } = new()
    {
        "updateSpatial",
        "setParent",
        "unparent",
    };

    protected override Dictionary<int, CroquetComponent> components { get; set; } = new Dictionary<int, CroquetComponent>();

    // Create Singleton Reference
    public static CroquetSpatialSystem Instance { get; private set; }

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

    private void Update()
    {
        // Update the transform (position, rotation, scale) in the scene
        UpdateTransforms();
    }

    void Unparent(string[] args)
    {
        GameObject child = CroquetEntitySystem.Instance.GetGameObjectByCroquetHandle(args[0]);
        if (child)
        {
            child.transform.SetParent(null);
        }
    }
    
    void SetParent(string[] args)
    {
        GameObject child = CroquetEntitySystem.Instance.GetGameObjectByCroquetHandle(args[0]);
        GameObject parent = CroquetEntitySystem.Instance.GetGameObjectByCroquetHandle(args[1]);
        if (parent && child)
        {
            child.transform.SetParent(parent.transform, false); // false => ignore child's existing world position
        }
    }
    
    /// <summary>
    /// Update the Unity Transform Components in the Scene to reflect the latest desired state.
    /// </summary>
    void UpdateTransforms()
    {
        foreach (KeyValuePair<int, CroquetComponent> kvp in components)
        {
            CroquetSpatialComponent spatialComponent = kvp.Value as CroquetSpatialComponent;
            
            if (Vector3.Distance(spatialComponent.scale,spatialComponent.transform.localScale) > spatialComponent.scaleDeltaEpsilon)
            {
                spatialComponent.transform.localScale = Vector3.Lerp(spatialComponent.transform.localScale, spatialComponent.scale, spatialComponent.scaleLerpFactor);
            }
            if (Quaternion.Angle(spatialComponent.rotation,spatialComponent.transform.localRotation) > spatialComponent.rotationDeltaEpsilon)
            {
                spatialComponent.transform.localRotation = Quaternion.Slerp(spatialComponent.transform.localRotation, spatialComponent.rotation, spatialComponent.rotationLerpFactor);
            }
            if (Vector3.Distance(spatialComponent.position,spatialComponent.transform.localPosition) > spatialComponent.positionDeltaEpsilon)
            {
                spatialComponent.transform.localPosition = Vector3.Lerp(spatialComponent.transform.localPosition, spatialComponent.position, spatialComponent.positionLerpFactor);
            }
        }
    }
    
    /// <summary>
    /// Processing messages from Croquet to update the spatial component
    /// </summary>
    /// <param name="rawData">TODO:Aran</param>
    /// <param name="startPos">TODO:Aran</param>
    /// <returns></returns>
    void UpdateSpatial(byte[] rawData, int startPos)
    {
        const uint SCALE = 32;
        const uint SCALE_SNAP = 16;
        const uint ROT = 8;
        const uint ROT_SNAP = 4;
        const uint POS = 2;
        const uint POS_SNAP = 1;
        
        int bufferPos = startPos; // byte index through the buffer
        while (bufferPos < rawData.Length)
        {
            // first number encodes object id and (in bits 0-5) whether there is an update (with/without
            // a snap) for each of scale, rotation, translation.  this leaves room for 2**26
            // possible ids - i.e., around 67 million.  that seems more than enough for any given
            // instant, but if some app creates and destroys thousands of entities per second, we
            // would need some kind of id recycling so we don't run out.
            UInt32 encodedId = BitConverter.ToUInt32(rawData, bufferPos);
            bufferPos += 4;
            string croquetHandle = (encodedId >> 6).ToString();

            int instanceID = CroquetEntitySystem.GetInstanceIDByCroquetHandle(croquetHandle);

            CroquetSpatialComponent spatialComponent;
            Transform trans;
            try
            {
                spatialComponent = components[instanceID] as CroquetSpatialComponent;
                trans = spatialComponent.transform;
            }
            catch (Exception e)
            {
                // object not found.  skip through the buffer to the next object's record.
                if ((encodedId & SCALE) != 0)
                {
                    bufferPos += 12;
                }
                if ((encodedId & ROT) != 0)
                {
                    bufferPos += 16;
                }
                if ((encodedId & POS) != 0)
                {
                    bufferPos += 12;
                }
                Debug.Log($"attempt to update absent object {croquetHandle} : {e}");
                continue;
            }

            if ((encodedId & SCALE) != 0)
            {
                Vector3 updatedScale = Vector3FromBuffer(rawData, bufferPos);
                bufferPos += 12;
                if ((encodedId & SCALE_SNAP) != 0)
                {
                    // immediately snap scale
                    trans.localScale = updatedScale;
                }
                
                // update the components data regardless
                spatialComponent.scale = updatedScale;
                // Log("verbose", "scale: " + updatedScale.ToString());
            }
            if ((encodedId & ROT) != 0)
            {
                Quaternion updatedQuatRot = QuaternionFromBuffer(rawData, bufferPos);
                bufferPos += 16;
                if ((encodedId & ROT_SNAP) != 0)
                {
                    trans.localRotation = updatedQuatRot;
                }
                // update the components data regardless
                spatialComponent.rotation = updatedQuatRot;
                // Log("verbose", "rot: " + updatedQuatRot.ToString());
            }
            if ((encodedId & POS) != 0)
            {
                Vector3 updatedPosition = Vector3FromBuffer(rawData, bufferPos);
                bufferPos += 12;
                if ((encodedId & POS_SNAP) != 0)
                {
                    trans.localPosition = updatedPosition;
                }
                // update the component's data regardless
                spatialComponent.position = updatedPosition;
                // Log("verbose", "pos: " + updatedPosition.ToString());
            }
        }
    }

    public void SnapObjectTo(string croquetHandle, Vector3? position = null, Quaternion? rotation = null, Vector3? scale = null)
    {
        int instanceID = CroquetEntitySystem.GetInstanceIDByCroquetHandle(croquetHandle);
        if (instanceID == 0) return;
        
        CroquetSpatialComponent spatialComponent = components[instanceID] as CroquetSpatialComponent;
        Transform trans = spatialComponent.transform;

        if (position != null)
        {
            trans.localPosition = position.Value;
            spatialComponent.position = position.Value;
        }

        if (rotation != null)
        {
            trans.localRotation = rotation.Value;
            spatialComponent.rotation = rotation.Value;
        }

        if (scale != null)
        {
            trans.localScale = scale.Value;
            spatialComponent.scale = scale.Value;
        }
    }

    public void SnapObjectInCroquet(string croquetHandle, Vector3? position = null, Quaternion? rotation = null,
        Vector3? scale = null)
    {
        List<string> argList = new List<string>();
        argList.Add("objectMoved");
        argList.Add(croquetHandle);
        
        if (position != null)
        {
            argList.Add("p");
            argList.Add(string.Join<float>(",", new[] { position.Value.x, position.Value.y, position.Value.z }));
        }

        if (rotation != null)
        {
            argList.Add("r");
            argList.Add(string.Join<float>(",", new[] { rotation.Value.x, rotation.Value.y, rotation.Value.z, rotation.Value.w }));
        }

        if (scale != null)
        {
            argList.Add("p");
            argList.Add(string.Join<float>(",", new[] { scale.Value.x, scale.Value.y, scale.Value.z }));
        }

        CroquetBridge.SendCroquet(argList.ToArray());
    }

    public override void ProcessCommand(string command, string[] args)
    {
        if (command.Equals("setParent"))
        {
            // associate parent
            SetParent(args);
        }
        else if (command.Equals("unparent"))
        {
            // unassociate parent
            Unparent(args);
        }
    }

    public override void ProcessCommand(string command, byte[] data, int startIndex)
    {
        if (command.Equals("updateSpatial"))
        {
            UpdateSpatial(data, startIndex);// TODO ARAN: together fix the data format coming in
        } 
    }
    
    Quaternion QuaternionFromBuffer(byte[] rawData, int startPos)
    {
        return new Quaternion(
            BitConverter.ToSingle(rawData, startPos),
            BitConverter.ToSingle(rawData, startPos + 4),
            BitConverter.ToSingle(rawData, startPos + 8),
            BitConverter.ToSingle(rawData, startPos + 12)
        );
    }

    Vector3 Vector3FromBuffer(byte[] rawData, int startPos)
    {
        return new Vector3(
            BitConverter.ToSingle(rawData, startPos),
            BitConverter.ToSingle(rawData, startPos + 4),
            BitConverter.ToSingle(rawData, startPos + 8)
        );
    }
}

