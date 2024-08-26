using UnityEngine;

public class PlayerStats : MonoBehaviour
{
    [SynchVar]
    public int health = 100;

    void Update()
    {
        health--;
    }
}
