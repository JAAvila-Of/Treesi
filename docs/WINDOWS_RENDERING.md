# Windows rendering fallback

Treesi keeps Avalonia's default rendering selection when Windows reports a hardware DXGI adapter or when adapter detection is unavailable. On a successful enumeration containing only software adapters (or no adapters), it selects Avalonia's direct software renderer instead of sending GPU work through a CPU-emulated Direct3D device.

This is a product startup policy, independent of tests, repository content and environment flags. It does not disable the caret, change motion preferences or alter themes. Linux and macOS keep their platform defaults. Adapter detection is read-only, bounded to 32 entries and uses the documented `DXGI_ADAPTER_FLAG_SOFTWARE` bit, not adapter-name heuristics. COM interfaces are released; failures preserve Avalonia's normal compatibility path. The descriptor ABI is checked without calling native APIs in the headless suite.

Native stacks in the isolated Windows VM identified `d3d10warp.dll!PixelJITProcessor::ExecuteJIT` and triangle-rasterization work on the five worker threads responsible for most focused-input CPU. [WARP](https://learn.microsoft.com/en-us/windows/win32/direct3darticles/directx-warp) is Microsoft's software Direct3D rasterizer. This motivates comparing direct CPU rendering for hosts with no hardware adapter. A shared VM does not establish performance or visual acceptance on physical GPUs; native UI tests and process measurements remain necessary.

References: [DXGI adapter flags](https://learn.microsoft.com/en-us/windows/win32/api/dxgi/ne-dxgi-dxgi_adapter_flag), [adapter enumeration](https://learn.microsoft.com/en-us/windows/win32/api/dxgi/nf-dxgi-idxgifactory1-enumadapters1), [descriptor layout](https://learn.microsoft.com/en-us/windows/win32/api/dxgi/ns-dxgi-dxgi_adapter_desc1).
