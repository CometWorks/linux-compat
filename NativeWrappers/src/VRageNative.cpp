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

static pe_image g_vrage_native_image;

// ============================================================================
// Struct definitions matching C# P/Invoke layout
// ============================================================================

// VRageMath.Vector2 (8 bytes)
typedef struct {
    float X;
    float Y;
} Vector2;

// VRageMath.Vector3 (12 bytes)
typedef struct {
    float X;
    float Y;
    float Z;
} Vector3;

// VRageMath.Vector3I (12 bytes)
typedef struct {
    int X;
    int Y;
    int Z;
} Vector3I;

// VRageMath.PackedVector.Byte4 (4 bytes)
typedef struct {
    uint32_t PackedValue;
} Byte4;

// VrVoxelTriangle (6 bytes, Pack=2)
#pragma pack(push, 2)
typedef struct {
    uint16_t V0;
    uint16_t V1;
    uint16_t V2;
} VrVoxelTriangle;
#pragma pack(pop)

// VrVoxelVertex (40 bytes, Pack=4)
#pragma pack(push, 4)
typedef struct {
    Vector3I Cell;      // 12 bytes
    Vector3 Position;   // 12 bytes
    Vector3 Normal;     // 12 bytes
    Byte4 Color;        // 4 bytes
    uint8_t Material;   // 1 byte
} VrVoxelVertex;
#pragma pack(pop)

// VrPlanetShape.Mapset (Pack=8)
#pragma pack(push, 8)
typedef struct {
    uint16_t* Front;
    uint16_t* Back;
    uint16_t* Left;
    uint16_t* Right;
    uint16_t* Up;
    uint16_t* Down;
    int Resolution;
} Mapset;
#pragma pack(pop)

// VrPlanetShape.DetailMapData (Pack=8)
#pragma pack(push, 8)
typedef struct {
    uint8_t* Data;
    int Resolution;
    float Factor;
    float Size;
    float Scale;
    float m_min;
    float m_max;
    float m_in;
    float m_out;
    float m_inRecip;
    float m_outRecip;
    float m_mid;
} DetailMapData;
#pragma pack(pop)

// VrSewOperation (byte-sized enum)
typedef uint8_t VrSewOperation;

// GeneratedVertexProtocol (byte-sized enum)
typedef uint8_t GeneratedVertexProtocol;

// VrTailor.RemappedVertex (Pack=2)
#pragma pack(push, 2)
typedef struct {
    Vector3I Cell;
    uint16_t Index;
    uint8_t ProducedTriangleCount;
    uint8_t GenerationCorner;
} RemappedVertex;
#pragma pack(pop)

// VrTailor.VertexRef (Pack=1)
#pragma pack(push, 1)
typedef struct {
    uint8_t Mesh;
    uint8_t _pad;
    uint16_t Index;
} VertexRef;
#pragma pack(pop)

// ============================================================================
// Windows x64 ABI function pointer typedefs
//
// In the Windows x64 calling convention structs > 8 bytes are passed by
// hidden pointer.  The WINAPI attribute handles the register mapping.
// ============================================================================

// --- IsoMesher ---
typedef WINAPI void   (*IsoMesher_Calculate_t)(void* mesh, int numSamples, uint8_t* content, uint8_t* material);
typedef WINAPI void   (*IsoMesher_CalculateMaterials_t)(void* mesh, int numSamples, uint8_t* content, uint8_t* material, int materialOverride);

// --- VrDecimatePostprocessing ---
typedef WINAPI void*  (*VrDecimatePostprocessing_Create_t)(void);
typedef WINAPI void   (*VrDecimatePostprocessing_Release_t)(void* instance);
typedef WINAPI float  (*VrDecimatePostprocessing_GetFeatureAngle_t)(void* instance);
typedef WINAPI void   (*VrDecimatePostprocessing_SetFeatureAngle_t)(void* instance, float value);
typedef WINAPI float  (*VrDecimatePostprocessing_GetEdgeThreshold_t)(void* instance);
typedef WINAPI void   (*VrDecimatePostprocessing_SetEdgeThreshold_t)(void* instance, float value);
typedef WINAPI float  (*VrDecimatePostprocessing_GetPlaneThreshold_t)(void* instance);
typedef WINAPI void   (*VrDecimatePostprocessing_SetPlaneThreshold_t)(void* instance, float value);
typedef WINAPI int    (*VrDecimatePostprocessing_GetIgnoreEdges_t)(void* instance);
typedef WINAPI void   (*VrDecimatePostprocessing_SetIgnoreEdges_t)(void* instance, int value);
typedef WINAPI void   (*VrDecimatePostprocessing_GetClassification_t)(void* instance, void* mesh, void* target, int count);
typedef WINAPI void   (*VrDecimatePostprocessing_GetClassificationDetails_t)(void* instance, int vertex, uint16_t** features, uint16_t* featureCount, uint16_t** loop, uint16_t* loopSize, float* characteristicDistance);
typedef WINAPI int    (*VrDecimatePostprocessing_TrySimplify_t)(void* instance, int vertex, uint16_t** triangles, uint16_t* triangleCount, Vector3* normal);
typedef WINAPI int    (*VrDecimatePostprocessing_Simplify_t)(void* instance, int vertex);

