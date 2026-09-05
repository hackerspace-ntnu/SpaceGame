// PlayerShip: the second drivable ship (PlayerShipBuilder). The project-wide sweeps already
// prove it is wired and registered; these tests cover what a sweep cannot know — that the door
// state a player leaves behind actually comes back, and that the builder's output keeps the
// shape the design promised (13 moving parts, two switchable assemblies, one boarding point) —
// and that the interior it promised is still a place you can stand.
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Core.Persistence;
using SpaceGame.Gameplay;
using SpaceGame.Items;
using SpaceGame.Presentation;
using SpaceGame.Vehicles;
using SpaceGame.World.Weather;

namespace SpaceGame.EditorTools
{
    public class PlayerShipTests
    {
        private const string PrefabPath = "Assets/Game/Prefabs/agents/Vehicles/Spacecraft/PlayerShip.prefab";

        // The player's capsule. Walkability means room for THIS, not for a point.
        //
        // MEASURED off the prefab, not typed here, and it is typing them here that went wrong: they
        // read 0.5 x 2 m, and the body that walks this deck is 0.5 x 3 m. The CapsuleCollider is
        // authored 2 m high on a child scaled 1.5 in Y, and no inspector anywhere shows the product
        // — so every probe below modelled a body a metre shorter than the real one, and anything
        // intruding between 2 m and 3 m over the floor (a rib, the gear wall's header, a low
        // deckhead) walked past all of them. A number copied out of a prefab is a number that can
        // only be right on the day it was copied; see PlayerShip.md's Gotchas.
        private static float? bodyRadius;
        private static float? bodyHeight;

        private static float BodyRadius { get { MeasureBody(); return bodyRadius.Value; } }
        private static float BodyHeight { get { MeasureBody(); return bodyHeight.Value; } }

        private const float StandingSkin = 0.02f;

        /// <summary>
        /// Metres either side of the deck's centre line that the cabin owes a player to walk in.
        ///
        /// <para>
        /// One metre, which is a body's whole width either side of the middle: enough to step round
        /// something, and comfortably inside the run between the gear wall's face on the +X side
        /// (2.49) and the projector and repair station on the -X side (-1.8), so it asks nothing of
        /// the outboard deck the hull's baked fill deliberately owns. It is a promise about the
        /// ROOM, not a measurement of it — widen it and the sweep starts asking the hull for floor
        /// nobody decided to give it.
        /// </para>
        /// </summary>
        private const float AisleHalfWidth = 1f;

        /// <summary>
        /// The player's capsule as it stands in the WORLD: the authored collider times the scale of
        /// the transform it hangs on, which is where the missing metre was hiding. Cached the way
        /// <see cref="InteractReach"/> caches its own read off the same prefab.
        /// </summary>
        private static void MeasureBody()
        {
            if (bodyHeight != null) return;

            GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            Assert.IsNotNull(player, $"No player prefab at {PlayerPrefabPath}.");

            var capsule = player.GetComponentInChildren<CapsuleCollider>(true);
            Assert.IsNotNull(capsule, "The player prefab has no CapsuleCollider for these probes to model.");
            Assert.AreEqual(1, capsule.direction,
                            "The player's capsule is no longer upright (Y), so the height and radius " +
                            "below are being read off the wrong axes.");

            Vector3 scale = capsule.transform.lossyScale;
            bodyHeight = capsule.height * Mathf.Abs(scale.y);
            bodyRadius = capsule.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        }

        // The interior is full of trigger volumes that are not walls — the four chairs' click
        // surfaces reach out over the aisle on purpose. Unity's queries count triggers by default,
        // and a probe that does would read a seat as a pillar. SpawnClearance.HasRoomToStand, which
        // is what actually decides whether a body fits somewhere in play, ignores them for exactly
        // this reason; these probes ask the same question and must ask it the same way.
        private const QueryTriggerInteraction NotTriggers = QueryTriggerInteraction.Ignore;

        // How far out from the gear wall's face a player stands when using it, and their eye
        // height there — the ray the aimability guard casts is the one WallAimController casts
        // in play.
        private const float WallUseStandoff = 2.5f;
        private const float WallUseEyeHeight = 1.7f;

        // The look-ray a player boards with is cast from the eye, about a metre above the body's own
        // origin — the same offset the seat markers are pushed down by. Its REACH is read off the
        // player rather than written here: Interactor's own default is 5 m and the player prefab
        // overrides it to 20, so a probe carrying the default would sweep a quarter of the distance
        // a player actually reaches and call the ship safe.
        private const string PlayerPrefabPath =
            "Assets/Game/Prefabs/Characters/Player/PlayerCharacter.prefab";
        private const float EyeAbovePivot = 1f;

        private static float? reach;

        private static float InteractReach
        {
            get
            {
                if (reach != null) return reach.Value;

                GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
                Assert.IsNotNull(player, $"No player prefab at {PlayerPrefabPath}.");
                reach = new SerializedObject(player.GetComponent<Interactor>())
                    .FindProperty("_castDistance").floatValue;
                return reach.Value;
            }
        }

        // Approach directions per boarding volume for the sweep below, at each of three distances
        // across the player's reach. Both matter: an exposure can open up close (through a gap that
        // the hull hides from further out) or only at range (the eye four metres over the dome and
        // the eye eighteen both look straight down through the glass).
        private const int Approaches = 256;
        private static readonly float[] Ranges = { 0.2f, 0.6f, 0.95f };

        // Metres of hem that still count as none. A face is authored as a cell COUNT times
        // PackGrid.Cell, but by the time it is read back off a prefab it is the DECIMAL that count
        // was serialized as — and 30 x PackGrid.Cell and the decimal it comes to are not obliged to
        // be the same float. So "fills edge to edge" is zero to within a rounding step, not
        // bitwise zero, and it must not
        // be asserted with Assert.AreEqual on a Vector2: that comparison is bitwise, and its
        // failure message prints both sides through Vector2.ToString, which is "F2" — a 1e-8 m hem
        // fails it and reports "(0.00, 0.00)" against "(0.00, 0.00)". Nothing under a tenth of a
        // millimetre is a hem; a hem that has cost the face a column is half a cell.
        private const float NoHem = 1e-4f;

        private GameObject ship;

        [TearDown]
        public void TearDown()
        {
            if (ship != null) Object.DestroyImmediate(ship);
        }

        private GameObject InstantiateShip()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(prefab, $"No prefab at {PrefabPath} — run Tools ▸ Vehicles ▸ Build PlayerShip Prefab.");

            ship = Object.Instantiate(prefab);
            ship.transform.position = Vector3.zero;
            // Colliders are placed by the transform hierarchy, and nothing has stepped physics
            // yet — without this the overlap queries run against the prefab's untouched poses.
            Physics.SyncTransforms();
            return ship;
        }

        // Physics queries hit the whole edit scene, and a fixture that ran earlier can leave a
        // body standing in this one's ship. Everything these probes read is filtered back to the
        // instance under test, so the result does not depend on what else is in the scene.
        private IEnumerable<Collider> Ours(IEnumerable<Collider> hits) =>
            hits.Where(h => h.transform.IsChildOf(ship.transform));

        // RaycastHit.transform is NOT the collider's transform: where the collider has a
        // Rigidbody above it, Unity hands back the RIGIDBODY's. This ship carries exactly one, on
        // its root, so every hit anywhere on the hull reports the root — which happens to be
        // harmless for "is this ours?" and is silently wrong for any question about WHICH part was
        // hit. Every probe here asks through hit.collider for that reason.
        private IEnumerable<RaycastHit> Ours(IEnumerable<RaycastHit> hits) =>
            hits.Where(h => h.collider.transform.IsChildOf(ship.transform));

        /// <summary>
        /// Highest ground anywhere under a standing body's footprint, or null if it is over
        /// nothing. Sampling the centre alone reads the deck's step up to the cockpit as a wall:
        /// the ray finds the low side, the capsule is then placed 0.4 m below the step it is
        /// beside, and its own width puts it inside the riser. A body that wide stands on the
        /// highest thing beneath it.
        /// </summary>
        private float? FloorUnderFootprint(Vector3 above)
        {
            float? highest = null;
            foreach (Vector3 offset in new[]
                     {
                         Vector3.zero,
                         Vector3.forward * BodyRadius, Vector3.back * BodyRadius,
                         Vector3.left * BodyRadius, Vector3.right * BodyRadius,
                     })
            {
                foreach (RaycastHit hit in Ours(Physics.RaycastAll(above + offset, Vector3.down,
                                                                  BodyHeight * 2f, ~0, NotTriggers)))
                    if (highest == null || hit.point.y > highest.Value)
                        highest = hit.point.y;
            }
            return highest;
        }

        /// <summary>A collider's own name plus, for a baked hull, the source mesh it came from.</summary>
        private static string Describe(Collider collider) =>
            collider is MeshCollider mesh && mesh.sharedMesh != null
                ? collider.name + " (" + mesh.sharedMesh.name + ")"
                : collider.name;

