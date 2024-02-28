using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class SessionNameChooser : MonoBehaviour
{
    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
    }
}
