﻿using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Text;
using Veldrid;
using StudioCore.Memory;
using System.Collections.Concurrent;
using System.Threading;
using Vortice.Vulkan;

namespace StudioCore.Scene
{
    public class GPUBufferAllocator
    {
        private long _bufferSize = 0;

        public long BufferSize { get => _bufferSize; }

        private object _allocationLock = new object();

        private List<GPUBufferHandle> _allocations = new List<GPUBufferHandle>();

        private DeviceBuffer _stagingBuffer = null;
        public DeviceBuffer _backingBuffer { get; private set; } = null;

        private ResourceLayout _bufferLayout = null;
        private ResourceSet _bufferResourceSet = null;

        private FreeListAllocator _allocator = null;

        private VkAccessFlags2 _dstAccessFlags = VkAccessFlags2.None;

        public GPUBufferAllocator(uint initialSize, VkBufferUsageFlags usage)
        {
            BufferDescription desc = new BufferDescription(
                initialSize,
                usage,
                VmaMemoryUsage.AutoPreferDevice,
                0);
            _backingBuffer = Renderer.Factory.CreateBuffer(desc);
            desc = new BufferDescription(
                initialSize,
                VkBufferUsageFlags.None,
                VmaMemoryUsage.AutoPreferHost,
                VmaAllocationCreateFlags.Mapped);
            _stagingBuffer = Renderer.Factory.CreateBuffer(desc);
            _bufferSize = initialSize;
            _allocator = new FreeListAllocator(initialSize);
            _dstAccessFlags = Util.AccessFlagsFromBufferUsageFlags(usage);
        }

        public GPUBufferAllocator(uint initialSize, VkBufferUsageFlags usage, uint stride)
        {
            BufferDescription desc = new BufferDescription(
                initialSize,
                usage,
                VmaMemoryUsage.AutoPreferDevice,
                0,
                stride);
            _backingBuffer = Renderer.Factory.CreateBuffer(desc);
            desc = new BufferDescription(
                initialSize,
                VkBufferUsageFlags.None,
                VmaMemoryUsage.AutoPreferHost,
                VmaAllocationCreateFlags.Mapped);
            _stagingBuffer = Renderer.Factory.CreateBuffer(desc);
            _bufferSize = initialSize;
            _allocator = new FreeListAllocator(initialSize);
            _dstAccessFlags = Util.AccessFlagsFromBufferUsageFlags(usage);
        }

        public GPUBufferAllocator(string name, uint initialSize, VkBufferUsageFlags usage, uint stride, VkShaderStageFlags stages)
        {
            BufferDescription desc = new BufferDescription(
                initialSize,
                usage,
                VmaMemoryUsage.AutoPreferDevice,
                0,
                stride);
            _backingBuffer = Renderer.Factory.CreateBuffer(desc);
            _bufferSize = initialSize;
            desc = new BufferDescription(
                initialSize,
                VkBufferUsageFlags.None,
                VmaMemoryUsage.AutoPreferHost,
                VmaAllocationCreateFlags.Mapped);
            _stagingBuffer = Renderer.Factory.CreateBuffer(desc);
            _allocator = new FreeListAllocator(initialSize);
            _dstAccessFlags = Util.AccessFlagsFromBufferUsageFlags(usage);

            var layoutdesc = new ResourceLayoutDescription(
                new ResourceLayoutElementDescription(name, VkDescriptorType.StorageBuffer, stages));
            _bufferLayout = Renderer.Factory.CreateResourceLayout(layoutdesc);
            var rsdesc = new ResourceSetDescription(_bufferLayout, _backingBuffer);
            _bufferResourceSet = Renderer.Factory.CreateResourceSet(rsdesc);
        }