// --- VrPlanetShape ---
// VrPlanetShape_Create passes Mapset (64 bytes) and DetailMapData (56 bytes) by
// value in C#.  Windows x64 ABI passes structs > 8 bytes via hidden pointer,
// so the PE function receives pointers to the caller's copies.
typedef WINAPI void*  (*VrPlanetShape_Create_t)(Vector3* translation, float radius, float hillMin, float hillMax, Mapset* maps, DetailMapData* detailMapData, int useLegacyAtan);
typedef WINAPI void   (*VrPlanetShape_Release_t)(void* instance);
typedef WINAPI void   (*VrPlanetShape_ReadContentRange_t)(void* instance, uint8_t* dest, int destSize, int offset, Vector3I* strides, Vector3I* start, Vector3I* end, float lodVoxelSize, int faceHint);
typedef WINAPI int    (*VrPlanetShape_CheckContentRange_t)(void* instance, Vector3I* start, Vector3I* end, float lodVoxelSize, int faceHint);
typedef WINAPI float  (*VrPlanetShape_GetValue_t)(void* instance, Vector2 texcoords, int face, Vector3* normal);
typedef WINAPI float  (*VrPlanetShape_GetHeight_t)(void* instance, Vector2 texcoords, int face, Vector3* normal);

// --- VrPostprocessing ---
typedef WINAPI void   (*VrPostprocessing_Process_t)(void* postprocess, void* mesh);

// --- VrRandomizePostprocessing ---
typedef WINAPI void*  (*VrRandomizePostprocessing_Create_t)(float ammount);
typedef WINAPI void   (*VrRandomizePostprocessing_Release_t)(void* instance);

// --- VrSewGuide ---
typedef WINAPI void*  (*VrSewGuide_Create1_t)(int lod, Vector3I* min, Vector3I* max, void* dataCache);
typedef WINAPI void*  (*VrSewGuide_Create2_t)(void* mesh, void* dataCache);
typedef WINAPI void   (*VrSewGuide_Release_t)(void* instance);
typedef WINAPI int    (*VrSewGuide_GetInMemorySize_t)(void* instance);
typedef WINAPI int    (*VrSewGuide_GetLod_t)(void* instance);
typedef WINAPI float  (*VrSewGuide_GetScale_t)(void* instance);
typedef WINAPI void   (*VrSewGuide_GetStart_t)(Vector3I* retval, void* instance);
typedef WINAPI void   (*VrSewGuide_GetEnd_t)(Vector3I* retval, void* instance);
typedef WINAPI void   (*VrSewGuide_GetSize_t)(Vector3I* retval, void* instance);
typedef WINAPI int    (*VrSewGuide_GetSewn_t)(void* instance);
typedef WINAPI void   (*VrSewGuide_Reset_t)(void* instance);
typedef WINAPI void   (*VrSewGuide_SetMesh_t)(void* instance, void* mesh, void* dataCache);
typedef WINAPI void*  (*VrSewGuide_GetMesh_t)(void* instance);
typedef WINAPI void   (*VrSewGuide_InvalidateGenerated_t)(void* instance, Vector3I* minRange);
typedef WINAPI int    (*VrSewGuide_GetVersion_t)(void* instance);

// --- VrShellDataCache ---
typedef WINAPI void*  (*VrShellDataCache_FromDataCube_t)(Vector3I* size, uint8_t* content, uint8_t* material);
typedef WINAPI void*  (*VrShellDataCache_Empty_t)(void);
typedef WINAPI void*  (*VrShellDataCache_Full_t)(void);

// --- VrTailor ---
typedef WINAPI void*  (*VrTailor_Create_t)(void);
typedef WINAPI void   (*VrTailor_Release_t)(void* instance);
typedef WINAPI void   (*VrTailor_Sew_t)(void* instance, void** guides, VrSewOperation operations, Vector3I* min, Vector3I* max);
typedef WINAPI void   (*VrTailor_ClearBuffers_t)(void* instance);
typedef WINAPI void   (*VrTailor_SetDebug_t)(void* instance, int debug);
typedef WINAPI void   (*VrTailor_SetGenerate_t)(void* instance, GeneratedVertexProtocol generate);
typedef WINAPI void   (*VrTailor_DebugReadGenerated_t)(void* instance, uint16_t** generatedVertices, int* count);
typedef WINAPI void   (*VrTailor_DebugReadRemapped_t)(void* instance, RemappedVertex** remappedVertices, int* count);
typedef WINAPI void   (*VrTailor_DebugReadStudied_t)(void* instance, VertexRef** studiedVertices, int* count);

