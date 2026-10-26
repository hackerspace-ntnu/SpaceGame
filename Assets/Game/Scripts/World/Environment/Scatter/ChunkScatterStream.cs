using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceGame.World.Environment
{
    /// <summary>
    /// Keeps one painted scatter (a grass biome, a rock size class) scattered around the cameras,
    /// chunk by chunk:
    ///
    /// <list type="number">
    /// <item>Chunks around an eye are scattered on worker threads from a captured copy of the
    /// ground, nearest first, so streaming never stalls a frame and a short budget always goes
    /// to the ground at the eye's feet.</item>
    /// <item>A chunk is stored at the <b>storage level</b> its distance needs (level 0 is every
    /// instance; the owner decides what sparser levels hold). As an eye nears it, it is
    /// re-scattered denser; as the eyes leave, sparser. The old chunk draws until the new one lands.</item>
    /// <item>Each level keeps its chunks in its own <see cref="ChunkInstancePool{T}"/>. A full
    /// pool takes back the slot of the chunk seen longest ago; only a second eye far away makes
    /// it grow.</item>
    /// </list>
    ///
    /// Drawing is the owner's: it records its cull over <see cref="CullPools"/>.
    /// </summary>
    public sealed class ChunkScatterStream<T> : IDisposable where T : unmanaged
    {
        /// <summary>Scatters one chunk on a worker thread. Must not touch a Unity object.</summary>
        public delegate T[] Scatter(Vector2Int chunk, int level, PaintedGroundPatch[] ground, int capacity);

        // Room in a level's pool beyond what one eye at a chunk corner needs, for the swaps in
        // flight while the eye moves.
        private const float PoolHeadroom = 1.25f;

        // Every camera's render is a tick, so a chunk the Scene view or another camera used a
        // few ticks ago is still on screen. Only chunks idle longer than this may be evicted or
        // stored sparser.
        private const int IdleTicksBeforeEviction = 8;

        // A finished chunk with no free slot waits this long for one to go idle — after a
        // teleport the old chunks soon do — before its pool grows for a second eye.
        private const int BlockedTicksBeforeGrowing = IdleTicksBeforeEviction * 2;

        // The walk over every chunk in reach is the stream's main CPU cost at a long reach (over
        // a thousand chunks at 300 m). While an eye stays in the chunk where everything was
        // settled — nothing missing, nothing pending — the walk is only repeated this often, to
        // keep the chunks marked as wanted. Two settled chunks are remembered, so the Scene view
        // and the Game view do not defeat it by taking turns.
        private const int SettledRefreshTicks = 30;
        private const int SettledMemory = 2;

        // Stale chunks are looked for this often rather than every tick; the lifetime is far longer.
        private const int ReleaseEveryTicks = 15;

        private sealed class Chunk
        {
            public bool Carried;        // false: no terrain here is painted for this scatter
            public int Level;
            public int Slot = -1;
            public int LastUsedTick;
            public int NeededTick;      // last tick an eye needed (nearly) this density
        }

        private readonly struct PendingScatter
        {
            public readonly Task<T[]> Task;
            public readonly int Level;

            public PendingScatter(Task<T[]> task, int level)
            {
                Task = task;
                Level = level;
            }
        }

        private readonly int slot;
        private readonly float chunkSize;
        private readonly float reach;
        private readonly float edgeWarp;
        private readonly Func<float, int> levelAt;
        private readonly Scatter scatter;
        private readonly Vector2Int[] nearestFirst;
        private readonly ChunkInstancePool<T>[] pools;
        private readonly int[] blockedSinceTick;
        private readonly Dictionary<Vector2Int, Chunk> chunks = new Dictionary<Vector2Int, Chunk>();
        private readonly Dictionary<Vector2Int, PendingScatter> pending = new Dictionary<Vector2Int, PendingScatter>();
        private readonly List<Vector2Int> scratch = new List<Vector2Int>();
        private readonly Vector2Int[] settledCentres = new Vector2Int[SettledMemory];
        private readonly int[] settledTicks = new int[SettledMemory];
        private int lastReleaseTick;

        /// <param name="slot">The scatter's slot in the <see cref="PaintedGround"/> it streams from.</param>
        /// <param name="reach">Chunks farther than this from every eye are not kept.</param>
        /// <param name="edgeWarp">How far, in paint cells, the painted outline wanders.</param>
        /// <param name="levelCapacities">Per storage level, the most instances one chunk can hold.</param>
        /// <param name="levelAt">The storage level a chunk this far from an eye needs.</param>
        public ChunkScatterStream(int slot, float chunkSize, float reach, float edgeWarp, int[] levelCapacities,
                                  Func<float, int> levelAt, Scatter scatter)
        {
            this.slot = slot;
            this.chunkSize = chunkSize;
            this.reach = reach;
            this.edgeWarp = edgeWarp;
            this.levelAt = levelAt;
            this.scatter = scatter;
            nearestFirst = OffsetsNearestFirst();
            int levels = levelCapacities.Length;
            pools = new ChunkInstancePool<T>[levels];
            blockedSinceTick = new int[levels];
            int[] needed = ChunksInReachByLevel(levels);
            for (int level = 0; level < levels; level++)
            {
                pools[level] = new ChunkInstancePool<T>(levelCapacities[level], Mathf.CeilToInt(needed[level] * PoolHeadroom) + 1);
                blockedSinceTick[level] = -1;
            }
            ForgetSettled();
        }

        public float ChunkSize => chunkSize;

        /// <summary>Chunks still being scattered on worker threads.</summary>
        public int PendingCount => pending.Count;

        public bool HasResidents
        {
            get
            {
                foreach (ChunkInstancePool<T> pool in pools)
                    if (pool.Resident > 0)
                        return true;
                return false;
            }
        }

        /// <summary>
        /// Marks the chunks around <paramref name="eye"/> as wanted, starts scattering missing
        /// ones and re-scattering ones stored at the wrong level (at most
        /// <paramref name="budget"/>), then takes in finished ones. True when one was taken in.
        /// </summary>
        public bool Stream(Vector3 eye, PaintedGround ground, int tick, ref int budget)
        {
            if (!ground.CarriesAnywhere(slot))
                return Harvest(tick);
            var centre = new Vector2Int(Mathf.FloorToInt(eye.x / chunkSize), Mathf.FloorToInt(eye.z / chunkSize));
            if (pending.Count == 0 && IsSettled(centre, tick))
                return false;

            // Nearest first, so a short budget always goes to the ground at the eye's feet.
            bool complete = true;
            foreach (Vector2Int offset in nearestFirst)
            {
                Vector2Int coord = centre + offset;
                float distance = DistanceToChunk(coord, eye);
                if (distance > reach)
                    continue;
                int level = levelAt(distance);
                if (chunks.TryGetValue(coord, out Chunk chunk))
                {
                    chunk.LastUsedTick = tick;
                    if (chunk.Carried && !pending.ContainsKey(coord) && NeedsRescatter(chunk, level, tick))
                    {
                        if (budget <= 0)
                        {
                            complete = false;
                            continue;
                        }
                        budget--;
                        StartScatter(coord, level, ground);
                    }
                    continue;
                }
                if (pending.ContainsKey(coord))
                    continue;
                // Ground where this scatter is not painted is settled at once and costs no
                // budget, so a long reach over a mostly bare terrain stays cheap.
                if (!ground.Carries(slot, ChunkRect(coord)))
                {
                    chunks[coord] = new Chunk { LastUsedTick = tick };
                    continue;
                }
                if (budget <= 0)
                {
                    complete = false;
                    continue;
                }
                budget--;
                StartScatter(coord, level, ground);
            }
            bool harvested = Harvest(tick);
            if (complete && pending.Count == 0)
                Settle(centre, tick);
            return harvested;
        }

        /// <summary>Records one Cull dispatch per pool; the cull's other settings are the caller's.</summary>
        public void CullPools(CommandBuffer commands, ComputeShader shader, int kernel)
        {
            foreach (ChunkInstancePool<T> pool in pools)
                pool.Cull(commands, shader, kernel);
        }

        /// <summary>
        /// Every stored chunk whose square comes within <paramref name="radius"/> of
        /// <paramref name="point"/> (on the ground plane), with its instances' CPU copy.
        /// </summary>
        public void ChunksNear(Vector3 point, float radius, List<KeyValuePair<Vector2Int, T[]>> into)
        {
            into.Clear();
            int span = Mathf.CeilToInt(radius / chunkSize);
            var centre = new Vector2Int(Mathf.FloorToInt(point.x / chunkSize), Mathf.FloorToInt(point.z / chunkSize));
            for (int dx = -span; dx <= span; dx++)
            {
                for (int dz = -span; dz <= span; dz++)
                {
                    Vector2Int coord = centre + new Vector2Int(dx, dz);
                    if (!chunks.TryGetValue(coord, out Chunk chunk) || chunk.Slot < 0)
                        continue;
                    if (DistanceToChunk(coord, point) <= radius)
                        into.Add(new KeyValuePair<Vector2Int, T[]>(coord, pools[chunk.Level].Contents(chunk.Slot)));
                }
            }
        }

        public void ReleaseStale(int tick, int lifetimeTicks)
        {
            if (tick - lastReleaseTick < ReleaseEveryTicks)
                return;
            lastReleaseTick = tick;
            scratch.Clear();
            foreach (KeyValuePair<Vector2Int, Chunk> pair in chunks)
                if (tick - pair.Value.LastUsedTick > lifetimeTicks)
                    scratch.Add(pair.Key);
            RemoveAll(scratch);
        }

        /// <summary>Drops the chunks over <paramref name="area"/>, finished or not, so they are scattered again.</summary>
        public void Invalidate(Rect area)
        {
            ForgetSettled();
            scratch.Clear();
            foreach (Vector2Int coord in chunks.Keys)
                if (ChunkRect(coord).Overlaps(area))
                    scratch.Add(coord);
            RemoveAll(scratch);

            // A scatter already running read the old ground; its result is discarded.
            scratch.Clear();
            foreach (Vector2Int coord in pending.Keys)
                if (ChunkRect(coord).Overlaps(area))
                    scratch.Add(coord);
            foreach (Vector2Int coord in scratch)
                pending.Remove(coord);
        }

        public void Dispose()
        {
            pending.Clear();
            chunks.Clear();
            foreach (ChunkInstancePool<T> pool in pools)
                pool.Dispose();
        }

        /// <summary>True when an eye in this chunk found nothing to do within the last refresh.</summary>
        private bool IsSettled(Vector2Int centre, int tick)
        {
            for (int i = 0; i < SettledMemory; i++)
                if (settledCentres[i] == centre && tick - settledTicks[i] < SettledRefreshTicks)
                    return true;
            return false;
        }

        /// <summary>Remembers this chunk as settled, in place of the same chunk or the oldest one.</summary>
        private void Settle(Vector2Int centre, int tick)
        {
            int replace = 0;
            for (int i = 0; i < SettledMemory; i++)
            {
                if (settledCentres[i] == centre)
                {
                    replace = i;
                    break;
                }
                if (settledTicks[i] < settledTicks[replace])
                    replace = i;
            }
            settledCentres[replace] = centre;
            settledTicks[replace] = tick;
        }

        private void ForgetSettled()
        {
            for (int i = 0; i < SettledMemory; i++)
                settledTicks[i] = int.MinValue / 2;
        }

        /// <summary>
        /// Too sparse for this eye: at once. Two levels denser than any eye needs: once no eye
        /// has needed it for a while, so the Scene view and the Game view far apart do not make
        /// a chunk flip between them.
        /// </summary>
        private bool NeedsRescatter(Chunk chunk, int level, int tick)
        {
            if (level <= chunk.Level + 1)
                chunk.NeededTick = tick;
            if (level < chunk.Level)
                return true;
            return level > chunk.Level + 1 && tick - chunk.NeededTick > IdleTicksBeforeEviction;
        }

        private void StartScatter(Vector2Int coord, int level, PaintedGround ground)
        {
            PaintedGroundPatch[] patches = ground.Capture(slot, ChunkRect(coord), edgeWarp);
            int capacity = pools[level].SlotCapacity;
            Scatter build = scatter;
            Task<T[]> task = Task.Run(() => Build(build, coord, level, patches, capacity));
            pending[coord] = new PendingScatter(task, level);
        }

        private static T[] Build(Scatter build, Vector2Int coord, int level, PaintedGroundPatch[] ground, int capacity)
        {
            try
            {
                return build(coord, level, ground, capacity);
            }
            catch (Exception e)
            {
                // A worker's exception would otherwise vanish with its task; the chunk stays bare.
                Debug.LogException(e);
                return Array.Empty<T>();
            }
        }

        private bool Harvest(int tick)
        {
            scratch.Clear();
            foreach (KeyValuePair<Vector2Int, PendingScatter> pair in pending)
                if (pair.Value.Task.IsCompleted)
                    scratch.Add(pair.Key);
            bool harvested = false;
            foreach (Vector2Int coord in scratch)
            {
                PendingScatter done = pending[coord];
                T[] scattered = done.Task.Result;
                // No slot yet: the result stays pending and is taken in on a later tick.
                if (scattered.Length > 0 && !TryFreeSlot(done.Level, tick))
                    continue;
                pending.Remove(coord);
                // A re-scatter replaces the chunk it was made for; until now that one was drawn.
                if (chunks.TryGetValue(coord, out Chunk previous))
                    ReleaseSlot(previous);
                var chunk = new Chunk { Carried = true, Level = done.Level, LastUsedTick = tick, NeededTick = tick };
                if (scattered.Length > 0)
                    chunk.Slot = pools[done.Level].Store(scattered);
                chunks[coord] = chunk;
                harvested = true;
            }
            return harvested;
        }

        private bool TryFreeSlot(int level, int tick)
        {
            ChunkInstancePool<T> pool = pools[level];
            if (pool.HasFreeSlot || !pool.IsAllocated || EvictOldest(level, tick))
            {
                blockedSinceTick[level] = -1;
                return true;
            }
            if (blockedSinceTick[level] < 0)
                blockedSinceTick[level] = tick;
            if (tick - blockedSinceTick[level] < BlockedTicksBeforeGrowing)
                return false;
            pool.Grow();
            blockedSinceTick[level] = -1;
            return true;
        }

        /// <summary>
        /// Frees the slot of the level's chunk no eye has wanted for longest. False when every
        /// one is still on some camera's screen, so the pool really is too small.
        /// </summary>
        private bool EvictOldest(int level, int tick)
        {
            Vector2Int oldest = default;
            int idleBefore = tick - IdleTicksBeforeEviction;
            int oldestTick = idleBefore;
            foreach (KeyValuePair<Vector2Int, Chunk> pair in chunks)
            {
                Chunk chunk = pair.Value;
                if (chunk.Slot >= 0 && chunk.Level == level && chunk.LastUsedTick < oldestTick)
                {
                    oldest = pair.Key;
                    oldestTick = chunk.LastUsedTick;
                }
            }
            if (oldestTick == idleBefore)
                return false;
            // Not through scratch: this runs inside Harvest's loop over it.
            Remove(oldest);
            return true;
        }

        private void RemoveAll(List<Vector2Int> coords)
        {
            foreach (Vector2Int coord in coords)
                Remove(coord);
        }

        private void Remove(Vector2Int coord)
        {
            ReleaseSlot(chunks[coord]);
            chunks.Remove(coord);
        }

        private void ReleaseSlot(Chunk chunk)
        {
            if (chunk.Slot < 0)
                return;
            pools[chunk.Level].Release(chunk.Slot);
            chunk.Slot = -1;
        }

        /// <summary>Every chunk offset that can be in reach, ordered by distance from the eye's chunk.</summary>
        private Vector2Int[] OffsetsNearestFirst()
        {
            int radius = Mathf.CeilToInt(reach / chunkSize) + 1;
            var offsets = new List<Vector2Int>();
            for (int dx = -radius; dx <= radius; dx++)
                for (int dz = -radius; dz <= radius; dz++)
                    if ((dx * dx + dz * dz) <= radius * radius)
                        offsets.Add(new Vector2Int(dx, dz));
            offsets.Sort((a, b) => a.sqrMagnitude.CompareTo(b.sqrMagnitude));
            return offsets.ToArray();
        }

        /// <summary>How many chunks of each storage level one eye at a chunk corner wants.</summary>
        private int[] ChunksInReachByLevel(int levels)
        {
            int radius = Mathf.CeilToInt(reach / chunkSize) + 1;
            var corner = new Vector3(chunkSize, 0f, chunkSize);
            var byLevel = new int[levels];
            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dz = -radius; dz <= radius; dz++)
                {
                    float distance = DistanceToChunk(new Vector2Int(1 + dx, 1 + dz), corner);
                    if (distance <= reach)
                        byLevel[levelAt(distance)]++;
                }
            }
            return byLevel;
        }

        private Rect ChunkRect(Vector2Int coord) =>
            new Rect(coord.x * chunkSize, coord.y * chunkSize, chunkSize, chunkSize);

        private float DistanceToChunk(Vector2Int coord, Vector3 eye)
        {
            float x = Mathf.Clamp(eye.x, coord.x * chunkSize, (coord.x + 1) * chunkSize);
            float z = Mathf.Clamp(eye.z, coord.y * chunkSize, (coord.y + 1) * chunkSize);
            return Vector2.Distance(new Vector2(x, z), new Vector2(eye.x, eye.z));
        }
    }
}
