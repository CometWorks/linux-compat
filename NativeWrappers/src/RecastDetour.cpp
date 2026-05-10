#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <fstream>
#include <stdexcept>

#include "dll_loader.h"

#define DECLARE_FUNCTION_POINTER(func) \
static WINAPI func##_t p##func = nullptr;

#define SET_FUNCTION_POINTER(func) \
p##func = (WINAPI func##_t)get_export(#func);

#define REQUIRE_FUNCTION_POINTER(func) \
if (!p##func) { \
    SET_FUNCTION_POINTER(func) \
    if (!p##func) { \
        fprintf(stderr, "Failed to load function: " #func "\n"); \
        throw std::runtime_error("Failed to load function: " #func); \
    } \
}

static void LogMessage(const char *text)
{
    std::ofstream("/tmp/ds.txt", std::ios::app) << text << "\n";
}

static void EnsureThreadInfo()
{
    if (!setup_nt_threadinfo(nullptr)) {
        fprintf(stderr, "Failed to initialize thread info\n");
        std::abort();
    }
    pe_ensure_tls_for_loaded_images();
}

static pe_image g_recast_image;

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
typedef WINAPI int (*RecastDetour_GetTilePolygonCount_t)(void* instance, Vector3D tilePosition, int tileLayer);
typedef WINAPI void (*RecastDetour_GetTilePolygonVertexCounts_t)(void* instance, Vector3D tilePosition, int tileLayer, void* countBuffer);
typedef WINAPI void (*RecastDetour_GetTilePolygonVertices_t)(void* instance, Vector3D tilePosition, int tileLayer, int polygon, void* vertexBuffer);
typedef WINAPI int (*RecastDetour_TileAlreadyGenerated_t)(void* instance, Vector3D pos);
typedef WINAPI int (*RecastDetour_GetTileDataSize_t)(void* instance, Vector3D pos, int tilelayer);
typedef WINAPI void (*RecastDetour_RemoveTile_t)(void* instance, Vector3D pos, int tilelayer);
typedef WINAPI size_t (*RecastDetour_GetAllocatedMemory_t)(void);

DECLARE_FUNCTION_POINTER(RecastDetour_Create)
DECLARE_FUNCTION_POINTER(RecastDetour_Destroy)
DECLARE_FUNCTION_POINTER(RecastDetour_Init)
DECLARE_FUNCTION_POINTER(RecastDetour_Clear)
DECLARE_FUNCTION_POINTER(RecastDetour_GetPath)
DECLARE_FUNCTION_POINTER(RecastDetour_GetNextPathPoint)
DECLARE_FUNCTION_POINTER(RecastDetour_CreateNavmeshTile)
DECLARE_FUNCTION_POINTER(RecastDetour_GetTilePolygonCount)
DECLARE_FUNCTION_POINTER(RecastDetour_GetTilePolygonVertexCounts)
DECLARE_FUNCTION_POINTER(RecastDetour_GetTilePolygonVertices)
DECLARE_FUNCTION_POINTER(RecastDetour_GetTileDataSize)
DECLARE_FUNCTION_POINTER(RecastDetour_TileAlreadyGenerated)
DECLARE_FUNCTION_POINTER(RecastDetour_RemoveTile)
DECLARE_FUNCTION_POINTER(RecastDetour_GetAllocatedMemory)

extern "C" {

void Init(const char* dllPath)
{
    if (!load_dll(&g_recast_image, dllPath)) {
        LogMessage("Failed to load RecastDetour.dll");
        throw std::runtime_error("Failed to load RecastDetour.dll");
    }

    SET_FUNCTION_POINTER(RecastDetour_Create)
    SET_FUNCTION_POINTER(RecastDetour_Destroy)
    SET_FUNCTION_POINTER(RecastDetour_Init)
    SET_FUNCTION_POINTER(RecastDetour_Clear)
    SET_FUNCTION_POINTER(RecastDetour_GetPath)
    SET_FUNCTION_POINTER(RecastDetour_GetNextPathPoint)
    SET_FUNCTION_POINTER(RecastDetour_CreateNavmeshTile)
    SET_FUNCTION_POINTER(RecastDetour_GetTilePolygonCount)
    SET_FUNCTION_POINTER(RecastDetour_GetTilePolygonVertexCounts)
    SET_FUNCTION_POINTER(RecastDetour_GetTilePolygonVertices)
    SET_FUNCTION_POINTER(RecastDetour_GetTileDataSize)
    SET_FUNCTION_POINTER(RecastDetour_TileAlreadyGenerated)
    SET_FUNCTION_POINTER(RecastDetour_RemoveTile)
    SET_FUNCTION_POINTER(RecastDetour_GetAllocatedMemory)
}

void* RecastDetour_Create(void) { EnsureThreadInfo(); REQUIRE_FUNCTION_POINTER(RecastDetour_Create) return pRecastDetour_Create(); }
void RecastDetour_Destroy(void* instance) { EnsureThreadInfo(); REQUIRE_FUNCTION_POINTER(RecastDetour_Destroy) pRecastDetour_Destroy(instance); }
void RecastDetour_Init(void* instance, float cellSize, float worldTileSize, float bMin[], float bMax[]) { EnsureThreadInfo(); REQUIRE_FUNCTION_POINTER(RecastDetour_Init) pRecastDetour_Init(instance, cellSize, worldTileSize, bMin, bMax); }
void RecastDetour_Clear(void* instance) { EnsureThreadInfo(); REQUIRE_FUNCTION_POINTER(RecastDetour_Clear) pRecastDetour_Clear(instance); }
int RecastDetour_GetPath(void* instance, Vector3 startPosition, Vector3 endPosition, void* destPath, int pathBufferSize) { EnsureThreadInfo(); REQUIRE_FUNCTION_POINTER(RecastDetour_GetPath) return pRecastDetour_GetPath(instance, startPosition, endPosition, destPath, pathBufferSize); }
int RecastDetour_GetNextPathPoint(void* instance, Vector3 startPosition, Vector3 endPosition, Vector3* nextPoint) { EnsureThreadInfo(); REQUIRE_FUNCTION_POINTER(RecastDetour_GetNextPathPoint) return pRecastDetour_GetNextPathPoint(instance, startPosition, endPosition, nextPoint); }
int RecastDetour_CreateNavmeshTile(void* instance, Vector3D pos, void* recastOptions, int tx, int ty, int tilelayer, void* vertices, int verticeCount, void* triangles, int triangleCount) { EnsureThreadInfo(); REQUIRE_FUNCTION_POINTER(RecastDetour_CreateNavmeshTile) return pRecastDetour_CreateNavmeshTile(instance, pos, recastOptions, tx, ty, tilelayer, vertices, verticeCount, triangles, triangleCount); }
int RecastDetour_GetTilePolygonCount(void* instance, Vector3D tilePosition, int tileLayer) { EnsureThreadInfo(); REQUIRE_FUNCTION_POINTER(RecastDetour_GetTilePolygonCount) return pRecastDetour_GetTilePolygonCount(instance, tilePosition, tileLayer); }
void RecastDetour_GetTilePolygonVertexCounts(void* instance, Vector3D tilePosition, int tileLayer, void* countBuffer) { EnsureThreadInfo(); REQUIRE_FUNCTION_POINTER(RecastDetour_GetTilePolygonVertexCounts) pRecastDetour_GetTilePolygonVertexCounts(instance, tilePosition, tileLayer, countBuffer); }
void RecastDetour_GetTilePolygonVertices(void* instance, Vector3D tilePosition, int tileLayer, int polygon, void* vertexBuffer) { EnsureThreadInfo(); REQUIRE_FUNCTION_POINTER(RecastDetour_GetTilePolygonVertices) pRecastDetour_GetTilePolygonVertices(instance, tilePosition, tileLayer, polygon, vertexBuffer); }
int RecastDetour_TileAlreadyGenerated(void* instance, Vector3D pos) { EnsureThreadInfo(); REQUIRE_FUNCTION_POINTER(RecastDetour_TileAlreadyGenerated) return pRecastDetour_TileAlreadyGenerated(instance, pos); }
int RecastDetour_GetTileDataSize(void* instance, Vector3D pos, int tilelayer) { EnsureThreadInfo(); REQUIRE_FUNCTION_POINTER(RecastDetour_GetTileDataSize) return pRecastDetour_GetTileDataSize(instance, pos, tilelayer); }
void RecastDetour_RemoveTile(void* instance, Vector3D pos, int tilelayer) { EnsureThreadInfo(); REQUIRE_FUNCTION_POINTER(RecastDetour_RemoveTile) pRecastDetour_RemoveTile(instance, pos, tilelayer); }
size_t RecastDetour_GetAllocatedMemory(void) { EnsureThreadInfo(); REQUIRE_FUNCTION_POINTER(RecastDetour_GetAllocatedMemory) return pRecastDetour_GetAllocatedMemory(); }

} // extern "C"