// --- VrVoxelMesh ---
typedef WINAPI void*  (*VrVoxelMesh_Create_t)(Vector3I* start, Vector3I* end, int lod);
typedef WINAPI void   (*VrVoxelMesh_Release_t)(void* instance);
typedef WINAPI int    (*VrVoxelMesh_GetVertexCount_t)(void* instance);
typedef WINAPI VrVoxelVertex* (*VrVoxelMesh_GetVertices_t)(void* instance);
typedef WINAPI int    (*VrVoxelMesh_GetTriangleCount_t)(void* instance);
typedef WINAPI VrVoxelTriangle* (*VrVoxelMesh_GetTriangles_t)(void* instance);
typedef WINAPI int    (*VrVoxelMesh_GetLod_t)(void* instance);
typedef WINAPI float  (*VrVoxelMesh_GetScale_t)(void* instance);
typedef WINAPI void   (*VrVoxelMesh_GetStart_t)(Vector3I* retval, void* instance);
typedef WINAPI void   (*VrVoxelMesh_GetEnd_t)(Vector3I* retval, void* instance);
typedef WINAPI void   (*VrVoxelMesh_GetMeshData_t)(void* instance, Vector3* positions, Vector3* normals, uint8_t* materials, Vector3I* cells, Byte4* color, VrVoxelTriangle* triangles);
typedef WINAPI void   (*VrVoxelMesh_Clear_t)(void* instance);

// ============================================================================
// Function pointer declarations
// ============================================================================

DECLARE_FUNCTION_POINTER(IsoMesher_Calculate)
DECLARE_FUNCTION_POINTER(IsoMesher_CalculateMaterials)

DECLARE_FUNCTION_POINTER(VrDecimatePostprocessing_Create)
DECLARE_FUNCTION_POINTER(VrDecimatePostprocessing_Release)
DECLARE_FUNCTION_POINTER(VrDecimatePostprocessing_GetFeatureAngle)
DECLARE_FUNCTION_POINTER(VrDecimatePostprocessing_SetFeatureAngle)
DECLARE_FUNCTION_POINTER(VrDecimatePostprocessing_GetEdgeThreshold)
DECLARE_FUNCTION_POINTER(VrDecimatePostprocessing_SetEdgeThreshold)
DECLARE_FUNCTION_POINTER(VrDecimatePostprocessing_GetPlaneThreshold)
DECLARE_FUNCTION_POINTER(VrDecimatePostprocessing_SetPlaneThreshold)
DECLARE_FUNCTION_POINTER(VrDecimatePostprocessing_GetIgnoreEdges)
DECLARE_FUNCTION_POINTER(VrDecimatePostprocessing_SetIgnoreEdges)
DECLARE_FUNCTION_POINTER(VrDecimatePostprocessing_GetClassification)
DECLARE_FUNCTION_POINTER(VrDecimatePostprocessing_GetClassificationDetails)
DECLARE_FUNCTION_POINTER(VrDecimatePostprocessing_TrySimplify)
DECLARE_FUNCTION_POINTER(VrDecimatePostprocessing_Simplify)

DECLARE_FUNCTION_POINTER(VrPlanetShape_Create)
DECLARE_FUNCTION_POINTER(VrPlanetShape_Release)
DECLARE_FUNCTION_POINTER(VrPlanetShape_ReadContentRange)
DECLARE_FUNCTION_POINTER(VrPlanetShape_CheckContentRange)
DECLARE_FUNCTION_POINTER(VrPlanetShape_GetValue)
DECLARE_FUNCTION_POINTER(VrPlanetShape_GetHeight)

DECLARE_FUNCTION_POINTER(VrPostprocessing_Process)

DECLARE_FUNCTION_POINTER(VrRandomizePostprocessing_Create)
DECLARE_FUNCTION_POINTER(VrRandomizePostprocessing_Release)

DECLARE_FUNCTION_POINTER(VrSewGuide_Create1)
DECLARE_FUNCTION_POINTER(VrSewGuide_Create2)
DECLARE_FUNCTION_POINTER(VrSewGuide_Release)
DECLARE_FUNCTION_POINTER(VrSewGuide_GetInMemorySize)
DECLARE_FUNCTION_POINTER(VrSewGuide_GetLod)
DECLARE_FUNCTION_POINTER(VrSewGuide_GetScale)
DECLARE_FUNCTION_POINTER(VrSewGuide_GetStart)
DECLARE_FUNCTION_POINTER(VrSewGuide_GetEnd)
DECLARE_FUNCTION_POINTER(VrSewGuide_GetSize)
DECLARE_FUNCTION_POINTER(VrSewGuide_GetSewn)
DECLARE_FUNCTION_POINTER(VrSewGuide_Reset)
DECLARE_FUNCTION_POINTER(VrSewGuide_SetMesh)
DECLARE_FUNCTION_POINTER(VrSewGuide_GetMesh)
DECLARE_FUNCTION_POINTER(VrSewGuide_InvalidateGenerated)
DECLARE_FUNCTION_POINTER(VrSewGuide_GetVersion)

DECLARE_FUNCTION_POINTER(VrShellDataCache_FromDataCube)
DECLARE_FUNCTION_POINTER(VrShellDataCache_Empty)
DECLARE_FUNCTION_POINTER(VrShellDataCache_Full)

DECLARE_FUNCTION_POINTER(VrTailor_Create)
DECLARE_FUNCTION_POINTER(VrTailor_Release)
DECLARE_FUNCTION_POINTER(VrTailor_Sew)
DECLARE_FUNCTION_POINTER(VrTailor_ClearBuffers)
DECLARE_FUNCTION_POINTER(VrTailor_SetDebug)
DECLARE_FUNCTION_POINTER(VrTailor_SetGenerate)
DECLARE_FUNCTION_POINTER(VrTailor_DebugReadGenerated)
DECLARE_FUNCTION_POINTER(VrTailor_DebugReadRemapped)
DECLARE_FUNCTION_POINTER(VrTailor_DebugReadStudied)

