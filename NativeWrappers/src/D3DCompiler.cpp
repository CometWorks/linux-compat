#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <stdexcept>

#include "dll_loader.h"

static void EnsureThreadInfo()
{
    if (!setup_nt_threadinfo(nullptr)) {
        fprintf(stderr, "D3DCompiler: Failed to initialize thread info\n");
        std::abort();
    }
    pe_ensure_tls_for_loaded_images();
}

static pe_image g_d3dcompiler_image;

// D3D_SHADER_MACRO layout (Windows): two pointers (Name, Definition), null-terminated array
struct D3D_SHADER_MACRO {
    const char *Name;
    const char *Definition;
};

// Windows x64 ABI function pointer for D3DCompile
typedef WINAPI int32_t (*pfnD3DCompile)(
    const void *pSrcData, uint64_t SrcDataSize,
    const char *pSourceName, const D3D_SHADER_MACRO *pDefines, void *pInclude,
    const char *pEntrypoint, const char *pTarget,
    uint32_t Flags1, uint32_t Flags2,
    void **ppCode, void **ppErrorMsgs);

// ID3DBlob method types (Windows x64 ABI - first arg is 'this')
typedef WINAPI void* (*pfnBlobGetBufferPointer)(void *pThis);
typedef WINAPI uint64_t (*pfnBlobGetBufferSize)(void *pThis);
typedef WINAPI uint32_t (*pfnBlobRelease)(void *pThis);

static pfnD3DCompile s_D3DCompile = nullptr;

// Helper: read vtable slot from a COM object
static inline void* vtable_slot(void *obj, int index)
{
    void **vtable = *(void***)obj;
    return vtable[index];
}

extern "C" {

void Init(const char* dllPath)
{
    if (!load_dll(&g_d3dcompiler_image, dllPath)) {
        fprintf(stderr, "D3DCompiler: Failed to load %s\n", dllPath);
        throw std::runtime_error("Failed to load d3dcompiler_47.dll");
    }

    s_D3DCompile = (pfnD3DCompile)get_export("D3DCompile");

    if (!s_D3DCompile) {
        fprintf(stderr, "D3DCompiler: D3DCompile export not found\n");
        throw std::runtime_error("D3DCompile export not found");
    }
}

int32_t SE_D3DCompile(
    const void *pSrcData, uint64_t SrcDataSize,
    const char *pSourceName, const D3D_SHADER_MACRO *pDefines, void *pInclude,
    const char *pEntrypoint, const char *pTarget,
    uint32_t Flags1, uint32_t Flags2,
    void **ppCode, void **ppErrorMsgs)
{
    EnsureThreadInfo();
    if (!s_D3DCompile) {
        fprintf(stderr, "D3DCompiler: D3DCompile not initialized\n");
        return -1;
    }
    return s_D3DCompile(pSrcData, SrcDataSize, pSourceName, pDefines, pInclude,
                        pEntrypoint, pTarget, Flags1, Flags2, ppCode, ppErrorMsgs);
}

void* SE_BlobGetBufferPointer(void *blob)
{
    EnsureThreadInfo();
    auto fn = (pfnBlobGetBufferPointer)vtable_slot(blob, 3);
    return fn(blob);
}

uint64_t SE_BlobGetBufferSize(void *blob)
{
    EnsureThreadInfo();
    auto fn = (pfnBlobGetBufferSize)vtable_slot(blob, 4);
    return fn(blob);
}

uint32_t SE_BlobRelease(void *blob)
{
    EnsureThreadInfo();
    auto fn = (pfnBlobRelease)vtable_slot(blob, 2);
    return fn(blob);
}

} // extern "C"
