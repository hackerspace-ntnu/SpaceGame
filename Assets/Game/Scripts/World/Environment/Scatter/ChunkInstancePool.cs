using System;
using System.Collections.Generic;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceGame.World.Environment
{
    /// <summary>
    /// The instances of many chunks in one GPU buffer: a slot per chunk, every slot the same size.
    /// A grass biome keeps one pool per density level, so a distant chunk stored at a sixteenth of
    /// its tufts takes a sixteenth of the memory. The pool keeps a CPU copy of every slot so it
    /// can double without re-scattering anything, and so rock colliders can be read back. It also
    /// records when each slot was filled, so a cull can grow a chunk in that lands in view.
    /// </summary>
    /// <typeparam name="T">The stored instance; its layout is a contract with the cull kernel.</typeparam>
    public sealed class ChunkInstancePool<T> : IDisposable where T : unmanaged
    {
        /// <summary>[numthreads] of every Cull kernel that reads a pool.</summary>
        public const int CullGroupSize = 64;

        private static readonly int InstancesId = Shader.PropertyToID("_Instances");
        private static readonly int SlotCountsId = Shader.PropertyToID("_SlotCounts");
        private static readonly int SlotCapacityId = Shader.PropertyToID("_SlotCapacity");
        private static readonly int SlotFilledId = Shader.PropertyToID("_SlotFilled");
        private static readonly int ScatterClockId = Shader.PropertyToID("_ScatterClock");

        private readonly int initialSlots;
        private readonly Stack<int> free = new Stack<int>();
        private T[][] contents = Array.Empty<T[]>();
        private uint[] counts = Array.Empty<uint>();
        private float[] filled = Array.Empty<float>();
        private GraphicsBuffer instances;
        private GraphicsBuffer countBuffer;
        private GraphicsBuffer filledBuffer;
        private bool countsChanged;

        /// <param name="initialSlots">How many slots the first allocation makes: what one camera needs.</param>
        public ChunkInstancePool(int slotCapacity, int initialSlots)
        {
            SlotCapacity = slotCapacity;
            this.initialSlots = Mathf.Max(1, initialSlots);
        }

        public int SlotCapacity { get; }
        public int Resident { get; private set; }
        public bool HasFreeSlot => free.Count > 0;
        public bool IsAllocated => counts.Length > 0;

        /// <summary>The CPU copy of one slot's instances.</summary>
        public T[] Contents(int slot) => contents[slot];

        /// <summary>Stores a chunk's instances in a free slot, allocating the pool on first use.</summary>
        public int Store(T[] chunk)
        {
            if (free.Count == 0)
                Grow();
            int slot = free.Pop();
            contents[slot] = chunk;
            counts[slot] = (uint)chunk.Length;
            filled[slot] = Clock;
            instances.SetData(chunk, 0, slot * SlotCapacity, chunk.Length);
            countsChanged = true;
            Resident++;
            return slot;
        }

        public void Release(int slot)
        {
            contents[slot] = null;
            counts[slot] = 0;
            free.Push(slot);
            countsChanged = true;
            Resident--;
        }

        /// <summary>
        /// The first call makes <c>initialSlots</c>; later ones (a second camera far away) double
        /// the pool, refilling the new buffer from the CPU copies.
        /// </summary>
        public void Grow()
        {
            int oldSlots = counts.Length;
            int newSlots = oldSlots == 0 ? initialSlots : oldSlots * 2;
            Array.Resize(ref contents, newSlots);
            Array.Resize(ref counts, newSlots);
            Array.Resize(ref filled, newSlots);

            instances?.Release();
            instances = new GraphicsBuffer(GraphicsBuffer.Target.Structured, newSlots * SlotCapacity, UnsafeUtility.SizeOf<T>());
            for (int slot = 0; slot < oldSlots; slot++)
                if (contents[slot] != null)
                    instances.SetData(contents[slot], 0, slot * SlotCapacity, contents[slot].Length);

            countBuffer?.Release();
            countBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, newSlots, sizeof(uint));
            filledBuffer?.Release();
            filledBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, newSlots, sizeof(float));
            for (int slot = newSlots - 1; slot >= oldSlots; slot--)
                free.Push(slot);
            countsChanged = true;
        }

        /// <summary>
        /// Records one Cull dispatch over every slot; the shared cull settings are set by the
        /// caller. The kernel may read <c>_SlotFilled</c>, when each slot was filled, against
        /// <c>_ScatterClock</c>, the time now.
        /// </summary>
        public void Cull(CommandBuffer commands, ComputeShader shader, int kernel)
        {
            if (Resident == 0)
                return;
            if (countsChanged)
            {
                countBuffer.SetData(counts);
                filledBuffer.SetData(filled);
                countsChanged = false;
            }
            commands.SetComputeBufferParam(shader, kernel, InstancesId, instances);
            commands.SetComputeBufferParam(shader, kernel, SlotCountsId, countBuffer);
            commands.SetComputeBufferParam(shader, kernel, SlotFilledId, filledBuffer);
            commands.SetComputeFloatParam(shader, ScatterClockId, Clock);
            commands.SetComputeIntParam(shader, SlotCapacityId, SlotCapacity);
            int groups = (SlotCapacity + CullGroupSize - 1) / CullGroupSize;
            commands.DispatchCompute(shader, kernel, groups, counts.Length, 1);
        }

        // Wall time, which runs in edit mode too; only differences of it reach the GPU.
        private static float Clock => Time.realtimeSinceStartup;

        public void Dispose()
        {
            instances?.Release();
            countBuffer?.Release();
            filledBuffer?.Release();
            instances = countBuffer = filledBuffer = null;
        }
    }
}
