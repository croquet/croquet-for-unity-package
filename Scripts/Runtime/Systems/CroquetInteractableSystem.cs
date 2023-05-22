using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;


public class CroquetInteractableSystem : CroquetSystem
{
    public bool SendPointerHitEvents = true;
    public float PointerHitDistance = 50.0f;
    public Camera userCamera;

    public override List<string> KnownCommands { get; } = new()
    {
        "makeInteractable",
    };

    protected override Dictionary<int, CroquetComponent> components { get; set; } =
        new Dictionary<int, CroquetComponent>();
    
    // Create Singleton Reference
    public static CroquetInteractableSystem Instance { get; private set; }

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
        if (Input.GetMouseButtonDown(0))
        {
            if (SendPointerHitEvents)
            {
                SendPointerHit();
            }
        }
    }

    public override void ProcessCommand(string command, string[] args)
    {
        if (command == "makeInteractable") MakeInteractable(args);
    }

    void SendPointerHit()
    {
        // Debug.Log($"[INPUT] Looking for pointer hit");

        // TODO: raycast against only an interactive-only bitmask.
        List<string> clickDetails = new List<string>();
        Ray ray = ((userCamera ? userCamera : Camera.main)!).ScreenPointToRay(Pointer.current.position.ReadValue());
        RaycastHit[] hits = Physics.RaycastAll(ray, PointerHitDistance);
        Array.Sort(hits, (x,y) => x.distance.CompareTo(y.distance));
        foreach (RaycastHit hit in hits)
        {
            // for each Unity hit, only register a click if the hit object has
            // a CroquetGameObject component and has been registered as interactable.
            // create a list with each clicked object handle, click location,
            // and click layers that the object has been registered with (if any).
            Transform objectHit = hit.transform;
            while (true)
            {
                CroquetInteractableComponent interactable = objectHit.gameObject.GetComponent<CroquetInteractableComponent>();
                CroquetEntityComponent entity = objectHit.GetComponent<CroquetEntityComponent>();
                if (interactable)
                {
                    if (interactable.interactable)
                    {
                        // collect id, hit.x, hit.y, hit.z[, layer1, layer2 etc]
                        List<string> oneHit = new List<string>();
                        oneHit.Add(entity.croquetHandle);
                        Vector3 xyz = hit.point;
                        oneHit.Add(xyz.x.ToString());
                        oneHit.Add(xyz.y.ToString());
                        oneHit.Add(xyz.z.ToString());
                        oneHit.AddRange(interactable.interactableLayers);

                        clickDetails.Add(String.Join(',', oneHit.ToArray()));
                    }

                    break;
                }

                objectHit = objectHit.parent;
                
                if (!objectHit) break;
            }
        }

        if (clickDetails.Count > 0)
        {
            List<string> eventArgs = new List<string>();
            eventArgs.Add("event");
            eventArgs.Add("pointerHit");
            eventArgs.AddRange(clickDetails);
            CroquetBridge.SendCroquet(eventArgs.ToArray());
        }
    }
    
    private void MakeInteractable(string[] args)
    {
        string croquetHandle = args[0];
        string layers = args[1];

        GameObject go = CroquetEntitySystem.Instance.GetGameObjectByCroquetHandle(croquetHandle);
        if (go != null)
        {
            CroquetInteractableComponent component = components[go.GetInstanceID()] as CroquetInteractableComponent;
            component.interactable = true;
            
            if (layers != "")
            {
                component.interactableLayers = layers.Split(",");
            }
        }

    }

}
