// Copyright (c) 2010-2015 SharpDX - Alexandre Mutel
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using SharpDX;
using SharpDX.Direct3D;
using SharpDX.DXGI;

namespace HelloTriangle
{
    using SharpDX.Direct3D12;

    /// <summary>
    /// HelloTriangleD3D12 sample ported from Direct3D12 SDK
    /// </summary>
    public class HelloTriangle : IDisposable
    {
        private const int SwapBufferCount = 2;
        private int width;
        private int height;
        private Device device;
        private CommandAllocator commandListAllocator;
        private CommandQueue commandQueue;
        private SwapChain swapChain;
        private RootSignature rootSignature;
        private Resource vertexBuffer;
        private VertexBufferView vertexBufferView;
        private PipelineState pipelineState;
        private DescriptorHeap descriptorHeap;
        private GraphicsCommandList commandList;
        private Resource renderTarget;
        private Rectangle scissorRectangle;
        private ViewportF viewPort;
        private AutoResetEvent eventHandle;
        private Fence fence;
        private long currentFence;
        private int indexLastSwapBuf;

        /// <summary>
        /// Initializes this instance.
        /// </summary>
        /// <param name="form">The form.</param>
        public void Initialize(Form form)
        {
            width = form.ClientSize.Width;
            height = form.ClientSize.Height;

            LoadPipeline(form);
            LoadAssets();
        }

        /// <summary>
        /// Updates this instance.
        /// </summary>
        public void Update()
        {
        }

        /// <summary>
        /// Render scene
        /// </summary>
        public void Render()
        {
            // record all the commands we need to render the scene into the command list
            PopulateCommandLists();

            // execute the command list
            commandQueue.ExecuteCommandList(commandList);

            // swap the back and front buffers
            swapChain.Present(1, 0);
            indexLastSwapBuf = (indexLastSwapBuf + 1) % SwapBufferCount;
            Utilities.Dispose(ref renderTarget);
            renderTarget = swapChain.GetBackBuffer<Resource>(indexLastSwapBuf);
            device.CreateRenderTargetView(renderTarget, null, descriptorHeap.CPUDescriptorHandleForHeapStart);

            // wait and reset EVERYTHING
            WaitForPrevFrame();
        }

        /// <summary>
        /// Cleanup allocations
        /// </summary>
        public void Dispose()
        {
            // wait for the GPU to be done with all resources
            WaitForPrevFrame();

            swapChain.SetFullscreenState(false, null);
            
            eventHandle.Close();

            // asset objects
            Utilities.Dispose(ref pipelineState);
            Utilities.Dispose(ref commandList);
            Utilities.Dispose(ref vertexBuffer);

            // pipeline objects
            Utilities.Dispose(ref descriptorHeap);
            Utilities.Dispose(ref renderTarget);
            Utilities.Dispose(ref rootSignature);
            Utilities.Dispose(ref commandListAllocator);
            Utilities.Dispose(ref commandQueue);
            Utilities.Dispose(ref device);
            Utilities.Dispose(ref swapChain);
        }

        /// <summary>
        /// Loads the rendering pipeline dependencies. This includes but is not limited to:
        ///		- device creation
        ///		- swapchain
        /// </summary>
        /// <param name="form">The form.</param>
        private void LoadPipeline(Form form)
        {
            // create swap chain descriptor
            var swapChainDescription = new SwapChainDescription()
            {
                BufferCount = SwapBufferCount,
                ModeDescription = new ModeDescription(Format.R8G8B8A8_UNorm),
                Usage = Usage.RenderTargetOutput,
                OutputHandle = form.Handle,
                SwapEffect = SwapEffect.FlipSequential,
                SampleDescription = new SampleDescription(1, 0),
                IsWindowed = true
            };

            // create the device
            try
            {
                device = CreateDeviceWithSwapChain(DriverType.Hardware, DeviceCreationFlags.Debug, FeatureLevel.Level_9_1, swapChainDescription, out swapChain, out commandQueue);
            }
            catch(SharpDXException)
            {
                device = CreateDeviceWithSwapChain(DriverType.Warp, DeviceCreationFlags.None, FeatureLevel.Level_9_1, swapChainDescription, out swapChain, out commandQueue);
            }

            // create command queue and allocator objects
            commandListAllocator = device.CreateCommandAllocator(CommandListType.Direct);
        }

