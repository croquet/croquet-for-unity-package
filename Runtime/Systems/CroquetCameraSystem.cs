using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CroquetCameraSystem : MonoBehaviour
{
    // TODO: Something wicked this way comes
    
    //else if (command == "grabCamera") GrabCamera(args);// OUT:CUSTOM CAM
    //else if (command == "releaseCamera") ReleaseCamera(args);// OUT:CUSTOM CAM
    
    
    // void ReleaseCamera(string[] args)
    // {
    //     string id = args[0];
    //     if (cameraOwnerId != id) return; // has already been switched
    //
    //     cameraOwnerId = "";
    //     GameObject camera = GameObject.FindWithTag("MainCamera");
    //     camera.transform.parent = null;
    // }
    
    // void GrabCamera(string[] args)
    // {
    //     string id = args[0];
    //     if (cameraOwnerId == id) return; // already registered
    //
    //     GameObject obj = FindObject(id);
    //     if (obj == null) return;
    //
    //     cameraOwnerId = id;
    //
    //     string[] rot = args[1].Split(',');
    //     string[] pos = args[2].Split(',');
    //     
    //     GameObject camera = GameObject.FindWithTag("MainCamera");
    //     camera.transform.SetParent(obj.transform, false); // false => ignore child's existing world position
    //     List<string> geomUpdate = new List<string>();
    //     geomUpdate.Add(reservedIds["camera"]);
    //     geomUpdate.Add("rotationSnap");
    //     geomUpdate.AddRange(rot);
    //     geomUpdate.Add("translationSnap"); // the keyword we'd get from Croquet
    //     geomUpdate.AddRange(pos);
    //     UpdateGeometry(geomUpdate.ToArray());
    // }
    
}
