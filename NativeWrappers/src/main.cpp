#include <cstdlib>
#include "support.h"
#include "dll_loader.h"

// RecastDetour API

typedef struct {
    float X;
    float Y;
    float Z;
} Vector3;

typedef struct {
    double X;
    double Y;
    double Z;
} Vector3D;

typedef WINAPI void* (*RecastDetour_Create_t)(void);
typedef WINAPI void (*RecastDetour_Destroy_t)(void* instance);
typedef WINAPI void (*RecastDetour_Init_t)(void* instance, float cellSize, float worldTileSize, float bMin[], float bMax[]);
typedef WINAPI void (*RecastDetour_Clear_t)(void* instance);
typedef WINAPI int (*RecastDetour_GetPath_t)(void* instance, Vector3 startPosition, Vector3 endPosition, void* destPath, int pathBufferSize);
typedef WINAPI int (*RecastDetour_GetNextPathPoint_t)(void* instance, Vector3 startPosition, Vector3 endPosition, Vector3* nextPoint);
typedef WINAPI int (*RecastDetour_CreateNavmeshTile_t)(void* instance, Vector3D pos, void* recastOptions, int tx, int ty, int tilelayer, void* vertices, int verticeCount, void* triangles, int triangleCount);
typedef WINAPI int (*RecastDetour_GetTilePolygonCount_t)(void* wrapper, Vector3D tilePosition, int tileLayer);
typedef WINAPI void (*RecastDetour_GetTilePolygonVertexCounts_t)(void* wrapper, Vector3D tilePosition, int tileLayer, void* countBuffer);
typedef WINAPI void (*RecastDetour_GetTilePolygonVertices_t)(void* wrapper, Vector3D tilePosition, int tileLayer, int polygon, void* vertexBuffer);
typedef WINAPI int (*RecastDetour_TileAlreadyGenerated_t)(void* instance, Vector3D pos);
typedef WINAPI int (*RecastDetour_GetTileDataSize_t)(void* instance, Vector3D pos, int tilelayer);
typedef WINAPI void (*RecastDetour_RemoveTile_t)(void* instance, Vector3D pos, int tilelayer);
typedef WINAPI size_t (*RecastDetour_GetAllocatedMemory_t)(void);

int main(int argc, char *argv[]) {
    // Load the DLL
    pe_image image;
    if (!load_dll(&image, "../RecastDetour.dll")) {
        LogMessage("Failed to load RecastDetour.dll");
        return 2;
    }

    // Test calls
    LogMessage("Calling: _callnewh");
    int (*pCallnewh)(size_t) = (int (*)(size_t))get_export("_callnewh");
    if (!pCallnewh) {
        LogMessage("Missing exported function: _callnewh");
        return 1;
    }
    {
        int v = pCallnewh(1);
        LogMessageA("returned: %d", v);
    }

    LogMessage("Calling: RecastDetour_Create");
    RecastDetour_Create_t pRecastDetour_Create = (RecastDetour_Create_t)get_export("RecastDetour_Create");
    if (!pRecastDetour_Create) {
        LogMessage("Missing exported function: RecastDetour_Create");
        return 1;
    }
    void *pMesh = pRecastDetour_Create();
    LogMessageA("pMesh=%p", pMesh);

    LogMessage("Calling: RecastDetour_Init");
    RecastDetour_Init_t pRecastDetour_Init = (RecastDetour_Init_t)get_export("RecastDetour_Init");
    if (!pRecastDetour_Init) {
        LogMessage("Missing exported function: RecastDetour_Init");
        return 1;
    }
    float a[3] = {0.0f,0.0f,0.0f};
    float b[3] = {2.0f,2.0f,2.0f};
    pRecastDetour_Init(pMesh, 0.2f, 2, a, b);

    LogMessage("Calling: RecastDetour_GetAllocatedMemory");
    RecastDetour_GetAllocatedMemory_t pRecastDetour_GetAllocatedMemory = (RecastDetour_GetAllocatedMemory_t)get_export("RecastDetour_GetAllocatedMemory");
    if (!pRecastDetour_GetAllocatedMemory) {
        LogMessage("Missing exported function: RecastDetour_GetAllocatedMemory");
        return 1;
    }
    {
        uint64_t v = pRecastDetour_GetAllocatedMemory();
        LogMessageA("returned: %lu", v);
    }

    LogMessage("Calling: RecastDetour_Clear");
    RecastDetour_Clear_t pRecastDetour_Clear = (RecastDetour_Clear_t)get_export("RecastDetour_Clear");
    if (!pRecastDetour_Clear) {
        LogMessage("Missing exported function: RecastDetour_Clear");
        return 1;
    }
    pRecastDetour_Clear(pMesh);

    LogMessage("Calling: RecastDetour_Destroy");
    RecastDetour_Destroy_t pRecastDetour_Destroy = (RecastDetour_Destroy_t)get_export("RecastDetour_Destroy");
    if (!pRecastDetour_Destroy) {
        LogMessage("Missing exported function: RecastDetour_Destroy");
        return 1;
    }
    pRecastDetour_Destroy(pMesh);

    LogMessage("Calling: RecastDetour_GetAllocatedMemory");
    {
        uint64_t v = pRecastDetour_GetAllocatedMemory();
        LogMessageA("returned: %lu", v);
    }

    return 0;
}
