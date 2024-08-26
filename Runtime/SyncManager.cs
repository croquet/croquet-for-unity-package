using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

public class SyncManager : MonoBehaviour
{
    private Dictionary<string, (FieldInfo, MonoBehaviour)> syncFields;

    void Start()
    {
        syncFields = new Dictionary<string, (FieldInfo, MonoBehaviour)>();

        foreach (MonoBehaviour mb in FindObjectsOfType<MonoBehaviour>())
        {
            var fields = mb.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            foreach (var field in fields)
            {
                if (Attribute.IsDefined(field, typeof(SynchVarAttribute)))
                {
                    syncFields.Add(field.Name, (field, mb));
                }
            }
        }

        foreach (var field in syncFields)
        {
            Debug.Log($"Found sync field {field.Key}, value is {field.Value.Item1.GetValue(field.Value.Item2)}");
        }
    }

    void Update()
    {
        foreach (var field in syncFields)
        {
            object currentValue = field.Value.Item1.GetValue(field.Value.Item2);
            Debug.Log($"Syncing {field.Key} with value {currentValue}");
        }
    }
}