        public GPUBufferHandle Allocate(uint size, int alignment)
        {
            GPUBufferHandle handle;
            lock (_allocationLock)
            {
                //if ((_allocatedbytes % alignment) != 0)
                //{
                //    _allocatedbytes += (alignment - (_allocatedbytes % alignment));
                //}
                uint addr;
                if (!_allocator.AlignedAlloc(size, (uint)alignment, out addr))
                {
                    throw new Exception($"GPU allocation failed. Try increasing buffer sizes in settings. Otherwise, Download more RAM 4head");
                }
                handle = new GPUBufferHandle(this, addr, size);
                _allocations.Add(handle);
            }
            return handle;
        }

        private void Free(uint addr)
        {
            lock (_allocationLock)
            {
                _allocator.Free(addr);
            }
        }

        public void BindAsVertexBuffer(CommandList cl)
        {
            cl.SetVertexBuffer(0, _backingBuffer);
        }

        public void BindAsIndexBuffer(CommandList cl, VkIndexType indexformat)
        {
            cl.SetIndexBuffer(_backingBuffer, indexformat);
        }

        public ResourceLayout GetLayout()
        {
            return _bufferLayout;
        }

        public void BindAsResourceSet(CommandList cl, uint slot)
        {
            if (_bufferResourceSet != null)
            {
                cl.SetGraphicsResourceSet(slot, _bufferResourceSet);
            }
        }

        public class GPUBufferHandle : IDisposable
        {
            private GPUBufferAllocator _allocator;
            private bool disposedValue;

            public uint AllocationStart { get; private set; }
            public uint AllocationSize { get; private set; }

            public GPUBufferHandle(GPUBufferAllocator alloc, uint start, uint size)
            {
                _allocator = alloc;
                AllocationStart = start;
                AllocationSize = size;
            }

            public void FillBuffer<T>(T[] data, Action completionHandler=null) where T : struct
            {
                Renderer.AddBackgroundUploadTask((device, cl) =>
                {
                    device.UpdateBuffer(_allocator._stagingBuffer, AllocationStart, data);
                    cl.CopyBuffer(_allocator._stagingBuffer, AllocationStart, _allocator._backingBuffer, AllocationStart, AllocationSize);
                    if (completionHandler != null)
                    {
                        completionHandler.Invoke();
                    }
                });
            }

            public void FillBuffer<T>(CommandList cl, T[] data) where T : struct
            {
                cl.UpdateBuffer(_allocator._backingBuffer, AllocationStart, data);
            }

            public void FillBuffer<T>(T data, Action completionHandler = null) where T : struct
            {
                Renderer.AddBackgroundUploadTask((device, cl) =>
                {
                    cl.UpdateBuffer(_allocator._backingBuffer, AllocationStart, ref data);
                    if (completionHandler != null)
                    {
                        completionHandler.Invoke();
                    }
                });
            }

            public void FillBuffer<T>(GraphicsDevice d, CommandList cl, ref T data) where T : struct
            {
                d.UpdateBuffer(_allocator._stagingBuffer, AllocationStart, data);
                cl.CopyBuffer(_allocator._stagingBuffer, 
                    AllocationStart, 
                    _allocator._backingBuffer, 
                    AllocationStart, 
                    AllocationSize);
                cl.BufferBarrier(_allocator._backingBuffer,
                    VkPipelineStageFlags2.Transfer,
                    VkAccessFlags2.TransferWrite,
                    VkPipelineStageFlags2.AllGraphics,
                    _allocator._dstAccessFlags);
            }

            public void FillBuffer(IntPtr data, uint size, Action completionHandler)
            {
                Renderer.AddBackgroundUploadTask((device, cl) =>
                {
                    cl.UpdateBuffer(_allocator._backingBuffer, AllocationStart, data, size);
                    completionHandler.Invoke();
                });
            }

            protected virtual void Dispose(bool disposing)
            {
                if (!disposedValue)
                {
                    if (disposing)
                    {
                        // TODO: dispose managed state (managed objects)
                    }

                    _allocator.Free(AllocationStart);
                    disposedValue = true;
                }
            }

            // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
            ~GPUBufferHandle()
            {
                Dispose(disposing: false);
            }

            public void Dispose()
            {
                Dispose(disposing: true);
                GC.SuppressFinalize(this);
            }
        }
    }

    /// <summary>
    /// Allocator for vertex/index buffers. Maintains a set of smaller megabuffers
    /// and tries to batch allocations together behind the scenes.
    /// </summary>
    public class VertexIndexBufferAllocator
    {
        private object _allocationLock = new object();

        private List<VertexIndexBufferHandle> _allocations = new List<VertexIndexBufferHandle>();
        private List<VertexIndexBuffer> _buffers = new List<VertexIndexBuffer>();

        private uint _maxVertsSize;
        private uint _maxIndicesSize;

        private VertexIndexBuffer _currentStaging;

        private ConcurrentQueue<VertexIndexBuffer> _pendingUpload = new ConcurrentQueue<VertexIndexBuffer>();

        private bool _stagingLocked = false;
        private bool _pendingFlush = false;

        private GraphicsDevice _device = null;

        public long TotalVertexFootprint
        {
            get
            {
                long total = 0;
                foreach (var a in _buffers)
                {
                    if (a != null)
                    {
                        total += a._bufferSizeVert;
                    }
                }
                return total;
            }
        }

        public long TotalIndexFootprint
        {
            get
            {
                long total = 0;
                foreach (var a in _buffers)
                {
                    if (a != null)
                    {
                        total += a._bufferSizeIndex;
                    }
                }
                return total;
            }
        }

        public VertexIndexBufferAllocator(GraphicsDevice gd, uint maxVertsSize, uint maxIndicesSize)
        {
            _device = gd;
            BufferDescription desc = new BufferDescription(
                maxVertsSize,
                VkBufferUsageFlags.None,
                VmaMemoryUsage.AutoPreferHost,
                VmaAllocationCreateFlags.Mapped);
            _currentStaging = new VertexIndexBuffer(_device);
            _currentStaging._stagingBufferVerts = Renderer.Factory.CreateBuffer(desc);
            _currentStaging._mappedStagingBufferVerts = 
                _device.Map(_currentStaging._stagingBufferVerts, MapMode.Write);
            desc = new BufferDescription(
                maxIndicesSize,
                VkBufferUsageFlags.None,
                VmaMemoryUsage.AutoPreferHost,
                VmaAllocationCreateFlags.Mapped);
            _currentStaging._stagingBufferIndices = Renderer.Factory.CreateBuffer(desc);
            _currentStaging._mappedStagingBufferIndices =
                _device.Map(_currentStaging._stagingBufferIndices, MapMode.Write);
            _maxVertsSize = maxVertsSize;
            _maxIndicesSize = maxIndicesSize;
            _currentStaging.BufferIndex = 0;
            _buffers.Add(_currentStaging);
        }