        /// <summary>
        /// The four chairs' click surfaces: the trigger volumes that carry a boarding control of
        /// their own. Found the way <see cref="Interactor.ResolveAlongRay"/> finds them rather than
        /// by name, so a fifth one added later is covered without touching these tests.
        /// </summary>
        /// <summary>
        /// The trigger volumes a body is BOARDED through — a chair's, or a station's.
        ///
        /// <para>
        /// Narrowed from "every trigger carrying an <c>IInteractable</c>", which was the same set
        /// only for as long as seats were the only controls on this hull mounted behind a trigger.
        /// The oxygen plant's two receptacles are triggers with an interactable as well, and for
        /// the same reason (a solid fixture's own body box answers for everything inside it) — so
        /// the old filter handed them to `DismountPointOf`, which asks a `SerializedObject` about a
        /// mount that is not there and throws. Both callers ask about boarding; the filter now says
        /// so instead of standing in for it.
        /// </para>
        /// </summary>
        private Collider[] BoardingVolumes() =>
            ship.GetComponentsInChildren<Collider>(true)
                .Where(c => c.isTrigger &&
                            (c.GetComponent<MountStation>() != null || c.GetComponent<MountModule>() != null))
                .ToArray();

        /// <summary>
        /// Whether an eye here, looking there, would be offered a seat: <see cref="Interactor"/>
        /// resolves along its ray, and what comes back is a way aboard.
        /// </summary>
        /// <remarks>
        /// Deliberately not <c>CanInteract()</c>, which is the test <see cref="Interactor"/> itself
        /// applies. That one is gated on <c>MountModule.mountCooldown</c> measured against
        /// <c>Time.time</c> — and edit-mode time never advances past zero, so every mount in the
        /// project answers "not yet" here and a probe asking it can only ever say no. What is
        /// actually being asked is whether the *ray* arrives at a way aboard, so the standing
        /// configuration is the right gate: a station is one by construction, and a module is one
        /// when it is directly interactable.
        /// </remarks>
        private bool Boards(Vector3 eye, Vector3 aim, out IInteractable control)
        {
            RaycastHit[] hits = Ours(Physics.RaycastAll(new Ray(eye, aim), InteractReach)).ToArray();
            if (!Interactor.ResolveAlongRay(hits, hits.Length, out control, out _))
                return false;

            return control is MountStation
                   || (control is MountModule module && module.MountableByDirectInteraction);
        }

        /// <summary>Where a boarding control puts a body down — and so where it is boarded from.</summary>
        private static Transform DismountPointOf(Collider volume)
        {
            Object control = volume.GetComponent<MountStation>() is MountStation station
                ? new SerializedObject(station).FindProperty("mount").objectReferenceValue
                : volume.GetComponent<MountModule>();
            return new SerializedObject(control).FindProperty("dismountPoint")
                       .objectReferenceValue as Transform;
        }

        /// <summary>
        /// Whether an eye is standing in open air outside the hull, rather than in a room inside it.
        /// </summary>
        /// <remarks>
        /// Two ways to be inside, because this hull is enclosed two ways. A body in the cabin has
        /// solid hull between it and the sky, which an outward ray finds. A body in the cockpit has
        /// only glass, which carries no collider at all — so the ray finds nothing and would call
        /// the pilot's seat open air. What encloses the cockpit is the `InteractionBlocker` over the
        /// canopy, and standing inside that volume is standing inside the ship.
        ///
        /// Both replace asking whether the ray crossed the canopy's bounds, which is a box that
        /// overhangs the cabin: it labelled a passenger standing behind their own chair, at deck
        /// height inside the ship, as somebody reaching in through the glass.
        ///
        /// <para>
        /// Three refinements, all of them paid for by the same bug on 2026-09-02, when a hand edit
        /// to the .blend deleted the hull blocks between the cabin and the cockpit (Cube.072,
        /// .078, .079, .125) and 15 eyes floating INSIDE the cabin — under a roof at y 8.5, over
        /// the aft chairs — came back "outside". None of them was a real exposure; the classifier
        /// was.
        /// </para>
        /// <list type="number">
        /// <item>The hull's shape is measured WITHOUT the plasma shell. It is a 35 x 16 x 69 m
        /// presentation ellipsoid centred 9.5 m aft of the ship, and including it dragged the
        /// hull's centre from (0, 5.1, 0) to (0, 5.3, -9.5) — which makes "outward" mean "+Z, out
        /// through the nose" for every point in the ship, whichever wall the eye is actually
        /// standing behind. `ThePlasmaShellEnclosesTheHull` already excluded it; this did not.</item>
        /// <item>Crossing the glazing counts as crossing the hull, exactly as STANDING in it
        /// already did — otherwise an eye inside a cabin that opens onto the cockpit escapes
        /// forward through glass that carries no collider and is called open air.</item>
        /// <item>Standing in the ship's own declared interior (`SandstormShelter`, the one volume
        /// in the game that means "inside this hull") is being inside it. Measured off the DECKS,
        /// so it does not share its input with the collision this is asking about.</item>
        /// </list>
        /// <para>
        /// The control for all three is <c>PlayerShip_NoChairIsBoardedFromOutsideTheHull</c> run
        /// with the `CanopyBlocker` deleted: it must still find hundreds of exterior approaches
        /// (242 measured, against 299 before these refinements), or this has stopped being able to
        /// see the hole it exists to find.
        /// </para>
        /// </remarks>
        private bool OutsideTheHull(Vector3 eye)
        {
            foreach (InteractionBlocker glazing in ship.GetComponentsInChildren<InteractionBlocker>(true))
                if (glazing.GetComponent<Collider>().bounds.Contains(eye))
                    return false;

            foreach (SandstormShelter interior in ship.GetComponentsInChildren<SandstormShelter>(true))
                if (interior.Contains(eye))
                    return false;

            Bounds hull = HullShape(includeBoardingStair: true);

            Vector3 outward = eye - hull.center;
            if (outward.sqrMagnitude < 0.001f) return false;

            return !Ours(Physics.RaycastAll(eye, outward.normalized, hull.size.magnitude,
                                            ~0, QueryTriggerInteraction.Collide))
                .Any(hit => !hit.collider.isTrigger
                            || hit.collider.GetComponent<InteractionBlocker>() != null);
        }

        /// <summary>
        /// The shape of the ship itself, from its renderers — never including the entry burn's
        /// plasma shell, which is presentation and is four times the hull's own length.
        /// </summary>
        private Bounds HullShape(bool includeBoardingStair)
        {
            Transform shell = FindPlasmaShell();

            Bounds hull = default;
            bool first = true;
            foreach (Renderer renderer in ship.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.transform == shell || renderer.transform.IsChildOf(shell)) continue;
                if (!includeBoardingStair && renderer.name.StartsWith("Mesh_BoardingStair")) continue;

                if (first) { hull = renderer.bounds; first = false; }
                else hull.Encapsulate(renderer.bounds);
            }

