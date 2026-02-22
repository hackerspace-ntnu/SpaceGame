using System;
using System.Collections.Generic;
using UnityEngine;

public class EffectManager : MonoBehaviour
{
    private List<Effect> activeEffects = new List<Effect>();
    private Rigidbody playerRigidbody;

    private void Awake()
    {
        playerRigidbody = GetComponent<Rigidbody>();
    }

    public void AddEffect(Effect effect)
    {
        activeEffects.Add(effect);
        effect.applyEffect?.Invoke(playerRigidbody);
    }

    public void RemoveEffect(Effect effect)
    {
        if (activeEffects.Remove(effect))
        {
            effect.stopEffect?.Invoke(playerRigidbody);
        }
    }

    private void Update()
    {
        for (int i = activeEffects.Count - 1; i >= 0; i--)
        {
            Effect effect = activeEffects[i];
            effect.timer -= Time.deltaTime;

            // Apply effect every frame while active
            effect.onTick?.Invoke(playerRigidbody);

            // Remove effect when timer expires
            if (effect.timer <= 0)
            {
                RemoveEffect(effect);
            }
        }
    }
}