        public VertexIndexBufferHandle Allocate(uint vsize, uint isize, int valignment, int ialignment, Action<VertexIndexBufferHandle> onStaging=null)
        {
            VertexIndexBufferHandle handle;
            bool needsFlush = false;
            lock (_allocationLock)
            {
                long val = 0;
                long ial = 0;
                if ((_currentStaging._stagingVertsSize % valignment) != 0)
                {
                    val += (valignment - (_currentStaging._stagingVertsSize % valignment));
                }
                if ((_currentStaging._stagingIndicesSize % ialignment) != 0)
                {
                    ial += (ialignment - (_currentStaging._stagingIndicesSize % ialignment));
                }
                
                if ((_currentStaging._stagingVertsSize + vsize + val) > _maxVertsSize || (_currentStaging._stagingIndicesSize + isize + ial) > _maxIndicesSize)
                {
                    // Buffer won't fit in current megabuffer. Create a new one while the current one is still staging
                    _currentStaging._allocationsFull = true;
                    _currentStaging.FlushIfNeeded();

                    _currentStaging = new VertexIndexBuffer(_device);
                    _currentStaging.BufferIndex = _buffers.Count;
                    _buffers.Add(_currentStaging);
                    BufferDescription desc = new BufferDescription(
                        _maxVertsSize,
                        VkBufferUsageFlags.None,
                        VmaMemoryUsage.AutoPreferHost,
                        VmaAllocationCreateFlags.Mapped);
                    _currentStaging._stagingBufferVerts = Renderer.Factory.CreateBuffer(desc);
                    _currentStaging._mappedStagingBufferVerts = 
                        _device.Map(_currentStaging._stagingBufferVerts, MapMode.Write);
                    desc = new BufferDescription(
                        _maxIndicesSize,
                        VkBufferUsageFlags.None,
                        VmaMemoryUsage.AutoPreferHost,
                        VmaAllocationCreateFlags.Mapped);
                    _currentStaging._stagingBufferIndices = Renderer.Factory.CreateBuffer(desc);
                    _currentStaging._mappedStagingBufferIndices = 
                        _device.Map(_currentStaging._stagingBufferIndices, MapMode.Write);

                    // Add to currently staging megabuffer
                    handle = new VertexIndexBufferHandle(this, _currentStaging, (uint)(_currentStaging._stagingVertsSize), (uint)vsize, (uint)(_currentStaging._stagingIndicesSize), (uint)isize);
                    _currentStaging._stagingVertsSize += vsize;
                    _currentStaging._stagingIndicesSize += isize;
                    _allocations.Add(handle);
                    if (onStaging != null)
                    {
                        onStaging.Invoke(handle);
                    }
                }
                else
                {
                    // Add to currently staging megabuffer
                    handle = new VertexIndexBufferHandle(this, _currentStaging, (uint)(_currentStaging._stagingVertsSize + val), (uint)vsize, (uint)(_currentStaging._stagingIndicesSize + ial), (uint)isize);
                    _currentStaging._stagingVertsSize += (vsize + val);
                    _currentStaging._stagingIndicesSize += (isize + ial);
                    _allocations.Add(handle);
                    if (onStaging != null)
                    {
                        onStaging.Invoke(handle);
                    }
                }
                _currentStaging._handleCount++;
            }

            if (needsFlush)
            {
                FlushStaging();
            }
            return handle;
        }

        public bool HasStagingOrPending()
        {
            if (_currentStaging._stagingVertsSize > 0)
            {
                return true;
            }
            return false;
        }

        public void FlushStaging(bool full = false)
        {
            lock (_allocationLock)
            {
                _currentStaging._allocationsFull = true;
                _currentStaging.FlushIfNeeded();

                _currentStaging = new VertexIndexBuffer(_device);
                _currentStaging.BufferIndex = _buffers.Count;
                _buffers.Add(_currentStaging);
                BufferDescription desc = new BufferDescription(
                    _maxVertsSize,
                    VkBufferUsageFlags.None,
                    VmaMemoryUsage.AutoPreferHost,
                    VmaAllocationCreateFlags.Mapped);
                _currentStaging._stagingBufferVerts = Renderer.Factory.CreateBuffer(desc);
                _currentStaging._mappedStagingBufferVerts = 
                    _device.Map(_currentStaging._stagingBufferVerts, MapMode.Write);
                desc = new BufferDescription(
                    _maxIndicesSize,
                    VkBufferUsageFlags.None,
                    VmaMemoryUsage.AutoPreferHost,
                    VmaAllocationCreateFlags.Mapped);
                _currentStaging._stagingBufferIndices = Renderer.Factory.CreateBuffer(desc);
                _currentStaging._mappedStagingBufferIndices = 
                    _device.Map(_currentStaging._stagingBufferIndices, MapMode.Write);
            }
        }