        /// <summary>
        /// Load the program assets
        /// This includes but is not limited to:
        ///		- shaders
        ///		- input layouts
        ///		- rasterizer state
        ///		- blend state
        ///		- pipeline state objects (PSOs)
        ///		- viewport
        ///		- descriptor heap
        ///		- render target
        ///		- vert/index buffers
        ///		- command lists
        /// </summary>
        private void LoadAssets()
        {
            // compile shaders
            var vertexShaderBytecode = SharpDX.D3DCompiler.ShaderBytecode.CompileFromFile("shaders.hlsl", "VShader", "vs_5_0");
            var pixelShaderBytecode = SharpDX.D3DCompiler.ShaderBytecode.CompileFromFile("shaders.hlsl", "PShader", "ps_5_0");
            
            // create input layout
            var layout = new[]
            {
                new InputElement("POSITION", 0, Format.R32G32B32_Float, 0),
                new InputElement("COLOR", 0, Format.R32G32B32A32_Float, 0),
            };

            // create an empty root signature
            var description = new RootSignatureDescription(RootSignatureFlags.AllowInputAssemblerInputLayout);
            using (var blob = description.Serialize())
                rootSignature = device.CreateRootSignature(blob);

            // create a PSO description
            var pipelineDesc = new GraphicsPipelineStateDescription()
                               {
                                   InputLayout = layout,
                                   RootSignature = rootSignature,
                                   VertexShader = vertexShaderBytecode.Bytecode.Data,
                                   PixelShader = pixelShaderBytecode.Bytecode.Data,
                                   RasterizerState = RasterizerStateDescription.Default(),
                                   BlendState = BlendStateDescription.Default(),
                                   DepthStencilState = DepthStencilStateDescription.Default(),
                                   SampleMask = -1,
                                   PrimitiveTopologyType = PrimitiveTopologyType.Triangle,
                                   RenderTargetCount = 1,
                                   SampleDescription = new SampleDescription(1, 0),
                               };
            pipelineDesc.DepthStencilState.IsDepthEnabled = false;
            pipelineDesc.DepthStencilState.IsStencilEnabled = false;
            pipelineDesc.RenderTargetFormats[0] = Format.R8G8B8A8_UNorm;

            // create the actual PSO
            pipelineState = device.CreateGraphicsPipelineState(pipelineDesc);

            // create descriptor heap
            descriptorHeap = device.CreateDescriptorHeap(new DescriptorHeapDescription()
            {
                Type = DescriptorHeapType.RenderTargetView,
                DescriptorCount = 1
            });

            // create command list
            commandList = device.CreateCommandList(CommandListType.Direct, commandListAllocator, pipelineState);

            // create backbuffer/rendertarget
            renderTarget = swapChain.GetBackBuffer<Resource>(0);
            device.CreateRenderTargetView(renderTarget, null, descriptorHeap.CPUDescriptorHandleForHeapStart);

            // set the viewport
            viewPort = new ViewportF(0, 0, width, height);

            // create scissor rectangle
            scissorRectangle = new Rectangle(0, 0, width, height);

            // create geometry for a triangle
            var triangleVerts = new []
            {
                new Vertex(0.0f, 0.5f, 0.0f, 1.0f, 0.0f, 0.0f, 1.0f),
                new Vertex(0.45f, -0.5f, 0.0f, 0.0f, 1.0f, 0.0f, 1.0f),
                new Vertex(-0.45f, -0.5f, 0.0f, 0.0f, 0.0f, 1.0f, 1.0f),
            };

            // actually create the vert buffer
            // Note: using upload heaps to transfer static data like vert buffers is not recommended.  Every time the GPU needs it, the upload heap will be marshalled over.  Please read up on Default Heap usage.  An upload heap is used here for code simplicity and because there are very few verts to actually transfer
            vertexBuffer = device.CreateCommittedResource(new HeapProperties(HeapType.Upload), HeapMiscFlags.None, ResourceDescription.Buffer(Utilities.SizeOf<Vertex>() * triangleVerts.Length), ResourceUsage.GenericRead);

            // copy the triangle data to the vertex buffer
            var ptr = vertexBuffer.Map(0);
            Utilities.Write(ptr, triangleVerts, 0, triangleVerts.Length);
            vertexBuffer.Unmap(0);

            // create vertex buffer view
            vertexBufferView = new VertexBufferView()
                                          {
                                              BufferLocation = vertexBuffer.GPUVirtualAddress,
                                              StrideInBytes = Utilities.SizeOf<Vertex>(),
                                              SizeInBytes = Utilities.SizeOf<Vertex>() * triangleVerts.Length,
                                          };

            // create fencing object
            fence = device.CreateFence(0, FenceMiscFlags.None);
            currentFence = 1;

            // close the command list and use it to execute the initial GPU setup
            commandList.Close();
            commandQueue.ExecuteCommandList(commandList);

            // create event handle
            eventHandle = new AutoResetEvent(false);

            // wait for the command list to execute; we are reusing the same command list in our main loop but for now, we just want to wait for setup to complete before continuing
            WaitForPrevFrame();
        }