DECLARE_FUNCTION_POINTER(VrVoxelMesh_Create)
DECLARE_FUNCTION_POINTER(VrVoxelMesh_Release)
DECLARE_FUNCTION_POINTER(VrVoxelMesh_GetVertexCount)
DECLARE_FUNCTION_POINTER(VrVoxelMesh_GetVertices)
DECLARE_FUNCTION_POINTER(VrVoxelMesh_GetTriangleCount)
DECLARE_FUNCTION_POINTER(VrVoxelMesh_GetTriangles)
DECLARE_FUNCTION_POINTER(VrVoxelMesh_GetLod)
DECLARE_FUNCTION_POINTER(VrVoxelMesh_GetScale)
DECLARE_FUNCTION_POINTER(VrVoxelMesh_GetStart)
DECLARE_FUNCTION_POINTER(VrVoxelMesh_GetEnd)
DECLARE_FUNCTION_POINTER(VrVoxelMesh_GetMeshData)
DECLARE_FUNCTION_POINTER(VrVoxelMesh_Clear)

// ============================================================================
// Initialization
// ============================================================================

static void InitImpl(const char* dllPath)
{
    if (!load_dll(&g_vrage_native_image, dllPath)) {
        LogMessage("Failed to load VRage.Native.dll");
        throw std::runtime_error("Failed to load VRage.Native.dll");
    }

    SET_FUNCTION_POINTER(IsoMesher_Calculate)
    SET_FUNCTION_POINTER(IsoMesher_CalculateMaterials)

    SET_FUNCTION_POINTER(VrDecimatePostprocessing_Create)
    SET_FUNCTION_POINTER(VrDecimatePostprocessing_Release)
    SET_FUNCTION_POINTER(VrDecimatePostprocessing_GetFeatureAngle)
    SET_FUNCTION_POINTER(VrDecimatePostprocessing_SetFeatureAngle)
    SET_FUNCTION_POINTER(VrDecimatePostprocessing_GetEdgeThreshold)
    SET_FUNCTION_POINTER(VrDecimatePostprocessing_SetEdgeThreshold)
    SET_FUNCTION_POINTER(VrDecimatePostprocessing_GetPlaneThreshold)
    SET_FUNCTION_POINTER(VrDecimatePostprocessing_SetPlaneThreshold)
    SET_FUNCTION_POINTER(VrDecimatePostprocessing_GetIgnoreEdges)
    SET_FUNCTION_POINTER(VrDecimatePostprocessing_SetIgnoreEdges)
    SET_FUNCTION_POINTER(VrDecimatePostprocessing_GetClassification)
    SET_FUNCTION_POINTER(VrDecimatePostprocessing_GetClassificationDetails)
    SET_FUNCTION_POINTER(VrDecimatePostprocessing_TrySimplify)
    SET_FUNCTION_POINTER(VrDecimatePostprocessing_Simplify)

    SET_FUNCTION_POINTER(VrPlanetShape_Create)
    SET_FUNCTION_POINTER(VrPlanetShape_Release)
    SET_FUNCTION_POINTER(VrPlanetShape_ReadContentRange)
    SET_FUNCTION_POINTER(VrPlanetShape_CheckContentRange)
    SET_FUNCTION_POINTER(VrPlanetShape_GetValue)
    SET_FUNCTION_POINTER(VrPlanetShape_GetHeight)

    SET_FUNCTION_POINTER(VrPostprocessing_Process)

    SET_FUNCTION_POINTER(VrRandomizePostprocessing_Create)
    SET_FUNCTION_POINTER(VrRandomizePostprocessing_Release)

    SET_FUNCTION_POINTER(VrSewGuide_Create1)
    SET_FUNCTION_POINTER(VrSewGuide_Create2)
    SET_FUNCTION_POINTER(VrSewGuide_Release)
    SET_FUNCTION_POINTER(VrSewGuide_GetInMemorySize)
    SET_FUNCTION_POINTER(VrSewGuide_GetLod)
    SET_FUNCTION_POINTER(VrSewGuide_GetScale)
    SET_FUNCTION_POINTER(VrSewGuide_GetStart)
    SET_FUNCTION_POINTER(VrSewGuide_GetEnd)
    SET_FUNCTION_POINTER(VrSewGuide_GetSize)
    SET_FUNCTION_POINTER(VrSewGuide_GetSewn)
    SET_FUNCTION_POINTER(VrSewGuide_Reset)
    SET_FUNCTION_POINTER(VrSewGuide_SetMesh)
    SET_FUNCTION_POINTER(VrSewGuide_GetMesh)
    SET_FUNCTION_POINTER(VrSewGuide_InvalidateGenerated)
    SET_FUNCTION_POINTER(VrSewGuide_GetVersion)

    SET_FUNCTION_POINTER(VrShellDataCache_FromDataCube)
    SET_FUNCTION_POINTER(VrShellDataCache_Empty)
    SET_FUNCTION_POINTER(VrShellDataCache_Full)

    SET_FUNCTION_POINTER(VrTailor_Create)
    SET_FUNCTION_POINTER(VrTailor_Release)
    SET_FUNCTION_POINTER(VrTailor_Sew)
    SET_FUNCTION_POINTER(VrTailor_ClearBuffers)
    SET_FUNCTION_POINTER(VrTailor_SetDebug)
    SET_FUNCTION_POINTER(VrTailor_SetGenerate)
    SET_FUNCTION_POINTER(VrTailor_DebugReadGenerated)
    SET_FUNCTION_POINTER(VrTailor_DebugReadRemapped)
    SET_FUNCTION_POINTER(VrTailor_DebugReadStudied)

    SET_FUNCTION_POINTER(VrVoxelMesh_Create)
    SET_FUNCTION_POINTER(VrVoxelMesh_Release)
    SET_FUNCTION_POINTER(VrVoxelMesh_GetVertexCount)
    SET_FUNCTION_POINTER(VrVoxelMesh_GetVertices)
    SET_FUNCTION_POINTER(VrVoxelMesh_GetTriangleCount)
    SET_FUNCTION_POINTER(VrVoxelMesh_GetTriangles)
    SET_FUNCTION_POINTER(VrVoxelMesh_GetLod)
    SET_FUNCTION_POINTER(VrVoxelMesh_GetScale)
    SET_FUNCTION_POINTER(VrVoxelMesh_GetStart)
    SET_FUNCTION_POINTER(VrVoxelMesh_GetEnd)
    SET_FUNCTION_POINTER(VrVoxelMesh_GetMeshData)
    SET_FUNCTION_POINTER(VrVoxelMesh_Clear)
}

