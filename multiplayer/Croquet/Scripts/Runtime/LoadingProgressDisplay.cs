using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class LoadingProgressDisplay : MonoBehaviour
{
    public abstract void Show();
    public abstract void SetMessage(string msg);
    public abstract void SetProgress(float progress);
    public abstract void SetProgress(float progress, string msg);
    public abstract void Hide();
}