        public bool BindAsVertexBuffer(CommandList cl, int index)
        {
            if (_buffers[index] == null)
            {
                return false;
            }
            cl.SetVertexBuffer(0, _buffers[index]._backingVertBuffer);
            return true;
        }

        public bool BindAsIndexBuffer(CommandList cl, int index, VkIndexType indexformat)
        {
            if (_buffers[index] == null)
            {
                return false;
            }
            cl.SetIndexBuffer(_buffers[index]._backingIndexBuffer, indexformat);
            return true;
        }

        public class VertexIndexBuffer
        {
            public List<VertexIndexBufferHandle> _allocations = new List<VertexIndexBufferHandle>();

            public enum Status
            {
                /// <summary>
                /// The buffer is currently a staging buffer, and data will be
                /// copied into the staging buffer
                /// </summary>
                Staging,

                /// <summary>
                /// The buffer is currently being uploaded to the GPU, and cannot be mutated
                /// </summary>
                Uploading,

                /// <summary>
                /// The allocation is resident in GPU memory, and data cannot be uploaded anymore.
                /// The buffer is now usable for graphics purposes
                /// </summary>
                Resident,
            }

            public Status AllocStatus { get; internal set; }

            public int BufferIndex { get; internal set; } = -1;
            public long _bufferSizeVert = 0;
            public long _bufferSizeIndex = 0;

            internal int _handleCount = 0;
            internal int _vfillCount = 0;
            internal int _ifillCount = 0;
            internal bool _allocationsFull = false;
            internal bool _pendingUpload = false;
            
            internal int _flushLock = 0;

            internal FreeListAllocator _vertAllocator;
            internal FreeListAllocator _indexAllocator;

            public DeviceBuffer _stagingBufferVerts = null;
            public DeviceBuffer _stagingBufferIndices = null;
            public MappedResource _mappedStagingBufferVerts;
            public MappedResource _mappedStagingBufferIndices;
            public long _stagingVertsSize = 0;
            public long _stagingIndicesSize = 0;

            public DeviceBuffer _backingVertBuffer { get; internal set; } = null;
            public DeviceBuffer _backingIndexBuffer { get; internal set; } = null;

            internal GraphicsDevice _device;

            public VertexIndexBuffer(GraphicsDevice device)
            {
                _device = device;
                AllocStatus = Status.Staging;
            }

            internal void FlushIfNeeded()
            {
                if (_allocationsFull && _handleCount == _vfillCount && _handleCount == _ifillCount)
                {
                    // Ensure that only one thread is actually doing the flushing
                    if (Interlocked.CompareExchange(ref _flushLock, 1, 0) != 0)
                        return;
                    if (AllocStatus != Status.Staging)
                    {
                        throw new Exception("Error: FlushIfNeeded called on non-staging buffer");
                    }
                    AllocStatus = Status.Uploading;
                    Renderer.AddBackgroundUploadTask((d, cl) =>
                    {
                        var ctx = Tracy.TracyCZoneN(1, $@"Buffer flush {BufferIndex}, v: {_stagingVertsSize}, i: {_stagingIndicesSize}");
                        _bufferSizeVert = _stagingVertsSize;
                        _bufferSizeIndex = _stagingIndicesSize;
                        var vd = new BufferDescription(
                            (uint)_stagingVertsSize,
                            VkBufferUsageFlags.VertexBuffer,
                            VmaMemoryUsage.AutoPreferDevice,
                            0);
                        var id = new BufferDescription(
                            (uint)_stagingIndicesSize,
                            VkBufferUsageFlags.IndexBuffer,
                            VmaMemoryUsage.AutoPreferDevice,
                            0);
                        _backingVertBuffer = d.ResourceFactory.CreateBuffer(ref vd);
                        _backingIndexBuffer = d.ResourceFactory.CreateBuffer(ref id);
                        //cl.CopyBuffer(_stagingBufferVerts, 0, _backingVertBuffer, 0, (uint)_stagingVertsSize);
                        //cl.CopyBuffer(_stagingBufferIndices, 0, _backingIndexBuffer, 0, (uint)_stagingIndicesSize);
                        _device.Unmap(_stagingBufferVerts);
                        _device.Unmap(_stagingBufferIndices);
                        Renderer.AddAsyncTransfer(_backingVertBuffer, 
                            _stagingBufferVerts, 
                            VkAccessFlags2.VertexAttributeRead, 
                            (d) =>
                        {
                            var ctx2 = Tracy.TracyCZoneN(1, $@"Buffer {BufferIndex} V transfer done");
                            _stagingBufferVerts.Dispose();
                            _stagingBufferVerts = null;
                            Tracy.TracyCZoneEnd(ctx2);
                        });
                        Renderer.AddAsyncTransfer(_backingIndexBuffer, 
                            _stagingBufferIndices,
                            VkAccessFlags2.IndexRead,
                            (d) =>
                        {
                            var ctx2 = Tracy.TracyCZoneN(1, $@"Buffer {BufferIndex} I transfer done");
                            _stagingVertsSize = 0;
                            _stagingIndicesSize = 0;
                            AllocStatus = Status.Resident;
                            _stagingBufferIndices.Dispose();
                            _stagingBufferIndices = null;
                            Tracy.TracyCZoneEnd(ctx2);
                        });
                        Tracy.TracyCZoneEnd(ctx);
                    });
                    Interlocked.CompareExchange(ref _flushLock, 0, 1);
                }
            }
        }