        /// Fill the command list with all the render commands and dependent state
        private void PopulateCommandLists()
        {
	        // command list allocators can be only be reset when the associated command lists have finished execution on the GPU; apps should use fences to determine GPU execution progress
            commandListAllocator.Reset();

            // HOWEVER, when ExecuteCommandList() is called on a particular command list, that command list can then be reset anytime and must be before rerecording
            commandList.Reset(commandListAllocator, pipelineState);

            // set the graphics root signature
            commandList.SetGraphicsRootSignature(rootSignature);

            // set the viewport and scissor rectangle
            commandList.SetViewport(viewPort);
            commandList.SetScissorRectangles(scissorRectangle);

	        // indicate this resource will be in use as a render target
	        commandList.ResourceBarrierTransition(renderTarget, ResourceUsage.Present, ResourceUsage.RenderTarget);

	        // record commands
	        var clearColor = new Color4(0.0f, 0.2f, 0.4f, 1.0f);
	        commandList.ClearRenderTargetView(descriptorHeap.CPUDescriptorHandleForHeapStart, clearColor, null, 0); // TODO rework this API call
	        commandList.SetRenderTargets(descriptorHeap.CPUDescriptorHandleForHeapStart, true, 1, null);
	        commandList.PrimitiveTopology = PrimitiveTopology.TriangleList;
	        commandList.SetVertexBuffers(0, new [] { vertexBufferView }, 1); // TODO remove alloc
	        commandList.DrawInstanced(3, 1, 0, 0);

	        // indicate that the render target will now be used to present when the command list is done executing
	        commandList.ResourceBarrierTransition(renderTarget, ResourceUsage.RenderTarget, ResourceUsage.Present);

	        // all we need to do now is execute the command list
            commandList.Close();
        }

        /// Let the previous frame finish before continuing
        private void WaitForPrevFrame()
        {
	        // WAITING FOR THE FRAME TO COMPLETE BEFORE CONTINUING IS NOT BEST PRACTICE.
	        // This is code implemented as such for simplicity.
	        // More advanced topics such as using fences for efficient resource usage.
            long localFence = currentFence;
            commandQueue.Signal(fence, localFence);
            currentFence++;

            if (fence.CompletedValue < localFence)
            {                
                fence.SetEventOnCompletion(localFence, eventHandle.SafeWaitHandle.DangerousGetHandle());
                eventHandle.WaitOne();
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Vertex
        {
            public Vertex(Vector3 position, Color4 color)
            {
                Position = position;
                Color = color;
            }

            public Vertex(float x, float y, float z, float r, float g, float b, float a)
            {
                Position = new Vector3(x, y, z);
                Color = new Color4(r, g, b, a);
            }

            public Vector3 Position;

            public Color4 Color;
        }

        private static Device CreateDeviceWithSwapChain(DriverType driverType,
            DeviceCreationFlags flags,
            FeatureLevel level,
            SwapChainDescription swapChainDescription,
            out SwapChain swapChain, out CommandQueue queue)
        {
            var device = new Device(driverType, flags, level);
            queue = device.CreateCommandQueue(new CommandQueueDescription(CommandListType.Direct));

            using (var factory = new Factory1())
            {
                swapChain = new SwapChain(factory, queue, swapChainDescription);
            }
            return device;
        }
    }
}