            Assert.IsFalse(first, "No hull renderers to measure — this test is proving nothing.");
            return hull;
        }

        /// <summary>Evenly spread directions over the whole sphere, for looking at a seat from everywhere.</summary>
        private static Vector3 Approach(int index, int total)
        {
            float y = 1f - index / (float)(total - 1) * 2f;
            float radius = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
            float theta = Mathf.PI * (3f - Mathf.Sqrt(5f)) * index;
            return new Vector3(Mathf.Cos(theta) * radius, y, Mathf.Sin(theta) * radius);
        }

        private ArticulatedPart[] BayDoors() =>
            ship.GetComponentsInChildren<ArticulatedPart>(true)
                .Where(p => p.name.StartsWith("BayDoorLeaf"))
                .ToArray();

        /// <summary>
        /// Parts the aft doors and pushes the move into the physics scene. Instant, because nothing
        /// steps Update here — an animated open would never arrive.
        /// </summary>
        private void OpenBayDoors()
        {
            foreach (ArticulatedPart leaf in BayDoors())
                leaf.SetOpen(true, instant: true);
            Physics.SyncTransforms();
        }

        [Test]
        public void PlayerShip_KeepsItsOpenDoors() =>
            PersistenceProbe.For(PrefabPath)
                .Mutate(go =>
                {
                    // A player state: side door opened (which deploys stair + platform), back
                    // door left shut.
                    foreach (ArticulatedPart part in go.GetComponentsInChildren<ArticulatedPart>(true))
                        if (part.name != "BackDoor")
                            part.SetOpen(true, instant: true);
                })
                .AssertSurvivesRoundTrip();

        [Test]
        public void PlayerShip_HasTheBuiltShape()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(prefab, $"No prefab at {PrefabPath} — run Tools ▸ Vehicles ▸ Build PlayerShip Prefab.");

            ArticulatedPart[] parts = prefab.GetComponentsInChildren<ArticulatedPart>(true);
            Assert.AreEqual(13, parts.Length,
                "Back door, six bay-door panels, four sliding leaves, boarding stair and sill platform.");

            string[] names = parts.Select(p => p.name).ToArray();
            Assert.Contains("BackDoor", names);
            Assert.Contains("BoardingStair", names);
            Assert.Contains("SillPlatform", names);
            // Three panels a side, because the bulkhead has no pocket for a solid leaf.
            Assert.AreEqual(3, names.Count(n => n.StartsWith("BayDoorLeaf_Port")));
            Assert.AreEqual(3, names.Count(n => n.StartsWith("BayDoorLeaf_Stbd")));

            Assert.AreEqual(11, prefab.GetComponentsInChildren<ArticulatedPartInteraction>(true).Length,
                "Every sliding leaf is a switch for the side assembly; the back door and every "
                + "bay-door panel are switches for the aft one.");
            // Four chairs, four boarding points, and only one of them the controls. The
            // front-left chair carries a MountStation into the ship's root MountModule (the one
            // the SteerModule is bound to); the other three are passenger seats with their own
            // modules that no SteerModule references (sit, no helm).
            Assert.AreEqual(4, prefab.GetComponentsInChildren<SpaceGame.Agents.MountModule>(true).Length,
                "Root pilot module + three passenger seat modules.");
            Assert.AreEqual(1, prefab.GetComponentsInChildren<MountStation>(true).Length,
                "One station, on the pilot's chair. The root module is not directly interactable, "
                + "so this is the only way to the controls.");
            Assert.AreEqual(1, prefab.GetComponents<SpaceGame.Agents.SteerModule>().Length,
                "Only the root module grants control.");
            Assert.AreEqual(3, prefab.GetComponentsInChildren<Transform>(true)
                .Count(t => t.name.StartsWith("PassengerSeat")), "Three ride-along seats.");
            Assert.IsNotNull(prefab.GetComponent<Unity.Netcode.NetworkObject>());
            Assert.AreNotEqual(0u, prefab.GetComponent<Unity.Netcode.NetworkObject>().PrefabIdHash,
                "Script-built prefabs ship GlobalObjectIdHash 0 unless re-imported — see builder.");
        }

        /// <summary>
        /// Look at a chair, get that chair — and only the pilot's gets the controls.
        /// </summary>
        /// <remarks>
        /// Every chair is boarded from a trigger volume of its own, because Interactor resolves a
        /// solid collider by walking UP the hierarchy and a trigger is the one thing it will not
        /// resolve upward. The pilot's carries a MountStation into the hull's own module; the
        /// other three carry passenger modules. Lose one of those volumes and the chair keeps its
        /// module, its network sync and its save wiring — the only symptom is that the chair stops
        /// offering anything, and nothing logs.
        ///
        /// So this asserts through the real resolver rather than by counting components: exactly
        /// one chair answers with the controls, and the rest each answer with a seat of their own.
        /// </remarks>
        [Test]
        public void PlayerShip_EachChairOffersItsOwnSeat()
        {
            InstantiateShip();
            MountModule helm = ship.GetComponent<MountModule>();
            Assert.IsNotNull(helm, "The root module is the helm — SteerModule is bound to it.");

            var answered = new List<(string chair, IInteractable control)>();
            foreach (Transform chair in ship.GetComponentsInChildren<Transform>(true)
                         .Where(t => t.name.StartsWith("Cockpit_Seat_Command")))
            {
                // Straight down onto the seat from inside the cockpit: the canopy carries no
                // collider, so this is the one approach that is the same for all four chairs
                // however they are turned.
                Bounds seatBounds = chair.GetComponent<Renderer>().bounds;
                var ray = new Ray(seatBounds.center + Vector3.up * (seatBounds.extents.y + 0.6f),
                                  Vector3.down);
                RaycastHit[] hits = Ours(Physics.RaycastAll(ray, 3f)).ToArray();

                Assert.IsTrue(
                    Interactor.ResolveAlongRay(hits, hits.Length, out IInteractable found, out _),
                    $"Nothing interactable under a body looking at '{chair.name}'.");
                answered.Add((chair.name, found));
            }

            Assert.AreEqual(4, answered.Count, "Four command chairs.");

            string[] helmChairs = answered.Where(a => a.control is MountStation)
                                          .Select(a => a.chair).ToArray();
            Assert.AreEqual(1, helmChairs.Length,
                "Exactly one chair may take the controls, but these do: " + string.Join(", ", helmChairs));

            // The station has to name the module the SteerModule drives, not merely be a station:
            // wired to a passenger seat it would still board somebody, into a chair with no
            // controls, and the ship would simply never answer its stick.
            SerializedProperty wired = new SerializedObject(
                answered.First(a => a.control is MountStation).control as MountStation)
                .FindProperty("mount");
            Assert.AreSame(helm, wired.objectReferenceValue,
                "The helm station does not board the module SteerModule is bound to.");

            MountModule[] seats = answered.Select(a => a.control).OfType<MountModule>().ToArray();
            Assert.AreEqual(3, seats.Distinct().Count(),
                "The other three chairs each seat their occupant in a seat of their own.");
            CollectionAssert.DoesNotContain(seats, helm,
                "A passenger chair answers with the hull's own module, so sitting down flies the ship.");
        }

        /// <summary>
        /// Nothing on the hull may board the helm — only the pilot's chair may.
        /// </summary>
        /// <remarks>
        /// The reported symptom: pressing E anywhere on the hull put the presser in the pilot's
        /// chair. The root MountModule was directly interactable, and Interactor resolves a solid
        /// collider by walking UP the hierarchy until it finds an IInteractable — so all 140-odd
        /// wall, floor and hull slabs, the boarding stair, the salvage sockets and the three
        /// passenger chairs' own meshes answered with the controls.
        ///
        /// Asserted over every collider rather than by casting a handful of rays, because the
        /// failure is a property of the hierarchy and not of any one viewpoint: this repeats
        /// exactly what ResolveAlongRay does with a solid hit, for every solid collider the ship
        /// has. It fails the moment mountableByDirectInteraction comes back on.
        /// </remarks>
        [Test]
        public void PlayerShip_NoHullColliderBoardsTheHelm()
        {
            InstantiateShip();

            foreach (Collider collider in ship.GetComponentsInChildren<Collider>(true))
            {
                // A trigger answers only when it holds the interactable itself, and the four
                // chairs' volumes are exactly that — they are the boarding points this is the
                // complement of.
                if (collider.isTrigger) continue;

                IInteractable resolved = collider.GetComponent<IInteractable>()
                                         ?? collider.GetComponentInParent<IInteractable>();

                Assert.IsNotInstanceOf<MountStation>(resolved,
                    $"'{collider.name}' is solid and carries the helm station, so it boards the "
                    + "helm from wherever it is hit.");

                if (resolved is MountModule reached)
                    Assert.IsFalse(reached.MountableByDirectInteraction,
                        $"Looking at '{collider.name}' offers a seat in '{reached.name}' — "
                        + "pressing E on the hull puts the presser in the pilot's chair.");
            }
        }

        /// <summary>
        /// Nobody standing outside the hull is offered a chair inside it.
        /// </summary>
        /// <remarks>
        /// The complement of the test above, and the half it cannot see. That one asks what a SOLID
        /// collider resolves to; this one asks what is in the way at all — and over the cockpit,
        /// nothing was. The canopy dome deliberately carries no collider (a convex hull of it fills
        /// the cockpit and would brain a three-metre pilot, see PlayerShipBuilder), and a trigger is
        /// transparent to <see cref="Interactor.ResolveAlongRay"/> unless it holds the interactable
        /// itself — which the four chairs' volumes do. So the chairs stood in open sight of anyone
        /// outside the glass, out to the player's whole 20 m reach: this sweep found 282 exterior
        /// approaches that boarded one, the plainest being an eye four metres straight above the
        /// dome with nothing but air and glass in between. That is the "pressing E on the ship
        /// mounts me" report; the fix is the canopy's `InteractionBlocker` (see PlayerShipBuilder).
        ///
        /// Swept rather than spot-checked because the hole is a shape, not a viewpoint: it is
        /// wherever the hull's collision has a gap over something boardable. And phrased as "from
        /// outside the hull" rather than "through the canopy", because the canopy is only where
        /// this ship's gap happens to be — the next one will be somewhere else.
        /// </remarks>
        [Test]
        public void PlayerShip_NoChairIsBoardedFromOutsideTheHull()
        {
            InstantiateShip();

            var reached = new List<string>();
            foreach (Collider volume in BoardingVolumes())
            {
                Vector3 centre = volume.bounds.center;
                foreach (float range in Ranges)
                for (int i = 0; i < Approaches; i++)
                {
                    Vector3 eye = centre + Approach(i, Approaches) * (InteractReach * range);
                    if (!OutsideTheHull(eye)) continue;

                    if (Boards(eye, (centre - eye).normalized, out IInteractable control))
                        reached.Add($"'{((Component)control).name}' from {eye:F1}");
                }
            }

            Assert.IsEmpty(reached,
                $"{reached.Count} approaches from outside the hull are offered a seat inside it. "
                + "You cannot reach through a wall, and you cannot reach through glass either: "
                + string.Join(", ", reached.Take(4)));
        }

        /// <summary>
        /// Every chair is still boarded from the deck it puts you down on.
        /// </summary>
        /// <remarks>
        /// The other half of the test above, and the reason it cannot simply be answered by making
        /// the cockpit opaque: whatever stops the ray from outside must not stop it from inside, or
        /// a pilot who stands up can never sit back down. A control's own dismount point is the one
        /// non-arbitrary place to ask from — it is where that control just put a body.
        /// </remarks>
        [Test]
        public void PlayerShip_EveryChairIsBoardedFromWhereItPutsYouDown()
        {
            InstantiateShip();

            var unreachable = new List<string>();
            foreach (Collider volume in BoardingVolumes())
            {
                Transform dismount = DismountPointOf(volume);
                Assert.IsNotNull(dismount, $"'{volume.name}' has no dismount point to stand on.");

                Vector3 eye = dismount.position + Vector3.up * EyeAbovePivot;
                if (!Boards(eye, (volume.bounds.center - eye).normalized, out _))
                    unreachable.Add($"{volume.name} from {dismount.position:F1}");
            }

            Assert.IsEmpty(unreachable,
                "A body standing where these controls put it down is offered nothing: "
                + string.Join(", ", unreachable));
        }

        /// <summary>
        /// A body must be able to walk the length of the main deck, down the middle.
        /// </summary>
        /// <remarks>
        /// This ship shipped once with its interior filled in. The collision was derived from the
        /// art by rule, and the rule that handled the hollow hull skins — shrink-wrap the surface
        /// into grid cells — has to make each cell span every surface point in it, so wherever the
        /// skin curved from floor to roof the cell became a pillar standing in the bay. Nothing
        /// about that is visible: the prefab looks right, the console says nothing, and you only
        /// find it by trying to walk. The collision comes from a baked convex decomposition now
        /// (see PlayerShipBuilder), and this is the assertion that says so in terms of the thing
        /// that actually broke.
        ///
        /// The aisle rather than the whole deck, because the deck slab is not the room: it runs on
        /// under the hull walls that stand on it, so its own footprint includes metres that were
        /// never floor — and the outboard strip of it is deliberately inside the hull skin's baked
        /// convex fill (see PlayerShip.md), which a full-width sweep would report as a fault when it
        /// is a decision. The aisle is the part the design does promise — the way from the back ramp
        /// forward — and it needs no threshold to be meaningful.
        ///
        /// But the aisle is a CORRIDOR, not a line, and for a long time this swept one x: the deck's
        /// exact centre. A column of samples half a metre wide says nothing about the floor half a
        /// metre to the side of it, which is where a player walks the moment they step round
        /// anything — so an intrusion that left the centre line clear was invisible here and showed
        /// up in play as being shoved by nothing. It sweeps a grid across
        /// <see cref="AisleHalfWidth"/> either side now.
        ///
        /// Each position stands on whatever the floor turns out to be there rather than on the
        /// deck slab's own plane. The deck steps up toward the cockpit, and a probe held at one
        /// height reads the step as a wall — which is how the first version of this test failed on
        /// collision that was doing its job.
        /// </remarks>
        [Test]
        public void PlayerShip_MainDeckAisleIsWalkable()
        {
            Bounds deck = InstantiateShip().transform.Find("Model/Mesh_Deck_Main").GetComponent<Renderer>().bounds;

            // The aisle runs from the back ramp forward, and the aft end of it is a DOORWAY. A
            // closed door standing in the way is the doors working, not the interior filling in —
            // which is the only thing this test is about — so they are opened first.
            OpenBayDoors();

            var blocked = new List<string>();

            const int steps = 16;
            const int columns = 4;
            int positions = 0;

            for (int c = 0; c <= columns; c++)
            {
                float x = Mathf.Lerp(deck.center.x - AisleHalfWidth, deck.center.x + AisleHalfWidth,
                                     c / (float)columns);

                for (int i = 0; i <= steps; i++)
                {
                    float z = Mathf.Lerp(deck.min.z + BodyRadius, deck.max.z - BodyRadius, i / (float)steps);
                    Vector3 above = new Vector3(x, deck.max.y + BodyHeight, z);
                    positions++;

                    float? floor = FloorUnderFootprint(above);
                    if (floor == null)
                    {
                        blocked.Add($"({x:F1}, {z:F1}): no floor under the aisle");
                        continue;
                    }

                    // A little standing skin: a capsule seated exactly on the surface it stands on
                    // is tangent to it, and tangency is not a reading worth arguing about.
                    Vector3 feet = new Vector3(above.x, floor.Value + StandingSkin, above.z);
                    Collider[] hit = Ours(Physics.OverlapCapsule(
                        feet + Vector3.up * BodyRadius,
                        feet + Vector3.up * (BodyHeight - BodyRadius), BodyRadius, ~0, NotTriggers)).ToArray();
                    if (hit.Length > 0)
                        blocked.Add($"({x:F1}, {z:F1}): standing at y {floor.Value:F2} is inside "
                                    + Describe(hit[0]));
                }
            }

            Assert.IsEmpty(blocked,
                $"{blocked.Count} of {positions} standing positions in the main deck's aisle are " +
                $"inside collision, for a {BodyRadius * 2f:F1} x {BodyHeight:F1} m body. Either the " +
                "interior is filling in again — check the collision bake in player_ship_export.py — " +
                "or something has been stood in the aisle that does not fit beside a player.");
        }

        /// <summary>
        /// PhysX refuses a concave MeshCollider on a non-kinematic Rigidbody, and the ship is one
        /// (it rests on the sand as dead weight when parked). A concave collider slipping into the
        /// build is an error raised at spawn time, in play, on whoever loads the ship first.
        /// </summary>
        [Test]
        public void PlayerShip_CollisionIsAllConvex()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(prefab);
            Assert.IsFalse(prefab.GetComponent<Rigidbody>().isKinematic,
                "The premise of this test: a kinematic body could carry concave colliders.");

            MeshCollider[] concave = prefab.GetComponentsInChildren<MeshCollider>(true)
                .Where(m => !m.convex)
                .ToArray();
            Assert.IsEmpty(concave.Select(m => m.name),
                "Every MeshCollider on the ship must be convex.");
        }

        /// <summary>
        /// Every leaf's switch must drive the whole assembly — stair and platform included. That
        /// is both the "any panel is interactable" contract and the "opening the door runs out
        /// the ladder" behaviour, and it lives in serialized wiring a refactor could drop.
        /// </summary>
        [Test]
        public void PlayerShip_EveryLeafSwitchDrivesTheWholeAssembly()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(prefab);

            ArticulatedPartInteraction[] sides = prefab.GetComponentsInChildren<ArticulatedPartInteraction>(true)
                .Where(i => i.name.StartsWith("SlidingDoorLeaf"))
                .ToArray();
            Assert.AreEqual(4, sides.Length, "Each sliding leaf carries its own switch.");

            foreach (ArticulatedPartInteraction side in sides)
            {
                var so = new SerializedObject(side);
                SerializedProperty parts = so.FindProperty("parts");
                var driven = Enumerable.Range(0, parts.arraySize)
                    .Select(i => parts.GetArrayElementAtIndex(i).objectReferenceValue)
                    .OfType<ArticulatedPart>()
                    .Select(p => p.name)
                    .ToArray();

                Assert.AreEqual(6, driven.Length, side.name + ": four leaves + stair + platform.");
                Assert.Contains("BoardingStair", driven);
                Assert.Contains("SillPlatform", driven);
            }
        }

        /// <summary>
        /// The aft entrance is one switch: ramp and both leaves, carried by all three, so pressing
        /// any of them opens the lot. Wired in serialized arrays a refactor could quietly drop —
        /// and the way it fails is a ramp that lowers onto a shut door.
        /// </summary>
        [Test]
        public void PlayerShip_TheAftEntranceOpensAsOnePiece()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(prefab);

            ArticulatedPartInteraction[] aft = prefab.GetComponentsInChildren<ArticulatedPartInteraction>(true)
                .Where(i => i.name == "BackDoor" || i.name.StartsWith("BayDoorLeaf"))
                .ToArray();
            Assert.AreEqual(7, aft.Length, "The ramp and all six bay-door panels each carry a switch.");

            foreach (ArticulatedPartInteraction switchOn in aft)
            {
                var so = new SerializedObject(switchOn);
                SerializedProperty parts = so.FindProperty("parts");
                var driven = Enumerable.Range(0, parts.arraySize)
                    .Select(i => parts.GetArrayElementAtIndex(i).objectReferenceValue)
                    .OfType<ArticulatedPart>()
                    .Select(p => p.name)
                    .ToArray();

                Assert.AreEqual(7, driven.Length, switchOn.name + ": ramp + all six panels.");
                Assert.Contains("BackDoor", driven);
                Assert.AreEqual(6, driven.Count(n => n.StartsWith("BayDoorLeaf")),
                    switchOn.name + ": every panel of both leaves.");
            }
        }

        /// <summary>
        /// The point of the doors: shut, the aft doorway is solid; open, a body fits through it.
        ///
        /// Both halves matter and each fails on its own. Leaves sized to the wrong aperture seal
        /// nothing — the ramp lowers onto a gap you can walk straight through, and the whole
        /// feature is invisible. Leaves with nowhere to retract to still LOOK like doors and still
        /// animate, but leave a doorway too narrow to enter, which reads in play as the ship
        /// refusing to let you aboard. Everything about them is measured at build time off geometry
        /// nothing names, so this asserts the measurement landed, not that the wiring exists.
        /// </summary>
        [Test]
        public void PlayerShip_AftDoorwaySealsShutAndClearsOpen()
        {
            InstantiateShip();

            ArticulatedPart[] leaves = BayDoors();
            Assert.AreEqual(6, leaves.Length, "Two leaves of three telescoping panels each.");

            // Where the two leaves meet, which is the middle of the doorway: the innermost panel of
            // each side, measured off the panels rather than re-derived, so this reads the same
            // aperture the builder actually used.
            Bounds port = LeafBounds(leaves.Single(l => l.name == "BayDoorLeaf_Port1"));
            Bounds stbd = LeafBounds(leaves.Single(l => l.name == "BayDoorLeaf_Stbd1"));
            Vector3 middle = (port.center + stbd.center) * 0.5f;

            Vector3 gap = new Vector3(BodyRadius * 2f, BodyHeight, 0.4f) * 0.5f;
            Assert.IsNotEmpty(
                Ours(Physics.OverlapBox(middle, gap, Quaternion.identity, ~0, NotTriggers)).ToArray(),
                "Shut, the aft doorway is open air — the leaves are not sealing it.");

            OpenBayDoors();

            Collider[] blocking = Ours(
                Physics.OverlapBox(middle, gap, Quaternion.identity, ~0, NotTriggers)).ToArray();
            Assert.IsEmpty(blocking.Select(Describe),
                "Open, the aft doorway is still blocked — the leaves have nowhere to retract to.");
        }

        private static Bounds LeafBounds(ArticulatedPart panel) =>
            panel.GetComponentInChildren<Renderer>().bounds;

        // ─────────── The inventory wall ───────────
        //
        // Placed by BuildInventoryWall, which measures its spot off the main deck and the side
        // door rather than carrying authored coordinates. That is the right way to place it and it
        // is also why these tests exist: a re-export that moves either landmark moves the wall, and
        // the way that fails is a rack standing in the middle of the room, or half inside the hull,
        // with nothing at all in the console.

        private WallInventory Wall() => ship.GetComponentInChildren<WallInventory>(true);

        // ── Repair station ──
        // Nested like the projector, by PlayerShipBuilder.BuildRepairStation: the bench on the
        // side opposite the gear wall, aft of the map projector. Set dressing since scrap was
        // removed — it holds no state and offers no interaction — but the deck is laid out around
        // its volume, so what a sweep cannot know still matters: that it stands where the room
        // leaves floor for it, and that the aisle in front of it is still walkable.

        private Transform Station() => ship.GetComponentsInChildren<Transform>(true)
            .First(t => t.name == "RepairStation");

        [Test]
        public void PlayerShip_HasOneRepairStationOnTheProjectorsSideFacingTheRoom()
        {
            InstantiateShip();

            Transform[] stations = ship.GetComponentsInChildren<Transform>(true)
                .Where(t => t.name == "RepairStation")
                .ToArray();
            Assert.AreEqual(1, stations.Length,
                            "The ship carries exactly one repair station — a second bench would " +
                            "stand in floor the deck has already spent.");
            Transform station = stations[0];

            HoloProjectorInteraction projector = ship.GetComponentInChildren<HoloProjectorInteraction>(true);
            Assert.IsNotNull(projector, "No map projector to stand beside.");
            PackSurface wallFace = Wall().GetComponentInChildren<PackSurface>(true);

            Vector3 stationLocal = ship.transform.InverseTransformPoint(station.transform.position);
            Vector3 projectorLocal = ship.transform.InverseTransformPoint(projector.transform.position);
            Vector3 wallLocal = ship.transform.InverseTransformPoint(wallFace.transform.position);

            Assert.AreEqual(Mathf.Sign(projectorLocal.x), Mathf.Sign(stationLocal.x),
                            "The station shares the projector's side of the deck.");
            Assert.AreNotEqual(Mathf.Sign(wallLocal.x), Mathf.Sign(stationLocal.x),
                               "The station is on the gear wall's side — the two would contest " +
                               "the same floor.");
            Assert.Less(stationLocal.z, projectorLocal.z,
                        "The station stands forward of the projector; the builder puts it aft.");

            // Its face looks across the deck, toward the wall's side — into the room.
            var acrossToWall = new Vector3(Mathf.Sign(wallLocal.x), 0f, 0f);
            Vector3 facing = ship.transform.InverseTransformDirection(station.transform.forward);
            Assert.Greater(Vector3.Dot(facing, acrossToWall), 0.99f,
                           $"The station faces {facing:0.00} in ship space; it should face " +
                           $"{acrossToWall} so its worktop is the side a player meets.");

            Assert.IsNotNull(station.GetComponent<Collider>(),
                             "The station has no collider, so the crew walks through the bench.");
        }

        /// <summary>
        /// Its volume shares no space with anything else of the ship's — the rib feet, the baked
        /// hull fill, the projector, the aft bulkhead's panels and the bay-door leaves — and its
        /// base is on the deck. A re-export that moves the aft bulkhead forward, or a nudge to
        /// <c>RepairStationAft</c>, fails here with the intruding collider named.
        /// </summary>
        [Test]
        public void PlayerShip_RepairStationStandsOnTheDeckClearOfTheHull()
        {
            InstantiateShip();

            Transform station = Station();
            var box = station.GetComponent<BoxCollider>();
            Assert.IsNotNull(box, "The station has no collider, so the crew walks through it.");

            // Shrunk by the standing skin: the fixture's base sits ON the deck plate, and an
            // overlap query counts a touching face as an overlap.
            Vector3 centre = box.transform.TransformPoint(box.center);
            Vector3 half = Vector3.Scale(box.size, box.transform.lossyScale) * 0.5f
                           - Vector3.one * StandingSkin;
            Collider[] inside = Ours(Physics.OverlapBox(centre, half, box.transform.rotation, ~0, NotTriggers))
                .Where(c => !c.transform.IsChildOf(station.transform))
                .ToArray();
            Assert.IsEmpty(inside.Select(Describe),
                           "Something of the ship's own is standing inside the repair station.");

            // The origin is the model's floor line; a ray from just above it finds the deck.
            Vector3 probe = station.transform.position + Vector3.up * 0.05f;
            RaycastHit[] under = Ours(Physics.RaycastAll(probe, Vector3.down, 1f, ~0, NotTriggers))
                .Where(h => !h.collider.transform.IsChildOf(station.transform))
                .OrderBy(h => h.distance)
                .ToArray();
            Assert.IsNotEmpty(under, "Nothing under the repair station at all — it is not on a deck.");
            float clearance = probe.y - under[0].point.y;
            Assert.That(clearance, Is.InRange(0.0f, 0.10f),
                        $"The station's base sits {clearance - 0.05f:0.00} m over the floor " +
                        "under it; it should stand on the deck.");
        }

        /// <summary>
        /// The bench does not eat the aisle: there is room for a standing body on the floor in
        /// front of its face. Nothing is interacted with here any more — the station offers no
        /// prompt since scrap went — but a fixture that walls off its own side of the deck is
        /// still a bug, and one only a body-sized query finds.
        /// </summary>
        [Test]
        public void PlayerShip_RepairStationLeavesRoomToStandInFrontOfIt()
        {
            InstantiateShip();

            Transform station = Station();
            var box = station.GetComponent<BoxCollider>();
            Vector3 face = box.transform.TransformPoint(box.center + Vector3.forward * box.size.z * 0.5f);

            Vector3 feet = face + station.transform.forward * (BodyRadius + 0.6f);
            feet.y = station.transform.position.y;

            Collider[] blocking = Ours(Physics.OverlapCapsule(
                    feet + Vector3.up * (BodyRadius + StandingSkin),
                    feet + Vector3.up * (BodyHeight - BodyRadius + StandingSkin),
                    BodyRadius, ~0, NotTriggers))
                .ToArray();
            Assert.IsEmpty(blocking.Select(Describe),
                           "A body cannot stand in front of the repair station's face.");
        }

        /// <summary>
        /// A fixture's state is collected by the HULL's entity. A fixture prefab carries its own
        /// SaveableEntity and TransformSaveable (the wiring pass stamps every saveable prefab);
        /// nested, the entity stops the hull's collection at the fixture and names the ship's own
        /// prefab id, and the pose saver overwrites the wreck's pose in the record. The builder
        /// strips both (<c>PlayerShipBuilder.StripNestedSavers</c>); this is what proves it.
        /// </summary>
        [Test]
        public void PlayerShip_FixtureStateIsSavedInTheHullsOwnRecord()
        {
            InstantiateShip();

            SaveableEntity[] entities = ship.GetComponentsInChildren<SaveableEntity>(true);
            Assert.AreEqual(1, entities.Length,
                            "SaveableEntities on the hull: " + string.Join(", ", entities.Select(e => e.name)) +
                            " — one per prefab, on the root. A nested one is a second hull on every load.");
            Assert.AreEqual(ship.transform, entities[0].transform, "The one entity is not on the root.");

            TransformSaveable[] poses = ship.GetComponentsInChildren<TransformSaveable>(true);
            Assert.AreEqual(1, poses.Length,
                            "TransformSaveables on the hull: " + string.Join(", ", poses.Select(p => p.name)) +
                            " — a fixture's would overwrite the wreck's saved pose with its own.");

            // The projector's saver is the hull's now. Asked of the saver list rather than of a
            // capture, because ProjectorSaveable deliberately writes nothing while the switch is
            // off — its authored state — so an absent key proves nothing.
            IReadOnlyList<SpaceGame.Persistence.ISaveable> savers = entities[0].Savers();
            Assert.IsTrue(savers.Any(s => s is ProjectorSaveable),
                          "The hull's entity does not collect the projector's saver.");
        }

        // ─────────── The oxygen plant ───────────
        //
        // Fourth fixture on the main deck, forward of the projector on the same side. Two things
        // are worth guarding: that it stands ON the deck with nothing of the ship's own inside it,
        // and that each of its two receptacles is what a look-ray from the aisle meets FIRST.
        // The second is not a nicety — the plant's own body box is solid and has an interactable
        // above it on the hull, so a dock whose trigger volume does not stand proud of that box is
        // wired, replicated, saved and completely unreachable.

        private OxygenGenerator Plant() => ship.GetComponentInChildren<OxygenGenerator>(true);

        [Test]
        public void PlayerShip_OxygenPlantStandsOnTheDeckClearOfEverything()
        {
            InstantiateShip();

            OxygenGenerator[] plants = ship.GetComponentsInChildren<OxygenGenerator>(true);
            Assert.AreEqual(1, plants.Length,
                            "The ship carries exactly one oxygen plant — run Tools ▸ SpaceGame ▸ " +
                            "Build Oxygen System, then rebuild the ship.");

            OxygenGenerator plant = plants[0];
            var box = plant.GetComponent<BoxCollider>();
            Assert.IsNotNull(box, "The plant has no body collider, so a player walks through it.");

            // Shrunk by the standing skin: the fixture's base sits ON the deck plate, and an
            // overlap query counts a touching face as an overlap.
            Vector3 centre = box.transform.TransformPoint(box.center);
            Vector3 half = Vector3.Scale(box.size, box.transform.lossyScale) * 0.5f
                           - Vector3.one * StandingSkin;
            Collider[] inside = Ours(Physics.OverlapBox(centre, half, box.transform.rotation, ~0, NotTriggers))
                .Where(c => !c.transform.IsChildOf(plant.transform))
                .ToArray();
            Assert.IsEmpty(inside.Select(Describe),
                           "Something of the ship's own is standing inside the oxygen plant — it " +
                           "is in the hull, the deckhead, or one of the other three fixtures.");

            // The origin is the model's floor line; a ray from just above it finds the deck.
            Vector3 probe = plant.transform.position + Vector3.up * 0.05f;
            RaycastHit[] under = Ours(Physics.RaycastAll(probe, Vector3.down, 1f, ~0, NotTriggers))
                .Where(h => !h.collider.transform.IsChildOf(plant.transform))
                .OrderBy(h => h.distance)
                .ToArray();
            Assert.IsNotEmpty(under, "Nothing under the oxygen plant at all — it is not on a deck.");
            float clearance = probe.y - under[0].point.y;
            Assert.That(clearance, Is.InRange(0.0f, 0.10f),
                        $"The plant's base sits {clearance - 0.05f:0.00} m over the floor under " +
                        "it; a wall unit should stand on the deck.");
        }

        /// <summary>
        /// Both receptacles are reachable: a look-ray from a standing eye in front of each one
        /// meets that dock's own trigger volume before anything else of the ship's.
        ///
        /// <para>
        /// Triggers are deliberately INCLUDED in this probe, unlike every walkability probe above
        /// it, because the thing under test is a trigger and because <c>Interactor</c>'s own
        /// raycast includes them. That is the whole mechanism: a trigger answers only when the
        /// interactable is on the trigger's own GameObject, so it is see-through to everything
        /// else while still being the nearest hit.
        /// </para>
        /// </summary>
        [Test]
        public void PlayerShip_BothOxygenDocksAreAimedAtFromTheAisle()
        {
            InstantiateShip();

            OxygenGeneratorDock[] docks = Plant().GetComponentsInChildren<OxygenGeneratorDock>(true);
            Assert.AreEqual(2, docks.Length, "The plant does not have both of its receptacles.");

            foreach (OxygenGeneratorDock dock in docks)
            {
                var volume = dock.GetComponent<BoxCollider>();
                Assert.IsNotNull(volume, dock.name + " has no aim volume.");
                Assert.IsTrue(volume.isTrigger,
                              dock.name + "'s aim volume is SOLID, so the ray resolves it up to " +
                              "the hull and the dock offers nothing.");

                Vector3 target = volume.transform.TransformPoint(volume.center);
                Vector3 outward = dock.transform.forward;

                // Standing far enough back that a body fits, and looking from a standing eye.
                Vector3 feet = target + outward * (BodyRadius + 0.9f);
                feet.y = Plant().transform.position.y;
                Vector3 eye = feet + Vector3.up * WallUseEyeHeight;

                RaycastHit[] hits = Ours(Physics.RaycastAll(eye, (target - eye).normalized, InteractReach))
                    .OrderBy(h => h.distance)
                    .ToArray();

                Assert.IsNotEmpty(hits, "The look-ray at " + dock.name + " meets nothing at all.");
                Assert.IsTrue(hits[0].collider == volume,
                              "The first thing the look-ray meets on the way to " + dock.name +
                              " is " + Describe(hits[0].collider) + ", not the dock's own volume. " +
                              "Its aim volume is not standing proud of the machine's body box.");
            }
        }

        /// <summary>
        /// The standing terminal stands on the cockpit's fore deck with nothing of the ship's
        /// own inside its box: not the hull's baked fill, not a chair, not another fixture. The
        /// window it stands in was swept rather than reasoned about (see
        /// <c>PlayerShipBuilder.StandingTerminalFore</c>); a remodel of the cockpit or the
        /// terminal that closes it fails here with the intruding collider named.
        /// </summary>
        [Test]
        public void PlayerShip_StandingTerminalStandsOnTheDeckClearOfEverything()
        {
            InstantiateShip();

            TerminalConsole terminal = Terminal();
            var box = terminal.GetComponent<BoxCollider>();
            Assert.IsNotNull(box, "The terminal has no collider, so no look-ray can find it.");

            Vector3 centre = box.transform.TransformPoint(box.center);
            Vector3 half = Vector3.Scale(box.size, box.transform.lossyScale) * 0.5f
                           - Vector3.one * StandingSkin;
            Collider[] inside = Ours(Physics.OverlapBox(centre, half, box.transform.rotation, ~0, NotTriggers))
                .Where(c => !c.transform.IsChildOf(terminal.transform))
                .ToArray();
            Assert.IsEmpty(inside.Select(Describe),
                           "Something of the ship's own is standing inside the terminal — the hull, " +
                           "a chair, or one of the other fixtures.");

            Vector3 probe = terminal.transform.position + Vector3.up * 0.05f;
            RaycastHit[] under = Ours(Physics.RaycastAll(probe, Vector3.down, 1f, ~0, NotTriggers))
                .Where(h => !h.collider.transform.IsChildOf(terminal.transform))
                .OrderBy(h => h.distance)
                .ToArray();
            Assert.IsNotEmpty(under, "Nothing under the terminal at all — it is not on a deck.");
            float clearance = probe.y - under[0].point.y;
            Assert.That(clearance, Is.InRange(0.0f, 0.10f),
                        $"The terminal's base sits {clearance - 0.05f:0.00} m over the floor under it.");
        }

        /// <summary>
        /// A player in the cockpit walkway can use it: the look-ray from a standing eye in front
        /// of the glass meets the terminal's own collider before anything else of the ship's,
        /// there is room for a body to stand there, and the screen is wired to be drawn on.
        /// </summary>
        [Test]
        public void PlayerShip_StandingTerminalIsReachedFromTheWalkway()
        {
            InstantiateShip();

            TerminalConsole terminal = Terminal();
            var box = terminal.GetComponent<BoxCollider>();
            Vector3 target = box.transform.TransformPoint(box.center);
            Vector3 face = box.transform.TransformPoint(box.center + Vector3.forward * box.size.z * 0.5f);

            Vector3 feet = face + terminal.transform.forward * (BodyRadius + 0.6f);
            feet.y = terminal.transform.position.y;
            Vector3 eye = feet + Vector3.up * WallUseEyeHeight;

            RaycastHit[] hits = Ours(Physics.RaycastAll(eye, (target - eye).normalized, InteractReach, ~0, NotTriggers))
                .OrderBy(h => h.distance)
                .ToArray();
            Assert.IsNotEmpty(hits, "The look-ray from the walkway meets nothing of the ship's at all.");
            Assert.IsTrue(hits[0].collider.transform.IsChildOf(terminal.transform),
                          $"The first thing the look-ray meets is {Describe(hits[0].collider)}, " +
                          "not the terminal.");

            Collider[] blocking = Ours(Physics.OverlapCapsule(
                    feet + Vector3.up * (BodyRadius + StandingSkin),
                    feet + Vector3.up * (BodyHeight - BodyRadius + StandingSkin),
                    BodyRadius, ~0, NotTriggers))
                .ToArray();
            Assert.IsEmpty(blocking.Select(Describe),
                           "A body cannot stand in front of the terminal's screen.");

            Assert.IsNotNull(terminal.GetComponent<TerminalFocusSession>(), "The press has no session to open.");
            Assert.IsNotNull(terminal.GetComponentInChildren<TerminalScreen>(true), "The glass has no screen to draw.");
        }

        private TerminalConsole Terminal()
        {
            TerminalConsole[] terminals = ship.GetComponentsInChildren<TerminalConsole>(true);
            Assert.AreEqual(1, terminals.Length,
                            "The ship carries exactly one standing terminal — run Tools ▸ SpaceGame ▸ " +
                            "Build Standing Terminal Prefab, then rebuild the ship.");
            return terminals[0];
        }

        [Test]
        public void PlayerShip_HasOneInventoryWall()
        {
            InstantiateShip();

            WallInventory[] walls = ship.GetComponentsInChildren<WallInventory>(true);
            Assert.AreEqual(1, walls.Length,
                            "The ship carries exactly one gear wall — every wall message names " +
                            "its own index, but nothing else here expects a second one.");

            PackSurface face = walls[0].GetComponentInChildren<PackSurface>(true);
            Assert.IsNotNull(face, "The wall has no placement face, so nothing can be put on it.");
            Assert.AreEqual(PackSurfaceId.WallGrid, face.Id);

            // In CELLS, which is the quantity InventoryWallBuilder authors — the metres are a
            // consequence of PackGrid.Cell, so a face asserted in metres has to be re-typed here
            // every time the pack is rescaled and says nothing about whether the grid still lines
            // up with the model. 30 x 22 is the grid the lander's aft room takes; the zero hem is
            // what keeps the model's five bay dividers on the lattice gear is dropped onto.
            Assert.AreEqual(new Vector2Int(30, 22), PackGrid.CellsOn(face.Size),
                            $"The gear wall's face is {face.Size.x:0.00} x {face.Size.y:0.00} m, " +
                            "which is no longer 30 x 22 cells.");
            Vector2 hem = PackGrid.Hem(face.Size);
            Assert.AreEqual(0f, Mathf.Max(hem.x, hem.y), NoHem,
                            $"The gear wall's face is inset by {hem.x * 1000f:0.###} x " +
                            $"{hem.y * 1000f:0.###} mm of hem, so it is not a whole number of " +
                            "cells and its grid no longer meets the model's bay dividers.");

            // Drawn larger than it reasons. The face above is the LOGICAL frame and does not move
            // with this; what this catches is the wall prefab having been built before the display
            // scale existed, or built from a model that was not re-scaled with it — in which case
            // the board and the lattice the player drops gear onto are out of step, which looks
            // like a modelling mistake and is not one. InventoryWallBuilder stamps it, and a move
            // of PackScale.Factor re-derives it, so a backpack resize needs the wall prefab
            // rebuilt even though the wall itself is drawn at exactly the same size afterwards.
            Assert.AreEqual(PackScale.WallDisplay, face.DisplayScale, 1e-4f,
                            $"The gear wall is drawn at x{face.DisplayScale:0.000} and " +
                            $"PackScale.WallDisplay is {PackScale.WallDisplay:0.000}. Re-run " +
                            "Tools/SpaceGame/Items/Build Inventory Wall Prefab, then rebuild the " +
                            "ship.");
        }

        /// <summary>
        /// The face looks into the room and its v axis points up.
        ///
        /// Both are worth pinning because neither survives a guess. A PackSurface's frame is local
        /// X = u, Z = v, Y = the normal, and there is no rotation that gives u-right, v-up and an
        /// outward normal at once — so the builder measures the surface and rotates the fitting to
        /// match rather than trusting the prefab's forward. A wall that came out facing the hull
        /// would look perfectly normal and accept nothing.
        /// </summary>
        [Test]
        public void PlayerShip_InventoryWallFacesIntoTheRoom()
        {
            InstantiateShip();

            PackSurface face = Wall().GetComponentInChildren<PackSurface>(true);
            Vector3 normal = ship.transform.InverseTransformDirection(face.transform.up);
            Vector3 up = ship.transform.InverseTransformDirection(face.transform.forward);

            Assert.Less(Mathf.Abs(normal.y), 0.01f, "The face should be vertical.");
            Assert.Greater(up.y, 0.99f, "The face's v axis should point up, not down or sideways.");

            // Inboard: the normal has to point back toward the ship's centreline, whichever side
            // the door put the wall on.
            //
            // Compared on the LATERAL axis alone, and against the face's CENTRE. A PackSurface's
            // transform sits at its rectangle's (0,0) corner, not in the middle of it, so a vector
            // from there to the ship's origin is mostly height and length — dotting a horizontal
            // normal against it answers 0.6 on a wall that is aimed perfectly.
            Vector3 centre = ship.transform.InverseTransformPoint(face.ToWorld(face.Size * 0.5f, 0f));

            Assert.Greater(Mathf.Abs(normal.x), 0.99f,
                           "The face should look straight across the ship, not along it.");
            Assert.Less(normal.x * centre.x, 0f,
                        "The face is pointing at the hull rather than into the room.");
        }

        /// <summary>
        /// The space gear occupies is empty.
        ///
        /// The wall is not flush against the hull and cannot be: the aft room's starboard side is a
        /// run of arch ribs whose feet reach 0.62 m inboard of the deck edge. So the fitting stands
        /// clear of them, and this is the test that says the clearance is still enough — a box the
        /// size of the grid, one item deep, in front of the face.
        /// </summary>
        [Test]
        public void PlayerShip_NothingBlocksTheInventoryWallsFace()
        {
            InstantiateShip();

            WallInventory wall = Wall();
            PackSurface face = wall.GetComponentInChildren<PackSurface>(true);

            // A third of a metre: deeper than the biggest thing the ladder puts on a wall lies, and
            // shallow enough that a probe standing in the aisle is not what fails this test.
            const float ItemDepth = 0.33f;

            // The DRAWN rectangle, not the logical one: the centre already comes through ToWorld
            // in the drawn frame, and a logical-sized box over a wall drawn 1.8x bigger covers
            // less than a third of the face — which is how a blocked bay corner could pass.
            Vector3 centre = face.ToWorld(face.Size * 0.5f, ItemDepth * 0.5f);
            var half = new Vector3(face.Size.x * face.DisplayScale * 0.5f,
                                   ItemDepth * 0.5f,
                                   face.Size.y * face.DisplayScale * 0.5f);

            Collider[] blocking = Ours(
                    Physics.OverlapBox(centre, half, face.transform.rotation, ~0, NotTriggers))
                .Where(c => !c.transform.IsChildOf(wall.transform))
                .ToArray();

            Assert.IsEmpty(blocking.Select(Describe),
                           "Something of the ship's own is standing in the wall's grid — gear " +
                           "placed there would be inside it.");
        }

        /// <summary>
        /// It stands on the deck rather than floating over it or sinking into it. Measured by
        /// dropping a ray from just under the fitting's own base, because the base is what the
        /// builder positions FROM — the grid band's height above the deck is a model constant.
        /// </summary>
        [Test]
        public void PlayerShip_InventoryWallStandsOnTheDeck()
        {
            InstantiateShip();

            WallInventory wall = Wall();
            PackSurface face = wall.GetComponentInChildren<PackSurface>(true);

            // The grid's bottom edge, then down to the deck. GRID_Z0 in inventory_wall.py is
            // 0.36 m in the modelling frame, so a wall sitting on the floor puts its bottom row
            // 0.36 x PackScale.WallDrawn metres up.
            Vector3 bottom = face.ToWorld(new Vector2(face.Size.x * 0.5f, 0f), 0.2f);

            Assert.IsTrue(
                Physics.Raycast(bottom, Vector3.down, out RaycastHit hit, 3f, ~0, NotTriggers),
                "Nothing under the inventory wall at all — it is not standing on a deck.");

            float clearance = bottom.y - hit.point.y;
            float expected = 0.36f * PackScale.WallDrawn;
            Assert.That(clearance, Is.InRange(expected - 0.2f, expected + 0.2f),
                        $"The wall's bottom row sits {clearance:0.00} m over the floor under it; " +
                        $"the model puts it {expected:0.00} m up, so anything far from that " +
                        "means the fitting is floating or buried.");
        }

        /// <summary>
        /// Every cell of the wall's drawn face can be aimed at from where a player stands.
        ///
        /// <para>
        /// This replaced the overhead-gap guard on 2026-09-02, when the wall's placement became
        /// AUTHORED (<c>PlayerShipBuilder.WallPlacementNudge</c>, read back from a hand
        /// placement) and the user chose to tuck the fitting's back and header into the hull's
        /// baked convex fill. "Headroom over the footprint", which that guard measured, stopped
        /// meaning anything at this position: rays cast up from the buried outboard strip start
        /// INSIDE the fill and report its underside as "the roof", so the number is geometry
        /// noise, not a regression signal.
        /// </para>
        /// <para>
        /// What actually has to hold is functional. The FACE is the thing the player uses —
        /// <c>WallAimController</c> resolves the crosshair with exactly this ray — so a ray from
        /// a standing eye to any cell of the grid must reach the wall before anything else of the
        /// ship. A remodel that lowers the roof over the face, a nudge that stands the grid
        /// behind the hull fill, or a fitting drifted deeper into the ship all fail here, on the
        /// built ship, with the culprit named. <c>WallInventoryTests.TheWallIsDrawnAtItsDecidedSize</c>
        /// fails on the size constant alone, before a rebuild makes this one able to speak.
        /// </para>
        /// </summary>
        [Test]
        public void PlayerShip_InventoryWallFaceIsAimableFromTheRoom()
        {
            InstantiateShip();

            WallInventory wall = Wall();
            PackSurface face = wall.GetComponentInChildren<PackSurface>(true);
            Bounds fitting = WallFitting(wall);

            // A standing player in the aisle in front of the face's centre, eye height off the
            // fitting's own base — the pose the wall is actually used from.
            Vector3 eye = face.ToWorld(face.Size * 0.5f, WallUseStandoff);
            eye.y = fitting.min.y + WallUseEyeHeight;

            // The four corner cells and the centre: the corners are where an occluder standing
            // beside or over the wall clips first, the centre is the control that says the probe
            // is inside the ship at all.
            var probes = new[]
            {
                new Vector2(0.05f, 0.05f),
                new Vector2(face.Size.x - 0.05f, 0.05f),
                new Vector2(0.05f, face.Size.y - 0.05f),
                new Vector2(face.Size.x - 0.05f, face.Size.y - 0.05f),
                face.Size * 0.5f,
            };

            foreach (Vector2 uv in probes)
            {
                Vector3 target = face.ToWorld(uv, 0f);
                Vector3 direction = (target - eye).normalized;

                // hit.COLLIDER, never hit.transform: the hull's one Rigidbody is on the ship's
                // root, so hit.transform is that root for every hit on the ship — asked that way
                // the wall could never match itself.
                Collider first = null;
                float nearest = float.MaxValue;

                foreach (RaycastHit hit in Ours(Physics.RaycastAll(eye, direction, 12f, ~0,
                                                                   NotTriggers)))
                {
                    if (hit.distance >= nearest) continue;

                    nearest = hit.distance;
                    first = hit.collider;
                }

                Assert.IsNotNull(first,
                                 $"Nothing at all on the ray to uv {uv} — the probe is not " +
                                 "inside the ship, so this test is proving nothing.");

                Assert.IsTrue(first.transform.IsChildOf(wall.transform),
                              $"Aiming at uv {uv} hits {Describe(first)} before the wall — that " +
                              "part of the grid cannot be used. The fitting has moved relative " +
                              "to the ship, or the ship has grown into the aisle; re-read " +
                              "PlayerShipBuilder.WallPlacementNudge against the room.");
            }
        }

        // ─────────── Atmospheric entry burn ───────────

        /// <summary>
        /// The sheath has to WRAP the ship. It is drawn on the back faces of its shell, so a shell
        /// that ends short of the hull puts the fire inside the fuselage: the nose then pokes out
        /// through its own plasma from outside, and from a seat the burn is clipped away by the
        /// deck it is supposed to be in front of. Nothing logs — the effect simply looks broken in
        /// a way that reads as the shader being wrong.
        ///
        /// <para>
        /// The boarding stair is excluded from the hull being measured for the same reason the
        /// builder excludes it from the ship's origin: it is authored DEPLOYED, reaching to the
        /// ground, and it is not part of the shape the air sees.
        /// </para>
        /// </summary>
        [Test]
        public void PlayerShip_ThePlasmaShellEnclosesTheHull()
        {
            InstantiateShip();

            Transform shell = FindPlasmaShell();
            Bounds sheath = new(shell.position, shell.lossyScale);

            Bounds hull = HullShape(includeBoardingStair: false);

            Assert.IsTrue(sheath.Contains(hull.min) && sheath.Contains(hull.max),
                          "The plasma shell " + sheath.size.ToString("0.0") + " m at " +
                          sheath.center.ToString("0.0") + " does not enclose the " +
                          hull.size.ToString("0.0") + " m hull at " + hull.center.ToString("0.0") +
                          ". Widen EntryShellGirth / EntryShellNoseReach / EntryShellWakeReach in " +
                          "PlayerShipBuilder rather than moving the shell.");
        }

        /// <summary>
        /// The shell must be invisible to physics. It arrives as a Unity primitive, which brings a
        /// SphereCollider along, and a twenty-metre sphere around the ship answers every
        /// interaction ray, every spawn-clearance probe and — worst — ShipHull's own measurement of
        /// the hull, which is taken from COLLIDERS. The arrival plans its landing height off that,
        /// so a collider here does not make the fire solid, it makes the ship land in the air.
        /// </summary>
        [Test]
        public void PlayerShip_ThePlasmaShellIsInvisibleToPhysics()
        {
            InstantiateShip();

            Transform burn = FindPlasmaShell().parent;

            Assert.IsEmpty(burn.GetComponentsInChildren<Collider>(true).Select(Describe),
                           "The entry burn carries colliders. ShipHull measures the hull from its " +
                           "colliders, so these become part of the shape the landing is planned " +
                           "against.");
        }

        /// <summary>
        /// A parked ship is not on fire, and neither is a wreck restored from a save. The renderer
        /// and the lamp are saved DISABLED and switched on by EntryBurn for the length of the
        /// descent only — left enabled in the prefab, every PlayerShip in every world sits inside a
        /// ball of orange for the rest of the session.
        /// </summary>
        [Test]
        public void PlayerShip_TheEntryBurnIsDarkUntilTheDescent()
        {
            InstantiateShip();

            Transform burn = FindPlasmaShell().parent;

            Assert.IsFalse(FindPlasmaShell().GetComponent<Renderer>().enabled,
                           "The plasma shell is enabled on a ship that has not launched.");

            foreach (Light lamp in burn.GetComponentsInChildren<Light>(true))
                Assert.IsFalse(lamp.enabled, "The entry glow lamp '" + lamp.name + "' is lit on a " +
                                             "ship that has not launched.");
        }

        /// <summary>
        /// The interior safe zone is the DECK, not the hull's whole 23 m span and not a slab of air
        /// over the roof. Both halves matter and both have failed: the volume this replaces was 80%
        /// of the collision bounds centred 5 m up, which sheltered a strip of open desert alongside
        /// the ship and contained nobody standing on the deck. Nothing logs either way — the storm
        /// just keeps hurting the crew indoors, or stops hurting them outdoors.
        /// </summary>
        /// <remarks>
        /// Asked through <see cref="SandstormShelter.Contains"/> rather than through the static
        /// <c>ShelterAt</c>: registration happens in OnEnable, which Unity does not raise for an
        /// object instantiated outside play mode, so the static registry is empty here.
        /// </remarks>
        [Test]
        public void PlayerShip_TheInteriorIsShelteredAndTheDesertBesideItIsNot()
        {
            InstantiateShip();

            SandstormShelter shelter = ship.GetComponent<SandstormShelter>();
            Assert.IsNotNull(shelter, "The hull carries no SandstormShelter, so the weather does " +
                                      "not stop at its walls — run Tools ▸ Vehicles ▸ Build " +
                                      "PlayerShip Prefab.");

            foreach (string deckName in new[] { "Mesh_Deck_Main", "Mesh_Deck_Fore" })
            {
                Bounds deck = ship.transform.Find("Model/" + deckName).GetComponent<Renderer>().bounds;
                // The pivot of a standing body: the capsule's centre, half its height up.
                Vector3 standing = new Vector3(deck.center.x, deck.max.y + BodyHeight * 0.5f, deck.center.z);
                Assert.IsTrue(shelter.Contains(standing),
                              $"A body standing on {deckName} at {standing:F2} is not inside the " +
                              "safe zone, so the crew are sanded inside their own ship.");
            }

            Bounds main = ship.transform.Find("Model/Mesh_Deck_Main").GetComponent<Renderer>().bounds;
            float head = main.max.y + BodyHeight * 0.5f;

            Bounds hull = ship.GetComponentsInChildren<Renderer>(true)
                .Select(r => r.bounds)
                .Aggregate((a, b) => { a.Encapsulate(b); return a; });

            var sheltered = new List<string>();
            foreach ((Vector3 point, string where) in new[]
                     {
                         (new Vector3(hull.max.x + 2f, head, main.center.z), "2 m off the starboard beam"),
                         (new Vector3(hull.min.x - 2f, head, main.center.z), "2 m off the port beam"),
                         (new Vector3(main.center.x, head, hull.max.z + 2f), "2 m ahead of the nose"),
                         (new Vector3(main.center.x, head, hull.min.z - 2f), "2 m astern of the ramp"),
                     })
            {
                if (shelter.Contains(point)) sheltered.Add(where);
            }

            Assert.IsEmpty(sheltered, "The interior safe zone reaches outside the hull — a body " +
                                      string.Join(" and ", sheltered) + " is sheltered from the storm.");
        }

        /// <summary>
        /// Both boarding ramps have to LAND on the ground plane the hull is seated against. Above
        /// it, the ramp ends in a lip the player's capsule — a Rigidbody with no step offset —
        /// cannot climb, which is what "it is very hard to get onto the ramp" is. Far below it, the
        /// buried slab is what a parked hull rests on, so the whole ship stands off its skirts by
        /// however deep the mistake was (measured: 0.6 m).
        /// </summary>
        [Test]
        public void PlayerShip_BothBoardingRampsMeetTheGround()
        {
            InstantiateShip();
            float ground = ship.transform.position.y;

            // The side ramp rides the boarding stair, and the stair only SLIDES — stowing it moves
            // nothing vertically, so its deployed heights are readable with the doors shut.
            Collider side = RampCollider("BoardingRamp");
            Assert.AreEqual(ground, side.bounds.min.y, 0.25f,
                            "The side boarding ramp's foot is " +
                            $"{side.bounds.min.y - ground:F2} m off the ground plane.");

            // The aft ramp IS the back door, so it has to be swung open before it is a ramp at all.
            ArticulatedPart backDoor = ship.GetComponentsInChildren<ArticulatedPart>(true)
                .First(part => part.name == "BackDoor");
            backDoor.SetOpen(true, instant: true);
            Physics.SyncTransforms();

            Collider aft = RampCollider("RampSurface");
            Assert.AreEqual(ground, aft.bounds.min.y, 0.25f,
                            $"The aft ramp's foot is {aft.bounds.min.y - ground:F2} m off the " +
                            "ground plane with the door fully open.");

            // ...and its head has to arrive at the deck it leads onto, not 0.2 m under it.
            Bounds deck = ship.transform.Find("Model/Mesh_Deck_Main").GetComponent<Renderer>().bounds;
            Assert.AreEqual(deck.max.y, aft.bounds.max.y, 0.15f,
                            $"The aft ramp's head is {aft.bounds.max.y - deck.max.y:F2} m off the " +
                            "main deck, so walking in from the sand ends at a step.");
        }

        private Collider RampCollider(string name)
        {
            Transform ramp = ship.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(t => t.name == name);
            Assert.IsNotNull(ramp, $"No '{name}' under the ship — that entrance has no walkable " +
                                   "surface over its treads.");

            Collider surface = ramp.GetComponent<Collider>();
            Assert.IsNotNull(surface, $"'{name}' carries no collider.");
            Assert.IsFalse(surface.isTrigger, $"'{name}' is a trigger, so nobody can stand on it.");
            return surface;
        }

        private Transform FindPlasmaShell()
        {
            Transform shell = ship.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(t => t.name == "PlasmaShell");

            Assert.IsNotNull(shell, "No PlasmaShell under the ship — run " +
                                    "Tools ▸ Vehicles ▸ Build PlayerShip Prefab.");
            return shell;
        }

        /// <summary>The whole fitting in world space, from its renderers rather than its one face
        /// collider: the collider is sized to the grid on purpose, and the tray, plinth and header
        /// cowl that stand outside it are exactly what has to clear the room.</summary>
        private static Bounds WallFitting(WallInventory wall)
        {
            Renderer[] renderers = wall.GetComponentsInChildren<Renderer>(true);
            Assert.IsNotEmpty(renderers, "The gear wall has no renderers to measure.");

            Bounds fitting = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) fitting.Encapsulate(renderers[i].bounds);
            return fitting;
        }
    }
}