        public class VertexIndexBufferHandle : IDisposable
        {
            private VertexIndexBufferAllocator _allocator;
            internal VertexIndexBuffer _buffer = null;

            internal Action<VertexIndexBufferHandle> _onStagedAction = null;

            public uint VAllocationStart { get; internal set; }
            public uint VAllocationSize { get; internal set; }
            public uint IAllocationStart { get; internal set; }
            public uint IAllocationSize { get; internal set; }

            internal int _valign;
            internal int _ialign;

            private bool _vfilled = false;
            private bool _ifilled = false;

            public VertexIndexBuffer.Status AllocStatus { get { return _buffer.AllocStatus; } }

            public int BufferIndex {
                get
                {
                    return (_buffer != null) ? _buffer.BufferIndex : -1;
                }
            }

            internal VertexIndexBufferHandle(VertexIndexBufferAllocator alloc, VertexIndexBuffer staging)
            {
                _allocator = alloc;
                _buffer = staging;
            }

            internal VertexIndexBufferHandle(VertexIndexBufferAllocator alloc, VertexIndexBuffer staging, uint vstart, uint vsize, uint istart, uint isize)
            {
                _allocator = alloc;
                _buffer = staging;
                VAllocationStart = vstart;
                VAllocationSize = vsize;
                IAllocationStart = istart;
                IAllocationSize = isize;
            }

            public void SetVFilled()
            {
                _vfilled = true;
                Interlocked.Increment(ref _buffer._vfillCount);
                _buffer.FlushIfNeeded();
            }

            public void SetIFilled()
            {
                _ifilled = true;
                Interlocked.Increment(ref _buffer._ifillCount);
                _buffer.FlushIfNeeded();
            }

            public void FillVBuffer<T>(T[] vdata, Action completionHandler = null) where T : struct
            {
                Renderer.AddLowPriorityBackgroundUploadTask((device, cl) =>
                {
                    if (_buffer == null)
                        return;
                    var ctx = Tracy.TracyCZoneN(1, $@"FillVBuffer");
                    if (_buffer.AllocStatus == VertexIndexBuffer.Status.Staging)
                    {
                        cl.UpdateBuffer(_buffer._stagingBufferVerts, VAllocationStart, vdata);
                    }
                    /*else if (AllocStatus == Status.Resident)
                    {
                        cl.UpdateBuffer(_buffer._backingVertBuffer, VAllocationStart, vdata);
                    }*/
                    else
                    {
                        throw new Exception("Attempt to copy data to non-staging buffer");
                    }
                    if (completionHandler != null)
                    {
                        completionHandler.Invoke();
                    }
                    SetVFilled();
                    Tracy.TracyCZoneEnd(ctx);
                });
            }

