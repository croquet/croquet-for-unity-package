using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CroquetSpatialSystem : CroquetBridgeExtension
{
    
    public List<String> Messages = new List<string>()
    {
        "updateSpatial",
        "setParent",
        "unparent",
    };
    
    // Instead of doing this
    // Dictionary<string, Vector3> desiredScale = new Dictionary<string, Vector3>();
    // Dictionary<string, Quaternion> desiredRot = new Dictionary<string, Quaternion>();
    // Dictionary<string, Vector3> desiredPos = new Dictionary<string, Vector3>();
    // We do this
    public Dictionary<string, CroquetSpatialComponent> SpatialComponents = new Dictionary<String, CroquetSpatialComponent>(); 
    // mapping ID to a specific CROQUETSPATIALCOMPONENT


    public void Start()
    {
        // Scan scene for all Spatial Components
        foreach (CroquetSpatialComponent spatialComponent in FindObjectsOfType<CroquetSpatialComponent>())
        {
            // Retrieve the necessary identifier
            var id = spatialComponent.gameObject.GetComponent<CroquetEntity>().croquetActorId;
            SpatialComponents.Add(id, spatialComponent);
        }
    }
    
    private void Update()
    {
        // session is notionally up and running
        UpdateSpatial();
    }

    void Unparent(string[] args)
    {
        GameObject child = CroquetEntitySystem.Instance.FindObject(args[0]);
        if (child)
        {
            child.transform.SetParent(null);
        }
    }
    
    void SetParent(string[] args)
    {
        GameObject child = CroquetEntitySystem.Instance.FindObject(args[0]);
        GameObject parent = CroquetEntitySystem.Instance.FindObject(args[1]);
        if (parent && child)
        {
            child.transform.SetParent(parent.transform, false); // false => ignore child's existing world position
        }
    }
    
    // Frame by frame tell stuff to move around
    void Update()
    {
        // THIS INSTEAD OF CONTAINSKEY()
        string id = strings[0];
        foreach (var spatialComponent in SpatialComponents)
        {
            Transform trans = SpatialComponents[id].transform;
            
        }
        // timing note: running in MacOS editor, when 450 objects have updates their total
        // processing time is around 2ms.
        foreach (KeyValuePair<string, GameObject> kvp in croquetObjects)
        {
            string id = kvp.Key;
            GameObject obj = kvp.Value;
            if (obj == null) continue;

            float lerpFactor = 0.2f;
            bool anyChange = false;
            if (desiredScale.ContainsKey(id))
            {
                obj.transform.localScale = Vector3.Lerp(obj.transform.localScale, desiredScale[id], lerpFactor);
                anyChange = true;
                if (Vector3.Distance(obj.transform.localScale, desiredScale[id]) < 0.01) desiredScale.Remove(id);
            }
            if (desiredRot.ContainsKey(id))
            {
                obj.transform.localRotation = Quaternion.Lerp(obj.transform.localRotation, desiredRot[id], lerpFactor);
                anyChange = true;
                if (Quaternion.Angle(obj.transform.localRotation, desiredRot[id]) < 0.1) desiredRot.Remove(id);
            }
            if (desiredPos.ContainsKey(id))
            {
                obj.transform.localPosition = Vector3.Lerp(obj.transform.localPosition, desiredPos[id], lerpFactor);
                anyChange = true;
                if (Vector3.Distance(obj.transform.localPosition, desiredPos[id]) < 0.01) desiredPos.Remove(id);
            }
            if (int.Parse(id) >= 100) // not one of the reserved objects (e.g., camera)
            {
                Renderer renderer = obj.GetComponentInChildren<Renderer>();
                Material material;
                if (renderer != null)
                {
                    material = renderer.material;
                }
                else // early return if bad material
                {
                    return;
                }

                if (anyChange)
                {
                    obj.SetActive(true);
                    
                }
            }
        }
    }
    
    // processing of message from Croquet updating the position component
    int UpdateSpatial(byte[] rawData, int startPos)
    {

        const uint SCALE = 32;
        const uint SCALE_SNAP = 16;
        const uint ROT = 8;
        const uint ROT_SNAP = 4;
        const uint POS = 2;
        const uint POS_SNAP = 1;
        
        int objectCount = 0;
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
            string id = (encodedId >> 6).ToString();
            if (croquetObjects.ContainsKey(id))
            {
                objectCount++;
                
                Transform trans = croquetObjects[id].transform;
                if ((encodedId & SCALE) != 0)
                {
                    Vector3 s = Vector3FromBuffer(rawData, bufferPos);
                    bufferPos += 12;
                    if ((encodedId & SCALE_SNAP) != 0)
                    {
                        trans.localScale = s;
                        desiredScale.Remove(id);
                    }
                    else
                    {
                        desiredScale[id] = s;
                    }
                    // Log("verbose", "scale: " + s.ToString());
                }
                if ((encodedId & ROT) != 0)
                {
                    Quaternion r = QuaternionFromBuffer(rawData, bufferPos);
                    bufferPos += 16;
                    if ((encodedId & ROT_SNAP) != 0)
                    {
                        trans.localRotation = r;
                        desiredRot.Remove(id);
                    }
                    else
                    {
                        desiredRot[id] = r;
                    }
                    // Log("verbose", "rot: " + r.ToString());
                }
                if ((encodedId & POS) != 0)
                {
                    // in Unity it's referred to as position
                    Vector3 p = Vector3FromBuffer(rawData, bufferPos);
                    // if (do_log) Debug.Log($"camera to {p} with snap: {(encodedId & POS_SNAP) != 0}");
                    bufferPos += 12;
                    if ((encodedId & POS_SNAP) != 0)
                    {
                        trans.localPosition = p;
                        desiredPos.Remove(id);
                    }
                    else
                    {
                        desiredPos[id] = p;
                    }
                    // Log("verbose", "pos: " + p.ToString());
                }
            }
            else Log("debug", $"attempt to update absent object {id}");
        }

        return objectCount;
    }

    
    
    public override void ProcessCommand(string command, string[] args)
    {
        if 
        UpdateGeometry(args);
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