// ============================================================================
// Exported wrapper functions (extern "C")
//
// The .NET runtime on Linux calls these with the SysV x64 ABI.
// Each wrapper translates to the Windows x64 ABI when calling the PE function.
//
// Key ABI differences handled:
// - Structs > 8 bytes: SysV may pass in registers, Windows always by pointer.
//   For the PE call we take the address of the SysV-passed struct.
// - Return structs > 8 bytes (e.g. Vector3I): Windows uses a hidden first
//   parameter (pointer to caller-allocated return space).
// - bool in Windows x64 is 1 byte but occupies a full register; we cast.
// ============================================================================

extern "C" {

void Init(const char* dllPath)
{
    InitImpl(dllPath);
}

// --- IsoMesher ---

void IsoMesher_Calculate(void* mesh, int numSamples, uint8_t* content, uint8_t* material)
{
    EnsureThreadInfo();
    REQUIRE_FUNCTION_POINTER(IsoMesher_Calculate)
    pIsoMesher_Calculate(mesh, numSamples, content, material);
}

void IsoMesher_CalculateMaterials(void* mesh, int numSamples, uint8_t* content, uint8_t* material, int materialOverride)
{
    EnsureThreadInfo();
    REQUIRE_FUNCTION_POINTER(IsoMesher_CalculateMaterials)
    pIsoMesher_CalculateMaterials(mesh, numSamples, content, material, materialOverride);
}

// --- VrDecimatePostprocessing ---

void* VrDecimatePostprocessing_Create(void)
{
    EnsureThreadInfo();
    REQUIRE_FUNCTION_POINTER(VrDecimatePostprocessing_Create)
    return pVrDecimatePostprocessing_Create();
}

void VrDecimatePostprocessing_Release(void* instance)
{
    EnsureThreadInfo();
    REQUIRE_FUNCTION_POINTER(VrDecimatePostprocessing_Release)
    pVrDecimatePostprocessing_Release(instance);
}

float VrDecimatePostprocessing_GetFeatureAngle(void* instance)
{
    EnsureThreadInfo();
    REQUIRE_FUNCTION_POINTER(VrDecimatePostprocessing_GetFeatureAngle)
    return pVrDecimatePostprocessing_GetFeatureAngle(instance);
}

void VrDecimatePostprocessing_SetFeatureAngle(void* instance, float value)
{
    EnsureThreadInfo();
    REQUIRE_FUNCTION_POINTER(VrDecimatePostprocessing_SetFeatureAngle)
    pVrDecimatePostprocessing_SetFeatureAngle(instance, value);
}

float VrDecimatePostprocessing_GetEdgeThreshold(void* instance)
{
    EnsureThreadInfo();
    REQUIRE_FUNCTION_POINTER(VrDecimatePostprocessing_GetEdgeThreshold)
    return pVrDecimatePostprocessing_GetEdgeThreshold(instance);
}

void VrDecimatePostprocessing_SetEdgeThreshold(void* instance, float value)
{
    EnsureThreadInfo();
    REQUIRE_FUNCTION_POINTER(VrDecimatePostprocessing_SetEdgeThreshold)
    pVrDecimatePostprocessing_SetEdgeThreshold(instance, value);
}

float VrDecimatePostprocessing_GetPlaneThreshold(void* instance)
{
    EnsureThreadInfo();
    REQUIRE_FUNCTION_POINTER(VrDecimatePostprocessing_GetPlaneThreshold)
    return pVrDecimatePostprocessing_GetPlaneThreshold(instance);
}

void VrDecimatePostprocessing_SetPlaneThreshold(void* instance, float value)
{
    EnsureThreadInfo();
    REQUIRE_FUNCTION_POINTER(VrDecimatePostprocessing_SetPlaneThreshold)
    pVrDecimatePostprocessing_SetPlaneThreshold(instance, value);
}

int VrDecimatePostprocessing_GetIgnoreEdges(void* instance)
{
    EnsureThreadInfo();
    REQUIRE_FUNCTION_POINTER(VrDecimatePostprocessing_GetIgnoreEdges)
    return pVrDecimatePostprocessing_GetIgnoreEdges(instance);
}

void VrDecimatePostprocessing_SetIgnoreEdges(void* instance, int value)
{
    EnsureThreadInfo();
    REQUIRE_FUNCTION_POINTER(VrDecimatePostprocessing_SetIgnoreEdges)
    pVrDecimatePostprocessing_SetIgnoreEdges(instance, value);
}

void VrDecimatePostprocessing_GetClassification(void* instance, void* mesh, void* target, int count)
{
    EnsureThreadInfo();
    REQUIRE_FUNCTION_POINTER(VrDecimatePostprocessing_GetClassification)
    pVrDecimatePostprocessing_GetClassification(instance, mesh, target, count);
}

void VrDecimatePostprocessing_GetClassificationDetails(void* instance, int vertex, uint16_t** features, uint16_t* featureCount, uint16_t** loop, uint16_t* loopSize, float* characteristicDistance)
{
    EnsureThreadInfo();
    REQUIRE_FUNCTION_POINTER(VrDecimatePostprocessing_GetClassificationDetails)
    pVrDecimatePostprocessing_GetClassificationDetails(instance, vertex, features, featureCount, loop, loopSize, characteristicDistance);
}

int VrDecimatePostprocessing_TrySimplify(void* instance, int vertex, uint16_t** triangles, uint16_t* triangleCount, Vector3* normal)
{
    EnsureThreadInfo();
    REQUIRE_FUNCTION_POINTER(VrDecimatePostprocessing_TrySimplify)
    return pVrDecimatePostprocessing_TrySimplify(instance, vertex, triangles, triangleCount, normal);
}

int VrDecimatePostprocessing_Simplify(void* instance, int vertex)
{
    EnsureThreadInfo();
    REQUIRE_FUNCTION_POINTER(VrDecimatePostprocessing_Simplify)
    return pVrDecimatePostprocessing_Simplify(instance, vertex);
}

// --- VrPlanetShape ---

void* VrPlanetShape_Create(Vector3 translation, float radius, float hillMin, float hillMax, Mapset maps, DetailMapData detailMapData, int useLegacyAtan)
{
    EnsureThreadInfo();
    REQUIRE_FUNCTION_POINTER(VrPlanetShape_Create)
    return pVrPlanetShape_Create(&translation, radius, hillMin, hillMax, &maps, &detailMapData, useLegacyAtan);
}

void VrPlanetShape_Release(void* instance)
{
    EnsureThreadInfo();
    REQUIRE_FUNCTION_POINTER(VrPlanetShape_Release)
    pVrPlanetShape_Release(instance);
}

void VrPlanetShape_ReadContentRange(void* instance, uint8_t* dest, int destSize, int offset, Vector3I strides, Vector3I start, Vector3I end, float lodVoxelSize, int faceHint)
{
    EnsureThreadInfo();
    REQUIRE_FUNCTION_POINTER(VrPlanetShape_ReadContentRange)
    pVrPlanetShape_ReadContentRange(instance, dest, destSize, offset, &strides, &start, &end, lodVoxelSize, faceHint);
}

int VrPlanetShape_CheckContentRange(void* instance, Vector3I start, Vector3I end, float lodVoxelSize, int faceHint)
{
    EnsureThreadInfo();
    REQUIRE_FUNCTION_POINTER(VrPlanetShape_CheckContentRange)
    return pVrPlanetShape_CheckContentRange(instance, &start, &end, lodVoxelSize, faceHint);
}

float VrPlanetShape_GetValue(void* instance, Vector2 texcoords, int face, Vector3* normal)
{
    EnsureThreadInfo();
    REQUIRE_FUNCTION_POINTER(VrPlanetShape_GetValue)
    return pVrPlanetShape_GetValue(instance, texcoords, face, normal);
}

float VrPlanetShape_GetHeight(void* instance, Vector2 texcoords, int face, Vector3* normal)
{
    EnsureThreadInfo();
    REQUIRE_FUNCTION_POINTER(VrPlanetShape_GetHeight)
    return pVrPlanetShape_GetHeight(instance, texcoords, face, normal);
}

// --- VrPostprocessing ---

void VrPostprocessing_Process(void* postprocess, void* mesh)
{
    EnsureThreadInfo();
    REQUIRE_FUNCTION_POINTER(VrPostprocessing_Process)
    pVrPostprocessing_Process(postprocess, mesh);
}

// --- VrRandomizePostprocessing ---

void* VrRandomizePostprocessing_Create(float ammount)
{
    EnsureThreadInfo();
    REQUIRE_FUNCTION_POINTER(VrRandomizePostprocessing_Create)
    return pVrRandomizePostprocessing_Create(ammount);
}

void VrRandomizePostprocessing_Release(void* instance)
{
    EnsureThreadInfo();
    REQUIRE_FUNCTION_POINTER(VrRandomizePostprocessing_Release)
    pVrRandomizePostprocessing_Release(instance);
}

// --- VrSewGuide ---

void* VrSewGuide_Create1(int lod, Vector3I min, Vector3I max, void* dataCache)
{
    EnsureThreadInfo();
    REQUIRE_FUNCTION_POINTER(VrSewGuide_Create1)
    return pVrSewGuide_Create1(lod, &min, &max, dataCache);
}

void* VrSewGuide_Create2(void* mesh, void* dataCache)
{
    EnsureThreadInfo();
    REQUIRE_FUNCTION_POINTER(VrSewGuide_Create2)
    return pVrSewGuide_Create2(mesh, dataCache);
}

void VrSewGuide_Release(void* instance)
{
    EnsureThreadInfo();
    REQUIRE_FUNCTION_POINTER(VrSewGuide_Release)
    pVrSewGuide_Release(instance);
}

int VrSewGuide_GetInMemorySize(void* instance)
{
    EnsureThreadInfo();
    REQUIRE_FUNCTION_POINTER(VrSewGuide_GetInMemorySize)
    return pVrSewGuide_GetInMemorySize(instance);
}

int VrSewGuide_GetLod(void* instance)
{
    EnsureThreadInfo();
    REQUIRE_FUNCTION_POINTER(VrSewGuide_GetLod)
    return pVrSewGuide_GetLod(instance);
}

float VrSewGuide_GetScale(void* instance)
{
    EnsureThreadInfo();
    REQUIRE_FUNCTION_POINTER(VrSewGuide_GetScale)
    return pVrSewGuide_GetScale(instance);
}

Vector3I VrSewGuide_GetStart(void* instance)
{
    EnsureThreadInfo();
    REQUIRE_FUNCTION_POINTER(VrSewGuide_GetStart)
    Vector3I retval;
    pVrSewGuide_GetStart(&retval, instance);
    return retval;
}

Vector3I VrSewGuide_GetEnd(void* instance)
{
    EnsureThreadInfo();
    REQUIRE_FUNCTION_POINTER(VrSewGuide_GetEnd)
    Vector3I retval;
    pVrSewGuide_GetEnd(&retval, instance);
    return retval;
}

Vector3I VrSewGuide_GetSize(void* instance)
{
    EnsureThreadInfo();
    REQUIRE_FUNCTION_POINTER(VrSewGuide_GetSize)
    Vector3I retval;
    pVrSewGuide_GetSize(&retval, instance);
    return retval;
}

int VrSewGuide_GetSewn(void* instance)
{
    EnsureThreadInfo();
    REQUIRE_FUNCTION_POINTER(VrSewGuide_GetSewn)
    return pVrSewGuide_GetSewn(instance);
}

void VrSewGuide_Reset(void* instance)
{
    EnsureThreadInfo();
    REQUIRE_FUNCTION_POINTER(VrSewGuide_Reset)
    pVrSewGuide_Reset(instance);
}

void VrSewGuide_SetMesh(void* instance, void* mesh, void* dataCache)
{
    EnsureThreadInfo();
    REQUIRE_FUNCTION_POINTER(VrSewGuide_SetMesh)
    pVrSewGuide_SetMesh(instance, mesh, dataCache);
}

void* VrSewGuide_GetMesh(void* instance)
{
    EnsureThreadInfo();
    REQUIRE_FUNCTION_POINTER(VrSewGuide_GetMesh)
    return pVrSewGuide_GetMesh(instance);
}

void VrSewGuide_InvalidateGenerated(void* instance, Vector3I minRange)
{
    EnsureThreadInfo();
    REQUIRE_FUNCTION_POINTER(VrSewGuide_InvalidateGenerated)
    pVrSewGuide_InvalidateGenerated(instance, &minRange);
}

int VrSewGuide_GetVersion(void* instance)
{
    EnsureThreadInfo();
    REQUIRE_FUNCTION_POINTER(VrSewGuide_GetVersion)
    return pVrSewGuide_GetVersion(instance);
}

// --- VrShellDataCache ---

void* VrShellDataCache_FromDataCube(Vector3I size, uint8_t* content, uint8_t* material)
{
    EnsureThreadInfo();
    REQUIRE_FUNCTION_POINTER(VrShellDataCache_FromDataCube)
    return pVrShellDataCache_FromDataCube(&size, content, material);
}

void* VrShellDataCache_Empty(void)
{
    EnsureThreadInfo();
    REQUIRE_FUNCTION_POINTER(VrShellDataCache_Empty)
    return pVrShellDataCache_Empty();
}

void* VrShellDataCache_Full(void)
{
    EnsureThreadInfo();
    REQUIRE_FUNCTION_POINTER(VrShellDataCache_Full)
    return pVrShellDataCache_Full();
}

// --- VrTailor ---

void* VrTailor_Create(void)
{
    EnsureThreadInfo();
    REQUIRE_FUNCTION_POINTER(VrTailor_Create)
    return pVrTailor_Create();
}

void VrTailor_Release(void* instance)
{
    EnsureThreadInfo();
    REQUIRE_FUNCTION_POINTER(VrTailor_Release)
    pVrTailor_Release(instance);
}

void VrTailor_Sew(void* instance, void** guides, VrSewOperation operations, Vector3I min, Vector3I max)
{
    EnsureThreadInfo();
    REQUIRE_FUNCTION_POINTER(VrTailor_Sew)
    pVrTailor_Sew(instance, guides, operations, &min, &max);
}

void VrTailor_ClearBuffers(void* instance)
{
    EnsureThreadInfo();
    REQUIRE_FUNCTION_POINTER(VrTailor_ClearBuffers)
    pVrTailor_ClearBuffers(instance);
}

void VrTailor_SetDebug(void* instance, int debug)
{
    EnsureThreadInfo();
    REQUIRE_FUNCTION_POINTER(VrTailor_SetDebug)
    pVrTailor_SetDebug(instance, debug);
}

void VrTailor_SetGenerate(void* instance, GeneratedVertexProtocol generate)
{
    EnsureThreadInfo();
    REQUIRE_FUNCTION_POINTER(VrTailor_SetGenerate)
    pVrTailor_SetGenerate(instance, generate);
}

void VrTailor_DebugReadGenerated(void* instance, uint16_t** generatedVertices, int* count)
{
    EnsureThreadInfo();
    REQUIRE_FUNCTION_POINTER(VrTailor_DebugReadGenerated)
    pVrTailor_DebugReadGenerated(instance, generatedVertices, count);
}

void VrTailor_DebugReadRemapped(void* instance, RemappedVertex** remappedVertices, int* count)
{
    EnsureThreadInfo();
    REQUIRE_FUNCTION_POINTER(VrTailor_DebugReadRemapped)
    pVrTailor_DebugReadRemapped(instance, remappedVertices, count);
}

void VrTailor_DebugReadStudied(void* instance, VertexRef** studiedVertices, int* count)
{
    EnsureThreadInfo();
    REQUIRE_FUNCTION_POINTER(VrTailor_DebugReadStudied)
    pVrTailor_DebugReadStudied(instance, studiedVertices, count);
}

// --- VrVoxelMesh ---

void* VrVoxelMesh_Create(Vector3I start, Vector3I end, int lod)
{
    EnsureThreadInfo();
    REQUIRE_FUNCTION_POINTER(VrVoxelMesh_Create)
    return pVrVoxelMesh_Create(&start, &end, lod);
}

void VrVoxelMesh_Release(void* instance)
{
    EnsureThreadInfo();
    REQUIRE_FUNCTION_POINTER(VrVoxelMesh_Release)
    pVrVoxelMesh_Release(instance);
}

int VrVoxelMesh_GetVertexCount(void* instance)
{
    EnsureThreadInfo();
    REQUIRE_FUNCTION_POINTER(VrVoxelMesh_GetVertexCount)
    return pVrVoxelMesh_GetVertexCount(instance);
}

VrVoxelVertex* VrVoxelMesh_GetVertices(void* instance)
{
    EnsureThreadInfo();
    REQUIRE_FUNCTION_POINTER(VrVoxelMesh_GetVertices)
    return pVrVoxelMesh_GetVertices(instance);
}

int VrVoxelMesh_GetTriangleCount(void* instance)
{
    EnsureThreadInfo();
    REQUIRE_FUNCTION_POINTER(VrVoxelMesh_GetTriangleCount)
    return pVrVoxelMesh_GetTriangleCount(instance);
}

VrVoxelTriangle* VrVoxelMesh_GetTriangles(void* instance)
{
    EnsureThreadInfo();
    REQUIRE_FUNCTION_POINTER(VrVoxelMesh_GetTriangles)
    return pVrVoxelMesh_GetTriangles(instance);
}

int VrVoxelMesh_GetLod(void* instance)
{
    EnsureThreadInfo();
    REQUIRE_FUNCTION_POINTER(VrVoxelMesh_GetLod)
    return pVrVoxelMesh_GetLod(instance);
}

float VrVoxelMesh_GetScale(void* instance)
{
    EnsureThreadInfo();
    REQUIRE_FUNCTION_POINTER(VrVoxelMesh_GetScale)
    return pVrVoxelMesh_GetScale(instance);
}

Vector3I VrVoxelMesh_GetStart(void* instance)
{
    EnsureThreadInfo();
    REQUIRE_FUNCTION_POINTER(VrVoxelMesh_GetStart)
    Vector3I retval;
    pVrVoxelMesh_GetStart(&retval, instance);
    return retval;
}

Vector3I VrVoxelMesh_GetEnd(void* instance)
{
    EnsureThreadInfo();
    REQUIRE_FUNCTION_POINTER(VrVoxelMesh_GetEnd)
    Vector3I retval;
    pVrVoxelMesh_GetEnd(&retval, instance);
    return retval;
}

void VrVoxelMesh_GetMeshData(void* instance, Vector3* positions, Vector3* normals, uint8_t* materials, Vector3I* cells, Byte4* color, VrVoxelTriangle* triangles)
{
    EnsureThreadInfo();
    REQUIRE_FUNCTION_POINTER(VrVoxelMesh_GetMeshData)
    pVrVoxelMesh_GetMeshData(instance, positions, normals, materials, cells, color, triangles);
}

void VrVoxelMesh_Clear(void* instance)
{
    EnsureThreadInfo();
    REQUIRE_FUNCTION_POINTER(VrVoxelMesh_Clear)
    pVrVoxelMesh_Clear(instance);
}

} // extern "C"
