// TEMPORARY DIAGNOSTIC — delete once the "player drifts while standing on the ship's deck" bug is
// understood. It writes nothing and changes nothing; it only reports, on demand, which layer is
// moving the body: the hull, the platform carrier, or the player's own physics.
//
// Press F9 while standing in the ship. It logs one line every 10 physics steps for four seconds.
using UnityEngine;
using UnityEngine.InputSystem;
using SpaceGame.Characters;

namespace SpaceGame.Vehicles
{
    public class ShipDriftProbe : MonoBehaviour
    {
        private const int LogEveryNSteps = 10;
        private const int StepsToRecord = 200;

        private WalkerPlatformCarrier carrier;
        private Rigidbody hull;
        private PlayerMovement player;
        private Rigidbody playerBody;

        private Vector3 lastHull, lastPlayer, startHull, startPlayer;
        private int step = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            var go = new GameObject("ShipDriftProbe") { hideFlags = HideFlags.HideAndDontSave };
            go.AddComponent<ShipDriftProbe>();
            DontDestroyOnLoad(go);
        }

        private void Update()
        {
            if (step >= 0 || Keyboard.current == null || !Keyboard.current.f9Key.wasPressedThisFrame) return;

            carrier = FindFirstObjectByType<WalkerPlatformCarrier>();
            player = FindFirstObjectByType<PlayerMovement>();
            if (carrier == null || player == null)
            {
                Debug.Log($"[DriftProbe] nothing to measure: carrier={carrier != null} player={player != null}");
                return;
            }

            hull = carrier.GetComponent<Rigidbody>();
            playerBody = player.GetComponent<Rigidbody>();
            startHull = lastHull = hull.position;
            startPlayer = lastPlayer = playerBody.position;
            step = 0;

            Debug.Log($"[DriftProbe] START hull='{hull.name}' kinematic={hull.isKinematic} " +
                      $"constraints={hull.constraints} gravity={hull.useGravity} " +
                      $"tilt={Vector3.Angle(hull.transform.up, Vector3.up):F2} deg | " +
                      $"player='{player.name}' kinematic={playerBody.isKinematic} " +
                      $"riders={carrier.RiderCount}");
        }

        private void FixedUpdate()
        {
            if (step < 0) return;

            step++;
            if (step % LogEveryNSteps == 0)
            {
                Vector3 hullDelta = hull.position - lastHull;
                Vector3 playerDelta = playerBody.position - lastPlayer;
                Vector3 relative = (playerBody.position - startPlayer) - (hull.position - startHull);
                lastHull = hull.position;
                lastPlayer = playerBody.position;

                string underfoot = "none";
                Collider col = player.GetComponentInChildren<CapsuleCollider>();
                if (col != null)
                {
                    Bounds b = col.bounds;
                    if (Physics.SphereCast(b.center + Vector3.up * 0.05f, Mathf.Max(0.05f, b.extents.x * 0.9f),
                                           Vector3.down, out RaycastHit g, b.extents.y + 0.2f, ~0,
                                           QueryTriggerInteraction.Ignore))
                        underfoot = $"{g.collider.name} normal={Vector3.Angle(g.normal, Vector3.up):F1}deg";
                }

                Debug.Log($"[DriftProbe] t={step * Time.fixedDeltaTime:F2} " +
                          $"hullD=({hullDelta.x * 1000f:F2},{hullDelta.y * 1000f:F2},{hullDelta.z * 1000f:F2})mm " +
                          $"playerD=({playerDelta.x * 1000f:F2},{playerDelta.y * 1000f:F2},{playerDelta.z * 1000f:F2})mm " +
                          $"REL=({relative.x:F3},{relative.y:F3},{relative.z:F3})m " +
                          $"hullVel={hull.linearVelocity} playerVel={playerBody.linearVelocity} " +
                          $"grounded={player.IsOnGround} on {underfoot} " +
                          $"riders={carrier.RiderCount} hullTilt={Vector3.Angle(hull.transform.up, Vector3.up):F2} " +
                          $"kin={hull.isKinematic} con={hull.constraints}");
            }

            if (step >= StepsToRecord)
            {
                Vector3 relative = (playerBody.position - startPlayer) - (hull.position - startHull);
                Debug.Log($"[DriftProbe] END after {StepsToRecord * Time.fixedDeltaTime:F1}s: " +
                          $"hull moved {(hull.position - startHull).ToString("F3")}, " +
                          $"player moved {(playerBody.position - startPlayer).ToString("F3")}, " +
                          $"player RELATIVE to hull {relative.ToString("F3")} " +
                          $"({new Vector2(relative.x, relative.z).magnitude:F3} m lateral)");
                step = -1;
            }
        }
    }
}
