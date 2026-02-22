using UnityEngine;

public class Effect
{
    public float timer;
    public System.Action<Rigidbody> applyEffect;
    public System.Action<Rigidbody> onTick;
    public System.Action<Rigidbody> stopEffect;

    public Effect(float duration)
    {
        timer = duration;
    }
}