            public void FillIBuffer<T>(T[] idata, Action completionHandler = null) where T : struct
            {
                Renderer.AddLowPriorityBackgroundUploadTask((device, cl) =>
                {
                    // If the buffer is null when we get here, it's likely that this allocation was
                    // destroyed by the time the staging is happening.
                    if (_buffer == null)
                        return;
                    
                    var ctx = Tracy.TracyCZoneN(1, $@"FillIBuffer");
                    if (_buffer.AllocStatus == VertexIndexBuffer.Status.Staging)
                    {
                        cl.UpdateBuffer(_buffer._stagingBufferIndices, IAllocationStart, idata);
                    }
                    /*else if (AllocStatus == Status.Resident)
                    {
                        cl.UpdateBuffer(_buffer._backingIndexBuffer, IAllocationStart, idata);
                    }*/
                    else
                    {
                        throw new Exception("Attempt to copy data to non-staging buffer");
                    }
                    if (completionHandler != null)
                    {
                        completionHandler.Invoke();
                    }
                    SetIFilled();
                    Tracy.TracyCZoneEnd(ctx);
                });
            }

            public unsafe IntPtr MapVBuffer()
            {
                if (_buffer == null || _buffer.AllocStatus != VertexIndexBuffer.Status.Staging)
                {
                    throw new Exception("Attempt to map vertex buffer that isn't staging");
                }
                return new IntPtr((byte*)_buffer._mappedStagingBufferVerts.Data.ToPointer() + VAllocationStart);
            }

            public void UnmapVBuffer()
            {
                if (_buffer == null || _buffer.AllocStatus != VertexIndexBuffer.Status.Staging)
                {
                    throw new Exception("Attempt to unmap vertex buffer that isn't staging");
                }
                SetVFilled();
            }
            
            public unsafe IntPtr MapIBuffer()
            {
                if (_buffer == null || _buffer.AllocStatus != VertexIndexBuffer.Status.Staging)
                {
                    throw new Exception("Attempt to map index buffer that isn't staging");
                }
                return new IntPtr((byte*)_buffer._mappedStagingBufferIndices.Data.ToPointer() + IAllocationStart);
            }

            public void UnmapIBuffer()
            {
                if (_buffer == null || _buffer.AllocStatus != VertexIndexBuffer.Status.Staging)
                {
                    throw new Exception("Attempt to unmap index buffer that isn't staging");
                }
                SetIFilled();
            }

            #region IDisposable Support
            private bool disposedValue = false; // To detect redundant calls

            protected virtual void Dispose(bool disposing)
            {
                if (!disposedValue)
                {
                    if (_buffer != null)
                    {
                        _allocator._allocations.Remove(this);
                        _buffer._handleCount--;
                        if (_vfilled)
                            Interlocked.Decrement(ref _buffer._vfillCount);
                        if (_ifilled)
                            Interlocked.Decrement(ref _buffer._ifillCount);
                        if (_buffer._handleCount <= 0 && _buffer.AllocStatus == VertexIndexBuffer.Status.Resident)
                        {
                            _buffer._backingVertBuffer.Dispose();
                            _buffer._backingIndexBuffer.Dispose();
                            _allocator._buffers[_buffer.BufferIndex] = null;
                        }
                        _buffer = null;
                        _allocator = null;
                    }

                    disposedValue = true;
                }
            }

            ~VertexIndexBufferHandle()
            {
               Dispose(false);
            }

            // This code added to correctly implement the disposable pattern.
            public void Dispose()
            {
                Dispose(true);
                GC.SuppressFinalize(this);
            }
            #endregion
        }
    }
}
