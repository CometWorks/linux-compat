#include <cstdint>
#include <cstddef>
#include <atomic>
#include <chrono>
#include <cstdio>
#include <cstdlib>
#include <fstream>
#include <mutex>
#include <stdexcept>
#include <unordered_map>
#include <vector>

#include "dll_loader.h"
#include "HavokThunkRegistry.h"

#define DECLARE_FUNCTION_POINTER(func) static WINAPI func##_t p##func = nullptr;

#define SET_FUNCTION_POINTER(func) p##func = (WINAPI func##_t)get_export(#func);

#define REQUIRE_FUNCTION_POINTER(func) if (!p##func) {     SET_FUNCTION_POINTER(func)     if (!p##func) {         fprintf(stderr, "Failed to load function: " #func "\n");         throw std::runtime_error("Failed to load function: " #func);     } }

static void LogMessage(const char *text)
{
    std::ofstream("/tmp/ds.txt", std::ios::app) << text << "\n";
}

#define LOG_CALL(func) ;
// Uncomment/comment to enable/disable detailed logging
/*
static long long TimestampMs()
{
    auto now = std::chrono::steady_clock::now().time_since_epoch();
    return std::chrono::duration_cast<std::chrono::milliseconds>(now).count();
}

#define LOG_CALL(func) fprintf(stderr, "[%lld] %s\\n", TimestampMs(), #func)
*/

static void EnsureThreadInfo()
{
    if (!setup_nt_threadinfo(nullptr)) {
        fprintf(stderr, "Failed to initialize thread info\n");
        std::abort();
    }
    pe_ensure_tls_for_loaded_images();
}

static std::mutex g_callback_owner_mutex;
static std::unordered_map<void *, std::vector<callback_owner_binding>> g_callback_owner_bindings;

void register_callback_owner(void *owner, std::initializer_list<callback_owner_binding> bindings)
{
    if (!owner) {
        return;
    }
    // fprintf(stderr, "register_callback_owner owner=%p count=%zu\n", owner, bindings.size());
    std::lock_guard<std::mutex> lock(g_callback_owner_mutex);
    auto &entry = g_callback_owner_bindings[owner];
    entry.insert(entry.end(), bindings.begin(), bindings.end());
}

void release_callback_owner(void *owner)
{
    if (!owner) {
        return;
    }
    // fprintf(stderr, "release_callback_owner owner=%p\n", owner);
    std::lock_guard<std::mutex> lock(g_callback_owner_mutex);
    auto it = g_callback_owner_bindings.find(owner);
    if (it == g_callback_owner_bindings.end()) {
        return;
    }
    g_callback_owner_bindings.erase(it);
}



struct Vector3 {
    float X;
    float Y;
    float Z;
};

struct Vector4 {
    float X;
    float Y;
    float Z;
    float W;
};

struct Quaternion {
    float X;
    float Y;
    float Z;
    float W;
};

struct Matrix {
    float M11;
    float M12;
    float M13;
    float M14;
    float M21;
    float M22;
    float M23;
    float M24;
    float M31;
    float M32;
    float M33;
    float M34;
    float M41;
    float M42;
    float M43;
    float M44;
};

struct Vector3I {
    int32_t X;
    int32_t Y;
    int32_t Z;
};

struct Vector3S {
    int16_t X;
    int16_t Y;
    int16_t Z;
};

struct HkMassProperties {
    float Volume;
    float Mass;
    Vector3 CenterOfMass;
    Matrix InertiaTensor;
};

struct HkUniformGridShapeArgsPOD {
    int32_t CellsCount_X;
    int32_t CellsCount_Y;
    int32_t CellsCount_Z;
    float CellSize;
    float CellOffset;
    float CellExpand;
};

struct HkStaticCompoundShape_DecomposeShapeKeyResult {
    int32_t instanceId;
    uint32_t childKey;
};

typedef WINAPI void* (*HkActivationListener_Create_t)(void* onActivate, void* onDeactivate);
typedef WINAPI void* (*HkBallAndSocketConstraintData_Create_t)(void);
typedef WINAPI void (*HkBallAndSocketConstraintData_SetInBodySpaceInternal_t)(void* instance, Vector3 pivotA, Vector3 pivotB);
typedef WINAPI void (*HkBaseSystem_Init_t)(int32_t solverMemorySize, void* log, bool deepProfiling);
typedef WINAPI void (*HkBaseSystem_Quit_t)(void);
typedef WINAPI void* (*HkBaseSystem_InitThread_t)(void);
typedef WINAPI void (*HkBaseSystem_QuitThread_t)(void* threadRouter);
typedef WINAPI void (*HkBaseSystem_GetVersionInfo_t)(void* buffer);
typedef WINAPI void (*HkBaseSystem_GetMemoryStatistics_t)(void* buffer);
typedef WINAPI void (*HkBaseSystem_EnableAssert_t)(int32_t assertId, bool enable);
typedef WINAPI bool (*HkBaseSystem_IsEnabled_t)(int32_t assertId);
typedef WINAPI bool (*HkBaseSystem_IsDestructionEnabled_t)(void);
typedef WINAPI void (*HkBaseSystem_OnSimulationFrameStarted_t)(int64_t frameNumber);
typedef WINAPI void (*HkBaseSystem_OnSimulationFrameFinished_t)(void);
typedef WINAPI int32_t (*HkBaseSystem_GetKeyCodes_t)(void* keyCodes);
typedef WINAPI bool (*HkBaseSystem_IsOutOfMemory_t)(void);
typedef WINAPI int64_t (*HkBaseSystem_GetCurrentMemoryConsumption_t)(void);
typedef WINAPI void* (*HkBoxShape_Create_t)(Vector3 halfExtents);
typedef WINAPI void* (*HkBoxShape_CreateWithConvexRadius_t)(Vector3 halfExtents, float convexRadius);
typedef WINAPI void* (*HkBoxShape_GetShapeFromCompoundShape_t)(void* shape, int32_t shapeIndex);
typedef WINAPI Vector3 (*HkBoxShape_GetHalfExtents_t)(void* instance);
typedef WINAPI void (*HkBoxShape_SetHalfExtents_t)(void* instance, Vector3 value);
typedef WINAPI void* (*HkBreakOffPartsUtil_Create_t)(void* breakLogicHandler, void* breakPartsHandler);
typedef WINAPI void (*HkBreakOffPartsUtil_Release_t)(void* instance);
typedef WINAPI void (*HkBreakOffPartsUtil_RemoveKeysFromListShape_t)(void* entity, void* shapeKeys, int32_t count);
typedef WINAPI void (*HkBreakOffPartsUtil_MarkEntityBreakable_t)(void* instance, void* entity, float maxImpulse);
typedef WINAPI void (*HkBreakOffPartsUtil_MarkPieceBreakable_t)(void* instance, void* entity, uint32_t shapeKey, float maxImpulse);
typedef WINAPI void (*HkBreakOffPartsUtil_SetMaxConstraintImpulse_t)(void* instance, void* entity, float maxConstraintImpulse);
typedef WINAPI void (*HkBreakOffPartsUtil_UnmarkEntityBreakable_t)(void* instance, void* entity);
typedef WINAPI void (*HkBreakOffPartsUtil_UnmarkPieceBreakable_t)(void* instance, void* entity, uint32_t shapeKey);
typedef WINAPI int32_t (*HkBreakOffPoints_Count_t)(void* instance);
typedef WINAPI void (*HkBreakOffPoints_Get_t)(void* instance, int32_t index, void* outPointInfo);
typedef WINAPI void* (*HkBreakableConstraintData_Create_t)(void* data);
typedef WINAPI float (*HkBreakableConstraintData_GetThreshold_t)(void* instance);
typedef WINAPI void (*HkBreakableConstraintData_SetThreshold_t)(void* instance, float value);
typedef WINAPI bool (*HkBreakableConstraintData_GetRemoveFromWorldOnBrake_t)(void* instance);
typedef WINAPI void (*HkBreakableConstraintData_SetRemoveFromWorldOnBrake_t)(void* instance, bool value);
typedef WINAPI bool (*HkBreakableConstraintData_GetReapplyVelocityOnBreak_t)(void* instance);
typedef WINAPI void (*HkBreakableConstraintData_SetReapplyVelocityOnBreak_t)(void* instance, bool value);
typedef WINAPI bool (*HkBreakableConstraintData_GetIsBroken_t)(void* instance, void* constraint);
typedef WINAPI void* (*HkBvCompressedMeshShape_CreateWithSimpleMesh_t)(void* simpleMeshShape);
typedef WINAPI void* (*HkBvCompressedMeshShape_CreateWithParams_t)(void* geometry, int32_t sCount, void* shapes, int32_t tCount, void* transforms, int32_t weldingType, int32_t dataMode, bool isWithConvexRadius, float convexRadius);
typedef WINAPI void* (*HkBvCompressedMeshShape_CreateUnsafe_t)(void* vertices, int32_t verticesCount, void* indices, int32_t indicesCount, void* materials, int32_t materialsCount, int32_t weldingType, float convexRadius);
typedef WINAPI void (*HkBvCompressedMeshShape_GetGeometry_t)(void* instance, void* geometry);
typedef WINAPI uint32_t (*HkBvCompressedMeshShape_GetUserData_t)(void* instance, uint32_t shapeKey);
typedef WINAPI void* (*HkBvShape_Create_t)(void* boundingVolumeShape, void* childShape);
typedef WINAPI void* (*HkBvShape_GetChildShape_t)(void* instance);
typedef WINAPI void* (*HkBvShape_GetBoundingVolumeShape_t)(void* instance);
typedef WINAPI void* (*HkCapsuleShape_Create_t)(Vector3 vertexA, Vector3 vertexB, float radius);
typedef WINAPI float (*HkCapsuleShape_GetRadius_t)(void* instance);
typedef WINAPI Vector3 (*HkCapsuleShape_GetVertexB_t)(void* instance);
typedef WINAPI Vector3 (*HkCapsuleShape_GetVertexA_t)(void* instance);
typedef WINAPI Vector3 (*HkCapsuleShape_GetCentre_t)(void* instance);
typedef WINAPI void* (*HkCharacterProxy_Create_t)(void* info);
typedef WINAPI Vector3 (*HkCharacterProxy_GetPosition_t)(void* instance);
typedef WINAPI void (*HkCharacterProxy_SetPosition_t)(void* instance, Vector3 value);
typedef WINAPI int32_t (*HkCharacterProxy_GetState_t)(void* instance);
typedef WINAPI void (*HkCharacterProxy_SetState_t)(void* instance, int32_t value);
typedef WINAPI void (*HkCharacterProxy_StepSimulation_t)(void* instance, float timeInSec, float posX, float posY, bool jump, bool wantJump, bool atLadder, Vector3 gravity, Vector3 up, Vector3 forward);
typedef WINAPI Vector3 (*HkCharacterProxy_GetLinearVelocity_t)(void* instance);
typedef WINAPI void (*HkCharacterProxy_SetLinearVelocity_t)(void* instance, Vector3 value);
typedef WINAPI void (*HkCharacterProxy_SetUp_t)(void* instance, Vector3 value);
typedef WINAPI void* (*HkCharacterProxyCinfo_Create_t)(void);
typedef WINAPI Vector3 (*HkCharacterProxyCinfo_GetPosition_t)(void* instance);
typedef WINAPI void (*HkCharacterProxyCinfo_SetPosition_t)(void* instance, Vector3 value);
typedef WINAPI Vector3 (*HkCharacterProxyCinfo_GetVelocity_t)(void* instance);
typedef WINAPI void (*HkCharacterProxyCinfo_SetVelocity_t)(void* instance, Vector3 value);
typedef WINAPI float (*HkCharacterProxyCinfo_GetDynamicFriction_t)(void* instance);
typedef WINAPI void (*HkCharacterProxyCinfo_SetDynamicFriction_t)(void* instance, float value);
typedef WINAPI float (*HkCharacterProxyCinfo_GetStaticFriction_t)(void* instance);
typedef WINAPI void (*HkCharacterProxyCinfo_SetStaticFriction_t)(void* instance, float value);
typedef WINAPI float (*HkCharacterProxyCinfo_GetKeepContactTolerance_t)(void* instance);
typedef WINAPI void (*HkCharacterProxyCinfo_SetKeepContactTolerance_t)(void* instance, float value);
typedef WINAPI Vector3 (*HkCharacterProxyCinfo_GetUp_t)(void* instance);
typedef WINAPI void (*HkCharacterProxyCinfo_SetUp_t)(void* instance, Vector3 up);
typedef WINAPI float (*HkCharacterProxyCinfo_GetExtraUpStaticFriction_t)(void* instance);
typedef WINAPI void (*HkCharacterProxyCinfo_SetExtraUpStaticFriction_t)(void* instance, float value);
typedef WINAPI float (*HkCharacterProxyCinfo_GetExtraDownStaticFriction_t)(void* instance);
typedef WINAPI void (*HkCharacterProxyCinfo_SetExtraDownStaticFriction_t)(void* instance, float value);
typedef WINAPI void (*HkCharacterProxyCinfo_SetShapePhantom_t)(void* instance, void* value);
typedef WINAPI void* (*HkCharacterProxyCinfo_GetShapePhantom_t)(void* instance);
typedef WINAPI float (*HkCharacterProxyCinfo_GetKeepDistance_t)(void* instance);
typedef WINAPI void (*HkCharacterProxyCinfo_SetKeepDistance_t)(void* instance, float value);
typedef WINAPI float (*HkCharacterProxyCinfo_GetContactAngleSensitivity_t)(void* instance);
typedef WINAPI void (*HkCharacterProxyCinfo_SetContactAngleSensitivity_t)(void* instance, float value);
typedef WINAPI int32_t (*HkCharacterProxyCinfo_GetUserPlanes_t)(void* instance);
typedef WINAPI void (*HkCharacterProxyCinfo_SetUserPlanes_t)(void* instance, int32_t value);
typedef WINAPI float (*HkCharacterProxyCinfo_GetMaxCharacterSpeedForSolver_t)(void* instance);
typedef WINAPI void (*HkCharacterProxyCinfo_SetMaxCharacterSpeedForSolver_t)(void* instance, float value);
typedef WINAPI float (*HkCharacterProxyCinfo_GetCharacterStrength_t)(void* instance);
typedef WINAPI void (*HkCharacterProxyCinfo_SetCharacterStrength_t)(void* instance, float value);
typedef WINAPI float (*HkCharacterProxyCinfo_GetCharacterMass_t)(void* instance);
typedef WINAPI void (*HkCharacterProxyCinfo_SetCharacterMass_t)(void* instance, float value);
typedef WINAPI float (*HkCharacterProxyCinfo_GetMaxSlope_t)(void* instance);
typedef WINAPI void (*HkCharacterProxyCinfo_SetMaxSlope_t)(void* instance, float value);
typedef WINAPI float (*HkCharacterProxyCinfo_GetPenetrationRecoverySpeed_t)(void* instance);
typedef WINAPI void (*HkCharacterProxyCinfo_SetPenetrationRecoverySpeed_t)(void* instance, float value);
typedef WINAPI int32_t (*HkCharacterProxyCinfo_GetMaxCastIterations_t)(void* instance);
typedef WINAPI void (*HkCharacterProxyCinfo_SetMaxCastIterations_t)(void* instance, int32_t value);
typedef WINAPI bool (*HkCharacterProxyCinfo_GetRefreshManifoldInCheckSupport_t)(void* instance);
typedef WINAPI void (*HkCharacterProxyCinfo_SetRefreshManifoldInCheckSupport_t)(void* instance, bool value);
typedef WINAPI void* (*HkCharacterRigidBody_Create_t)(void* characterRigidBodyCinfo, float maxCharacterSpeed);
typedef WINAPI void* (*HkCharacterRigidBody_GetCharacterRigidbody_t)(void* instance);
typedef WINAPI void (*HkCharacterRigidBody_SetWalkingState_t)(void* instance, void* shape, float jumpHeight, float gainSpeed, float maxCharacterSpeed);
typedef WINAPI void (*HkCharacterRigidBody_SetFlyingState_t)(void* instance, void* shape, float maxCharacterSpeed, float maxAcceleration);
typedef WINAPI void (*HkCharacterRigidBody_SetLadderState_t)(void* instance, float maxCharacterSpeed, float maxAcceleration);
typedef WINAPI void (*HkCharacterRigidBody_SetDefaultShape_t)(void* instance, void* shape);
typedef WINAPI void (*HkCharacterRigidBody_SetShapeForCrouch_t)(void* instance, void* shape);
typedef WINAPI Vector3 (*HkCharacterRigidBody_GetPosition_t)(void* instance);
typedef WINAPI void (*HkCharacterRigidBody_SetPosition_t)(void* instance, Vector3 value);
typedef WINAPI int32_t (*HkCharacterRigidBody_GetState_t)(void* instance);
typedef WINAPI void (*HkCharacterRigidBody_SetState_t)(void* instance, int32_t value);
typedef WINAPI void (*HkCharacterRigidBody_StepSimulation_t)(void* instance, float timeInSec, bool Jump, bool WantJump, bool AtLadder, float PosX, float PosY, float Speed, float Elevate, Vector3 Up, Vector3 Forward, Vector3 ElevateVector, Vector3 ElevateUpVector, Vector3 Gravity, float myJumpHeight, void* AngularVelocity);
typedef WINAPI void (*HkCharacterRigidBody_UpdateVelocity_t)(void* instance, float timeInSec, bool Supported, Vector3 AngularVelocity, Quaternion DesiredOrientation);
typedef WINAPI void (*HkCharacterRigidBody_UpdateSupport_t)(void* instance, float timeInSec);
typedef WINAPI void (*HkCharacterRigidBody_SetRigidBodyTransform_t)(void* instance, Matrix world);
typedef WINAPI Matrix (*HkCharacterRigidBody_GetRigidBodyTransform_t)(void* instance);
typedef WINAPI Vector3 (*HkCharacterRigidBody_GetLinearVelocity_t)(void* instance);
typedef WINAPI void (*HkCharacterRigidBody_SetLinearVelocity_t)(void* instance, Vector3 value);
typedef WINAPI void (*HkCharacterRigidBody_ApplyLinearImpulse_t)(void* instance, Vector3 impulse);
typedef WINAPI void (*HkCharacterRigidBody_ApplyAngularImpulse_t)(void* instance, Vector3 impulse);
typedef WINAPI void (*HkCharacterRigidBody_SetSupportDistance_t)(void* instance, float distance);
typedef WINAPI void (*HkCharacterRigidBody_SetHardSupportDistance_t)(void* instance, float distance);
typedef WINAPI Vector3 (*HkCharacterRigidBody_GetAngularVelocity_t)(void* instance);
typedef WINAPI void (*HkCharacterRigidBody_SetAngularVelocity_t)(void* instance, Vector3 value);
typedef WINAPI bool (*HkCharacterRigidBody_IsSupportedByFloatingObject_t)(void* instance);
typedef WINAPI bool (*HkCharacterRigidBody_IsSupported_t)(void* instance);
typedef WINAPI Vector3 (*HkCharacterRigidBody_GetSupportNormal_t)(void* instance);
typedef WINAPI Vector3 (*HkCharacterRigidBody_GetGroundVelocity_t)(void* instance);
typedef WINAPI bool (*HkCharacterRigidBody_GetUseSupportInfoQuery_t)(void* instance);
typedef WINAPI void (*HkCharacterRigidBody_SetUseSupportInfoQuery_t)(void* instance, bool value);
typedef WINAPI void (*HkCharacterRigidBody_SetPreviousSupportedState_t)(void* instance, bool supported);
typedef WINAPI void (*HkCharacterRigidBody_ResetSurfaceVelocity_t)(void* instance);
typedef WINAPI void (*HkCharacterRigidBody_SetMaxSlope_t)(void* instance, float value);
typedef WINAPI float (*HkCharacterRigidBody_GetMaxSlope_t)(void* instance);
typedef WINAPI void (*HkCharacterRigidBody_GetSupportBodies_t)(void* instance, void* size, void* version, void* list);
typedef WINAPI void* (*HkCharacterRigidBodyCinfo_Create_t)(void);
typedef WINAPI int32_t (*HkCharacterRigidBodyCinfo_GetCollisionFilterInfo_t)(void* instance);
typedef WINAPI void (*HkCharacterRigidBodyCinfo_SetCollisionFilterInfo_t)(void* instance, int32_t value);
typedef WINAPI void* (*HkCharacterRigidBodyCinfo_GetShape_t)(void* instance);
typedef WINAPI void (*HkCharacterRigidBodyCinfo_SetShape_t)(void* instance, void* value);
typedef WINAPI Vector3 (*HkCharacterRigidBodyCinfo_GetPosition_t)(void* instance);
typedef WINAPI void (*HkCharacterRigidBodyCinfo_SetPosition_t)(void* instance, Vector3 value);
typedef WINAPI Quaternion (*HkCharacterRigidBodyCinfo_GetRotation_t)(void* instance);
typedef WINAPI void (*HkCharacterRigidBodyCinfo_SetRotation_t)(void* instance, Quaternion value);
typedef WINAPI float (*HkCharacterRigidBodyCinfo_GetMass_t)(void* instance);
typedef WINAPI void (*HkCharacterRigidBodyCinfo_SetMass_t)(void* instance, float value);
typedef WINAPI float (*HkCharacterRigidBodyCinfo_GetFriction_t)(void* instance);
typedef WINAPI void (*HkCharacterRigidBodyCinfo_SetFriction_t)(void* instance, float value);
typedef WINAPI float (*HkCharacterRigidBodyCinfo_GetMaxLinearVelocity_t)(void* instance);
typedef WINAPI void (*HkCharacterRigidBodyCinfo_SetMaxLinearVelocity_t)(void* instance, float value);
typedef WINAPI float (*HkCharacterRigidBodyCinfo_GetAllowedPenetrationDepth_t)(void* instance);
typedef WINAPI void (*HkCharacterRigidBodyCinfo_SetAllowedPenetrationDepth_t)(void* instance, float value);
typedef WINAPI Vector3 (*HkCharacterRigidBodyCinfo_GetUp_t)(void* instance);
typedef WINAPI void (*HkCharacterRigidBodyCinfo_SetUp_t)(void* instance, Vector3 value);
typedef WINAPI float (*HkCharacterRigidBodyCinfo_GetMaxSlope_t)(void* instance);
typedef WINAPI void (*HkCharacterRigidBodyCinfo_SetMaxSlope_t)(void* instance, float value);
typedef WINAPI float (*HkCharacterRigidBodyCinfo_GetMaxForce_t)(void* instance);
typedef WINAPI void (*HkCharacterRigidBodyCinfo_SetMaxForce_t)(void* instance, float value);
typedef WINAPI float (*HkCharacterRigidBodyCinfo_GetUnweldingHeightOffsetFactor_t)(void* instance);
typedef WINAPI void (*HkCharacterRigidBodyCinfo_SetUnweldingHeightOffsetFactor_t)(void* instance, float value);
typedef WINAPI float (*HkCharacterRigidBodyCinfo_GetMaxSpeedForSimplexSolver_t)(void* instance);
typedef WINAPI void (*HkCharacterRigidBodyCinfo_SetMaxSpeedForSimplexSolver_t)(void* instance, float value);
typedef WINAPI float (*HkCharacterRigidBodyCinfo_GetSupportDistance_t)(void* instance);
typedef WINAPI void (*HkCharacterRigidBodyCinfo_SetSupportDistance_t)(void* instance, float value);
typedef WINAPI float (*HkCharacterRigidBodyCinfo_GetHardSupportDistance_t)(void* instance);
typedef WINAPI void (*HkCharacterRigidBodyCinfo_SetHardSupportDistance_t)(void* instance, float value);
typedef WINAPI void* (*HkCogWheelConstraintData_Create_t)(void);
typedef WINAPI void (*HkCogWheelConstraintData_SetInWorldSpace_t)(void* instance, Matrix bodyATransform, Matrix bodyBTransform, Vector3 rotationPivotA, Vector3 rotationAxisA, float radiusA, Vector3 rotationPivotB, Vector3 rotationAxisB, float radiusB);
typedef WINAPI void (*HkCogWheelConstraintData_SetInBodySpaceInternal_t)(void* instance, Vector3 rotationPivotAInA, Vector3 rotationAxisAInA, float radiusA, Vector3 rotationPivotBInB, Vector3 rotationAxisBInB, float radiusB);
typedef WINAPI int32_t (*HkCollisionEvent_GetSource_t)(void* instance);
typedef WINAPI void* (*HkCollisionEvent_GetRigidBody_t)(void* instance, int32_t bodyIndex);
typedef WINAPI void* (*HkCollisionEvent_GetBodyA_t)(void* instance);
typedef WINAPI void* (*HkCollisionEvent_GetBodyB_t)(void* instance);
typedef WINAPI bool (*HkCollisionEvent_SetImpulse_t)(void* instance, float impulse);
typedef WINAPI void (*HkCollisionEvent_SetImpulseScaling_t)(void* instance, float impulse, float maxAccel);
typedef WINAPI int32_t (*HkCollisionEvent_GetContactPointCount_t)(void* instance);
typedef WINAPI void (*HkCollisionEvent_Disable_t)(void* instance);
typedef WINAPI void* (*HkCollisionEvent_GetContactPointPropertiesAt_t)(void* instance, int32_t index);
typedef WINAPI void (*HkCollisionEvent_GetOffsets_t)(void* bodyPointerOffset);
typedef WINAPI void* (*HkConstraint_Create_t)(void* entityA, void* entityB, void* data, int32_t priority);
typedef WINAPI void (*HkConstraint_AddConstraintListener_t)(void* instance, void* listener);
typedef WINAPI void (*HkConstraint_RemoveConstraintListener_t)(void* instance, void* listener);
typedef WINAPI void (*HkConstraint_ReplaceEntity_t)(void* instance, void* oldEntity, void* newEntity);
typedef WINAPI void (*HkConstraint_SetVirtualMassInverse_t)(void* instance, Vector4 invMassA, Vector4 invMassB);
typedef WINAPI int32_t (*HkConstraint_GetPriority_t)(void* instance);
typedef WINAPI void (*HkConstraint_SetPriority_t)(void* instance, int32_t value);
typedef WINAPI bool (*HkConstraint_GetWantRuntime_t)(void* instance);
typedef WINAPI void (*HkConstraint_SetWantRuntime_t)(void* instance, bool value);
typedef WINAPI bool (*HkConstraint_IsInWorld_t)(void* instance);
typedef WINAPI void* (*HkConstraint_GetRigidBodyA_t)(void* instance);
typedef WINAPI void* (*HkConstraint_GetRigidBodyB_t)(void* instance);
typedef WINAPI bool (*HkConstraint_GetEnabled_t)(void* instance);
typedef WINAPI void (*HkConstraint_SetEnabled_t)(void* instance, bool value);
typedef WINAPI void (*HkConstraint_GetPivotsInWorld_t)(void* instance, void* outPivotA, void* outPivotB);
typedef WINAPI uint64_t (*HkConstraint_GetUserData_t)(void* instance);
typedef WINAPI void (*HkConstraint_SetUserData_t)(void* instance, uint64_t value);
typedef WINAPI void (*HkConstraint_AddCenterOfMassModifierAtom_t)(void* instance, Vector3 modifierA, Vector3 modifierB);
typedef WINAPI void (*HkConstraint_FindConnectedConstraints_t)(void* rigidBody, void* reader, void* userData);
typedef WINAPI float (*HkConstraintData_GetMaximumLinearImpulse_t)(void* instance);
typedef WINAPI void (*HkConstraintData_SetMaximumLinearImpulse_t)(void* instance, float value);
typedef WINAPI float (*HkConstraintData_GetMaximumAngularImpulse_t)(void* instance);
typedef WINAPI void (*HkConstraintData_SetMaximumAngularImpulse_t)(void* instance, float value);
typedef WINAPI float (*HkConstraintData_GetBreachImpulse_t)(void* instance);
typedef WINAPI void (*HkConstraintData_SetBreachImpulse_t)(void* instance, float value);
typedef WINAPI float (*HkConstraintData_GetInertiaStabilizationFactor_t)(void* instance);
typedef WINAPI void (*HkConstraintData_SetInertiaStabilizationFactor_t)(void* instance, float value);
typedef WINAPI void (*HkConstraintData_SetSolvingMethod_t)(void* instance, int32_t method);
typedef WINAPI void* (*HkConstraintListener_Create_t)(void);
typedef WINAPI void (*HkConstraintListener_Release_t)(void* instance);
typedef WINAPI void (*HkConstraintListener_SetCallbacks_t)(void* instance, void* onAdded, void* onRemoved, void* onBreaking);
typedef WINAPI void* (*HkConstraintProjectorListener_Create_t)(void* world);
typedef WINAPI void (*HkConstraintProjectorListener_Release_t)(void* listener);
typedef WINAPI int32_t (*HkConstraintStabilizationUtil_StabilizeRagdollInertias_t)(void* physicsSystem, float stabilizationAmount, float solverStabilizationAmount);
typedef WINAPI void* (*HkContactListener_Create_t)(void* onContact, void* collisionAdded, void* collisionRemoved, int32_t callbackLimit);
typedef WINAPI void (*HkContactListener_SetCallbackLimit_t)(void* instance, int32_t value);
typedef WINAPI void (*HkContactListener_ResetLimit_t)(void* instance);
typedef WINAPI Vector3 (*HkContactPoint_GetPosition_t)(void* instance);
typedef WINAPI void (*HkContactPoint_SetPosition_t)(void* instance, Vector3 value);
typedef WINAPI Vector4 (*HkContactPoint_GetNormalAndDistance_t)(void* instance);
typedef WINAPI void (*HkContactPoint_SetNormalAndDistance_t)(void* instance, Vector4 value);
typedef WINAPI Vector3 (*HkContactPoint_GetNormal_t)(void* instance);
typedef WINAPI void (*HkContactPoint_SetNormal_t)(void* instance, Vector3 value);
typedef WINAPI float (*HkContactPoint_GetDistance_t)(void* instance);
typedef WINAPI void (*HkContactPoint_SetDistance_t)(void* instance, float value);
typedef WINAPI void (*HkContactPoint_Flip_t)(void* instance);
typedef WINAPI void* (*HkContactPointEvent_GetBase_t)(void* instance);
typedef WINAPI bool (*HkContactPointEvent_IsToi_t)(void* instance);
typedef WINAPI float (*HkContactPointEvent_GetSeparatingVelocity_t)(void* instance);
typedef WINAPI void (*HkContactPointEvent_SetSeparatingVelocity_t)(void* instance, float value);
typedef WINAPI float (*HkContactPointEvent_GetRotateNormal_t)(void* instance);
typedef WINAPI void (*HkContactPointEvent_SetRotateNormal_t)(void* instance, float value);
typedef WINAPI int32_t (*HkContactPointEvent_GetEventType_t)(void* instance);
typedef WINAPI void* (*HkContactPointEvent_GetContactPoint_t)(void* instance);
typedef WINAPI void* (*HkContactPointEvent_GetContactProperties_t)(void* instance);
typedef WINAPI bool (*HkContactPointEvent_GetFiringCallbacksForFullManifold_t)(void* instance);
typedef WINAPI bool (*HkContactPointEvent_GetFirstCallbackForFullManifold_t)(void* instance);
typedef WINAPI bool (*HkContactPointEvent_GetLastCallbackForFullManifold_t)(void* instance);
typedef WINAPI uint16_t (*HkContactPointEvent_GetContactPointId_t)(void* instance);
typedef WINAPI void (*HkContactPointEvent_AccessVelocities_t)(void* instance, int32_t bodyIndex);
typedef WINAPI void (*HkContactPointEvent_UpdateVelocities_t)(void* instance, int32_t bodyIndex);
typedef WINAPI uint32_t (*HkContactPointEvent_GetShapeKey_t)(void* instance, int32_t bodyIdx);
typedef WINAPI uint32_t (*HkContactPointEvent_GetShapeKeyWithShapeID_t)(void* instance, int32_t bodyIdx, int32_t shapeIdx);
typedef WINAPI void (*HkContactPointEvent_GetFieldOffsets_t)(void* separatingVelocityOffset, void* typeOffset, void* propertiesOffset, void* contactPointOffset, void* firingCallbacksForFullManifoldOffset, void* firstCallbackForFullManifoldOffset, void* lastCallbackForFullManifoldOffset);
typedef WINAPI float (*HkContactPointProperties_GetImpulseApplied_t)(void* instance);
typedef WINAPI float (*HkContactPointProperties_GetInternalSolverData_t)(void* instance);
typedef WINAPI bool (*HkContactPointProperties_WasUsed_t)(void* instance);
typedef WINAPI float (*HkContactPointProperties_GetFriction_t)(void* instance);
typedef WINAPI void (*HkContactPointProperties_SetFriction_t)(void* instance, float value);
typedef WINAPI float (*HkContactPointProperties_GetRestitution_t)(void* instance);
typedef WINAPI void (*HkContactPointProperties_SetRestitution_t)(void* instance, float value);
typedef WINAPI bool (*HkContactPointProperties_IsPotential_t)(void* instance);
typedef WINAPI float (*HkContactPointProperties_GetMaxImpulsePerStep_t)(void* instance);
typedef WINAPI void (*HkContactPointProperties_SetMaxImpulsePerStep_t)(void* instance, float value);
typedef WINAPI float (*HkContactPointProperties_GetMaxImpulse_t)(void* instance);
typedef WINAPI void (*HkContactPointProperties_SetMaxImpulse_t)(void* instance, float value);
typedef WINAPI bool (*HkContactPointProperties_GetIsDisabled_t)(void* instance);
typedef WINAPI void (*HkContactPointProperties_SetIsDisabled_t)(void* instance, bool value);
typedef WINAPI bool (*HkContactPointProperties_GetIsNew_t)(void* instance);
typedef WINAPI void (*HkContactPointProperties_SetIsNew_t)(void* instance, bool value);
typedef WINAPI uint32_t (*HkContactPointProperties_GetUserData_t)(void* instance);
typedef WINAPI void (*HkContactPointProperties_SetUserData_t)(void* instance, uint32_t value);
typedef WINAPI void (*HkContactPointProperties_GetFieldOffsets_t)(void* userDataOffset);
typedef WINAPI void* (*HkContactSoundListener_Create_t)(void* onContact);
typedef WINAPI void* (*HkConvexShape_GetConvexShapeFromCompoundShape_t)(void* shape, int32_t shapeIndex);
typedef WINAPI float (*HkConvexShape_GetConvexRadius_t)(void* instance);
typedef WINAPI void (*HkConvexShape_SetConvexRadius_t)(void* instance, float value);
typedef WINAPI float (*HkConvexShape_GetDefaultConvexRadius_t)(void);
typedef WINAPI void* (*HkConvexTransformShape_Create_t)(void* childShape, Matrix transform, int32_t refPolicy);
typedef WINAPI void* (*HkConvexTransformShape_CreateTranslated_t)(void* childShape, Vector3 translation, Quaternion rotation, Vector3 scale, int32_t refPolicy);
typedef WINAPI void* (*HkConvexTransformShape_GetChildShape_t)(void* instance);
typedef WINAPI Matrix (*HkConvexTransformShape_GetTransform_t)(void* instance);
typedef WINAPI void* (*HkConvexTranslateShape_CreateWithChild_t)(void* childShape, Vector3 translation, int32_t refPolicy);
typedef WINAPI void* (*HkConvexTranslateShape_GetChildShape_t)(void* instance);
typedef WINAPI Vector3 (*HkConvexTranslateShape_GetTranslation_t)(void* instance);
typedef WINAPI void* (*HkConvexVerticesShape_Create_t)(void* verts, int32_t count);
typedef WINAPI void* (*HkConvexVerticesShape_CreateWithRadius_t)(void* verts, int32_t count, bool shrink, float convexRadius);
typedef WINAPI Vector3 (*HkConvexVerticesShape_GetCenter_t)(void* instance);
typedef WINAPI int32_t (*HkConvexVerticesShape_GetVertexCount_t)(void* instance);
typedef WINAPI int32_t (*HkConvexVerticesShape_GetFaceCount_t)(void* instance);
typedef WINAPI void (*HkConvexVerticesShape_GetFaces_t)(void* instance, void* faceIndexCount, void* faceIndices, void* faceCount, void* faceVertexCounts);
typedef WINAPI void (*HkConvexVerticesShape_GetVertices_t)(void* instance, void* vertexBuffer);
typedef WINAPI void (*HkConvexVerticesShape_GetGeometry_t)(void* instance, void* geometry, void* center);
typedef WINAPI void* (*HkCustomWheelConstraintData_Create_t)(void);
typedef WINAPI bool (*HkCustomWheelConstraintData_GetLimitsEnabled_t)(void* instance);
typedef WINAPI void (*HkCustomWheelConstraintData_SetLimitsEnabled_t)(void* instance, bool value);
typedef WINAPI float (*HkCustomWheelConstraintData_GetSuspensionMinLimit_t)(void* instance);
typedef WINAPI void (*HkCustomWheelConstraintData_SetSuspensionMinLimit_t)(void* instance, float value);
typedef WINAPI float (*HkCustomWheelConstraintData_GetSuspensionMaxLimit_t)(void* instance);
typedef WINAPI void (*HkCustomWheelConstraintData_SetSuspensionMaxLimit_t)(void* instance, float value);
typedef WINAPI bool (*HkCustomWheelConstraintData_GetFrictionEnabled_t)(void* instance);
typedef WINAPI void (*HkCustomWheelConstraintData_SetFrictionEnabled_t)(void* instance, bool value);
typedef WINAPI float (*HkCustomWheelConstraintData_GetMaxFrictionTorque_t)(void* instance);
typedef WINAPI void (*HkCustomWheelConstraintData_SetMaxFrictionTorque_t)(void* instance, float value);
typedef WINAPI void (*HkCustomWheelConstraintData_SetInBodySpaceInternal_t)(void* instance, Vector3 pivotA, Vector3 pivotB, Vector3 axleA, Vector3 axleB, Vector3 suspensionAxisB, Vector3 steeringAxisB);
typedef WINAPI void (*HkCustomWheelConstraintData_SetSuspensionStrength_t)(void* instance, float value);
typedef WINAPI void (*HkCustomWheelConstraintData_SetSuspensionDamping_t)(void* instance, float value);
typedef WINAPI void (*HkCustomWheelConstraintData_SetSteeringAngle_t)(void* instance, float value);
typedef WINAPI void (*HkCustomWheelConstraintData_SetAngleLimits_t)(void* instance, float min, float max);
typedef WINAPI float (*HkCustomWheelConstraintData_GetAngleLimitsMin_t)(void* instance);
typedef WINAPI float (*HkCustomWheelConstraintData_GetAngleLimitsMax_t)(void* instance);
typedef WINAPI void (*HkCustomWheelConstraintData_DisableLimits_t)(void* instance);
typedef WINAPI float (*HkCustomWheelConstraintData_GetCurrentAngle_t)(void* constraint);
typedef WINAPI void (*HkCustomWheelConstraintData_SetCurrentAngle_t)(void* constraint, float angle);
typedef WINAPI void* (*HkCylinderShape_Create_t)(Vector3 vertexA, Vector3 vertexB, float cylinderRadius);
typedef WINAPI void* (*HkCylinderShape_CreateWithConvexRadius_t)(Vector3 vertexA, Vector3 vertexB, float cylinderRadius, float convexRadius);
typedef WINAPI Vector3 (*HkCylinderShape_GetVertexB_t)(void* instance);
typedef WINAPI Vector3 (*HkCylinderShape_GetVertexA_t)(void* instance);
typedef WINAPI void (*HkCylinderShape_SetVertexB_t)(void* instance, Vector3 vertex);
typedef WINAPI void (*HkCylinderShape_SetVertexA_t)(void* instance, Vector3 vertex);
typedef WINAPI float (*HkCylinderShape_GetRadius_t)(void* instance);
typedef WINAPI void (*HkCylinderShape_SetRadius_t)(void* instance, float radius);
typedef WINAPI void (*HkCylinderShape_SetNumberOfVirtualSideSegments_t)(int32_t number);
typedef WINAPI void* (*HkEasePenetrationAction_Create_t)(void* body, float duration);
typedef WINAPI float (*HkEasePenetrationAction_GetInitialAdditionalAllowedPenetrationDepth_t)(void* instance);
typedef WINAPI void (*HkEasePenetrationAction_SetInitialAdditionalAllowedPenetrationDepth_t)(void* instance, float initialAdditionalAllowedPenetrationDepth);
typedef WINAPI float (*HkEasePenetrationAction_GetInitialAllowedPenetrationDepthMultiplier_t)(void* instance);
typedef WINAPI void (*HkEasePenetrationAction_SetInitialAllowedPenetrationDepthMultiplier_t)(void* instance, float initialAllowedPenetrationDepthMultiplier);
typedef WINAPI void (*HkEntity_AddActivationListener_t)(void* instance, void* listener);
typedef WINAPI void (*HkEntity_RemoveActivationListener_t)(void* instance, void* listener);
typedef WINAPI void (*HKEntity_AddEntityListener_t)(void* instance, void* listener);
typedef WINAPI void (*HKEntity_RemoveEntityListener_t)(void* instance, void* listener);
typedef WINAPI void (*HkEntity_SetContactListener_t)(void* instance, void* listener, bool value);
typedef WINAPI int32_t (*HkEntity_GetQuality_t)(void* instance);
typedef WINAPI void (*HkEntity_SetQuality_t)(void* instance, int32_t value);
typedef WINAPI bool (*HkEntity_IsFixed_t)(void* instance);
typedef WINAPI bool (*HkEntity_IsFixedOrKeyframed_t)(void* instance);
typedef WINAPI int32_t (*HkRigidBody_GetMotionType_t)(void* instance);
typedef WINAPI int32_t (*HkEntity_GetContactPointCallbackDelay_t)(void* instance);
typedef WINAPI void (*HkEntity_SetContactPointCallbackDelay_t)(void* instance, int32_t value);
typedef WINAPI void (*HkEntity_SetProperty_t)(void* instance, int32_t key, float v);
typedef WINAPI bool (*HkEntity_HasProperty_t)(void* instance, int32_t key);
typedef WINAPI void (*HkEntity_RemoveProperty_t)(void* instance, int32_t key);
typedef WINAPI Quaternion (*HkRigidBody_GetRotation_t)(void* instance);
typedef WINAPI void (*HkRigidBody_SetRotation_t)(void* instance, Quaternion value);
typedef WINAPI Vector3 (*HkRigidBody_GetPosition_t)(void* instance);
typedef WINAPI void (*HkRigidBody_SetPosition_t)(void* instance, Vector3 value);
typedef WINAPI void (*HkRigidBody_Activate_t)(void* instance);
typedef WINAPI void (*HkRigidBody_ActivateAsCriticalOperation_t)(void* instance);
typedef WINAPI void (*HkRigidBody_Deactivate_t)(void* instance);
typedef WINAPI void (*HkRigidBody_UpdateMotionType_t)(void* instance, int32_t type);
typedef WINAPI bool (*HkRigidBody_GetIsActive_t)(void* instance);
typedef WINAPI void (*HkRigidBody_RequestDeactivation_t)(void* instance);
typedef WINAPI Vector3 (*HkRigidBody_GetLinearVelocity_t)(void* instance);
typedef WINAPI void (*HkRigidBody_SetLinearVelocity_t)(void* instance, Vector3 value);
typedef WINAPI Vector3 (*HkRigidBody_GetAngularVelocity_t)(void* instance);
typedef WINAPI void (*HkRigidBody_SetAngularVelocity_t)(void* instance, Vector3 value);
typedef WINAPI void (*HkEntity_GetFieldOffsets_t)(void* userDataOffset, void* transformOffset, void* rotationOffset, void* linearVelocityOffset, void* angularVelocityOffset, void* motionTypeOffset, void* simulationIslandOffset, void* worldOffset);
typedef WINAPI void* (*HkEntityListener_Create_t)(void* onAdd, void* onRemove, void* onDelete, void* onShapeChange, void* onMotionTypeChange);
typedef WINAPI void (*HkEntityListener_Release_t)(void* entityListener);
typedef WINAPI void* (*HkFixedConstraintData_Create_t)(void);
typedef WINAPI void (*HkFixedConstraintData_SetInBodySpaceInternal_t)(void* instance, Matrix pivotA, Matrix pivotB);
typedef WINAPI void (*HkFixedConstraintData_SetInWorldSpace_t)(void* instance, Matrix bodyATransform, Matrix bodyBTransform, Matrix pivot);
typedef WINAPI bool (*HkFixedConstraintData_IsValid_t)(void* instance);
typedef WINAPI bool (*HkFixedConstraintData_SetInertiaStabilizationFactor_t)(void* instance, float value);
typedef WINAPI bool (*HkFixedConstraintData_GetInertiaStabilizationFactor_t)(void* instance, void* outResult);
typedef WINAPI float (*HkFixedConstraintData_GetSolverImpulseInLastStep_t)(void* constraint, uint8_t constraintAtom);
typedef WINAPI void* (*HkGeometry_Create_t)(void);
typedef WINAPI void* (*HkGeometry_CreateWithParams_t)(int32_t vCount, void* vertices, int32_t iCount, void* indices, int32_t mCount, void* materials);
typedef WINAPI void (*HkGeometry_Destroy_t)(void* instance);
typedef WINAPI int32_t (*HkGeometry_GetTriangleCount_t)(void* instance);
typedef WINAPI int32_t (*HkGeometry_GetVertexCount_t)(void* instance);
typedef WINAPI void (*HkGeometry_Append_t)(void* instance, void* geometry, Matrix matrix);
typedef WINAPI void (*HkGeometry_GetTriangle_t)(void* instance, int32_t triangleIndex, void* outTriangle);
typedef WINAPI Vector3 (*HkGeometry_GetVertex_t)(void* instance, int32_t vertexIndex);
typedef WINAPI void (*HkGeometry_SetGeometry_t)(void* instance, int32_t vCount, void* vertices, int32_t iCount, void* indices, int32_t mCount, void* materials);
typedef WINAPI void* (*HkGridShape_Create_t)(float cellSize, int32_t policy);
typedef WINAPI float (*HkGridShape_GetCellSize_t)(void* instance);
typedef WINAPI int32_t (*HkGridShape_GetShapeCount_t)(void* instance);
typedef WINAPI void (*HkGridShape_SetDebugRigidBody_t)(void* instance, void* rigidBody);
typedef WINAPI void* (*HkGridShape_GetDebugRigidBody_t)(void* instance);
typedef WINAPI void (*HkGridShape_SetDebugDraw_t)(void* instance, bool debugDraw);
typedef WINAPI bool (*HkGridShape_GetDebugDraw_t)(void* instance);
typedef WINAPI void (*HkGridShape_AddShapes_t)(void* instance, void* shapes, uint32_t count, Vector3S min, Vector3S max);
typedef WINAPI bool (*HkGridShape_Contains_t)(void* instance, int16_t x, int16_t y, int16_t z);
typedef WINAPI void (*HkGridShape_GetShape_t)(void* instance, Vector3I pos, void* shapeBuffer);
typedef WINAPI void (*HkGridShape_GetShapeInfo_t)(void* instance, int32_t index, void* min, void* max, void* shapeBuffer);
typedef WINAPI int32_t (*HkGridShape_GetShapeInfoCount_t)(void* instance);
typedef WINAPI void (*HkGridShape_GetShapeMin_t)(void* instance, uint32_t shapeKey, void* min);
typedef WINAPI void (*HkGridShape_GetShapesInInterval_t)(void* instance, Vector3 min, Vector3 max, void* shapeBuffer);
typedef WINAPI void (*HkGridShape_GetChildBounds_t)(void* instance, uint32_t shapeKey, void* min, void* max);
typedef WINAPI void (*HkGridShape_RemoveShapes_t)(void* instance, void* positions, uint32_t count, void* results);
typedef WINAPI void (*HkGridShape_GetCellRanges_t)(void* instance, void* positions, uint32_t count, void* results);
typedef WINAPI uint32_t (*HkGroupFilter_CalcFilterInfo_t)(int32_t layer, int32_t systemGroup, int32_t subSystemId, int32_t subSystemDontCollideWith);
typedef WINAPI int32_t (*HkGroupFilter_GetLayerFromFilterInfo_t)(uint32_t filterInfo);
typedef WINAPI int32_t (*HkGroupFilter_getSubSystemDontCollideWithFromFilterInfo_t)(uint32_t filterInfo);
typedef WINAPI int32_t (*HkGroupFilter_GetSubSystemIdFromFilterInfo_t)(uint32_t filterInfo);
typedef WINAPI int32_t (*HkGroupFilter_GetSystemGroupFromFilterInfo_t)(uint32_t filterInfo);
typedef WINAPI int32_t (*HkGroupFilter_SetLayer_t)(uint32_t filterInfo, int32_t newLayer);
typedef WINAPI void (*HkGroupFilter_DisableCollisionsBetween_t)(void* instance, int32_t layerA, int32_t layerB);
typedef WINAPI void (*HkGroupFilter_DisableCollisionsUsingBitfield_t)(void* instance, uint32_t layerBitsA, uint32_t layerBitsB);
typedef WINAPI void (*HkGroupFilter_EnableCollisionsBetween_t)(void* instance, int32_t layerA, int32_t layerB);
typedef WINAPI void (*HkGroupFilter_EnableCollisionsUsingBitfield_t)(void* instance, uint32_t layerBitsA, uint32_t layerBitsB);
typedef WINAPI int32_t (*HkGroupFilter_GetNewSystemGroup_t)(void* instance);
typedef WINAPI void (*HkGlobal_ReleasePtr_t)(void* ptr);
typedef WINAPI void (*HkGlobal_ReleaseString_t)(void* ptr);
typedef WINAPI void (*HkGlobal_ReleaseArrayPtr_t)(void* ptr);
typedef WINAPI void* (*HkHingeConstraintData_Create_t)(void);
typedef WINAPI void (*HkHingeConstraintData_SetInBodySpaceInternal_t)(void* instance, Vector3 pivotA, Vector3 pivotB, Vector3 axisA, Vector3 axisB);
typedef WINAPI void (*HkHingeConstraintData_SetInWorldSpace_t)(void* instance, Matrix bodyATransform, Matrix bodyBTransform, Vector3 pivot, Vector3 axis);
typedef WINAPI bool (*HkHingeConstraintData_SetInertiaStabilizationFactor_t)(void* instance, float value);
typedef WINAPI bool (*HkHingeConstraintData_GetInertiaStabilizationFactor_t)(void* instance, void* outResult);
typedef WINAPI void* (*HkInertiaTensorComputer_Create_t)(void);
typedef WINAPI void (*HkInertiaTensorComputer_CombineMassPropertiesInstance_t)(void* instance, void* massElements, int32_t count, void* returnMassProperties);
typedef WINAPI void (*HkInertiaTensorComputer_Release_t)(void* instance);
typedef WINAPI void (*HkInertiaTensorComputer_ComputeBoxVolumeMassProperties_t)(Vector3 halfExtents, float mass, void* returnMassProperties);
typedef WINAPI void (*HkInertiaTensorComputer_ComputeCapsuleVolumeMassProperties_t)(Vector3 startAxis, Vector3 endAxis, float radius, float mass, void* returnMassProperties);
typedef WINAPI void (*HkInertiaTensorComputer_ComputeCylinderVolumeMassProperties_t)(Vector3 startAxis, Vector3 endAxis, float radius, float mass, void* returnMassProperties);
typedef WINAPI void (*HkInertiaTensorComputer_ComputeSphereVolumeMassProperties_t)(float radius, float mass, void* returnMassProperties);
typedef WINAPI void* (*HkJobQueue_Create_t)(void* outThreadCount);
typedef WINAPI void* (*HkJobQueue_CreateWithNumThreads_t)(int32_t threadCount);
typedef WINAPI void (*HkJobQueue_Release_t)(void* instance);
typedef WINAPI int32_t (*HkJobQueue_GetWaitPolicy_t)(void* jobQueue);
typedef WINAPI void (*HkJobQueue_SetWaitPolicy_t)(void* jobQueue, int32_t value);
typedef WINAPI int32_t (*HkJobQueue_GetMasterThreadFinishingFlags_t)(void* jobQueue);
typedef WINAPI void (*HkJobQueue_SetMasterThreadFinishingFlags_t)(void* jobQueue, int32_t value);
typedef WINAPI void (*HkJobQueue_ProcessAllJobs_t)(void* jobQueue);
typedef WINAPI void* (*HkJobThreadPool_Create_t)(void* outThreadCount);
typedef WINAPI void* (*HkJobThreadPool_CreateWithNumThreads_t)(int32_t threadCount);
typedef WINAPI void (*HkJobThreadPool_RemoveReference_t)(void* instance);
typedef WINAPI void (*HkJobThreadPool_RunOnEachWorker_t)(void* instance, void* action, void* data);
typedef WINAPI void (*HkJobThreadPool_ExecuteJobQueue_t)(void* instance, void* jobQueue);
typedef WINAPI int32_t (*HkJobThreadPool_GetThisThreadIndex_t)(void* instance);
typedef WINAPI void (*HkJobThreadPool_WaitForCompletion_t)(void* instance);
typedef WINAPI void (*HkJobThreadPool_ClearTimerData_t)(void* instance);
typedef WINAPI void (*HkKeyFrameUtility_ApplyHardKeyFrame_t)(Vector4 nextPosition, Quaternion nextOrientation, float invDeltaTime, void* body);
typedef WINAPI float (*HkLimitedForceConstraintMotor_GetMinForce_t)(void* instance);
typedef WINAPI void (*HkLimitedForceConstraintMotor_SetMinForce_t)(void* instance, float value);
typedef WINAPI float (*HkLimitedForceConstraintMotor_GetMaxForce_t)(void* instance);
typedef WINAPI void (*HkLimitedForceConstraintMotor_SetMaxForce_t)(void* instance, float value);
typedef WINAPI void* (*HkLimitedHingeConstraintData_Create_t)(void);
typedef WINAPI void (*HkLimitedHingeConstraintData_SetInBodySpaceInternal_t)(void* instance, Vector3 pivotA, Vector3 pivotB, Vector3 axisA, Vector3 axisB, Vector3 axisAPerp, Vector3 axisBPerp);
typedef WINAPI void (*HkLimitedHingeConstraintData_SetInWorldSpace_t)(void* instance, Matrix bodyATransform, Matrix bodyBTransform, Vector3 pivot, Vector3 axis);
typedef WINAPI void (*HkLimitedHingeConstraintData_SetMotor_t)(void* instance, void* motor);
typedef WINAPI bool (*HkLimitedHingeConstraintData_IsMotorEnabled_t)(void* instance);
typedef WINAPI void (*HkLimitedHingeConstraintData_SetMotorEnabled_t)(void* instance, void* constraint, bool enabled);
typedef WINAPI float (*HkLimitedHingeConstraintData_GetTargetAngle_t)(void* instance);
typedef WINAPI void (*HkLimitedHingeConstraintData_SetTargetAngle_t)(void* instance, float value);
typedef WINAPI float (*HkLimitedHingeConstraintData_GetMaxFrictionTorque_t)(void* instance);
typedef WINAPI void (*HkLimitedHingeConstraintData_SetMaxFrictionTorque_t)(void* instance, float value);
typedef WINAPI float (*HkLimitedHingeConstraintData_GetMinAngularLimit_t)(void* instance);
typedef WINAPI void (*HkLimitedHingeConstraintData_SetMinAngularLimit_t)(void* instance, float value);
typedef WINAPI float (*HkLimitedHingeConstraintData_GetMaxAngularLimit_t)(void* instance);
typedef WINAPI void (*HkLimitedHingeConstraintData_SetMaxAngularLimit_t)(void* instance, float value);
typedef WINAPI void (*HkLimitedHingeConstraintData_DisableLimits_t)(void* instance);
typedef WINAPI bool (*HkLimitedHingeConstraintData_SetInertiaStabilizationFactor_t)(void* instance, float value);
typedef WINAPI bool (*HkLimitedHingeConstraintData_GetInertiaStabilizationFactor_t)(void* instance, void* outResult);
typedef WINAPI Vector3 (*HkLimitedHingeConstraintData_GetBodyAPos_t)(void* instance);
typedef WINAPI Vector3 (*HkLimitedHingeConstraintData_GetBodyBPos_t)(void* instance);
typedef WINAPI uint8_t (*HkLimitedHingeConstraintData_GetIsInitialized_t)(void* constraint);
typedef WINAPI void (*HkLimitedHingeConstraintData_SetIsInitialized_t)(void* constraint, uint8_t isInitialized);
typedef WINAPI float (*HkLimitedHingeConstraintData_GetPreviousTargetAngle_t)(void* constraint);
typedef WINAPI void (*HkLimitedHingeConstraintData_SetPreviousTargetAngle_t)(void* constraint, float previousTargetAngle);
typedef WINAPI float (*HkLimitedHingeConstraintData_GetCurrentAngle_t)(void* constraint);
typedef WINAPI void (*HkLimitedHingeConstraintData_SetCurrentAngle_t)(void* constraint, float value);
typedef WINAPI void* (*HkListShape_Create_t)(void* shapes, int32_t count, int32_t refPolicy);
typedef WINAPI uint16_t (*HkListShape_GetDisabledChildrenCount_t)(void* instance);
typedef WINAPI int32_t (*HkListShape_GetTotalChildrenCount_t)(void* instance);
typedef WINAPI void (*HkListShape_EnableShape_t)(void* instance, uint32_t shapeKey, bool isEnable);
typedef WINAPI void* (*HkListShape_GetChildByIndex_t)(void* instance, int32_t index);
typedef WINAPI bool (*HkListShape_IsChildEnabled_t)(void* instance, uint32_t shapeKey);
typedef WINAPI void* (*HkMalleableConstraintData_Create_t)(void* data);
typedef WINAPI float (*HkMalleableConstraintData_GetStrength_t)(void* instance);
typedef WINAPI void (*HkMalleableConstraintData_SetStrength_t)(void* instance, float value);
typedef WINAPI void* (*HkMassChangerUtil_Create_t)(void* body, int32_t otherBodyLayerMask, float invMassScale, float invMassScaleOtherBody);
typedef WINAPI bool (*HkMassChangerUtil_IsValid_t)(void* listener);
typedef WINAPI void (*HkMassChangerUtil_Remove_t)(void* listener);
typedef WINAPI void (*HkMemorySnapshot_Diff_t)(void* a, void* b, void* inA, void* inB);
typedef WINAPI void* (*HkMoppBvTreeShape_Create_t)(void* shapeCollection);
typedef WINAPI void* (*HkMoppBvTreeShape_GetShapeCollection_t)(void* instance);
typedef WINAPI void (*HkMoppBvTreeShape_DisableKeys_t)(void* instance, void* keys, int32_t count);
typedef WINAPI void (*HkMoppBvTreeShape_QueryAABB_t)(void* instance, Vector3 min, Vector3 max, void* shapeKeys);
typedef WINAPI void (*HkMoppBvTreeShape_QueryPoint_t)(void* instance, Vector3 point, void* shapeKeys);
typedef WINAPI void (*HkMotion_SetWorldMatrix_t)(void* instance, Matrix m);
typedef WINAPI int32_t (*HkMotion_GetDeactivationClass_t)(void* instance);
typedef WINAPI void (*HkMotion_SetDeactivationClass_t)(void* instance, int32_t value);
typedef WINAPI void* (*HkPhantomCallbackShape_Create_t)(void* enterCallback, void* leaveCallback, void* deleteCallback);
typedef WINAPI void* (*HkPrismaticConstraintData_Create_t)(void);
typedef WINAPI void (*HkPrismaticConstraintData_SetInWorldSpace_t)(void* instance, Matrix bodyATransform, Matrix bodyBTransform, Vector3 pivot, Vector3 axis);
typedef WINAPI void (*HkPrismaticConstraintData_SetInBodySpaceInternal_t)(void* instance, Vector3 bodyA, Vector3 bodyB, Vector3 axisA, Vector3 axisB, Vector3 axisAperp, Vector3 axisBperp);
typedef WINAPI float (*HkPrismaticConstraintData_GetMaximumLinearLimit_t)(void* instance);
typedef WINAPI void (*HkPrismaticConstraintData_SetMaximumLinearLimit_t)(void* instance, float value);
typedef WINAPI float (*HkPrismaticConstraintData_GetMinimumLinearLimit_t)(void* instance);
typedef WINAPI void (*HkPrismaticConstraintData_SetMinimumLinearLimit_t)(void* instance, float value);
typedef WINAPI float (*HkPrismaticConstraintData_GetMaxFrictionForce_t)(void* instance);
typedef WINAPI void (*HkPrismaticConstraintData_SetMaxFrictionForce_t)(void* instance, float value);
typedef WINAPI float (*HkPrismaticConstraintData_GetTargetPosition_t)(void* instance);
typedef WINAPI void (*HkPrismaticConstraintData_SetTargetPosition_t)(void* instance, float value);
typedef WINAPI void (*HkPrismaticConstraintData_SetMotor_t)(void* instance, void* motor);
typedef WINAPI bool (*HkPrismaticConstraintData_IsMotorEnabled_t)(void* instance);
typedef WINAPI void (*HkPrismaticConstraintData_SetMotorEnabled_t)(void* instance, void* constraint, bool enabled);
typedef WINAPI float (*HkPrismaticConstraintData_GetCurrentPosition_t)(void* constraint);
typedef WINAPI bool (*HkPhysicsSystem_IsActive_t)(void* instance);
typedef WINAPI void (*HkPhysicsSystem_SetActive_t)(void* instance, bool value);
typedef WINAPI void (*HkPhysicsSystem_RecreateConstraints_t)(void* instance);
typedef WINAPI void (*HkPhysicsSystem_GetConstraintDataFromSystem_t)(void* instance, void* constraintBuffer);
typedef WINAPI void* (*HkPhysicsSystem_GetName_t)(void* instance);
typedef WINAPI void* (*HkPhysicsSystem_LoadRagdollFromFile_t)(const char* fileName);
typedef WINAPI void* (*HkPhysicsSystem_LoadRagdollFromBuffer_t)(void* buffer, int32_t length);
typedef WINAPI bool (*HkPhysicsSystem_InitFromData_t)(void* loadedData, void* physicsSystem, void* bodyBuffer);
typedef WINAPI uint32_t (*HkpGroupFilter_CalcFilterInfo_t)(int32_t layer, int32_t systemGroup, int32_t subSystemId, int32_t subSystemDontCollideWith);
typedef WINAPI uint32_t (*HkpGroupFilter_CalcFilterInfoFromCurrent_t)(uint32_t currentInfo, int32_t collisionLayer);
typedef WINAPI void (*HkpInertiaTensorComputer_OptimizeInertiasOfConstraintTree_t)(void* constraints, int32_t size, void* rigidBody);
typedef WINAPI void (*HkPhysicsSystem_Release_t)(void* physicsSystem);
typedef WINAPI float (*HkRagdollConstraintData_GetPlaneMinAngularLimit_t)(void* instance);
typedef WINAPI void (*HkRagdollConstraintData_SetPlaneMinAngularLimit_t)(void* instance, float value);
typedef WINAPI float (*HkRagdollConstraintData_GetPlaneMaxAngularLimit_t)(void* instance);
typedef WINAPI void (*HkRagdollConstraintData_SetPlaneMaxAngularLimit_t)(void* instance, float value);
typedef WINAPI float (*HkRagdollConstraintData_GetTwistMinAngularLimit_t)(void* instance);
typedef WINAPI void (*HkRagdollConstraintData_SetTwistMinAngularLimit_t)(void* instance, float value);
typedef WINAPI float (*HkRagdollConstraintData_GetTwistMaxAngularLimit_t)(void* instance);
typedef WINAPI void (*HkRagdollConstraintData_SetTwistMaxAngularLimit_t)(void* instance, float value);
typedef WINAPI float (*HkRagdollConstraintData_GetMaxFrictionTorque_t)(void* instance);
typedef WINAPI void (*HkRagdollConstraintData_SetMaxFrictionTorque_t)(void* instance, float value);
typedef WINAPI void (*HkRagdollConstraintData_SetInBodySpaceInternal_t)(void* instance, Vector3 pivotA, Vector3 pivotB, Vector3 planeAxisA, Vector3 planeAxisB, Vector3 twistAxisA, Vector3 twistAxisB);
typedef WINAPI void (*HkRagdollConstraintData_SetAsymmetricConeAngle_t)(void* instance, float coneMin, float coneMax);
typedef WINAPI void (*HkRagdollConstraintData_SetConeLimitStabilization_t)(void* instance, bool enable);
typedef WINAPI void (*HkReferenceObject_AddReference_t)(void* instance);
typedef WINAPI void (*HkReferenceObject_RemoveReference_t)(void* instance);
typedef WINAPI bool (*HkReferenceObject_IsValid_t)(void* instance);
typedef WINAPI void (*HkReferenceObject_DebugRemoveRef_t)(void* instance);
typedef WINAPI int32_t (*HkReferenceObject_ReferenceCount_t)(void* instance);
typedef WINAPI void* (*HkRigidBody_Create_t)(void* bodyInfo);
typedef WINAPI void* (*HkRigidBody_CreateWithCustomVelocity_t)(void* bodyInfo);
typedef WINAPI void (*HkRigidBody_SetNumShapeKeysInContactPointProperties_t)(void* instance, int32_t value);
typedef WINAPI int32_t (*HkRigidBody_GetResponseModifiers_t)(void* instance);
typedef WINAPI void (*HkRigidBody_SetResponseModifiers_t)(void* instance, int32_t value);
typedef WINAPI void* (*HkRigidBody_GetShape_t)(void* instance);
typedef WINAPI int32_t (*HkRigidBody_SetShape_t)(void* instance, void* shape);
typedef WINAPI int32_t (*HkRigidBody_UpdateShape_t)(void* instance);
typedef WINAPI Matrix (*HkRigidBody_PredictRigidBodyMatrix_t)(void* instance, float deltaTime, void* world);
typedef WINAPI void (*HkRigidBody_SetMassProperties_t)(void* instance, HkMassProperties properties);
typedef WINAPI void (*HkRigidBody_SetWorldMatrix_t)(void* instance, Matrix m);
typedef WINAPI void (*HkRigidBody_SetTransform_t)(void* instance, Matrix m);
typedef WINAPI bool (*HkRigidBody_GetEnableDeactivation_t)(void* instance);
typedef WINAPI void (*HkRigidBody_SetEnableDeactivation_t)(void* instance, bool value);
typedef WINAPI bool (*HkRigidBody_GetMarkedForVelocityRecompute_t)(void* instance);
typedef WINAPI void (*HkRigidBody_SetMarkedForVelocityRecompute_t)(void* instance, bool value);
typedef WINAPI void* (*HkRigidBody_GetMotion_t)(void* instance);
typedef WINAPI float (*HkRigidBody_GetMass_t)(void* instance);
typedef WINAPI void (*HkRigidBody_SetMass_t)(void* instance, float value);
typedef WINAPI Vector3 (*HkRigidBody_GetCenterOfMassLocal_t)(void* instance);
typedef WINAPI void (*HkRigidBody_SetCenterOfMassLocal_t)(void* instance, Vector3 value);
typedef WINAPI Matrix (*HkRigidBody_GetInertiaTensor_t)(void* instance);
typedef WINAPI void (*HkRigidBody_SetInertiaTensor_t)(void* instance, Matrix value);
typedef WINAPI Matrix (*HkRigidBody_GetInverseInertiaTensor_t)(void* instance);
typedef WINAPI void (*HkRigidBody_SetInverseInertiaTensor_t)(void* instance, Matrix value);
typedef WINAPI Vector3 (*HkRigidBody_GetCenterOfMassWorld_t)(void* instance);
typedef WINAPI bool (*HkRigidBody_GetCustomVelocity_t)(void* instance, void* velocity);
typedef WINAPI void (*HkRigidBody_SetCustomVelocity_t)(void* instance, Vector3 value, bool valid);
typedef WINAPI Vector4 (*HkRigidBody_GetDeltaAngle_t)(void* instance);
typedef WINAPI float (*HkRigidBody_GetLinearDamping_t)(void* instance);
typedef WINAPI void (*HkRigidBody_SetLinearDamping_t)(void* instance, float value);
typedef WINAPI float (*HkRigidBody_GetAngularDamping_t)(void* instance);
typedef WINAPI void (*HkRigidBody_SetAngularDamping_t)(void* instance, float value);
typedef WINAPI float (*HkRigidBody_GetMaxLinearVelocity_t)(void* instance);
typedef WINAPI void (*HkRigidBody_SetMaxLinearVelocity_t)(void* instance, float value);
typedef WINAPI float (*HkRigidBody_GetMaxAngularVelocity_t)(void* instance);
typedef WINAPI void (*HkRigidBody_SetMaxAngularVelocity_t)(void* instance, float value);
typedef WINAPI float (*HkRigidBody_GetAllowedPenetrationDepth_t)(void* instance);
typedef WINAPI void (*HkRigidBody_SetAllowedPenetrationDepth_t)(void* instance, float value);
typedef WINAPI float (*HkRigidBody_GetFriction_t)(void* instance);
typedef WINAPI void (*HkRigidBody_SetFriction_t)(void* instance, float value);
typedef WINAPI float (*HkRigidBody_GetRestitution_t)(void* instance);
typedef WINAPI void (*HkRigidBody_SetRestitution_t)(void* instance, float value);
typedef WINAPI void (*HkRigidBody_ApplyLinearImpulse_t)(void* instance, Vector3 impulse);
typedef WINAPI void (*HkRigidBody_ApplyPointImpulse_t)(void* instance, Vector3 impulse, Vector3 point);
typedef WINAPI void (*HkRigidBody_ApplyAngularImpulse_t)(void* instance, Vector3 impulse);
typedef WINAPI void (*HkRigidBody_SetLayer_t)(void* instance, int32_t value);
typedef WINAPI uint32_t (*HkRigidBody_GetCollisionFilterInfo_t)(void* instance);
typedef WINAPI void (*HkRigidBody_SetCollisionFilterInfo_t)(void* instance, uint32_t info);
typedef WINAPI void (*HkRigidBody_ApplyForce_t)(void* instance, float time, Vector3 force);
typedef WINAPI void (*HkRigidBody_ApplyForceToPoint_t)(void* instance, float time, Vector3 force, Vector3 point);
typedef WINAPI void (*HkRigidBody_ApplyTorque_t)(void* instance, float time, Vector3 torque);
typedef WINAPI void* (*HkRigidBody_GetNativeObjectName_t)(void* instance);
typedef WINAPI void (*HkRigidBody_RemoveFromWorld_t)(void* instance);
typedef WINAPI bool (*HkRigidBody_HasGravity_t)(void* instance);
typedef WINAPI bool (*HkRigidBody_HasConstraints_t)(void* instance);
typedef WINAPI void* (*HkRigidBody_GetBreakableBody_t)(void* instance);
typedef WINAPI Vector3 (*HkRigidBody_GetGravity_t)(void* gravityAction);
typedef WINAPI void (*HkRigidBody_ReleaseGravity_t)(void* gravityAction);
typedef WINAPI void (*HkRigidBody_SetGravity_t)(void* gravityAction, Vector3 gravity);
typedef WINAPI void* (*HkRigidBody_Clone_t)(void* cloneBody);
typedef WINAPI void* (*HkRigidBody_FromShape_t)(void* shape);
typedef WINAPI uint64_t (*HkRigidBody_GetGcRoot_t)(void* instance);
typedef WINAPI void* (*HkRigidBody_GetGravityAction_t)(void* instance, void* action, Vector3 gravity);
typedef WINAPI void (*HkRigidBody_AddGravityAction_t)(void* instance, void* action);
typedef WINAPI int32_t (*HkRigidBody_GetDeactivationCounter0_t)(void* instance);
typedef WINAPI int32_t (*HkRigidBody_GetDeactivationCounter1_t)(void* instance);
typedef WINAPI bool (*HkRigidBody_HasActions_t)(void* instance, int32_t actionType);
typedef WINAPI void* (*HkRigidBodyCinfo_Create_t)(void);
typedef WINAPI void (*HkRigidBodyCinfo_Release_t)(void* instance);
typedef WINAPI int32_t (*HkRigidBodyCinfo_GetCollisionResponse_t)(void* instance);
typedef WINAPI void (*HkRigidBodyCinfo_SetCollisionResponse_t)(void* instance, int32_t value);
typedef WINAPI int32_t (*HkRigidBodyCinfo_GetResponseModifiers_t)(void* instance);
typedef WINAPI void (*HkRigidBodyCinfo_SetResponseModifiers_t)(void* instance, int32_t value);
typedef WINAPI Vector3 (*HkRigidBodyCinfo_GetPosition_t)(void* instance);
typedef WINAPI void (*HkRigidBodyCinfo_SetPosition_t)(void* instance, Vector3 value);
typedef WINAPI Quaternion (*HkRigidBodyCinfo_GetRotation_t)(void* instance);
typedef WINAPI void (*HkRigidBodyCinfo_SetRotation_t)(void* instance, Quaternion value);
typedef WINAPI Vector3 (*HkRigidBodyCinfo_GetLinearVelocity_t)(void* instance);
typedef WINAPI void (*HkRigidBodyCinfo_SetLinearVelocity_t)(void* instance, Vector3 value);
typedef WINAPI Vector3 (*HkRigidBodyCinfo_GetAngularVelocity_t)(void* instance);
typedef WINAPI void (*HkRigidBodyCinfo_SetAngularVelocity_t)(void* instance, Vector3 value);
typedef WINAPI Vector3 (*HkRigidBodyCinfo_GetCenterOfMass_t)(void* instance);
typedef WINAPI void (*HkRigidBodyCinfo_SetCenterOfMass_t)(void* instance, Vector3 value);
typedef WINAPI float (*HkRigidBodyCinfo_GetMass_t)(void* instance);
typedef WINAPI void (*HkRigidBodyCinfo_SetMass_t)(void* instance, float value);
typedef WINAPI float (*HkRigidBodyCinfo_GetLinearDamping_t)(void* instance);
typedef WINAPI void (*HkRigidBodyCinfo_SetLinearDamping_t)(void* instance, float value);
typedef WINAPI float (*HkRigidBodyCinfo_GetAngularDamping_t)(void* instance);
typedef WINAPI void (*HkRigidBodyCinfo_SetAngularDamping_t)(void* instance, float value);
typedef WINAPI float (*HkRigidBodyCinfo_GetFriction_t)(void* instance);
typedef WINAPI void (*HkRigidBodyCinfo_SetFriction_t)(void* instance, float value);
typedef WINAPI float (*HkRigidBodyCinfo_GetRestitution_t)(void* instance);
typedef WINAPI void (*HkRigidBodyCinfo_SetRestitution_t)(void* instance, float value);
typedef WINAPI float (*HkRigidBodyCinfo_GetMaxLinearVelocity_t)(void* instance);
typedef WINAPI void (*HkRigidBodyCinfo_SetMaxLinearVelocity_t)(void* instance, float value);
typedef WINAPI float (*HkRigidBodyCinfo_GetMaxAngularVelocity_t)(void* instance);
typedef WINAPI void (*HkRigidBodyCinfo_SetMaxAngularVelocity_t)(void* instance, float value);
typedef WINAPI uint16_t (*HkRigidBodyCinfo_GetContactPointCallbackDelay_t)(void* instance);
typedef WINAPI void (*HkRigidBodyCinfo_SetContactPointCallbackDelay_t)(void* instance, uint16_t value);
typedef WINAPI float (*HkRigidBodyCinfo_GetAllowedPenetrationDepth_t)(void* instance);
typedef WINAPI void (*HkRigidBodyCinfo_SetAllowedPenetrationDepth_t)(void* instance, float value);
typedef WINAPI int32_t (*HkRigidBodyCinfo_GetMotionType_t)(void* instance);
typedef WINAPI void (*HkRigidBodyCinfo_SetMotionType_t)(void* instance, int32_t value);
typedef WINAPI int32_t (*HkRigidBodyCinfo_GetSolverDeactivation_t)(void* instance);
typedef WINAPI void (*HkRigidBodyCinfo_SetSolverDeactivation_t)(void* instance, int32_t value);
typedef WINAPI int32_t (*HkRigidBodyCinfo_GetQualityType_t)(void* instance);
typedef WINAPI void (*HkRigidBodyCinfo_SetQualityType_t)(void* instance, int32_t value);
typedef WINAPI int8_t (*HkRigidBodyCinfo_GetAutoRemoveLevel_t)(void* instance);
typedef WINAPI void (*HkRigidBodyCinfo_SetAutoRemoveLevel_t)(void* instance, int8_t value);
typedef WINAPI void* (*HkRigidBodyCinfo_GetShape_t)(void* instance);
typedef WINAPI void (*HkRigidBodyCinfo_SetShape_t)(void* instance, void* value);
typedef WINAPI void (*HkRigidBodyCinfo_CalculateBoxInertiaTensor_t)(void* instance, Vector3 halfExtents, float mass);
typedef WINAPI void (*HkRigidBodyCinfo_CalculateSphereInertiaTensor_t)(void* instance, float radius, float mass);
typedef WINAPI void (*HkRigidBodyCinfo_SetMassProperties_t)(void* instance, HkMassProperties properties);
typedef WINAPI void (*HkRigidBodyCinfo_ComputeShapeMass_t)(void* instance, void* shape, float mass);
typedef WINAPI void* (*HkRopeConstraintData_Create_t)(void);
typedef WINAPI void (*HkRopeConstraintData_SetInBodySpaceInternal_t)(void* instance, Vector3 pivotA, Vector3 pivotB);
typedef WINAPI float (*HkRopeConstraintData_Update_t)(void* instance, void* constraint);
typedef WINAPI float (*HkRopeConstraintData_GetStrength_t)(void* instance);
typedef WINAPI void (*HkRopeConstraintData_SetStrength_t)(void* instance, float value);
typedef WINAPI float (*HkRopeConstraintData_GetLinearLimit_t)(void* instance);
typedef WINAPI void (*HkRopeConstraintData_SetLinearLimit_t)(void* instance, float value);
typedef WINAPI bool (*HkRopeConstraintData_IsValid_t)(void* instance);
typedef WINAPI int32_t (*HkShape_GetReferenceCount_t)(void* instance);
typedef WINAPI int32_t (*HkShape_GetShapeType_t)(void* instance);
typedef WINAPI bool (*HkShape_IsConvex_t)(void* instance);
typedef WINAPI float (*HkShape_GetConvexRadius_t)(void* instance);
typedef WINAPI void (*HkShape_SetConvexRadius_t)(void* instance, float value);
typedef WINAPI uint64_t (*HkShape_GetUserData_t)(void* instance);
typedef WINAPI void (*HkShape_SetUserData_t)(void* instance, uint64_t value);
typedef WINAPI void (*HkShape_SetRigidBody_t)(void* instance, void* rigidBody);
typedef WINAPI bool (*HkShape_IsContainer_t)(void* instance);
typedef WINAPI void (*HkShape_AddReference_t)(void* instance);
typedef WINAPI void (*HkShape_RemoveReference_t)(void* instance);
typedef WINAPI void (*HkShape_DisableRefCount_t)(void* instance);
typedef WINAPI void (*HkShape_GetLocalAABB_t)(void* instance, float tolerance, void* outMin, void* outMax);
typedef WINAPI uint32_t (*HkShape_CastRayCollectSingleHit_t)(void* instance, Vector3 from, Vector3 to);
typedef WINAPI void* (*HkShape_LoadShapeFromFile_t)(const char* filename);
typedef WINAPI void* (*HkShape_GetContainer_t)(void* instance);
typedef WINAPI int32_t (*HkShapeBatch_GetCount_t)(int32_t batchId);
typedef WINAPI void (*HkShapeBatch_GetInfo_t)(int32_t batchId, int32_t shapeIndex, void* outPos);
typedef WINAPI void (*HkShapeBatch_SetResult_t)(int32_t batchId, int32_t shapeIndex, void* shape);
typedef WINAPI void* (*HkShapeBuffer_Create_t)(void);
typedef WINAPI void* (*HkShapeBuffer_Destroy_t)(void* instance);
typedef WINAPI int32_t (*HkShapeCollection_GetShapeCount_t)(void* instance);
typedef WINAPI void* (*HkShapeCollection_GetShape_t)(void* instance, uint32_t shapeKey);
typedef WINAPI void* (*HkShapeCollection_GetShapeWithBuffer_t)(void* instance, uint32_t shapeKey, void* buffer);
typedef WINAPI uint32_t (*HkShapeContainer_GetFirstKey_t)(void* instance);
typedef WINAPI uint32_t (*HkShapeContainer_GetNextKey_t)(void* instance, uint32_t key);
typedef WINAPI void* (*HkShapeContainer_CurrentValue_t)(void* instance, uint32_t key, void* buffer);
typedef WINAPI void* (*HkShapeContainer_GetShape_t)(void* instance, uint32_t key);
typedef WINAPI bool (*HkShapeContainer_IsShapeKeyValid_t)(void* instance, uint32_t key);
typedef WINAPI bool (*HkShapeCutterUtil_Cut_t)(void* shape, Vector4 plane, void* aabbMin, void* aabbMax);
typedef WINAPI bool (*HkShapeLoader_LoadShapesListFromBuffer_t)(int32_t cBuffer, void* buffer, void* shapeBuffer, void* containsScene, void* containsDestruction);
typedef WINAPI bool (*HkShapeLoader_LoadShapesListFromFile_t)(const char* fileName, void* shapeBuffer);
typedef WINAPI bool (*HkShapeLoader_SaveShapesListToFile_t)(const char* fileName, void* listShapes, bool xmlFormat);
typedef WINAPI bool (*HkShapeLoader_CleanupShapesBuffer_t)(int32_t cBuffer, void* buffer, void* returnByteArray);
typedef WINAPI void* (*HkSimpleMeshShape_Create_t)(int32_t vCount, void* vertices, int32_t iCount, void* indices, int32_t mCount, void* materials);
typedef WINAPI void* (*HkSimpleShapePhantom_Create_t)(void* shape);
typedef WINAPI void* (*HkSimpleShapePhantom_CreateWithLayer_t)(void* shape, int32_t layer);
typedef WINAPI void* (*HkSimpleShapePhantom_GetShape_t)(void* instance);
typedef WINAPI void* (*HkSimpleValueProperty_CreateFloat_t)(float value);
typedef WINAPI void* (*HkSimpleValueProperty_CreateUInt_t)(uint32_t value);
typedef WINAPI void* (*HkSimpleValueProperty_CreateInt_t)(int32_t value);
typedef WINAPI float (*HkSimpleValueProperty_GetValueFloat_t)(void* instance);
typedef WINAPI void (*HkSimpleValueProperty_SetValueFloat_t)(void* instance, float valueFloat);
typedef WINAPI uint32_t (*HkSimpleValueProperty_GetValueUInt_t)(void* instance);
typedef WINAPI void (*HkSimpleValueProperty_SetValueUInt_t)(void* instance, uint32_t valueUInt);
typedef WINAPI int32_t (*HkSimpleValueProperty_GetValueInt_t)(void* instance);
typedef WINAPI void (*HkSimpleValueProperty_SetValueInt_t)(void* instance, int32_t calueInt);
typedef WINAPI int32_t (*HkSimulationIsland_GetEntityCount_t)(void* island);
typedef WINAPI void* (*HkSimulationIsland_GetEntity_t)(void* island, int32_t index);
typedef WINAPI void (*HkSimulationIsland_GetBounds_t)(void* island, void* bb);
typedef WINAPI void (*HkSimulationIsland_GetOffsets_t)(void* activeOffset, void* activeBitFieldOffset);
typedef WINAPI void* (*HkSmartListShape_Create_t)(int32_t dummy);
typedef WINAPI int32_t (*HkSmartListShape_GetShapeCount_t)(void* instance);
typedef WINAPI void (*HkSmartListShape_AddShape_t)(void* instance, void* shape);
typedef WINAPI void (*HkSmartListShape_RemoveShape_t)(void* instance, void* shape);
typedef WINAPI void (*HkSmartListShape_Validate_t)(void* instance);
typedef WINAPI void* (*HkSphereShape_Create_t)(float radius);
typedef WINAPI float (*HkSphereShape_GetRadius_t)(void* instance);
typedef WINAPI void (*HkSphereShape_SetRadius_t)(void* instance, float radius);
typedef WINAPI void* (*HkStaticCompoundShape_Create_t)(int32_t refPolicy);
typedef WINAPI int32_t (*HkStaticCompoundShape_GetInstanceCount_t)(void* instance);
typedef WINAPI int32_t (*HkStaticCompoundShape_AddInstance_t)(void* instance, void* shape, Matrix transform);
typedef WINAPI void (*HkStaticCompoundShape_Bake_t)(void* instance);
typedef WINAPI uint32_t (*HkStaticCompoundShape_ComposeShapeKey_t)(void* instance, int32_t instanceId, uint32_t shapeKey);
typedef WINAPI HkStaticCompoundShape_DecomposeShapeKeyResult (*HkStaticCompoundShape_DecomposeShapeKey_t)(void* instance, uint32_t shapeKey);
typedef WINAPI void (*HkStaticCompoundShape_EnableAllShapeKeys_t)(void* instance);
typedef WINAPI void (*HkStaticCompoundShape_EnableInstance_t)(void* instance, int32_t instanceId, bool enable);
typedef WINAPI void (*HkStaticCompoundShape_EnableShapeKey_t)(void* instance, uint32_t key, bool enable);
typedef WINAPI uint32_t (*HkStaticCompoundShape_GetFirstKey_t)(void* instance);
typedef WINAPI void* (*HkStaticCompoundShape_GetInstance_t)(void* instance, int32_t instanceIndex);
typedef WINAPI Matrix (*HkStaticCompoundShape_GetInstanceTransform_t)(void* instance, int32_t instanceIndex);
typedef WINAPI bool (*HkStaticCompoundShape_IsInstanceEnabled_t)(void* instance, int32_t instanceId);
typedef WINAPI bool (*HkStaticCompoundShape_IsShapeKeyEnabled_t)(void* instance, uint32_t key);
typedef WINAPI void (*HkTaskProfiler_Init_t)(void* onTaskStarted, void* onTaskFinished);
typedef WINAPI void (*HkTaskProfiler_ReleaseResources_t)(void);
typedef WINAPI void (*HkTaskProfiler_HookJobQueue_t)(void* jobQueue);
typedef WINAPI void (*HkTaskProfiler_ReplayTimers_t)(void* blockBegin, void* blockEnd);
typedef WINAPI void (*HkTaskProfiler_Begin1_t)(void);
typedef WINAPI void (*HkTaskProfiler_Begin2_t)(void);
typedef WINAPI void (*HkTaskProfiler_Begin3_t)(void);
typedef WINAPI void (*HkTaskProfiler_Begin4_t)(void);
typedef WINAPI void (*HkTaskProfiler_Begin5_t)(void);
typedef WINAPI void (*HkTaskProfiler_End_t)(void);
typedef WINAPI void* (*HkTransformShape_Create_t)(void* childShape, Matrix transform);
typedef WINAPI void* (*HkTransformShape_CreateWithTranslation_t)(void* childShape, Vector3 translation, Quaternion rotation);
typedef WINAPI Matrix (*HkTransformShape_GetTransform_t)(void* instance);
typedef WINAPI void* (*HkTransformShape_GetChildShape_t)(void* instance);
typedef WINAPI Vector3 (*HkTriangleShape_GetExtrusion_t)(void* instance);
typedef WINAPI Vector3 (*HkTriangleShape_GetPt2_t)(void* instance);
typedef WINAPI Vector3 (*HkTriangleShape_GetPt1_t)(void* instance);
typedef WINAPI Vector3 (*HkTriangleShape_GetPt0_t)(void* instance);
typedef WINAPI void* (*HkUniformGridShape_Create_t)(HkUniformGridShapeArgsPOD argsPod);
typedef WINAPI int32_t (*HkUniformGridShape_GetShapeCount_t)(void* instance);
typedef WINAPI void (*HkUniformGridShape_DiscardLargeData_t)(void* instance);
typedef WINAPI int32_t (*HkUniformGridShape_GetHitsAndClear_t)(void* instance);
typedef WINAPI int32_t (*HkUniformGridShape_GetHitCellsInRange_t)(void* instance, Vector3 min, Vector3 max, int32_t bufferSize, void* buffer);
typedef WINAPI int32_t (*HkUniformGridShape_GetMissingCellsInRange_t)(void* instance, Vector3 min, Vector3 max, int32_t bufferSize, void* buffer);
typedef WINAPI int32_t (*HkUniformGridShape_InvalidateRange_t)(void* instance, Vector3 min, Vector3 max, int32_t bufferSize, void* buffer);
typedef WINAPI void (*HkUniformGridShape_InvalidateRangeImmediate_t)(void* instance, Vector3I minChanged, Vector3I maxChanged);
typedef WINAPI void (*HkUniformGridShape_RemoveChild_t)(void* instance, int32_t x, int32_t y, int32_t z);
typedef WINAPI void (*HkUniformGridShape_SetChild_t)(void* instance, int32_t x, int32_t y, int32_t z, void* shape, int32_t refPolicy);
typedef WINAPI void* (*HkUniformGridShape_GetChild_t)(void* instance, int32_t x, int32_t y, int32_t z);
typedef WINAPI void (*HkUniformGridShape_SetDeleteHandler_t)(void* instance, void* handler);
typedef WINAPI void (*HkUniformGridShape_RemoveShapeRequestHandler_t)(void* instance);
typedef WINAPI void (*HkUniformGridShape_SetShapeRequestHandler_t)(void* instance, void* blockingCallback);
typedef WINAPI void (*HkUniformGridShape_EnableExtendedCache_t)(void* instance);
typedef WINAPI float (*HkUtils_CalculateSeparatingVelocity_t)(void* body1, void* body2, void* contactPoint);
typedef WINAPI void (*HkUtils_SetSoftContact_t)(void* bodyA, void* bodyB, float softness, float maxVel);
typedef WINAPI void (*HkVDB_SyncTimers_t)(void* threadPool);
typedef WINAPI void (*HkVDB_StepVDB_t)(void* world, float timeInSec);
typedef WINAPI void (*HkVDB_Start_t)(void);
typedef WINAPI void (*HkVDB_ReleaseResources_t)(void);
typedef WINAPI int32_t (*HkVDB_GetPort_t)(void);
typedef WINAPI void (*HkVDB_SetPort_t)(int32_t value);
typedef WINAPI void (*HkVDB_UpdateCamera_t)(void* from, void* to, void* up);
typedef WINAPI void (*HkVDB_Capture_t)(const char* path);
typedef WINAPI void (*HkVDB_EndCapture_t)(void);
typedef WINAPI void* (*HkVec3IProperty_Create_t)(Vector3I value);
typedef WINAPI Vector3I (*HkVec3IProperty_GetValue_t)(void* instance);
typedef WINAPI void (*HkVec3IProperty_SetValue_t)(void* instance, Vector3I value);
typedef WINAPI void* (*HkVelocityConstraintMotor_Create_t)(float velocityTarget, float maxForce);
typedef WINAPI float (*HkVelocityConstraintMotor_GetTau_t)(void* instance);
typedef WINAPI void (*HkVelocityConstraintMotor_SetTau_t)(void* instance, float value);
typedef WINAPI float (*HkVelocityConstraintMotor_GetVelocityTarget_t)(void* instance);
typedef WINAPI void (*HkVelocityConstraintMotor_SetVelocityTarget_t)(void* instance, float value);
typedef WINAPI bool (*HkVelocityConstraintMotor_GetConstantRecoveryVelocity_t)(void* instance);
typedef WINAPI void (*HkVelocityConstraintMotor_SetConstantRecoveryVelocity_t)(void* instance, bool value);
typedef WINAPI void* (*HkWheelConstraintData_Create_t)(void);
typedef WINAPI void (*HkWheelConstraintData_SetInWorldSpace_t)(void* instance, Matrix wheelBody, Matrix chasisBody, Vector3 pivot, Vector3 axle, Vector3 suspensionAxis, Vector3 steeringAxis);
typedef WINAPI void (*HkWheelConstraintData_SetInBodySpaceInternal_t)(void* instance, Vector3 pivotA, Vector3 pivotB, Vector3 axleA, Vector3 axleB, Vector3 suspensionAxisB, Vector3 steeringAxisB);
typedef WINAPI void (*HkWheelConstraintData_SetSuspensionMinLimit_t)(void* instance, float value);
typedef WINAPI void (*HkWheelConstraintData_SetSuspensionMaxLimit_t)(void* instance, float value);
typedef WINAPI void (*HkWheelConstraintData_SetSuspensionStrength_t)(void* instance, float value);
typedef WINAPI void (*HkWheelConstraintData_SetSuspensionDamping_t)(void* instance, float value);
typedef WINAPI void (*HkWheelConstraintData_SetSteeringAngle_t)(void* instance, float value);
typedef WINAPI void* (*HkWheelResponseModifierUtil_Create_t)(void* rigidBody, void* softness, void* acceleration);
typedef WINAPI void (*HkWheelResponseModifierUtil_Release_t)(void* instance);
typedef WINAPI void* (*HkWorld_Create_t)(bool enableGlobalGravity, float broadphaseCubeSideLength, float contactRestingVelocity, bool enableMultithreading, int32_t solverIterations, void* broadPhaseCallback);
typedef WINAPI void* (*HkWorld_CreateCInfo_t)(void* cInfo, void* broadPhaseCallback);
typedef WINAPI void* (*HkWorld_CreateBodyPairCollection_t)(void);
typedef WINAPI void (*HkWorld_RegisterWithJobQueue_t)(void* world, void* jobQueue);
typedef WINAPI void (*HkWorld_Lock_t)(void* world);
typedef WINAPI void (*HkWorld_Unlock_t)(void* world);
typedef WINAPI void (*HkWorld_LockCriticalOperations_t)(void* world);
typedef WINAPI void (*HkWorld_UnlockCriticalOperations_t)(void* world);
typedef WINAPI void (*HkWorld_ExecutePendingCriticalOperations_t)(void* world);
typedef WINAPI void (*HkWorld_StepDeltaTime_t)(void* world, float deltaTime);
typedef WINAPI void (*HkWorld_StepMultiThreaded_t)(void* world, void* jobQueue, void* threadPool, float deltaTime);
typedef WINAPI bool (*HkWorld_InitMtStep_t)(void* world, void* jobQueue, float deltaTime);
typedef WINAPI bool (*HkWorld_FinishMtStep_t)(void* world, void* jobQueue, void* threadPool);
typedef WINAPI void (*HkWorld_ExecuteViolatedConstraintProjections_t)(void* world, void* constraintListener, bool doProjections);
typedef WINAPI void (*HkWorld_ReportRuntimeDataConstraints_t)(void* world);
typedef WINAPI void (*HkWorld_AddConstraint_t)(void* world, void* constraint);
typedef WINAPI void (*HkWorld_RemoveConstraint_t)(void* world, void* constraint);
typedef WINAPI void (*HkWorld_AddEntity_t)(void* world, void* entity);
typedef WINAPI void (*HkWorld_RemoveEntity_t)(void* world, void* entity);
typedef WINAPI void (*HkWorld_AddPhantom_t)(void* world, void* phantom);
typedef WINAPI void (*HkWorld_RemovePhantom_t)(void* world, void* phantom);
typedef WINAPI void (*HkWorld_AddPhysicsSystem_t)(void* world, void* system);
typedef WINAPI void (*HkWorld_RemovePhysicsSystem_t)(void* world, void* system);
typedef WINAPI void (*HkWorld_GetPenetrationsShape_t)(void* world, void* bodyCollector, void* shape, Vector3 translation, Quaternion rotation, int32_t filter, void* buffer);
typedef WINAPI void (*HkWorld_GetPenetrationsBox_t)(void* world, void* bodyCollector, Vector3 halfExtents, Vector3 translation, Quaternion rotation, int32_t filter, void* buffer);
typedef WINAPI void (*HkWorld_GetPenetrationsShapeShape_t)(void* world, void* bodyCollector, void* shape1, Vector3 translation1, Quaternion rotation1, void* shape2, Vector3 translation2, Quaternion rotation2, void* buffer);
typedef WINAPI bool (*HkWorld_IsPenetratingShapeShape_t)(void* world, void* shape1, Vector3 translation1, Quaternion rotation1, void* shape2, Vector3 translation2, Quaternion rotation2);
typedef WINAPI bool (*HkWorld_IsPenetratingShapeShapeTransform_t)(void* world, void* shape1, Matrix transform1, void* shape2, Matrix transform2);
typedef WINAPI bool (*HkWorld_CastShape_t)(void* world, Vector3 to, void* shape, Matrix transform, int32_t filterLayer, float extraPenetration, void* outResult);
typedef WINAPI bool (*HkWorld_CastShapeReturnPoint_t)(void* world, Vector3 to, void* shape, Matrix transform, int32_t filterLayer, float extraPenetration, void* outPosition);
typedef WINAPI bool (*HkWorld_CastShapeReturnContact_t)(void* world, Vector3 to, void* shape, Matrix transform, int32_t filterLayer, float extraPenetration, void* outPoint);
typedef WINAPI bool (*HkWorld_CastShapeReturnContactData_t)(void* world, Vector3 to, void* shape, Matrix transform, uint32_t collisionFilterInfo, float extraPenetration, void* outPosition, void* outNormal, void* outDistance);
typedef WINAPI bool (*HkWorld_CastShapeReturnContactBodyData_t)(void* world, Vector3 to, void* shape, Matrix transform, uint32_t collisionFilterInfo, float extraPenetration, void* hitInfo);
typedef WINAPI bool (*HkWorld_CastShapeReturnContactBodyDatas_t)(void* world, Vector3 to, void* shape, Matrix transform, uint32_t collisionFilterInfo, float extraPenetration, void* buffer);
typedef WINAPI void (*HkWorld_CastRayAll_t)(void* world, Vector3 from, Vector3 to, int32_t raycastFilterLayer, void* buffer);
typedef WINAPI bool (*HkWorld_CastRayCollisionFilter_t)(void* world, Vector3 from, Vector3 to, uint32_t colllisionFilter, bool ignoreConvexShape, void* outConvexRadius, void* hitInfo);
typedef WINAPI bool (*HkWorld_CastRayFilterLayer_t)(void* world, Vector3 from, Vector3 to, int32_t raycastFilterLayer, bool useFilter, void* hitInfo);
typedef WINAPI void (*HkWorld_MarkForWrite_t)(void* world);
typedef WINAPI void (*HkWorld_UnmarkForWrite_t)(void* world);
typedef WINAPI void (*HkWorld_RefreshCollisionFilterOnEntity_t)(void* world, void* entity);
typedef WINAPI void (*HkWorld_RefreshCollisionFilterOnWorld_t)(void* world);
typedef WINAPI void (*HkWorld_ReintegrateEntity_t)(void* world, void* entity);
typedef WINAPI void (*HkWorld_AddAction_t)(void* world, void* action);
typedef WINAPI void (*HkWorld_RemoveAction_t)(void* world, void* action);
typedef WINAPI void* (*HkWorld_EnsureBatchSizes_t)(void* arr, void* size, int32_t count, int32_t newCount);
typedef WINAPI void (*HkWorld_SetBatchBody_t)(void* arr, int32_t index, void* body);
typedef WINAPI void (*HkWorld_AddEntityBatch_t)(void* world, void* arr, int32_t count);
typedef WINAPI void (*HkWorld_RemoveEntityBatch_t)(void* world, void* arr, int32_t count);
typedef WINAPI int32_t (*HkWorld_GetActiveSimulationIslandsCount_t)(void* world);
typedef WINAPI int32_t (*HkWorld_GetActiveSimulationIslandEntities_t)(void* world, int32_t islandIndex, void* entities);
typedef WINAPI void (*HkWorld_DeactivateSimulationIslandRigidBodies_t)(void* world, void* rigidBody);
typedef WINAPI bool (*HkWorld_IsActiveSimulationIsland_t)(void* world, void* rigidBody);
typedef WINAPI int32_t (*HkWorld_GetConstraintCount_t)(void* world);
typedef WINAPI int32_t (*HkWorld_GetActionCount_t)(void* world);
typedef WINAPI void* (*HkWorld_GetFixedBody_t)(void* world);
typedef WINAPI void (*HkWorld_ReadSimulationIslandInfos_t)(void* world, void* buffer);
typedef WINAPI Vector3 (*HkWorld_GetGravity_t)(void* world);
typedef WINAPI void (*HkWorld_SetGravity_t)(void* world, Vector3 value);
typedef WINAPI float (*HkWorld_GetDeactivationRotationSqrdA_t)(void* world);
typedef WINAPI void (*HkWorld_SetDeactivationRotationSqrdA_t)(void* world, float value);
typedef WINAPI float (*HkWorld_GetDeactivationRotationSqrdB_t)(void* world);
typedef WINAPI void (*HkWorld_SetDeactivationRotationSqrdB_t)(void* world, float value);
typedef WINAPI void (*HkWorld_AddWorldExtension_t)(void* world, void* extension);
typedef WINAPI void (*HkWorld_Release_t)(void* world, void* filter, void* penetrationHits, void* addBatch, void* removeBatch);
typedef WINAPI void* (*HkPhysicsContext_Create_t)(void);
typedef WINAPI void (*HkPhysicsContext_RegisterAllPhysicsProcesses_t)(void);
typedef WINAPI void (*HkPhysicsContext_AddWorld_t)(void* physicsContext, void* world);
typedef WINAPI void (*HkPhysicsContext_RemoveWorld_t)(void* physicsContext, void* world);
typedef WINAPI int32_t (*HkPhysicsContext_GetNumWorlds_t)(void* physicsContext);
typedef WINAPI void (*HkPhysicsContext_SyncTimers_t)(void* physicsContext, void* threadPool);
typedef WINAPI void (*HkPhysicsContext_Release_t)(void* physicsContext);
typedef WINAPI void* (*HkGroupFilter_Create_t)(void* world);
typedef WINAPI bool (*HkGroupFilter_IsCollisionEnabled_t)(void* filter, uint32_t colllinfo1, uint32_t collinfo2);
typedef WINAPI void* (*HkpAabbPhantom_Create_t)(Vector3 min, Vector3 max, uint32_t collisionFilterInfo, void* collidableAddedD, void* collidableRemovedD);
typedef WINAPI void (*HkpAabbPhantom_GetAabb_t)(void* instance, void* min, void* max);
typedef WINAPI void (*HkpAabbPhantom_SetAabb_t)(void* instance, Vector3 min, Vector3 max);
typedef WINAPI void (*HkpAabbPhantom_Release_t)(void* instance);
typedef WINAPI void* (*HkpCollidableAddedEvent_GetRigidBody_t)(void* instance);
typedef WINAPI void* (*HkpCollidableRemovedEvent_GetRigidBody_t)(void* instance);
typedef WINAPI void (*HkSimpleShapePhantom_SetTransform_t)(void* instance, Matrix matrix);
typedef WINAPI void (*HkIntermediateBuffer_ReleaseUnmanaged_t)(void* memory);

DECLARE_FUNCTION_POINTER(HkActivationListener_Create)
DECLARE_FUNCTION_POINTER(HkBallAndSocketConstraintData_Create)
DECLARE_FUNCTION_POINTER(HkBallAndSocketConstraintData_SetInBodySpaceInternal)
DECLARE_FUNCTION_POINTER(HkBaseSystem_Init)
DECLARE_FUNCTION_POINTER(HkBaseSystem_Quit)
DECLARE_FUNCTION_POINTER(HkBaseSystem_InitThread)
DECLARE_FUNCTION_POINTER(HkBaseSystem_QuitThread)
DECLARE_FUNCTION_POINTER(HkBaseSystem_GetVersionInfo)
DECLARE_FUNCTION_POINTER(HkBaseSystem_GetMemoryStatistics)
DECLARE_FUNCTION_POINTER(HkBaseSystem_EnableAssert)
DECLARE_FUNCTION_POINTER(HkBaseSystem_IsEnabled)
DECLARE_FUNCTION_POINTER(HkBaseSystem_IsDestructionEnabled)
DECLARE_FUNCTION_POINTER(HkBaseSystem_OnSimulationFrameStarted)
DECLARE_FUNCTION_POINTER(HkBaseSystem_OnSimulationFrameFinished)
DECLARE_FUNCTION_POINTER(HkBaseSystem_GetKeyCodes)
DECLARE_FUNCTION_POINTER(HkBaseSystem_IsOutOfMemory)
DECLARE_FUNCTION_POINTER(HkBaseSystem_GetCurrentMemoryConsumption)
DECLARE_FUNCTION_POINTER(HkBoxShape_Create)
DECLARE_FUNCTION_POINTER(HkBoxShape_CreateWithConvexRadius)
DECLARE_FUNCTION_POINTER(HkBoxShape_GetShapeFromCompoundShape)
DECLARE_FUNCTION_POINTER(HkBoxShape_GetHalfExtents)
DECLARE_FUNCTION_POINTER(HkBoxShape_SetHalfExtents)
DECLARE_FUNCTION_POINTER(HkBreakOffPartsUtil_Create)
DECLARE_FUNCTION_POINTER(HkBreakOffPartsUtil_Release)
DECLARE_FUNCTION_POINTER(HkBreakOffPartsUtil_RemoveKeysFromListShape)
DECLARE_FUNCTION_POINTER(HkBreakOffPartsUtil_MarkEntityBreakable)
DECLARE_FUNCTION_POINTER(HkBreakOffPartsUtil_MarkPieceBreakable)
DECLARE_FUNCTION_POINTER(HkBreakOffPartsUtil_SetMaxConstraintImpulse)
DECLARE_FUNCTION_POINTER(HkBreakOffPartsUtil_UnmarkEntityBreakable)
DECLARE_FUNCTION_POINTER(HkBreakOffPartsUtil_UnmarkPieceBreakable)
DECLARE_FUNCTION_POINTER(HkBreakOffPoints_Count)
DECLARE_FUNCTION_POINTER(HkBreakOffPoints_Get)
DECLARE_FUNCTION_POINTER(HkBreakableConstraintData_Create)
DECLARE_FUNCTION_POINTER(HkBreakableConstraintData_GetThreshold)
DECLARE_FUNCTION_POINTER(HkBreakableConstraintData_SetThreshold)
DECLARE_FUNCTION_POINTER(HkBreakableConstraintData_GetRemoveFromWorldOnBrake)
DECLARE_FUNCTION_POINTER(HkBreakableConstraintData_SetRemoveFromWorldOnBrake)
DECLARE_FUNCTION_POINTER(HkBreakableConstraintData_GetReapplyVelocityOnBreak)
DECLARE_FUNCTION_POINTER(HkBreakableConstraintData_SetReapplyVelocityOnBreak)
DECLARE_FUNCTION_POINTER(HkBreakableConstraintData_GetIsBroken)
DECLARE_FUNCTION_POINTER(HkBvCompressedMeshShape_CreateWithSimpleMesh)
DECLARE_FUNCTION_POINTER(HkBvCompressedMeshShape_CreateWithParams)
DECLARE_FUNCTION_POINTER(HkBvCompressedMeshShape_CreateUnsafe)
DECLARE_FUNCTION_POINTER(HkBvCompressedMeshShape_GetGeometry)
DECLARE_FUNCTION_POINTER(HkBvCompressedMeshShape_GetUserData)
DECLARE_FUNCTION_POINTER(HkBvShape_Create)
DECLARE_FUNCTION_POINTER(HkBvShape_GetChildShape)
DECLARE_FUNCTION_POINTER(HkBvShape_GetBoundingVolumeShape)
DECLARE_FUNCTION_POINTER(HkCapsuleShape_Create)
DECLARE_FUNCTION_POINTER(HkCapsuleShape_GetRadius)
DECLARE_FUNCTION_POINTER(HkCapsuleShape_GetVertexB)
DECLARE_FUNCTION_POINTER(HkCapsuleShape_GetVertexA)
DECLARE_FUNCTION_POINTER(HkCapsuleShape_GetCentre)
DECLARE_FUNCTION_POINTER(HkCharacterProxy_Create)
DECLARE_FUNCTION_POINTER(HkCharacterProxy_GetPosition)
DECLARE_FUNCTION_POINTER(HkCharacterProxy_SetPosition)
DECLARE_FUNCTION_POINTER(HkCharacterProxy_GetState)
DECLARE_FUNCTION_POINTER(HkCharacterProxy_SetState)
DECLARE_FUNCTION_POINTER(HkCharacterProxy_StepSimulation)
DECLARE_FUNCTION_POINTER(HkCharacterProxy_GetLinearVelocity)
DECLARE_FUNCTION_POINTER(HkCharacterProxy_SetLinearVelocity)
DECLARE_FUNCTION_POINTER(HkCharacterProxy_SetUp)
DECLARE_FUNCTION_POINTER(HkCharacterProxyCinfo_Create)
DECLARE_FUNCTION_POINTER(HkCharacterProxyCinfo_GetPosition)
DECLARE_FUNCTION_POINTER(HkCharacterProxyCinfo_SetPosition)
DECLARE_FUNCTION_POINTER(HkCharacterProxyCinfo_GetVelocity)
DECLARE_FUNCTION_POINTER(HkCharacterProxyCinfo_SetVelocity)
DECLARE_FUNCTION_POINTER(HkCharacterProxyCinfo_GetDynamicFriction)
DECLARE_FUNCTION_POINTER(HkCharacterProxyCinfo_SetDynamicFriction)
DECLARE_FUNCTION_POINTER(HkCharacterProxyCinfo_GetStaticFriction)
DECLARE_FUNCTION_POINTER(HkCharacterProxyCinfo_SetStaticFriction)
DECLARE_FUNCTION_POINTER(HkCharacterProxyCinfo_GetKeepContactTolerance)
DECLARE_FUNCTION_POINTER(HkCharacterProxyCinfo_SetKeepContactTolerance)
DECLARE_FUNCTION_POINTER(HkCharacterProxyCinfo_GetUp)
DECLARE_FUNCTION_POINTER(HkCharacterProxyCinfo_SetUp)
DECLARE_FUNCTION_POINTER(HkCharacterProxyCinfo_GetExtraUpStaticFriction)
DECLARE_FUNCTION_POINTER(HkCharacterProxyCinfo_SetExtraUpStaticFriction)
DECLARE_FUNCTION_POINTER(HkCharacterProxyCinfo_GetExtraDownStaticFriction)
DECLARE_FUNCTION_POINTER(HkCharacterProxyCinfo_SetExtraDownStaticFriction)
DECLARE_FUNCTION_POINTER(HkCharacterProxyCinfo_SetShapePhantom)
DECLARE_FUNCTION_POINTER(HkCharacterProxyCinfo_GetShapePhantom)
DECLARE_FUNCTION_POINTER(HkCharacterProxyCinfo_GetKeepDistance)
DECLARE_FUNCTION_POINTER(HkCharacterProxyCinfo_SetKeepDistance)
DECLARE_FUNCTION_POINTER(HkCharacterProxyCinfo_GetContactAngleSensitivity)
DECLARE_FUNCTION_POINTER(HkCharacterProxyCinfo_SetContactAngleSensitivity)
DECLARE_FUNCTION_POINTER(HkCharacterProxyCinfo_GetUserPlanes)
DECLARE_FUNCTION_POINTER(HkCharacterProxyCinfo_SetUserPlanes)
DECLARE_FUNCTION_POINTER(HkCharacterProxyCinfo_GetMaxCharacterSpeedForSolver)
DECLARE_FUNCTION_POINTER(HkCharacterProxyCinfo_SetMaxCharacterSpeedForSolver)
DECLARE_FUNCTION_POINTER(HkCharacterProxyCinfo_GetCharacterStrength)
DECLARE_FUNCTION_POINTER(HkCharacterProxyCinfo_SetCharacterStrength)
DECLARE_FUNCTION_POINTER(HkCharacterProxyCinfo_GetCharacterMass)
DECLARE_FUNCTION_POINTER(HkCharacterProxyCinfo_SetCharacterMass)
DECLARE_FUNCTION_POINTER(HkCharacterProxyCinfo_GetMaxSlope)
DECLARE_FUNCTION_POINTER(HkCharacterProxyCinfo_SetMaxSlope)
DECLARE_FUNCTION_POINTER(HkCharacterProxyCinfo_GetPenetrationRecoverySpeed)
DECLARE_FUNCTION_POINTER(HkCharacterProxyCinfo_SetPenetrationRecoverySpeed)
DECLARE_FUNCTION_POINTER(HkCharacterProxyCinfo_GetMaxCastIterations)
DECLARE_FUNCTION_POINTER(HkCharacterProxyCinfo_SetMaxCastIterations)
DECLARE_FUNCTION_POINTER(HkCharacterProxyCinfo_GetRefreshManifoldInCheckSupport)
DECLARE_FUNCTION_POINTER(HkCharacterProxyCinfo_SetRefreshManifoldInCheckSupport)
DECLARE_FUNCTION_POINTER(HkCharacterRigidBody_Create)
DECLARE_FUNCTION_POINTER(HkCharacterRigidBody_GetCharacterRigidbody)
DECLARE_FUNCTION_POINTER(HkCharacterRigidBody_SetWalkingState)
DECLARE_FUNCTION_POINTER(HkCharacterRigidBody_SetFlyingState)
DECLARE_FUNCTION_POINTER(HkCharacterRigidBody_SetLadderState)
DECLARE_FUNCTION_POINTER(HkCharacterRigidBody_SetDefaultShape)
DECLARE_FUNCTION_POINTER(HkCharacterRigidBody_SetShapeForCrouch)
DECLARE_FUNCTION_POINTER(HkCharacterRigidBody_GetPosition)
DECLARE_FUNCTION_POINTER(HkCharacterRigidBody_SetPosition)
DECLARE_FUNCTION_POINTER(HkCharacterRigidBody_GetState)
DECLARE_FUNCTION_POINTER(HkCharacterRigidBody_SetState)
DECLARE_FUNCTION_POINTER(HkCharacterRigidBody_StepSimulation)
DECLARE_FUNCTION_POINTER(HkCharacterRigidBody_UpdateVelocity)
DECLARE_FUNCTION_POINTER(HkCharacterRigidBody_UpdateSupport)
DECLARE_FUNCTION_POINTER(HkCharacterRigidBody_SetRigidBodyTransform)
DECLARE_FUNCTION_POINTER(HkCharacterRigidBody_GetRigidBodyTransform)
DECLARE_FUNCTION_POINTER(HkCharacterRigidBody_GetLinearVelocity)
DECLARE_FUNCTION_POINTER(HkCharacterRigidBody_SetLinearVelocity)
DECLARE_FUNCTION_POINTER(HkCharacterRigidBody_ApplyLinearImpulse)
DECLARE_FUNCTION_POINTER(HkCharacterRigidBody_ApplyAngularImpulse)
DECLARE_FUNCTION_POINTER(HkCharacterRigidBody_SetSupportDistance)
DECLARE_FUNCTION_POINTER(HkCharacterRigidBody_SetHardSupportDistance)
DECLARE_FUNCTION_POINTER(HkCharacterRigidBody_GetAngularVelocity)
DECLARE_FUNCTION_POINTER(HkCharacterRigidBody_SetAngularVelocity)
DECLARE_FUNCTION_POINTER(HkCharacterRigidBody_IsSupportedByFloatingObject)
DECLARE_FUNCTION_POINTER(HkCharacterRigidBody_IsSupported)
DECLARE_FUNCTION_POINTER(HkCharacterRigidBody_GetSupportNormal)
DECLARE_FUNCTION_POINTER(HkCharacterRigidBody_GetGroundVelocity)
DECLARE_FUNCTION_POINTER(HkCharacterRigidBody_GetUseSupportInfoQuery)
DECLARE_FUNCTION_POINTER(HkCharacterRigidBody_SetUseSupportInfoQuery)
DECLARE_FUNCTION_POINTER(HkCharacterRigidBody_SetPreviousSupportedState)
DECLARE_FUNCTION_POINTER(HkCharacterRigidBody_ResetSurfaceVelocity)
DECLARE_FUNCTION_POINTER(HkCharacterRigidBody_SetMaxSlope)
DECLARE_FUNCTION_POINTER(HkCharacterRigidBody_GetMaxSlope)
DECLARE_FUNCTION_POINTER(HkCharacterRigidBody_GetSupportBodies)
DECLARE_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_Create)
DECLARE_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_GetCollisionFilterInfo)
DECLARE_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_SetCollisionFilterInfo)
DECLARE_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_GetShape)
DECLARE_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_SetShape)
DECLARE_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_GetPosition)
DECLARE_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_SetPosition)
DECLARE_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_GetRotation)
DECLARE_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_SetRotation)
DECLARE_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_GetMass)
DECLARE_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_SetMass)
DECLARE_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_GetFriction)
DECLARE_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_SetFriction)
DECLARE_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_GetMaxLinearVelocity)
DECLARE_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_SetMaxLinearVelocity)
DECLARE_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_GetAllowedPenetrationDepth)
DECLARE_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_SetAllowedPenetrationDepth)
DECLARE_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_GetUp)
DECLARE_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_SetUp)
DECLARE_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_GetMaxSlope)
DECLARE_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_SetMaxSlope)
DECLARE_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_GetMaxForce)
DECLARE_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_SetMaxForce)
DECLARE_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_GetUnweldingHeightOffsetFactor)
DECLARE_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_SetUnweldingHeightOffsetFactor)
DECLARE_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_GetMaxSpeedForSimplexSolver)
DECLARE_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_SetMaxSpeedForSimplexSolver)
DECLARE_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_GetSupportDistance)
DECLARE_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_SetSupportDistance)
DECLARE_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_GetHardSupportDistance)
DECLARE_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_SetHardSupportDistance)
DECLARE_FUNCTION_POINTER(HkCogWheelConstraintData_Create)
DECLARE_FUNCTION_POINTER(HkCogWheelConstraintData_SetInWorldSpace)
DECLARE_FUNCTION_POINTER(HkCogWheelConstraintData_SetInBodySpaceInternal)
DECLARE_FUNCTION_POINTER(HkCollisionEvent_GetSource)
DECLARE_FUNCTION_POINTER(HkCollisionEvent_GetRigidBody)
DECLARE_FUNCTION_POINTER(HkCollisionEvent_GetBodyA)
DECLARE_FUNCTION_POINTER(HkCollisionEvent_GetBodyB)
DECLARE_FUNCTION_POINTER(HkCollisionEvent_SetImpulse)
DECLARE_FUNCTION_POINTER(HkCollisionEvent_SetImpulseScaling)
DECLARE_FUNCTION_POINTER(HkCollisionEvent_GetContactPointCount)
DECLARE_FUNCTION_POINTER(HkCollisionEvent_Disable)
DECLARE_FUNCTION_POINTER(HkCollisionEvent_GetContactPointPropertiesAt)
DECLARE_FUNCTION_POINTER(HkCollisionEvent_GetOffsets)
DECLARE_FUNCTION_POINTER(HkConstraint_Create)
DECLARE_FUNCTION_POINTER(HkConstraint_AddConstraintListener)
DECLARE_FUNCTION_POINTER(HkConstraint_RemoveConstraintListener)
DECLARE_FUNCTION_POINTER(HkConstraint_ReplaceEntity)
DECLARE_FUNCTION_POINTER(HkConstraint_SetVirtualMassInverse)
DECLARE_FUNCTION_POINTER(HkConstraint_GetPriority)
DECLARE_FUNCTION_POINTER(HkConstraint_SetPriority)
DECLARE_FUNCTION_POINTER(HkConstraint_GetWantRuntime)
DECLARE_FUNCTION_POINTER(HkConstraint_SetWantRuntime)
DECLARE_FUNCTION_POINTER(HkConstraint_IsInWorld)
DECLARE_FUNCTION_POINTER(HkConstraint_GetRigidBodyA)
DECLARE_FUNCTION_POINTER(HkConstraint_GetRigidBodyB)
DECLARE_FUNCTION_POINTER(HkConstraint_GetEnabled)
DECLARE_FUNCTION_POINTER(HkConstraint_SetEnabled)
DECLARE_FUNCTION_POINTER(HkConstraint_GetPivotsInWorld)
DECLARE_FUNCTION_POINTER(HkConstraint_GetUserData)
DECLARE_FUNCTION_POINTER(HkConstraint_SetUserData)
DECLARE_FUNCTION_POINTER(HkConstraint_AddCenterOfMassModifierAtom)
DECLARE_FUNCTION_POINTER(HkConstraint_FindConnectedConstraints)
DECLARE_FUNCTION_POINTER(HkConstraintData_GetMaximumLinearImpulse)
DECLARE_FUNCTION_POINTER(HkConstraintData_SetMaximumLinearImpulse)
DECLARE_FUNCTION_POINTER(HkConstraintData_GetMaximumAngularImpulse)
DECLARE_FUNCTION_POINTER(HkConstraintData_SetMaximumAngularImpulse)
DECLARE_FUNCTION_POINTER(HkConstraintData_GetBreachImpulse)
DECLARE_FUNCTION_POINTER(HkConstraintData_SetBreachImpulse)
DECLARE_FUNCTION_POINTER(HkConstraintData_GetInertiaStabilizationFactor)
DECLARE_FUNCTION_POINTER(HkConstraintData_SetInertiaStabilizationFactor)
DECLARE_FUNCTION_POINTER(HkConstraintData_SetSolvingMethod)
DECLARE_FUNCTION_POINTER(HkConstraintListener_Create)
DECLARE_FUNCTION_POINTER(HkConstraintListener_Release)
DECLARE_FUNCTION_POINTER(HkConstraintListener_SetCallbacks)
DECLARE_FUNCTION_POINTER(HkConstraintProjectorListener_Create)
DECLARE_FUNCTION_POINTER(HkConstraintProjectorListener_Release)
DECLARE_FUNCTION_POINTER(HkConstraintStabilizationUtil_StabilizeRagdollInertias)
DECLARE_FUNCTION_POINTER(HkContactListener_Create)
DECLARE_FUNCTION_POINTER(HkContactListener_SetCallbackLimit)
DECLARE_FUNCTION_POINTER(HkContactListener_ResetLimit)
DECLARE_FUNCTION_POINTER(HkContactPoint_GetPosition)
DECLARE_FUNCTION_POINTER(HkContactPoint_SetPosition)
DECLARE_FUNCTION_POINTER(HkContactPoint_GetNormalAndDistance)
DECLARE_FUNCTION_POINTER(HkContactPoint_SetNormalAndDistance)
DECLARE_FUNCTION_POINTER(HkContactPoint_GetNormal)
DECLARE_FUNCTION_POINTER(HkContactPoint_SetNormal)
DECLARE_FUNCTION_POINTER(HkContactPoint_GetDistance)
DECLARE_FUNCTION_POINTER(HkContactPoint_SetDistance)
DECLARE_FUNCTION_POINTER(HkContactPoint_Flip)
DECLARE_FUNCTION_POINTER(HkContactPointEvent_GetBase)
DECLARE_FUNCTION_POINTER(HkContactPointEvent_IsToi)
DECLARE_FUNCTION_POINTER(HkContactPointEvent_GetSeparatingVelocity)
DECLARE_FUNCTION_POINTER(HkContactPointEvent_SetSeparatingVelocity)
DECLARE_FUNCTION_POINTER(HkContactPointEvent_GetRotateNormal)
DECLARE_FUNCTION_POINTER(HkContactPointEvent_SetRotateNormal)
DECLARE_FUNCTION_POINTER(HkContactPointEvent_GetEventType)
DECLARE_FUNCTION_POINTER(HkContactPointEvent_GetContactPoint)
DECLARE_FUNCTION_POINTER(HkContactPointEvent_GetContactProperties)
DECLARE_FUNCTION_POINTER(HkContactPointEvent_GetFiringCallbacksForFullManifold)
DECLARE_FUNCTION_POINTER(HkContactPointEvent_GetFirstCallbackForFullManifold)
DECLARE_FUNCTION_POINTER(HkContactPointEvent_GetLastCallbackForFullManifold)
DECLARE_FUNCTION_POINTER(HkContactPointEvent_GetContactPointId)
DECLARE_FUNCTION_POINTER(HkContactPointEvent_AccessVelocities)
DECLARE_FUNCTION_POINTER(HkContactPointEvent_UpdateVelocities)
DECLARE_FUNCTION_POINTER(HkContactPointEvent_GetShapeKey)
DECLARE_FUNCTION_POINTER(HkContactPointEvent_GetShapeKeyWithShapeID)
DECLARE_FUNCTION_POINTER(HkContactPointEvent_GetFieldOffsets)
DECLARE_FUNCTION_POINTER(HkContactPointProperties_GetImpulseApplied)
DECLARE_FUNCTION_POINTER(HkContactPointProperties_GetInternalSolverData)
DECLARE_FUNCTION_POINTER(HkContactPointProperties_WasUsed)
DECLARE_FUNCTION_POINTER(HkContactPointProperties_GetFriction)
DECLARE_FUNCTION_POINTER(HkContactPointProperties_SetFriction)
DECLARE_FUNCTION_POINTER(HkContactPointProperties_GetRestitution)
DECLARE_FUNCTION_POINTER(HkContactPointProperties_SetRestitution)
DECLARE_FUNCTION_POINTER(HkContactPointProperties_IsPotential)
DECLARE_FUNCTION_POINTER(HkContactPointProperties_GetMaxImpulsePerStep)
DECLARE_FUNCTION_POINTER(HkContactPointProperties_SetMaxImpulsePerStep)
DECLARE_FUNCTION_POINTER(HkContactPointProperties_GetMaxImpulse)
DECLARE_FUNCTION_POINTER(HkContactPointProperties_SetMaxImpulse)
DECLARE_FUNCTION_POINTER(HkContactPointProperties_GetIsDisabled)
DECLARE_FUNCTION_POINTER(HkContactPointProperties_SetIsDisabled)
DECLARE_FUNCTION_POINTER(HkContactPointProperties_GetIsNew)
DECLARE_FUNCTION_POINTER(HkContactPointProperties_SetIsNew)
DECLARE_FUNCTION_POINTER(HkContactPointProperties_GetUserData)
DECLARE_FUNCTION_POINTER(HkContactPointProperties_SetUserData)
DECLARE_FUNCTION_POINTER(HkContactPointProperties_GetFieldOffsets)
DECLARE_FUNCTION_POINTER(HkContactSoundListener_Create)
DECLARE_FUNCTION_POINTER(HkConvexShape_GetConvexShapeFromCompoundShape)
DECLARE_FUNCTION_POINTER(HkConvexShape_GetConvexRadius)
DECLARE_FUNCTION_POINTER(HkConvexShape_SetConvexRadius)
DECLARE_FUNCTION_POINTER(HkConvexShape_GetDefaultConvexRadius)
DECLARE_FUNCTION_POINTER(HkConvexTransformShape_Create)
DECLARE_FUNCTION_POINTER(HkConvexTransformShape_CreateTranslated)
DECLARE_FUNCTION_POINTER(HkConvexTransformShape_GetChildShape)
DECLARE_FUNCTION_POINTER(HkConvexTransformShape_GetTransform)
DECLARE_FUNCTION_POINTER(HkConvexTranslateShape_CreateWithChild)
DECLARE_FUNCTION_POINTER(HkConvexTranslateShape_GetChildShape)
DECLARE_FUNCTION_POINTER(HkConvexTranslateShape_GetTranslation)
DECLARE_FUNCTION_POINTER(HkConvexVerticesShape_Create)
DECLARE_FUNCTION_POINTER(HkConvexVerticesShape_CreateWithRadius)
DECLARE_FUNCTION_POINTER(HkConvexVerticesShape_GetCenter)
DECLARE_FUNCTION_POINTER(HkConvexVerticesShape_GetVertexCount)
DECLARE_FUNCTION_POINTER(HkConvexVerticesShape_GetFaceCount)
DECLARE_FUNCTION_POINTER(HkConvexVerticesShape_GetFaces)
DECLARE_FUNCTION_POINTER(HkConvexVerticesShape_GetVertices)
DECLARE_FUNCTION_POINTER(HkConvexVerticesShape_GetGeometry)
DECLARE_FUNCTION_POINTER(HkCustomWheelConstraintData_Create)
DECLARE_FUNCTION_POINTER(HkCustomWheelConstraintData_GetLimitsEnabled)
DECLARE_FUNCTION_POINTER(HkCustomWheelConstraintData_SetLimitsEnabled)
DECLARE_FUNCTION_POINTER(HkCustomWheelConstraintData_GetSuspensionMinLimit)
DECLARE_FUNCTION_POINTER(HkCustomWheelConstraintData_SetSuspensionMinLimit)
DECLARE_FUNCTION_POINTER(HkCustomWheelConstraintData_GetSuspensionMaxLimit)
DECLARE_FUNCTION_POINTER(HkCustomWheelConstraintData_SetSuspensionMaxLimit)
DECLARE_FUNCTION_POINTER(HkCustomWheelConstraintData_GetFrictionEnabled)
DECLARE_FUNCTION_POINTER(HkCustomWheelConstraintData_SetFrictionEnabled)
DECLARE_FUNCTION_POINTER(HkCustomWheelConstraintData_GetMaxFrictionTorque)
DECLARE_FUNCTION_POINTER(HkCustomWheelConstraintData_SetMaxFrictionTorque)
DECLARE_FUNCTION_POINTER(HkCustomWheelConstraintData_SetInBodySpaceInternal)
DECLARE_FUNCTION_POINTER(HkCustomWheelConstraintData_SetSuspensionStrength)
DECLARE_FUNCTION_POINTER(HkCustomWheelConstraintData_SetSuspensionDamping)
DECLARE_FUNCTION_POINTER(HkCustomWheelConstraintData_SetSteeringAngle)
DECLARE_FUNCTION_POINTER(HkCustomWheelConstraintData_SetAngleLimits)
DECLARE_FUNCTION_POINTER(HkCustomWheelConstraintData_GetAngleLimitsMin)
DECLARE_FUNCTION_POINTER(HkCustomWheelConstraintData_GetAngleLimitsMax)
DECLARE_FUNCTION_POINTER(HkCustomWheelConstraintData_DisableLimits)
DECLARE_FUNCTION_POINTER(HkCustomWheelConstraintData_GetCurrentAngle)
DECLARE_FUNCTION_POINTER(HkCustomWheelConstraintData_SetCurrentAngle)
DECLARE_FUNCTION_POINTER(HkCylinderShape_Create)
DECLARE_FUNCTION_POINTER(HkCylinderShape_CreateWithConvexRadius)
DECLARE_FUNCTION_POINTER(HkCylinderShape_GetVertexB)
DECLARE_FUNCTION_POINTER(HkCylinderShape_GetVertexA)
DECLARE_FUNCTION_POINTER(HkCylinderShape_SetVertexB)
DECLARE_FUNCTION_POINTER(HkCylinderShape_SetVertexA)
DECLARE_FUNCTION_POINTER(HkCylinderShape_GetRadius)
DECLARE_FUNCTION_POINTER(HkCylinderShape_SetRadius)
DECLARE_FUNCTION_POINTER(HkCylinderShape_SetNumberOfVirtualSideSegments)
DECLARE_FUNCTION_POINTER(HkEasePenetrationAction_Create)
DECLARE_FUNCTION_POINTER(HkEasePenetrationAction_GetInitialAdditionalAllowedPenetrationDepth)
DECLARE_FUNCTION_POINTER(HkEasePenetrationAction_SetInitialAdditionalAllowedPenetrationDepth)
DECLARE_FUNCTION_POINTER(HkEasePenetrationAction_GetInitialAllowedPenetrationDepthMultiplier)
DECLARE_FUNCTION_POINTER(HkEasePenetrationAction_SetInitialAllowedPenetrationDepthMultiplier)
DECLARE_FUNCTION_POINTER(HkEntity_AddActivationListener)
DECLARE_FUNCTION_POINTER(HkEntity_RemoveActivationListener)
DECLARE_FUNCTION_POINTER(HKEntity_AddEntityListener)
DECLARE_FUNCTION_POINTER(HKEntity_RemoveEntityListener)
DECLARE_FUNCTION_POINTER(HkEntity_SetContactListener)
DECLARE_FUNCTION_POINTER(HkEntity_GetQuality)
DECLARE_FUNCTION_POINTER(HkEntity_SetQuality)
DECLARE_FUNCTION_POINTER(HkEntity_IsFixed)
DECLARE_FUNCTION_POINTER(HkEntity_IsFixedOrKeyframed)
DECLARE_FUNCTION_POINTER(HkRigidBody_GetMotionType)
DECLARE_FUNCTION_POINTER(HkEntity_GetContactPointCallbackDelay)
DECLARE_FUNCTION_POINTER(HkEntity_SetContactPointCallbackDelay)
DECLARE_FUNCTION_POINTER(HkEntity_SetProperty)
DECLARE_FUNCTION_POINTER(HkEntity_HasProperty)
DECLARE_FUNCTION_POINTER(HkEntity_RemoveProperty)
DECLARE_FUNCTION_POINTER(HkRigidBody_GetRotation)
DECLARE_FUNCTION_POINTER(HkRigidBody_SetRotation)
DECLARE_FUNCTION_POINTER(HkRigidBody_GetPosition)
DECLARE_FUNCTION_POINTER(HkRigidBody_SetPosition)
DECLARE_FUNCTION_POINTER(HkRigidBody_Activate)
DECLARE_FUNCTION_POINTER(HkRigidBody_ActivateAsCriticalOperation)
DECLARE_FUNCTION_POINTER(HkRigidBody_Deactivate)
DECLARE_FUNCTION_POINTER(HkRigidBody_UpdateMotionType)
DECLARE_FUNCTION_POINTER(HkRigidBody_GetIsActive)
DECLARE_FUNCTION_POINTER(HkRigidBody_RequestDeactivation)
DECLARE_FUNCTION_POINTER(HkRigidBody_GetLinearVelocity)
DECLARE_FUNCTION_POINTER(HkRigidBody_SetLinearVelocity)
DECLARE_FUNCTION_POINTER(HkRigidBody_GetAngularVelocity)
DECLARE_FUNCTION_POINTER(HkRigidBody_SetAngularVelocity)
DECLARE_FUNCTION_POINTER(HkEntity_GetFieldOffsets)
DECLARE_FUNCTION_POINTER(HkEntityListener_Create)
DECLARE_FUNCTION_POINTER(HkEntityListener_Release)
DECLARE_FUNCTION_POINTER(HkFixedConstraintData_Create)
DECLARE_FUNCTION_POINTER(HkFixedConstraintData_SetInBodySpaceInternal)
DECLARE_FUNCTION_POINTER(HkFixedConstraintData_SetInWorldSpace)
DECLARE_FUNCTION_POINTER(HkFixedConstraintData_IsValid)
DECLARE_FUNCTION_POINTER(HkFixedConstraintData_SetInertiaStabilizationFactor)
DECLARE_FUNCTION_POINTER(HkFixedConstraintData_GetInertiaStabilizationFactor)
DECLARE_FUNCTION_POINTER(HkFixedConstraintData_GetSolverImpulseInLastStep)
DECLARE_FUNCTION_POINTER(HkGeometry_Create)
DECLARE_FUNCTION_POINTER(HkGeometry_CreateWithParams)
DECLARE_FUNCTION_POINTER(HkGeometry_Destroy)
DECLARE_FUNCTION_POINTER(HkGeometry_GetTriangleCount)
DECLARE_FUNCTION_POINTER(HkGeometry_GetVertexCount)
DECLARE_FUNCTION_POINTER(HkGeometry_Append)
DECLARE_FUNCTION_POINTER(HkGeometry_GetTriangle)
DECLARE_FUNCTION_POINTER(HkGeometry_GetVertex)
DECLARE_FUNCTION_POINTER(HkGeometry_SetGeometry)
DECLARE_FUNCTION_POINTER(HkGridShape_Create)
DECLARE_FUNCTION_POINTER(HkGridShape_GetCellSize)
DECLARE_FUNCTION_POINTER(HkGridShape_GetShapeCount)
DECLARE_FUNCTION_POINTER(HkGridShape_SetDebugRigidBody)
DECLARE_FUNCTION_POINTER(HkGridShape_GetDebugRigidBody)
DECLARE_FUNCTION_POINTER(HkGridShape_SetDebugDraw)
DECLARE_FUNCTION_POINTER(HkGridShape_GetDebugDraw)
DECLARE_FUNCTION_POINTER(HkGridShape_AddShapes)
DECLARE_FUNCTION_POINTER(HkGridShape_Contains)
DECLARE_FUNCTION_POINTER(HkGridShape_GetShape)
DECLARE_FUNCTION_POINTER(HkGridShape_GetShapeInfo)
DECLARE_FUNCTION_POINTER(HkGridShape_GetShapeInfoCount)
DECLARE_FUNCTION_POINTER(HkGridShape_GetShapeMin)
DECLARE_FUNCTION_POINTER(HkGridShape_GetShapesInInterval)
DECLARE_FUNCTION_POINTER(HkGridShape_GetChildBounds)
DECLARE_FUNCTION_POINTER(HkGridShape_RemoveShapes)
DECLARE_FUNCTION_POINTER(HkGridShape_GetCellRanges)
DECLARE_FUNCTION_POINTER(HkGroupFilter_CalcFilterInfo)
DECLARE_FUNCTION_POINTER(HkGroupFilter_GetLayerFromFilterInfo)
DECLARE_FUNCTION_POINTER(HkGroupFilter_getSubSystemDontCollideWithFromFilterInfo)
DECLARE_FUNCTION_POINTER(HkGroupFilter_GetSubSystemIdFromFilterInfo)
DECLARE_FUNCTION_POINTER(HkGroupFilter_GetSystemGroupFromFilterInfo)
DECLARE_FUNCTION_POINTER(HkGroupFilter_SetLayer)
DECLARE_FUNCTION_POINTER(HkGroupFilter_DisableCollisionsBetween)
DECLARE_FUNCTION_POINTER(HkGroupFilter_DisableCollisionsUsingBitfield)
DECLARE_FUNCTION_POINTER(HkGroupFilter_EnableCollisionsBetween)
DECLARE_FUNCTION_POINTER(HkGroupFilter_EnableCollisionsUsingBitfield)
DECLARE_FUNCTION_POINTER(HkGroupFilter_GetNewSystemGroup)
DECLARE_FUNCTION_POINTER(HkGlobal_ReleasePtr)
DECLARE_FUNCTION_POINTER(HkGlobal_ReleaseString)
DECLARE_FUNCTION_POINTER(HkGlobal_ReleaseArrayPtr)
DECLARE_FUNCTION_POINTER(HkHingeConstraintData_Create)
DECLARE_FUNCTION_POINTER(HkHingeConstraintData_SetInBodySpaceInternal)
DECLARE_FUNCTION_POINTER(HkHingeConstraintData_SetInWorldSpace)
DECLARE_FUNCTION_POINTER(HkHingeConstraintData_SetInertiaStabilizationFactor)
DECLARE_FUNCTION_POINTER(HkHingeConstraintData_GetInertiaStabilizationFactor)
DECLARE_FUNCTION_POINTER(HkInertiaTensorComputer_Create)
DECLARE_FUNCTION_POINTER(HkInertiaTensorComputer_CombineMassPropertiesInstance)
DECLARE_FUNCTION_POINTER(HkInertiaTensorComputer_Release)
DECLARE_FUNCTION_POINTER(HkInertiaTensorComputer_ComputeBoxVolumeMassProperties)
DECLARE_FUNCTION_POINTER(HkInertiaTensorComputer_ComputeCapsuleVolumeMassProperties)
DECLARE_FUNCTION_POINTER(HkInertiaTensorComputer_ComputeCylinderVolumeMassProperties)
DECLARE_FUNCTION_POINTER(HkInertiaTensorComputer_ComputeSphereVolumeMassProperties)
DECLARE_FUNCTION_POINTER(HkJobQueue_Create)
DECLARE_FUNCTION_POINTER(HkJobQueue_CreateWithNumThreads)
DECLARE_FUNCTION_POINTER(HkJobQueue_Release)
DECLARE_FUNCTION_POINTER(HkJobQueue_GetWaitPolicy)
DECLARE_FUNCTION_POINTER(HkJobQueue_SetWaitPolicy)
DECLARE_FUNCTION_POINTER(HkJobQueue_GetMasterThreadFinishingFlags)
DECLARE_FUNCTION_POINTER(HkJobQueue_SetMasterThreadFinishingFlags)
DECLARE_FUNCTION_POINTER(HkJobQueue_ProcessAllJobs)
DECLARE_FUNCTION_POINTER(HkJobThreadPool_Create)
DECLARE_FUNCTION_POINTER(HkJobThreadPool_CreateWithNumThreads)
DECLARE_FUNCTION_POINTER(HkJobThreadPool_RemoveReference)
DECLARE_FUNCTION_POINTER(HkJobThreadPool_RunOnEachWorker)
DECLARE_FUNCTION_POINTER(HkJobThreadPool_ExecuteJobQueue)
DECLARE_FUNCTION_POINTER(HkJobThreadPool_GetThisThreadIndex)
DECLARE_FUNCTION_POINTER(HkJobThreadPool_WaitForCompletion)
DECLARE_FUNCTION_POINTER(HkJobThreadPool_ClearTimerData)
DECLARE_FUNCTION_POINTER(HkKeyFrameUtility_ApplyHardKeyFrame)
DECLARE_FUNCTION_POINTER(HkLimitedForceConstraintMotor_GetMinForce)
DECLARE_FUNCTION_POINTER(HkLimitedForceConstraintMotor_SetMinForce)
DECLARE_FUNCTION_POINTER(HkLimitedForceConstraintMotor_GetMaxForce)
DECLARE_FUNCTION_POINTER(HkLimitedForceConstraintMotor_SetMaxForce)
DECLARE_FUNCTION_POINTER(HkLimitedHingeConstraintData_Create)
DECLARE_FUNCTION_POINTER(HkLimitedHingeConstraintData_SetInBodySpaceInternal)
DECLARE_FUNCTION_POINTER(HkLimitedHingeConstraintData_SetInWorldSpace)
DECLARE_FUNCTION_POINTER(HkLimitedHingeConstraintData_SetMotor)
DECLARE_FUNCTION_POINTER(HkLimitedHingeConstraintData_IsMotorEnabled)
DECLARE_FUNCTION_POINTER(HkLimitedHingeConstraintData_SetMotorEnabled)
DECLARE_FUNCTION_POINTER(HkLimitedHingeConstraintData_GetTargetAngle)
DECLARE_FUNCTION_POINTER(HkLimitedHingeConstraintData_SetTargetAngle)
DECLARE_FUNCTION_POINTER(HkLimitedHingeConstraintData_GetMaxFrictionTorque)
DECLARE_FUNCTION_POINTER(HkLimitedHingeConstraintData_SetMaxFrictionTorque)
DECLARE_FUNCTION_POINTER(HkLimitedHingeConstraintData_GetMinAngularLimit)
DECLARE_FUNCTION_POINTER(HkLimitedHingeConstraintData_SetMinAngularLimit)
DECLARE_FUNCTION_POINTER(HkLimitedHingeConstraintData_GetMaxAngularLimit)
DECLARE_FUNCTION_POINTER(HkLimitedHingeConstraintData_SetMaxAngularLimit)
DECLARE_FUNCTION_POINTER(HkLimitedHingeConstraintData_DisableLimits)
DECLARE_FUNCTION_POINTER(HkLimitedHingeConstraintData_SetInertiaStabilizationFactor)
DECLARE_FUNCTION_POINTER(HkLimitedHingeConstraintData_GetInertiaStabilizationFactor)
DECLARE_FUNCTION_POINTER(HkLimitedHingeConstraintData_GetBodyAPos)
DECLARE_FUNCTION_POINTER(HkLimitedHingeConstraintData_GetBodyBPos)
DECLARE_FUNCTION_POINTER(HkLimitedHingeConstraintData_GetIsInitialized)
DECLARE_FUNCTION_POINTER(HkLimitedHingeConstraintData_SetIsInitialized)
DECLARE_FUNCTION_POINTER(HkLimitedHingeConstraintData_GetPreviousTargetAngle)
DECLARE_FUNCTION_POINTER(HkLimitedHingeConstraintData_SetPreviousTargetAngle)
DECLARE_FUNCTION_POINTER(HkLimitedHingeConstraintData_GetCurrentAngle)
DECLARE_FUNCTION_POINTER(HkLimitedHingeConstraintData_SetCurrentAngle)
DECLARE_FUNCTION_POINTER(HkListShape_Create)
DECLARE_FUNCTION_POINTER(HkListShape_GetDisabledChildrenCount)
DECLARE_FUNCTION_POINTER(HkListShape_GetTotalChildrenCount)
DECLARE_FUNCTION_POINTER(HkListShape_EnableShape)
DECLARE_FUNCTION_POINTER(HkListShape_GetChildByIndex)
DECLARE_FUNCTION_POINTER(HkListShape_IsChildEnabled)
DECLARE_FUNCTION_POINTER(HkMalleableConstraintData_Create)
DECLARE_FUNCTION_POINTER(HkMalleableConstraintData_GetStrength)
DECLARE_FUNCTION_POINTER(HkMalleableConstraintData_SetStrength)
DECLARE_FUNCTION_POINTER(HkMassChangerUtil_Create)
DECLARE_FUNCTION_POINTER(HkMassChangerUtil_IsValid)
DECLARE_FUNCTION_POINTER(HkMassChangerUtil_Remove)
DECLARE_FUNCTION_POINTER(HkMemorySnapshot_Diff)
DECLARE_FUNCTION_POINTER(HkMoppBvTreeShape_Create)
DECLARE_FUNCTION_POINTER(HkMoppBvTreeShape_GetShapeCollection)
DECLARE_FUNCTION_POINTER(HkMoppBvTreeShape_DisableKeys)
DECLARE_FUNCTION_POINTER(HkMoppBvTreeShape_QueryAABB)
DECLARE_FUNCTION_POINTER(HkMoppBvTreeShape_QueryPoint)
DECLARE_FUNCTION_POINTER(HkMotion_SetWorldMatrix)
DECLARE_FUNCTION_POINTER(HkMotion_GetDeactivationClass)
DECLARE_FUNCTION_POINTER(HkMotion_SetDeactivationClass)
DECLARE_FUNCTION_POINTER(HkPhantomCallbackShape_Create)
DECLARE_FUNCTION_POINTER(HkPrismaticConstraintData_Create)
DECLARE_FUNCTION_POINTER(HkPrismaticConstraintData_SetInWorldSpace)
DECLARE_FUNCTION_POINTER(HkPrismaticConstraintData_SetInBodySpaceInternal)
DECLARE_FUNCTION_POINTER(HkPrismaticConstraintData_GetMaximumLinearLimit)
DECLARE_FUNCTION_POINTER(HkPrismaticConstraintData_SetMaximumLinearLimit)
DECLARE_FUNCTION_POINTER(HkPrismaticConstraintData_GetMinimumLinearLimit)
DECLARE_FUNCTION_POINTER(HkPrismaticConstraintData_SetMinimumLinearLimit)
DECLARE_FUNCTION_POINTER(HkPrismaticConstraintData_GetMaxFrictionForce)
DECLARE_FUNCTION_POINTER(HkPrismaticConstraintData_SetMaxFrictionForce)
DECLARE_FUNCTION_POINTER(HkPrismaticConstraintData_GetTargetPosition)
DECLARE_FUNCTION_POINTER(HkPrismaticConstraintData_SetTargetPosition)
DECLARE_FUNCTION_POINTER(HkPrismaticConstraintData_SetMotor)
DECLARE_FUNCTION_POINTER(HkPrismaticConstraintData_IsMotorEnabled)
DECLARE_FUNCTION_POINTER(HkPrismaticConstraintData_SetMotorEnabled)
DECLARE_FUNCTION_POINTER(HkPrismaticConstraintData_GetCurrentPosition)
DECLARE_FUNCTION_POINTER(HkPhysicsSystem_IsActive)
DECLARE_FUNCTION_POINTER(HkPhysicsSystem_SetActive)
DECLARE_FUNCTION_POINTER(HkPhysicsSystem_RecreateConstraints)
DECLARE_FUNCTION_POINTER(HkPhysicsSystem_GetConstraintDataFromSystem)
DECLARE_FUNCTION_POINTER(HkPhysicsSystem_GetName)
DECLARE_FUNCTION_POINTER(HkPhysicsSystem_LoadRagdollFromFile)
DECLARE_FUNCTION_POINTER(HkPhysicsSystem_LoadRagdollFromBuffer)
DECLARE_FUNCTION_POINTER(HkPhysicsSystem_InitFromData)
DECLARE_FUNCTION_POINTER(HkpGroupFilter_CalcFilterInfo)
DECLARE_FUNCTION_POINTER(HkpGroupFilter_CalcFilterInfoFromCurrent)
DECLARE_FUNCTION_POINTER(HkpInertiaTensorComputer_OptimizeInertiasOfConstraintTree)
DECLARE_FUNCTION_POINTER(HkPhysicsSystem_Release)
DECLARE_FUNCTION_POINTER(HkRagdollConstraintData_GetPlaneMinAngularLimit)
DECLARE_FUNCTION_POINTER(HkRagdollConstraintData_SetPlaneMinAngularLimit)
DECLARE_FUNCTION_POINTER(HkRagdollConstraintData_GetPlaneMaxAngularLimit)
DECLARE_FUNCTION_POINTER(HkRagdollConstraintData_SetPlaneMaxAngularLimit)
DECLARE_FUNCTION_POINTER(HkRagdollConstraintData_GetTwistMinAngularLimit)
DECLARE_FUNCTION_POINTER(HkRagdollConstraintData_SetTwistMinAngularLimit)
DECLARE_FUNCTION_POINTER(HkRagdollConstraintData_GetTwistMaxAngularLimit)
DECLARE_FUNCTION_POINTER(HkRagdollConstraintData_SetTwistMaxAngularLimit)
DECLARE_FUNCTION_POINTER(HkRagdollConstraintData_GetMaxFrictionTorque)
DECLARE_FUNCTION_POINTER(HkRagdollConstraintData_SetMaxFrictionTorque)
DECLARE_FUNCTION_POINTER(HkRagdollConstraintData_SetInBodySpaceInternal)
DECLARE_FUNCTION_POINTER(HkRagdollConstraintData_SetAsymmetricConeAngle)
DECLARE_FUNCTION_POINTER(HkRagdollConstraintData_SetConeLimitStabilization)
DECLARE_FUNCTION_POINTER(HkReferenceObject_AddReference)
DECLARE_FUNCTION_POINTER(HkReferenceObject_RemoveReference)
DECLARE_FUNCTION_POINTER(HkReferenceObject_IsValid)
DECLARE_FUNCTION_POINTER(HkReferenceObject_DebugRemoveRef)
DECLARE_FUNCTION_POINTER(HkReferenceObject_ReferenceCount)
DECLARE_FUNCTION_POINTER(HkRigidBody_Create)
DECLARE_FUNCTION_POINTER(HkRigidBody_CreateWithCustomVelocity)
DECLARE_FUNCTION_POINTER(HkRigidBody_SetNumShapeKeysInContactPointProperties)
DECLARE_FUNCTION_POINTER(HkRigidBody_GetResponseModifiers)
DECLARE_FUNCTION_POINTER(HkRigidBody_SetResponseModifiers)
DECLARE_FUNCTION_POINTER(HkRigidBody_GetShape)
DECLARE_FUNCTION_POINTER(HkRigidBody_SetShape)
DECLARE_FUNCTION_POINTER(HkRigidBody_UpdateShape)
DECLARE_FUNCTION_POINTER(HkRigidBody_PredictRigidBodyMatrix)
DECLARE_FUNCTION_POINTER(HkRigidBody_SetMassProperties)
DECLARE_FUNCTION_POINTER(HkRigidBody_SetWorldMatrix)
DECLARE_FUNCTION_POINTER(HkRigidBody_SetTransform)
DECLARE_FUNCTION_POINTER(HkRigidBody_GetEnableDeactivation)
DECLARE_FUNCTION_POINTER(HkRigidBody_SetEnableDeactivation)
DECLARE_FUNCTION_POINTER(HkRigidBody_GetMarkedForVelocityRecompute)
DECLARE_FUNCTION_POINTER(HkRigidBody_SetMarkedForVelocityRecompute)
DECLARE_FUNCTION_POINTER(HkRigidBody_GetMotion)
DECLARE_FUNCTION_POINTER(HkRigidBody_GetMass)
DECLARE_FUNCTION_POINTER(HkRigidBody_SetMass)
DECLARE_FUNCTION_POINTER(HkRigidBody_GetCenterOfMassLocal)
DECLARE_FUNCTION_POINTER(HkRigidBody_SetCenterOfMassLocal)
DECLARE_FUNCTION_POINTER(HkRigidBody_GetInertiaTensor)
DECLARE_FUNCTION_POINTER(HkRigidBody_SetInertiaTensor)
DECLARE_FUNCTION_POINTER(HkRigidBody_GetInverseInertiaTensor)
DECLARE_FUNCTION_POINTER(HkRigidBody_SetInverseInertiaTensor)
DECLARE_FUNCTION_POINTER(HkRigidBody_GetCenterOfMassWorld)
DECLARE_FUNCTION_POINTER(HkRigidBody_GetCustomVelocity)
DECLARE_FUNCTION_POINTER(HkRigidBody_SetCustomVelocity)
DECLARE_FUNCTION_POINTER(HkRigidBody_GetDeltaAngle)
DECLARE_FUNCTION_POINTER(HkRigidBody_GetLinearDamping)
DECLARE_FUNCTION_POINTER(HkRigidBody_SetLinearDamping)
DECLARE_FUNCTION_POINTER(HkRigidBody_GetAngularDamping)
DECLARE_FUNCTION_POINTER(HkRigidBody_SetAngularDamping)
DECLARE_FUNCTION_POINTER(HkRigidBody_GetMaxLinearVelocity)
DECLARE_FUNCTION_POINTER(HkRigidBody_SetMaxLinearVelocity)
DECLARE_FUNCTION_POINTER(HkRigidBody_GetMaxAngularVelocity)
DECLARE_FUNCTION_POINTER(HkRigidBody_SetMaxAngularVelocity)
DECLARE_FUNCTION_POINTER(HkRigidBody_GetAllowedPenetrationDepth)
DECLARE_FUNCTION_POINTER(HkRigidBody_SetAllowedPenetrationDepth)
DECLARE_FUNCTION_POINTER(HkRigidBody_GetFriction)
DECLARE_FUNCTION_POINTER(HkRigidBody_SetFriction)
DECLARE_FUNCTION_POINTER(HkRigidBody_GetRestitution)
DECLARE_FUNCTION_POINTER(HkRigidBody_SetRestitution)
DECLARE_FUNCTION_POINTER(HkRigidBody_ApplyLinearImpulse)
DECLARE_FUNCTION_POINTER(HkRigidBody_ApplyPointImpulse)
DECLARE_FUNCTION_POINTER(HkRigidBody_ApplyAngularImpulse)
DECLARE_FUNCTION_POINTER(HkRigidBody_SetLayer)
DECLARE_FUNCTION_POINTER(HkRigidBody_GetCollisionFilterInfo)
DECLARE_FUNCTION_POINTER(HkRigidBody_SetCollisionFilterInfo)
DECLARE_FUNCTION_POINTER(HkRigidBody_ApplyForce)
DECLARE_FUNCTION_POINTER(HkRigidBody_ApplyForceToPoint)
DECLARE_FUNCTION_POINTER(HkRigidBody_ApplyTorque)
DECLARE_FUNCTION_POINTER(HkRigidBody_GetNativeObjectName)
DECLARE_FUNCTION_POINTER(HkRigidBody_RemoveFromWorld)
DECLARE_FUNCTION_POINTER(HkRigidBody_HasGravity)
DECLARE_FUNCTION_POINTER(HkRigidBody_HasConstraints)
DECLARE_FUNCTION_POINTER(HkRigidBody_GetBreakableBody)
DECLARE_FUNCTION_POINTER(HkRigidBody_GetGravity)
DECLARE_FUNCTION_POINTER(HkRigidBody_ReleaseGravity)
DECLARE_FUNCTION_POINTER(HkRigidBody_SetGravity)
DECLARE_FUNCTION_POINTER(HkRigidBody_Clone)
DECLARE_FUNCTION_POINTER(HkRigidBody_FromShape)
DECLARE_FUNCTION_POINTER(HkRigidBody_GetGcRoot)
DECLARE_FUNCTION_POINTER(HkRigidBody_GetGravityAction)
DECLARE_FUNCTION_POINTER(HkRigidBody_AddGravityAction)
DECLARE_FUNCTION_POINTER(HkRigidBody_GetDeactivationCounter0)
DECLARE_FUNCTION_POINTER(HkRigidBody_GetDeactivationCounter1)
DECLARE_FUNCTION_POINTER(HkRigidBody_HasActions)
DECLARE_FUNCTION_POINTER(HkRigidBodyCinfo_Create)
DECLARE_FUNCTION_POINTER(HkRigidBodyCinfo_Release)
DECLARE_FUNCTION_POINTER(HkRigidBodyCinfo_GetCollisionResponse)
DECLARE_FUNCTION_POINTER(HkRigidBodyCinfo_SetCollisionResponse)
DECLARE_FUNCTION_POINTER(HkRigidBodyCinfo_GetResponseModifiers)
DECLARE_FUNCTION_POINTER(HkRigidBodyCinfo_SetResponseModifiers)
DECLARE_FUNCTION_POINTER(HkRigidBodyCinfo_GetPosition)
DECLARE_FUNCTION_POINTER(HkRigidBodyCinfo_SetPosition)
DECLARE_FUNCTION_POINTER(HkRigidBodyCinfo_GetRotation)
DECLARE_FUNCTION_POINTER(HkRigidBodyCinfo_SetRotation)
DECLARE_FUNCTION_POINTER(HkRigidBodyCinfo_GetLinearVelocity)
DECLARE_FUNCTION_POINTER(HkRigidBodyCinfo_SetLinearVelocity)
DECLARE_FUNCTION_POINTER(HkRigidBodyCinfo_GetAngularVelocity)
DECLARE_FUNCTION_POINTER(HkRigidBodyCinfo_SetAngularVelocity)
DECLARE_FUNCTION_POINTER(HkRigidBodyCinfo_GetCenterOfMass)
DECLARE_FUNCTION_POINTER(HkRigidBodyCinfo_SetCenterOfMass)
DECLARE_FUNCTION_POINTER(HkRigidBodyCinfo_GetMass)
DECLARE_FUNCTION_POINTER(HkRigidBodyCinfo_SetMass)
DECLARE_FUNCTION_POINTER(HkRigidBodyCinfo_GetLinearDamping)
DECLARE_FUNCTION_POINTER(HkRigidBodyCinfo_SetLinearDamping)
DECLARE_FUNCTION_POINTER(HkRigidBodyCinfo_GetAngularDamping)
DECLARE_FUNCTION_POINTER(HkRigidBodyCinfo_SetAngularDamping)
DECLARE_FUNCTION_POINTER(HkRigidBodyCinfo_GetFriction)
DECLARE_FUNCTION_POINTER(HkRigidBodyCinfo_SetFriction)
DECLARE_FUNCTION_POINTER(HkRigidBodyCinfo_GetRestitution)
DECLARE_FUNCTION_POINTER(HkRigidBodyCinfo_SetRestitution)
DECLARE_FUNCTION_POINTER(HkRigidBodyCinfo_GetMaxLinearVelocity)
DECLARE_FUNCTION_POINTER(HkRigidBodyCinfo_SetMaxLinearVelocity)
DECLARE_FUNCTION_POINTER(HkRigidBodyCinfo_GetMaxAngularVelocity)
DECLARE_FUNCTION_POINTER(HkRigidBodyCinfo_SetMaxAngularVelocity)
DECLARE_FUNCTION_POINTER(HkRigidBodyCinfo_GetContactPointCallbackDelay)
DECLARE_FUNCTION_POINTER(HkRigidBodyCinfo_SetContactPointCallbackDelay)
DECLARE_FUNCTION_POINTER(HkRigidBodyCinfo_GetAllowedPenetrationDepth)
DECLARE_FUNCTION_POINTER(HkRigidBodyCinfo_SetAllowedPenetrationDepth)
DECLARE_FUNCTION_POINTER(HkRigidBodyCinfo_GetMotionType)
DECLARE_FUNCTION_POINTER(HkRigidBodyCinfo_SetMotionType)
DECLARE_FUNCTION_POINTER(HkRigidBodyCinfo_GetSolverDeactivation)
DECLARE_FUNCTION_POINTER(HkRigidBodyCinfo_SetSolverDeactivation)
DECLARE_FUNCTION_POINTER(HkRigidBodyCinfo_GetQualityType)
DECLARE_FUNCTION_POINTER(HkRigidBodyCinfo_SetQualityType)
DECLARE_FUNCTION_POINTER(HkRigidBodyCinfo_GetAutoRemoveLevel)
DECLARE_FUNCTION_POINTER(HkRigidBodyCinfo_SetAutoRemoveLevel)
DECLARE_FUNCTION_POINTER(HkRigidBodyCinfo_GetShape)
DECLARE_FUNCTION_POINTER(HkRigidBodyCinfo_SetShape)
DECLARE_FUNCTION_POINTER(HkRigidBodyCinfo_CalculateBoxInertiaTensor)
DECLARE_FUNCTION_POINTER(HkRigidBodyCinfo_CalculateSphereInertiaTensor)
DECLARE_FUNCTION_POINTER(HkRigidBodyCinfo_SetMassProperties)
DECLARE_FUNCTION_POINTER(HkRigidBodyCinfo_ComputeShapeMass)
DECLARE_FUNCTION_POINTER(HkRopeConstraintData_Create)
DECLARE_FUNCTION_POINTER(HkRopeConstraintData_SetInBodySpaceInternal)
DECLARE_FUNCTION_POINTER(HkRopeConstraintData_Update)
DECLARE_FUNCTION_POINTER(HkRopeConstraintData_GetStrength)
DECLARE_FUNCTION_POINTER(HkRopeConstraintData_SetStrength)
DECLARE_FUNCTION_POINTER(HkRopeConstraintData_GetLinearLimit)
DECLARE_FUNCTION_POINTER(HkRopeConstraintData_SetLinearLimit)
DECLARE_FUNCTION_POINTER(HkRopeConstraintData_IsValid)
DECLARE_FUNCTION_POINTER(HkShape_GetReferenceCount)
DECLARE_FUNCTION_POINTER(HkShape_GetShapeType)
DECLARE_FUNCTION_POINTER(HkShape_IsConvex)
DECLARE_FUNCTION_POINTER(HkShape_GetConvexRadius)
DECLARE_FUNCTION_POINTER(HkShape_SetConvexRadius)
DECLARE_FUNCTION_POINTER(HkShape_GetUserData)
DECLARE_FUNCTION_POINTER(HkShape_SetUserData)
DECLARE_FUNCTION_POINTER(HkShape_SetRigidBody)
DECLARE_FUNCTION_POINTER(HkShape_IsContainer)
DECLARE_FUNCTION_POINTER(HkShape_AddReference)
DECLARE_FUNCTION_POINTER(HkShape_RemoveReference)
DECLARE_FUNCTION_POINTER(HkShape_DisableRefCount)
DECLARE_FUNCTION_POINTER(HkShape_GetLocalAABB)
DECLARE_FUNCTION_POINTER(HkShape_CastRayCollectSingleHit)
DECLARE_FUNCTION_POINTER(HkShape_LoadShapeFromFile)
DECLARE_FUNCTION_POINTER(HkShape_GetContainer)
DECLARE_FUNCTION_POINTER(HkShapeBatch_GetCount)
DECLARE_FUNCTION_POINTER(HkShapeBatch_GetInfo)
DECLARE_FUNCTION_POINTER(HkShapeBatch_SetResult)
DECLARE_FUNCTION_POINTER(HkShapeBuffer_Create)
DECLARE_FUNCTION_POINTER(HkShapeBuffer_Destroy)
DECLARE_FUNCTION_POINTER(HkShapeCollection_GetShapeCount)
DECLARE_FUNCTION_POINTER(HkShapeCollection_GetShape)
DECLARE_FUNCTION_POINTER(HkShapeCollection_GetShapeWithBuffer)
DECLARE_FUNCTION_POINTER(HkShapeContainer_GetFirstKey)
DECLARE_FUNCTION_POINTER(HkShapeContainer_GetNextKey)
DECLARE_FUNCTION_POINTER(HkShapeContainer_CurrentValue)
DECLARE_FUNCTION_POINTER(HkShapeContainer_GetShape)
DECLARE_FUNCTION_POINTER(HkShapeContainer_IsShapeKeyValid)
DECLARE_FUNCTION_POINTER(HkShapeCutterUtil_Cut)
DECLARE_FUNCTION_POINTER(HkShapeLoader_LoadShapesListFromBuffer)
DECLARE_FUNCTION_POINTER(HkShapeLoader_LoadShapesListFromFile)
DECLARE_FUNCTION_POINTER(HkShapeLoader_SaveShapesListToFile)
DECLARE_FUNCTION_POINTER(HkShapeLoader_CleanupShapesBuffer)
DECLARE_FUNCTION_POINTER(HkSimpleMeshShape_Create)
DECLARE_FUNCTION_POINTER(HkSimpleShapePhantom_Create)
DECLARE_FUNCTION_POINTER(HkSimpleShapePhantom_CreateWithLayer)
DECLARE_FUNCTION_POINTER(HkSimpleShapePhantom_GetShape)
DECLARE_FUNCTION_POINTER(HkSimpleValueProperty_CreateFloat)
DECLARE_FUNCTION_POINTER(HkSimpleValueProperty_CreateUInt)
DECLARE_FUNCTION_POINTER(HkSimpleValueProperty_CreateInt)
DECLARE_FUNCTION_POINTER(HkSimpleValueProperty_GetValueFloat)
DECLARE_FUNCTION_POINTER(HkSimpleValueProperty_SetValueFloat)
DECLARE_FUNCTION_POINTER(HkSimpleValueProperty_GetValueUInt)
DECLARE_FUNCTION_POINTER(HkSimpleValueProperty_SetValueUInt)
DECLARE_FUNCTION_POINTER(HkSimpleValueProperty_GetValueInt)
DECLARE_FUNCTION_POINTER(HkSimpleValueProperty_SetValueInt)
DECLARE_FUNCTION_POINTER(HkSimulationIsland_GetEntityCount)
DECLARE_FUNCTION_POINTER(HkSimulationIsland_GetEntity)
DECLARE_FUNCTION_POINTER(HkSimulationIsland_GetBounds)
DECLARE_FUNCTION_POINTER(HkSimulationIsland_GetOffsets)
DECLARE_FUNCTION_POINTER(HkSmartListShape_Create)
DECLARE_FUNCTION_POINTER(HkSmartListShape_GetShapeCount)
DECLARE_FUNCTION_POINTER(HkSmartListShape_AddShape)
DECLARE_FUNCTION_POINTER(HkSmartListShape_RemoveShape)
DECLARE_FUNCTION_POINTER(HkSmartListShape_Validate)
DECLARE_FUNCTION_POINTER(HkSphereShape_Create)
DECLARE_FUNCTION_POINTER(HkSphereShape_GetRadius)
DECLARE_FUNCTION_POINTER(HkSphereShape_SetRadius)
DECLARE_FUNCTION_POINTER(HkStaticCompoundShape_Create)
DECLARE_FUNCTION_POINTER(HkStaticCompoundShape_GetInstanceCount)
DECLARE_FUNCTION_POINTER(HkStaticCompoundShape_AddInstance)
DECLARE_FUNCTION_POINTER(HkStaticCompoundShape_Bake)
DECLARE_FUNCTION_POINTER(HkStaticCompoundShape_ComposeShapeKey)
DECLARE_FUNCTION_POINTER(HkStaticCompoundShape_DecomposeShapeKey)
DECLARE_FUNCTION_POINTER(HkStaticCompoundShape_EnableAllShapeKeys)
DECLARE_FUNCTION_POINTER(HkStaticCompoundShape_EnableInstance)
DECLARE_FUNCTION_POINTER(HkStaticCompoundShape_EnableShapeKey)
DECLARE_FUNCTION_POINTER(HkStaticCompoundShape_GetFirstKey)
DECLARE_FUNCTION_POINTER(HkStaticCompoundShape_GetInstance)
DECLARE_FUNCTION_POINTER(HkStaticCompoundShape_GetInstanceTransform)
DECLARE_FUNCTION_POINTER(HkStaticCompoundShape_IsInstanceEnabled)
DECLARE_FUNCTION_POINTER(HkStaticCompoundShape_IsShapeKeyEnabled)
DECLARE_FUNCTION_POINTER(HkTaskProfiler_Init)
DECLARE_FUNCTION_POINTER(HkTaskProfiler_ReleaseResources)
DECLARE_FUNCTION_POINTER(HkTaskProfiler_HookJobQueue)
DECLARE_FUNCTION_POINTER(HkTaskProfiler_ReplayTimers)
DECLARE_FUNCTION_POINTER(HkTaskProfiler_Begin1)
DECLARE_FUNCTION_POINTER(HkTaskProfiler_Begin2)
DECLARE_FUNCTION_POINTER(HkTaskProfiler_Begin3)
DECLARE_FUNCTION_POINTER(HkTaskProfiler_Begin4)
DECLARE_FUNCTION_POINTER(HkTaskProfiler_Begin5)
DECLARE_FUNCTION_POINTER(HkTaskProfiler_End)
DECLARE_FUNCTION_POINTER(HkTransformShape_Create)
DECLARE_FUNCTION_POINTER(HkTransformShape_CreateWithTranslation)
DECLARE_FUNCTION_POINTER(HkTransformShape_GetTransform)
DECLARE_FUNCTION_POINTER(HkTransformShape_GetChildShape)
DECLARE_FUNCTION_POINTER(HkTriangleShape_GetExtrusion)
DECLARE_FUNCTION_POINTER(HkTriangleShape_GetPt2)
DECLARE_FUNCTION_POINTER(HkTriangleShape_GetPt1)
DECLARE_FUNCTION_POINTER(HkTriangleShape_GetPt0)
DECLARE_FUNCTION_POINTER(HkUniformGridShape_Create)
DECLARE_FUNCTION_POINTER(HkUniformGridShape_GetShapeCount)
DECLARE_FUNCTION_POINTER(HkUniformGridShape_DiscardLargeData)
DECLARE_FUNCTION_POINTER(HkUniformGridShape_GetHitsAndClear)
DECLARE_FUNCTION_POINTER(HkUniformGridShape_GetHitCellsInRange)
DECLARE_FUNCTION_POINTER(HkUniformGridShape_GetMissingCellsInRange)
DECLARE_FUNCTION_POINTER(HkUniformGridShape_InvalidateRange)
DECLARE_FUNCTION_POINTER(HkUniformGridShape_InvalidateRangeImmediate)
DECLARE_FUNCTION_POINTER(HkUniformGridShape_RemoveChild)
DECLARE_FUNCTION_POINTER(HkUniformGridShape_SetChild)
DECLARE_FUNCTION_POINTER(HkUniformGridShape_GetChild)
DECLARE_FUNCTION_POINTER(HkUniformGridShape_SetDeleteHandler)
DECLARE_FUNCTION_POINTER(HkUniformGridShape_RemoveShapeRequestHandler)
DECLARE_FUNCTION_POINTER(HkUniformGridShape_SetShapeRequestHandler)
DECLARE_FUNCTION_POINTER(HkUniformGridShape_EnableExtendedCache)
DECLARE_FUNCTION_POINTER(HkUtils_CalculateSeparatingVelocity)
DECLARE_FUNCTION_POINTER(HkUtils_SetSoftContact)
DECLARE_FUNCTION_POINTER(HkVDB_SyncTimers)
DECLARE_FUNCTION_POINTER(HkVDB_StepVDB)
DECLARE_FUNCTION_POINTER(HkVDB_Start)
DECLARE_FUNCTION_POINTER(HkVDB_ReleaseResources)
DECLARE_FUNCTION_POINTER(HkVDB_GetPort)
DECLARE_FUNCTION_POINTER(HkVDB_SetPort)
DECLARE_FUNCTION_POINTER(HkVDB_UpdateCamera)
DECLARE_FUNCTION_POINTER(HkVDB_Capture)
DECLARE_FUNCTION_POINTER(HkVDB_EndCapture)
DECLARE_FUNCTION_POINTER(HkVec3IProperty_Create)
DECLARE_FUNCTION_POINTER(HkVec3IProperty_GetValue)
DECLARE_FUNCTION_POINTER(HkVec3IProperty_SetValue)
DECLARE_FUNCTION_POINTER(HkVelocityConstraintMotor_Create)
DECLARE_FUNCTION_POINTER(HkVelocityConstraintMotor_GetTau)
DECLARE_FUNCTION_POINTER(HkVelocityConstraintMotor_SetTau)
DECLARE_FUNCTION_POINTER(HkVelocityConstraintMotor_GetVelocityTarget)
DECLARE_FUNCTION_POINTER(HkVelocityConstraintMotor_SetVelocityTarget)
DECLARE_FUNCTION_POINTER(HkVelocityConstraintMotor_GetConstantRecoveryVelocity)
DECLARE_FUNCTION_POINTER(HkVelocityConstraintMotor_SetConstantRecoveryVelocity)
DECLARE_FUNCTION_POINTER(HkWheelConstraintData_Create)
DECLARE_FUNCTION_POINTER(HkWheelConstraintData_SetInWorldSpace)
DECLARE_FUNCTION_POINTER(HkWheelConstraintData_SetInBodySpaceInternal)
DECLARE_FUNCTION_POINTER(HkWheelConstraintData_SetSuspensionMinLimit)
DECLARE_FUNCTION_POINTER(HkWheelConstraintData_SetSuspensionMaxLimit)
DECLARE_FUNCTION_POINTER(HkWheelConstraintData_SetSuspensionStrength)
DECLARE_FUNCTION_POINTER(HkWheelConstraintData_SetSuspensionDamping)
DECLARE_FUNCTION_POINTER(HkWheelConstraintData_SetSteeringAngle)
DECLARE_FUNCTION_POINTER(HkWheelResponseModifierUtil_Create)
DECLARE_FUNCTION_POINTER(HkWheelResponseModifierUtil_Release)
DECLARE_FUNCTION_POINTER(HkWorld_Create)
DECLARE_FUNCTION_POINTER(HkWorld_CreateCInfo)
DECLARE_FUNCTION_POINTER(HkWorld_CreateBodyPairCollection)
DECLARE_FUNCTION_POINTER(HkWorld_RegisterWithJobQueue)
DECLARE_FUNCTION_POINTER(HkWorld_Lock)
DECLARE_FUNCTION_POINTER(HkWorld_Unlock)
DECLARE_FUNCTION_POINTER(HkWorld_LockCriticalOperations)
DECLARE_FUNCTION_POINTER(HkWorld_UnlockCriticalOperations)
DECLARE_FUNCTION_POINTER(HkWorld_ExecutePendingCriticalOperations)
DECLARE_FUNCTION_POINTER(HkWorld_StepDeltaTime)
DECLARE_FUNCTION_POINTER(HkWorld_StepMultiThreaded)
DECLARE_FUNCTION_POINTER(HkWorld_InitMtStep)
DECLARE_FUNCTION_POINTER(HkWorld_FinishMtStep)
DECLARE_FUNCTION_POINTER(HkWorld_ExecuteViolatedConstraintProjections)
DECLARE_FUNCTION_POINTER(HkWorld_ReportRuntimeDataConstraints)
DECLARE_FUNCTION_POINTER(HkWorld_AddConstraint)
DECLARE_FUNCTION_POINTER(HkWorld_RemoveConstraint)
DECLARE_FUNCTION_POINTER(HkWorld_AddEntity)
DECLARE_FUNCTION_POINTER(HkWorld_RemoveEntity)
DECLARE_FUNCTION_POINTER(HkWorld_AddPhantom)
DECLARE_FUNCTION_POINTER(HkWorld_RemovePhantom)
DECLARE_FUNCTION_POINTER(HkWorld_AddPhysicsSystem)
DECLARE_FUNCTION_POINTER(HkWorld_RemovePhysicsSystem)
DECLARE_FUNCTION_POINTER(HkWorld_GetPenetrationsShape)
DECLARE_FUNCTION_POINTER(HkWorld_GetPenetrationsBox)
DECLARE_FUNCTION_POINTER(HkWorld_GetPenetrationsShapeShape)
DECLARE_FUNCTION_POINTER(HkWorld_IsPenetratingShapeShape)
DECLARE_FUNCTION_POINTER(HkWorld_IsPenetratingShapeShapeTransform)
DECLARE_FUNCTION_POINTER(HkWorld_CastShape)
DECLARE_FUNCTION_POINTER(HkWorld_CastShapeReturnPoint)
DECLARE_FUNCTION_POINTER(HkWorld_CastShapeReturnContact)
DECLARE_FUNCTION_POINTER(HkWorld_CastShapeReturnContactData)
DECLARE_FUNCTION_POINTER(HkWorld_CastShapeReturnContactBodyData)
DECLARE_FUNCTION_POINTER(HkWorld_CastShapeReturnContactBodyDatas)
DECLARE_FUNCTION_POINTER(HkWorld_CastRayAll)
DECLARE_FUNCTION_POINTER(HkWorld_CastRayCollisionFilter)
DECLARE_FUNCTION_POINTER(HkWorld_CastRayFilterLayer)
DECLARE_FUNCTION_POINTER(HkWorld_MarkForWrite)
DECLARE_FUNCTION_POINTER(HkWorld_UnmarkForWrite)
DECLARE_FUNCTION_POINTER(HkWorld_RefreshCollisionFilterOnEntity)
DECLARE_FUNCTION_POINTER(HkWorld_RefreshCollisionFilterOnWorld)
DECLARE_FUNCTION_POINTER(HkWorld_ReintegrateEntity)
DECLARE_FUNCTION_POINTER(HkWorld_AddAction)
DECLARE_FUNCTION_POINTER(HkWorld_RemoveAction)
DECLARE_FUNCTION_POINTER(HkWorld_EnsureBatchSizes)
DECLARE_FUNCTION_POINTER(HkWorld_SetBatchBody)
DECLARE_FUNCTION_POINTER(HkWorld_AddEntityBatch)
DECLARE_FUNCTION_POINTER(HkWorld_RemoveEntityBatch)
DECLARE_FUNCTION_POINTER(HkWorld_GetActiveSimulationIslandsCount)
DECLARE_FUNCTION_POINTER(HkWorld_GetActiveSimulationIslandEntities)
DECLARE_FUNCTION_POINTER(HkWorld_DeactivateSimulationIslandRigidBodies)
DECLARE_FUNCTION_POINTER(HkWorld_IsActiveSimulationIsland)
DECLARE_FUNCTION_POINTER(HkWorld_GetConstraintCount)
DECLARE_FUNCTION_POINTER(HkWorld_GetActionCount)
DECLARE_FUNCTION_POINTER(HkWorld_GetFixedBody)
DECLARE_FUNCTION_POINTER(HkWorld_ReadSimulationIslandInfos)
DECLARE_FUNCTION_POINTER(HkWorld_GetGravity)
DECLARE_FUNCTION_POINTER(HkWorld_SetGravity)
DECLARE_FUNCTION_POINTER(HkWorld_GetDeactivationRotationSqrdA)
DECLARE_FUNCTION_POINTER(HkWorld_SetDeactivationRotationSqrdA)
DECLARE_FUNCTION_POINTER(HkWorld_GetDeactivationRotationSqrdB)
DECLARE_FUNCTION_POINTER(HkWorld_SetDeactivationRotationSqrdB)
DECLARE_FUNCTION_POINTER(HkWorld_AddWorldExtension)
DECLARE_FUNCTION_POINTER(HkWorld_Release)
DECLARE_FUNCTION_POINTER(HkPhysicsContext_Create)
DECLARE_FUNCTION_POINTER(HkPhysicsContext_RegisterAllPhysicsProcesses)
DECLARE_FUNCTION_POINTER(HkPhysicsContext_AddWorld)
DECLARE_FUNCTION_POINTER(HkPhysicsContext_RemoveWorld)
DECLARE_FUNCTION_POINTER(HkPhysicsContext_GetNumWorlds)
DECLARE_FUNCTION_POINTER(HkPhysicsContext_SyncTimers)
DECLARE_FUNCTION_POINTER(HkPhysicsContext_Release)
DECLARE_FUNCTION_POINTER(HkGroupFilter_Create)
DECLARE_FUNCTION_POINTER(HkGroupFilter_IsCollisionEnabled)
DECLARE_FUNCTION_POINTER(HkpAabbPhantom_Create)
DECLARE_FUNCTION_POINTER(HkpAabbPhantom_GetAabb)
DECLARE_FUNCTION_POINTER(HkpAabbPhantom_SetAabb)
DECLARE_FUNCTION_POINTER(HkpAabbPhantom_Release)
DECLARE_FUNCTION_POINTER(HkpCollidableAddedEvent_GetRigidBody)
DECLARE_FUNCTION_POINTER(HkpCollidableRemovedEvent_GetRigidBody)
DECLARE_FUNCTION_POINTER(HkSimpleShapePhantom_SetTransform)
DECLARE_FUNCTION_POINTER(HkIntermediateBuffer_ReleaseUnmanaged)

static pe_image g_havok_image;

static void InitImpl(const char* dllPath)
{
    if (!load_dll(&g_havok_image, dllPath)) {
        LogMessage("Failed to load Havok.dll");
        throw std::runtime_error("Failed to load Havok.dll");
    }

    SET_FUNCTION_POINTER(HkActivationListener_Create)
    SET_FUNCTION_POINTER(HkBallAndSocketConstraintData_Create)
    SET_FUNCTION_POINTER(HkBallAndSocketConstraintData_SetInBodySpaceInternal)
    SET_FUNCTION_POINTER(HkBaseSystem_Init)
    SET_FUNCTION_POINTER(HkBaseSystem_Quit)
    SET_FUNCTION_POINTER(HkBaseSystem_InitThread)
    SET_FUNCTION_POINTER(HkBaseSystem_QuitThread)
    SET_FUNCTION_POINTER(HkBaseSystem_GetVersionInfo)
    SET_FUNCTION_POINTER(HkBaseSystem_GetMemoryStatistics)
    SET_FUNCTION_POINTER(HkBaseSystem_EnableAssert)
    SET_FUNCTION_POINTER(HkBaseSystem_IsEnabled)
    SET_FUNCTION_POINTER(HkBaseSystem_IsDestructionEnabled)
    SET_FUNCTION_POINTER(HkBaseSystem_OnSimulationFrameStarted)
    SET_FUNCTION_POINTER(HkBaseSystem_OnSimulationFrameFinished)
    SET_FUNCTION_POINTER(HkBaseSystem_GetKeyCodes)
    SET_FUNCTION_POINTER(HkBaseSystem_IsOutOfMemory)
    SET_FUNCTION_POINTER(HkBaseSystem_GetCurrentMemoryConsumption)
    SET_FUNCTION_POINTER(HkBoxShape_Create)
    SET_FUNCTION_POINTER(HkBoxShape_CreateWithConvexRadius)
    SET_FUNCTION_POINTER(HkBoxShape_GetShapeFromCompoundShape)
    SET_FUNCTION_POINTER(HkBoxShape_GetHalfExtents)
    SET_FUNCTION_POINTER(HkBoxShape_SetHalfExtents)
    SET_FUNCTION_POINTER(HkBreakOffPartsUtil_Create)
    SET_FUNCTION_POINTER(HkBreakOffPartsUtil_Release)
    SET_FUNCTION_POINTER(HkBreakOffPartsUtil_RemoveKeysFromListShape)
    SET_FUNCTION_POINTER(HkBreakOffPartsUtil_MarkEntityBreakable)
    SET_FUNCTION_POINTER(HkBreakOffPartsUtil_MarkPieceBreakable)
    SET_FUNCTION_POINTER(HkBreakOffPartsUtil_SetMaxConstraintImpulse)
    SET_FUNCTION_POINTER(HkBreakOffPartsUtil_UnmarkEntityBreakable)
    SET_FUNCTION_POINTER(HkBreakOffPartsUtil_UnmarkPieceBreakable)
    SET_FUNCTION_POINTER(HkBreakOffPoints_Count)
    SET_FUNCTION_POINTER(HkBreakOffPoints_Get)
    SET_FUNCTION_POINTER(HkBreakableConstraintData_Create)
    SET_FUNCTION_POINTER(HkBreakableConstraintData_GetThreshold)
    SET_FUNCTION_POINTER(HkBreakableConstraintData_SetThreshold)
    SET_FUNCTION_POINTER(HkBreakableConstraintData_GetRemoveFromWorldOnBrake)
    SET_FUNCTION_POINTER(HkBreakableConstraintData_SetRemoveFromWorldOnBrake)
    SET_FUNCTION_POINTER(HkBreakableConstraintData_GetReapplyVelocityOnBreak)
    SET_FUNCTION_POINTER(HkBreakableConstraintData_SetReapplyVelocityOnBreak)
    SET_FUNCTION_POINTER(HkBreakableConstraintData_GetIsBroken)
    SET_FUNCTION_POINTER(HkBvCompressedMeshShape_CreateWithSimpleMesh)
    SET_FUNCTION_POINTER(HkBvCompressedMeshShape_CreateWithParams)
    SET_FUNCTION_POINTER(HkBvCompressedMeshShape_CreateUnsafe)
    SET_FUNCTION_POINTER(HkBvCompressedMeshShape_GetGeometry)
    SET_FUNCTION_POINTER(HkBvCompressedMeshShape_GetUserData)
    SET_FUNCTION_POINTER(HkBvShape_Create)
    SET_FUNCTION_POINTER(HkBvShape_GetChildShape)
    SET_FUNCTION_POINTER(HkBvShape_GetBoundingVolumeShape)
    SET_FUNCTION_POINTER(HkCapsuleShape_Create)
    SET_FUNCTION_POINTER(HkCapsuleShape_GetRadius)
    SET_FUNCTION_POINTER(HkCapsuleShape_GetVertexB)
    SET_FUNCTION_POINTER(HkCapsuleShape_GetVertexA)
    SET_FUNCTION_POINTER(HkCapsuleShape_GetCentre)
    SET_FUNCTION_POINTER(HkCharacterProxy_Create)
    SET_FUNCTION_POINTER(HkCharacterProxy_GetPosition)
    SET_FUNCTION_POINTER(HkCharacterProxy_SetPosition)
    SET_FUNCTION_POINTER(HkCharacterProxy_GetState)
    SET_FUNCTION_POINTER(HkCharacterProxy_SetState)
    SET_FUNCTION_POINTER(HkCharacterProxy_StepSimulation)
    SET_FUNCTION_POINTER(HkCharacterProxy_GetLinearVelocity)
    SET_FUNCTION_POINTER(HkCharacterProxy_SetLinearVelocity)
    SET_FUNCTION_POINTER(HkCharacterProxy_SetUp)
    SET_FUNCTION_POINTER(HkCharacterProxyCinfo_Create)
    SET_FUNCTION_POINTER(HkCharacterProxyCinfo_GetPosition)
    SET_FUNCTION_POINTER(HkCharacterProxyCinfo_SetPosition)
    SET_FUNCTION_POINTER(HkCharacterProxyCinfo_GetVelocity)
    SET_FUNCTION_POINTER(HkCharacterProxyCinfo_SetVelocity)
    SET_FUNCTION_POINTER(HkCharacterProxyCinfo_GetDynamicFriction)
    SET_FUNCTION_POINTER(HkCharacterProxyCinfo_SetDynamicFriction)
    SET_FUNCTION_POINTER(HkCharacterProxyCinfo_GetStaticFriction)
    SET_FUNCTION_POINTER(HkCharacterProxyCinfo_SetStaticFriction)
    SET_FUNCTION_POINTER(HkCharacterProxyCinfo_GetKeepContactTolerance)
    SET_FUNCTION_POINTER(HkCharacterProxyCinfo_SetKeepContactTolerance)
    SET_FUNCTION_POINTER(HkCharacterProxyCinfo_GetUp)
    SET_FUNCTION_POINTER(HkCharacterProxyCinfo_SetUp)
    SET_FUNCTION_POINTER(HkCharacterProxyCinfo_GetExtraUpStaticFriction)
    SET_FUNCTION_POINTER(HkCharacterProxyCinfo_SetExtraUpStaticFriction)
    SET_FUNCTION_POINTER(HkCharacterProxyCinfo_GetExtraDownStaticFriction)
    SET_FUNCTION_POINTER(HkCharacterProxyCinfo_SetExtraDownStaticFriction)
    SET_FUNCTION_POINTER(HkCharacterProxyCinfo_SetShapePhantom)
    SET_FUNCTION_POINTER(HkCharacterProxyCinfo_GetShapePhantom)
    SET_FUNCTION_POINTER(HkCharacterProxyCinfo_GetKeepDistance)
    SET_FUNCTION_POINTER(HkCharacterProxyCinfo_SetKeepDistance)
    SET_FUNCTION_POINTER(HkCharacterProxyCinfo_GetContactAngleSensitivity)
    SET_FUNCTION_POINTER(HkCharacterProxyCinfo_SetContactAngleSensitivity)
    SET_FUNCTION_POINTER(HkCharacterProxyCinfo_GetUserPlanes)
    SET_FUNCTION_POINTER(HkCharacterProxyCinfo_SetUserPlanes)
    SET_FUNCTION_POINTER(HkCharacterProxyCinfo_GetMaxCharacterSpeedForSolver)
    SET_FUNCTION_POINTER(HkCharacterProxyCinfo_SetMaxCharacterSpeedForSolver)
    SET_FUNCTION_POINTER(HkCharacterProxyCinfo_GetCharacterStrength)
    SET_FUNCTION_POINTER(HkCharacterProxyCinfo_SetCharacterStrength)
    SET_FUNCTION_POINTER(HkCharacterProxyCinfo_GetCharacterMass)
    SET_FUNCTION_POINTER(HkCharacterProxyCinfo_SetCharacterMass)
    SET_FUNCTION_POINTER(HkCharacterProxyCinfo_GetMaxSlope)
    SET_FUNCTION_POINTER(HkCharacterProxyCinfo_SetMaxSlope)
    SET_FUNCTION_POINTER(HkCharacterProxyCinfo_GetPenetrationRecoverySpeed)
    SET_FUNCTION_POINTER(HkCharacterProxyCinfo_SetPenetrationRecoverySpeed)
    SET_FUNCTION_POINTER(HkCharacterProxyCinfo_GetMaxCastIterations)
    SET_FUNCTION_POINTER(HkCharacterProxyCinfo_SetMaxCastIterations)
    SET_FUNCTION_POINTER(HkCharacterProxyCinfo_GetRefreshManifoldInCheckSupport)
    SET_FUNCTION_POINTER(HkCharacterProxyCinfo_SetRefreshManifoldInCheckSupport)
    SET_FUNCTION_POINTER(HkCharacterRigidBody_Create)
    SET_FUNCTION_POINTER(HkCharacterRigidBody_GetCharacterRigidbody)
    SET_FUNCTION_POINTER(HkCharacterRigidBody_SetWalkingState)
    SET_FUNCTION_POINTER(HkCharacterRigidBody_SetFlyingState)
    SET_FUNCTION_POINTER(HkCharacterRigidBody_SetLadderState)
    SET_FUNCTION_POINTER(HkCharacterRigidBody_SetDefaultShape)
    SET_FUNCTION_POINTER(HkCharacterRigidBody_SetShapeForCrouch)
    SET_FUNCTION_POINTER(HkCharacterRigidBody_GetPosition)
    SET_FUNCTION_POINTER(HkCharacterRigidBody_SetPosition)
    SET_FUNCTION_POINTER(HkCharacterRigidBody_GetState)
    SET_FUNCTION_POINTER(HkCharacterRigidBody_SetState)
    SET_FUNCTION_POINTER(HkCharacterRigidBody_StepSimulation)
    SET_FUNCTION_POINTER(HkCharacterRigidBody_UpdateVelocity)
    SET_FUNCTION_POINTER(HkCharacterRigidBody_UpdateSupport)
    SET_FUNCTION_POINTER(HkCharacterRigidBody_SetRigidBodyTransform)
    SET_FUNCTION_POINTER(HkCharacterRigidBody_GetRigidBodyTransform)
    SET_FUNCTION_POINTER(HkCharacterRigidBody_GetLinearVelocity)
    SET_FUNCTION_POINTER(HkCharacterRigidBody_SetLinearVelocity)
    SET_FUNCTION_POINTER(HkCharacterRigidBody_ApplyLinearImpulse)
    SET_FUNCTION_POINTER(HkCharacterRigidBody_ApplyAngularImpulse)
    SET_FUNCTION_POINTER(HkCharacterRigidBody_SetSupportDistance)
    SET_FUNCTION_POINTER(HkCharacterRigidBody_SetHardSupportDistance)
    SET_FUNCTION_POINTER(HkCharacterRigidBody_GetAngularVelocity)
    SET_FUNCTION_POINTER(HkCharacterRigidBody_SetAngularVelocity)
    SET_FUNCTION_POINTER(HkCharacterRigidBody_IsSupportedByFloatingObject)
    SET_FUNCTION_POINTER(HkCharacterRigidBody_IsSupported)
    SET_FUNCTION_POINTER(HkCharacterRigidBody_GetSupportNormal)
    SET_FUNCTION_POINTER(HkCharacterRigidBody_GetGroundVelocity)
    SET_FUNCTION_POINTER(HkCharacterRigidBody_GetUseSupportInfoQuery)
    SET_FUNCTION_POINTER(HkCharacterRigidBody_SetUseSupportInfoQuery)
    SET_FUNCTION_POINTER(HkCharacterRigidBody_SetPreviousSupportedState)
    SET_FUNCTION_POINTER(HkCharacterRigidBody_ResetSurfaceVelocity)
    SET_FUNCTION_POINTER(HkCharacterRigidBody_SetMaxSlope)
    SET_FUNCTION_POINTER(HkCharacterRigidBody_GetMaxSlope)
    SET_FUNCTION_POINTER(HkCharacterRigidBody_GetSupportBodies)
    SET_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_Create)
    SET_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_GetCollisionFilterInfo)
    SET_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_SetCollisionFilterInfo)
    SET_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_GetShape)
    SET_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_SetShape)
    SET_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_GetPosition)
    SET_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_SetPosition)
    SET_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_GetRotation)
    SET_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_SetRotation)
    SET_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_GetMass)
    SET_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_SetMass)
    SET_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_GetFriction)
    SET_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_SetFriction)
    SET_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_GetMaxLinearVelocity)
    SET_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_SetMaxLinearVelocity)
    SET_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_GetAllowedPenetrationDepth)
    SET_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_SetAllowedPenetrationDepth)
    SET_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_GetUp)
    SET_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_SetUp)
    SET_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_GetMaxSlope)
    SET_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_SetMaxSlope)
    SET_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_GetMaxForce)
    SET_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_SetMaxForce)
    SET_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_GetUnweldingHeightOffsetFactor)
    SET_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_SetUnweldingHeightOffsetFactor)
    SET_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_GetMaxSpeedForSimplexSolver)
    SET_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_SetMaxSpeedForSimplexSolver)
    SET_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_GetSupportDistance)
    SET_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_SetSupportDistance)
    SET_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_GetHardSupportDistance)
    SET_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_SetHardSupportDistance)
    SET_FUNCTION_POINTER(HkCogWheelConstraintData_Create)
    SET_FUNCTION_POINTER(HkCogWheelConstraintData_SetInWorldSpace)
    SET_FUNCTION_POINTER(HkCogWheelConstraintData_SetInBodySpaceInternal)
    SET_FUNCTION_POINTER(HkCollisionEvent_GetSource)
    SET_FUNCTION_POINTER(HkCollisionEvent_GetRigidBody)
    SET_FUNCTION_POINTER(HkCollisionEvent_GetBodyA)
    SET_FUNCTION_POINTER(HkCollisionEvent_GetBodyB)
    SET_FUNCTION_POINTER(HkCollisionEvent_SetImpulse)
    SET_FUNCTION_POINTER(HkCollisionEvent_SetImpulseScaling)
    SET_FUNCTION_POINTER(HkCollisionEvent_GetContactPointCount)
    SET_FUNCTION_POINTER(HkCollisionEvent_Disable)
    SET_FUNCTION_POINTER(HkCollisionEvent_GetContactPointPropertiesAt)
    SET_FUNCTION_POINTER(HkCollisionEvent_GetOffsets)
    SET_FUNCTION_POINTER(HkConstraint_Create)
    SET_FUNCTION_POINTER(HkConstraint_AddConstraintListener)
    SET_FUNCTION_POINTER(HkConstraint_RemoveConstraintListener)
    SET_FUNCTION_POINTER(HkConstraint_ReplaceEntity)
    SET_FUNCTION_POINTER(HkConstraint_SetVirtualMassInverse)
    SET_FUNCTION_POINTER(HkConstraint_GetPriority)
    SET_FUNCTION_POINTER(HkConstraint_SetPriority)
    SET_FUNCTION_POINTER(HkConstraint_GetWantRuntime)
    SET_FUNCTION_POINTER(HkConstraint_SetWantRuntime)
    SET_FUNCTION_POINTER(HkConstraint_IsInWorld)
    SET_FUNCTION_POINTER(HkConstraint_GetRigidBodyA)
    SET_FUNCTION_POINTER(HkConstraint_GetRigidBodyB)
    SET_FUNCTION_POINTER(HkConstraint_GetEnabled)
    SET_FUNCTION_POINTER(HkConstraint_SetEnabled)
    SET_FUNCTION_POINTER(HkConstraint_GetPivotsInWorld)
    SET_FUNCTION_POINTER(HkConstraint_GetUserData)
    SET_FUNCTION_POINTER(HkConstraint_SetUserData)
    SET_FUNCTION_POINTER(HkConstraint_AddCenterOfMassModifierAtom)
    SET_FUNCTION_POINTER(HkConstraint_FindConnectedConstraints)
    SET_FUNCTION_POINTER(HkConstraintData_GetMaximumLinearImpulse)
    SET_FUNCTION_POINTER(HkConstraintData_SetMaximumLinearImpulse)
    SET_FUNCTION_POINTER(HkConstraintData_GetMaximumAngularImpulse)
    SET_FUNCTION_POINTER(HkConstraintData_SetMaximumAngularImpulse)
    SET_FUNCTION_POINTER(HkConstraintData_GetBreachImpulse)
    SET_FUNCTION_POINTER(HkConstraintData_SetBreachImpulse)
    SET_FUNCTION_POINTER(HkConstraintData_GetInertiaStabilizationFactor)
    SET_FUNCTION_POINTER(HkConstraintData_SetInertiaStabilizationFactor)
    SET_FUNCTION_POINTER(HkConstraintData_SetSolvingMethod)
    SET_FUNCTION_POINTER(HkConstraintListener_Create)
    SET_FUNCTION_POINTER(HkConstraintListener_Release)
    SET_FUNCTION_POINTER(HkConstraintListener_SetCallbacks)
    SET_FUNCTION_POINTER(HkConstraintProjectorListener_Create)
    SET_FUNCTION_POINTER(HkConstraintProjectorListener_Release)
    SET_FUNCTION_POINTER(HkConstraintStabilizationUtil_StabilizeRagdollInertias)
    SET_FUNCTION_POINTER(HkContactListener_Create)
    SET_FUNCTION_POINTER(HkContactListener_SetCallbackLimit)
    SET_FUNCTION_POINTER(HkContactListener_ResetLimit)
    SET_FUNCTION_POINTER(HkContactPoint_GetPosition)
    SET_FUNCTION_POINTER(HkContactPoint_SetPosition)
    SET_FUNCTION_POINTER(HkContactPoint_GetNormalAndDistance)
    SET_FUNCTION_POINTER(HkContactPoint_SetNormalAndDistance)
    SET_FUNCTION_POINTER(HkContactPoint_GetNormal)
    SET_FUNCTION_POINTER(HkContactPoint_SetNormal)
    SET_FUNCTION_POINTER(HkContactPoint_GetDistance)
    SET_FUNCTION_POINTER(HkContactPoint_SetDistance)
    SET_FUNCTION_POINTER(HkContactPoint_Flip)
    SET_FUNCTION_POINTER(HkContactPointEvent_GetBase)
    SET_FUNCTION_POINTER(HkContactPointEvent_IsToi)
    SET_FUNCTION_POINTER(HkContactPointEvent_GetSeparatingVelocity)
    SET_FUNCTION_POINTER(HkContactPointEvent_SetSeparatingVelocity)
    SET_FUNCTION_POINTER(HkContactPointEvent_GetRotateNormal)
    SET_FUNCTION_POINTER(HkContactPointEvent_SetRotateNormal)
    SET_FUNCTION_POINTER(HkContactPointEvent_GetEventType)
    SET_FUNCTION_POINTER(HkContactPointEvent_GetContactPoint)
    SET_FUNCTION_POINTER(HkContactPointEvent_GetContactProperties)
    SET_FUNCTION_POINTER(HkContactPointEvent_GetFiringCallbacksForFullManifold)
    SET_FUNCTION_POINTER(HkContactPointEvent_GetFirstCallbackForFullManifold)
    SET_FUNCTION_POINTER(HkContactPointEvent_GetLastCallbackForFullManifold)
    SET_FUNCTION_POINTER(HkContactPointEvent_GetContactPointId)
    SET_FUNCTION_POINTER(HkContactPointEvent_AccessVelocities)
    SET_FUNCTION_POINTER(HkContactPointEvent_UpdateVelocities)
    SET_FUNCTION_POINTER(HkContactPointEvent_GetShapeKey)
    SET_FUNCTION_POINTER(HkContactPointEvent_GetShapeKeyWithShapeID)
    SET_FUNCTION_POINTER(HkContactPointEvent_GetFieldOffsets)
    SET_FUNCTION_POINTER(HkContactPointProperties_GetImpulseApplied)
    SET_FUNCTION_POINTER(HkContactPointProperties_GetInternalSolverData)
    SET_FUNCTION_POINTER(HkContactPointProperties_WasUsed)
    SET_FUNCTION_POINTER(HkContactPointProperties_GetFriction)
    SET_FUNCTION_POINTER(HkContactPointProperties_SetFriction)
    SET_FUNCTION_POINTER(HkContactPointProperties_GetRestitution)
    SET_FUNCTION_POINTER(HkContactPointProperties_SetRestitution)
    SET_FUNCTION_POINTER(HkContactPointProperties_IsPotential)
    SET_FUNCTION_POINTER(HkContactPointProperties_GetMaxImpulsePerStep)
    SET_FUNCTION_POINTER(HkContactPointProperties_SetMaxImpulsePerStep)
    SET_FUNCTION_POINTER(HkContactPointProperties_GetMaxImpulse)
    SET_FUNCTION_POINTER(HkContactPointProperties_SetMaxImpulse)
    SET_FUNCTION_POINTER(HkContactPointProperties_GetIsDisabled)
    SET_FUNCTION_POINTER(HkContactPointProperties_SetIsDisabled)
    SET_FUNCTION_POINTER(HkContactPointProperties_GetIsNew)
    SET_FUNCTION_POINTER(HkContactPointProperties_SetIsNew)
    SET_FUNCTION_POINTER(HkContactPointProperties_GetUserData)
    SET_FUNCTION_POINTER(HkContactPointProperties_SetUserData)
    SET_FUNCTION_POINTER(HkContactPointProperties_GetFieldOffsets)
    SET_FUNCTION_POINTER(HkContactSoundListener_Create)
    SET_FUNCTION_POINTER(HkConvexShape_GetConvexShapeFromCompoundShape)
    SET_FUNCTION_POINTER(HkConvexShape_GetConvexRadius)
    SET_FUNCTION_POINTER(HkConvexShape_SetConvexRadius)
    SET_FUNCTION_POINTER(HkConvexShape_GetDefaultConvexRadius)
    SET_FUNCTION_POINTER(HkConvexTransformShape_Create)
    SET_FUNCTION_POINTER(HkConvexTransformShape_CreateTranslated)
    SET_FUNCTION_POINTER(HkConvexTransformShape_GetChildShape)
    SET_FUNCTION_POINTER(HkConvexTransformShape_GetTransform)
    SET_FUNCTION_POINTER(HkConvexTranslateShape_CreateWithChild)
    SET_FUNCTION_POINTER(HkConvexTranslateShape_GetChildShape)
    SET_FUNCTION_POINTER(HkConvexTranslateShape_GetTranslation)
    SET_FUNCTION_POINTER(HkConvexVerticesShape_Create)
    SET_FUNCTION_POINTER(HkConvexVerticesShape_CreateWithRadius)
    SET_FUNCTION_POINTER(HkConvexVerticesShape_GetCenter)
    SET_FUNCTION_POINTER(HkConvexVerticesShape_GetVertexCount)
    SET_FUNCTION_POINTER(HkConvexVerticesShape_GetFaceCount)
    SET_FUNCTION_POINTER(HkConvexVerticesShape_GetFaces)
    SET_FUNCTION_POINTER(HkConvexVerticesShape_GetVertices)
    SET_FUNCTION_POINTER(HkConvexVerticesShape_GetGeometry)
    SET_FUNCTION_POINTER(HkCustomWheelConstraintData_Create)
    SET_FUNCTION_POINTER(HkCustomWheelConstraintData_GetLimitsEnabled)
    SET_FUNCTION_POINTER(HkCustomWheelConstraintData_SetLimitsEnabled)
    SET_FUNCTION_POINTER(HkCustomWheelConstraintData_GetSuspensionMinLimit)
    SET_FUNCTION_POINTER(HkCustomWheelConstraintData_SetSuspensionMinLimit)
    SET_FUNCTION_POINTER(HkCustomWheelConstraintData_GetSuspensionMaxLimit)
    SET_FUNCTION_POINTER(HkCustomWheelConstraintData_SetSuspensionMaxLimit)
    SET_FUNCTION_POINTER(HkCustomWheelConstraintData_GetFrictionEnabled)
    SET_FUNCTION_POINTER(HkCustomWheelConstraintData_SetFrictionEnabled)
    SET_FUNCTION_POINTER(HkCustomWheelConstraintData_GetMaxFrictionTorque)
    SET_FUNCTION_POINTER(HkCustomWheelConstraintData_SetMaxFrictionTorque)
    SET_FUNCTION_POINTER(HkCustomWheelConstraintData_SetInBodySpaceInternal)
    SET_FUNCTION_POINTER(HkCustomWheelConstraintData_SetSuspensionStrength)
    SET_FUNCTION_POINTER(HkCustomWheelConstraintData_SetSuspensionDamping)
    SET_FUNCTION_POINTER(HkCustomWheelConstraintData_SetSteeringAngle)
    SET_FUNCTION_POINTER(HkCustomWheelConstraintData_SetAngleLimits)
    SET_FUNCTION_POINTER(HkCustomWheelConstraintData_GetAngleLimitsMin)
    SET_FUNCTION_POINTER(HkCustomWheelConstraintData_GetAngleLimitsMax)
    SET_FUNCTION_POINTER(HkCustomWheelConstraintData_DisableLimits)
    SET_FUNCTION_POINTER(HkCustomWheelConstraintData_GetCurrentAngle)
    SET_FUNCTION_POINTER(HkCustomWheelConstraintData_SetCurrentAngle)
    SET_FUNCTION_POINTER(HkCylinderShape_Create)
    SET_FUNCTION_POINTER(HkCylinderShape_CreateWithConvexRadius)
    SET_FUNCTION_POINTER(HkCylinderShape_GetVertexB)
    SET_FUNCTION_POINTER(HkCylinderShape_GetVertexA)
    SET_FUNCTION_POINTER(HkCylinderShape_SetVertexB)
    SET_FUNCTION_POINTER(HkCylinderShape_SetVertexA)
    SET_FUNCTION_POINTER(HkCylinderShape_GetRadius)
    SET_FUNCTION_POINTER(HkCylinderShape_SetRadius)
    SET_FUNCTION_POINTER(HkCylinderShape_SetNumberOfVirtualSideSegments)
    SET_FUNCTION_POINTER(HkEasePenetrationAction_Create)
    SET_FUNCTION_POINTER(HkEasePenetrationAction_GetInitialAdditionalAllowedPenetrationDepth)
    SET_FUNCTION_POINTER(HkEasePenetrationAction_SetInitialAdditionalAllowedPenetrationDepth)
    SET_FUNCTION_POINTER(HkEasePenetrationAction_GetInitialAllowedPenetrationDepthMultiplier)
    SET_FUNCTION_POINTER(HkEasePenetrationAction_SetInitialAllowedPenetrationDepthMultiplier)
    SET_FUNCTION_POINTER(HkEntity_AddActivationListener)
    SET_FUNCTION_POINTER(HkEntity_RemoveActivationListener)
    SET_FUNCTION_POINTER(HKEntity_AddEntityListener)
    SET_FUNCTION_POINTER(HKEntity_RemoveEntityListener)
    SET_FUNCTION_POINTER(HkEntity_SetContactListener)
    SET_FUNCTION_POINTER(HkEntity_GetQuality)
    SET_FUNCTION_POINTER(HkEntity_SetQuality)
    SET_FUNCTION_POINTER(HkEntity_IsFixed)
    SET_FUNCTION_POINTER(HkEntity_IsFixedOrKeyframed)
    SET_FUNCTION_POINTER(HkRigidBody_GetMotionType)
    SET_FUNCTION_POINTER(HkEntity_GetContactPointCallbackDelay)
    SET_FUNCTION_POINTER(HkEntity_SetContactPointCallbackDelay)
    SET_FUNCTION_POINTER(HkEntity_SetProperty)
    SET_FUNCTION_POINTER(HkEntity_HasProperty)
    SET_FUNCTION_POINTER(HkEntity_RemoveProperty)
    SET_FUNCTION_POINTER(HkRigidBody_GetRotation)
    SET_FUNCTION_POINTER(HkRigidBody_SetRotation)
    SET_FUNCTION_POINTER(HkRigidBody_GetPosition)
    SET_FUNCTION_POINTER(HkRigidBody_SetPosition)
    SET_FUNCTION_POINTER(HkRigidBody_Activate)
    SET_FUNCTION_POINTER(HkRigidBody_ActivateAsCriticalOperation)
    SET_FUNCTION_POINTER(HkRigidBody_Deactivate)
    SET_FUNCTION_POINTER(HkRigidBody_UpdateMotionType)
    SET_FUNCTION_POINTER(HkRigidBody_GetIsActive)
    SET_FUNCTION_POINTER(HkRigidBody_RequestDeactivation)
    SET_FUNCTION_POINTER(HkRigidBody_GetLinearVelocity)
    SET_FUNCTION_POINTER(HkRigidBody_SetLinearVelocity)
    SET_FUNCTION_POINTER(HkRigidBody_GetAngularVelocity)
    SET_FUNCTION_POINTER(HkRigidBody_SetAngularVelocity)
    SET_FUNCTION_POINTER(HkEntity_GetFieldOffsets)
    SET_FUNCTION_POINTER(HkEntityListener_Create)
    SET_FUNCTION_POINTER(HkEntityListener_Release)
    SET_FUNCTION_POINTER(HkFixedConstraintData_Create)
    SET_FUNCTION_POINTER(HkFixedConstraintData_SetInBodySpaceInternal)
    SET_FUNCTION_POINTER(HkFixedConstraintData_SetInWorldSpace)
    SET_FUNCTION_POINTER(HkFixedConstraintData_IsValid)
    SET_FUNCTION_POINTER(HkFixedConstraintData_SetInertiaStabilizationFactor)
    SET_FUNCTION_POINTER(HkFixedConstraintData_GetInertiaStabilizationFactor)
    SET_FUNCTION_POINTER(HkFixedConstraintData_GetSolverImpulseInLastStep)
    SET_FUNCTION_POINTER(HkGeometry_Create)
    SET_FUNCTION_POINTER(HkGeometry_CreateWithParams)
    SET_FUNCTION_POINTER(HkGeometry_Destroy)
    SET_FUNCTION_POINTER(HkGeometry_GetTriangleCount)
    SET_FUNCTION_POINTER(HkGeometry_GetVertexCount)
    SET_FUNCTION_POINTER(HkGeometry_Append)
    SET_FUNCTION_POINTER(HkGeometry_GetTriangle)
    SET_FUNCTION_POINTER(HkGeometry_GetVertex)
    SET_FUNCTION_POINTER(HkGeometry_SetGeometry)
    SET_FUNCTION_POINTER(HkGridShape_Create)
    SET_FUNCTION_POINTER(HkGridShape_GetCellSize)
    SET_FUNCTION_POINTER(HkGridShape_GetShapeCount)
    SET_FUNCTION_POINTER(HkGridShape_SetDebugRigidBody)
    SET_FUNCTION_POINTER(HkGridShape_GetDebugRigidBody)
    SET_FUNCTION_POINTER(HkGridShape_SetDebugDraw)
    SET_FUNCTION_POINTER(HkGridShape_GetDebugDraw)
    SET_FUNCTION_POINTER(HkGridShape_AddShapes)
    SET_FUNCTION_POINTER(HkGridShape_Contains)
    SET_FUNCTION_POINTER(HkGridShape_GetShape)
    SET_FUNCTION_POINTER(HkGridShape_GetShapeInfo)
    SET_FUNCTION_POINTER(HkGridShape_GetShapeInfoCount)
    SET_FUNCTION_POINTER(HkGridShape_GetShapeMin)
    SET_FUNCTION_POINTER(HkGridShape_GetShapesInInterval)
    SET_FUNCTION_POINTER(HkGridShape_GetChildBounds)
    SET_FUNCTION_POINTER(HkGridShape_RemoveShapes)
    SET_FUNCTION_POINTER(HkGridShape_GetCellRanges)
    SET_FUNCTION_POINTER(HkGroupFilter_CalcFilterInfo)
    SET_FUNCTION_POINTER(HkGroupFilter_GetLayerFromFilterInfo)
    SET_FUNCTION_POINTER(HkGroupFilter_getSubSystemDontCollideWithFromFilterInfo)
    SET_FUNCTION_POINTER(HkGroupFilter_GetSubSystemIdFromFilterInfo)
    SET_FUNCTION_POINTER(HkGroupFilter_GetSystemGroupFromFilterInfo)
    SET_FUNCTION_POINTER(HkGroupFilter_SetLayer)
    SET_FUNCTION_POINTER(HkGroupFilter_DisableCollisionsBetween)
    SET_FUNCTION_POINTER(HkGroupFilter_DisableCollisionsUsingBitfield)
    SET_FUNCTION_POINTER(HkGroupFilter_EnableCollisionsBetween)
    SET_FUNCTION_POINTER(HkGroupFilter_EnableCollisionsUsingBitfield)
    SET_FUNCTION_POINTER(HkGroupFilter_GetNewSystemGroup)
    SET_FUNCTION_POINTER(HkGlobal_ReleasePtr)
    SET_FUNCTION_POINTER(HkGlobal_ReleaseString)
    SET_FUNCTION_POINTER(HkGlobal_ReleaseArrayPtr)
    SET_FUNCTION_POINTER(HkHingeConstraintData_Create)
    SET_FUNCTION_POINTER(HkHingeConstraintData_SetInBodySpaceInternal)
    SET_FUNCTION_POINTER(HkHingeConstraintData_SetInWorldSpace)
    SET_FUNCTION_POINTER(HkHingeConstraintData_SetInertiaStabilizationFactor)
    SET_FUNCTION_POINTER(HkHingeConstraintData_GetInertiaStabilizationFactor)
    SET_FUNCTION_POINTER(HkInertiaTensorComputer_Create)
    SET_FUNCTION_POINTER(HkInertiaTensorComputer_CombineMassPropertiesInstance)
    SET_FUNCTION_POINTER(HkInertiaTensorComputer_Release)
    SET_FUNCTION_POINTER(HkInertiaTensorComputer_ComputeBoxVolumeMassProperties)
    SET_FUNCTION_POINTER(HkInertiaTensorComputer_ComputeCapsuleVolumeMassProperties)
    SET_FUNCTION_POINTER(HkInertiaTensorComputer_ComputeCylinderVolumeMassProperties)
    SET_FUNCTION_POINTER(HkInertiaTensorComputer_ComputeSphereVolumeMassProperties)
    SET_FUNCTION_POINTER(HkJobQueue_Create)
    SET_FUNCTION_POINTER(HkJobQueue_CreateWithNumThreads)
    SET_FUNCTION_POINTER(HkJobQueue_Release)
    SET_FUNCTION_POINTER(HkJobQueue_GetWaitPolicy)
    SET_FUNCTION_POINTER(HkJobQueue_SetWaitPolicy)
    SET_FUNCTION_POINTER(HkJobQueue_GetMasterThreadFinishingFlags)
    SET_FUNCTION_POINTER(HkJobQueue_SetMasterThreadFinishingFlags)
    SET_FUNCTION_POINTER(HkJobQueue_ProcessAllJobs)
    SET_FUNCTION_POINTER(HkJobThreadPool_Create)
    SET_FUNCTION_POINTER(HkJobThreadPool_CreateWithNumThreads)
    SET_FUNCTION_POINTER(HkJobThreadPool_RemoveReference)
    SET_FUNCTION_POINTER(HkJobThreadPool_RunOnEachWorker)
    SET_FUNCTION_POINTER(HkJobThreadPool_ExecuteJobQueue)
    SET_FUNCTION_POINTER(HkJobThreadPool_GetThisThreadIndex)
    SET_FUNCTION_POINTER(HkJobThreadPool_WaitForCompletion)
    SET_FUNCTION_POINTER(HkJobThreadPool_ClearTimerData)
    SET_FUNCTION_POINTER(HkKeyFrameUtility_ApplyHardKeyFrame)
    SET_FUNCTION_POINTER(HkLimitedForceConstraintMotor_GetMinForce)
    SET_FUNCTION_POINTER(HkLimitedForceConstraintMotor_SetMinForce)
    SET_FUNCTION_POINTER(HkLimitedForceConstraintMotor_GetMaxForce)
    SET_FUNCTION_POINTER(HkLimitedForceConstraintMotor_SetMaxForce)
    SET_FUNCTION_POINTER(HkLimitedHingeConstraintData_Create)
    SET_FUNCTION_POINTER(HkLimitedHingeConstraintData_SetInBodySpaceInternal)
    SET_FUNCTION_POINTER(HkLimitedHingeConstraintData_SetInWorldSpace)
    SET_FUNCTION_POINTER(HkLimitedHingeConstraintData_SetMotor)
    SET_FUNCTION_POINTER(HkLimitedHingeConstraintData_IsMotorEnabled)
    SET_FUNCTION_POINTER(HkLimitedHingeConstraintData_SetMotorEnabled)
    SET_FUNCTION_POINTER(HkLimitedHingeConstraintData_GetTargetAngle)
    SET_FUNCTION_POINTER(HkLimitedHingeConstraintData_SetTargetAngle)
    SET_FUNCTION_POINTER(HkLimitedHingeConstraintData_GetMaxFrictionTorque)
    SET_FUNCTION_POINTER(HkLimitedHingeConstraintData_SetMaxFrictionTorque)
    SET_FUNCTION_POINTER(HkLimitedHingeConstraintData_GetMinAngularLimit)
    SET_FUNCTION_POINTER(HkLimitedHingeConstraintData_SetMinAngularLimit)
    SET_FUNCTION_POINTER(HkLimitedHingeConstraintData_GetMaxAngularLimit)
    SET_FUNCTION_POINTER(HkLimitedHingeConstraintData_SetMaxAngularLimit)
    SET_FUNCTION_POINTER(HkLimitedHingeConstraintData_DisableLimits)
    SET_FUNCTION_POINTER(HkLimitedHingeConstraintData_SetInertiaStabilizationFactor)
    SET_FUNCTION_POINTER(HkLimitedHingeConstraintData_GetInertiaStabilizationFactor)
    SET_FUNCTION_POINTER(HkLimitedHingeConstraintData_GetBodyAPos)
    SET_FUNCTION_POINTER(HkLimitedHingeConstraintData_GetBodyBPos)
    SET_FUNCTION_POINTER(HkLimitedHingeConstraintData_GetIsInitialized)
    SET_FUNCTION_POINTER(HkLimitedHingeConstraintData_SetIsInitialized)
    SET_FUNCTION_POINTER(HkLimitedHingeConstraintData_GetPreviousTargetAngle)
    SET_FUNCTION_POINTER(HkLimitedHingeConstraintData_SetPreviousTargetAngle)
    SET_FUNCTION_POINTER(HkLimitedHingeConstraintData_GetCurrentAngle)
    SET_FUNCTION_POINTER(HkLimitedHingeConstraintData_SetCurrentAngle)
    SET_FUNCTION_POINTER(HkListShape_Create)
    SET_FUNCTION_POINTER(HkListShape_GetDisabledChildrenCount)
    SET_FUNCTION_POINTER(HkListShape_GetTotalChildrenCount)
    SET_FUNCTION_POINTER(HkListShape_EnableShape)
    SET_FUNCTION_POINTER(HkListShape_GetChildByIndex)
    SET_FUNCTION_POINTER(HkListShape_IsChildEnabled)
    SET_FUNCTION_POINTER(HkMalleableConstraintData_Create)
    SET_FUNCTION_POINTER(HkMalleableConstraintData_GetStrength)
    SET_FUNCTION_POINTER(HkMalleableConstraintData_SetStrength)
    SET_FUNCTION_POINTER(HkMassChangerUtil_Create)
    SET_FUNCTION_POINTER(HkMassChangerUtil_IsValid)
    SET_FUNCTION_POINTER(HkMassChangerUtil_Remove)
    SET_FUNCTION_POINTER(HkMemorySnapshot_Diff)
    SET_FUNCTION_POINTER(HkMoppBvTreeShape_Create)
    SET_FUNCTION_POINTER(HkMoppBvTreeShape_GetShapeCollection)
    SET_FUNCTION_POINTER(HkMoppBvTreeShape_DisableKeys)
    SET_FUNCTION_POINTER(HkMoppBvTreeShape_QueryAABB)
    SET_FUNCTION_POINTER(HkMoppBvTreeShape_QueryPoint)
    SET_FUNCTION_POINTER(HkMotion_SetWorldMatrix)
    SET_FUNCTION_POINTER(HkMotion_GetDeactivationClass)
    SET_FUNCTION_POINTER(HkMotion_SetDeactivationClass)
    SET_FUNCTION_POINTER(HkPhantomCallbackShape_Create)
    SET_FUNCTION_POINTER(HkPrismaticConstraintData_Create)
    SET_FUNCTION_POINTER(HkPrismaticConstraintData_SetInWorldSpace)
    SET_FUNCTION_POINTER(HkPrismaticConstraintData_SetInBodySpaceInternal)
    SET_FUNCTION_POINTER(HkPrismaticConstraintData_GetMaximumLinearLimit)
    SET_FUNCTION_POINTER(HkPrismaticConstraintData_SetMaximumLinearLimit)
    SET_FUNCTION_POINTER(HkPrismaticConstraintData_GetMinimumLinearLimit)
    SET_FUNCTION_POINTER(HkPrismaticConstraintData_SetMinimumLinearLimit)
    SET_FUNCTION_POINTER(HkPrismaticConstraintData_GetMaxFrictionForce)
    SET_FUNCTION_POINTER(HkPrismaticConstraintData_SetMaxFrictionForce)
    SET_FUNCTION_POINTER(HkPrismaticConstraintData_GetTargetPosition)
    SET_FUNCTION_POINTER(HkPrismaticConstraintData_SetTargetPosition)
    SET_FUNCTION_POINTER(HkPrismaticConstraintData_SetMotor)
    SET_FUNCTION_POINTER(HkPrismaticConstraintData_IsMotorEnabled)
    SET_FUNCTION_POINTER(HkPrismaticConstraintData_SetMotorEnabled)
    SET_FUNCTION_POINTER(HkPrismaticConstraintData_GetCurrentPosition)
    SET_FUNCTION_POINTER(HkPhysicsSystem_IsActive)
    SET_FUNCTION_POINTER(HkPhysicsSystem_SetActive)
    SET_FUNCTION_POINTER(HkPhysicsSystem_RecreateConstraints)
    SET_FUNCTION_POINTER(HkPhysicsSystem_GetConstraintDataFromSystem)
    SET_FUNCTION_POINTER(HkPhysicsSystem_GetName)
    SET_FUNCTION_POINTER(HkPhysicsSystem_LoadRagdollFromFile)
    SET_FUNCTION_POINTER(HkPhysicsSystem_LoadRagdollFromBuffer)
    SET_FUNCTION_POINTER(HkPhysicsSystem_InitFromData)
    SET_FUNCTION_POINTER(HkpGroupFilter_CalcFilterInfo)
    SET_FUNCTION_POINTER(HkpGroupFilter_CalcFilterInfoFromCurrent)
    SET_FUNCTION_POINTER(HkpInertiaTensorComputer_OptimizeInertiasOfConstraintTree)
    SET_FUNCTION_POINTER(HkPhysicsSystem_Release)
    SET_FUNCTION_POINTER(HkRagdollConstraintData_GetPlaneMinAngularLimit)
    SET_FUNCTION_POINTER(HkRagdollConstraintData_SetPlaneMinAngularLimit)
    SET_FUNCTION_POINTER(HkRagdollConstraintData_GetPlaneMaxAngularLimit)
    SET_FUNCTION_POINTER(HkRagdollConstraintData_SetPlaneMaxAngularLimit)
    SET_FUNCTION_POINTER(HkRagdollConstraintData_GetTwistMinAngularLimit)
    SET_FUNCTION_POINTER(HkRagdollConstraintData_SetTwistMinAngularLimit)
    SET_FUNCTION_POINTER(HkRagdollConstraintData_GetTwistMaxAngularLimit)
    SET_FUNCTION_POINTER(HkRagdollConstraintData_SetTwistMaxAngularLimit)
    SET_FUNCTION_POINTER(HkRagdollConstraintData_GetMaxFrictionTorque)
    SET_FUNCTION_POINTER(HkRagdollConstraintData_SetMaxFrictionTorque)
    SET_FUNCTION_POINTER(HkRagdollConstraintData_SetInBodySpaceInternal)
    SET_FUNCTION_POINTER(HkRagdollConstraintData_SetAsymmetricConeAngle)
    SET_FUNCTION_POINTER(HkRagdollConstraintData_SetConeLimitStabilization)
    SET_FUNCTION_POINTER(HkReferenceObject_AddReference)
    SET_FUNCTION_POINTER(HkReferenceObject_RemoveReference)
    SET_FUNCTION_POINTER(HkReferenceObject_IsValid)
    SET_FUNCTION_POINTER(HkReferenceObject_DebugRemoveRef)
    SET_FUNCTION_POINTER(HkReferenceObject_ReferenceCount)
    SET_FUNCTION_POINTER(HkRigidBody_Create)
    SET_FUNCTION_POINTER(HkRigidBody_CreateWithCustomVelocity)
    SET_FUNCTION_POINTER(HkRigidBody_SetNumShapeKeysInContactPointProperties)
    SET_FUNCTION_POINTER(HkRigidBody_GetResponseModifiers)
    SET_FUNCTION_POINTER(HkRigidBody_SetResponseModifiers)
    SET_FUNCTION_POINTER(HkRigidBody_GetShape)
    SET_FUNCTION_POINTER(HkRigidBody_SetShape)
    SET_FUNCTION_POINTER(HkRigidBody_UpdateShape)
    SET_FUNCTION_POINTER(HkRigidBody_PredictRigidBodyMatrix)
    SET_FUNCTION_POINTER(HkRigidBody_SetMassProperties)
    SET_FUNCTION_POINTER(HkRigidBody_SetWorldMatrix)
    SET_FUNCTION_POINTER(HkRigidBody_SetTransform)
    SET_FUNCTION_POINTER(HkRigidBody_GetEnableDeactivation)
    SET_FUNCTION_POINTER(HkRigidBody_SetEnableDeactivation)
    SET_FUNCTION_POINTER(HkRigidBody_GetMarkedForVelocityRecompute)
    SET_FUNCTION_POINTER(HkRigidBody_SetMarkedForVelocityRecompute)
    SET_FUNCTION_POINTER(HkRigidBody_GetMotion)
    SET_FUNCTION_POINTER(HkRigidBody_GetMass)
    SET_FUNCTION_POINTER(HkRigidBody_SetMass)
    SET_FUNCTION_POINTER(HkRigidBody_GetCenterOfMassLocal)
    SET_FUNCTION_POINTER(HkRigidBody_SetCenterOfMassLocal)
    SET_FUNCTION_POINTER(HkRigidBody_GetInertiaTensor)
    SET_FUNCTION_POINTER(HkRigidBody_SetInertiaTensor)
    SET_FUNCTION_POINTER(HkRigidBody_GetInverseInertiaTensor)
    SET_FUNCTION_POINTER(HkRigidBody_SetInverseInertiaTensor)
    SET_FUNCTION_POINTER(HkRigidBody_GetCenterOfMassWorld)
    SET_FUNCTION_POINTER(HkRigidBody_GetCustomVelocity)
    SET_FUNCTION_POINTER(HkRigidBody_SetCustomVelocity)
    SET_FUNCTION_POINTER(HkRigidBody_GetDeltaAngle)
    SET_FUNCTION_POINTER(HkRigidBody_GetLinearDamping)
    SET_FUNCTION_POINTER(HkRigidBody_SetLinearDamping)
    SET_FUNCTION_POINTER(HkRigidBody_GetAngularDamping)
    SET_FUNCTION_POINTER(HkRigidBody_SetAngularDamping)
    SET_FUNCTION_POINTER(HkRigidBody_GetMaxLinearVelocity)
    SET_FUNCTION_POINTER(HkRigidBody_SetMaxLinearVelocity)
    SET_FUNCTION_POINTER(HkRigidBody_GetMaxAngularVelocity)
    SET_FUNCTION_POINTER(HkRigidBody_SetMaxAngularVelocity)
    SET_FUNCTION_POINTER(HkRigidBody_GetAllowedPenetrationDepth)
    SET_FUNCTION_POINTER(HkRigidBody_SetAllowedPenetrationDepth)
    SET_FUNCTION_POINTER(HkRigidBody_GetFriction)
    SET_FUNCTION_POINTER(HkRigidBody_SetFriction)
    SET_FUNCTION_POINTER(HkRigidBody_GetRestitution)
    SET_FUNCTION_POINTER(HkRigidBody_SetRestitution)
    SET_FUNCTION_POINTER(HkRigidBody_ApplyLinearImpulse)
    SET_FUNCTION_POINTER(HkRigidBody_ApplyPointImpulse)
    SET_FUNCTION_POINTER(HkRigidBody_ApplyAngularImpulse)
    SET_FUNCTION_POINTER(HkRigidBody_SetLayer)
    SET_FUNCTION_POINTER(HkRigidBody_GetCollisionFilterInfo)
    SET_FUNCTION_POINTER(HkRigidBody_SetCollisionFilterInfo)
    SET_FUNCTION_POINTER(HkRigidBody_ApplyForce)
    SET_FUNCTION_POINTER(HkRigidBody_ApplyForceToPoint)
    SET_FUNCTION_POINTER(HkRigidBody_ApplyTorque)
    SET_FUNCTION_POINTER(HkRigidBody_GetNativeObjectName)
    SET_FUNCTION_POINTER(HkRigidBody_RemoveFromWorld)
    SET_FUNCTION_POINTER(HkRigidBody_HasGravity)
    SET_FUNCTION_POINTER(HkRigidBody_HasConstraints)
    SET_FUNCTION_POINTER(HkRigidBody_GetBreakableBody)
    SET_FUNCTION_POINTER(HkRigidBody_GetGravity)
    SET_FUNCTION_POINTER(HkRigidBody_ReleaseGravity)
    SET_FUNCTION_POINTER(HkRigidBody_SetGravity)
    SET_FUNCTION_POINTER(HkRigidBody_Clone)
    SET_FUNCTION_POINTER(HkRigidBody_FromShape)
    SET_FUNCTION_POINTER(HkRigidBody_GetGcRoot)
    SET_FUNCTION_POINTER(HkRigidBody_GetGravityAction)
    SET_FUNCTION_POINTER(HkRigidBody_AddGravityAction)
    SET_FUNCTION_POINTER(HkRigidBody_GetDeactivationCounter0)
    SET_FUNCTION_POINTER(HkRigidBody_GetDeactivationCounter1)
    SET_FUNCTION_POINTER(HkRigidBody_HasActions)
    SET_FUNCTION_POINTER(HkRigidBodyCinfo_Create)
    SET_FUNCTION_POINTER(HkRigidBodyCinfo_Release)
    SET_FUNCTION_POINTER(HkRigidBodyCinfo_GetCollisionResponse)
    SET_FUNCTION_POINTER(HkRigidBodyCinfo_SetCollisionResponse)
    SET_FUNCTION_POINTER(HkRigidBodyCinfo_GetResponseModifiers)
    SET_FUNCTION_POINTER(HkRigidBodyCinfo_SetResponseModifiers)
    SET_FUNCTION_POINTER(HkRigidBodyCinfo_GetPosition)
    SET_FUNCTION_POINTER(HkRigidBodyCinfo_SetPosition)
    SET_FUNCTION_POINTER(HkRigidBodyCinfo_GetRotation)
    SET_FUNCTION_POINTER(HkRigidBodyCinfo_SetRotation)
    SET_FUNCTION_POINTER(HkRigidBodyCinfo_GetLinearVelocity)
    SET_FUNCTION_POINTER(HkRigidBodyCinfo_SetLinearVelocity)
    SET_FUNCTION_POINTER(HkRigidBodyCinfo_GetAngularVelocity)
    SET_FUNCTION_POINTER(HkRigidBodyCinfo_SetAngularVelocity)
    SET_FUNCTION_POINTER(HkRigidBodyCinfo_GetCenterOfMass)
    SET_FUNCTION_POINTER(HkRigidBodyCinfo_SetCenterOfMass)
    SET_FUNCTION_POINTER(HkRigidBodyCinfo_GetMass)
    SET_FUNCTION_POINTER(HkRigidBodyCinfo_SetMass)
    SET_FUNCTION_POINTER(HkRigidBodyCinfo_GetLinearDamping)
    SET_FUNCTION_POINTER(HkRigidBodyCinfo_SetLinearDamping)
    SET_FUNCTION_POINTER(HkRigidBodyCinfo_GetAngularDamping)
    SET_FUNCTION_POINTER(HkRigidBodyCinfo_SetAngularDamping)
    SET_FUNCTION_POINTER(HkRigidBodyCinfo_GetFriction)
    SET_FUNCTION_POINTER(HkRigidBodyCinfo_SetFriction)
    SET_FUNCTION_POINTER(HkRigidBodyCinfo_GetRestitution)
    SET_FUNCTION_POINTER(HkRigidBodyCinfo_SetRestitution)
    SET_FUNCTION_POINTER(HkRigidBodyCinfo_GetMaxLinearVelocity)
    SET_FUNCTION_POINTER(HkRigidBodyCinfo_SetMaxLinearVelocity)
    SET_FUNCTION_POINTER(HkRigidBodyCinfo_GetMaxAngularVelocity)
    SET_FUNCTION_POINTER(HkRigidBodyCinfo_SetMaxAngularVelocity)
    SET_FUNCTION_POINTER(HkRigidBodyCinfo_GetContactPointCallbackDelay)
    SET_FUNCTION_POINTER(HkRigidBodyCinfo_SetContactPointCallbackDelay)
    SET_FUNCTION_POINTER(HkRigidBodyCinfo_GetAllowedPenetrationDepth)
    SET_FUNCTION_POINTER(HkRigidBodyCinfo_SetAllowedPenetrationDepth)
    SET_FUNCTION_POINTER(HkRigidBodyCinfo_GetMotionType)
    SET_FUNCTION_POINTER(HkRigidBodyCinfo_SetMotionType)
    SET_FUNCTION_POINTER(HkRigidBodyCinfo_GetSolverDeactivation)
    SET_FUNCTION_POINTER(HkRigidBodyCinfo_SetSolverDeactivation)
    SET_FUNCTION_POINTER(HkRigidBodyCinfo_GetQualityType)
    SET_FUNCTION_POINTER(HkRigidBodyCinfo_SetQualityType)
    SET_FUNCTION_POINTER(HkRigidBodyCinfo_GetAutoRemoveLevel)
    SET_FUNCTION_POINTER(HkRigidBodyCinfo_SetAutoRemoveLevel)
    SET_FUNCTION_POINTER(HkRigidBodyCinfo_GetShape)
    SET_FUNCTION_POINTER(HkRigidBodyCinfo_SetShape)
    SET_FUNCTION_POINTER(HkRigidBodyCinfo_CalculateBoxInertiaTensor)
    SET_FUNCTION_POINTER(HkRigidBodyCinfo_CalculateSphereInertiaTensor)
    SET_FUNCTION_POINTER(HkRigidBodyCinfo_SetMassProperties)
    SET_FUNCTION_POINTER(HkRigidBodyCinfo_ComputeShapeMass)
    SET_FUNCTION_POINTER(HkRopeConstraintData_Create)
    SET_FUNCTION_POINTER(HkRopeConstraintData_SetInBodySpaceInternal)
    SET_FUNCTION_POINTER(HkRopeConstraintData_Update)
    SET_FUNCTION_POINTER(HkRopeConstraintData_GetStrength)
    SET_FUNCTION_POINTER(HkRopeConstraintData_SetStrength)
    SET_FUNCTION_POINTER(HkRopeConstraintData_GetLinearLimit)
    SET_FUNCTION_POINTER(HkRopeConstraintData_SetLinearLimit)
    SET_FUNCTION_POINTER(HkRopeConstraintData_IsValid)
    SET_FUNCTION_POINTER(HkShape_GetReferenceCount)
    SET_FUNCTION_POINTER(HkShape_GetShapeType)
    SET_FUNCTION_POINTER(HkShape_IsConvex)
    SET_FUNCTION_POINTER(HkShape_GetConvexRadius)
    SET_FUNCTION_POINTER(HkShape_SetConvexRadius)
    SET_FUNCTION_POINTER(HkShape_GetUserData)
    SET_FUNCTION_POINTER(HkShape_SetUserData)
    SET_FUNCTION_POINTER(HkShape_SetRigidBody)
    SET_FUNCTION_POINTER(HkShape_IsContainer)
    SET_FUNCTION_POINTER(HkShape_AddReference)
    SET_FUNCTION_POINTER(HkShape_RemoveReference)
    SET_FUNCTION_POINTER(HkShape_DisableRefCount)
    SET_FUNCTION_POINTER(HkShape_GetLocalAABB)
    SET_FUNCTION_POINTER(HkShape_CastRayCollectSingleHit)
    SET_FUNCTION_POINTER(HkShape_LoadShapeFromFile)
    SET_FUNCTION_POINTER(HkShape_GetContainer)
    SET_FUNCTION_POINTER(HkShapeBatch_GetCount)
    SET_FUNCTION_POINTER(HkShapeBatch_GetInfo)
    SET_FUNCTION_POINTER(HkShapeBatch_SetResult)
    SET_FUNCTION_POINTER(HkShapeBuffer_Create)
    SET_FUNCTION_POINTER(HkShapeBuffer_Destroy)
    SET_FUNCTION_POINTER(HkShapeCollection_GetShapeCount)
    SET_FUNCTION_POINTER(HkShapeCollection_GetShape)
    SET_FUNCTION_POINTER(HkShapeCollection_GetShapeWithBuffer)
    SET_FUNCTION_POINTER(HkShapeContainer_GetFirstKey)
    SET_FUNCTION_POINTER(HkShapeContainer_GetNextKey)
    SET_FUNCTION_POINTER(HkShapeContainer_CurrentValue)
    SET_FUNCTION_POINTER(HkShapeContainer_GetShape)
    SET_FUNCTION_POINTER(HkShapeContainer_IsShapeKeyValid)
    SET_FUNCTION_POINTER(HkShapeCutterUtil_Cut)
    SET_FUNCTION_POINTER(HkShapeLoader_LoadShapesListFromBuffer)
    SET_FUNCTION_POINTER(HkShapeLoader_LoadShapesListFromFile)
    SET_FUNCTION_POINTER(HkShapeLoader_SaveShapesListToFile)
    SET_FUNCTION_POINTER(HkShapeLoader_CleanupShapesBuffer)
    SET_FUNCTION_POINTER(HkSimpleMeshShape_Create)
    SET_FUNCTION_POINTER(HkSimpleShapePhantom_Create)
    SET_FUNCTION_POINTER(HkSimpleShapePhantom_CreateWithLayer)
    SET_FUNCTION_POINTER(HkSimpleShapePhantom_GetShape)
    SET_FUNCTION_POINTER(HkSimpleValueProperty_CreateFloat)
    SET_FUNCTION_POINTER(HkSimpleValueProperty_CreateUInt)
    SET_FUNCTION_POINTER(HkSimpleValueProperty_CreateInt)
    SET_FUNCTION_POINTER(HkSimpleValueProperty_GetValueFloat)
    SET_FUNCTION_POINTER(HkSimpleValueProperty_SetValueFloat)
    SET_FUNCTION_POINTER(HkSimpleValueProperty_GetValueUInt)
    SET_FUNCTION_POINTER(HkSimpleValueProperty_SetValueUInt)
    SET_FUNCTION_POINTER(HkSimpleValueProperty_GetValueInt)
    SET_FUNCTION_POINTER(HkSimpleValueProperty_SetValueInt)
    SET_FUNCTION_POINTER(HkSimulationIsland_GetEntityCount)
    SET_FUNCTION_POINTER(HkSimulationIsland_GetEntity)
    SET_FUNCTION_POINTER(HkSimulationIsland_GetBounds)
    SET_FUNCTION_POINTER(HkSimulationIsland_GetOffsets)
    SET_FUNCTION_POINTER(HkSmartListShape_Create)
    SET_FUNCTION_POINTER(HkSmartListShape_GetShapeCount)
    SET_FUNCTION_POINTER(HkSmartListShape_AddShape)
    SET_FUNCTION_POINTER(HkSmartListShape_RemoveShape)
    SET_FUNCTION_POINTER(HkSmartListShape_Validate)
    SET_FUNCTION_POINTER(HkSphereShape_Create)
    SET_FUNCTION_POINTER(HkSphereShape_GetRadius)
    SET_FUNCTION_POINTER(HkSphereShape_SetRadius)
    SET_FUNCTION_POINTER(HkStaticCompoundShape_Create)
    SET_FUNCTION_POINTER(HkStaticCompoundShape_GetInstanceCount)
    SET_FUNCTION_POINTER(HkStaticCompoundShape_AddInstance)
    SET_FUNCTION_POINTER(HkStaticCompoundShape_Bake)
    SET_FUNCTION_POINTER(HkStaticCompoundShape_ComposeShapeKey)
    SET_FUNCTION_POINTER(HkStaticCompoundShape_DecomposeShapeKey)
    SET_FUNCTION_POINTER(HkStaticCompoundShape_EnableAllShapeKeys)
    SET_FUNCTION_POINTER(HkStaticCompoundShape_EnableInstance)
    SET_FUNCTION_POINTER(HkStaticCompoundShape_EnableShapeKey)
    SET_FUNCTION_POINTER(HkStaticCompoundShape_GetFirstKey)
    SET_FUNCTION_POINTER(HkStaticCompoundShape_GetInstance)
    SET_FUNCTION_POINTER(HkStaticCompoundShape_GetInstanceTransform)
    SET_FUNCTION_POINTER(HkStaticCompoundShape_IsInstanceEnabled)
    SET_FUNCTION_POINTER(HkStaticCompoundShape_IsShapeKeyEnabled)
    SET_FUNCTION_POINTER(HkTaskProfiler_Init)
    SET_FUNCTION_POINTER(HkTaskProfiler_ReleaseResources)
    SET_FUNCTION_POINTER(HkTaskProfiler_HookJobQueue)
    SET_FUNCTION_POINTER(HkTaskProfiler_ReplayTimers)
    SET_FUNCTION_POINTER(HkTaskProfiler_Begin1)
    SET_FUNCTION_POINTER(HkTaskProfiler_Begin2)
    SET_FUNCTION_POINTER(HkTaskProfiler_Begin3)
    SET_FUNCTION_POINTER(HkTaskProfiler_Begin4)
    SET_FUNCTION_POINTER(HkTaskProfiler_Begin5)
    SET_FUNCTION_POINTER(HkTaskProfiler_End)
    SET_FUNCTION_POINTER(HkTransformShape_Create)
    SET_FUNCTION_POINTER(HkTransformShape_CreateWithTranslation)
    SET_FUNCTION_POINTER(HkTransformShape_GetTransform)
    SET_FUNCTION_POINTER(HkTransformShape_GetChildShape)
    SET_FUNCTION_POINTER(HkTriangleShape_GetExtrusion)
    SET_FUNCTION_POINTER(HkTriangleShape_GetPt2)
    SET_FUNCTION_POINTER(HkTriangleShape_GetPt1)
    SET_FUNCTION_POINTER(HkTriangleShape_GetPt0)
    SET_FUNCTION_POINTER(HkUniformGridShape_Create)
    SET_FUNCTION_POINTER(HkUniformGridShape_GetShapeCount)
    SET_FUNCTION_POINTER(HkUniformGridShape_DiscardLargeData)
    SET_FUNCTION_POINTER(HkUniformGridShape_GetHitsAndClear)
    SET_FUNCTION_POINTER(HkUniformGridShape_GetHitCellsInRange)
    SET_FUNCTION_POINTER(HkUniformGridShape_GetMissingCellsInRange)
    SET_FUNCTION_POINTER(HkUniformGridShape_InvalidateRange)
    SET_FUNCTION_POINTER(HkUniformGridShape_InvalidateRangeImmediate)
    SET_FUNCTION_POINTER(HkUniformGridShape_RemoveChild)
    SET_FUNCTION_POINTER(HkUniformGridShape_SetChild)
    SET_FUNCTION_POINTER(HkUniformGridShape_GetChild)
    SET_FUNCTION_POINTER(HkUniformGridShape_SetDeleteHandler)
    SET_FUNCTION_POINTER(HkUniformGridShape_RemoveShapeRequestHandler)
    SET_FUNCTION_POINTER(HkUniformGridShape_SetShapeRequestHandler)
    SET_FUNCTION_POINTER(HkUniformGridShape_EnableExtendedCache)
    SET_FUNCTION_POINTER(HkUtils_CalculateSeparatingVelocity)
    SET_FUNCTION_POINTER(HkUtils_SetSoftContact)
    SET_FUNCTION_POINTER(HkVDB_SyncTimers)
    SET_FUNCTION_POINTER(HkVDB_StepVDB)
    SET_FUNCTION_POINTER(HkVDB_Start)
    SET_FUNCTION_POINTER(HkVDB_ReleaseResources)
    SET_FUNCTION_POINTER(HkVDB_GetPort)
    SET_FUNCTION_POINTER(HkVDB_SetPort)
    SET_FUNCTION_POINTER(HkVDB_UpdateCamera)
    SET_FUNCTION_POINTER(HkVDB_Capture)
    SET_FUNCTION_POINTER(HkVDB_EndCapture)
    SET_FUNCTION_POINTER(HkVec3IProperty_Create)
    SET_FUNCTION_POINTER(HkVec3IProperty_GetValue)
    SET_FUNCTION_POINTER(HkVec3IProperty_SetValue)
    SET_FUNCTION_POINTER(HkVelocityConstraintMotor_Create)
    SET_FUNCTION_POINTER(HkVelocityConstraintMotor_GetTau)
    SET_FUNCTION_POINTER(HkVelocityConstraintMotor_SetTau)
    SET_FUNCTION_POINTER(HkVelocityConstraintMotor_GetVelocityTarget)
    SET_FUNCTION_POINTER(HkVelocityConstraintMotor_SetVelocityTarget)
    SET_FUNCTION_POINTER(HkVelocityConstraintMotor_GetConstantRecoveryVelocity)
    SET_FUNCTION_POINTER(HkVelocityConstraintMotor_SetConstantRecoveryVelocity)
    SET_FUNCTION_POINTER(HkWheelConstraintData_Create)
    SET_FUNCTION_POINTER(HkWheelConstraintData_SetInWorldSpace)
    SET_FUNCTION_POINTER(HkWheelConstraintData_SetInBodySpaceInternal)
    SET_FUNCTION_POINTER(HkWheelConstraintData_SetSuspensionMinLimit)
    SET_FUNCTION_POINTER(HkWheelConstraintData_SetSuspensionMaxLimit)
    SET_FUNCTION_POINTER(HkWheelConstraintData_SetSuspensionStrength)
    SET_FUNCTION_POINTER(HkWheelConstraintData_SetSuspensionDamping)
    SET_FUNCTION_POINTER(HkWheelConstraintData_SetSteeringAngle)
    SET_FUNCTION_POINTER(HkWheelResponseModifierUtil_Create)
    SET_FUNCTION_POINTER(HkWheelResponseModifierUtil_Release)
    SET_FUNCTION_POINTER(HkWorld_Create)
    SET_FUNCTION_POINTER(HkWorld_CreateCInfo)
    SET_FUNCTION_POINTER(HkWorld_CreateBodyPairCollection)
    SET_FUNCTION_POINTER(HkWorld_RegisterWithJobQueue)
    SET_FUNCTION_POINTER(HkWorld_Lock)
    SET_FUNCTION_POINTER(HkWorld_Unlock)
    SET_FUNCTION_POINTER(HkWorld_LockCriticalOperations)
    SET_FUNCTION_POINTER(HkWorld_UnlockCriticalOperations)
    SET_FUNCTION_POINTER(HkWorld_ExecutePendingCriticalOperations)
    SET_FUNCTION_POINTER(HkWorld_StepDeltaTime)
    SET_FUNCTION_POINTER(HkWorld_StepMultiThreaded)
    SET_FUNCTION_POINTER(HkWorld_InitMtStep)
    SET_FUNCTION_POINTER(HkWorld_FinishMtStep)
    SET_FUNCTION_POINTER(HkWorld_ExecuteViolatedConstraintProjections)
    SET_FUNCTION_POINTER(HkWorld_ReportRuntimeDataConstraints)
    SET_FUNCTION_POINTER(HkWorld_AddConstraint)
    SET_FUNCTION_POINTER(HkWorld_RemoveConstraint)
    SET_FUNCTION_POINTER(HkWorld_AddEntity)
    SET_FUNCTION_POINTER(HkWorld_RemoveEntity)
    SET_FUNCTION_POINTER(HkWorld_AddPhantom)
    SET_FUNCTION_POINTER(HkWorld_RemovePhantom)
    SET_FUNCTION_POINTER(HkWorld_AddPhysicsSystem)
    SET_FUNCTION_POINTER(HkWorld_RemovePhysicsSystem)
    SET_FUNCTION_POINTER(HkWorld_GetPenetrationsShape)
    SET_FUNCTION_POINTER(HkWorld_GetPenetrationsBox)
    SET_FUNCTION_POINTER(HkWorld_GetPenetrationsShapeShape)
    SET_FUNCTION_POINTER(HkWorld_IsPenetratingShapeShape)
    SET_FUNCTION_POINTER(HkWorld_IsPenetratingShapeShapeTransform)
    SET_FUNCTION_POINTER(HkWorld_CastShape)
    SET_FUNCTION_POINTER(HkWorld_CastShapeReturnPoint)
    SET_FUNCTION_POINTER(HkWorld_CastShapeReturnContact)
    SET_FUNCTION_POINTER(HkWorld_CastShapeReturnContactData)
    SET_FUNCTION_POINTER(HkWorld_CastShapeReturnContactBodyData)
    SET_FUNCTION_POINTER(HkWorld_CastShapeReturnContactBodyDatas)
    SET_FUNCTION_POINTER(HkWorld_CastRayAll)
    SET_FUNCTION_POINTER(HkWorld_CastRayCollisionFilter)
    SET_FUNCTION_POINTER(HkWorld_CastRayFilterLayer)
    SET_FUNCTION_POINTER(HkWorld_MarkForWrite)
    SET_FUNCTION_POINTER(HkWorld_UnmarkForWrite)
    SET_FUNCTION_POINTER(HkWorld_RefreshCollisionFilterOnEntity)
    SET_FUNCTION_POINTER(HkWorld_RefreshCollisionFilterOnWorld)
    SET_FUNCTION_POINTER(HkWorld_ReintegrateEntity)
    SET_FUNCTION_POINTER(HkWorld_AddAction)
    SET_FUNCTION_POINTER(HkWorld_RemoveAction)
    SET_FUNCTION_POINTER(HkWorld_EnsureBatchSizes)
    SET_FUNCTION_POINTER(HkWorld_SetBatchBody)
    SET_FUNCTION_POINTER(HkWorld_AddEntityBatch)
    SET_FUNCTION_POINTER(HkWorld_RemoveEntityBatch)
    SET_FUNCTION_POINTER(HkWorld_GetActiveSimulationIslandsCount)
    SET_FUNCTION_POINTER(HkWorld_GetActiveSimulationIslandEntities)
    SET_FUNCTION_POINTER(HkWorld_DeactivateSimulationIslandRigidBodies)
    SET_FUNCTION_POINTER(HkWorld_IsActiveSimulationIsland)
    SET_FUNCTION_POINTER(HkWorld_GetConstraintCount)
    SET_FUNCTION_POINTER(HkWorld_GetActionCount)
    SET_FUNCTION_POINTER(HkWorld_GetFixedBody)
    SET_FUNCTION_POINTER(HkWorld_ReadSimulationIslandInfos)
    SET_FUNCTION_POINTER(HkWorld_GetGravity)
    SET_FUNCTION_POINTER(HkWorld_SetGravity)
    SET_FUNCTION_POINTER(HkWorld_GetDeactivationRotationSqrdA)
    SET_FUNCTION_POINTER(HkWorld_SetDeactivationRotationSqrdA)
    SET_FUNCTION_POINTER(HkWorld_GetDeactivationRotationSqrdB)
    SET_FUNCTION_POINTER(HkWorld_SetDeactivationRotationSqrdB)
    SET_FUNCTION_POINTER(HkWorld_AddWorldExtension)
    SET_FUNCTION_POINTER(HkWorld_Release)
    SET_FUNCTION_POINTER(HkPhysicsContext_Create)
    SET_FUNCTION_POINTER(HkPhysicsContext_RegisterAllPhysicsProcesses)
    SET_FUNCTION_POINTER(HkPhysicsContext_AddWorld)
    SET_FUNCTION_POINTER(HkPhysicsContext_RemoveWorld)
    SET_FUNCTION_POINTER(HkPhysicsContext_GetNumWorlds)
    SET_FUNCTION_POINTER(HkPhysicsContext_SyncTimers)
    SET_FUNCTION_POINTER(HkPhysicsContext_Release)
    SET_FUNCTION_POINTER(HkGroupFilter_Create)
    SET_FUNCTION_POINTER(HkGroupFilter_IsCollisionEnabled)
    SET_FUNCTION_POINTER(HkpAabbPhantom_Create)
    SET_FUNCTION_POINTER(HkpAabbPhantom_GetAabb)
    SET_FUNCTION_POINTER(HkpAabbPhantom_SetAabb)
    SET_FUNCTION_POINTER(HkpAabbPhantom_Release)
    SET_FUNCTION_POINTER(HkpCollidableAddedEvent_GetRigidBody)
    SET_FUNCTION_POINTER(HkpCollidableRemovedEvent_GetRigidBody)
    SET_FUNCTION_POINTER(HkSimpleShapePhantom_SetTransform)
    SET_FUNCTION_POINTER(HkIntermediateBuffer_ReleaseUnmanaged)
    register_function("Havok.dll", "HkJobThreadPool_RemoveReference", get_export("?HkJobThreadPool_RemoveReference@Havok@@YAXPEAVhkThreadPool@@@Z"));
}

extern "C" {

void Init(const char* dllPath)
{
    InitImpl(dllPath);
}

void* HkActivationListener_Create(void* onActivate, void* onDeactivate) { EnsureThreadInfo();
    LOG_CALL(HkActivationListener_Create);
    REQUIRE_FUNCTION_POINTER(HkActivationListener_Create)
    auto result = pHkActivationListener_Create(bridge_void_ptr(onActivate), bridge_void_ptr(onDeactivate));
    register_callback_owner(result, {callback_owner_binding{&release_void_ptr, onActivate}, callback_owner_binding{&release_void_ptr, onDeactivate}});
    return result;
}

void* HkBallAndSocketConstraintData_Create(void) { EnsureThreadInfo();
    LOG_CALL(HkBallAndSocketConstraintData_Create);
    REQUIRE_FUNCTION_POINTER(HkBallAndSocketConstraintData_Create)
    return pHkBallAndSocketConstraintData_Create();
}

void HkBallAndSocketConstraintData_SetInBodySpaceInternal(void* instance, Vector3 pivotA, Vector3 pivotB) { EnsureThreadInfo();
    LOG_CALL(HkBallAndSocketConstraintData_SetInBodySpaceInternal);
    REQUIRE_FUNCTION_POINTER(HkBallAndSocketConstraintData_SetInBodySpaceInternal)
    pHkBallAndSocketConstraintData_SetInBodySpaceInternal(instance, pivotA, pivotB);
}

void HkBaseSystem_Init(int32_t solverMemorySize, void* log, bool deepProfiling) { EnsureThreadInfo();
    LOG_CALL(HkBaseSystem_Init);
    REQUIRE_FUNCTION_POINTER(HkBaseSystem_Init)
    pHkBaseSystem_Init(solverMemorySize, bridge_void_charptr(log), deepProfiling);
}

void HkBaseSystem_Quit(void) { EnsureThreadInfo();
    LOG_CALL(HkBaseSystem_Quit);
    REQUIRE_FUNCTION_POINTER(HkBaseSystem_Quit)
    pHkBaseSystem_Quit();
}

void* HkBaseSystem_InitThread(void) { EnsureThreadInfo();
    LOG_CALL(HkBaseSystem_InitThread);
    REQUIRE_FUNCTION_POINTER(HkBaseSystem_InitThread)
    return pHkBaseSystem_InitThread();
}

void HkBaseSystem_QuitThread(void* threadRouter) { EnsureThreadInfo();
    LOG_CALL(HkBaseSystem_QuitThread);
    REQUIRE_FUNCTION_POINTER(HkBaseSystem_QuitThread)
    pHkBaseSystem_QuitThread(threadRouter);
}

void HkBaseSystem_GetVersionInfo(void* buffer) { EnsureThreadInfo();
    LOG_CALL(HkBaseSystem_GetVersionInfo);
    REQUIRE_FUNCTION_POINTER(HkBaseSystem_GetVersionInfo)
    pHkBaseSystem_GetVersionInfo(buffer);
}

void HkBaseSystem_GetMemoryStatistics(void* buffer) { EnsureThreadInfo();
    LOG_CALL(HkBaseSystem_GetMemoryStatistics);
    REQUIRE_FUNCTION_POINTER(HkBaseSystem_GetMemoryStatistics)
    pHkBaseSystem_GetMemoryStatistics(buffer);
}

void HkBaseSystem_EnableAssert(int32_t assertId, bool enable) { EnsureThreadInfo();
    LOG_CALL(HkBaseSystem_EnableAssert);
    REQUIRE_FUNCTION_POINTER(HkBaseSystem_EnableAssert)
    pHkBaseSystem_EnableAssert(assertId, enable);
}

bool HkBaseSystem_IsEnabled(int32_t assertId) { EnsureThreadInfo();
    LOG_CALL(HkBaseSystem_IsEnabled);
    REQUIRE_FUNCTION_POINTER(HkBaseSystem_IsEnabled)
    return pHkBaseSystem_IsEnabled(assertId);
}

bool HkBaseSystem_IsDestructionEnabled(void) { EnsureThreadInfo();
    LOG_CALL(HkBaseSystem_IsDestructionEnabled);
    REQUIRE_FUNCTION_POINTER(HkBaseSystem_IsDestructionEnabled)
    return pHkBaseSystem_IsDestructionEnabled();
}

void HkBaseSystem_OnSimulationFrameStarted(int64_t frameNumber) { EnsureThreadInfo();
    LOG_CALL(HkBaseSystem_OnSimulationFrameStarted);
    REQUIRE_FUNCTION_POINTER(HkBaseSystem_OnSimulationFrameStarted)
    pHkBaseSystem_OnSimulationFrameStarted(frameNumber);
}

void HkBaseSystem_OnSimulationFrameFinished(void) { EnsureThreadInfo();
    LOG_CALL(HkBaseSystem_OnSimulationFrameFinished);
    REQUIRE_FUNCTION_POINTER(HkBaseSystem_OnSimulationFrameFinished)
    pHkBaseSystem_OnSimulationFrameFinished();
}

int32_t HkBaseSystem_GetKeyCodes(void* keyCodes) { EnsureThreadInfo();
    LOG_CALL(HkBaseSystem_GetKeyCodes);
    REQUIRE_FUNCTION_POINTER(HkBaseSystem_GetKeyCodes)
    return pHkBaseSystem_GetKeyCodes(keyCodes);
}

bool HkBaseSystem_IsOutOfMemory(void) { EnsureThreadInfo();
    LOG_CALL(HkBaseSystem_IsOutOfMemory);
    REQUIRE_FUNCTION_POINTER(HkBaseSystem_IsOutOfMemory)
    return pHkBaseSystem_IsOutOfMemory();
}

int64_t HkBaseSystem_GetCurrentMemoryConsumption(void) { EnsureThreadInfo();
    LOG_CALL(HkBaseSystem_GetCurrentMemoryConsumption);
    REQUIRE_FUNCTION_POINTER(HkBaseSystem_GetCurrentMemoryConsumption)
    return pHkBaseSystem_GetCurrentMemoryConsumption();
}

void* HkBoxShape_Create(Vector3 halfExtents) { EnsureThreadInfo();
    LOG_CALL(HkBoxShape_Create);
    REQUIRE_FUNCTION_POINTER(HkBoxShape_Create)
    return pHkBoxShape_Create(halfExtents);
}

void* HkBoxShape_CreateWithConvexRadius(Vector3 halfExtents, float convexRadius) { EnsureThreadInfo();
    LOG_CALL(HkBoxShape_CreateWithConvexRadius);
    REQUIRE_FUNCTION_POINTER(HkBoxShape_CreateWithConvexRadius)
    return pHkBoxShape_CreateWithConvexRadius(halfExtents, convexRadius);
}

void* HkBoxShape_GetShapeFromCompoundShape(void* shape, int32_t shapeIndex) { EnsureThreadInfo();
    LOG_CALL(HkBoxShape_GetShapeFromCompoundShape);
    REQUIRE_FUNCTION_POINTER(HkBoxShape_GetShapeFromCompoundShape)
    return pHkBoxShape_GetShapeFromCompoundShape(shape, shapeIndex);
}

Vector3 HkBoxShape_GetHalfExtents(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkBoxShape_GetHalfExtents);
    REQUIRE_FUNCTION_POINTER(HkBoxShape_GetHalfExtents)
    return pHkBoxShape_GetHalfExtents(instance);
}

void HkBoxShape_SetHalfExtents(void* instance, Vector3 value) { EnsureThreadInfo();
    LOG_CALL(HkBoxShape_SetHalfExtents);
    REQUIRE_FUNCTION_POINTER(HkBoxShape_SetHalfExtents)
    pHkBoxShape_SetHalfExtents(instance, value);
}

void* HkBreakOffPartsUtil_Create(void* breakLogicHandler, void* breakPartsHandler) { EnsureThreadInfo();
    LOG_CALL(HkBreakOffPartsUtil_Create);
    REQUIRE_FUNCTION_POINTER(HkBreakOffPartsUtil_Create)
    return pHkBreakOffPartsUtil_Create(bridge_int_ptr_ptr_uint_ptr(breakLogicHandler), bridge_bool_ptr_ptr(breakPartsHandler));
}

void HkBreakOffPartsUtil_Release(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkBreakOffPartsUtil_Release);
    REQUIRE_FUNCTION_POINTER(HkBreakOffPartsUtil_Release)
    pHkBreakOffPartsUtil_Release(instance);
}

void HkBreakOffPartsUtil_RemoveKeysFromListShape(void* entity, void* shapeKeys, int32_t count) { EnsureThreadInfo();
    LOG_CALL(HkBreakOffPartsUtil_RemoveKeysFromListShape);
    REQUIRE_FUNCTION_POINTER(HkBreakOffPartsUtil_RemoveKeysFromListShape)
    pHkBreakOffPartsUtil_RemoveKeysFromListShape(entity, shapeKeys, count);
}

void HkBreakOffPartsUtil_MarkEntityBreakable(void* instance, void* entity, float maxImpulse) { EnsureThreadInfo();
    LOG_CALL(HkBreakOffPartsUtil_MarkEntityBreakable);
    REQUIRE_FUNCTION_POINTER(HkBreakOffPartsUtil_MarkEntityBreakable)
    pHkBreakOffPartsUtil_MarkEntityBreakable(instance, entity, maxImpulse);
}

void HkBreakOffPartsUtil_MarkPieceBreakable(void* instance, void* entity, uint32_t shapeKey, float maxImpulse) { EnsureThreadInfo();
    LOG_CALL(HkBreakOffPartsUtil_MarkPieceBreakable);
    REQUIRE_FUNCTION_POINTER(HkBreakOffPartsUtil_MarkPieceBreakable)
    pHkBreakOffPartsUtil_MarkPieceBreakable(instance, entity, shapeKey, maxImpulse);
}

void HkBreakOffPartsUtil_SetMaxConstraintImpulse(void* instance, void* entity, float maxConstraintImpulse) { EnsureThreadInfo();
    LOG_CALL(HkBreakOffPartsUtil_SetMaxConstraintImpulse);
    REQUIRE_FUNCTION_POINTER(HkBreakOffPartsUtil_SetMaxConstraintImpulse)
    pHkBreakOffPartsUtil_SetMaxConstraintImpulse(instance, entity, maxConstraintImpulse);
}

void HkBreakOffPartsUtil_UnmarkEntityBreakable(void* instance, void* entity) { EnsureThreadInfo();
    LOG_CALL(HkBreakOffPartsUtil_UnmarkEntityBreakable);
    REQUIRE_FUNCTION_POINTER(HkBreakOffPartsUtil_UnmarkEntityBreakable)
    pHkBreakOffPartsUtil_UnmarkEntityBreakable(instance, entity);
}

void HkBreakOffPartsUtil_UnmarkPieceBreakable(void* instance, void* entity, uint32_t shapeKey) { EnsureThreadInfo();
    LOG_CALL(HkBreakOffPartsUtil_UnmarkPieceBreakable);
    REQUIRE_FUNCTION_POINTER(HkBreakOffPartsUtil_UnmarkPieceBreakable)
    pHkBreakOffPartsUtil_UnmarkPieceBreakable(instance, entity, shapeKey);
}

int32_t HkBreakOffPoints_Count(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkBreakOffPoints_Count);
    REQUIRE_FUNCTION_POINTER(HkBreakOffPoints_Count)
    return pHkBreakOffPoints_Count(instance);
}

void HkBreakOffPoints_Get(void* instance, int32_t index, void* outPointInfo) { EnsureThreadInfo();
    LOG_CALL(HkBreakOffPoints_Get);
    REQUIRE_FUNCTION_POINTER(HkBreakOffPoints_Get)
    pHkBreakOffPoints_Get(instance, index, outPointInfo);
}

void* HkBreakableConstraintData_Create(void* data) { EnsureThreadInfo();
    LOG_CALL(HkBreakableConstraintData_Create);
    REQUIRE_FUNCTION_POINTER(HkBreakableConstraintData_Create)
    return pHkBreakableConstraintData_Create(data);
}

float HkBreakableConstraintData_GetThreshold(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkBreakableConstraintData_GetThreshold);
    REQUIRE_FUNCTION_POINTER(HkBreakableConstraintData_GetThreshold)
    return pHkBreakableConstraintData_GetThreshold(instance);
}

void HkBreakableConstraintData_SetThreshold(void* instance, float value) { EnsureThreadInfo();
    LOG_CALL(HkBreakableConstraintData_SetThreshold);
    REQUIRE_FUNCTION_POINTER(HkBreakableConstraintData_SetThreshold)
    pHkBreakableConstraintData_SetThreshold(instance, value);
}

bool HkBreakableConstraintData_GetRemoveFromWorldOnBrake(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkBreakableConstraintData_GetRemoveFromWorldOnBrake);
    REQUIRE_FUNCTION_POINTER(HkBreakableConstraintData_GetRemoveFromWorldOnBrake)
    return pHkBreakableConstraintData_GetRemoveFromWorldOnBrake(instance);
}

void HkBreakableConstraintData_SetRemoveFromWorldOnBrake(void* instance, bool value) { EnsureThreadInfo();
    LOG_CALL(HkBreakableConstraintData_SetRemoveFromWorldOnBrake);
    REQUIRE_FUNCTION_POINTER(HkBreakableConstraintData_SetRemoveFromWorldOnBrake)
    pHkBreakableConstraintData_SetRemoveFromWorldOnBrake(instance, value);
}

bool HkBreakableConstraintData_GetReapplyVelocityOnBreak(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkBreakableConstraintData_GetReapplyVelocityOnBreak);
    REQUIRE_FUNCTION_POINTER(HkBreakableConstraintData_GetReapplyVelocityOnBreak)
    return pHkBreakableConstraintData_GetReapplyVelocityOnBreak(instance);
}

void HkBreakableConstraintData_SetReapplyVelocityOnBreak(void* instance, bool value) { EnsureThreadInfo();
    LOG_CALL(HkBreakableConstraintData_SetReapplyVelocityOnBreak);
    REQUIRE_FUNCTION_POINTER(HkBreakableConstraintData_SetReapplyVelocityOnBreak)
    pHkBreakableConstraintData_SetReapplyVelocityOnBreak(instance, value);
}

bool HkBreakableConstraintData_GetIsBroken(void* instance, void* constraint) { EnsureThreadInfo();
    LOG_CALL(HkBreakableConstraintData_GetIsBroken);
    REQUIRE_FUNCTION_POINTER(HkBreakableConstraintData_GetIsBroken)
    return pHkBreakableConstraintData_GetIsBroken(instance, constraint);
}

void* HkBvCompressedMeshShape_CreateWithSimpleMesh(void* simpleMeshShape) { EnsureThreadInfo();
    LOG_CALL(HkBvCompressedMeshShape_CreateWithSimpleMesh);
    REQUIRE_FUNCTION_POINTER(HkBvCompressedMeshShape_CreateWithSimpleMesh)
    return pHkBvCompressedMeshShape_CreateWithSimpleMesh(simpleMeshShape);
}

void* HkBvCompressedMeshShape_CreateWithParams(void* geometry, int32_t sCount, void* shapes, int32_t tCount, void* transforms, int32_t weldingType, int32_t dataMode, bool isWithConvexRadius, float convexRadius) { EnsureThreadInfo();
    LOG_CALL(HkBvCompressedMeshShape_CreateWithParams);
    REQUIRE_FUNCTION_POINTER(HkBvCompressedMeshShape_CreateWithParams)
    return pHkBvCompressedMeshShape_CreateWithParams(geometry, sCount, shapes, tCount, transforms, weldingType, dataMode, isWithConvexRadius, convexRadius);
}

void* HkBvCompressedMeshShape_CreateUnsafe(void* vertices, int32_t verticesCount, void* indices, int32_t indicesCount, void* materials, int32_t materialsCount, int32_t weldingType, float convexRadius) { EnsureThreadInfo();
    LOG_CALL(HkBvCompressedMeshShape_CreateUnsafe);
    REQUIRE_FUNCTION_POINTER(HkBvCompressedMeshShape_CreateUnsafe)
    return pHkBvCompressedMeshShape_CreateUnsafe(vertices, verticesCount, indices, indicesCount, materials, materialsCount, weldingType, convexRadius);
}

void HkBvCompressedMeshShape_GetGeometry(void* instance, void* geometry) { EnsureThreadInfo();
    LOG_CALL(HkBvCompressedMeshShape_GetGeometry);
    REQUIRE_FUNCTION_POINTER(HkBvCompressedMeshShape_GetGeometry)
    pHkBvCompressedMeshShape_GetGeometry(instance, geometry);
}

uint32_t HkBvCompressedMeshShape_GetUserData(void* instance, uint32_t shapeKey) { EnsureThreadInfo();
    LOG_CALL(HkBvCompressedMeshShape_GetUserData);
    REQUIRE_FUNCTION_POINTER(HkBvCompressedMeshShape_GetUserData)
    return pHkBvCompressedMeshShape_GetUserData(instance, shapeKey);
}

void* HkBvShape_Create(void* boundingVolumeShape, void* childShape) { EnsureThreadInfo();
    LOG_CALL(HkBvShape_Create);
    REQUIRE_FUNCTION_POINTER(HkBvShape_Create)
    return pHkBvShape_Create(boundingVolumeShape, childShape);
}

void* HkBvShape_GetChildShape(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkBvShape_GetChildShape);
    REQUIRE_FUNCTION_POINTER(HkBvShape_GetChildShape)
    return pHkBvShape_GetChildShape(instance);
}

void* HkBvShape_GetBoundingVolumeShape(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkBvShape_GetBoundingVolumeShape);
    REQUIRE_FUNCTION_POINTER(HkBvShape_GetBoundingVolumeShape)
    return pHkBvShape_GetBoundingVolumeShape(instance);
}

void* HkCapsuleShape_Create(Vector3 vertexA, Vector3 vertexB, float radius) { EnsureThreadInfo();
    LOG_CALL(HkCapsuleShape_Create);
    REQUIRE_FUNCTION_POINTER(HkCapsuleShape_Create)
    return pHkCapsuleShape_Create(vertexA, vertexB, radius);
}

float HkCapsuleShape_GetRadius(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkCapsuleShape_GetRadius);
    REQUIRE_FUNCTION_POINTER(HkCapsuleShape_GetRadius)
    return pHkCapsuleShape_GetRadius(instance);
}

Vector3 HkCapsuleShape_GetVertexB(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkCapsuleShape_GetVertexB);
    REQUIRE_FUNCTION_POINTER(HkCapsuleShape_GetVertexB)
    return pHkCapsuleShape_GetVertexB(instance);
}

Vector3 HkCapsuleShape_GetVertexA(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkCapsuleShape_GetVertexA);
    REQUIRE_FUNCTION_POINTER(HkCapsuleShape_GetVertexA)
    return pHkCapsuleShape_GetVertexA(instance);
}

Vector3 HkCapsuleShape_GetCentre(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkCapsuleShape_GetCentre);
    REQUIRE_FUNCTION_POINTER(HkCapsuleShape_GetCentre)
    return pHkCapsuleShape_GetCentre(instance);
}

void* HkCharacterProxy_Create(void* info) { EnsureThreadInfo();
    LOG_CALL(HkCharacterProxy_Create);
    REQUIRE_FUNCTION_POINTER(HkCharacterProxy_Create)
    return pHkCharacterProxy_Create(info);
}

Vector3 HkCharacterProxy_GetPosition(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkCharacterProxy_GetPosition);
    REQUIRE_FUNCTION_POINTER(HkCharacterProxy_GetPosition)
    return pHkCharacterProxy_GetPosition(instance);
}

void HkCharacterProxy_SetPosition(void* instance, Vector3 value) { EnsureThreadInfo();
    LOG_CALL(HkCharacterProxy_SetPosition);
    REQUIRE_FUNCTION_POINTER(HkCharacterProxy_SetPosition)
    pHkCharacterProxy_SetPosition(instance, value);
}

int32_t HkCharacterProxy_GetState(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkCharacterProxy_GetState);
    REQUIRE_FUNCTION_POINTER(HkCharacterProxy_GetState)
    return pHkCharacterProxy_GetState(instance);
}

void HkCharacterProxy_SetState(void* instance, int32_t value) { EnsureThreadInfo();
    LOG_CALL(HkCharacterProxy_SetState);
    REQUIRE_FUNCTION_POINTER(HkCharacterProxy_SetState)
    pHkCharacterProxy_SetState(instance, value);
}

void HkCharacterProxy_StepSimulation(void* instance, float timeInSec, float posX, float posY, bool jump, bool wantJump, bool atLadder, Vector3 gravity, Vector3 up, Vector3 forward) { EnsureThreadInfo();
    LOG_CALL(HkCharacterProxy_StepSimulation);
    REQUIRE_FUNCTION_POINTER(HkCharacterProxy_StepSimulation)
    pHkCharacterProxy_StepSimulation(instance, timeInSec, posX, posY, jump, wantJump, atLadder, gravity, up, forward);
}

Vector3 HkCharacterProxy_GetLinearVelocity(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkCharacterProxy_GetLinearVelocity);
    REQUIRE_FUNCTION_POINTER(HkCharacterProxy_GetLinearVelocity)
    return pHkCharacterProxy_GetLinearVelocity(instance);
}

void HkCharacterProxy_SetLinearVelocity(void* instance, Vector3 value) { EnsureThreadInfo();
    LOG_CALL(HkCharacterProxy_SetLinearVelocity);
    REQUIRE_FUNCTION_POINTER(HkCharacterProxy_SetLinearVelocity)
    pHkCharacterProxy_SetLinearVelocity(instance, value);
}

void HkCharacterProxy_SetUp(void* instance, Vector3 value) { EnsureThreadInfo();
    LOG_CALL(HkCharacterProxy_SetUp);
    REQUIRE_FUNCTION_POINTER(HkCharacterProxy_SetUp)
    pHkCharacterProxy_SetUp(instance, value);
}

void* HkCharacterProxyCinfo_Create(void) { EnsureThreadInfo();
    LOG_CALL(HkCharacterProxyCinfo_Create);
    REQUIRE_FUNCTION_POINTER(HkCharacterProxyCinfo_Create)
    return pHkCharacterProxyCinfo_Create();
}

Vector3 HkCharacterProxyCinfo_GetPosition(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkCharacterProxyCinfo_GetPosition);
    REQUIRE_FUNCTION_POINTER(HkCharacterProxyCinfo_GetPosition)
    return pHkCharacterProxyCinfo_GetPosition(instance);
}

void HkCharacterProxyCinfo_SetPosition(void* instance, Vector3 value) { EnsureThreadInfo();
    LOG_CALL(HkCharacterProxyCinfo_SetPosition);
    REQUIRE_FUNCTION_POINTER(HkCharacterProxyCinfo_SetPosition)
    pHkCharacterProxyCinfo_SetPosition(instance, value);
}

Vector3 HkCharacterProxyCinfo_GetVelocity(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkCharacterProxyCinfo_GetVelocity);
    REQUIRE_FUNCTION_POINTER(HkCharacterProxyCinfo_GetVelocity)
    return pHkCharacterProxyCinfo_GetVelocity(instance);
}

void HkCharacterProxyCinfo_SetVelocity(void* instance, Vector3 value) { EnsureThreadInfo();
    LOG_CALL(HkCharacterProxyCinfo_SetVelocity);
    REQUIRE_FUNCTION_POINTER(HkCharacterProxyCinfo_SetVelocity)
    pHkCharacterProxyCinfo_SetVelocity(instance, value);
}

float HkCharacterProxyCinfo_GetDynamicFriction(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkCharacterProxyCinfo_GetDynamicFriction);
    REQUIRE_FUNCTION_POINTER(HkCharacterProxyCinfo_GetDynamicFriction)
    return pHkCharacterProxyCinfo_GetDynamicFriction(instance);
}

void HkCharacterProxyCinfo_SetDynamicFriction(void* instance, float value) { EnsureThreadInfo();
    LOG_CALL(HkCharacterProxyCinfo_SetDynamicFriction);
    REQUIRE_FUNCTION_POINTER(HkCharacterProxyCinfo_SetDynamicFriction)
    pHkCharacterProxyCinfo_SetDynamicFriction(instance, value);
}

float HkCharacterProxyCinfo_GetStaticFriction(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkCharacterProxyCinfo_GetStaticFriction);
    REQUIRE_FUNCTION_POINTER(HkCharacterProxyCinfo_GetStaticFriction)
    return pHkCharacterProxyCinfo_GetStaticFriction(instance);
}

void HkCharacterProxyCinfo_SetStaticFriction(void* instance, float value) { EnsureThreadInfo();
    LOG_CALL(HkCharacterProxyCinfo_SetStaticFriction);
    REQUIRE_FUNCTION_POINTER(HkCharacterProxyCinfo_SetStaticFriction)
    pHkCharacterProxyCinfo_SetStaticFriction(instance, value);
}

float HkCharacterProxyCinfo_GetKeepContactTolerance(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkCharacterProxyCinfo_GetKeepContactTolerance);
    REQUIRE_FUNCTION_POINTER(HkCharacterProxyCinfo_GetKeepContactTolerance)
    return pHkCharacterProxyCinfo_GetKeepContactTolerance(instance);
}

void HkCharacterProxyCinfo_SetKeepContactTolerance(void* instance, float value) { EnsureThreadInfo();
    LOG_CALL(HkCharacterProxyCinfo_SetKeepContactTolerance);
    REQUIRE_FUNCTION_POINTER(HkCharacterProxyCinfo_SetKeepContactTolerance)
    pHkCharacterProxyCinfo_SetKeepContactTolerance(instance, value);
}

Vector3 HkCharacterProxyCinfo_GetUp(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkCharacterProxyCinfo_GetUp);
    REQUIRE_FUNCTION_POINTER(HkCharacterProxyCinfo_GetUp)
    return pHkCharacterProxyCinfo_GetUp(instance);
}

void HkCharacterProxyCinfo_SetUp(void* instance, Vector3 up) { EnsureThreadInfo();
    LOG_CALL(HkCharacterProxyCinfo_SetUp);
    REQUIRE_FUNCTION_POINTER(HkCharacterProxyCinfo_SetUp)
    pHkCharacterProxyCinfo_SetUp(instance, up);
}

float HkCharacterProxyCinfo_GetExtraUpStaticFriction(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkCharacterProxyCinfo_GetExtraUpStaticFriction);
    REQUIRE_FUNCTION_POINTER(HkCharacterProxyCinfo_GetExtraUpStaticFriction)
    return pHkCharacterProxyCinfo_GetExtraUpStaticFriction(instance);
}

void HkCharacterProxyCinfo_SetExtraUpStaticFriction(void* instance, float value) { EnsureThreadInfo();
    LOG_CALL(HkCharacterProxyCinfo_SetExtraUpStaticFriction);
    REQUIRE_FUNCTION_POINTER(HkCharacterProxyCinfo_SetExtraUpStaticFriction)
    pHkCharacterProxyCinfo_SetExtraUpStaticFriction(instance, value);
}

float HkCharacterProxyCinfo_GetExtraDownStaticFriction(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkCharacterProxyCinfo_GetExtraDownStaticFriction);
    REQUIRE_FUNCTION_POINTER(HkCharacterProxyCinfo_GetExtraDownStaticFriction)
    return pHkCharacterProxyCinfo_GetExtraDownStaticFriction(instance);
}

void HkCharacterProxyCinfo_SetExtraDownStaticFriction(void* instance, float value) { EnsureThreadInfo();
    LOG_CALL(HkCharacterProxyCinfo_SetExtraDownStaticFriction);
    REQUIRE_FUNCTION_POINTER(HkCharacterProxyCinfo_SetExtraDownStaticFriction)
    pHkCharacterProxyCinfo_SetExtraDownStaticFriction(instance, value);
}

void HkCharacterProxyCinfo_SetShapePhantom(void* instance, void* value) { EnsureThreadInfo();
    LOG_CALL(HkCharacterProxyCinfo_SetShapePhantom);
    REQUIRE_FUNCTION_POINTER(HkCharacterProxyCinfo_SetShapePhantom)
    pHkCharacterProxyCinfo_SetShapePhantom(instance, value);
}

void* HkCharacterProxyCinfo_GetShapePhantom(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkCharacterProxyCinfo_GetShapePhantom);
    REQUIRE_FUNCTION_POINTER(HkCharacterProxyCinfo_GetShapePhantom)
    return pHkCharacterProxyCinfo_GetShapePhantom(instance);
}

float HkCharacterProxyCinfo_GetKeepDistance(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkCharacterProxyCinfo_GetKeepDistance);
    REQUIRE_FUNCTION_POINTER(HkCharacterProxyCinfo_GetKeepDistance)
    return pHkCharacterProxyCinfo_GetKeepDistance(instance);
}

void HkCharacterProxyCinfo_SetKeepDistance(void* instance, float value) { EnsureThreadInfo();
    LOG_CALL(HkCharacterProxyCinfo_SetKeepDistance);
    REQUIRE_FUNCTION_POINTER(HkCharacterProxyCinfo_SetKeepDistance)
    pHkCharacterProxyCinfo_SetKeepDistance(instance, value);
}

float HkCharacterProxyCinfo_GetContactAngleSensitivity(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkCharacterProxyCinfo_GetContactAngleSensitivity);
    REQUIRE_FUNCTION_POINTER(HkCharacterProxyCinfo_GetContactAngleSensitivity)
    return pHkCharacterProxyCinfo_GetContactAngleSensitivity(instance);
}

void HkCharacterProxyCinfo_SetContactAngleSensitivity(void* instance, float value) { EnsureThreadInfo();
    LOG_CALL(HkCharacterProxyCinfo_SetContactAngleSensitivity);
    REQUIRE_FUNCTION_POINTER(HkCharacterProxyCinfo_SetContactAngleSensitivity)
    pHkCharacterProxyCinfo_SetContactAngleSensitivity(instance, value);
}

int32_t HkCharacterProxyCinfo_GetUserPlanes(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkCharacterProxyCinfo_GetUserPlanes);
    REQUIRE_FUNCTION_POINTER(HkCharacterProxyCinfo_GetUserPlanes)
    return pHkCharacterProxyCinfo_GetUserPlanes(instance);
}

void HkCharacterProxyCinfo_SetUserPlanes(void* instance, int32_t value) { EnsureThreadInfo();
    LOG_CALL(HkCharacterProxyCinfo_SetUserPlanes);
    REQUIRE_FUNCTION_POINTER(HkCharacterProxyCinfo_SetUserPlanes)
    pHkCharacterProxyCinfo_SetUserPlanes(instance, value);
}

float HkCharacterProxyCinfo_GetMaxCharacterSpeedForSolver(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkCharacterProxyCinfo_GetMaxCharacterSpeedForSolver);
    REQUIRE_FUNCTION_POINTER(HkCharacterProxyCinfo_GetMaxCharacterSpeedForSolver)
    return pHkCharacterProxyCinfo_GetMaxCharacterSpeedForSolver(instance);
}

void HkCharacterProxyCinfo_SetMaxCharacterSpeedForSolver(void* instance, float value) { EnsureThreadInfo();
    LOG_CALL(HkCharacterProxyCinfo_SetMaxCharacterSpeedForSolver);
    REQUIRE_FUNCTION_POINTER(HkCharacterProxyCinfo_SetMaxCharacterSpeedForSolver)
    pHkCharacterProxyCinfo_SetMaxCharacterSpeedForSolver(instance, value);
}

float HkCharacterProxyCinfo_GetCharacterStrength(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkCharacterProxyCinfo_GetCharacterStrength);
    REQUIRE_FUNCTION_POINTER(HkCharacterProxyCinfo_GetCharacterStrength)
    return pHkCharacterProxyCinfo_GetCharacterStrength(instance);
}

void HkCharacterProxyCinfo_SetCharacterStrength(void* instance, float value) { EnsureThreadInfo();
    LOG_CALL(HkCharacterProxyCinfo_SetCharacterStrength);
    REQUIRE_FUNCTION_POINTER(HkCharacterProxyCinfo_SetCharacterStrength)
    pHkCharacterProxyCinfo_SetCharacterStrength(instance, value);
}

float HkCharacterProxyCinfo_GetCharacterMass(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkCharacterProxyCinfo_GetCharacterMass);
    REQUIRE_FUNCTION_POINTER(HkCharacterProxyCinfo_GetCharacterMass)
    return pHkCharacterProxyCinfo_GetCharacterMass(instance);
}

void HkCharacterProxyCinfo_SetCharacterMass(void* instance, float value) { EnsureThreadInfo();
    LOG_CALL(HkCharacterProxyCinfo_SetCharacterMass);
    REQUIRE_FUNCTION_POINTER(HkCharacterProxyCinfo_SetCharacterMass)
    pHkCharacterProxyCinfo_SetCharacterMass(instance, value);
}

float HkCharacterProxyCinfo_GetMaxSlope(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkCharacterProxyCinfo_GetMaxSlope);
    REQUIRE_FUNCTION_POINTER(HkCharacterProxyCinfo_GetMaxSlope)
    return pHkCharacterProxyCinfo_GetMaxSlope(instance);
}

void HkCharacterProxyCinfo_SetMaxSlope(void* instance, float value) { EnsureThreadInfo();
    LOG_CALL(HkCharacterProxyCinfo_SetMaxSlope);
    REQUIRE_FUNCTION_POINTER(HkCharacterProxyCinfo_SetMaxSlope)
    pHkCharacterProxyCinfo_SetMaxSlope(instance, value);
}

float HkCharacterProxyCinfo_GetPenetrationRecoverySpeed(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkCharacterProxyCinfo_GetPenetrationRecoverySpeed);
    REQUIRE_FUNCTION_POINTER(HkCharacterProxyCinfo_GetPenetrationRecoverySpeed)
    return pHkCharacterProxyCinfo_GetPenetrationRecoverySpeed(instance);
}

void HkCharacterProxyCinfo_SetPenetrationRecoverySpeed(void* instance, float value) { EnsureThreadInfo();
    LOG_CALL(HkCharacterProxyCinfo_SetPenetrationRecoverySpeed);
    REQUIRE_FUNCTION_POINTER(HkCharacterProxyCinfo_SetPenetrationRecoverySpeed)
    pHkCharacterProxyCinfo_SetPenetrationRecoverySpeed(instance, value);
}

int32_t HkCharacterProxyCinfo_GetMaxCastIterations(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkCharacterProxyCinfo_GetMaxCastIterations);
    REQUIRE_FUNCTION_POINTER(HkCharacterProxyCinfo_GetMaxCastIterations)
    return pHkCharacterProxyCinfo_GetMaxCastIterations(instance);
}

void HkCharacterProxyCinfo_SetMaxCastIterations(void* instance, int32_t value) { EnsureThreadInfo();
    LOG_CALL(HkCharacterProxyCinfo_SetMaxCastIterations);
    REQUIRE_FUNCTION_POINTER(HkCharacterProxyCinfo_SetMaxCastIterations)
    pHkCharacterProxyCinfo_SetMaxCastIterations(instance, value);
}

bool HkCharacterProxyCinfo_GetRefreshManifoldInCheckSupport(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkCharacterProxyCinfo_GetRefreshManifoldInCheckSupport);
    REQUIRE_FUNCTION_POINTER(HkCharacterProxyCinfo_GetRefreshManifoldInCheckSupport)
    return pHkCharacterProxyCinfo_GetRefreshManifoldInCheckSupport(instance);
}

void HkCharacterProxyCinfo_SetRefreshManifoldInCheckSupport(void* instance, bool value) { EnsureThreadInfo();
    LOG_CALL(HkCharacterProxyCinfo_SetRefreshManifoldInCheckSupport);
    REQUIRE_FUNCTION_POINTER(HkCharacterProxyCinfo_SetRefreshManifoldInCheckSupport)
    pHkCharacterProxyCinfo_SetRefreshManifoldInCheckSupport(instance, value);
}

void* HkCharacterRigidBody_Create(void* characterRigidBodyCinfo, float maxCharacterSpeed) { EnsureThreadInfo();
    LOG_CALL(HkCharacterRigidBody_Create);
    REQUIRE_FUNCTION_POINTER(HkCharacterRigidBody_Create)
    return pHkCharacterRigidBody_Create(characterRigidBodyCinfo, maxCharacterSpeed);
}

void* HkCharacterRigidBody_GetCharacterRigidbody(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkCharacterRigidBody_GetCharacterRigidbody);
    REQUIRE_FUNCTION_POINTER(HkCharacterRigidBody_GetCharacterRigidbody)
    return pHkCharacterRigidBody_GetCharacterRigidbody(instance);
}

void HkCharacterRigidBody_SetWalkingState(void* instance, void* shape, float jumpHeight, float gainSpeed, float maxCharacterSpeed) { EnsureThreadInfo();
    LOG_CALL(HkCharacterRigidBody_SetWalkingState);
    REQUIRE_FUNCTION_POINTER(HkCharacterRigidBody_SetWalkingState)
    pHkCharacterRigidBody_SetWalkingState(instance, shape, jumpHeight, gainSpeed, maxCharacterSpeed);
}

void HkCharacterRigidBody_SetFlyingState(void* instance, void* shape, float maxCharacterSpeed, float maxAcceleration) { EnsureThreadInfo();
    LOG_CALL(HkCharacterRigidBody_SetFlyingState);
    REQUIRE_FUNCTION_POINTER(HkCharacterRigidBody_SetFlyingState)
    pHkCharacterRigidBody_SetFlyingState(instance, shape, maxCharacterSpeed, maxAcceleration);
}

void HkCharacterRigidBody_SetLadderState(void* instance, float maxCharacterSpeed, float maxAcceleration) { EnsureThreadInfo();
    LOG_CALL(HkCharacterRigidBody_SetLadderState);
    REQUIRE_FUNCTION_POINTER(HkCharacterRigidBody_SetLadderState)
    pHkCharacterRigidBody_SetLadderState(instance, maxCharacterSpeed, maxAcceleration);
}

void HkCharacterRigidBody_SetDefaultShape(void* instance, void* shape) { EnsureThreadInfo();
    LOG_CALL(HkCharacterRigidBody_SetDefaultShape);
    REQUIRE_FUNCTION_POINTER(HkCharacterRigidBody_SetDefaultShape)
    pHkCharacterRigidBody_SetDefaultShape(instance, shape);
}

void HkCharacterRigidBody_SetShapeForCrouch(void* instance, void* shape) { EnsureThreadInfo();
    LOG_CALL(HkCharacterRigidBody_SetShapeForCrouch);
    REQUIRE_FUNCTION_POINTER(HkCharacterRigidBody_SetShapeForCrouch)
    pHkCharacterRigidBody_SetShapeForCrouch(instance, shape);
}

Vector3 HkCharacterRigidBody_GetPosition(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkCharacterRigidBody_GetPosition);
    REQUIRE_FUNCTION_POINTER(HkCharacterRigidBody_GetPosition)
    return pHkCharacterRigidBody_GetPosition(instance);
}

void HkCharacterRigidBody_SetPosition(void* instance, Vector3 value) { EnsureThreadInfo();
    LOG_CALL(HkCharacterRigidBody_SetPosition);
    REQUIRE_FUNCTION_POINTER(HkCharacterRigidBody_SetPosition)
    pHkCharacterRigidBody_SetPosition(instance, value);
}

int32_t HkCharacterRigidBody_GetState(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkCharacterRigidBody_GetState);
    REQUIRE_FUNCTION_POINTER(HkCharacterRigidBody_GetState)
    return pHkCharacterRigidBody_GetState(instance);
}

void HkCharacterRigidBody_SetState(void* instance, int32_t value) { EnsureThreadInfo();
    LOG_CALL(HkCharacterRigidBody_SetState);
    REQUIRE_FUNCTION_POINTER(HkCharacterRigidBody_SetState)
    pHkCharacterRigidBody_SetState(instance, value);
}

void HkCharacterRigidBody_StepSimulation(void* instance, float timeInSec, bool Jump, bool WantJump, bool AtLadder, float PosX, float PosY, float Speed, float Elevate, Vector3 Up, Vector3 Forward, Vector3 ElevateVector, Vector3 ElevateUpVector, Vector3 Gravity, float myJumpHeight, void* AngularVelocity) { EnsureThreadInfo();
    LOG_CALL(HkCharacterRigidBody_StepSimulation);
    REQUIRE_FUNCTION_POINTER(HkCharacterRigidBody_StepSimulation)
    pHkCharacterRigidBody_StepSimulation(instance, timeInSec, Jump, WantJump, AtLadder, PosX, PosY, Speed, Elevate, Up, Forward, ElevateVector, ElevateUpVector, Gravity, myJumpHeight, AngularVelocity);
}

void HkCharacterRigidBody_UpdateVelocity(void* instance, float timeInSec, bool Supported, Vector3 AngularVelocity, Quaternion DesiredOrientation) { EnsureThreadInfo();
    LOG_CALL(HkCharacterRigidBody_UpdateVelocity);
    REQUIRE_FUNCTION_POINTER(HkCharacterRigidBody_UpdateVelocity)
    pHkCharacterRigidBody_UpdateVelocity(instance, timeInSec, Supported, AngularVelocity, DesiredOrientation);
}

void HkCharacterRigidBody_UpdateSupport(void* instance, float timeInSec) { EnsureThreadInfo();
    LOG_CALL(HkCharacterRigidBody_UpdateSupport);
    REQUIRE_FUNCTION_POINTER(HkCharacterRigidBody_UpdateSupport)
    pHkCharacterRigidBody_UpdateSupport(instance, timeInSec);
}

void HkCharacterRigidBody_SetRigidBodyTransform(void* instance, Matrix world) { EnsureThreadInfo();
    LOG_CALL(HkCharacterRigidBody_SetRigidBodyTransform);
    REQUIRE_FUNCTION_POINTER(HkCharacterRigidBody_SetRigidBodyTransform)
    pHkCharacterRigidBody_SetRigidBodyTransform(instance, world);
}

Matrix HkCharacterRigidBody_GetRigidBodyTransform(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkCharacterRigidBody_GetRigidBodyTransform);
    REQUIRE_FUNCTION_POINTER(HkCharacterRigidBody_GetRigidBodyTransform)
    return pHkCharacterRigidBody_GetRigidBodyTransform(instance);
}

Vector3 HkCharacterRigidBody_GetLinearVelocity(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkCharacterRigidBody_GetLinearVelocity);
    REQUIRE_FUNCTION_POINTER(HkCharacterRigidBody_GetLinearVelocity)
    return pHkCharacterRigidBody_GetLinearVelocity(instance);
}

void HkCharacterRigidBody_SetLinearVelocity(void* instance, Vector3 value) { EnsureThreadInfo();
    LOG_CALL(HkCharacterRigidBody_SetLinearVelocity);
    REQUIRE_FUNCTION_POINTER(HkCharacterRigidBody_SetLinearVelocity)
    pHkCharacterRigidBody_SetLinearVelocity(instance, value);
}

void HkCharacterRigidBody_ApplyLinearImpulse(void* instance, Vector3 impulse) { EnsureThreadInfo();
    LOG_CALL(HkCharacterRigidBody_ApplyLinearImpulse);
    REQUIRE_FUNCTION_POINTER(HkCharacterRigidBody_ApplyLinearImpulse)
    pHkCharacterRigidBody_ApplyLinearImpulse(instance, impulse);
}

void HkCharacterRigidBody_ApplyAngularImpulse(void* instance, Vector3 impulse) { EnsureThreadInfo();
    LOG_CALL(HkCharacterRigidBody_ApplyAngularImpulse);
    REQUIRE_FUNCTION_POINTER(HkCharacterRigidBody_ApplyAngularImpulse)
    pHkCharacterRigidBody_ApplyAngularImpulse(instance, impulse);
}

void HkCharacterRigidBody_SetSupportDistance(void* instance, float distance) { EnsureThreadInfo();
    LOG_CALL(HkCharacterRigidBody_SetSupportDistance);
    REQUIRE_FUNCTION_POINTER(HkCharacterRigidBody_SetSupportDistance)
    pHkCharacterRigidBody_SetSupportDistance(instance, distance);
}

void HkCharacterRigidBody_SetHardSupportDistance(void* instance, float distance) { EnsureThreadInfo();
    LOG_CALL(HkCharacterRigidBody_SetHardSupportDistance);
    REQUIRE_FUNCTION_POINTER(HkCharacterRigidBody_SetHardSupportDistance)
    pHkCharacterRigidBody_SetHardSupportDistance(instance, distance);
}

Vector3 HkCharacterRigidBody_GetAngularVelocity(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkCharacterRigidBody_GetAngularVelocity);
    REQUIRE_FUNCTION_POINTER(HkCharacterRigidBody_GetAngularVelocity)
    return pHkCharacterRigidBody_GetAngularVelocity(instance);
}

void HkCharacterRigidBody_SetAngularVelocity(void* instance, Vector3 value) { EnsureThreadInfo();
    LOG_CALL(HkCharacterRigidBody_SetAngularVelocity);
    REQUIRE_FUNCTION_POINTER(HkCharacterRigidBody_SetAngularVelocity)
    pHkCharacterRigidBody_SetAngularVelocity(instance, value);
}

bool HkCharacterRigidBody_IsSupportedByFloatingObject(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkCharacterRigidBody_IsSupportedByFloatingObject);
    REQUIRE_FUNCTION_POINTER(HkCharacterRigidBody_IsSupportedByFloatingObject)
    return pHkCharacterRigidBody_IsSupportedByFloatingObject(instance);
}

bool HkCharacterRigidBody_IsSupported(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkCharacterRigidBody_IsSupported);
    REQUIRE_FUNCTION_POINTER(HkCharacterRigidBody_IsSupported)
    return pHkCharacterRigidBody_IsSupported(instance);
}

Vector3 HkCharacterRigidBody_GetSupportNormal(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkCharacterRigidBody_GetSupportNormal);
    REQUIRE_FUNCTION_POINTER(HkCharacterRigidBody_GetSupportNormal)
    return pHkCharacterRigidBody_GetSupportNormal(instance);
}

Vector3 HkCharacterRigidBody_GetGroundVelocity(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkCharacterRigidBody_GetGroundVelocity);
    REQUIRE_FUNCTION_POINTER(HkCharacterRigidBody_GetGroundVelocity)
    return pHkCharacterRigidBody_GetGroundVelocity(instance);
}

bool HkCharacterRigidBody_GetUseSupportInfoQuery(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkCharacterRigidBody_GetUseSupportInfoQuery);
    REQUIRE_FUNCTION_POINTER(HkCharacterRigidBody_GetUseSupportInfoQuery)
    return pHkCharacterRigidBody_GetUseSupportInfoQuery(instance);
}

void HkCharacterRigidBody_SetUseSupportInfoQuery(void* instance, bool value) { EnsureThreadInfo();
    LOG_CALL(HkCharacterRigidBody_SetUseSupportInfoQuery);
    REQUIRE_FUNCTION_POINTER(HkCharacterRigidBody_SetUseSupportInfoQuery)
    pHkCharacterRigidBody_SetUseSupportInfoQuery(instance, value);
}

void HkCharacterRigidBody_SetPreviousSupportedState(void* instance, bool supported) { EnsureThreadInfo();
    LOG_CALL(HkCharacterRigidBody_SetPreviousSupportedState);
    REQUIRE_FUNCTION_POINTER(HkCharacterRigidBody_SetPreviousSupportedState)
    pHkCharacterRigidBody_SetPreviousSupportedState(instance, supported);
}

void HkCharacterRigidBody_ResetSurfaceVelocity(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkCharacterRigidBody_ResetSurfaceVelocity);
    REQUIRE_FUNCTION_POINTER(HkCharacterRigidBody_ResetSurfaceVelocity)
    pHkCharacterRigidBody_ResetSurfaceVelocity(instance);
}

void HkCharacterRigidBody_SetMaxSlope(void* instance, float value) { EnsureThreadInfo();
    LOG_CALL(HkCharacterRigidBody_SetMaxSlope);
    REQUIRE_FUNCTION_POINTER(HkCharacterRigidBody_SetMaxSlope)
    pHkCharacterRigidBody_SetMaxSlope(instance, value);
}

float HkCharacterRigidBody_GetMaxSlope(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkCharacterRigidBody_GetMaxSlope);
    REQUIRE_FUNCTION_POINTER(HkCharacterRigidBody_GetMaxSlope)
    return pHkCharacterRigidBody_GetMaxSlope(instance);
}

void HkCharacterRigidBody_GetSupportBodies(void* instance, void* size, void* version, void* list) { EnsureThreadInfo();
    LOG_CALL(HkCharacterRigidBody_GetSupportBodies);
    REQUIRE_FUNCTION_POINTER(HkCharacterRigidBody_GetSupportBodies)
    pHkCharacterRigidBody_GetSupportBodies(instance, size, version, list);
}

void* HkCharacterRigidBodyCinfo_Create(void) { EnsureThreadInfo();
    LOG_CALL(HkCharacterRigidBodyCinfo_Create);
    REQUIRE_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_Create)
    return pHkCharacterRigidBodyCinfo_Create();
}

int32_t HkCharacterRigidBodyCinfo_GetCollisionFilterInfo(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkCharacterRigidBodyCinfo_GetCollisionFilterInfo);
    REQUIRE_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_GetCollisionFilterInfo)
    return pHkCharacterRigidBodyCinfo_GetCollisionFilterInfo(instance);
}

void HkCharacterRigidBodyCinfo_SetCollisionFilterInfo(void* instance, int32_t value) { EnsureThreadInfo();
    LOG_CALL(HkCharacterRigidBodyCinfo_SetCollisionFilterInfo);
    REQUIRE_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_SetCollisionFilterInfo)
    pHkCharacterRigidBodyCinfo_SetCollisionFilterInfo(instance, value);
}

void* HkCharacterRigidBodyCinfo_GetShape(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkCharacterRigidBodyCinfo_GetShape);
    REQUIRE_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_GetShape)
    return pHkCharacterRigidBodyCinfo_GetShape(instance);
}

void HkCharacterRigidBodyCinfo_SetShape(void* instance, void* value) { EnsureThreadInfo();
    LOG_CALL(HkCharacterRigidBodyCinfo_SetShape);
    REQUIRE_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_SetShape)
    pHkCharacterRigidBodyCinfo_SetShape(instance, value);
}

Vector3 HkCharacterRigidBodyCinfo_GetPosition(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkCharacterRigidBodyCinfo_GetPosition);
    REQUIRE_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_GetPosition)
    return pHkCharacterRigidBodyCinfo_GetPosition(instance);
}

void HkCharacterRigidBodyCinfo_SetPosition(void* instance, Vector3 value) { EnsureThreadInfo();
    LOG_CALL(HkCharacterRigidBodyCinfo_SetPosition);
    REQUIRE_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_SetPosition)
    pHkCharacterRigidBodyCinfo_SetPosition(instance, value);
}

Quaternion HkCharacterRigidBodyCinfo_GetRotation(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkCharacterRigidBodyCinfo_GetRotation);
    REQUIRE_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_GetRotation)
    return pHkCharacterRigidBodyCinfo_GetRotation(instance);
}

void HkCharacterRigidBodyCinfo_SetRotation(void* instance, Quaternion value) { EnsureThreadInfo();
    LOG_CALL(HkCharacterRigidBodyCinfo_SetRotation);
    REQUIRE_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_SetRotation)
    pHkCharacterRigidBodyCinfo_SetRotation(instance, value);
}

float HkCharacterRigidBodyCinfo_GetMass(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkCharacterRigidBodyCinfo_GetMass);
    REQUIRE_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_GetMass)
    return pHkCharacterRigidBodyCinfo_GetMass(instance);
}

void HkCharacterRigidBodyCinfo_SetMass(void* instance, float value) { EnsureThreadInfo();
    LOG_CALL(HkCharacterRigidBodyCinfo_SetMass);
    REQUIRE_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_SetMass)
    pHkCharacterRigidBodyCinfo_SetMass(instance, value);
}

float HkCharacterRigidBodyCinfo_GetFriction(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkCharacterRigidBodyCinfo_GetFriction);
    REQUIRE_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_GetFriction)
    return pHkCharacterRigidBodyCinfo_GetFriction(instance);
}

void HkCharacterRigidBodyCinfo_SetFriction(void* instance, float value) { EnsureThreadInfo();
    LOG_CALL(HkCharacterRigidBodyCinfo_SetFriction);
    REQUIRE_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_SetFriction)
    pHkCharacterRigidBodyCinfo_SetFriction(instance, value);
}

float HkCharacterRigidBodyCinfo_GetMaxLinearVelocity(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkCharacterRigidBodyCinfo_GetMaxLinearVelocity);
    REQUIRE_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_GetMaxLinearVelocity)
    return pHkCharacterRigidBodyCinfo_GetMaxLinearVelocity(instance);
}

void HkCharacterRigidBodyCinfo_SetMaxLinearVelocity(void* instance, float value) { EnsureThreadInfo();
    LOG_CALL(HkCharacterRigidBodyCinfo_SetMaxLinearVelocity);
    REQUIRE_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_SetMaxLinearVelocity)
    pHkCharacterRigidBodyCinfo_SetMaxLinearVelocity(instance, value);
}

float HkCharacterRigidBodyCinfo_GetAllowedPenetrationDepth(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkCharacterRigidBodyCinfo_GetAllowedPenetrationDepth);
    REQUIRE_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_GetAllowedPenetrationDepth)
    return pHkCharacterRigidBodyCinfo_GetAllowedPenetrationDepth(instance);
}

void HkCharacterRigidBodyCinfo_SetAllowedPenetrationDepth(void* instance, float value) { EnsureThreadInfo();
    LOG_CALL(HkCharacterRigidBodyCinfo_SetAllowedPenetrationDepth);
    REQUIRE_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_SetAllowedPenetrationDepth)
    pHkCharacterRigidBodyCinfo_SetAllowedPenetrationDepth(instance, value);
}

Vector3 HkCharacterRigidBodyCinfo_GetUp(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkCharacterRigidBodyCinfo_GetUp);
    REQUIRE_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_GetUp)
    return pHkCharacterRigidBodyCinfo_GetUp(instance);
}

void HkCharacterRigidBodyCinfo_SetUp(void* instance, Vector3 value) { EnsureThreadInfo();
    LOG_CALL(HkCharacterRigidBodyCinfo_SetUp);
    REQUIRE_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_SetUp)
    pHkCharacterRigidBodyCinfo_SetUp(instance, value);
}

float HkCharacterRigidBodyCinfo_GetMaxSlope(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkCharacterRigidBodyCinfo_GetMaxSlope);
    REQUIRE_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_GetMaxSlope)
    return pHkCharacterRigidBodyCinfo_GetMaxSlope(instance);
}

void HkCharacterRigidBodyCinfo_SetMaxSlope(void* instance, float value) { EnsureThreadInfo();
    LOG_CALL(HkCharacterRigidBodyCinfo_SetMaxSlope);
    REQUIRE_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_SetMaxSlope)
    pHkCharacterRigidBodyCinfo_SetMaxSlope(instance, value);
}

float HkCharacterRigidBodyCinfo_GetMaxForce(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkCharacterRigidBodyCinfo_GetMaxForce);
    REQUIRE_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_GetMaxForce)
    return pHkCharacterRigidBodyCinfo_GetMaxForce(instance);
}

void HkCharacterRigidBodyCinfo_SetMaxForce(void* instance, float value) { EnsureThreadInfo();
    LOG_CALL(HkCharacterRigidBodyCinfo_SetMaxForce);
    REQUIRE_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_SetMaxForce)
    pHkCharacterRigidBodyCinfo_SetMaxForce(instance, value);
}

float HkCharacterRigidBodyCinfo_GetUnweldingHeightOffsetFactor(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkCharacterRigidBodyCinfo_GetUnweldingHeightOffsetFactor);
    REQUIRE_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_GetUnweldingHeightOffsetFactor)
    return pHkCharacterRigidBodyCinfo_GetUnweldingHeightOffsetFactor(instance);
}

void HkCharacterRigidBodyCinfo_SetUnweldingHeightOffsetFactor(void* instance, float value) { EnsureThreadInfo();
    LOG_CALL(HkCharacterRigidBodyCinfo_SetUnweldingHeightOffsetFactor);
    REQUIRE_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_SetUnweldingHeightOffsetFactor)
    pHkCharacterRigidBodyCinfo_SetUnweldingHeightOffsetFactor(instance, value);
}

float HkCharacterRigidBodyCinfo_GetMaxSpeedForSimplexSolver(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkCharacterRigidBodyCinfo_GetMaxSpeedForSimplexSolver);
    REQUIRE_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_GetMaxSpeedForSimplexSolver)
    return pHkCharacterRigidBodyCinfo_GetMaxSpeedForSimplexSolver(instance);
}

void HkCharacterRigidBodyCinfo_SetMaxSpeedForSimplexSolver(void* instance, float value) { EnsureThreadInfo();
    LOG_CALL(HkCharacterRigidBodyCinfo_SetMaxSpeedForSimplexSolver);
    REQUIRE_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_SetMaxSpeedForSimplexSolver)
    pHkCharacterRigidBodyCinfo_SetMaxSpeedForSimplexSolver(instance, value);
}

float HkCharacterRigidBodyCinfo_GetSupportDistance(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkCharacterRigidBodyCinfo_GetSupportDistance);
    REQUIRE_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_GetSupportDistance)
    return pHkCharacterRigidBodyCinfo_GetSupportDistance(instance);
}

void HkCharacterRigidBodyCinfo_SetSupportDistance(void* instance, float value) { EnsureThreadInfo();
    LOG_CALL(HkCharacterRigidBodyCinfo_SetSupportDistance);
    REQUIRE_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_SetSupportDistance)
    pHkCharacterRigidBodyCinfo_SetSupportDistance(instance, value);
}

float HkCharacterRigidBodyCinfo_GetHardSupportDistance(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkCharacterRigidBodyCinfo_GetHardSupportDistance);
    REQUIRE_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_GetHardSupportDistance)
    return pHkCharacterRigidBodyCinfo_GetHardSupportDistance(instance);
}

void HkCharacterRigidBodyCinfo_SetHardSupportDistance(void* instance, float value) { EnsureThreadInfo();
    LOG_CALL(HkCharacterRigidBodyCinfo_SetHardSupportDistance);
    REQUIRE_FUNCTION_POINTER(HkCharacterRigidBodyCinfo_SetHardSupportDistance)
    pHkCharacterRigidBodyCinfo_SetHardSupportDistance(instance, value);
}

void* HkCogWheelConstraintData_Create(void) { EnsureThreadInfo();
    LOG_CALL(HkCogWheelConstraintData_Create);
    REQUIRE_FUNCTION_POINTER(HkCogWheelConstraintData_Create)
    return pHkCogWheelConstraintData_Create();
}

void HkCogWheelConstraintData_SetInWorldSpace(void* instance, Matrix bodyATransform, Matrix bodyBTransform, Vector3 rotationPivotA, Vector3 rotationAxisA, float radiusA, Vector3 rotationPivotB, Vector3 rotationAxisB, float radiusB) { EnsureThreadInfo();
    LOG_CALL(HkCogWheelConstraintData_SetInWorldSpace);
    REQUIRE_FUNCTION_POINTER(HkCogWheelConstraintData_SetInWorldSpace)
    pHkCogWheelConstraintData_SetInWorldSpace(instance, bodyATransform, bodyBTransform, rotationPivotA, rotationAxisA, radiusA, rotationPivotB, rotationAxisB, radiusB);
}

void HkCogWheelConstraintData_SetInBodySpaceInternal(void* instance, Vector3 rotationPivotAInA, Vector3 rotationAxisAInA, float radiusA, Vector3 rotationPivotBInB, Vector3 rotationAxisBInB, float radiusB) { EnsureThreadInfo();
    LOG_CALL(HkCogWheelConstraintData_SetInBodySpaceInternal);
    REQUIRE_FUNCTION_POINTER(HkCogWheelConstraintData_SetInBodySpaceInternal)
    pHkCogWheelConstraintData_SetInBodySpaceInternal(instance, rotationPivotAInA, rotationAxisAInA, radiusA, rotationPivotBInB, rotationAxisBInB, radiusB);
}

int32_t HkCollisionEvent_GetSource(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkCollisionEvent_GetSource);
    REQUIRE_FUNCTION_POINTER(HkCollisionEvent_GetSource)
    return pHkCollisionEvent_GetSource(instance);
}

void* HkCollisionEvent_GetRigidBody(void* instance, int32_t bodyIndex) { EnsureThreadInfo();
    LOG_CALL(HkCollisionEvent_GetRigidBody);
    REQUIRE_FUNCTION_POINTER(HkCollisionEvent_GetRigidBody)
    return pHkCollisionEvent_GetRigidBody(instance, bodyIndex);
}

void* HkCollisionEvent_GetBodyA(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkCollisionEvent_GetBodyA);
    REQUIRE_FUNCTION_POINTER(HkCollisionEvent_GetBodyA)
    return pHkCollisionEvent_GetBodyA(instance);
}

void* HkCollisionEvent_GetBodyB(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkCollisionEvent_GetBodyB);
    REQUIRE_FUNCTION_POINTER(HkCollisionEvent_GetBodyB)
    return pHkCollisionEvent_GetBodyB(instance);
}

bool HkCollisionEvent_SetImpulse(void* instance, float impulse) { EnsureThreadInfo();
    LOG_CALL(HkCollisionEvent_SetImpulse);
    REQUIRE_FUNCTION_POINTER(HkCollisionEvent_SetImpulse)
    return pHkCollisionEvent_SetImpulse(instance, impulse);
}

void HkCollisionEvent_SetImpulseScaling(void* instance, float impulse, float maxAccel) { EnsureThreadInfo();
    LOG_CALL(HkCollisionEvent_SetImpulseScaling);
    REQUIRE_FUNCTION_POINTER(HkCollisionEvent_SetImpulseScaling)
    pHkCollisionEvent_SetImpulseScaling(instance, impulse, maxAccel);
}

int32_t HkCollisionEvent_GetContactPointCount(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkCollisionEvent_GetContactPointCount);
    REQUIRE_FUNCTION_POINTER(HkCollisionEvent_GetContactPointCount)
    return pHkCollisionEvent_GetContactPointCount(instance);
}

void HkCollisionEvent_Disable(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkCollisionEvent_Disable);
    REQUIRE_FUNCTION_POINTER(HkCollisionEvent_Disable)
    pHkCollisionEvent_Disable(instance);
}

void* HkCollisionEvent_GetContactPointPropertiesAt(void* instance, int32_t index) { EnsureThreadInfo();
    LOG_CALL(HkCollisionEvent_GetContactPointPropertiesAt);
    REQUIRE_FUNCTION_POINTER(HkCollisionEvent_GetContactPointPropertiesAt)
    return pHkCollisionEvent_GetContactPointPropertiesAt(instance, index);
}

void HkCollisionEvent_GetOffsets(void* bodyPointerOffset) { EnsureThreadInfo();
    LOG_CALL(HkCollisionEvent_GetOffsets);
    REQUIRE_FUNCTION_POINTER(HkCollisionEvent_GetOffsets)
    pHkCollisionEvent_GetOffsets(bodyPointerOffset);
}

void* HkConstraint_Create(void* entityA, void* entityB, void* data, int32_t priority) { EnsureThreadInfo();
    LOG_CALL(HkConstraint_Create);
    REQUIRE_FUNCTION_POINTER(HkConstraint_Create)
    return pHkConstraint_Create(entityA, entityB, data, priority);
}

void HkConstraint_AddConstraintListener(void* instance, void* listener) { EnsureThreadInfo();
    LOG_CALL(HkConstraint_AddConstraintListener);
    REQUIRE_FUNCTION_POINTER(HkConstraint_AddConstraintListener)
    pHkConstraint_AddConstraintListener(instance, listener);
}

void HkConstraint_RemoveConstraintListener(void* instance, void* listener) { EnsureThreadInfo();
    LOG_CALL(HkConstraint_RemoveConstraintListener);
    REQUIRE_FUNCTION_POINTER(HkConstraint_RemoveConstraintListener)
    pHkConstraint_RemoveConstraintListener(instance, listener);
}

void HkConstraint_ReplaceEntity(void* instance, void* oldEntity, void* newEntity) { EnsureThreadInfo();
    LOG_CALL(HkConstraint_ReplaceEntity);
    REQUIRE_FUNCTION_POINTER(HkConstraint_ReplaceEntity)
    pHkConstraint_ReplaceEntity(instance, oldEntity, newEntity);
}

void HkConstraint_SetVirtualMassInverse(void* instance, Vector4 invMassA, Vector4 invMassB) { EnsureThreadInfo();
    LOG_CALL(HkConstraint_SetVirtualMassInverse);
    REQUIRE_FUNCTION_POINTER(HkConstraint_SetVirtualMassInverse)
    pHkConstraint_SetVirtualMassInverse(instance, invMassA, invMassB);
}

int32_t HkConstraint_GetPriority(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkConstraint_GetPriority);
    REQUIRE_FUNCTION_POINTER(HkConstraint_GetPriority)
    return pHkConstraint_GetPriority(instance);
}

void HkConstraint_SetPriority(void* instance, int32_t value) { EnsureThreadInfo();
    LOG_CALL(HkConstraint_SetPriority);
    REQUIRE_FUNCTION_POINTER(HkConstraint_SetPriority)
    pHkConstraint_SetPriority(instance, value);
}

bool HkConstraint_GetWantRuntime(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkConstraint_GetWantRuntime);
    REQUIRE_FUNCTION_POINTER(HkConstraint_GetWantRuntime)
    return pHkConstraint_GetWantRuntime(instance);
}

void HkConstraint_SetWantRuntime(void* instance, bool value) { EnsureThreadInfo();
    LOG_CALL(HkConstraint_SetWantRuntime);
    REQUIRE_FUNCTION_POINTER(HkConstraint_SetWantRuntime)
    pHkConstraint_SetWantRuntime(instance, value);
}

bool HkConstraint_IsInWorld(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkConstraint_IsInWorld);
    REQUIRE_FUNCTION_POINTER(HkConstraint_IsInWorld)
    return pHkConstraint_IsInWorld(instance);
}

void* HkConstraint_GetRigidBodyA(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkConstraint_GetRigidBodyA);
    REQUIRE_FUNCTION_POINTER(HkConstraint_GetRigidBodyA)
    return pHkConstraint_GetRigidBodyA(instance);
}

void* HkConstraint_GetRigidBodyB(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkConstraint_GetRigidBodyB);
    REQUIRE_FUNCTION_POINTER(HkConstraint_GetRigidBodyB)
    return pHkConstraint_GetRigidBodyB(instance);
}

bool HkConstraint_GetEnabled(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkConstraint_GetEnabled);
    REQUIRE_FUNCTION_POINTER(HkConstraint_GetEnabled)
    return pHkConstraint_GetEnabled(instance);
}

void HkConstraint_SetEnabled(void* instance, bool value) { EnsureThreadInfo();
    LOG_CALL(HkConstraint_SetEnabled);
    REQUIRE_FUNCTION_POINTER(HkConstraint_SetEnabled)
    pHkConstraint_SetEnabled(instance, value);
}

void HkConstraint_GetPivotsInWorld(void* instance, void* outPivotA, void* outPivotB) { EnsureThreadInfo();
    LOG_CALL(HkConstraint_GetPivotsInWorld);
    REQUIRE_FUNCTION_POINTER(HkConstraint_GetPivotsInWorld)
    pHkConstraint_GetPivotsInWorld(instance, outPivotA, outPivotB);
}

uint64_t HkConstraint_GetUserData(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkConstraint_GetUserData);
    REQUIRE_FUNCTION_POINTER(HkConstraint_GetUserData)
    return pHkConstraint_GetUserData(instance);
}

void HkConstraint_SetUserData(void* instance, uint64_t value) { EnsureThreadInfo();
    LOG_CALL(HkConstraint_SetUserData);
    REQUIRE_FUNCTION_POINTER(HkConstraint_SetUserData)
    pHkConstraint_SetUserData(instance, value);
}

void HkConstraint_AddCenterOfMassModifierAtom(void* instance, Vector3 modifierA, Vector3 modifierB) { EnsureThreadInfo();
    LOG_CALL(HkConstraint_AddCenterOfMassModifierAtom);
    REQUIRE_FUNCTION_POINTER(HkConstraint_AddCenterOfMassModifierAtom)
    pHkConstraint_AddCenterOfMassModifierAtom(instance, modifierA, modifierB);
}

void HkConstraint_FindConnectedConstraints(void* rigidBody, void* reader, void* userData) { EnsureThreadInfo();
    LOG_CALL(HkConstraint_FindConnectedConstraints);
    REQUIRE_FUNCTION_POINTER(HkConstraint_FindConnectedConstraints)
    pHkConstraint_FindConnectedConstraints(rigidBody, bridge_void_ptr_int_ptr(reader), userData);
}

float HkConstraintData_GetMaximumLinearImpulse(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkConstraintData_GetMaximumLinearImpulse);
    REQUIRE_FUNCTION_POINTER(HkConstraintData_GetMaximumLinearImpulse)
    return pHkConstraintData_GetMaximumLinearImpulse(instance);
}

void HkConstraintData_SetMaximumLinearImpulse(void* instance, float value) { EnsureThreadInfo();
    LOG_CALL(HkConstraintData_SetMaximumLinearImpulse);
    REQUIRE_FUNCTION_POINTER(HkConstraintData_SetMaximumLinearImpulse)
    pHkConstraintData_SetMaximumLinearImpulse(instance, value);
}

float HkConstraintData_GetMaximumAngularImpulse(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkConstraintData_GetMaximumAngularImpulse);
    REQUIRE_FUNCTION_POINTER(HkConstraintData_GetMaximumAngularImpulse)
    return pHkConstraintData_GetMaximumAngularImpulse(instance);
}

void HkConstraintData_SetMaximumAngularImpulse(void* instance, float value) { EnsureThreadInfo();
    LOG_CALL(HkConstraintData_SetMaximumAngularImpulse);
    REQUIRE_FUNCTION_POINTER(HkConstraintData_SetMaximumAngularImpulse)
    pHkConstraintData_SetMaximumAngularImpulse(instance, value);
}

float HkConstraintData_GetBreachImpulse(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkConstraintData_GetBreachImpulse);
    REQUIRE_FUNCTION_POINTER(HkConstraintData_GetBreachImpulse)
    return pHkConstraintData_GetBreachImpulse(instance);
}

void HkConstraintData_SetBreachImpulse(void* instance, float value) { EnsureThreadInfo();
    LOG_CALL(HkConstraintData_SetBreachImpulse);
    REQUIRE_FUNCTION_POINTER(HkConstraintData_SetBreachImpulse)
    pHkConstraintData_SetBreachImpulse(instance, value);
}

float HkConstraintData_GetInertiaStabilizationFactor(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkConstraintData_GetInertiaStabilizationFactor);
    REQUIRE_FUNCTION_POINTER(HkConstraintData_GetInertiaStabilizationFactor)
    return pHkConstraintData_GetInertiaStabilizationFactor(instance);
}

void HkConstraintData_SetInertiaStabilizationFactor(void* instance, float value) { EnsureThreadInfo();
    LOG_CALL(HkConstraintData_SetInertiaStabilizationFactor);
    REQUIRE_FUNCTION_POINTER(HkConstraintData_SetInertiaStabilizationFactor)
    pHkConstraintData_SetInertiaStabilizationFactor(instance, value);
}

void HkConstraintData_SetSolvingMethod(void* instance, int32_t method) { EnsureThreadInfo();
    LOG_CALL(HkConstraintData_SetSolvingMethod);
    REQUIRE_FUNCTION_POINTER(HkConstraintData_SetSolvingMethod)
    pHkConstraintData_SetSolvingMethod(instance, method);
}

void* HkConstraintListener_Create(void) { EnsureThreadInfo();
    LOG_CALL(HkConstraintListener_Create);
    REQUIRE_FUNCTION_POINTER(HkConstraintListener_Create)
    return pHkConstraintListener_Create();
}

void HkConstraintListener_Release(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkConstraintListener_Release);
    REQUIRE_FUNCTION_POINTER(HkConstraintListener_Release)
    pHkConstraintListener_Release(instance);
}

void HkConstraintListener_SetCallbacks(void* instance, void* onAdded, void* onRemoved, void* onBreaking) { EnsureThreadInfo();
    LOG_CALL(HkConstraintListener_SetCallbacks);
    REQUIRE_FUNCTION_POINTER(HkConstraintListener_SetCallbacks)
    pHkConstraintListener_SetCallbacks(instance, bridge_void_ptr(onAdded), bridge_void_ptr(onRemoved), bridge_void_ptr(onBreaking));
}

void* HkConstraintProjectorListener_Create(void* world) { EnsureThreadInfo();
    LOG_CALL(HkConstraintProjectorListener_Create);
    REQUIRE_FUNCTION_POINTER(HkConstraintProjectorListener_Create)
    return pHkConstraintProjectorListener_Create(world);
}

void HkConstraintProjectorListener_Release(void* listener) { EnsureThreadInfo();
    LOG_CALL(HkConstraintProjectorListener_Release);
    REQUIRE_FUNCTION_POINTER(HkConstraintProjectorListener_Release)
    pHkConstraintProjectorListener_Release(listener);
}

int32_t HkConstraintStabilizationUtil_StabilizeRagdollInertias(void* physicsSystem, float stabilizationAmount, float solverStabilizationAmount) { EnsureThreadInfo();
    LOG_CALL(HkConstraintStabilizationUtil_StabilizeRagdollInertias);
    REQUIRE_FUNCTION_POINTER(HkConstraintStabilizationUtil_StabilizeRagdollInertias)
    return pHkConstraintStabilizationUtil_StabilizeRagdollInertias(physicsSystem, stabilizationAmount, solverStabilizationAmount);
}

void* HkContactListener_Create(void* onContact, void* collisionAdded, void* collisionRemoved, int32_t callbackLimit) { EnsureThreadInfo();
    LOG_CALL(HkContactListener_Create);
    REQUIRE_FUNCTION_POINTER(HkContactListener_Create)
    auto result = pHkContactListener_Create(bridge_void_ptr_ptr(onContact), bridge_void_ptr_ptr(collisionAdded), bridge_void_ptr_ptr(collisionRemoved), callbackLimit);
    register_callback_owner(result, {callback_owner_binding{&release_void_ptr_ptr, onContact}, callback_owner_binding{&release_void_ptr_ptr, collisionAdded}, callback_owner_binding{&release_void_ptr_ptr, collisionRemoved}});
    return result;
}

void HkContactListener_SetCallbackLimit(void* instance, int32_t value) { EnsureThreadInfo();
    LOG_CALL(HkContactListener_SetCallbackLimit);
    REQUIRE_FUNCTION_POINTER(HkContactListener_SetCallbackLimit)
    pHkContactListener_SetCallbackLimit(instance, value);
}

void HkContactListener_ResetLimit(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkContactListener_ResetLimit);
    REQUIRE_FUNCTION_POINTER(HkContactListener_ResetLimit)
    pHkContactListener_ResetLimit(instance);
}

Vector3 HkContactPoint_GetPosition(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkContactPoint_GetPosition);
    REQUIRE_FUNCTION_POINTER(HkContactPoint_GetPosition)
    return pHkContactPoint_GetPosition(instance);
}

void HkContactPoint_SetPosition(void* instance, Vector3 value) { EnsureThreadInfo();
    LOG_CALL(HkContactPoint_SetPosition);
    REQUIRE_FUNCTION_POINTER(HkContactPoint_SetPosition)
    pHkContactPoint_SetPosition(instance, value);
}

Vector4 HkContactPoint_GetNormalAndDistance(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkContactPoint_GetNormalAndDistance);
    REQUIRE_FUNCTION_POINTER(HkContactPoint_GetNormalAndDistance)
    return pHkContactPoint_GetNormalAndDistance(instance);
}

void HkContactPoint_SetNormalAndDistance(void* instance, Vector4 value) { EnsureThreadInfo();
    LOG_CALL(HkContactPoint_SetNormalAndDistance);
    REQUIRE_FUNCTION_POINTER(HkContactPoint_SetNormalAndDistance)
    pHkContactPoint_SetNormalAndDistance(instance, value);
}

Vector3 HkContactPoint_GetNormal(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkContactPoint_GetNormal);
    REQUIRE_FUNCTION_POINTER(HkContactPoint_GetNormal)
    return pHkContactPoint_GetNormal(instance);
}

void HkContactPoint_SetNormal(void* instance, Vector3 value) { EnsureThreadInfo();
    LOG_CALL(HkContactPoint_SetNormal);
    REQUIRE_FUNCTION_POINTER(HkContactPoint_SetNormal)
    pHkContactPoint_SetNormal(instance, value);
}

float HkContactPoint_GetDistance(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkContactPoint_GetDistance);
    REQUIRE_FUNCTION_POINTER(HkContactPoint_GetDistance)
    return pHkContactPoint_GetDistance(instance);
}

void HkContactPoint_SetDistance(void* instance, float value) { EnsureThreadInfo();
    LOG_CALL(HkContactPoint_SetDistance);
    REQUIRE_FUNCTION_POINTER(HkContactPoint_SetDistance)
    pHkContactPoint_SetDistance(instance, value);
}

void HkContactPoint_Flip(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkContactPoint_Flip);
    REQUIRE_FUNCTION_POINTER(HkContactPoint_Flip)
    pHkContactPoint_Flip(instance);
}

void* HkContactPointEvent_GetBase(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkContactPointEvent_GetBase);
    REQUIRE_FUNCTION_POINTER(HkContactPointEvent_GetBase)
    return pHkContactPointEvent_GetBase(instance);
}

bool HkContactPointEvent_IsToi(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkContactPointEvent_IsToi);
    REQUIRE_FUNCTION_POINTER(HkContactPointEvent_IsToi)
    return pHkContactPointEvent_IsToi(instance);
}

float HkContactPointEvent_GetSeparatingVelocity(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkContactPointEvent_GetSeparatingVelocity);
    REQUIRE_FUNCTION_POINTER(HkContactPointEvent_GetSeparatingVelocity)
    return pHkContactPointEvent_GetSeparatingVelocity(instance);
}

void HkContactPointEvent_SetSeparatingVelocity(void* instance, float value) { EnsureThreadInfo();
    LOG_CALL(HkContactPointEvent_SetSeparatingVelocity);
    REQUIRE_FUNCTION_POINTER(HkContactPointEvent_SetSeparatingVelocity)
    pHkContactPointEvent_SetSeparatingVelocity(instance, value);
}

float HkContactPointEvent_GetRotateNormal(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkContactPointEvent_GetRotateNormal);
    REQUIRE_FUNCTION_POINTER(HkContactPointEvent_GetRotateNormal)
    return pHkContactPointEvent_GetRotateNormal(instance);
}

void HkContactPointEvent_SetRotateNormal(void* instance, float value) { EnsureThreadInfo();
    LOG_CALL(HkContactPointEvent_SetRotateNormal);
    REQUIRE_FUNCTION_POINTER(HkContactPointEvent_SetRotateNormal)
    pHkContactPointEvent_SetRotateNormal(instance, value);
}

int32_t HkContactPointEvent_GetEventType(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkContactPointEvent_GetEventType);
    REQUIRE_FUNCTION_POINTER(HkContactPointEvent_GetEventType)
    return pHkContactPointEvent_GetEventType(instance);
}

void* HkContactPointEvent_GetContactPoint(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkContactPointEvent_GetContactPoint);
    REQUIRE_FUNCTION_POINTER(HkContactPointEvent_GetContactPoint)
    return pHkContactPointEvent_GetContactPoint(instance);
}

void* HkContactPointEvent_GetContactProperties(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkContactPointEvent_GetContactProperties);
    REQUIRE_FUNCTION_POINTER(HkContactPointEvent_GetContactProperties)
    return pHkContactPointEvent_GetContactProperties(instance);
}

bool HkContactPointEvent_GetFiringCallbacksForFullManifold(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkContactPointEvent_GetFiringCallbacksForFullManifold);
    REQUIRE_FUNCTION_POINTER(HkContactPointEvent_GetFiringCallbacksForFullManifold)
    return pHkContactPointEvent_GetFiringCallbacksForFullManifold(instance);
}

bool HkContactPointEvent_GetFirstCallbackForFullManifold(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkContactPointEvent_GetFirstCallbackForFullManifold);
    REQUIRE_FUNCTION_POINTER(HkContactPointEvent_GetFirstCallbackForFullManifold)
    return pHkContactPointEvent_GetFirstCallbackForFullManifold(instance);
}

bool HkContactPointEvent_GetLastCallbackForFullManifold(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkContactPointEvent_GetLastCallbackForFullManifold);
    REQUIRE_FUNCTION_POINTER(HkContactPointEvent_GetLastCallbackForFullManifold)
    return pHkContactPointEvent_GetLastCallbackForFullManifold(instance);
}

uint16_t HkContactPointEvent_GetContactPointId(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkContactPointEvent_GetContactPointId);
    REQUIRE_FUNCTION_POINTER(HkContactPointEvent_GetContactPointId)
    return pHkContactPointEvent_GetContactPointId(instance);
}

void HkContactPointEvent_AccessVelocities(void* instance, int32_t bodyIndex) { EnsureThreadInfo();
    LOG_CALL(HkContactPointEvent_AccessVelocities);
    REQUIRE_FUNCTION_POINTER(HkContactPointEvent_AccessVelocities)
    pHkContactPointEvent_AccessVelocities(instance, bodyIndex);
}

void HkContactPointEvent_UpdateVelocities(void* instance, int32_t bodyIndex) { EnsureThreadInfo();
    LOG_CALL(HkContactPointEvent_UpdateVelocities);
    REQUIRE_FUNCTION_POINTER(HkContactPointEvent_UpdateVelocities)
    pHkContactPointEvent_UpdateVelocities(instance, bodyIndex);
}

uint32_t HkContactPointEvent_GetShapeKey(void* instance, int32_t bodyIdx) { EnsureThreadInfo();
    LOG_CALL(HkContactPointEvent_GetShapeKey);
    REQUIRE_FUNCTION_POINTER(HkContactPointEvent_GetShapeKey)
    return pHkContactPointEvent_GetShapeKey(instance, bodyIdx);
}

uint32_t HkContactPointEvent_GetShapeKeyWithShapeID(void* instance, int32_t bodyIdx, int32_t shapeIdx) { EnsureThreadInfo();
    LOG_CALL(HkContactPointEvent_GetShapeKeyWithShapeID);
    REQUIRE_FUNCTION_POINTER(HkContactPointEvent_GetShapeKeyWithShapeID)
    return pHkContactPointEvent_GetShapeKeyWithShapeID(instance, bodyIdx, shapeIdx);
}

void HkContactPointEvent_GetFieldOffsets(void* separatingVelocityOffset, void* typeOffset, void* propertiesOffset, void* contactPointOffset, void* firingCallbacksForFullManifoldOffset, void* firstCallbackForFullManifoldOffset, void* lastCallbackForFullManifoldOffset) { EnsureThreadInfo();
    LOG_CALL(HkContactPointEvent_GetFieldOffsets);
    REQUIRE_FUNCTION_POINTER(HkContactPointEvent_GetFieldOffsets)
    pHkContactPointEvent_GetFieldOffsets(separatingVelocityOffset, typeOffset, propertiesOffset, contactPointOffset, firingCallbacksForFullManifoldOffset, firstCallbackForFullManifoldOffset, lastCallbackForFullManifoldOffset);
}

float HkContactPointProperties_GetImpulseApplied(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkContactPointProperties_GetImpulseApplied);
    REQUIRE_FUNCTION_POINTER(HkContactPointProperties_GetImpulseApplied)
    return pHkContactPointProperties_GetImpulseApplied(instance);
}

float HkContactPointProperties_GetInternalSolverData(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkContactPointProperties_GetInternalSolverData);
    REQUIRE_FUNCTION_POINTER(HkContactPointProperties_GetInternalSolverData)
    return pHkContactPointProperties_GetInternalSolverData(instance);
}

bool HkContactPointProperties_WasUsed(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkContactPointProperties_WasUsed);
    REQUIRE_FUNCTION_POINTER(HkContactPointProperties_WasUsed)
    return pHkContactPointProperties_WasUsed(instance);
}

float HkContactPointProperties_GetFriction(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkContactPointProperties_GetFriction);
    REQUIRE_FUNCTION_POINTER(HkContactPointProperties_GetFriction)
    return pHkContactPointProperties_GetFriction(instance);
}

void HkContactPointProperties_SetFriction(void* instance, float value) { EnsureThreadInfo();
    LOG_CALL(HkContactPointProperties_SetFriction);
    REQUIRE_FUNCTION_POINTER(HkContactPointProperties_SetFriction)
    pHkContactPointProperties_SetFriction(instance, value);
}

float HkContactPointProperties_GetRestitution(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkContactPointProperties_GetRestitution);
    REQUIRE_FUNCTION_POINTER(HkContactPointProperties_GetRestitution)
    return pHkContactPointProperties_GetRestitution(instance);
}

void HkContactPointProperties_SetRestitution(void* instance, float value) { EnsureThreadInfo();
    LOG_CALL(HkContactPointProperties_SetRestitution);
    REQUIRE_FUNCTION_POINTER(HkContactPointProperties_SetRestitution)
    pHkContactPointProperties_SetRestitution(instance, value);
}

bool HkContactPointProperties_IsPotential(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkContactPointProperties_IsPotential);
    REQUIRE_FUNCTION_POINTER(HkContactPointProperties_IsPotential)
    return pHkContactPointProperties_IsPotential(instance);
}

float HkContactPointProperties_GetMaxImpulsePerStep(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkContactPointProperties_GetMaxImpulsePerStep);
    REQUIRE_FUNCTION_POINTER(HkContactPointProperties_GetMaxImpulsePerStep)
    return pHkContactPointProperties_GetMaxImpulsePerStep(instance);
}

void HkContactPointProperties_SetMaxImpulsePerStep(void* instance, float value) { EnsureThreadInfo();
    LOG_CALL(HkContactPointProperties_SetMaxImpulsePerStep);
    REQUIRE_FUNCTION_POINTER(HkContactPointProperties_SetMaxImpulsePerStep)
    pHkContactPointProperties_SetMaxImpulsePerStep(instance, value);
}

float HkContactPointProperties_GetMaxImpulse(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkContactPointProperties_GetMaxImpulse);
    REQUIRE_FUNCTION_POINTER(HkContactPointProperties_GetMaxImpulse)
    return pHkContactPointProperties_GetMaxImpulse(instance);
}

void HkContactPointProperties_SetMaxImpulse(void* instance, float value) { EnsureThreadInfo();
    LOG_CALL(HkContactPointProperties_SetMaxImpulse);
    REQUIRE_FUNCTION_POINTER(HkContactPointProperties_SetMaxImpulse)
    pHkContactPointProperties_SetMaxImpulse(instance, value);
}

bool HkContactPointProperties_GetIsDisabled(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkContactPointProperties_GetIsDisabled);
    REQUIRE_FUNCTION_POINTER(HkContactPointProperties_GetIsDisabled)
    return pHkContactPointProperties_GetIsDisabled(instance);
}

void HkContactPointProperties_SetIsDisabled(void* instance, bool value) { EnsureThreadInfo();
    LOG_CALL(HkContactPointProperties_SetIsDisabled);
    REQUIRE_FUNCTION_POINTER(HkContactPointProperties_SetIsDisabled)
    pHkContactPointProperties_SetIsDisabled(instance, value);
}

bool HkContactPointProperties_GetIsNew(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkContactPointProperties_GetIsNew);
    REQUIRE_FUNCTION_POINTER(HkContactPointProperties_GetIsNew)
    return pHkContactPointProperties_GetIsNew(instance);
}

void HkContactPointProperties_SetIsNew(void* instance, bool value) { EnsureThreadInfo();
    LOG_CALL(HkContactPointProperties_SetIsNew);
    REQUIRE_FUNCTION_POINTER(HkContactPointProperties_SetIsNew)
    pHkContactPointProperties_SetIsNew(instance, value);
}

uint32_t HkContactPointProperties_GetUserData(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkContactPointProperties_GetUserData);
    REQUIRE_FUNCTION_POINTER(HkContactPointProperties_GetUserData)
    return pHkContactPointProperties_GetUserData(instance);
}

void HkContactPointProperties_SetUserData(void* instance, uint32_t value) { EnsureThreadInfo();
    LOG_CALL(HkContactPointProperties_SetUserData);
    REQUIRE_FUNCTION_POINTER(HkContactPointProperties_SetUserData)
    pHkContactPointProperties_SetUserData(instance, value);
}

void HkContactPointProperties_GetFieldOffsets(void* userDataOffset) { EnsureThreadInfo();
    LOG_CALL(HkContactPointProperties_GetFieldOffsets);
    REQUIRE_FUNCTION_POINTER(HkContactPointProperties_GetFieldOffsets)
    pHkContactPointProperties_GetFieldOffsets(userDataOffset);
}

void* HkContactSoundListener_Create(void* onContact) { EnsureThreadInfo();
    LOG_CALL(HkContactSoundListener_Create);
    REQUIRE_FUNCTION_POINTER(HkContactSoundListener_Create)
    auto result = pHkContactSoundListener_Create(bridge_void_ptr_ptr(onContact));
    register_callback_owner(result, {callback_owner_binding{&release_void_ptr_ptr, onContact}});
    return result;
}

void* HkConvexShape_GetConvexShapeFromCompoundShape(void* shape, int32_t shapeIndex) { EnsureThreadInfo();
    LOG_CALL(HkConvexShape_GetConvexShapeFromCompoundShape);
    REQUIRE_FUNCTION_POINTER(HkConvexShape_GetConvexShapeFromCompoundShape)
    return pHkConvexShape_GetConvexShapeFromCompoundShape(shape, shapeIndex);
}

float HkConvexShape_GetConvexRadius(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkConvexShape_GetConvexRadius);
    REQUIRE_FUNCTION_POINTER(HkConvexShape_GetConvexRadius)
    return pHkConvexShape_GetConvexRadius(instance);
}

void HkConvexShape_SetConvexRadius(void* instance, float value) { EnsureThreadInfo();
    LOG_CALL(HkConvexShape_SetConvexRadius);
    REQUIRE_FUNCTION_POINTER(HkConvexShape_SetConvexRadius)
    pHkConvexShape_SetConvexRadius(instance, value);
}

float HkConvexShape_GetDefaultConvexRadius(void) { EnsureThreadInfo();
    LOG_CALL(HkConvexShape_GetDefaultConvexRadius);
    REQUIRE_FUNCTION_POINTER(HkConvexShape_GetDefaultConvexRadius)
    return pHkConvexShape_GetDefaultConvexRadius();
}

void* HkConvexTransformShape_Create(void* childShape, Matrix transform, int32_t refPolicy) { EnsureThreadInfo();
    LOG_CALL(HkConvexTransformShape_Create);
    REQUIRE_FUNCTION_POINTER(HkConvexTransformShape_Create)
    return pHkConvexTransformShape_Create(childShape, transform, refPolicy);
}

void* HkConvexTransformShape_CreateTranslated(void* childShape, Vector3 translation, Quaternion rotation, Vector3 scale, int32_t refPolicy) { EnsureThreadInfo();
    LOG_CALL(HkConvexTransformShape_CreateTranslated);
    REQUIRE_FUNCTION_POINTER(HkConvexTransformShape_CreateTranslated)
    return pHkConvexTransformShape_CreateTranslated(childShape, translation, rotation, scale, refPolicy);
}

void* HkConvexTransformShape_GetChildShape(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkConvexTransformShape_GetChildShape);
    REQUIRE_FUNCTION_POINTER(HkConvexTransformShape_GetChildShape)
    return pHkConvexTransformShape_GetChildShape(instance);
}

Matrix HkConvexTransformShape_GetTransform(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkConvexTransformShape_GetTransform);
    REQUIRE_FUNCTION_POINTER(HkConvexTransformShape_GetTransform)
    return pHkConvexTransformShape_GetTransform(instance);
}

void* HkConvexTranslateShape_CreateWithChild(void* childShape, Vector3 translation, int32_t refPolicy) { EnsureThreadInfo();
    LOG_CALL(HkConvexTranslateShape_CreateWithChild);
    REQUIRE_FUNCTION_POINTER(HkConvexTranslateShape_CreateWithChild)
    return pHkConvexTranslateShape_CreateWithChild(childShape, translation, refPolicy);
}

void* HkConvexTranslateShape_GetChildShape(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkConvexTranslateShape_GetChildShape);
    REQUIRE_FUNCTION_POINTER(HkConvexTranslateShape_GetChildShape)
    return pHkConvexTranslateShape_GetChildShape(instance);
}

Vector3 HkConvexTranslateShape_GetTranslation(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkConvexTranslateShape_GetTranslation);
    REQUIRE_FUNCTION_POINTER(HkConvexTranslateShape_GetTranslation)
    return pHkConvexTranslateShape_GetTranslation(instance);
}

void* HkConvexVerticesShape_Create(void* verts, int32_t count) { EnsureThreadInfo();
    LOG_CALL(HkConvexVerticesShape_Create);
    REQUIRE_FUNCTION_POINTER(HkConvexVerticesShape_Create)
    return pHkConvexVerticesShape_Create(verts, count);
}

void* HkConvexVerticesShape_CreateWithRadius(void* verts, int32_t count, bool shrink, float convexRadius) { EnsureThreadInfo();
    LOG_CALL(HkConvexVerticesShape_CreateWithRadius);
    REQUIRE_FUNCTION_POINTER(HkConvexVerticesShape_CreateWithRadius)
    return pHkConvexVerticesShape_CreateWithRadius(verts, count, shrink, convexRadius);
}

Vector3 HkConvexVerticesShape_GetCenter(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkConvexVerticesShape_GetCenter);
    REQUIRE_FUNCTION_POINTER(HkConvexVerticesShape_GetCenter)
    return pHkConvexVerticesShape_GetCenter(instance);
}

int32_t HkConvexVerticesShape_GetVertexCount(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkConvexVerticesShape_GetVertexCount);
    REQUIRE_FUNCTION_POINTER(HkConvexVerticesShape_GetVertexCount)
    return pHkConvexVerticesShape_GetVertexCount(instance);
}

int32_t HkConvexVerticesShape_GetFaceCount(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkConvexVerticesShape_GetFaceCount);
    REQUIRE_FUNCTION_POINTER(HkConvexVerticesShape_GetFaceCount)
    return pHkConvexVerticesShape_GetFaceCount(instance);
}

void HkConvexVerticesShape_GetFaces(void* instance, void* faceIndexCount, void* faceIndices, void* faceCount, void* faceVertexCounts) { EnsureThreadInfo();
    LOG_CALL(HkConvexVerticesShape_GetFaces);
    REQUIRE_FUNCTION_POINTER(HkConvexVerticesShape_GetFaces)
    pHkConvexVerticesShape_GetFaces(instance, faceIndexCount, faceIndices, faceCount, faceVertexCounts);
}

void HkConvexVerticesShape_GetVertices(void* instance, void* vertexBuffer) { EnsureThreadInfo();
    LOG_CALL(HkConvexVerticesShape_GetVertices);
    REQUIRE_FUNCTION_POINTER(HkConvexVerticesShape_GetVertices)
    pHkConvexVerticesShape_GetVertices(instance, vertexBuffer);
}

void HkConvexVerticesShape_GetGeometry(void* instance, void* geometry, void* center) { EnsureThreadInfo();
    LOG_CALL(HkConvexVerticesShape_GetGeometry);
    REQUIRE_FUNCTION_POINTER(HkConvexVerticesShape_GetGeometry)
    pHkConvexVerticesShape_GetGeometry(instance, geometry, center);
}

void* HkCustomWheelConstraintData_Create(void) { EnsureThreadInfo();
    LOG_CALL(HkCustomWheelConstraintData_Create);
    REQUIRE_FUNCTION_POINTER(HkCustomWheelConstraintData_Create)
    return pHkCustomWheelConstraintData_Create();
}

bool HkCustomWheelConstraintData_GetLimitsEnabled(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkCustomWheelConstraintData_GetLimitsEnabled);
    REQUIRE_FUNCTION_POINTER(HkCustomWheelConstraintData_GetLimitsEnabled)
    return pHkCustomWheelConstraintData_GetLimitsEnabled(instance);
}

void HkCustomWheelConstraintData_SetLimitsEnabled(void* instance, bool value) { EnsureThreadInfo();
    LOG_CALL(HkCustomWheelConstraintData_SetLimitsEnabled);
    REQUIRE_FUNCTION_POINTER(HkCustomWheelConstraintData_SetLimitsEnabled)
    pHkCustomWheelConstraintData_SetLimitsEnabled(instance, value);
}

float HkCustomWheelConstraintData_GetSuspensionMinLimit(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkCustomWheelConstraintData_GetSuspensionMinLimit);
    REQUIRE_FUNCTION_POINTER(HkCustomWheelConstraintData_GetSuspensionMinLimit)
    return pHkCustomWheelConstraintData_GetSuspensionMinLimit(instance);
}

void HkCustomWheelConstraintData_SetSuspensionMinLimit(void* instance, float value) { EnsureThreadInfo();
    LOG_CALL(HkCustomWheelConstraintData_SetSuspensionMinLimit);
    REQUIRE_FUNCTION_POINTER(HkCustomWheelConstraintData_SetSuspensionMinLimit)
    pHkCustomWheelConstraintData_SetSuspensionMinLimit(instance, value);
}

float HkCustomWheelConstraintData_GetSuspensionMaxLimit(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkCustomWheelConstraintData_GetSuspensionMaxLimit);
    REQUIRE_FUNCTION_POINTER(HkCustomWheelConstraintData_GetSuspensionMaxLimit)
    return pHkCustomWheelConstraintData_GetSuspensionMaxLimit(instance);
}

void HkCustomWheelConstraintData_SetSuspensionMaxLimit(void* instance, float value) { EnsureThreadInfo();
    LOG_CALL(HkCustomWheelConstraintData_SetSuspensionMaxLimit);
    REQUIRE_FUNCTION_POINTER(HkCustomWheelConstraintData_SetSuspensionMaxLimit)
    pHkCustomWheelConstraintData_SetSuspensionMaxLimit(instance, value);
}

bool HkCustomWheelConstraintData_GetFrictionEnabled(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkCustomWheelConstraintData_GetFrictionEnabled);
    REQUIRE_FUNCTION_POINTER(HkCustomWheelConstraintData_GetFrictionEnabled)
    return pHkCustomWheelConstraintData_GetFrictionEnabled(instance);
}

void HkCustomWheelConstraintData_SetFrictionEnabled(void* instance, bool value) { EnsureThreadInfo();
    LOG_CALL(HkCustomWheelConstraintData_SetFrictionEnabled);
    REQUIRE_FUNCTION_POINTER(HkCustomWheelConstraintData_SetFrictionEnabled)
    pHkCustomWheelConstraintData_SetFrictionEnabled(instance, value);
}

float HkCustomWheelConstraintData_GetMaxFrictionTorque(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkCustomWheelConstraintData_GetMaxFrictionTorque);
    REQUIRE_FUNCTION_POINTER(HkCustomWheelConstraintData_GetMaxFrictionTorque)
    return pHkCustomWheelConstraintData_GetMaxFrictionTorque(instance);
}

void HkCustomWheelConstraintData_SetMaxFrictionTorque(void* instance, float value) { EnsureThreadInfo();
    LOG_CALL(HkCustomWheelConstraintData_SetMaxFrictionTorque);
    REQUIRE_FUNCTION_POINTER(HkCustomWheelConstraintData_SetMaxFrictionTorque)
    pHkCustomWheelConstraintData_SetMaxFrictionTorque(instance, value);
}

void HkCustomWheelConstraintData_SetInBodySpaceInternal(void* instance, Vector3 pivotA, Vector3 pivotB, Vector3 axleA, Vector3 axleB, Vector3 suspensionAxisB, Vector3 steeringAxisB) { EnsureThreadInfo();
    LOG_CALL(HkCustomWheelConstraintData_SetInBodySpaceInternal);
    REQUIRE_FUNCTION_POINTER(HkCustomWheelConstraintData_SetInBodySpaceInternal)
    pHkCustomWheelConstraintData_SetInBodySpaceInternal(instance, pivotA, pivotB, axleA, axleB, suspensionAxisB, steeringAxisB);
}

void HkCustomWheelConstraintData_SetSuspensionStrength(void* instance, float value) { EnsureThreadInfo();
    LOG_CALL(HkCustomWheelConstraintData_SetSuspensionStrength);
    REQUIRE_FUNCTION_POINTER(HkCustomWheelConstraintData_SetSuspensionStrength)
    pHkCustomWheelConstraintData_SetSuspensionStrength(instance, value);
}

void HkCustomWheelConstraintData_SetSuspensionDamping(void* instance, float value) { EnsureThreadInfo();
    LOG_CALL(HkCustomWheelConstraintData_SetSuspensionDamping);
    REQUIRE_FUNCTION_POINTER(HkCustomWheelConstraintData_SetSuspensionDamping)
    pHkCustomWheelConstraintData_SetSuspensionDamping(instance, value);
}

void HkCustomWheelConstraintData_SetSteeringAngle(void* instance, float value) { EnsureThreadInfo();
    LOG_CALL(HkCustomWheelConstraintData_SetSteeringAngle);
    REQUIRE_FUNCTION_POINTER(HkCustomWheelConstraintData_SetSteeringAngle)
    pHkCustomWheelConstraintData_SetSteeringAngle(instance, value);
}

void HkCustomWheelConstraintData_SetAngleLimits(void* instance, float min, float max) { EnsureThreadInfo();
    LOG_CALL(HkCustomWheelConstraintData_SetAngleLimits);
    REQUIRE_FUNCTION_POINTER(HkCustomWheelConstraintData_SetAngleLimits)
    pHkCustomWheelConstraintData_SetAngleLimits(instance, min, max);
}

float HkCustomWheelConstraintData_GetAngleLimitsMin(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkCustomWheelConstraintData_GetAngleLimitsMin);
    REQUIRE_FUNCTION_POINTER(HkCustomWheelConstraintData_GetAngleLimitsMin)
    return pHkCustomWheelConstraintData_GetAngleLimitsMin(instance);
}

float HkCustomWheelConstraintData_GetAngleLimitsMax(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkCustomWheelConstraintData_GetAngleLimitsMax);
    REQUIRE_FUNCTION_POINTER(HkCustomWheelConstraintData_GetAngleLimitsMax)
    return pHkCustomWheelConstraintData_GetAngleLimitsMax(instance);
}

void HkCustomWheelConstraintData_DisableLimits(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkCustomWheelConstraintData_DisableLimits);
    REQUIRE_FUNCTION_POINTER(HkCustomWheelConstraintData_DisableLimits)
    pHkCustomWheelConstraintData_DisableLimits(instance);
}

float HkCustomWheelConstraintData_GetCurrentAngle(void* constraint) { EnsureThreadInfo();
    LOG_CALL(HkCustomWheelConstraintData_GetCurrentAngle);
    REQUIRE_FUNCTION_POINTER(HkCustomWheelConstraintData_GetCurrentAngle)
    return pHkCustomWheelConstraintData_GetCurrentAngle(constraint);
}

void HkCustomWheelConstraintData_SetCurrentAngle(void* constraint, float angle) { EnsureThreadInfo();
    LOG_CALL(HkCustomWheelConstraintData_SetCurrentAngle);
    REQUIRE_FUNCTION_POINTER(HkCustomWheelConstraintData_SetCurrentAngle)
    pHkCustomWheelConstraintData_SetCurrentAngle(constraint, angle);
}

void* HkCylinderShape_Create(Vector3 vertexA, Vector3 vertexB, float cylinderRadius) { EnsureThreadInfo();
    LOG_CALL(HkCylinderShape_Create);
    REQUIRE_FUNCTION_POINTER(HkCylinderShape_Create)
    return pHkCylinderShape_Create(vertexA, vertexB, cylinderRadius);
}

void* HkCylinderShape_CreateWithConvexRadius(Vector3 vertexA, Vector3 vertexB, float cylinderRadius, float convexRadius) { EnsureThreadInfo();
    LOG_CALL(HkCylinderShape_CreateWithConvexRadius);
    REQUIRE_FUNCTION_POINTER(HkCylinderShape_CreateWithConvexRadius)
    return pHkCylinderShape_CreateWithConvexRadius(vertexA, vertexB, cylinderRadius, convexRadius);
}

Vector3 HkCylinderShape_GetVertexB(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkCylinderShape_GetVertexB);
    REQUIRE_FUNCTION_POINTER(HkCylinderShape_GetVertexB)
    return pHkCylinderShape_GetVertexB(instance);
}

Vector3 HkCylinderShape_GetVertexA(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkCylinderShape_GetVertexA);
    REQUIRE_FUNCTION_POINTER(HkCylinderShape_GetVertexA)
    return pHkCylinderShape_GetVertexA(instance);
}

void HkCylinderShape_SetVertexB(void* instance, Vector3 vertex) { EnsureThreadInfo();
    LOG_CALL(HkCylinderShape_SetVertexB);
    REQUIRE_FUNCTION_POINTER(HkCylinderShape_SetVertexB)
    pHkCylinderShape_SetVertexB(instance, vertex);
}

void HkCylinderShape_SetVertexA(void* instance, Vector3 vertex) { EnsureThreadInfo();
    LOG_CALL(HkCylinderShape_SetVertexA);
    REQUIRE_FUNCTION_POINTER(HkCylinderShape_SetVertexA)
    pHkCylinderShape_SetVertexA(instance, vertex);
}

float HkCylinderShape_GetRadius(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkCylinderShape_GetRadius);
    REQUIRE_FUNCTION_POINTER(HkCylinderShape_GetRadius)
    return pHkCylinderShape_GetRadius(instance);
}

void HkCylinderShape_SetRadius(void* instance, float radius) { EnsureThreadInfo();
    LOG_CALL(HkCylinderShape_SetRadius);
    REQUIRE_FUNCTION_POINTER(HkCylinderShape_SetRadius)
    pHkCylinderShape_SetRadius(instance, radius);
}

void HkCylinderShape_SetNumberOfVirtualSideSegments(int32_t number) { EnsureThreadInfo();
    LOG_CALL(HkCylinderShape_SetNumberOfVirtualSideSegments);
    REQUIRE_FUNCTION_POINTER(HkCylinderShape_SetNumberOfVirtualSideSegments)
    pHkCylinderShape_SetNumberOfVirtualSideSegments(number);
}

void* HkEasePenetrationAction_Create(void* body, float duration) { EnsureThreadInfo();
    LOG_CALL(HkEasePenetrationAction_Create);
    REQUIRE_FUNCTION_POINTER(HkEasePenetrationAction_Create)
    return pHkEasePenetrationAction_Create(body, duration);
}

float HkEasePenetrationAction_GetInitialAdditionalAllowedPenetrationDepth(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkEasePenetrationAction_GetInitialAdditionalAllowedPenetrationDepth);
    REQUIRE_FUNCTION_POINTER(HkEasePenetrationAction_GetInitialAdditionalAllowedPenetrationDepth)
    return pHkEasePenetrationAction_GetInitialAdditionalAllowedPenetrationDepth(instance);
}

void HkEasePenetrationAction_SetInitialAdditionalAllowedPenetrationDepth(void* instance, float initialAdditionalAllowedPenetrationDepth) { EnsureThreadInfo();
    LOG_CALL(HkEasePenetrationAction_SetInitialAdditionalAllowedPenetrationDepth);
    REQUIRE_FUNCTION_POINTER(HkEasePenetrationAction_SetInitialAdditionalAllowedPenetrationDepth)
    pHkEasePenetrationAction_SetInitialAdditionalAllowedPenetrationDepth(instance, initialAdditionalAllowedPenetrationDepth);
}

float HkEasePenetrationAction_GetInitialAllowedPenetrationDepthMultiplier(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkEasePenetrationAction_GetInitialAllowedPenetrationDepthMultiplier);
    REQUIRE_FUNCTION_POINTER(HkEasePenetrationAction_GetInitialAllowedPenetrationDepthMultiplier)
    return pHkEasePenetrationAction_GetInitialAllowedPenetrationDepthMultiplier(instance);
}

void HkEasePenetrationAction_SetInitialAllowedPenetrationDepthMultiplier(void* instance, float initialAllowedPenetrationDepthMultiplier) { EnsureThreadInfo();
    LOG_CALL(HkEasePenetrationAction_SetInitialAllowedPenetrationDepthMultiplier);
    REQUIRE_FUNCTION_POINTER(HkEasePenetrationAction_SetInitialAllowedPenetrationDepthMultiplier)
    pHkEasePenetrationAction_SetInitialAllowedPenetrationDepthMultiplier(instance, initialAllowedPenetrationDepthMultiplier);
}

void HkEntity_AddActivationListener(void* instance, void* listener) { EnsureThreadInfo();
    LOG_CALL(HkEntity_AddActivationListener);
    REQUIRE_FUNCTION_POINTER(HkEntity_AddActivationListener)
    pHkEntity_AddActivationListener(instance, listener);
}

void HkEntity_RemoveActivationListener(void* instance, void* listener) { EnsureThreadInfo();
    LOG_CALL(HkEntity_RemoveActivationListener);
    REQUIRE_FUNCTION_POINTER(HkEntity_RemoveActivationListener)
    pHkEntity_RemoveActivationListener(instance, listener);
}

void HKEntity_AddEntityListener(void* instance, void* listener) { EnsureThreadInfo();
    LOG_CALL(HKEntity_AddEntityListener);
    REQUIRE_FUNCTION_POINTER(HKEntity_AddEntityListener)
    pHKEntity_AddEntityListener(instance, listener);
}

void HKEntity_RemoveEntityListener(void* instance, void* listener) { EnsureThreadInfo();
    LOG_CALL(HKEntity_RemoveEntityListener);
    REQUIRE_FUNCTION_POINTER(HKEntity_RemoveEntityListener)
    pHKEntity_RemoveEntityListener(instance, listener);
}

void HkEntity_SetContactListener(void* instance, void* listener, bool value) { EnsureThreadInfo();
    LOG_CALL(HkEntity_SetContactListener);
    REQUIRE_FUNCTION_POINTER(HkEntity_SetContactListener)
    pHkEntity_SetContactListener(instance, listener, value);
}

int32_t HkEntity_GetQuality(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkEntity_GetQuality);
    REQUIRE_FUNCTION_POINTER(HkEntity_GetQuality)
    return pHkEntity_GetQuality(instance);
}

void HkEntity_SetQuality(void* instance, int32_t value) { EnsureThreadInfo();
    LOG_CALL(HkEntity_SetQuality);
    REQUIRE_FUNCTION_POINTER(HkEntity_SetQuality)
    pHkEntity_SetQuality(instance, value);
}

bool HkEntity_IsFixed(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkEntity_IsFixed);
    REQUIRE_FUNCTION_POINTER(HkEntity_IsFixed)
    return pHkEntity_IsFixed(instance);
}

bool HkEntity_IsFixedOrKeyframed(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkEntity_IsFixedOrKeyframed);
    REQUIRE_FUNCTION_POINTER(HkEntity_IsFixedOrKeyframed)
    return pHkEntity_IsFixedOrKeyframed(instance);
}

int32_t HkRigidBody_GetMotionType(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkRigidBody_GetMotionType);
    REQUIRE_FUNCTION_POINTER(HkRigidBody_GetMotionType)
    return pHkRigidBody_GetMotionType(instance);
}

int32_t HkEntity_GetContactPointCallbackDelay(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkEntity_GetContactPointCallbackDelay);
    REQUIRE_FUNCTION_POINTER(HkEntity_GetContactPointCallbackDelay)
    return pHkEntity_GetContactPointCallbackDelay(instance);
}

void HkEntity_SetContactPointCallbackDelay(void* instance, int32_t value) { EnsureThreadInfo();
    LOG_CALL(HkEntity_SetContactPointCallbackDelay);
    REQUIRE_FUNCTION_POINTER(HkEntity_SetContactPointCallbackDelay)
    pHkEntity_SetContactPointCallbackDelay(instance, value);
}

void HkEntity_SetProperty(void* instance, int32_t key, float v) { EnsureThreadInfo();
    LOG_CALL(HkEntity_SetProperty);
    REQUIRE_FUNCTION_POINTER(HkEntity_SetProperty)
    pHkEntity_SetProperty(instance, key, v);
}

bool HkEntity_HasProperty(void* instance, int32_t key) { EnsureThreadInfo();
    LOG_CALL(HkEntity_HasProperty);
    REQUIRE_FUNCTION_POINTER(HkEntity_HasProperty)
    return pHkEntity_HasProperty(instance, key);
}

void HkEntity_RemoveProperty(void* instance, int32_t key) { EnsureThreadInfo();
    LOG_CALL(HkEntity_RemoveProperty);
    REQUIRE_FUNCTION_POINTER(HkEntity_RemoveProperty)
    pHkEntity_RemoveProperty(instance, key);
}

Quaternion HkRigidBody_GetRotation(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkRigidBody_GetRotation);
    REQUIRE_FUNCTION_POINTER(HkRigidBody_GetRotation)
    return pHkRigidBody_GetRotation(instance);
}

void HkRigidBody_SetRotation(void* instance, Quaternion value) { EnsureThreadInfo();
    LOG_CALL(HkRigidBody_SetRotation);
    REQUIRE_FUNCTION_POINTER(HkRigidBody_SetRotation)
    pHkRigidBody_SetRotation(instance, value);
}

Vector3 HkRigidBody_GetPosition(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkRigidBody_GetPosition);
    REQUIRE_FUNCTION_POINTER(HkRigidBody_GetPosition)
    return pHkRigidBody_GetPosition(instance);
}

void HkRigidBody_SetPosition(void* instance, Vector3 value) { EnsureThreadInfo();
    LOG_CALL(HkRigidBody_SetPosition);
    REQUIRE_FUNCTION_POINTER(HkRigidBody_SetPosition)
    pHkRigidBody_SetPosition(instance, value);
}

void HkRigidBody_Activate(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkRigidBody_Activate);
    REQUIRE_FUNCTION_POINTER(HkRigidBody_Activate)
    pHkRigidBody_Activate(instance);
}

void HkRigidBody_ActivateAsCriticalOperation(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkRigidBody_ActivateAsCriticalOperation);
    REQUIRE_FUNCTION_POINTER(HkRigidBody_ActivateAsCriticalOperation)
    pHkRigidBody_ActivateAsCriticalOperation(instance);
}

void HkRigidBody_Deactivate(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkRigidBody_Deactivate);
    REQUIRE_FUNCTION_POINTER(HkRigidBody_Deactivate)
    pHkRigidBody_Deactivate(instance);
}

void HkRigidBody_UpdateMotionType(void* instance, int32_t type) { EnsureThreadInfo();
    LOG_CALL(HkRigidBody_UpdateMotionType);
    REQUIRE_FUNCTION_POINTER(HkRigidBody_UpdateMotionType)
    pHkRigidBody_UpdateMotionType(instance, type);
}

bool HkRigidBody_GetIsActive(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkRigidBody_GetIsActive);
    REQUIRE_FUNCTION_POINTER(HkRigidBody_GetIsActive)
    return pHkRigidBody_GetIsActive(instance);
}

void HkRigidBody_RequestDeactivation(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkRigidBody_RequestDeactivation);
    REQUIRE_FUNCTION_POINTER(HkRigidBody_RequestDeactivation)
    pHkRigidBody_RequestDeactivation(instance);
}

Vector3 HkRigidBody_GetLinearVelocity(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkRigidBody_GetLinearVelocity);
    REQUIRE_FUNCTION_POINTER(HkRigidBody_GetLinearVelocity)
    return pHkRigidBody_GetLinearVelocity(instance);
}

void HkRigidBody_SetLinearVelocity(void* instance, Vector3 value) { EnsureThreadInfo();
    LOG_CALL(HkRigidBody_SetLinearVelocity);
    REQUIRE_FUNCTION_POINTER(HkRigidBody_SetLinearVelocity)
    pHkRigidBody_SetLinearVelocity(instance, value);
}

Vector3 HkRigidBody_GetAngularVelocity(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkRigidBody_GetAngularVelocity);
    REQUIRE_FUNCTION_POINTER(HkRigidBody_GetAngularVelocity)
    return pHkRigidBody_GetAngularVelocity(instance);
}

void HkRigidBody_SetAngularVelocity(void* instance, Vector3 value) { EnsureThreadInfo();
    LOG_CALL(HkRigidBody_SetAngularVelocity);
    REQUIRE_FUNCTION_POINTER(HkRigidBody_SetAngularVelocity)
    pHkRigidBody_SetAngularVelocity(instance, value);
}

void HkEntity_GetFieldOffsets(void* userDataOffset, void* transformOffset, void* rotationOffset, void* linearVelocityOffset, void* angularVelocityOffset, void* motionTypeOffset, void* simulationIslandOffset, void* worldOffset) { EnsureThreadInfo();
    LOG_CALL(HkEntity_GetFieldOffsets);
    REQUIRE_FUNCTION_POINTER(HkEntity_GetFieldOffsets)
    pHkEntity_GetFieldOffsets(userDataOffset, transformOffset, rotationOffset, linearVelocityOffset, angularVelocityOffset, motionTypeOffset, simulationIslandOffset, worldOffset);
}

void* HkEntityListener_Create(void* onAdd, void* onRemove, void* onDelete, void* onShapeChange, void* onMotionTypeChange) { EnsureThreadInfo();
    LOG_CALL(HkEntityListener_Create);
    REQUIRE_FUNCTION_POINTER(HkEntityListener_Create)
    auto result = pHkEntityListener_Create(bridge_void_ptr_ptr(onAdd), bridge_void_ptr_ptr(onRemove), bridge_void_ptr_ptr(onDelete), bridge_void_ptr_ptr(onShapeChange), bridge_void_ptr_ptr(onMotionTypeChange));
    register_callback_owner(result, {callback_owner_binding{&release_void_ptr_ptr, onAdd}, callback_owner_binding{&release_void_ptr_ptr, onRemove}, callback_owner_binding{&release_void_ptr_ptr, onDelete}, callback_owner_binding{&release_void_ptr_ptr, onShapeChange}, callback_owner_binding{&release_void_ptr_ptr, onMotionTypeChange}});
    return result;
}

void HkEntityListener_Release(void* entityListener) { EnsureThreadInfo();
    LOG_CALL(HkEntityListener_Release);
    REQUIRE_FUNCTION_POINTER(HkEntityListener_Release)
    pHkEntityListener_Release(entityListener);
    release_callback_owner(entityListener);
}

void* HkFixedConstraintData_Create(void) { EnsureThreadInfo();
    LOG_CALL(HkFixedConstraintData_Create);
    REQUIRE_FUNCTION_POINTER(HkFixedConstraintData_Create)
    return pHkFixedConstraintData_Create();
}

void HkFixedConstraintData_SetInBodySpaceInternal(void* instance, Matrix pivotA, Matrix pivotB) { EnsureThreadInfo();
    LOG_CALL(HkFixedConstraintData_SetInBodySpaceInternal);
    REQUIRE_FUNCTION_POINTER(HkFixedConstraintData_SetInBodySpaceInternal)
    pHkFixedConstraintData_SetInBodySpaceInternal(instance, pivotA, pivotB);
}

void HkFixedConstraintData_SetInWorldSpace(void* instance, Matrix bodyATransform, Matrix bodyBTransform, Matrix pivot) { EnsureThreadInfo();
    LOG_CALL(HkFixedConstraintData_SetInWorldSpace);
    REQUIRE_FUNCTION_POINTER(HkFixedConstraintData_SetInWorldSpace)
    pHkFixedConstraintData_SetInWorldSpace(instance, bodyATransform, bodyBTransform, pivot);
}

bool HkFixedConstraintData_IsValid(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkFixedConstraintData_IsValid);
    REQUIRE_FUNCTION_POINTER(HkFixedConstraintData_IsValid)
    return pHkFixedConstraintData_IsValid(instance);
}

bool HkFixedConstraintData_SetInertiaStabilizationFactor(void* instance, float value) { EnsureThreadInfo();
    LOG_CALL(HkFixedConstraintData_SetInertiaStabilizationFactor);
    REQUIRE_FUNCTION_POINTER(HkFixedConstraintData_SetInertiaStabilizationFactor)
    return pHkFixedConstraintData_SetInertiaStabilizationFactor(instance, value);
}

bool HkFixedConstraintData_GetInertiaStabilizationFactor(void* instance, void* outResult) { EnsureThreadInfo();
    LOG_CALL(HkFixedConstraintData_GetInertiaStabilizationFactor);
    REQUIRE_FUNCTION_POINTER(HkFixedConstraintData_GetInertiaStabilizationFactor)
    return pHkFixedConstraintData_GetInertiaStabilizationFactor(instance, outResult);
}

float HkFixedConstraintData_GetSolverImpulseInLastStep(void* constraint, uint8_t constraintAtom) { EnsureThreadInfo();
    LOG_CALL(HkFixedConstraintData_GetSolverImpulseInLastStep);
    REQUIRE_FUNCTION_POINTER(HkFixedConstraintData_GetSolverImpulseInLastStep)
    return pHkFixedConstraintData_GetSolverImpulseInLastStep(constraint, constraintAtom);
}

void* HkGeometry_Create(void) { EnsureThreadInfo();
    LOG_CALL(HkGeometry_Create);
    REQUIRE_FUNCTION_POINTER(HkGeometry_Create)
    return pHkGeometry_Create();
}

void* HkGeometry_CreateWithParams(int32_t vCount, void* vertices, int32_t iCount, void* indices, int32_t mCount, void* materials) { EnsureThreadInfo();
    LOG_CALL(HkGeometry_CreateWithParams);
    REQUIRE_FUNCTION_POINTER(HkGeometry_CreateWithParams)
    return pHkGeometry_CreateWithParams(vCount, vertices, iCount, indices, mCount, materials);
}

void HkGeometry_Destroy(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkGeometry_Destroy);
    REQUIRE_FUNCTION_POINTER(HkGeometry_Destroy)
    pHkGeometry_Destroy(instance);
}

int32_t HkGeometry_GetTriangleCount(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkGeometry_GetTriangleCount);
    REQUIRE_FUNCTION_POINTER(HkGeometry_GetTriangleCount)
    return pHkGeometry_GetTriangleCount(instance);
}

int32_t HkGeometry_GetVertexCount(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkGeometry_GetVertexCount);
    REQUIRE_FUNCTION_POINTER(HkGeometry_GetVertexCount)
    return pHkGeometry_GetVertexCount(instance);
}

void HkGeometry_Append(void* instance, void* geometry, Matrix matrix) { EnsureThreadInfo();
    LOG_CALL(HkGeometry_Append);
    REQUIRE_FUNCTION_POINTER(HkGeometry_Append)
    pHkGeometry_Append(instance, geometry, matrix);
}

void HkGeometry_GetTriangle(void* instance, int32_t triangleIndex, void* outTriangle) { EnsureThreadInfo();
    LOG_CALL(HkGeometry_GetTriangle);
    REQUIRE_FUNCTION_POINTER(HkGeometry_GetTriangle)
    pHkGeometry_GetTriangle(instance, triangleIndex, outTriangle);
}

Vector3 HkGeometry_GetVertex(void* instance, int32_t vertexIndex) { EnsureThreadInfo();
    LOG_CALL(HkGeometry_GetVertex);
    REQUIRE_FUNCTION_POINTER(HkGeometry_GetVertex)
    return pHkGeometry_GetVertex(instance, vertexIndex);
}

void HkGeometry_SetGeometry(void* instance, int32_t vCount, void* vertices, int32_t iCount, void* indices, int32_t mCount, void* materials) { EnsureThreadInfo();
    LOG_CALL(HkGeometry_SetGeometry);
    REQUIRE_FUNCTION_POINTER(HkGeometry_SetGeometry)
    pHkGeometry_SetGeometry(instance, vCount, vertices, iCount, indices, mCount, materials);
}

void* HkGridShape_Create(float cellSize, int32_t policy) { EnsureThreadInfo();
    LOG_CALL(HkGridShape_Create);
    REQUIRE_FUNCTION_POINTER(HkGridShape_Create)
    return pHkGridShape_Create(cellSize, policy);
}

float HkGridShape_GetCellSize(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkGridShape_GetCellSize);
    REQUIRE_FUNCTION_POINTER(HkGridShape_GetCellSize)
    return pHkGridShape_GetCellSize(instance);
}

int32_t HkGridShape_GetShapeCount(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkGridShape_GetShapeCount);
    REQUIRE_FUNCTION_POINTER(HkGridShape_GetShapeCount)
    return pHkGridShape_GetShapeCount(instance);
}

void HkGridShape_SetDebugRigidBody(void* instance, void* rigidBody) { EnsureThreadInfo();
    LOG_CALL(HkGridShape_SetDebugRigidBody);
    REQUIRE_FUNCTION_POINTER(HkGridShape_SetDebugRigidBody)
    pHkGridShape_SetDebugRigidBody(instance, rigidBody);
}

void* HkGridShape_GetDebugRigidBody(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkGridShape_GetDebugRigidBody);
    REQUIRE_FUNCTION_POINTER(HkGridShape_GetDebugRigidBody)
    return pHkGridShape_GetDebugRigidBody(instance);
}

void HkGridShape_SetDebugDraw(void* instance, bool debugDraw) { EnsureThreadInfo();
    LOG_CALL(HkGridShape_SetDebugDraw);
    REQUIRE_FUNCTION_POINTER(HkGridShape_SetDebugDraw)
    pHkGridShape_SetDebugDraw(instance, debugDraw);
}

bool HkGridShape_GetDebugDraw(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkGridShape_GetDebugDraw);
    REQUIRE_FUNCTION_POINTER(HkGridShape_GetDebugDraw)
    return pHkGridShape_GetDebugDraw(instance);
}

void HkGridShape_AddShapes(void* instance, void* shapes, uint32_t count, Vector3S min, Vector3S max) { EnsureThreadInfo();
    LOG_CALL(HkGridShape_AddShapes);
    REQUIRE_FUNCTION_POINTER(HkGridShape_AddShapes)
    pHkGridShape_AddShapes(instance, shapes, count, min, max);
}

bool HkGridShape_Contains(void* instance, int16_t x, int16_t y, int16_t z) { EnsureThreadInfo();
    LOG_CALL(HkGridShape_Contains);
    REQUIRE_FUNCTION_POINTER(HkGridShape_Contains)
    return pHkGridShape_Contains(instance, x, y, z);
}

void HkGridShape_GetShape(void* instance, Vector3I pos, void* shapeBuffer) { EnsureThreadInfo();
    LOG_CALL(HkGridShape_GetShape);
    REQUIRE_FUNCTION_POINTER(HkGridShape_GetShape)
    pHkGridShape_GetShape(instance, pos, shapeBuffer);
}

void HkGridShape_GetShapeInfo(void* instance, int32_t index, void* min, void* max, void* shapeBuffer) { EnsureThreadInfo();
    LOG_CALL(HkGridShape_GetShapeInfo);
    REQUIRE_FUNCTION_POINTER(HkGridShape_GetShapeInfo)
    pHkGridShape_GetShapeInfo(instance, index, min, max, shapeBuffer);
}

int32_t HkGridShape_GetShapeInfoCount(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkGridShape_GetShapeInfoCount);
    REQUIRE_FUNCTION_POINTER(HkGridShape_GetShapeInfoCount)
    return pHkGridShape_GetShapeInfoCount(instance);
}

void HkGridShape_GetShapeMin(void* instance, uint32_t shapeKey, void* min) { EnsureThreadInfo();
    LOG_CALL(HkGridShape_GetShapeMin);
    REQUIRE_FUNCTION_POINTER(HkGridShape_GetShapeMin)
    pHkGridShape_GetShapeMin(instance, shapeKey, min);
}

void HkGridShape_GetShapesInInterval(void* instance, Vector3 min, Vector3 max, void* shapeBuffer) { EnsureThreadInfo();
    LOG_CALL(HkGridShape_GetShapesInInterval);
    REQUIRE_FUNCTION_POINTER(HkGridShape_GetShapesInInterval)
    pHkGridShape_GetShapesInInterval(instance, min, max, shapeBuffer);
}

void HkGridShape_GetChildBounds(void* instance, uint32_t shapeKey, void* min, void* max) { EnsureThreadInfo();
    LOG_CALL(HkGridShape_GetChildBounds);
    REQUIRE_FUNCTION_POINTER(HkGridShape_GetChildBounds)
    pHkGridShape_GetChildBounds(instance, shapeKey, min, max);
}

void HkGridShape_RemoveShapes(void* instance, void* positions, uint32_t count, void* results) { EnsureThreadInfo();
    LOG_CALL(HkGridShape_RemoveShapes);
    REQUIRE_FUNCTION_POINTER(HkGridShape_RemoveShapes)
    pHkGridShape_RemoveShapes(instance, positions, count, results);
}

void HkGridShape_GetCellRanges(void* instance, void* positions, uint32_t count, void* results) { EnsureThreadInfo();
    LOG_CALL(HkGridShape_GetCellRanges);
    REQUIRE_FUNCTION_POINTER(HkGridShape_GetCellRanges)
    pHkGridShape_GetCellRanges(instance, positions, count, results);
}

uint32_t HkGroupFilter_CalcFilterInfo(int32_t layer, int32_t systemGroup, int32_t subSystemId, int32_t subSystemDontCollideWith) { EnsureThreadInfo();
    LOG_CALL(HkGroupFilter_CalcFilterInfo);
    REQUIRE_FUNCTION_POINTER(HkGroupFilter_CalcFilterInfo)
    return pHkGroupFilter_CalcFilterInfo(layer, systemGroup, subSystemId, subSystemDontCollideWith);
}

int32_t HkGroupFilter_GetLayerFromFilterInfo(uint32_t filterInfo) { EnsureThreadInfo();
    LOG_CALL(HkGroupFilter_GetLayerFromFilterInfo);
    REQUIRE_FUNCTION_POINTER(HkGroupFilter_GetLayerFromFilterInfo)
    return pHkGroupFilter_GetLayerFromFilterInfo(filterInfo);
}

int32_t HkGroupFilter_getSubSystemDontCollideWithFromFilterInfo(uint32_t filterInfo) { EnsureThreadInfo();
    LOG_CALL(HkGroupFilter_getSubSystemDontCollideWithFromFilterInfo);
    REQUIRE_FUNCTION_POINTER(HkGroupFilter_getSubSystemDontCollideWithFromFilterInfo)
    return pHkGroupFilter_getSubSystemDontCollideWithFromFilterInfo(filterInfo);
}

int32_t HkGroupFilter_GetSubSystemIdFromFilterInfo(uint32_t filterInfo) { EnsureThreadInfo();
    LOG_CALL(HkGroupFilter_GetSubSystemIdFromFilterInfo);
    REQUIRE_FUNCTION_POINTER(HkGroupFilter_GetSubSystemIdFromFilterInfo)
    return pHkGroupFilter_GetSubSystemIdFromFilterInfo(filterInfo);
}

int32_t HkGroupFilter_GetSystemGroupFromFilterInfo(uint32_t filterInfo) { EnsureThreadInfo();
    LOG_CALL(HkGroupFilter_GetSystemGroupFromFilterInfo);
    REQUIRE_FUNCTION_POINTER(HkGroupFilter_GetSystemGroupFromFilterInfo)
    return pHkGroupFilter_GetSystemGroupFromFilterInfo(filterInfo);
}

int32_t HkGroupFilter_SetLayer(uint32_t filterInfo, int32_t newLayer) { EnsureThreadInfo();
    LOG_CALL(HkGroupFilter_SetLayer);
    REQUIRE_FUNCTION_POINTER(HkGroupFilter_SetLayer)
    return pHkGroupFilter_SetLayer(filterInfo, newLayer);
}

void HkGroupFilter_DisableCollisionsBetween(void* instance, int32_t layerA, int32_t layerB) { EnsureThreadInfo();
    LOG_CALL(HkGroupFilter_DisableCollisionsBetween);
    REQUIRE_FUNCTION_POINTER(HkGroupFilter_DisableCollisionsBetween)
    pHkGroupFilter_DisableCollisionsBetween(instance, layerA, layerB);
}

void HkGroupFilter_DisableCollisionsUsingBitfield(void* instance, uint32_t layerBitsA, uint32_t layerBitsB) { EnsureThreadInfo();
    LOG_CALL(HkGroupFilter_DisableCollisionsUsingBitfield);
    REQUIRE_FUNCTION_POINTER(HkGroupFilter_DisableCollisionsUsingBitfield)
    pHkGroupFilter_DisableCollisionsUsingBitfield(instance, layerBitsA, layerBitsB);
}

void HkGroupFilter_EnableCollisionsBetween(void* instance, int32_t layerA, int32_t layerB) { EnsureThreadInfo();
    LOG_CALL(HkGroupFilter_EnableCollisionsBetween);
    REQUIRE_FUNCTION_POINTER(HkGroupFilter_EnableCollisionsBetween)
    pHkGroupFilter_EnableCollisionsBetween(instance, layerA, layerB);
}

void HkGroupFilter_EnableCollisionsUsingBitfield(void* instance, uint32_t layerBitsA, uint32_t layerBitsB) { EnsureThreadInfo();
    LOG_CALL(HkGroupFilter_EnableCollisionsUsingBitfield);
    REQUIRE_FUNCTION_POINTER(HkGroupFilter_EnableCollisionsUsingBitfield)
    pHkGroupFilter_EnableCollisionsUsingBitfield(instance, layerBitsA, layerBitsB);
}

int32_t HkGroupFilter_GetNewSystemGroup(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkGroupFilter_GetNewSystemGroup);
    REQUIRE_FUNCTION_POINTER(HkGroupFilter_GetNewSystemGroup)
    return pHkGroupFilter_GetNewSystemGroup(instance);
}

void HkGlobal_ReleasePtr(void* ptr) { EnsureThreadInfo();
    LOG_CALL(HkGlobal_ReleasePtr);
    REQUIRE_FUNCTION_POINTER(HkGlobal_ReleasePtr)
    pHkGlobal_ReleasePtr(ptr);
    release_callback_owner(ptr);
}

void HkGlobal_ReleaseString(void* ptr) { EnsureThreadInfo();
    LOG_CALL(HkGlobal_ReleaseString);
    REQUIRE_FUNCTION_POINTER(HkGlobal_ReleaseString)
    pHkGlobal_ReleaseString(ptr);
}

void HkGlobal_ReleaseArrayPtr(void* ptr) { EnsureThreadInfo();
    LOG_CALL(HkGlobal_ReleaseArrayPtr);
    REQUIRE_FUNCTION_POINTER(HkGlobal_ReleaseArrayPtr)
    pHkGlobal_ReleaseArrayPtr(ptr);
}

void* HkHingeConstraintData_Create(void) { EnsureThreadInfo();
    LOG_CALL(HkHingeConstraintData_Create);
    REQUIRE_FUNCTION_POINTER(HkHingeConstraintData_Create)
    return pHkHingeConstraintData_Create();
}

void HkHingeConstraintData_SetInBodySpaceInternal(void* instance, Vector3 pivotA, Vector3 pivotB, Vector3 axisA, Vector3 axisB) { EnsureThreadInfo();
    LOG_CALL(HkHingeConstraintData_SetInBodySpaceInternal);
    REQUIRE_FUNCTION_POINTER(HkHingeConstraintData_SetInBodySpaceInternal)
    pHkHingeConstraintData_SetInBodySpaceInternal(instance, pivotA, pivotB, axisA, axisB);
}

void HkHingeConstraintData_SetInWorldSpace(void* instance, Matrix bodyATransform, Matrix bodyBTransform, Vector3 pivot, Vector3 axis) { EnsureThreadInfo();
    LOG_CALL(HkHingeConstraintData_SetInWorldSpace);
    REQUIRE_FUNCTION_POINTER(HkHingeConstraintData_SetInWorldSpace)
    pHkHingeConstraintData_SetInWorldSpace(instance, bodyATransform, bodyBTransform, pivot, axis);
}

bool HkHingeConstraintData_SetInertiaStabilizationFactor(void* instance, float value) { EnsureThreadInfo();
    LOG_CALL(HkHingeConstraintData_SetInertiaStabilizationFactor);
    REQUIRE_FUNCTION_POINTER(HkHingeConstraintData_SetInertiaStabilizationFactor)
    return pHkHingeConstraintData_SetInertiaStabilizationFactor(instance, value);
}

bool HkHingeConstraintData_GetInertiaStabilizationFactor(void* instance, void* outResult) { EnsureThreadInfo();
    LOG_CALL(HkHingeConstraintData_GetInertiaStabilizationFactor);
    REQUIRE_FUNCTION_POINTER(HkHingeConstraintData_GetInertiaStabilizationFactor)
    return pHkHingeConstraintData_GetInertiaStabilizationFactor(instance, outResult);
}

void* HkInertiaTensorComputer_Create(void) { EnsureThreadInfo();
    LOG_CALL(HkInertiaTensorComputer_Create);
    REQUIRE_FUNCTION_POINTER(HkInertiaTensorComputer_Create)
    return pHkInertiaTensorComputer_Create();
}

void HkInertiaTensorComputer_CombineMassPropertiesInstance(void* instance, void* massElements, int32_t count, void* returnMassProperties) { EnsureThreadInfo();
    LOG_CALL(HkInertiaTensorComputer_CombineMassPropertiesInstance);
    REQUIRE_FUNCTION_POINTER(HkInertiaTensorComputer_CombineMassPropertiesInstance)
    pHkInertiaTensorComputer_CombineMassPropertiesInstance(instance, massElements, count, returnMassProperties);
}

void HkInertiaTensorComputer_Release(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkInertiaTensorComputer_Release);
    REQUIRE_FUNCTION_POINTER(HkInertiaTensorComputer_Release)
    pHkInertiaTensorComputer_Release(instance);
}

void HkInertiaTensorComputer_ComputeBoxVolumeMassProperties(Vector3 halfExtents, float mass, void* returnMassProperties) { EnsureThreadInfo();
    LOG_CALL(HkInertiaTensorComputer_ComputeBoxVolumeMassProperties);
    REQUIRE_FUNCTION_POINTER(HkInertiaTensorComputer_ComputeBoxVolumeMassProperties)
    pHkInertiaTensorComputer_ComputeBoxVolumeMassProperties(halfExtents, mass, returnMassProperties);
}

void HkInertiaTensorComputer_ComputeCapsuleVolumeMassProperties(Vector3 startAxis, Vector3 endAxis, float radius, float mass, void* returnMassProperties) { EnsureThreadInfo();
    LOG_CALL(HkInertiaTensorComputer_ComputeCapsuleVolumeMassProperties);
    REQUIRE_FUNCTION_POINTER(HkInertiaTensorComputer_ComputeCapsuleVolumeMassProperties)
    pHkInertiaTensorComputer_ComputeCapsuleVolumeMassProperties(startAxis, endAxis, radius, mass, returnMassProperties);
}

void HkInertiaTensorComputer_ComputeCylinderVolumeMassProperties(Vector3 startAxis, Vector3 endAxis, float radius, float mass, void* returnMassProperties) { EnsureThreadInfo();
    LOG_CALL(HkInertiaTensorComputer_ComputeCylinderVolumeMassProperties);
    REQUIRE_FUNCTION_POINTER(HkInertiaTensorComputer_ComputeCylinderVolumeMassProperties)
    pHkInertiaTensorComputer_ComputeCylinderVolumeMassProperties(startAxis, endAxis, radius, mass, returnMassProperties);
}

void HkInertiaTensorComputer_ComputeSphereVolumeMassProperties(float radius, float mass, void* returnMassProperties) { EnsureThreadInfo();
    LOG_CALL(HkInertiaTensorComputer_ComputeSphereVolumeMassProperties);
    REQUIRE_FUNCTION_POINTER(HkInertiaTensorComputer_ComputeSphereVolumeMassProperties)
    pHkInertiaTensorComputer_ComputeSphereVolumeMassProperties(radius, mass, returnMassProperties);
}

void* HkJobQueue_Create(void* outThreadCount) { EnsureThreadInfo();
    LOG_CALL(HkJobQueue_Create);
    REQUIRE_FUNCTION_POINTER(HkJobQueue_Create)
    return pHkJobQueue_Create(outThreadCount);
}

void* HkJobQueue_CreateWithNumThreads(int32_t threadCount) { EnsureThreadInfo();
    LOG_CALL(HkJobQueue_CreateWithNumThreads);
    REQUIRE_FUNCTION_POINTER(HkJobQueue_CreateWithNumThreads)
    return pHkJobQueue_CreateWithNumThreads(threadCount);
}

void HkJobQueue_Release(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkJobQueue_Release);
    REQUIRE_FUNCTION_POINTER(HkJobQueue_Release)
    pHkJobQueue_Release(instance);
}

int32_t HkJobQueue_GetWaitPolicy(void* jobQueue) { EnsureThreadInfo();
    LOG_CALL(HkJobQueue_GetWaitPolicy);
    REQUIRE_FUNCTION_POINTER(HkJobQueue_GetWaitPolicy)
    return pHkJobQueue_GetWaitPolicy(jobQueue);
}

void HkJobQueue_SetWaitPolicy(void* jobQueue, int32_t value) { EnsureThreadInfo();
    LOG_CALL(HkJobQueue_SetWaitPolicy);
    REQUIRE_FUNCTION_POINTER(HkJobQueue_SetWaitPolicy)
    pHkJobQueue_SetWaitPolicy(jobQueue, value);
}

int32_t HkJobQueue_GetMasterThreadFinishingFlags(void* jobQueue) { EnsureThreadInfo();
    LOG_CALL(HkJobQueue_GetMasterThreadFinishingFlags);
    REQUIRE_FUNCTION_POINTER(HkJobQueue_GetMasterThreadFinishingFlags)
    return pHkJobQueue_GetMasterThreadFinishingFlags(jobQueue);
}

void HkJobQueue_SetMasterThreadFinishingFlags(void* jobQueue, int32_t value) { EnsureThreadInfo();
    LOG_CALL(HkJobQueue_SetMasterThreadFinishingFlags);
    REQUIRE_FUNCTION_POINTER(HkJobQueue_SetMasterThreadFinishingFlags)
    pHkJobQueue_SetMasterThreadFinishingFlags(jobQueue, value);
}

void HkJobQueue_ProcessAllJobs(void* jobQueue) { EnsureThreadInfo();
    LOG_CALL(HkJobQueue_ProcessAllJobs);
    REQUIRE_FUNCTION_POINTER(HkJobQueue_ProcessAllJobs)
    pHkJobQueue_ProcessAllJobs(jobQueue);
}

void* HkJobThreadPool_Create(void* outThreadCount) { EnsureThreadInfo();
    LOG_CALL(HkJobThreadPool_Create);
    REQUIRE_FUNCTION_POINTER(HkJobThreadPool_Create)
    return pHkJobThreadPool_Create(outThreadCount);
}

void* HkJobThreadPool_CreateWithNumThreads(int32_t threadCount) { EnsureThreadInfo();
    LOG_CALL(HkJobThreadPool_CreateWithNumThreads);
    REQUIRE_FUNCTION_POINTER(HkJobThreadPool_CreateWithNumThreads)
    return pHkJobThreadPool_CreateWithNumThreads(threadCount);
}

void HkJobThreadPool_RemoveReference(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkJobThreadPool_RemoveReference);
    REQUIRE_FUNCTION_POINTER(HkJobThreadPool_RemoveReference)
    pHkJobThreadPool_RemoveReference(instance);
}

void HkJobThreadPool_RunOnEachWorker(void* instance, void* action, void* data) { EnsureThreadInfo();
    LOG_CALL(HkJobThreadPool_RunOnEachWorker);
    REQUIRE_FUNCTION_POINTER(HkJobThreadPool_RunOnEachWorker)
    pHkJobThreadPool_RunOnEachWorker(instance, bridge_void_ptr(action), data);
}

void HkJobThreadPool_ExecuteJobQueue(void* instance, void* jobQueue) { EnsureThreadInfo();
    LOG_CALL(HkJobThreadPool_ExecuteJobQueue);
    REQUIRE_FUNCTION_POINTER(HkJobThreadPool_ExecuteJobQueue)
    pHkJobThreadPool_ExecuteJobQueue(instance, jobQueue);
}

int32_t HkJobThreadPool_GetThisThreadIndex(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkJobThreadPool_GetThisThreadIndex);
    REQUIRE_FUNCTION_POINTER(HkJobThreadPool_GetThisThreadIndex)
    return pHkJobThreadPool_GetThisThreadIndex(instance);
}

void HkJobThreadPool_WaitForCompletion(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkJobThreadPool_WaitForCompletion);
    REQUIRE_FUNCTION_POINTER(HkJobThreadPool_WaitForCompletion)
    pHkJobThreadPool_WaitForCompletion(instance);
}

void HkJobThreadPool_ClearTimerData(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkJobThreadPool_ClearTimerData);
    REQUIRE_FUNCTION_POINTER(HkJobThreadPool_ClearTimerData)
    pHkJobThreadPool_ClearTimerData(instance);
}

void HkKeyFrameUtility_ApplyHardKeyFrame(Vector4 nextPosition, Quaternion nextOrientation, float invDeltaTime, void* body) { EnsureThreadInfo();
    LOG_CALL(HkKeyFrameUtility_ApplyHardKeyFrame);
    REQUIRE_FUNCTION_POINTER(HkKeyFrameUtility_ApplyHardKeyFrame)
    pHkKeyFrameUtility_ApplyHardKeyFrame(nextPosition, nextOrientation, invDeltaTime, body);
}

float HkLimitedForceConstraintMotor_GetMinForce(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkLimitedForceConstraintMotor_GetMinForce);
    REQUIRE_FUNCTION_POINTER(HkLimitedForceConstraintMotor_GetMinForce)
    return pHkLimitedForceConstraintMotor_GetMinForce(instance);
}

void HkLimitedForceConstraintMotor_SetMinForce(void* instance, float value) { EnsureThreadInfo();
    LOG_CALL(HkLimitedForceConstraintMotor_SetMinForce);
    REQUIRE_FUNCTION_POINTER(HkLimitedForceConstraintMotor_SetMinForce)
    pHkLimitedForceConstraintMotor_SetMinForce(instance, value);
}

float HkLimitedForceConstraintMotor_GetMaxForce(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkLimitedForceConstraintMotor_GetMaxForce);
    REQUIRE_FUNCTION_POINTER(HkLimitedForceConstraintMotor_GetMaxForce)
    return pHkLimitedForceConstraintMotor_GetMaxForce(instance);
}

void HkLimitedForceConstraintMotor_SetMaxForce(void* instance, float value) { EnsureThreadInfo();
    LOG_CALL(HkLimitedForceConstraintMotor_SetMaxForce);
    REQUIRE_FUNCTION_POINTER(HkLimitedForceConstraintMotor_SetMaxForce)
    pHkLimitedForceConstraintMotor_SetMaxForce(instance, value);
}

void* HkLimitedHingeConstraintData_Create(void) { EnsureThreadInfo();
    LOG_CALL(HkLimitedHingeConstraintData_Create);
    REQUIRE_FUNCTION_POINTER(HkLimitedHingeConstraintData_Create)
    return pHkLimitedHingeConstraintData_Create();
}

void HkLimitedHingeConstraintData_SetInBodySpaceInternal(void* instance, Vector3 pivotA, Vector3 pivotB, Vector3 axisA, Vector3 axisB, Vector3 axisAPerp, Vector3 axisBPerp) { EnsureThreadInfo();
    LOG_CALL(HkLimitedHingeConstraintData_SetInBodySpaceInternal);
    REQUIRE_FUNCTION_POINTER(HkLimitedHingeConstraintData_SetInBodySpaceInternal)
    pHkLimitedHingeConstraintData_SetInBodySpaceInternal(instance, pivotA, pivotB, axisA, axisB, axisAPerp, axisBPerp);
}

void HkLimitedHingeConstraintData_SetInWorldSpace(void* instance, Matrix bodyATransform, Matrix bodyBTransform, Vector3 pivot, Vector3 axis) { EnsureThreadInfo();
    LOG_CALL(HkLimitedHingeConstraintData_SetInWorldSpace);
    REQUIRE_FUNCTION_POINTER(HkLimitedHingeConstraintData_SetInWorldSpace)
    pHkLimitedHingeConstraintData_SetInWorldSpace(instance, bodyATransform, bodyBTransform, pivot, axis);
}

void HkLimitedHingeConstraintData_SetMotor(void* instance, void* motor) { EnsureThreadInfo();
    LOG_CALL(HkLimitedHingeConstraintData_SetMotor);
    REQUIRE_FUNCTION_POINTER(HkLimitedHingeConstraintData_SetMotor)
    pHkLimitedHingeConstraintData_SetMotor(instance, motor);
}

bool HkLimitedHingeConstraintData_IsMotorEnabled(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkLimitedHingeConstraintData_IsMotorEnabled);
    REQUIRE_FUNCTION_POINTER(HkLimitedHingeConstraintData_IsMotorEnabled)
    return pHkLimitedHingeConstraintData_IsMotorEnabled(instance);
}

void HkLimitedHingeConstraintData_SetMotorEnabled(void* instance, void* constraint, bool enabled) { EnsureThreadInfo();
    LOG_CALL(HkLimitedHingeConstraintData_SetMotorEnabled);
    REQUIRE_FUNCTION_POINTER(HkLimitedHingeConstraintData_SetMotorEnabled)
    pHkLimitedHingeConstraintData_SetMotorEnabled(instance, constraint, enabled);
}

float HkLimitedHingeConstraintData_GetTargetAngle(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkLimitedHingeConstraintData_GetTargetAngle);
    REQUIRE_FUNCTION_POINTER(HkLimitedHingeConstraintData_GetTargetAngle)
    return pHkLimitedHingeConstraintData_GetTargetAngle(instance);
}

void HkLimitedHingeConstraintData_SetTargetAngle(void* instance, float value) { EnsureThreadInfo();
    LOG_CALL(HkLimitedHingeConstraintData_SetTargetAngle);
    REQUIRE_FUNCTION_POINTER(HkLimitedHingeConstraintData_SetTargetAngle)
    pHkLimitedHingeConstraintData_SetTargetAngle(instance, value);
}

float HkLimitedHingeConstraintData_GetMaxFrictionTorque(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkLimitedHingeConstraintData_GetMaxFrictionTorque);
    REQUIRE_FUNCTION_POINTER(HkLimitedHingeConstraintData_GetMaxFrictionTorque)
    return pHkLimitedHingeConstraintData_GetMaxFrictionTorque(instance);
}

void HkLimitedHingeConstraintData_SetMaxFrictionTorque(void* instance, float value) { EnsureThreadInfo();
    LOG_CALL(HkLimitedHingeConstraintData_SetMaxFrictionTorque);
    REQUIRE_FUNCTION_POINTER(HkLimitedHingeConstraintData_SetMaxFrictionTorque)
    pHkLimitedHingeConstraintData_SetMaxFrictionTorque(instance, value);
}

float HkLimitedHingeConstraintData_GetMinAngularLimit(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkLimitedHingeConstraintData_GetMinAngularLimit);
    REQUIRE_FUNCTION_POINTER(HkLimitedHingeConstraintData_GetMinAngularLimit)
    return pHkLimitedHingeConstraintData_GetMinAngularLimit(instance);
}

void HkLimitedHingeConstraintData_SetMinAngularLimit(void* instance, float value) { EnsureThreadInfo();
    LOG_CALL(HkLimitedHingeConstraintData_SetMinAngularLimit);
    REQUIRE_FUNCTION_POINTER(HkLimitedHingeConstraintData_SetMinAngularLimit)
    pHkLimitedHingeConstraintData_SetMinAngularLimit(instance, value);
}

float HkLimitedHingeConstraintData_GetMaxAngularLimit(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkLimitedHingeConstraintData_GetMaxAngularLimit);
    REQUIRE_FUNCTION_POINTER(HkLimitedHingeConstraintData_GetMaxAngularLimit)
    return pHkLimitedHingeConstraintData_GetMaxAngularLimit(instance);
}

void HkLimitedHingeConstraintData_SetMaxAngularLimit(void* instance, float value) { EnsureThreadInfo();
    LOG_CALL(HkLimitedHingeConstraintData_SetMaxAngularLimit);
    REQUIRE_FUNCTION_POINTER(HkLimitedHingeConstraintData_SetMaxAngularLimit)
    pHkLimitedHingeConstraintData_SetMaxAngularLimit(instance, value);
}

void HkLimitedHingeConstraintData_DisableLimits(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkLimitedHingeConstraintData_DisableLimits);
    REQUIRE_FUNCTION_POINTER(HkLimitedHingeConstraintData_DisableLimits)
    pHkLimitedHingeConstraintData_DisableLimits(instance);
}

bool HkLimitedHingeConstraintData_SetInertiaStabilizationFactor(void* instance, float value) { EnsureThreadInfo();
    LOG_CALL(HkLimitedHingeConstraintData_SetInertiaStabilizationFactor);
    REQUIRE_FUNCTION_POINTER(HkLimitedHingeConstraintData_SetInertiaStabilizationFactor)
    return pHkLimitedHingeConstraintData_SetInertiaStabilizationFactor(instance, value);
}

bool HkLimitedHingeConstraintData_GetInertiaStabilizationFactor(void* instance, void* outResult) { EnsureThreadInfo();
    LOG_CALL(HkLimitedHingeConstraintData_GetInertiaStabilizationFactor);
    REQUIRE_FUNCTION_POINTER(HkLimitedHingeConstraintData_GetInertiaStabilizationFactor)
    return pHkLimitedHingeConstraintData_GetInertiaStabilizationFactor(instance, outResult);
}

Vector3 HkLimitedHingeConstraintData_GetBodyAPos(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkLimitedHingeConstraintData_GetBodyAPos);
    REQUIRE_FUNCTION_POINTER(HkLimitedHingeConstraintData_GetBodyAPos)
    return pHkLimitedHingeConstraintData_GetBodyAPos(instance);
}

Vector3 HkLimitedHingeConstraintData_GetBodyBPos(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkLimitedHingeConstraintData_GetBodyBPos);
    REQUIRE_FUNCTION_POINTER(HkLimitedHingeConstraintData_GetBodyBPos)
    return pHkLimitedHingeConstraintData_GetBodyBPos(instance);
}

uint8_t HkLimitedHingeConstraintData_GetIsInitialized(void* constraint) { EnsureThreadInfo();
    LOG_CALL(HkLimitedHingeConstraintData_GetIsInitialized);
    REQUIRE_FUNCTION_POINTER(HkLimitedHingeConstraintData_GetIsInitialized)
    return pHkLimitedHingeConstraintData_GetIsInitialized(constraint);
}

void HkLimitedHingeConstraintData_SetIsInitialized(void* constraint, uint8_t isInitialized) { EnsureThreadInfo();
    LOG_CALL(HkLimitedHingeConstraintData_SetIsInitialized);
    REQUIRE_FUNCTION_POINTER(HkLimitedHingeConstraintData_SetIsInitialized)
    pHkLimitedHingeConstraintData_SetIsInitialized(constraint, isInitialized);
}

float HkLimitedHingeConstraintData_GetPreviousTargetAngle(void* constraint) { EnsureThreadInfo();
    LOG_CALL(HkLimitedHingeConstraintData_GetPreviousTargetAngle);
    REQUIRE_FUNCTION_POINTER(HkLimitedHingeConstraintData_GetPreviousTargetAngle)
    return pHkLimitedHingeConstraintData_GetPreviousTargetAngle(constraint);
}

void HkLimitedHingeConstraintData_SetPreviousTargetAngle(void* constraint, float previousTargetAngle) { EnsureThreadInfo();
    LOG_CALL(HkLimitedHingeConstraintData_SetPreviousTargetAngle);
    REQUIRE_FUNCTION_POINTER(HkLimitedHingeConstraintData_SetPreviousTargetAngle)
    pHkLimitedHingeConstraintData_SetPreviousTargetAngle(constraint, previousTargetAngle);
}

float HkLimitedHingeConstraintData_GetCurrentAngle(void* constraint) { EnsureThreadInfo();
    LOG_CALL(HkLimitedHingeConstraintData_GetCurrentAngle);
    REQUIRE_FUNCTION_POINTER(HkLimitedHingeConstraintData_GetCurrentAngle)
    return pHkLimitedHingeConstraintData_GetCurrentAngle(constraint);
}

void HkLimitedHingeConstraintData_SetCurrentAngle(void* constraint, float value) { EnsureThreadInfo();
    LOG_CALL(HkLimitedHingeConstraintData_SetCurrentAngle);
    REQUIRE_FUNCTION_POINTER(HkLimitedHingeConstraintData_SetCurrentAngle)
    pHkLimitedHingeConstraintData_SetCurrentAngle(constraint, value);
}

void* HkListShape_Create(void* shapes, int32_t count, int32_t refPolicy) { EnsureThreadInfo();
    LOG_CALL(HkListShape_Create);
    REQUIRE_FUNCTION_POINTER(HkListShape_Create)
    return pHkListShape_Create(shapes, count, refPolicy);
}

uint16_t HkListShape_GetDisabledChildrenCount(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkListShape_GetDisabledChildrenCount);
    REQUIRE_FUNCTION_POINTER(HkListShape_GetDisabledChildrenCount)
    return pHkListShape_GetDisabledChildrenCount(instance);
}

int32_t HkListShape_GetTotalChildrenCount(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkListShape_GetTotalChildrenCount);
    REQUIRE_FUNCTION_POINTER(HkListShape_GetTotalChildrenCount)
    return pHkListShape_GetTotalChildrenCount(instance);
}

void HkListShape_EnableShape(void* instance, uint32_t shapeKey, bool isEnable) { EnsureThreadInfo();
    LOG_CALL(HkListShape_EnableShape);
    REQUIRE_FUNCTION_POINTER(HkListShape_EnableShape)
    pHkListShape_EnableShape(instance, shapeKey, isEnable);
}

void* HkListShape_GetChildByIndex(void* instance, int32_t index) { EnsureThreadInfo();
    LOG_CALL(HkListShape_GetChildByIndex);
    REQUIRE_FUNCTION_POINTER(HkListShape_GetChildByIndex)
    return pHkListShape_GetChildByIndex(instance, index);
}

bool HkListShape_IsChildEnabled(void* instance, uint32_t shapeKey) { EnsureThreadInfo();
    LOG_CALL(HkListShape_IsChildEnabled);
    REQUIRE_FUNCTION_POINTER(HkListShape_IsChildEnabled)
    return pHkListShape_IsChildEnabled(instance, shapeKey);
}

void* HkMalleableConstraintData_Create(void* data) { EnsureThreadInfo();
    LOG_CALL(HkMalleableConstraintData_Create);
    REQUIRE_FUNCTION_POINTER(HkMalleableConstraintData_Create)
    return pHkMalleableConstraintData_Create(data);
}

float HkMalleableConstraintData_GetStrength(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkMalleableConstraintData_GetStrength);
    REQUIRE_FUNCTION_POINTER(HkMalleableConstraintData_GetStrength)
    return pHkMalleableConstraintData_GetStrength(instance);
}

void HkMalleableConstraintData_SetStrength(void* instance, float value) { EnsureThreadInfo();
    LOG_CALL(HkMalleableConstraintData_SetStrength);
    REQUIRE_FUNCTION_POINTER(HkMalleableConstraintData_SetStrength)
    pHkMalleableConstraintData_SetStrength(instance, value);
}

void* HkMassChangerUtil_Create(void* body, int32_t otherBodyLayerMask, float invMassScale, float invMassScaleOtherBody) { EnsureThreadInfo();
    LOG_CALL(HkMassChangerUtil_Create);
    REQUIRE_FUNCTION_POINTER(HkMassChangerUtil_Create)
    return pHkMassChangerUtil_Create(body, otherBodyLayerMask, invMassScale, invMassScaleOtherBody);
}

bool HkMassChangerUtil_IsValid(void* listener) { EnsureThreadInfo();
    LOG_CALL(HkMassChangerUtil_IsValid);
    REQUIRE_FUNCTION_POINTER(HkMassChangerUtil_IsValid)
    return pHkMassChangerUtil_IsValid(listener);
}

void HkMassChangerUtil_Remove(void* listener) { EnsureThreadInfo();
    LOG_CALL(HkMassChangerUtil_Remove);
    REQUIRE_FUNCTION_POINTER(HkMassChangerUtil_Remove)
    pHkMassChangerUtil_Remove(listener);
}

void HkMemorySnapshot_Diff(void* a, void* b, void* inA, void* inB) { EnsureThreadInfo();
    LOG_CALL(HkMemorySnapshot_Diff);
    REQUIRE_FUNCTION_POINTER(HkMemorySnapshot_Diff)
    pHkMemorySnapshot_Diff(a, b, inA, inB);
}

void* HkMoppBvTreeShape_Create(void* shapeCollection) { EnsureThreadInfo();
    LOG_CALL(HkMoppBvTreeShape_Create);
    REQUIRE_FUNCTION_POINTER(HkMoppBvTreeShape_Create)
    return pHkMoppBvTreeShape_Create(shapeCollection);
}

void* HkMoppBvTreeShape_GetShapeCollection(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkMoppBvTreeShape_GetShapeCollection);
    REQUIRE_FUNCTION_POINTER(HkMoppBvTreeShape_GetShapeCollection)
    return pHkMoppBvTreeShape_GetShapeCollection(instance);
}

void HkMoppBvTreeShape_DisableKeys(void* instance, void* keys, int32_t count) { EnsureThreadInfo();
    LOG_CALL(HkMoppBvTreeShape_DisableKeys);
    REQUIRE_FUNCTION_POINTER(HkMoppBvTreeShape_DisableKeys)
    pHkMoppBvTreeShape_DisableKeys(instance, keys, count);
}

void HkMoppBvTreeShape_QueryAABB(void* instance, Vector3 min, Vector3 max, void* shapeKeys) { EnsureThreadInfo();
    LOG_CALL(HkMoppBvTreeShape_QueryAABB);
    REQUIRE_FUNCTION_POINTER(HkMoppBvTreeShape_QueryAABB)
    pHkMoppBvTreeShape_QueryAABB(instance, min, max, shapeKeys);
}

void HkMoppBvTreeShape_QueryPoint(void* instance, Vector3 point, void* shapeKeys) { EnsureThreadInfo();
    LOG_CALL(HkMoppBvTreeShape_QueryPoint);
    REQUIRE_FUNCTION_POINTER(HkMoppBvTreeShape_QueryPoint)
    pHkMoppBvTreeShape_QueryPoint(instance, point, shapeKeys);
}

void HkMotion_SetWorldMatrix(void* instance, Matrix m) { EnsureThreadInfo();
    LOG_CALL(HkMotion_SetWorldMatrix);
    REQUIRE_FUNCTION_POINTER(HkMotion_SetWorldMatrix)
    pHkMotion_SetWorldMatrix(instance, m);
}

int32_t HkMotion_GetDeactivationClass(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkMotion_GetDeactivationClass);
    REQUIRE_FUNCTION_POINTER(HkMotion_GetDeactivationClass)
    return pHkMotion_GetDeactivationClass(instance);
}

void HkMotion_SetDeactivationClass(void* instance, int32_t value) { EnsureThreadInfo();
    LOG_CALL(HkMotion_SetDeactivationClass);
    REQUIRE_FUNCTION_POINTER(HkMotion_SetDeactivationClass)
    pHkMotion_SetDeactivationClass(instance, value);
}

void* HkPhantomCallbackShape_Create(void* enterCallback, void* leaveCallback, void* deleteCallback) { EnsureThreadInfo();
    LOG_CALL(HkPhantomCallbackShape_Create);
    REQUIRE_FUNCTION_POINTER(HkPhantomCallbackShape_Create)
    return pHkPhantomCallbackShape_Create(bridge_void_ptr_ptr(enterCallback), bridge_void_ptr_ptr(leaveCallback), bridge_void_ptr(deleteCallback));
}

void* HkPrismaticConstraintData_Create(void) { EnsureThreadInfo();
    LOG_CALL(HkPrismaticConstraintData_Create);
    REQUIRE_FUNCTION_POINTER(HkPrismaticConstraintData_Create)
    return pHkPrismaticConstraintData_Create();
}

void HkPrismaticConstraintData_SetInWorldSpace(void* instance, Matrix bodyATransform, Matrix bodyBTransform, Vector3 pivot, Vector3 axis) { EnsureThreadInfo();
    LOG_CALL(HkPrismaticConstraintData_SetInWorldSpace);
    REQUIRE_FUNCTION_POINTER(HkPrismaticConstraintData_SetInWorldSpace)
    pHkPrismaticConstraintData_SetInWorldSpace(instance, bodyATransform, bodyBTransform, pivot, axis);
}

void HkPrismaticConstraintData_SetInBodySpaceInternal(void* instance, Vector3 bodyA, Vector3 bodyB, Vector3 axisA, Vector3 axisB, Vector3 axisAperp, Vector3 axisBperp) { EnsureThreadInfo();
    LOG_CALL(HkPrismaticConstraintData_SetInBodySpaceInternal);
    REQUIRE_FUNCTION_POINTER(HkPrismaticConstraintData_SetInBodySpaceInternal)
    pHkPrismaticConstraintData_SetInBodySpaceInternal(instance, bodyA, bodyB, axisA, axisB, axisAperp, axisBperp);
}

float HkPrismaticConstraintData_GetMaximumLinearLimit(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkPrismaticConstraintData_GetMaximumLinearLimit);
    REQUIRE_FUNCTION_POINTER(HkPrismaticConstraintData_GetMaximumLinearLimit)
    return pHkPrismaticConstraintData_GetMaximumLinearLimit(instance);
}

void HkPrismaticConstraintData_SetMaximumLinearLimit(void* instance, float value) { EnsureThreadInfo();
    LOG_CALL(HkPrismaticConstraintData_SetMaximumLinearLimit);
    REQUIRE_FUNCTION_POINTER(HkPrismaticConstraintData_SetMaximumLinearLimit)
    pHkPrismaticConstraintData_SetMaximumLinearLimit(instance, value);
}

float HkPrismaticConstraintData_GetMinimumLinearLimit(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkPrismaticConstraintData_GetMinimumLinearLimit);
    REQUIRE_FUNCTION_POINTER(HkPrismaticConstraintData_GetMinimumLinearLimit)
    return pHkPrismaticConstraintData_GetMinimumLinearLimit(instance);
}

void HkPrismaticConstraintData_SetMinimumLinearLimit(void* instance, float value) { EnsureThreadInfo();
    LOG_CALL(HkPrismaticConstraintData_SetMinimumLinearLimit);
    REQUIRE_FUNCTION_POINTER(HkPrismaticConstraintData_SetMinimumLinearLimit)
    pHkPrismaticConstraintData_SetMinimumLinearLimit(instance, value);
}

float HkPrismaticConstraintData_GetMaxFrictionForce(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkPrismaticConstraintData_GetMaxFrictionForce);
    REQUIRE_FUNCTION_POINTER(HkPrismaticConstraintData_GetMaxFrictionForce)
    return pHkPrismaticConstraintData_GetMaxFrictionForce(instance);
}

void HkPrismaticConstraintData_SetMaxFrictionForce(void* instance, float value) { EnsureThreadInfo();
    LOG_CALL(HkPrismaticConstraintData_SetMaxFrictionForce);
    REQUIRE_FUNCTION_POINTER(HkPrismaticConstraintData_SetMaxFrictionForce)
    pHkPrismaticConstraintData_SetMaxFrictionForce(instance, value);
}

float HkPrismaticConstraintData_GetTargetPosition(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkPrismaticConstraintData_GetTargetPosition);
    REQUIRE_FUNCTION_POINTER(HkPrismaticConstraintData_GetTargetPosition)
    return pHkPrismaticConstraintData_GetTargetPosition(instance);
}

void HkPrismaticConstraintData_SetTargetPosition(void* instance, float value) { EnsureThreadInfo();
    LOG_CALL(HkPrismaticConstraintData_SetTargetPosition);
    REQUIRE_FUNCTION_POINTER(HkPrismaticConstraintData_SetTargetPosition)
    pHkPrismaticConstraintData_SetTargetPosition(instance, value);
}

void HkPrismaticConstraintData_SetMotor(void* instance, void* motor) { EnsureThreadInfo();
    LOG_CALL(HkPrismaticConstraintData_SetMotor);
    REQUIRE_FUNCTION_POINTER(HkPrismaticConstraintData_SetMotor)
    pHkPrismaticConstraintData_SetMotor(instance, motor);
}

bool HkPrismaticConstraintData_IsMotorEnabled(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkPrismaticConstraintData_IsMotorEnabled);
    REQUIRE_FUNCTION_POINTER(HkPrismaticConstraintData_IsMotorEnabled)
    return pHkPrismaticConstraintData_IsMotorEnabled(instance);
}

void HkPrismaticConstraintData_SetMotorEnabled(void* instance, void* constraint, bool enabled) { EnsureThreadInfo();
    LOG_CALL(HkPrismaticConstraintData_SetMotorEnabled);
    REQUIRE_FUNCTION_POINTER(HkPrismaticConstraintData_SetMotorEnabled)
    pHkPrismaticConstraintData_SetMotorEnabled(instance, constraint, enabled);
}

float HkPrismaticConstraintData_GetCurrentPosition(void* constraint) { EnsureThreadInfo();
    LOG_CALL(HkPrismaticConstraintData_GetCurrentPosition);
    REQUIRE_FUNCTION_POINTER(HkPrismaticConstraintData_GetCurrentPosition)
    return pHkPrismaticConstraintData_GetCurrentPosition(constraint);
}

bool HkPhysicsSystem_IsActive(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkPhysicsSystem_IsActive);
    REQUIRE_FUNCTION_POINTER(HkPhysicsSystem_IsActive)
    return pHkPhysicsSystem_IsActive(instance);
}

void HkPhysicsSystem_SetActive(void* instance, bool value) { EnsureThreadInfo();
    LOG_CALL(HkPhysicsSystem_SetActive);
    REQUIRE_FUNCTION_POINTER(HkPhysicsSystem_SetActive)
    pHkPhysicsSystem_SetActive(instance, value);
}

void HkPhysicsSystem_RecreateConstraints(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkPhysicsSystem_RecreateConstraints);
    REQUIRE_FUNCTION_POINTER(HkPhysicsSystem_RecreateConstraints)
    pHkPhysicsSystem_RecreateConstraints(instance);
}

void HkPhysicsSystem_GetConstraintDataFromSystem(void* instance, void* constraintBuffer) { EnsureThreadInfo();
    LOG_CALL(HkPhysicsSystem_GetConstraintDataFromSystem);
    REQUIRE_FUNCTION_POINTER(HkPhysicsSystem_GetConstraintDataFromSystem)
    pHkPhysicsSystem_GetConstraintDataFromSystem(instance, constraintBuffer);
}

void* HkPhysicsSystem_GetName(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkPhysicsSystem_GetName);
    REQUIRE_FUNCTION_POINTER(HkPhysicsSystem_GetName)
    return pHkPhysicsSystem_GetName(instance);
}

void* HkPhysicsSystem_LoadRagdollFromFile(const char* fileName) { EnsureThreadInfo();
    LOG_CALL(HkPhysicsSystem_LoadRagdollFromFile);
    REQUIRE_FUNCTION_POINTER(HkPhysicsSystem_LoadRagdollFromFile)
    return pHkPhysicsSystem_LoadRagdollFromFile(fileName);
}

void* HkPhysicsSystem_LoadRagdollFromBuffer(void* buffer, int32_t length) { EnsureThreadInfo();
    LOG_CALL(HkPhysicsSystem_LoadRagdollFromBuffer);
    REQUIRE_FUNCTION_POINTER(HkPhysicsSystem_LoadRagdollFromBuffer)
    return pHkPhysicsSystem_LoadRagdollFromBuffer(buffer, length);
}

bool HkPhysicsSystem_InitFromData(void* loadedData, void* physicsSystem, void* bodyBuffer) { EnsureThreadInfo();
    LOG_CALL(HkPhysicsSystem_InitFromData);
    REQUIRE_FUNCTION_POINTER(HkPhysicsSystem_InitFromData)
    return pHkPhysicsSystem_InitFromData(loadedData, physicsSystem, bodyBuffer);
}

uint32_t HkpGroupFilter_CalcFilterInfo(int32_t layer, int32_t systemGroup, int32_t subSystemId, int32_t subSystemDontCollideWith) { EnsureThreadInfo();
    LOG_CALL(HkpGroupFilter_CalcFilterInfo);
    REQUIRE_FUNCTION_POINTER(HkpGroupFilter_CalcFilterInfo)
    return pHkpGroupFilter_CalcFilterInfo(layer, systemGroup, subSystemId, subSystemDontCollideWith);
}

uint32_t HkpGroupFilter_CalcFilterInfoFromCurrent(uint32_t currentInfo, int32_t collisionLayer) { EnsureThreadInfo();
    LOG_CALL(HkpGroupFilter_CalcFilterInfoFromCurrent);
    REQUIRE_FUNCTION_POINTER(HkpGroupFilter_CalcFilterInfoFromCurrent)
    return pHkpGroupFilter_CalcFilterInfoFromCurrent(currentInfo, collisionLayer);
}

void HkpInertiaTensorComputer_OptimizeInertiasOfConstraintTree(void* constraints, int32_t size, void* rigidBody) { EnsureThreadInfo();
    LOG_CALL(HkpInertiaTensorComputer_OptimizeInertiasOfConstraintTree);
    REQUIRE_FUNCTION_POINTER(HkpInertiaTensorComputer_OptimizeInertiasOfConstraintTree)
    pHkpInertiaTensorComputer_OptimizeInertiasOfConstraintTree(constraints, size, rigidBody);
}

void HkPhysicsSystem_Release(void* physicsSystem) { EnsureThreadInfo();
    LOG_CALL(HkPhysicsSystem_Release);
    REQUIRE_FUNCTION_POINTER(HkPhysicsSystem_Release)
    pHkPhysicsSystem_Release(physicsSystem);
}

float HkRagdollConstraintData_GetPlaneMinAngularLimit(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkRagdollConstraintData_GetPlaneMinAngularLimit);
    REQUIRE_FUNCTION_POINTER(HkRagdollConstraintData_GetPlaneMinAngularLimit)
    return pHkRagdollConstraintData_GetPlaneMinAngularLimit(instance);
}

void HkRagdollConstraintData_SetPlaneMinAngularLimit(void* instance, float value) { EnsureThreadInfo();
    LOG_CALL(HkRagdollConstraintData_SetPlaneMinAngularLimit);
    REQUIRE_FUNCTION_POINTER(HkRagdollConstraintData_SetPlaneMinAngularLimit)
    pHkRagdollConstraintData_SetPlaneMinAngularLimit(instance, value);
}

float HkRagdollConstraintData_GetPlaneMaxAngularLimit(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkRagdollConstraintData_GetPlaneMaxAngularLimit);
    REQUIRE_FUNCTION_POINTER(HkRagdollConstraintData_GetPlaneMaxAngularLimit)
    return pHkRagdollConstraintData_GetPlaneMaxAngularLimit(instance);
}

void HkRagdollConstraintData_SetPlaneMaxAngularLimit(void* instance, float value) { EnsureThreadInfo();
    LOG_CALL(HkRagdollConstraintData_SetPlaneMaxAngularLimit);
    REQUIRE_FUNCTION_POINTER(HkRagdollConstraintData_SetPlaneMaxAngularLimit)
    pHkRagdollConstraintData_SetPlaneMaxAngularLimit(instance, value);
}

float HkRagdollConstraintData_GetTwistMinAngularLimit(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkRagdollConstraintData_GetTwistMinAngularLimit);
    REQUIRE_FUNCTION_POINTER(HkRagdollConstraintData_GetTwistMinAngularLimit)
    return pHkRagdollConstraintData_GetTwistMinAngularLimit(instance);
}

void HkRagdollConstraintData_SetTwistMinAngularLimit(void* instance, float value) { EnsureThreadInfo();
    LOG_CALL(HkRagdollConstraintData_SetTwistMinAngularLimit);
    REQUIRE_FUNCTION_POINTER(HkRagdollConstraintData_SetTwistMinAngularLimit)
    pHkRagdollConstraintData_SetTwistMinAngularLimit(instance, value);
}

float HkRagdollConstraintData_GetTwistMaxAngularLimit(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkRagdollConstraintData_GetTwistMaxAngularLimit);
    REQUIRE_FUNCTION_POINTER(HkRagdollConstraintData_GetTwistMaxAngularLimit)
    return pHkRagdollConstraintData_GetTwistMaxAngularLimit(instance);
}

void HkRagdollConstraintData_SetTwistMaxAngularLimit(void* instance, float value) { EnsureThreadInfo();
    LOG_CALL(HkRagdollConstraintData_SetTwistMaxAngularLimit);
    REQUIRE_FUNCTION_POINTER(HkRagdollConstraintData_SetTwistMaxAngularLimit)
    pHkRagdollConstraintData_SetTwistMaxAngularLimit(instance, value);
}

float HkRagdollConstraintData_GetMaxFrictionTorque(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkRagdollConstraintData_GetMaxFrictionTorque);
    REQUIRE_FUNCTION_POINTER(HkRagdollConstraintData_GetMaxFrictionTorque)
    return pHkRagdollConstraintData_GetMaxFrictionTorque(instance);
}

void HkRagdollConstraintData_SetMaxFrictionTorque(void* instance, float value) { EnsureThreadInfo();
    LOG_CALL(HkRagdollConstraintData_SetMaxFrictionTorque);
    REQUIRE_FUNCTION_POINTER(HkRagdollConstraintData_SetMaxFrictionTorque)
    pHkRagdollConstraintData_SetMaxFrictionTorque(instance, value);
}

void HkRagdollConstraintData_SetInBodySpaceInternal(void* instance, Vector3 pivotA, Vector3 pivotB, Vector3 planeAxisA, Vector3 planeAxisB, Vector3 twistAxisA, Vector3 twistAxisB) { EnsureThreadInfo();
    LOG_CALL(HkRagdollConstraintData_SetInBodySpaceInternal);
    REQUIRE_FUNCTION_POINTER(HkRagdollConstraintData_SetInBodySpaceInternal)
    pHkRagdollConstraintData_SetInBodySpaceInternal(instance, pivotA, pivotB, planeAxisA, planeAxisB, twistAxisA, twistAxisB);
}

void HkRagdollConstraintData_SetAsymmetricConeAngle(void* instance, float coneMin, float coneMax) { EnsureThreadInfo();
    LOG_CALL(HkRagdollConstraintData_SetAsymmetricConeAngle);
    REQUIRE_FUNCTION_POINTER(HkRagdollConstraintData_SetAsymmetricConeAngle)
    pHkRagdollConstraintData_SetAsymmetricConeAngle(instance, coneMin, coneMax);
}

void HkRagdollConstraintData_SetConeLimitStabilization(void* instance, bool enable) { EnsureThreadInfo();
    LOG_CALL(HkRagdollConstraintData_SetConeLimitStabilization);
    REQUIRE_FUNCTION_POINTER(HkRagdollConstraintData_SetConeLimitStabilization)
    pHkRagdollConstraintData_SetConeLimitStabilization(instance, enable);
}

void HkReferenceObject_AddReference(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkReferenceObject_AddReference);
    REQUIRE_FUNCTION_POINTER(HkReferenceObject_AddReference)
    pHkReferenceObject_AddReference(instance);
}

void HkReferenceObject_RemoveReference(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkReferenceObject_RemoveReference);
    REQUIRE_FUNCTION_POINTER(HkReferenceObject_RemoveReference)
    pHkReferenceObject_RemoveReference(instance);
}

bool HkReferenceObject_IsValid(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkReferenceObject_IsValid);
    REQUIRE_FUNCTION_POINTER(HkReferenceObject_IsValid)
    return pHkReferenceObject_IsValid(instance);
}

void HkReferenceObject_DebugRemoveRef(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkReferenceObject_DebugRemoveRef);
    REQUIRE_FUNCTION_POINTER(HkReferenceObject_DebugRemoveRef)
    pHkReferenceObject_DebugRemoveRef(instance);
}

int32_t HkReferenceObject_ReferenceCount(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkReferenceObject_ReferenceCount);
    REQUIRE_FUNCTION_POINTER(HkReferenceObject_ReferenceCount)
    return pHkReferenceObject_ReferenceCount(instance);
}

void* HkRigidBody_Create(void* bodyInfo) { EnsureThreadInfo();
    LOG_CALL(HkRigidBody_Create);
    REQUIRE_FUNCTION_POINTER(HkRigidBody_Create)
    return pHkRigidBody_Create(bodyInfo);
}

void* HkRigidBody_CreateWithCustomVelocity(void* bodyInfo) { EnsureThreadInfo();
    LOG_CALL(HkRigidBody_CreateWithCustomVelocity);
    REQUIRE_FUNCTION_POINTER(HkRigidBody_CreateWithCustomVelocity)
    return pHkRigidBody_CreateWithCustomVelocity(bodyInfo);
}

void HkRigidBody_SetNumShapeKeysInContactPointProperties(void* instance, int32_t value) { EnsureThreadInfo();
    LOG_CALL(HkRigidBody_SetNumShapeKeysInContactPointProperties);
    REQUIRE_FUNCTION_POINTER(HkRigidBody_SetNumShapeKeysInContactPointProperties)
    pHkRigidBody_SetNumShapeKeysInContactPointProperties(instance, value);
}

int32_t HkRigidBody_GetResponseModifiers(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkRigidBody_GetResponseModifiers);
    REQUIRE_FUNCTION_POINTER(HkRigidBody_GetResponseModifiers)
    return pHkRigidBody_GetResponseModifiers(instance);
}

void HkRigidBody_SetResponseModifiers(void* instance, int32_t value) { EnsureThreadInfo();
    LOG_CALL(HkRigidBody_SetResponseModifiers);
    REQUIRE_FUNCTION_POINTER(HkRigidBody_SetResponseModifiers)
    pHkRigidBody_SetResponseModifiers(instance, value);
}

void* HkRigidBody_GetShape(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkRigidBody_GetShape);
    REQUIRE_FUNCTION_POINTER(HkRigidBody_GetShape)
    return pHkRigidBody_GetShape(instance);
}

int32_t HkRigidBody_SetShape(void* instance, void* shape) { EnsureThreadInfo();
    LOG_CALL(HkRigidBody_SetShape);
    REQUIRE_FUNCTION_POINTER(HkRigidBody_SetShape)
    return pHkRigidBody_SetShape(instance, shape);
}

int32_t HkRigidBody_UpdateShape(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkRigidBody_UpdateShape);
    REQUIRE_FUNCTION_POINTER(HkRigidBody_UpdateShape)
    return pHkRigidBody_UpdateShape(instance);
}

Matrix HkRigidBody_PredictRigidBodyMatrix(void* instance, float deltaTime, void* world) { EnsureThreadInfo();
    LOG_CALL(HkRigidBody_PredictRigidBodyMatrix);
    REQUIRE_FUNCTION_POINTER(HkRigidBody_PredictRigidBodyMatrix)
    return pHkRigidBody_PredictRigidBodyMatrix(instance, deltaTime, world);
}

void HkRigidBody_SetMassProperties(void* instance, HkMassProperties properties) { EnsureThreadInfo();
    LOG_CALL(HkRigidBody_SetMassProperties);
    REQUIRE_FUNCTION_POINTER(HkRigidBody_SetMassProperties)
    pHkRigidBody_SetMassProperties(instance, properties);
}

void HkRigidBody_SetWorldMatrix(void* instance, Matrix m) { EnsureThreadInfo();
    LOG_CALL(HkRigidBody_SetWorldMatrix);
    REQUIRE_FUNCTION_POINTER(HkRigidBody_SetWorldMatrix)
    pHkRigidBody_SetWorldMatrix(instance, m);
}

void HkRigidBody_SetTransform(void* instance, Matrix m) { EnsureThreadInfo();
    LOG_CALL(HkRigidBody_SetTransform);
    REQUIRE_FUNCTION_POINTER(HkRigidBody_SetTransform)
    pHkRigidBody_SetTransform(instance, m);
}

bool HkRigidBody_GetEnableDeactivation(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkRigidBody_GetEnableDeactivation);
    REQUIRE_FUNCTION_POINTER(HkRigidBody_GetEnableDeactivation)
    return pHkRigidBody_GetEnableDeactivation(instance);
}

void HkRigidBody_SetEnableDeactivation(void* instance, bool value) { EnsureThreadInfo();
    LOG_CALL(HkRigidBody_SetEnableDeactivation);
    REQUIRE_FUNCTION_POINTER(HkRigidBody_SetEnableDeactivation)
    pHkRigidBody_SetEnableDeactivation(instance, value);
}

bool HkRigidBody_GetMarkedForVelocityRecompute(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkRigidBody_GetMarkedForVelocityRecompute);
    REQUIRE_FUNCTION_POINTER(HkRigidBody_GetMarkedForVelocityRecompute)
    return pHkRigidBody_GetMarkedForVelocityRecompute(instance);
}

void HkRigidBody_SetMarkedForVelocityRecompute(void* instance, bool value) { EnsureThreadInfo();
    LOG_CALL(HkRigidBody_SetMarkedForVelocityRecompute);
    REQUIRE_FUNCTION_POINTER(HkRigidBody_SetMarkedForVelocityRecompute)
    pHkRigidBody_SetMarkedForVelocityRecompute(instance, value);
}

void* HkRigidBody_GetMotion(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkRigidBody_GetMotion);
    REQUIRE_FUNCTION_POINTER(HkRigidBody_GetMotion)
    return pHkRigidBody_GetMotion(instance);
}

float HkRigidBody_GetMass(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkRigidBody_GetMass);
    REQUIRE_FUNCTION_POINTER(HkRigidBody_GetMass)
    return pHkRigidBody_GetMass(instance);
}

void HkRigidBody_SetMass(void* instance, float value) { EnsureThreadInfo();
    LOG_CALL(HkRigidBody_SetMass);
    REQUIRE_FUNCTION_POINTER(HkRigidBody_SetMass)
    pHkRigidBody_SetMass(instance, value);
}

Vector3 HkRigidBody_GetCenterOfMassLocal(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkRigidBody_GetCenterOfMassLocal);
    REQUIRE_FUNCTION_POINTER(HkRigidBody_GetCenterOfMassLocal)
    return pHkRigidBody_GetCenterOfMassLocal(instance);
}

void HkRigidBody_SetCenterOfMassLocal(void* instance, Vector3 value) { EnsureThreadInfo();
    LOG_CALL(HkRigidBody_SetCenterOfMassLocal);
    REQUIRE_FUNCTION_POINTER(HkRigidBody_SetCenterOfMassLocal)
    pHkRigidBody_SetCenterOfMassLocal(instance, value);
}

Matrix HkRigidBody_GetInertiaTensor(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkRigidBody_GetInertiaTensor);
    REQUIRE_FUNCTION_POINTER(HkRigidBody_GetInertiaTensor)
    return pHkRigidBody_GetInertiaTensor(instance);
}

void HkRigidBody_SetInertiaTensor(void* instance, Matrix value) { EnsureThreadInfo();
    LOG_CALL(HkRigidBody_SetInertiaTensor);
    REQUIRE_FUNCTION_POINTER(HkRigidBody_SetInertiaTensor)
    pHkRigidBody_SetInertiaTensor(instance, value);
}

Matrix HkRigidBody_GetInverseInertiaTensor(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkRigidBody_GetInverseInertiaTensor);
    REQUIRE_FUNCTION_POINTER(HkRigidBody_GetInverseInertiaTensor)
    return pHkRigidBody_GetInverseInertiaTensor(instance);
}

void HkRigidBody_SetInverseInertiaTensor(void* instance, Matrix value) { EnsureThreadInfo();
    LOG_CALL(HkRigidBody_SetInverseInertiaTensor);
    REQUIRE_FUNCTION_POINTER(HkRigidBody_SetInverseInertiaTensor)
    pHkRigidBody_SetInverseInertiaTensor(instance, value);
}

Vector3 HkRigidBody_GetCenterOfMassWorld(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkRigidBody_GetCenterOfMassWorld);
    REQUIRE_FUNCTION_POINTER(HkRigidBody_GetCenterOfMassWorld)
    return pHkRigidBody_GetCenterOfMassWorld(instance);
}

bool HkRigidBody_GetCustomVelocity(void* instance, void* velocity) { EnsureThreadInfo();
    LOG_CALL(HkRigidBody_GetCustomVelocity);
    REQUIRE_FUNCTION_POINTER(HkRigidBody_GetCustomVelocity)
    return pHkRigidBody_GetCustomVelocity(instance, velocity);
}

void HkRigidBody_SetCustomVelocity(void* instance, Vector3 value, bool valid) { EnsureThreadInfo();
    LOG_CALL(HkRigidBody_SetCustomVelocity);
    REQUIRE_FUNCTION_POINTER(HkRigidBody_SetCustomVelocity)
    pHkRigidBody_SetCustomVelocity(instance, value, valid);
}

Vector4 HkRigidBody_GetDeltaAngle(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkRigidBody_GetDeltaAngle);
    REQUIRE_FUNCTION_POINTER(HkRigidBody_GetDeltaAngle)
    return pHkRigidBody_GetDeltaAngle(instance);
}

float HkRigidBody_GetLinearDamping(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkRigidBody_GetLinearDamping);
    REQUIRE_FUNCTION_POINTER(HkRigidBody_GetLinearDamping)
    return pHkRigidBody_GetLinearDamping(instance);
}

void HkRigidBody_SetLinearDamping(void* instance, float value) { EnsureThreadInfo();
    LOG_CALL(HkRigidBody_SetLinearDamping);
    REQUIRE_FUNCTION_POINTER(HkRigidBody_SetLinearDamping)
    pHkRigidBody_SetLinearDamping(instance, value);
}

float HkRigidBody_GetAngularDamping(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkRigidBody_GetAngularDamping);
    REQUIRE_FUNCTION_POINTER(HkRigidBody_GetAngularDamping)
    return pHkRigidBody_GetAngularDamping(instance);
}

void HkRigidBody_SetAngularDamping(void* instance, float value) { EnsureThreadInfo();
    LOG_CALL(HkRigidBody_SetAngularDamping);
    REQUIRE_FUNCTION_POINTER(HkRigidBody_SetAngularDamping)
    pHkRigidBody_SetAngularDamping(instance, value);
}

float HkRigidBody_GetMaxLinearVelocity(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkRigidBody_GetMaxLinearVelocity);
    REQUIRE_FUNCTION_POINTER(HkRigidBody_GetMaxLinearVelocity)
    return pHkRigidBody_GetMaxLinearVelocity(instance);
}

void HkRigidBody_SetMaxLinearVelocity(void* instance, float value) { EnsureThreadInfo();
    LOG_CALL(HkRigidBody_SetMaxLinearVelocity);
    REQUIRE_FUNCTION_POINTER(HkRigidBody_SetMaxLinearVelocity)
    pHkRigidBody_SetMaxLinearVelocity(instance, value);
}

float HkRigidBody_GetMaxAngularVelocity(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkRigidBody_GetMaxAngularVelocity);
    REQUIRE_FUNCTION_POINTER(HkRigidBody_GetMaxAngularVelocity)
    return pHkRigidBody_GetMaxAngularVelocity(instance);
}

void HkRigidBody_SetMaxAngularVelocity(void* instance, float value) { EnsureThreadInfo();
    LOG_CALL(HkRigidBody_SetMaxAngularVelocity);
    REQUIRE_FUNCTION_POINTER(HkRigidBody_SetMaxAngularVelocity)
    pHkRigidBody_SetMaxAngularVelocity(instance, value);
}

float HkRigidBody_GetAllowedPenetrationDepth(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkRigidBody_GetAllowedPenetrationDepth);
    REQUIRE_FUNCTION_POINTER(HkRigidBody_GetAllowedPenetrationDepth)
    return pHkRigidBody_GetAllowedPenetrationDepth(instance);
}

void HkRigidBody_SetAllowedPenetrationDepth(void* instance, float value) { EnsureThreadInfo();
    LOG_CALL(HkRigidBody_SetAllowedPenetrationDepth);
    REQUIRE_FUNCTION_POINTER(HkRigidBody_SetAllowedPenetrationDepth)
    pHkRigidBody_SetAllowedPenetrationDepth(instance, value);
}

float HkRigidBody_GetFriction(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkRigidBody_GetFriction);
    REQUIRE_FUNCTION_POINTER(HkRigidBody_GetFriction)
    return pHkRigidBody_GetFriction(instance);
}

void HkRigidBody_SetFriction(void* instance, float value) { EnsureThreadInfo();
    LOG_CALL(HkRigidBody_SetFriction);
    REQUIRE_FUNCTION_POINTER(HkRigidBody_SetFriction)
    pHkRigidBody_SetFriction(instance, value);
}

float HkRigidBody_GetRestitution(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkRigidBody_GetRestitution);
    REQUIRE_FUNCTION_POINTER(HkRigidBody_GetRestitution)
    return pHkRigidBody_GetRestitution(instance);
}

void HkRigidBody_SetRestitution(void* instance, float value) { EnsureThreadInfo();
    LOG_CALL(HkRigidBody_SetRestitution);
    REQUIRE_FUNCTION_POINTER(HkRigidBody_SetRestitution)
    pHkRigidBody_SetRestitution(instance, value);
}

void HkRigidBody_ApplyLinearImpulse(void* instance, Vector3 impulse) { EnsureThreadInfo();
    LOG_CALL(HkRigidBody_ApplyLinearImpulse);
    REQUIRE_FUNCTION_POINTER(HkRigidBody_ApplyLinearImpulse)
    pHkRigidBody_ApplyLinearImpulse(instance, impulse);
}

void HkRigidBody_ApplyPointImpulse(void* instance, Vector3 impulse, Vector3 point) { EnsureThreadInfo();
    LOG_CALL(HkRigidBody_ApplyPointImpulse);
    REQUIRE_FUNCTION_POINTER(HkRigidBody_ApplyPointImpulse)
    pHkRigidBody_ApplyPointImpulse(instance, impulse, point);
}

void HkRigidBody_ApplyAngularImpulse(void* instance, Vector3 impulse) { EnsureThreadInfo();
    LOG_CALL(HkRigidBody_ApplyAngularImpulse);
    REQUIRE_FUNCTION_POINTER(HkRigidBody_ApplyAngularImpulse)
    pHkRigidBody_ApplyAngularImpulse(instance, impulse);
}

void HkRigidBody_SetLayer(void* instance, int32_t value) { EnsureThreadInfo();
    LOG_CALL(HkRigidBody_SetLayer);
    REQUIRE_FUNCTION_POINTER(HkRigidBody_SetLayer)
    pHkRigidBody_SetLayer(instance, value);
}

uint32_t HkRigidBody_GetCollisionFilterInfo(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkRigidBody_GetCollisionFilterInfo);
    REQUIRE_FUNCTION_POINTER(HkRigidBody_GetCollisionFilterInfo)
    return pHkRigidBody_GetCollisionFilterInfo(instance);
}

void HkRigidBody_SetCollisionFilterInfo(void* instance, uint32_t info) { EnsureThreadInfo();
    LOG_CALL(HkRigidBody_SetCollisionFilterInfo);
    REQUIRE_FUNCTION_POINTER(HkRigidBody_SetCollisionFilterInfo)
    pHkRigidBody_SetCollisionFilterInfo(instance, info);
}

void HkRigidBody_ApplyForce(void* instance, float time, Vector3 force) { EnsureThreadInfo();
    LOG_CALL(HkRigidBody_ApplyForce);
    REQUIRE_FUNCTION_POINTER(HkRigidBody_ApplyForce)
    pHkRigidBody_ApplyForce(instance, time, force);
}

void HkRigidBody_ApplyForceToPoint(void* instance, float time, Vector3 force, Vector3 point) { EnsureThreadInfo();
    LOG_CALL(HkRigidBody_ApplyForceToPoint);
    REQUIRE_FUNCTION_POINTER(HkRigidBody_ApplyForceToPoint)
    pHkRigidBody_ApplyForceToPoint(instance, time, force, point);
}

void HkRigidBody_ApplyTorque(void* instance, float time, Vector3 torque) { EnsureThreadInfo();
    LOG_CALL(HkRigidBody_ApplyTorque);
    REQUIRE_FUNCTION_POINTER(HkRigidBody_ApplyTorque)
    pHkRigidBody_ApplyTorque(instance, time, torque);
}

void* HkRigidBody_GetNativeObjectName(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkRigidBody_GetNativeObjectName);
    REQUIRE_FUNCTION_POINTER(HkRigidBody_GetNativeObjectName)
    return pHkRigidBody_GetNativeObjectName(instance);
}

void HkRigidBody_RemoveFromWorld(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkRigidBody_RemoveFromWorld);
    REQUIRE_FUNCTION_POINTER(HkRigidBody_RemoveFromWorld)
    pHkRigidBody_RemoveFromWorld(instance);
}

bool HkRigidBody_HasGravity(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkRigidBody_HasGravity);
    REQUIRE_FUNCTION_POINTER(HkRigidBody_HasGravity)
    return pHkRigidBody_HasGravity(instance);
}

bool HkRigidBody_HasConstraints(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkRigidBody_HasConstraints);
    REQUIRE_FUNCTION_POINTER(HkRigidBody_HasConstraints)
    return pHkRigidBody_HasConstraints(instance);
}

void* HkRigidBody_GetBreakableBody(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkRigidBody_GetBreakableBody);
    REQUIRE_FUNCTION_POINTER(HkRigidBody_GetBreakableBody)
    return pHkRigidBody_GetBreakableBody(instance);
}

Vector3 HkRigidBody_GetGravity(void* gravityAction) { EnsureThreadInfo();
    LOG_CALL(HkRigidBody_GetGravity);
    REQUIRE_FUNCTION_POINTER(HkRigidBody_GetGravity)
    return pHkRigidBody_GetGravity(gravityAction);
}

void HkRigidBody_ReleaseGravity(void* gravityAction) { EnsureThreadInfo();
    LOG_CALL(HkRigidBody_ReleaseGravity);
    REQUIRE_FUNCTION_POINTER(HkRigidBody_ReleaseGravity)
    pHkRigidBody_ReleaseGravity(gravityAction);
}

void HkRigidBody_SetGravity(void* gravityAction, Vector3 gravity) { EnsureThreadInfo();
    LOG_CALL(HkRigidBody_SetGravity);
    REQUIRE_FUNCTION_POINTER(HkRigidBody_SetGravity)
    pHkRigidBody_SetGravity(gravityAction, gravity);
}

void* HkRigidBody_Clone(void* cloneBody) { EnsureThreadInfo();
    LOG_CALL(HkRigidBody_Clone);
    REQUIRE_FUNCTION_POINTER(HkRigidBody_Clone)
    return pHkRigidBody_Clone(cloneBody);
}

void* HkRigidBody_FromShape(void* shape) { EnsureThreadInfo();
    LOG_CALL(HkRigidBody_FromShape);
    REQUIRE_FUNCTION_POINTER(HkRigidBody_FromShape)
    return pHkRigidBody_FromShape(shape);
}

uint64_t HkRigidBody_GetGcRoot(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkRigidBody_GetGcRoot);
    REQUIRE_FUNCTION_POINTER(HkRigidBody_GetGcRoot)
    return pHkRigidBody_GetGcRoot(instance);
}

void* HkRigidBody_GetGravityAction(void* instance, void* action, Vector3 gravity) { EnsureThreadInfo();
    LOG_CALL(HkRigidBody_GetGravityAction);
    REQUIRE_FUNCTION_POINTER(HkRigidBody_GetGravityAction)
    return pHkRigidBody_GetGravityAction(instance, action, gravity);
}

void HkRigidBody_AddGravityAction(void* instance, void* action) { EnsureThreadInfo();
    LOG_CALL(HkRigidBody_AddGravityAction);
    REQUIRE_FUNCTION_POINTER(HkRigidBody_AddGravityAction)
    pHkRigidBody_AddGravityAction(instance, action);
}

int32_t HkRigidBody_GetDeactivationCounter0(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkRigidBody_GetDeactivationCounter0);
    REQUIRE_FUNCTION_POINTER(HkRigidBody_GetDeactivationCounter0)
    return pHkRigidBody_GetDeactivationCounter0(instance);
}

int32_t HkRigidBody_GetDeactivationCounter1(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkRigidBody_GetDeactivationCounter1);
    REQUIRE_FUNCTION_POINTER(HkRigidBody_GetDeactivationCounter1)
    return pHkRigidBody_GetDeactivationCounter1(instance);
}

bool HkRigidBody_HasActions(void* instance, int32_t actionType) { EnsureThreadInfo();
    LOG_CALL(HkRigidBody_HasActions);
    REQUIRE_FUNCTION_POINTER(HkRigidBody_HasActions)
    return pHkRigidBody_HasActions(instance, actionType);
}

void* HkRigidBodyCinfo_Create(void) { EnsureThreadInfo();
    LOG_CALL(HkRigidBodyCinfo_Create);
    REQUIRE_FUNCTION_POINTER(HkRigidBodyCinfo_Create)
    return pHkRigidBodyCinfo_Create();
}

void HkRigidBodyCinfo_Release(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkRigidBodyCinfo_Release);
    REQUIRE_FUNCTION_POINTER(HkRigidBodyCinfo_Release)
    pHkRigidBodyCinfo_Release(instance);
}

int32_t HkRigidBodyCinfo_GetCollisionResponse(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkRigidBodyCinfo_GetCollisionResponse);
    REQUIRE_FUNCTION_POINTER(HkRigidBodyCinfo_GetCollisionResponse)
    return pHkRigidBodyCinfo_GetCollisionResponse(instance);
}

void HkRigidBodyCinfo_SetCollisionResponse(void* instance, int32_t value) { EnsureThreadInfo();
    LOG_CALL(HkRigidBodyCinfo_SetCollisionResponse);
    REQUIRE_FUNCTION_POINTER(HkRigidBodyCinfo_SetCollisionResponse)
    pHkRigidBodyCinfo_SetCollisionResponse(instance, value);
}

int32_t HkRigidBodyCinfo_GetResponseModifiers(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkRigidBodyCinfo_GetResponseModifiers);
    REQUIRE_FUNCTION_POINTER(HkRigidBodyCinfo_GetResponseModifiers)
    return pHkRigidBodyCinfo_GetResponseModifiers(instance);
}

void HkRigidBodyCinfo_SetResponseModifiers(void* instance, int32_t value) { EnsureThreadInfo();
    LOG_CALL(HkRigidBodyCinfo_SetResponseModifiers);
    REQUIRE_FUNCTION_POINTER(HkRigidBodyCinfo_SetResponseModifiers)
    pHkRigidBodyCinfo_SetResponseModifiers(instance, value);
}

Vector3 HkRigidBodyCinfo_GetPosition(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkRigidBodyCinfo_GetPosition);
    REQUIRE_FUNCTION_POINTER(HkRigidBodyCinfo_GetPosition)
    return pHkRigidBodyCinfo_GetPosition(instance);
}

void HkRigidBodyCinfo_SetPosition(void* instance, Vector3 value) { EnsureThreadInfo();
    LOG_CALL(HkRigidBodyCinfo_SetPosition);
    REQUIRE_FUNCTION_POINTER(HkRigidBodyCinfo_SetPosition)
    pHkRigidBodyCinfo_SetPosition(instance, value);
}

Quaternion HkRigidBodyCinfo_GetRotation(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkRigidBodyCinfo_GetRotation);
    REQUIRE_FUNCTION_POINTER(HkRigidBodyCinfo_GetRotation)
    return pHkRigidBodyCinfo_GetRotation(instance);
}

void HkRigidBodyCinfo_SetRotation(void* instance, Quaternion value) { EnsureThreadInfo();
    LOG_CALL(HkRigidBodyCinfo_SetRotation);
    REQUIRE_FUNCTION_POINTER(HkRigidBodyCinfo_SetRotation)
    pHkRigidBodyCinfo_SetRotation(instance, value);
}

Vector3 HkRigidBodyCinfo_GetLinearVelocity(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkRigidBodyCinfo_GetLinearVelocity);
    REQUIRE_FUNCTION_POINTER(HkRigidBodyCinfo_GetLinearVelocity)
    return pHkRigidBodyCinfo_GetLinearVelocity(instance);
}

void HkRigidBodyCinfo_SetLinearVelocity(void* instance, Vector3 value) { EnsureThreadInfo();
    LOG_CALL(HkRigidBodyCinfo_SetLinearVelocity);
    REQUIRE_FUNCTION_POINTER(HkRigidBodyCinfo_SetLinearVelocity)
    pHkRigidBodyCinfo_SetLinearVelocity(instance, value);
}

Vector3 HkRigidBodyCinfo_GetAngularVelocity(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkRigidBodyCinfo_GetAngularVelocity);
    REQUIRE_FUNCTION_POINTER(HkRigidBodyCinfo_GetAngularVelocity)
    return pHkRigidBodyCinfo_GetAngularVelocity(instance);
}

void HkRigidBodyCinfo_SetAngularVelocity(void* instance, Vector3 value) { EnsureThreadInfo();
    LOG_CALL(HkRigidBodyCinfo_SetAngularVelocity);
    REQUIRE_FUNCTION_POINTER(HkRigidBodyCinfo_SetAngularVelocity)
    pHkRigidBodyCinfo_SetAngularVelocity(instance, value);
}

Vector3 HkRigidBodyCinfo_GetCenterOfMass(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkRigidBodyCinfo_GetCenterOfMass);
    REQUIRE_FUNCTION_POINTER(HkRigidBodyCinfo_GetCenterOfMass)
    return pHkRigidBodyCinfo_GetCenterOfMass(instance);
}

void HkRigidBodyCinfo_SetCenterOfMass(void* instance, Vector3 value) { EnsureThreadInfo();
    LOG_CALL(HkRigidBodyCinfo_SetCenterOfMass);
    REQUIRE_FUNCTION_POINTER(HkRigidBodyCinfo_SetCenterOfMass)
    pHkRigidBodyCinfo_SetCenterOfMass(instance, value);
}

float HkRigidBodyCinfo_GetMass(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkRigidBodyCinfo_GetMass);
    REQUIRE_FUNCTION_POINTER(HkRigidBodyCinfo_GetMass)
    return pHkRigidBodyCinfo_GetMass(instance);
}

void HkRigidBodyCinfo_SetMass(void* instance, float value) { EnsureThreadInfo();
    LOG_CALL(HkRigidBodyCinfo_SetMass);
    REQUIRE_FUNCTION_POINTER(HkRigidBodyCinfo_SetMass)
    pHkRigidBodyCinfo_SetMass(instance, value);
}

float HkRigidBodyCinfo_GetLinearDamping(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkRigidBodyCinfo_GetLinearDamping);
    REQUIRE_FUNCTION_POINTER(HkRigidBodyCinfo_GetLinearDamping)
    return pHkRigidBodyCinfo_GetLinearDamping(instance);
}

void HkRigidBodyCinfo_SetLinearDamping(void* instance, float value) { EnsureThreadInfo();
    LOG_CALL(HkRigidBodyCinfo_SetLinearDamping);
    REQUIRE_FUNCTION_POINTER(HkRigidBodyCinfo_SetLinearDamping)
    pHkRigidBodyCinfo_SetLinearDamping(instance, value);
}

float HkRigidBodyCinfo_GetAngularDamping(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkRigidBodyCinfo_GetAngularDamping);
    REQUIRE_FUNCTION_POINTER(HkRigidBodyCinfo_GetAngularDamping)
    return pHkRigidBodyCinfo_GetAngularDamping(instance);
}

void HkRigidBodyCinfo_SetAngularDamping(void* instance, float value) { EnsureThreadInfo();
    LOG_CALL(HkRigidBodyCinfo_SetAngularDamping);
    REQUIRE_FUNCTION_POINTER(HkRigidBodyCinfo_SetAngularDamping)
    pHkRigidBodyCinfo_SetAngularDamping(instance, value);
}

float HkRigidBodyCinfo_GetFriction(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkRigidBodyCinfo_GetFriction);
    REQUIRE_FUNCTION_POINTER(HkRigidBodyCinfo_GetFriction)
    return pHkRigidBodyCinfo_GetFriction(instance);
}

void HkRigidBodyCinfo_SetFriction(void* instance, float value) { EnsureThreadInfo();
    LOG_CALL(HkRigidBodyCinfo_SetFriction);
    REQUIRE_FUNCTION_POINTER(HkRigidBodyCinfo_SetFriction)
    pHkRigidBodyCinfo_SetFriction(instance, value);
}

float HkRigidBodyCinfo_GetRestitution(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkRigidBodyCinfo_GetRestitution);
    REQUIRE_FUNCTION_POINTER(HkRigidBodyCinfo_GetRestitution)
    return pHkRigidBodyCinfo_GetRestitution(instance);
}

void HkRigidBodyCinfo_SetRestitution(void* instance, float value) { EnsureThreadInfo();
    LOG_CALL(HkRigidBodyCinfo_SetRestitution);
    REQUIRE_FUNCTION_POINTER(HkRigidBodyCinfo_SetRestitution)
    pHkRigidBodyCinfo_SetRestitution(instance, value);
}

float HkRigidBodyCinfo_GetMaxLinearVelocity(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkRigidBodyCinfo_GetMaxLinearVelocity);
    REQUIRE_FUNCTION_POINTER(HkRigidBodyCinfo_GetMaxLinearVelocity)
    return pHkRigidBodyCinfo_GetMaxLinearVelocity(instance);
}

void HkRigidBodyCinfo_SetMaxLinearVelocity(void* instance, float value) { EnsureThreadInfo();
    LOG_CALL(HkRigidBodyCinfo_SetMaxLinearVelocity);
    REQUIRE_FUNCTION_POINTER(HkRigidBodyCinfo_SetMaxLinearVelocity)
    pHkRigidBodyCinfo_SetMaxLinearVelocity(instance, value);
}

float HkRigidBodyCinfo_GetMaxAngularVelocity(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkRigidBodyCinfo_GetMaxAngularVelocity);
    REQUIRE_FUNCTION_POINTER(HkRigidBodyCinfo_GetMaxAngularVelocity)
    return pHkRigidBodyCinfo_GetMaxAngularVelocity(instance);
}

void HkRigidBodyCinfo_SetMaxAngularVelocity(void* instance, float value) { EnsureThreadInfo();
    LOG_CALL(HkRigidBodyCinfo_SetMaxAngularVelocity);
    REQUIRE_FUNCTION_POINTER(HkRigidBodyCinfo_SetMaxAngularVelocity)
    pHkRigidBodyCinfo_SetMaxAngularVelocity(instance, value);
}

uint16_t HkRigidBodyCinfo_GetContactPointCallbackDelay(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkRigidBodyCinfo_GetContactPointCallbackDelay);
    REQUIRE_FUNCTION_POINTER(HkRigidBodyCinfo_GetContactPointCallbackDelay)
    return pHkRigidBodyCinfo_GetContactPointCallbackDelay(instance);
}

void HkRigidBodyCinfo_SetContactPointCallbackDelay(void* instance, uint16_t value) { EnsureThreadInfo();
    LOG_CALL(HkRigidBodyCinfo_SetContactPointCallbackDelay);
    REQUIRE_FUNCTION_POINTER(HkRigidBodyCinfo_SetContactPointCallbackDelay)
    pHkRigidBodyCinfo_SetContactPointCallbackDelay(instance, value);
}

float HkRigidBodyCinfo_GetAllowedPenetrationDepth(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkRigidBodyCinfo_GetAllowedPenetrationDepth);
    REQUIRE_FUNCTION_POINTER(HkRigidBodyCinfo_GetAllowedPenetrationDepth)
    return pHkRigidBodyCinfo_GetAllowedPenetrationDepth(instance);
}

void HkRigidBodyCinfo_SetAllowedPenetrationDepth(void* instance, float value) { EnsureThreadInfo();
    LOG_CALL(HkRigidBodyCinfo_SetAllowedPenetrationDepth);
    REQUIRE_FUNCTION_POINTER(HkRigidBodyCinfo_SetAllowedPenetrationDepth)
    pHkRigidBodyCinfo_SetAllowedPenetrationDepth(instance, value);
}

int32_t HkRigidBodyCinfo_GetMotionType(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkRigidBodyCinfo_GetMotionType);
    REQUIRE_FUNCTION_POINTER(HkRigidBodyCinfo_GetMotionType)
    return pHkRigidBodyCinfo_GetMotionType(instance);
}

void HkRigidBodyCinfo_SetMotionType(void* instance, int32_t value) { EnsureThreadInfo();
    LOG_CALL(HkRigidBodyCinfo_SetMotionType);
    REQUIRE_FUNCTION_POINTER(HkRigidBodyCinfo_SetMotionType)
    pHkRigidBodyCinfo_SetMotionType(instance, value);
}

int32_t HkRigidBodyCinfo_GetSolverDeactivation(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkRigidBodyCinfo_GetSolverDeactivation);
    REQUIRE_FUNCTION_POINTER(HkRigidBodyCinfo_GetSolverDeactivation)
    return pHkRigidBodyCinfo_GetSolverDeactivation(instance);
}

void HkRigidBodyCinfo_SetSolverDeactivation(void* instance, int32_t value) { EnsureThreadInfo();
    LOG_CALL(HkRigidBodyCinfo_SetSolverDeactivation);
    REQUIRE_FUNCTION_POINTER(HkRigidBodyCinfo_SetSolverDeactivation)
    pHkRigidBodyCinfo_SetSolverDeactivation(instance, value);
}

int32_t HkRigidBodyCinfo_GetQualityType(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkRigidBodyCinfo_GetQualityType);
    REQUIRE_FUNCTION_POINTER(HkRigidBodyCinfo_GetQualityType)
    return pHkRigidBodyCinfo_GetQualityType(instance);
}

void HkRigidBodyCinfo_SetQualityType(void* instance, int32_t value) { EnsureThreadInfo();
    LOG_CALL(HkRigidBodyCinfo_SetQualityType);
    REQUIRE_FUNCTION_POINTER(HkRigidBodyCinfo_SetQualityType)
    pHkRigidBodyCinfo_SetQualityType(instance, value);
}

int8_t HkRigidBodyCinfo_GetAutoRemoveLevel(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkRigidBodyCinfo_GetAutoRemoveLevel);
    REQUIRE_FUNCTION_POINTER(HkRigidBodyCinfo_GetAutoRemoveLevel)
    return pHkRigidBodyCinfo_GetAutoRemoveLevel(instance);
}

void HkRigidBodyCinfo_SetAutoRemoveLevel(void* instance, int8_t value) { EnsureThreadInfo();
    LOG_CALL(HkRigidBodyCinfo_SetAutoRemoveLevel);
    REQUIRE_FUNCTION_POINTER(HkRigidBodyCinfo_SetAutoRemoveLevel)
    pHkRigidBodyCinfo_SetAutoRemoveLevel(instance, value);
}

void* HkRigidBodyCinfo_GetShape(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkRigidBodyCinfo_GetShape);
    REQUIRE_FUNCTION_POINTER(HkRigidBodyCinfo_GetShape)
    return pHkRigidBodyCinfo_GetShape(instance);
}

void HkRigidBodyCinfo_SetShape(void* instance, void* value) { EnsureThreadInfo();
    LOG_CALL(HkRigidBodyCinfo_SetShape);
    REQUIRE_FUNCTION_POINTER(HkRigidBodyCinfo_SetShape)
    pHkRigidBodyCinfo_SetShape(instance, value);
}

void HkRigidBodyCinfo_CalculateBoxInertiaTensor(void* instance, Vector3 halfExtents, float mass) { EnsureThreadInfo();
    LOG_CALL(HkRigidBodyCinfo_CalculateBoxInertiaTensor);
    REQUIRE_FUNCTION_POINTER(HkRigidBodyCinfo_CalculateBoxInertiaTensor)
    pHkRigidBodyCinfo_CalculateBoxInertiaTensor(instance, halfExtents, mass);
}

void HkRigidBodyCinfo_CalculateSphereInertiaTensor(void* instance, float radius, float mass) { EnsureThreadInfo();
    LOG_CALL(HkRigidBodyCinfo_CalculateSphereInertiaTensor);
    REQUIRE_FUNCTION_POINTER(HkRigidBodyCinfo_CalculateSphereInertiaTensor)
    pHkRigidBodyCinfo_CalculateSphereInertiaTensor(instance, radius, mass);
}

void HkRigidBodyCinfo_SetMassProperties(void* instance, HkMassProperties properties) { EnsureThreadInfo();
    LOG_CALL(HkRigidBodyCinfo_SetMassProperties);
    REQUIRE_FUNCTION_POINTER(HkRigidBodyCinfo_SetMassProperties)
    pHkRigidBodyCinfo_SetMassProperties(instance, properties);
}

void HkRigidBodyCinfo_ComputeShapeMass(void* instance, void* shape, float mass) { EnsureThreadInfo();
    LOG_CALL(HkRigidBodyCinfo_ComputeShapeMass);
    REQUIRE_FUNCTION_POINTER(HkRigidBodyCinfo_ComputeShapeMass)
    pHkRigidBodyCinfo_ComputeShapeMass(instance, shape, mass);
}

void* HkRopeConstraintData_Create(void) { EnsureThreadInfo();
    LOG_CALL(HkRopeConstraintData_Create);
    REQUIRE_FUNCTION_POINTER(HkRopeConstraintData_Create)
    return pHkRopeConstraintData_Create();
}

void HkRopeConstraintData_SetInBodySpaceInternal(void* instance, Vector3 pivotA, Vector3 pivotB) { EnsureThreadInfo();
    LOG_CALL(HkRopeConstraintData_SetInBodySpaceInternal);
    REQUIRE_FUNCTION_POINTER(HkRopeConstraintData_SetInBodySpaceInternal)
    pHkRopeConstraintData_SetInBodySpaceInternal(instance, pivotA, pivotB);
}

float HkRopeConstraintData_Update(void* instance, void* constraint) { EnsureThreadInfo();
    LOG_CALL(HkRopeConstraintData_Update);
    REQUIRE_FUNCTION_POINTER(HkRopeConstraintData_Update)
    return pHkRopeConstraintData_Update(instance, constraint);
}

float HkRopeConstraintData_GetStrength(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkRopeConstraintData_GetStrength);
    REQUIRE_FUNCTION_POINTER(HkRopeConstraintData_GetStrength)
    return pHkRopeConstraintData_GetStrength(instance);
}

void HkRopeConstraintData_SetStrength(void* instance, float value) { EnsureThreadInfo();
    LOG_CALL(HkRopeConstraintData_SetStrength);
    REQUIRE_FUNCTION_POINTER(HkRopeConstraintData_SetStrength)
    pHkRopeConstraintData_SetStrength(instance, value);
}

float HkRopeConstraintData_GetLinearLimit(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkRopeConstraintData_GetLinearLimit);
    REQUIRE_FUNCTION_POINTER(HkRopeConstraintData_GetLinearLimit)
    return pHkRopeConstraintData_GetLinearLimit(instance);
}

void HkRopeConstraintData_SetLinearLimit(void* instance, float value) { EnsureThreadInfo();
    LOG_CALL(HkRopeConstraintData_SetLinearLimit);
    REQUIRE_FUNCTION_POINTER(HkRopeConstraintData_SetLinearLimit)
    pHkRopeConstraintData_SetLinearLimit(instance, value);
}

bool HkRopeConstraintData_IsValid(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkRopeConstraintData_IsValid);
    REQUIRE_FUNCTION_POINTER(HkRopeConstraintData_IsValid)
    return pHkRopeConstraintData_IsValid(instance);
}

int32_t HkShape_GetReferenceCount(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkShape_GetReferenceCount);
    REQUIRE_FUNCTION_POINTER(HkShape_GetReferenceCount)
    return pHkShape_GetReferenceCount(instance);
}

int32_t HkShape_GetShapeType(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkShape_GetShapeType);
    REQUIRE_FUNCTION_POINTER(HkShape_GetShapeType)
    return pHkShape_GetShapeType(instance);
}

bool HkShape_IsConvex(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkShape_IsConvex);
    REQUIRE_FUNCTION_POINTER(HkShape_IsConvex)
    return pHkShape_IsConvex(instance);
}

float HkShape_GetConvexRadius(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkShape_GetConvexRadius);
    REQUIRE_FUNCTION_POINTER(HkShape_GetConvexRadius)
    return pHkShape_GetConvexRadius(instance);
}

void HkShape_SetConvexRadius(void* instance, float value) { EnsureThreadInfo();
    LOG_CALL(HkShape_SetConvexRadius);
    REQUIRE_FUNCTION_POINTER(HkShape_SetConvexRadius)
    pHkShape_SetConvexRadius(instance, value);
}

uint64_t HkShape_GetUserData(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkShape_GetUserData);
    REQUIRE_FUNCTION_POINTER(HkShape_GetUserData)
    return pHkShape_GetUserData(instance);
}

void HkShape_SetUserData(void* instance, uint64_t value) { EnsureThreadInfo();
    LOG_CALL(HkShape_SetUserData);
    REQUIRE_FUNCTION_POINTER(HkShape_SetUserData)
    pHkShape_SetUserData(instance, value);
}

void HkShape_SetRigidBody(void* instance, void* rigidBody) { EnsureThreadInfo();
    LOG_CALL(HkShape_SetRigidBody);
    REQUIRE_FUNCTION_POINTER(HkShape_SetRigidBody)
    pHkShape_SetRigidBody(instance, rigidBody);
}

bool HkShape_IsContainer(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkShape_IsContainer);
    REQUIRE_FUNCTION_POINTER(HkShape_IsContainer)
    return pHkShape_IsContainer(instance);
}

void HkShape_AddReference(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkShape_AddReference);
    REQUIRE_FUNCTION_POINTER(HkShape_AddReference)
    pHkShape_AddReference(instance);
}

void HkShape_RemoveReference(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkShape_RemoveReference);
    REQUIRE_FUNCTION_POINTER(HkShape_RemoveReference)
    pHkShape_RemoveReference(instance);
}

void HkShape_DisableRefCount(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkShape_DisableRefCount);
    REQUIRE_FUNCTION_POINTER(HkShape_DisableRefCount)
    pHkShape_DisableRefCount(instance);
}

void HkShape_GetLocalAABB(void* instance, float tolerance, void* outMin, void* outMax) { EnsureThreadInfo();
    LOG_CALL(HkShape_GetLocalAABB);
    REQUIRE_FUNCTION_POINTER(HkShape_GetLocalAABB)
    pHkShape_GetLocalAABB(instance, tolerance, outMin, outMax);
}

uint32_t HkShape_CastRayCollectSingleHit(void* instance, Vector3 from, Vector3 to) { EnsureThreadInfo();
    LOG_CALL(HkShape_CastRayCollectSingleHit);
    REQUIRE_FUNCTION_POINTER(HkShape_CastRayCollectSingleHit)
    return pHkShape_CastRayCollectSingleHit(instance, from, to);
}

void* HkShape_LoadShapeFromFile(const char* filename) { EnsureThreadInfo();
    LOG_CALL(HkShape_LoadShapeFromFile);
    REQUIRE_FUNCTION_POINTER(HkShape_LoadShapeFromFile)
    return pHkShape_LoadShapeFromFile(filename);
}

void* HkShape_GetContainer(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkShape_GetContainer);
    REQUIRE_FUNCTION_POINTER(HkShape_GetContainer)
    return pHkShape_GetContainer(instance);
}

int32_t HkShapeBatch_GetCount(int32_t batchId) { EnsureThreadInfo();
    LOG_CALL(HkShapeBatch_GetCount);
    REQUIRE_FUNCTION_POINTER(HkShapeBatch_GetCount)
    return pHkShapeBatch_GetCount(batchId);
}

void HkShapeBatch_GetInfo(int32_t batchId, int32_t shapeIndex, void* outPos) { EnsureThreadInfo();
    LOG_CALL(HkShapeBatch_GetInfo);
    REQUIRE_FUNCTION_POINTER(HkShapeBatch_GetInfo)
    pHkShapeBatch_GetInfo(batchId, shapeIndex, outPos);
}

void HkShapeBatch_SetResult(int32_t batchId, int32_t shapeIndex, void* shape) { EnsureThreadInfo();
    LOG_CALL(HkShapeBatch_SetResult);
    REQUIRE_FUNCTION_POINTER(HkShapeBatch_SetResult)
    pHkShapeBatch_SetResult(batchId, shapeIndex, shape);
}

void* HkShapeBuffer_Create(void) { EnsureThreadInfo();
    LOG_CALL(HkShapeBuffer_Create);
    REQUIRE_FUNCTION_POINTER(HkShapeBuffer_Create)
    return pHkShapeBuffer_Create();
}

void* HkShapeBuffer_Destroy(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkShapeBuffer_Destroy);
    REQUIRE_FUNCTION_POINTER(HkShapeBuffer_Destroy)
    return pHkShapeBuffer_Destroy(instance);
}

int32_t HkShapeCollection_GetShapeCount(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkShapeCollection_GetShapeCount);
    REQUIRE_FUNCTION_POINTER(HkShapeCollection_GetShapeCount)
    return pHkShapeCollection_GetShapeCount(instance);
}

void* HkShapeCollection_GetShape(void* instance, uint32_t shapeKey) { EnsureThreadInfo();
    LOG_CALL(HkShapeCollection_GetShape);
    REQUIRE_FUNCTION_POINTER(HkShapeCollection_GetShape)
    return pHkShapeCollection_GetShape(instance, shapeKey);
}

void* HkShapeCollection_GetShapeWithBuffer(void* instance, uint32_t shapeKey, void* buffer) { EnsureThreadInfo();
    LOG_CALL(HkShapeCollection_GetShapeWithBuffer);
    REQUIRE_FUNCTION_POINTER(HkShapeCollection_GetShapeWithBuffer)
    return pHkShapeCollection_GetShapeWithBuffer(instance, shapeKey, buffer);
}

uint32_t HkShapeContainer_GetFirstKey(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkShapeContainer_GetFirstKey);
    REQUIRE_FUNCTION_POINTER(HkShapeContainer_GetFirstKey)
    return pHkShapeContainer_GetFirstKey(instance);
}

uint32_t HkShapeContainer_GetNextKey(void* instance, uint32_t key) { EnsureThreadInfo();
    LOG_CALL(HkShapeContainer_GetNextKey);
    REQUIRE_FUNCTION_POINTER(HkShapeContainer_GetNextKey)
    return pHkShapeContainer_GetNextKey(instance, key);
}

void* HkShapeContainer_CurrentValue(void* instance, uint32_t key, void* buffer) { EnsureThreadInfo();
    LOG_CALL(HkShapeContainer_CurrentValue);
    REQUIRE_FUNCTION_POINTER(HkShapeContainer_CurrentValue)
    return pHkShapeContainer_CurrentValue(instance, key, buffer);
}

void* HkShapeContainer_GetShape(void* instance, uint32_t key) { EnsureThreadInfo();
    LOG_CALL(HkShapeContainer_GetShape);
    REQUIRE_FUNCTION_POINTER(HkShapeContainer_GetShape)
    return pHkShapeContainer_GetShape(instance, key);
}

bool HkShapeContainer_IsShapeKeyValid(void* instance, uint32_t key) { EnsureThreadInfo();
    LOG_CALL(HkShapeContainer_IsShapeKeyValid);
    REQUIRE_FUNCTION_POINTER(HkShapeContainer_IsShapeKeyValid)
    return pHkShapeContainer_IsShapeKeyValid(instance, key);
}

bool HkShapeCutterUtil_Cut(void* shape, Vector4 plane, void* aabbMin, void* aabbMax) { EnsureThreadInfo();
    LOG_CALL(HkShapeCutterUtil_Cut);
    REQUIRE_FUNCTION_POINTER(HkShapeCutterUtil_Cut)
    return pHkShapeCutterUtil_Cut(shape, plane, aabbMin, aabbMax);
}

bool HkShapeLoader_LoadShapesListFromBuffer(int32_t cBuffer, void* buffer, void* shapeBuffer, void* containsScene, void* containsDestruction) { EnsureThreadInfo();
    LOG_CALL(HkShapeLoader_LoadShapesListFromBuffer);
    REQUIRE_FUNCTION_POINTER(HkShapeLoader_LoadShapesListFromBuffer)
    return pHkShapeLoader_LoadShapesListFromBuffer(cBuffer, buffer, shapeBuffer, containsScene, containsDestruction);
}

bool HkShapeLoader_LoadShapesListFromFile(const char* fileName, void* shapeBuffer) { EnsureThreadInfo();
    LOG_CALL(HkShapeLoader_LoadShapesListFromFile);
    REQUIRE_FUNCTION_POINTER(HkShapeLoader_LoadShapesListFromFile)
    return pHkShapeLoader_LoadShapesListFromFile(fileName, shapeBuffer);
}

bool HkShapeLoader_SaveShapesListToFile(const char* fileName, void* listShapes, bool xmlFormat) { EnsureThreadInfo();
    LOG_CALL(HkShapeLoader_SaveShapesListToFile);
    REQUIRE_FUNCTION_POINTER(HkShapeLoader_SaveShapesListToFile)
    return pHkShapeLoader_SaveShapesListToFile(fileName, listShapes, xmlFormat);
}

bool HkShapeLoader_CleanupShapesBuffer(int32_t cBuffer, void* buffer, void* returnByteArray) { EnsureThreadInfo();
    LOG_CALL(HkShapeLoader_CleanupShapesBuffer);
    REQUIRE_FUNCTION_POINTER(HkShapeLoader_CleanupShapesBuffer)
    return pHkShapeLoader_CleanupShapesBuffer(cBuffer, buffer, bridge_void_ptr_int(returnByteArray));
}

void* HkSimpleMeshShape_Create(int32_t vCount, void* vertices, int32_t iCount, void* indices, int32_t mCount, void* materials) { EnsureThreadInfo();
    LOG_CALL(HkSimpleMeshShape_Create);
    REQUIRE_FUNCTION_POINTER(HkSimpleMeshShape_Create)
    return pHkSimpleMeshShape_Create(vCount, vertices, iCount, indices, mCount, materials);
}

void* HkSimpleShapePhantom_Create(void* shape) { EnsureThreadInfo();
    LOG_CALL(HkSimpleShapePhantom_Create);
    REQUIRE_FUNCTION_POINTER(HkSimpleShapePhantom_Create)
    return pHkSimpleShapePhantom_Create(shape);
}

void* HkSimpleShapePhantom_CreateWithLayer(void* shape, int32_t layer) { EnsureThreadInfo();
    LOG_CALL(HkSimpleShapePhantom_CreateWithLayer);
    REQUIRE_FUNCTION_POINTER(HkSimpleShapePhantom_CreateWithLayer)
    return pHkSimpleShapePhantom_CreateWithLayer(shape, layer);
}

void* HkSimpleShapePhantom_GetShape(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkSimpleShapePhantom_GetShape);
    REQUIRE_FUNCTION_POINTER(HkSimpleShapePhantom_GetShape)
    return pHkSimpleShapePhantom_GetShape(instance);
}

void* HkSimpleValueProperty_CreateFloat(float value) { EnsureThreadInfo();
    LOG_CALL(HkSimpleValueProperty_CreateFloat);
    REQUIRE_FUNCTION_POINTER(HkSimpleValueProperty_CreateFloat)
    return pHkSimpleValueProperty_CreateFloat(value);
}

void* HkSimpleValueProperty_CreateUInt(uint32_t value) { EnsureThreadInfo();
    LOG_CALL(HkSimpleValueProperty_CreateUInt);
    REQUIRE_FUNCTION_POINTER(HkSimpleValueProperty_CreateUInt)
    return pHkSimpleValueProperty_CreateUInt(value);
}

void* HkSimpleValueProperty_CreateInt(int32_t value) { EnsureThreadInfo();
    LOG_CALL(HkSimpleValueProperty_CreateInt);
    REQUIRE_FUNCTION_POINTER(HkSimpleValueProperty_CreateInt)
    return pHkSimpleValueProperty_CreateInt(value);
}

float HkSimpleValueProperty_GetValueFloat(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkSimpleValueProperty_GetValueFloat);
    REQUIRE_FUNCTION_POINTER(HkSimpleValueProperty_GetValueFloat)
    return pHkSimpleValueProperty_GetValueFloat(instance);
}

void HkSimpleValueProperty_SetValueFloat(void* instance, float valueFloat) { EnsureThreadInfo();
    LOG_CALL(HkSimpleValueProperty_SetValueFloat);
    REQUIRE_FUNCTION_POINTER(HkSimpleValueProperty_SetValueFloat)
    pHkSimpleValueProperty_SetValueFloat(instance, valueFloat);
}

uint32_t HkSimpleValueProperty_GetValueUInt(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkSimpleValueProperty_GetValueUInt);
    REQUIRE_FUNCTION_POINTER(HkSimpleValueProperty_GetValueUInt)
    return pHkSimpleValueProperty_GetValueUInt(instance);
}

void HkSimpleValueProperty_SetValueUInt(void* instance, uint32_t valueUInt) { EnsureThreadInfo();
    LOG_CALL(HkSimpleValueProperty_SetValueUInt);
    REQUIRE_FUNCTION_POINTER(HkSimpleValueProperty_SetValueUInt)
    pHkSimpleValueProperty_SetValueUInt(instance, valueUInt);
}

int32_t HkSimpleValueProperty_GetValueInt(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkSimpleValueProperty_GetValueInt);
    REQUIRE_FUNCTION_POINTER(HkSimpleValueProperty_GetValueInt)
    return pHkSimpleValueProperty_GetValueInt(instance);
}

void HkSimpleValueProperty_SetValueInt(void* instance, int32_t calueInt) { EnsureThreadInfo();
    LOG_CALL(HkSimpleValueProperty_SetValueInt);
    REQUIRE_FUNCTION_POINTER(HkSimpleValueProperty_SetValueInt)
    pHkSimpleValueProperty_SetValueInt(instance, calueInt);
}

int32_t HkSimulationIsland_GetEntityCount(void* island) { EnsureThreadInfo();
    LOG_CALL(HkSimulationIsland_GetEntityCount);
    REQUIRE_FUNCTION_POINTER(HkSimulationIsland_GetEntityCount)
    return pHkSimulationIsland_GetEntityCount(island);
}

void* HkSimulationIsland_GetEntity(void* island, int32_t index) { EnsureThreadInfo();
    LOG_CALL(HkSimulationIsland_GetEntity);
    REQUIRE_FUNCTION_POINTER(HkSimulationIsland_GetEntity)
    return pHkSimulationIsland_GetEntity(island, index);
}

void HkSimulationIsland_GetBounds(void* island, void* bb) { EnsureThreadInfo();
    LOG_CALL(HkSimulationIsland_GetBounds);
    REQUIRE_FUNCTION_POINTER(HkSimulationIsland_GetBounds)
    pHkSimulationIsland_GetBounds(island, bb);
}

void HkSimulationIsland_GetOffsets(void* activeOffset, void* activeBitFieldOffset) { EnsureThreadInfo();
    LOG_CALL(HkSimulationIsland_GetOffsets);
    REQUIRE_FUNCTION_POINTER(HkSimulationIsland_GetOffsets)
    pHkSimulationIsland_GetOffsets(activeOffset, activeBitFieldOffset);
}

void* HkSmartListShape_Create(int32_t dummy) { EnsureThreadInfo();
    LOG_CALL(HkSmartListShape_Create);
    REQUIRE_FUNCTION_POINTER(HkSmartListShape_Create)
    return pHkSmartListShape_Create(dummy);
}

int32_t HkSmartListShape_GetShapeCount(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkSmartListShape_GetShapeCount);
    REQUIRE_FUNCTION_POINTER(HkSmartListShape_GetShapeCount)
    return pHkSmartListShape_GetShapeCount(instance);
}

void HkSmartListShape_AddShape(void* instance, void* shape) { EnsureThreadInfo();
    LOG_CALL(HkSmartListShape_AddShape);
    REQUIRE_FUNCTION_POINTER(HkSmartListShape_AddShape)
    pHkSmartListShape_AddShape(instance, shape);
}

void HkSmartListShape_RemoveShape(void* instance, void* shape) { EnsureThreadInfo();
    LOG_CALL(HkSmartListShape_RemoveShape);
    REQUIRE_FUNCTION_POINTER(HkSmartListShape_RemoveShape)
    pHkSmartListShape_RemoveShape(instance, shape);
}

void HkSmartListShape_Validate(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkSmartListShape_Validate);
    REQUIRE_FUNCTION_POINTER(HkSmartListShape_Validate)
    pHkSmartListShape_Validate(instance);
}

void* HkSphereShape_Create(float radius) { EnsureThreadInfo();
    LOG_CALL(HkSphereShape_Create);
    REQUIRE_FUNCTION_POINTER(HkSphereShape_Create)
    return pHkSphereShape_Create(radius);
}

float HkSphereShape_GetRadius(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkSphereShape_GetRadius);
    REQUIRE_FUNCTION_POINTER(HkSphereShape_GetRadius)
    return pHkSphereShape_GetRadius(instance);
}

void HkSphereShape_SetRadius(void* instance, float radius) { EnsureThreadInfo();
    LOG_CALL(HkSphereShape_SetRadius);
    REQUIRE_FUNCTION_POINTER(HkSphereShape_SetRadius)
    pHkSphereShape_SetRadius(instance, radius);
}

void* HkStaticCompoundShape_Create(int32_t refPolicy) { EnsureThreadInfo();
    LOG_CALL(HkStaticCompoundShape_Create);
    REQUIRE_FUNCTION_POINTER(HkStaticCompoundShape_Create)
    return pHkStaticCompoundShape_Create(refPolicy);
}

int32_t HkStaticCompoundShape_GetInstanceCount(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkStaticCompoundShape_GetInstanceCount);
    REQUIRE_FUNCTION_POINTER(HkStaticCompoundShape_GetInstanceCount)
    return pHkStaticCompoundShape_GetInstanceCount(instance);
}

int32_t HkStaticCompoundShape_AddInstance(void* instance, void* shape, Matrix transform) { EnsureThreadInfo();
    LOG_CALL(HkStaticCompoundShape_AddInstance);
    REQUIRE_FUNCTION_POINTER(HkStaticCompoundShape_AddInstance)
    return pHkStaticCompoundShape_AddInstance(instance, shape, transform);
}

void HkStaticCompoundShape_Bake(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkStaticCompoundShape_Bake);
    REQUIRE_FUNCTION_POINTER(HkStaticCompoundShape_Bake)
    pHkStaticCompoundShape_Bake(instance);
}

uint32_t HkStaticCompoundShape_ComposeShapeKey(void* instance, int32_t instanceId, uint32_t shapeKey) { EnsureThreadInfo();
    LOG_CALL(HkStaticCompoundShape_ComposeShapeKey);
    REQUIRE_FUNCTION_POINTER(HkStaticCompoundShape_ComposeShapeKey)
    return pHkStaticCompoundShape_ComposeShapeKey(instance, instanceId, shapeKey);
}

HkStaticCompoundShape_DecomposeShapeKeyResult HkStaticCompoundShape_DecomposeShapeKey(void* instance, uint32_t shapeKey) { EnsureThreadInfo();
    LOG_CALL(HkStaticCompoundShape_DecomposeShapeKey);
    REQUIRE_FUNCTION_POINTER(HkStaticCompoundShape_DecomposeShapeKey)
    return pHkStaticCompoundShape_DecomposeShapeKey(instance, shapeKey);
}

void HkStaticCompoundShape_EnableAllShapeKeys(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkStaticCompoundShape_EnableAllShapeKeys);
    REQUIRE_FUNCTION_POINTER(HkStaticCompoundShape_EnableAllShapeKeys)
    pHkStaticCompoundShape_EnableAllShapeKeys(instance);
}

void HkStaticCompoundShape_EnableInstance(void* instance, int32_t instanceId, bool enable) { EnsureThreadInfo();
    LOG_CALL(HkStaticCompoundShape_EnableInstance);
    REQUIRE_FUNCTION_POINTER(HkStaticCompoundShape_EnableInstance)
    pHkStaticCompoundShape_EnableInstance(instance, instanceId, enable);
}

void HkStaticCompoundShape_EnableShapeKey(void* instance, uint32_t key, bool enable) { EnsureThreadInfo();
    LOG_CALL(HkStaticCompoundShape_EnableShapeKey);
    REQUIRE_FUNCTION_POINTER(HkStaticCompoundShape_EnableShapeKey)
    pHkStaticCompoundShape_EnableShapeKey(instance, key, enable);
}

uint32_t HkStaticCompoundShape_GetFirstKey(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkStaticCompoundShape_GetFirstKey);
    REQUIRE_FUNCTION_POINTER(HkStaticCompoundShape_GetFirstKey)
    return pHkStaticCompoundShape_GetFirstKey(instance);
}

void* HkStaticCompoundShape_GetInstance(void* instance, int32_t instanceIndex) { EnsureThreadInfo();
    LOG_CALL(HkStaticCompoundShape_GetInstance);
    REQUIRE_FUNCTION_POINTER(HkStaticCompoundShape_GetInstance)
    return pHkStaticCompoundShape_GetInstance(instance, instanceIndex);
}

Matrix HkStaticCompoundShape_GetInstanceTransform(void* instance, int32_t instanceIndex) { EnsureThreadInfo();
    LOG_CALL(HkStaticCompoundShape_GetInstanceTransform);
    REQUIRE_FUNCTION_POINTER(HkStaticCompoundShape_GetInstanceTransform)
    return pHkStaticCompoundShape_GetInstanceTransform(instance, instanceIndex);
}

bool HkStaticCompoundShape_IsInstanceEnabled(void* instance, int32_t instanceId) { EnsureThreadInfo();
    LOG_CALL(HkStaticCompoundShape_IsInstanceEnabled);
    REQUIRE_FUNCTION_POINTER(HkStaticCompoundShape_IsInstanceEnabled)
    return pHkStaticCompoundShape_IsInstanceEnabled(instance, instanceId);
}

bool HkStaticCompoundShape_IsShapeKeyEnabled(void* instance, uint32_t key) { EnsureThreadInfo();
    LOG_CALL(HkStaticCompoundShape_IsShapeKeyEnabled);
    REQUIRE_FUNCTION_POINTER(HkStaticCompoundShape_IsShapeKeyEnabled)
    return pHkStaticCompoundShape_IsShapeKeyEnabled(instance, key);
}

void HkTaskProfiler_Init(void* onTaskStarted, void* onTaskFinished) { EnsureThreadInfo();
    LOG_CALL(HkTaskProfiler_Init);
    REQUIRE_FUNCTION_POINTER(HkTaskProfiler_Init)
    pHkTaskProfiler_Init(bridge_void_charptr_int(onTaskStarted), bridge_void_void(onTaskFinished));
}

void HkTaskProfiler_ReleaseResources(void) { EnsureThreadInfo();
    LOG_CALL(HkTaskProfiler_ReleaseResources);
    REQUIRE_FUNCTION_POINTER(HkTaskProfiler_ReleaseResources)
    pHkTaskProfiler_ReleaseResources();
}

void HkTaskProfiler_HookJobQueue(void* jobQueue) { EnsureThreadInfo();
    LOG_CALL(HkTaskProfiler_HookJobQueue);
    REQUIRE_FUNCTION_POINTER(HkTaskProfiler_HookJobQueue)
    pHkTaskProfiler_HookJobQueue(jobQueue);
}

void HkTaskProfiler_ReplayTimers(void* blockBegin, void* blockEnd) { EnsureThreadInfo();
    LOG_CALL(HkTaskProfiler_ReplayTimers);
    REQUIRE_FUNCTION_POINTER(HkTaskProfiler_ReplayTimers)
    pHkTaskProfiler_ReplayTimers(bridge_void_charptr(blockBegin), bridge_void_i64(blockEnd));
}

void HkTaskProfiler_Begin1(void) { EnsureThreadInfo();
    LOG_CALL(HkTaskProfiler_Begin1);
    REQUIRE_FUNCTION_POINTER(HkTaskProfiler_Begin1)
    pHkTaskProfiler_Begin1();
}

void HkTaskProfiler_Begin2(void) { EnsureThreadInfo();
    LOG_CALL(HkTaskProfiler_Begin2);
    REQUIRE_FUNCTION_POINTER(HkTaskProfiler_Begin2)
    pHkTaskProfiler_Begin2();
}

void HkTaskProfiler_Begin3(void) { EnsureThreadInfo();
    LOG_CALL(HkTaskProfiler_Begin3);
    REQUIRE_FUNCTION_POINTER(HkTaskProfiler_Begin3)
    pHkTaskProfiler_Begin3();
}

void HkTaskProfiler_Begin4(void) { EnsureThreadInfo();
    LOG_CALL(HkTaskProfiler_Begin4);
    REQUIRE_FUNCTION_POINTER(HkTaskProfiler_Begin4)
    pHkTaskProfiler_Begin4();
}

void HkTaskProfiler_Begin5(void) { EnsureThreadInfo();
    LOG_CALL(HkTaskProfiler_Begin5);
    REQUIRE_FUNCTION_POINTER(HkTaskProfiler_Begin5)
    pHkTaskProfiler_Begin5();
}

void HkTaskProfiler_End(void) { EnsureThreadInfo();
    LOG_CALL(HkTaskProfiler_End);
    REQUIRE_FUNCTION_POINTER(HkTaskProfiler_End)
    pHkTaskProfiler_End();
}

void* HkTransformShape_Create(void* childShape, Matrix transform) { EnsureThreadInfo();
    LOG_CALL(HkTransformShape_Create);
    REQUIRE_FUNCTION_POINTER(HkTransformShape_Create)
    return pHkTransformShape_Create(childShape, transform);
}

void* HkTransformShape_CreateWithTranslation(void* childShape, Vector3 translation, Quaternion rotation) { EnsureThreadInfo();
    LOG_CALL(HkTransformShape_CreateWithTranslation);
    REQUIRE_FUNCTION_POINTER(HkTransformShape_CreateWithTranslation)
    return pHkTransformShape_CreateWithTranslation(childShape, translation, rotation);
}

Matrix HkTransformShape_GetTransform(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkTransformShape_GetTransform);
    REQUIRE_FUNCTION_POINTER(HkTransformShape_GetTransform)
    return pHkTransformShape_GetTransform(instance);
}

void* HkTransformShape_GetChildShape(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkTransformShape_GetChildShape);
    REQUIRE_FUNCTION_POINTER(HkTransformShape_GetChildShape)
    return pHkTransformShape_GetChildShape(instance);
}

Vector3 HkTriangleShape_GetExtrusion(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkTriangleShape_GetExtrusion);
    REQUIRE_FUNCTION_POINTER(HkTriangleShape_GetExtrusion)
    return pHkTriangleShape_GetExtrusion(instance);
}

Vector3 HkTriangleShape_GetPt2(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkTriangleShape_GetPt2);
    REQUIRE_FUNCTION_POINTER(HkTriangleShape_GetPt2)
    return pHkTriangleShape_GetPt2(instance);
}

Vector3 HkTriangleShape_GetPt1(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkTriangleShape_GetPt1);
    REQUIRE_FUNCTION_POINTER(HkTriangleShape_GetPt1)
    return pHkTriangleShape_GetPt1(instance);
}

Vector3 HkTriangleShape_GetPt0(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkTriangleShape_GetPt0);
    REQUIRE_FUNCTION_POINTER(HkTriangleShape_GetPt0)
    return pHkTriangleShape_GetPt0(instance);
}

void* HkUniformGridShape_Create(HkUniformGridShapeArgsPOD argsPod) { EnsureThreadInfo();
    LOG_CALL(HkUniformGridShape_Create);
    REQUIRE_FUNCTION_POINTER(HkUniformGridShape_Create)
    return pHkUniformGridShape_Create(argsPod);
}

int32_t HkUniformGridShape_GetShapeCount(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkUniformGridShape_GetShapeCount);
    REQUIRE_FUNCTION_POINTER(HkUniformGridShape_GetShapeCount)
    return pHkUniformGridShape_GetShapeCount(instance);
}

void HkUniformGridShape_DiscardLargeData(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkUniformGridShape_DiscardLargeData);
    REQUIRE_FUNCTION_POINTER(HkUniformGridShape_DiscardLargeData)
    pHkUniformGridShape_DiscardLargeData(instance);
}

int32_t HkUniformGridShape_GetHitsAndClear(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkUniformGridShape_GetHitsAndClear);
    REQUIRE_FUNCTION_POINTER(HkUniformGridShape_GetHitsAndClear)
    return pHkUniformGridShape_GetHitsAndClear(instance);
}

int32_t HkUniformGridShape_GetHitCellsInRange(void* instance, Vector3 min, Vector3 max, int32_t bufferSize, void* buffer) { EnsureThreadInfo();
    LOG_CALL(HkUniformGridShape_GetHitCellsInRange);
    REQUIRE_FUNCTION_POINTER(HkUniformGridShape_GetHitCellsInRange)
    return pHkUniformGridShape_GetHitCellsInRange(instance, min, max, bufferSize, buffer);
}

int32_t HkUniformGridShape_GetMissingCellsInRange(void* instance, Vector3 min, Vector3 max, int32_t bufferSize, void* buffer) { EnsureThreadInfo();
    LOG_CALL(HkUniformGridShape_GetMissingCellsInRange);
    REQUIRE_FUNCTION_POINTER(HkUniformGridShape_GetMissingCellsInRange)
    return pHkUniformGridShape_GetMissingCellsInRange(instance, min, max, bufferSize, buffer);
}

int32_t HkUniformGridShape_InvalidateRange(void* instance, Vector3 min, Vector3 max, int32_t bufferSize, void* buffer) { EnsureThreadInfo();
    LOG_CALL(HkUniformGridShape_InvalidateRange);
    REQUIRE_FUNCTION_POINTER(HkUniformGridShape_InvalidateRange)
    return pHkUniformGridShape_InvalidateRange(instance, min, max, bufferSize, buffer);
}

void HkUniformGridShape_InvalidateRangeImmediate(void* instance, Vector3I minChanged, Vector3I maxChanged) { EnsureThreadInfo();
    LOG_CALL(HkUniformGridShape_InvalidateRangeImmediate);
    REQUIRE_FUNCTION_POINTER(HkUniformGridShape_InvalidateRangeImmediate)
    pHkUniformGridShape_InvalidateRangeImmediate(instance, minChanged, maxChanged);
}

void HkUniformGridShape_RemoveChild(void* instance, int32_t x, int32_t y, int32_t z) { EnsureThreadInfo();
    LOG_CALL(HkUniformGridShape_RemoveChild);
    REQUIRE_FUNCTION_POINTER(HkUniformGridShape_RemoveChild)
    pHkUniformGridShape_RemoveChild(instance, x, y, z);
}

void HkUniformGridShape_SetChild(void* instance, int32_t x, int32_t y, int32_t z, void* shape, int32_t refPolicy) { EnsureThreadInfo();
    LOG_CALL(HkUniformGridShape_SetChild);
    REQUIRE_FUNCTION_POINTER(HkUniformGridShape_SetChild)
    pHkUniformGridShape_SetChild(instance, x, y, z, shape, refPolicy);
}

void* HkUniformGridShape_GetChild(void* instance, int32_t x, int32_t y, int32_t z) { EnsureThreadInfo();
    LOG_CALL(HkUniformGridShape_GetChild);
    REQUIRE_FUNCTION_POINTER(HkUniformGridShape_GetChild)
    return pHkUniformGridShape_GetChild(instance, x, y, z);
}

void HkUniformGridShape_SetDeleteHandler(void* instance, void* handler) { EnsureThreadInfo();
    LOG_CALL(HkUniformGridShape_SetDeleteHandler);
    REQUIRE_FUNCTION_POINTER(HkUniformGridShape_SetDeleteHandler)
    pHkUniformGridShape_SetDeleteHandler(instance, bridge_void_ptr(handler));
}

void HkUniformGridShape_RemoveShapeRequestHandler(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkUniformGridShape_RemoveShapeRequestHandler);
    REQUIRE_FUNCTION_POINTER(HkUniformGridShape_RemoveShapeRequestHandler)
    pHkUniformGridShape_RemoveShapeRequestHandler(instance);
}

void HkUniformGridShape_SetShapeRequestHandler(void* instance, void* blockingCallback) { EnsureThreadInfo();
    LOG_CALL(HkUniformGridShape_SetShapeRequestHandler);
    REQUIRE_FUNCTION_POINTER(HkUniformGridShape_SetShapeRequestHandler)
    pHkUniformGridShape_SetShapeRequestHandler(instance, bridge_void_ptr_int(blockingCallback));
}

void HkUniformGridShape_EnableExtendedCache(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkUniformGridShape_EnableExtendedCache);
    REQUIRE_FUNCTION_POINTER(HkUniformGridShape_EnableExtendedCache)
    pHkUniformGridShape_EnableExtendedCache(instance);
}

float HkUtils_CalculateSeparatingVelocity(void* body1, void* body2, void* contactPoint) { EnsureThreadInfo();
    LOG_CALL(HkUtils_CalculateSeparatingVelocity);
    REQUIRE_FUNCTION_POINTER(HkUtils_CalculateSeparatingVelocity)
    return pHkUtils_CalculateSeparatingVelocity(body1, body2, contactPoint);
}

void HkUtils_SetSoftContact(void* bodyA, void* bodyB, float softness, float maxVel) { EnsureThreadInfo();
    LOG_CALL(HkUtils_SetSoftContact);
    REQUIRE_FUNCTION_POINTER(HkUtils_SetSoftContact)
    pHkUtils_SetSoftContact(bodyA, bodyB, softness, maxVel);
}

void HkVDB_SyncTimers(void* threadPool) { EnsureThreadInfo();
    LOG_CALL(HkVDB_SyncTimers);
    REQUIRE_FUNCTION_POINTER(HkVDB_SyncTimers)
    pHkVDB_SyncTimers(threadPool);
}

void HkVDB_StepVDB(void* world, float timeInSec) { EnsureThreadInfo();
    LOG_CALL(HkVDB_StepVDB);
    REQUIRE_FUNCTION_POINTER(HkVDB_StepVDB)
    pHkVDB_StepVDB(world, timeInSec);
}

void HkVDB_Start(void) { EnsureThreadInfo();
    LOG_CALL(HkVDB_Start);
    REQUIRE_FUNCTION_POINTER(HkVDB_Start)
    pHkVDB_Start();
}

void HkVDB_ReleaseResources(void) { EnsureThreadInfo();
    LOG_CALL(HkVDB_ReleaseResources);
    REQUIRE_FUNCTION_POINTER(HkVDB_ReleaseResources)
    pHkVDB_ReleaseResources();
}

int32_t HkVDB_GetPort(void) { EnsureThreadInfo();
    LOG_CALL(HkVDB_GetPort);
    REQUIRE_FUNCTION_POINTER(HkVDB_GetPort)
    return pHkVDB_GetPort();
}

void HkVDB_SetPort(int32_t value) { EnsureThreadInfo();
    LOG_CALL(HkVDB_SetPort);
    REQUIRE_FUNCTION_POINTER(HkVDB_SetPort)
    pHkVDB_SetPort(value);
}

void HkVDB_UpdateCamera(void* from, void* to, void* up) { EnsureThreadInfo();
    LOG_CALL(HkVDB_UpdateCamera);
    REQUIRE_FUNCTION_POINTER(HkVDB_UpdateCamera)
    pHkVDB_UpdateCamera(from, to, up);
}

void HkVDB_Capture(const char* path) { EnsureThreadInfo();
    LOG_CALL(HkVDB_Capture);
    REQUIRE_FUNCTION_POINTER(HkVDB_Capture)
    pHkVDB_Capture(path);
}

void HkVDB_EndCapture(void) { EnsureThreadInfo();
    LOG_CALL(HkVDB_EndCapture);
    REQUIRE_FUNCTION_POINTER(HkVDB_EndCapture)
    pHkVDB_EndCapture();
}

void* HkVec3IProperty_Create(Vector3I value) { EnsureThreadInfo();
    LOG_CALL(HkVec3IProperty_Create);
    REQUIRE_FUNCTION_POINTER(HkVec3IProperty_Create)
    return pHkVec3IProperty_Create(value);
}

Vector3I HkVec3IProperty_GetValue(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkVec3IProperty_GetValue);
    REQUIRE_FUNCTION_POINTER(HkVec3IProperty_GetValue)
    return pHkVec3IProperty_GetValue(instance);
}

void HkVec3IProperty_SetValue(void* instance, Vector3I value) { EnsureThreadInfo();
    LOG_CALL(HkVec3IProperty_SetValue);
    REQUIRE_FUNCTION_POINTER(HkVec3IProperty_SetValue)
    pHkVec3IProperty_SetValue(instance, value);
}

void* HkVelocityConstraintMotor_Create(float velocityTarget, float maxForce) { EnsureThreadInfo();
    LOG_CALL(HkVelocityConstraintMotor_Create);
    REQUIRE_FUNCTION_POINTER(HkVelocityConstraintMotor_Create)
    return pHkVelocityConstraintMotor_Create(velocityTarget, maxForce);
}

float HkVelocityConstraintMotor_GetTau(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkVelocityConstraintMotor_GetTau);
    REQUIRE_FUNCTION_POINTER(HkVelocityConstraintMotor_GetTau)
    return pHkVelocityConstraintMotor_GetTau(instance);
}

void HkVelocityConstraintMotor_SetTau(void* instance, float value) { EnsureThreadInfo();
    LOG_CALL(HkVelocityConstraintMotor_SetTau);
    REQUIRE_FUNCTION_POINTER(HkVelocityConstraintMotor_SetTau)
    pHkVelocityConstraintMotor_SetTau(instance, value);
}

float HkVelocityConstraintMotor_GetVelocityTarget(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkVelocityConstraintMotor_GetVelocityTarget);
    REQUIRE_FUNCTION_POINTER(HkVelocityConstraintMotor_GetVelocityTarget)
    return pHkVelocityConstraintMotor_GetVelocityTarget(instance);
}

void HkVelocityConstraintMotor_SetVelocityTarget(void* instance, float value) { EnsureThreadInfo();
    LOG_CALL(HkVelocityConstraintMotor_SetVelocityTarget);
    REQUIRE_FUNCTION_POINTER(HkVelocityConstraintMotor_SetVelocityTarget)
    pHkVelocityConstraintMotor_SetVelocityTarget(instance, value);
}

bool HkVelocityConstraintMotor_GetConstantRecoveryVelocity(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkVelocityConstraintMotor_GetConstantRecoveryVelocity);
    REQUIRE_FUNCTION_POINTER(HkVelocityConstraintMotor_GetConstantRecoveryVelocity)
    return pHkVelocityConstraintMotor_GetConstantRecoveryVelocity(instance);
}

void HkVelocityConstraintMotor_SetConstantRecoveryVelocity(void* instance, bool value) { EnsureThreadInfo();
    LOG_CALL(HkVelocityConstraintMotor_SetConstantRecoveryVelocity);
    REQUIRE_FUNCTION_POINTER(HkVelocityConstraintMotor_SetConstantRecoveryVelocity)
    pHkVelocityConstraintMotor_SetConstantRecoveryVelocity(instance, value);
}

void* HkWheelConstraintData_Create(void) { EnsureThreadInfo();
    LOG_CALL(HkWheelConstraintData_Create);
    REQUIRE_FUNCTION_POINTER(HkWheelConstraintData_Create)
    return pHkWheelConstraintData_Create();
}

void HkWheelConstraintData_SetInWorldSpace(void* instance, Matrix wheelBody, Matrix chasisBody, Vector3 pivot, Vector3 axle, Vector3 suspensionAxis, Vector3 steeringAxis) { EnsureThreadInfo();
    LOG_CALL(HkWheelConstraintData_SetInWorldSpace);
    REQUIRE_FUNCTION_POINTER(HkWheelConstraintData_SetInWorldSpace)
    pHkWheelConstraintData_SetInWorldSpace(instance, wheelBody, chasisBody, pivot, axle, suspensionAxis, steeringAxis);
}

void HkWheelConstraintData_SetInBodySpaceInternal(void* instance, Vector3 pivotA, Vector3 pivotB, Vector3 axleA, Vector3 axleB, Vector3 suspensionAxisB, Vector3 steeringAxisB) { EnsureThreadInfo();
    LOG_CALL(HkWheelConstraintData_SetInBodySpaceInternal);
    REQUIRE_FUNCTION_POINTER(HkWheelConstraintData_SetInBodySpaceInternal)
    pHkWheelConstraintData_SetInBodySpaceInternal(instance, pivotA, pivotB, axleA, axleB, suspensionAxisB, steeringAxisB);
}

void HkWheelConstraintData_SetSuspensionMinLimit(void* instance, float value) { EnsureThreadInfo();
    LOG_CALL(HkWheelConstraintData_SetSuspensionMinLimit);
    REQUIRE_FUNCTION_POINTER(HkWheelConstraintData_SetSuspensionMinLimit)
    pHkWheelConstraintData_SetSuspensionMinLimit(instance, value);
}

void HkWheelConstraintData_SetSuspensionMaxLimit(void* instance, float value) { EnsureThreadInfo();
    LOG_CALL(HkWheelConstraintData_SetSuspensionMaxLimit);
    REQUIRE_FUNCTION_POINTER(HkWheelConstraintData_SetSuspensionMaxLimit)
    pHkWheelConstraintData_SetSuspensionMaxLimit(instance, value);
}

void HkWheelConstraintData_SetSuspensionStrength(void* instance, float value) { EnsureThreadInfo();
    LOG_CALL(HkWheelConstraintData_SetSuspensionStrength);
    REQUIRE_FUNCTION_POINTER(HkWheelConstraintData_SetSuspensionStrength)
    pHkWheelConstraintData_SetSuspensionStrength(instance, value);
}

void HkWheelConstraintData_SetSuspensionDamping(void* instance, float value) { EnsureThreadInfo();
    LOG_CALL(HkWheelConstraintData_SetSuspensionDamping);
    REQUIRE_FUNCTION_POINTER(HkWheelConstraintData_SetSuspensionDamping)
    pHkWheelConstraintData_SetSuspensionDamping(instance, value);
}

void HkWheelConstraintData_SetSteeringAngle(void* instance, float value) { EnsureThreadInfo();
    LOG_CALL(HkWheelConstraintData_SetSteeringAngle);
    REQUIRE_FUNCTION_POINTER(HkWheelConstraintData_SetSteeringAngle)
    pHkWheelConstraintData_SetSteeringAngle(instance, value);
}

void* HkWheelResponseModifierUtil_Create(void* rigidBody, void* softness, void* acceleration) { EnsureThreadInfo();
    LOG_CALL(HkWheelResponseModifierUtil_Create);
    REQUIRE_FUNCTION_POINTER(HkWheelResponseModifierUtil_Create)
    return pHkWheelResponseModifierUtil_Create(rigidBody, bridge_float_ptr(softness), bridge_float_ptr(acceleration));
}

void HkWheelResponseModifierUtil_Release(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkWheelResponseModifierUtil_Release);
    REQUIRE_FUNCTION_POINTER(HkWheelResponseModifierUtil_Release)
    pHkWheelResponseModifierUtil_Release(instance);
}

void* HkWorld_Create(bool enableGlobalGravity, float broadphaseCubeSideLength, float contactRestingVelocity, bool enableMultithreading, int32_t solverIterations, void* broadPhaseCallback) { EnsureThreadInfo();
    LOG_CALL(HkWorld_Create);
    REQUIRE_FUNCTION_POINTER(HkWorld_Create)
    return pHkWorld_Create(enableGlobalGravity, broadphaseCubeSideLength, contactRestingVelocity, enableMultithreading, solverIterations, bridge_void_ptr_ptr(broadPhaseCallback));
}

void* HkWorld_CreateCInfo(void* cInfo, void* broadPhaseCallback) { EnsureThreadInfo();
    LOG_CALL(HkWorld_CreateCInfo);
    REQUIRE_FUNCTION_POINTER(HkWorld_CreateCInfo)
    return pHkWorld_CreateCInfo(cInfo, bridge_void_ptr_ptr(broadPhaseCallback));
}

void* HkWorld_CreateBodyPairCollection(void) { EnsureThreadInfo();
    LOG_CALL(HkWorld_CreateBodyPairCollection);
    REQUIRE_FUNCTION_POINTER(HkWorld_CreateBodyPairCollection)
    return pHkWorld_CreateBodyPairCollection();
}

void HkWorld_RegisterWithJobQueue(void* world, void* jobQueue) { EnsureThreadInfo();
    LOG_CALL(HkWorld_RegisterWithJobQueue);
    REQUIRE_FUNCTION_POINTER(HkWorld_RegisterWithJobQueue)
    pHkWorld_RegisterWithJobQueue(world, jobQueue);
}

void HkWorld_Lock(void* world) { EnsureThreadInfo();
    LOG_CALL(HkWorld_Lock);
    REQUIRE_FUNCTION_POINTER(HkWorld_Lock)
    pHkWorld_Lock(world);
}

void HkWorld_Unlock(void* world) { EnsureThreadInfo();
    LOG_CALL(HkWorld_Unlock);
    REQUIRE_FUNCTION_POINTER(HkWorld_Unlock)
    pHkWorld_Unlock(world);
}

void HkWorld_LockCriticalOperations(void* world) { EnsureThreadInfo();
    LOG_CALL(HkWorld_LockCriticalOperations);
    REQUIRE_FUNCTION_POINTER(HkWorld_LockCriticalOperations)
    pHkWorld_LockCriticalOperations(world);
}

void HkWorld_UnlockCriticalOperations(void* world) { EnsureThreadInfo();
    LOG_CALL(HkWorld_UnlockCriticalOperations);
    REQUIRE_FUNCTION_POINTER(HkWorld_UnlockCriticalOperations)
    pHkWorld_UnlockCriticalOperations(world);
}

void HkWorld_ExecutePendingCriticalOperations(void* world) { EnsureThreadInfo();
    LOG_CALL(HkWorld_ExecutePendingCriticalOperations);
    REQUIRE_FUNCTION_POINTER(HkWorld_ExecutePendingCriticalOperations)
    pHkWorld_ExecutePendingCriticalOperations(world);
}

void HkWorld_StepDeltaTime(void* world, float deltaTime) { EnsureThreadInfo();
    LOG_CALL(HkWorld_StepDeltaTime);
    REQUIRE_FUNCTION_POINTER(HkWorld_StepDeltaTime)
    pHkWorld_StepDeltaTime(world, deltaTime);
}

void HkWorld_StepMultiThreaded(void* world, void* jobQueue, void* threadPool, float deltaTime) { EnsureThreadInfo();
    LOG_CALL(HkWorld_StepMultiThreaded);
    REQUIRE_FUNCTION_POINTER(HkWorld_StepMultiThreaded)
    pHkWorld_StepMultiThreaded(world, jobQueue, threadPool, deltaTime);
}

bool HkWorld_InitMtStep(void* world, void* jobQueue, float deltaTime) { EnsureThreadInfo();
    LOG_CALL(HkWorld_InitMtStep);
    REQUIRE_FUNCTION_POINTER(HkWorld_InitMtStep)
    return pHkWorld_InitMtStep(world, jobQueue, deltaTime);
}

bool HkWorld_FinishMtStep(void* world, void* jobQueue, void* threadPool) { EnsureThreadInfo();
    LOG_CALL(HkWorld_FinishMtStep);
    REQUIRE_FUNCTION_POINTER(HkWorld_FinishMtStep)
    return pHkWorld_FinishMtStep(world, jobQueue, threadPool);
}

void HkWorld_ExecuteViolatedConstraintProjections(void* world, void* constraintListener, bool doProjections) { EnsureThreadInfo();
    LOG_CALL(HkWorld_ExecuteViolatedConstraintProjections);
    REQUIRE_FUNCTION_POINTER(HkWorld_ExecuteViolatedConstraintProjections)
    pHkWorld_ExecuteViolatedConstraintProjections(world, constraintListener, doProjections);
}

void HkWorld_ReportRuntimeDataConstraints(void* world) { EnsureThreadInfo();
    LOG_CALL(HkWorld_ReportRuntimeDataConstraints);
    REQUIRE_FUNCTION_POINTER(HkWorld_ReportRuntimeDataConstraints)
    pHkWorld_ReportRuntimeDataConstraints(world);
}

void HkWorld_AddConstraint(void* world, void* constraint) { EnsureThreadInfo();
    LOG_CALL(HkWorld_AddConstraint);
    REQUIRE_FUNCTION_POINTER(HkWorld_AddConstraint)
    pHkWorld_AddConstraint(world, constraint);
}

void HkWorld_RemoveConstraint(void* world, void* constraint) { EnsureThreadInfo();
    LOG_CALL(HkWorld_RemoveConstraint);
    REQUIRE_FUNCTION_POINTER(HkWorld_RemoveConstraint)
    pHkWorld_RemoveConstraint(world, constraint);
}

void HkWorld_AddEntity(void* world, void* entity) { EnsureThreadInfo();
    LOG_CALL(HkWorld_AddEntity);
    REQUIRE_FUNCTION_POINTER(HkWorld_AddEntity)
    pHkWorld_AddEntity(world, entity);
}

void HkWorld_RemoveEntity(void* world, void* entity) { EnsureThreadInfo();
    LOG_CALL(HkWorld_RemoveEntity);
    REQUIRE_FUNCTION_POINTER(HkWorld_RemoveEntity)
    pHkWorld_RemoveEntity(world, entity);
}

void HkWorld_AddPhantom(void* world, void* phantom) { EnsureThreadInfo();
    LOG_CALL(HkWorld_AddPhantom);
    REQUIRE_FUNCTION_POINTER(HkWorld_AddPhantom)
    pHkWorld_AddPhantom(world, phantom);
}

void HkWorld_RemovePhantom(void* world, void* phantom) { EnsureThreadInfo();
    LOG_CALL(HkWorld_RemovePhantom);
    REQUIRE_FUNCTION_POINTER(HkWorld_RemovePhantom)
    pHkWorld_RemovePhantom(world, phantom);
}

void HkWorld_AddPhysicsSystem(void* world, void* system) { EnsureThreadInfo();
    LOG_CALL(HkWorld_AddPhysicsSystem);
    REQUIRE_FUNCTION_POINTER(HkWorld_AddPhysicsSystem)
    pHkWorld_AddPhysicsSystem(world, system);
}

void HkWorld_RemovePhysicsSystem(void* world, void* system) { EnsureThreadInfo();
    LOG_CALL(HkWorld_RemovePhysicsSystem);
    REQUIRE_FUNCTION_POINTER(HkWorld_RemovePhysicsSystem)
    pHkWorld_RemovePhysicsSystem(world, system);
}

void HkWorld_GetPenetrationsShape(void* world, void* bodyCollector, void* shape, Vector3 translation, Quaternion rotation, int32_t filter, void* buffer) { EnsureThreadInfo();
    LOG_CALL(HkWorld_GetPenetrationsShape);
    REQUIRE_FUNCTION_POINTER(HkWorld_GetPenetrationsShape)
    pHkWorld_GetPenetrationsShape(world, bodyCollector, shape, translation, rotation, filter, buffer);
}

void HkWorld_GetPenetrationsBox(void* world, void* bodyCollector, Vector3 halfExtents, Vector3 translation, Quaternion rotation, int32_t filter, void* buffer) { EnsureThreadInfo();
    LOG_CALL(HkWorld_GetPenetrationsBox);
    REQUIRE_FUNCTION_POINTER(HkWorld_GetPenetrationsBox)
    pHkWorld_GetPenetrationsBox(world, bodyCollector, halfExtents, translation, rotation, filter, buffer);
}

void HkWorld_GetPenetrationsShapeShape(void* world, void* bodyCollector, void* shape1, Vector3 translation1, Quaternion rotation1, void* shape2, Vector3 translation2, Quaternion rotation2, void* buffer) { EnsureThreadInfo();
    LOG_CALL(HkWorld_GetPenetrationsShapeShape);
    REQUIRE_FUNCTION_POINTER(HkWorld_GetPenetrationsShapeShape)
    pHkWorld_GetPenetrationsShapeShape(world, bodyCollector, shape1, translation1, rotation1, shape2, translation2, rotation2, buffer);
}

bool HkWorld_IsPenetratingShapeShape(void* world, void* shape1, Vector3 translation1, Quaternion rotation1, void* shape2, Vector3 translation2, Quaternion rotation2) { EnsureThreadInfo();
    LOG_CALL(HkWorld_IsPenetratingShapeShape);
    REQUIRE_FUNCTION_POINTER(HkWorld_IsPenetratingShapeShape)
    return pHkWorld_IsPenetratingShapeShape(world, shape1, translation1, rotation1, shape2, translation2, rotation2);
}

bool HkWorld_IsPenetratingShapeShapeTransform(void* world, void* shape1, Matrix transform1, void* shape2, Matrix transform2) { EnsureThreadInfo();
    LOG_CALL(HkWorld_IsPenetratingShapeShapeTransform);
    REQUIRE_FUNCTION_POINTER(HkWorld_IsPenetratingShapeShapeTransform)
    return pHkWorld_IsPenetratingShapeShapeTransform(world, shape1, transform1, shape2, transform2);
}

bool HkWorld_CastShape(void* world, Vector3 to, void* shape, Matrix transform, int32_t filterLayer, float extraPenetration, void* outResult) { EnsureThreadInfo();
    LOG_CALL(HkWorld_CastShape);
    REQUIRE_FUNCTION_POINTER(HkWorld_CastShape)
    return pHkWorld_CastShape(world, to, shape, transform, filterLayer, extraPenetration, outResult);
}

bool HkWorld_CastShapeReturnPoint(void* world, Vector3 to, void* shape, Matrix transform, int32_t filterLayer, float extraPenetration, void* outPosition) { EnsureThreadInfo();
    LOG_CALL(HkWorld_CastShapeReturnPoint);
    REQUIRE_FUNCTION_POINTER(HkWorld_CastShapeReturnPoint)
    return pHkWorld_CastShapeReturnPoint(world, to, shape, transform, filterLayer, extraPenetration, outPosition);
}

bool HkWorld_CastShapeReturnContact(void* world, Vector3 to, void* shape, Matrix transform, int32_t filterLayer, float extraPenetration, void* outPoint) { EnsureThreadInfo();
    LOG_CALL(HkWorld_CastShapeReturnContact);
    REQUIRE_FUNCTION_POINTER(HkWorld_CastShapeReturnContact)
    return pHkWorld_CastShapeReturnContact(world, to, shape, transform, filterLayer, extraPenetration, outPoint);
}

bool HkWorld_CastShapeReturnContactData(void* world, Vector3 to, void* shape, Matrix transform, uint32_t collisionFilterInfo, float extraPenetration, void* outPosition, void* outNormal, void* outDistance) { EnsureThreadInfo();
    LOG_CALL(HkWorld_CastShapeReturnContactData);
    REQUIRE_FUNCTION_POINTER(HkWorld_CastShapeReturnContactData)
    return pHkWorld_CastShapeReturnContactData(world, to, shape, transform, collisionFilterInfo, extraPenetration, outPosition, outNormal, outDistance);
}

bool HkWorld_CastShapeReturnContactBodyData(void* world, Vector3 to, void* shape, Matrix transform, uint32_t collisionFilterInfo, float extraPenetration, void* hitInfo) { EnsureThreadInfo();
    LOG_CALL(HkWorld_CastShapeReturnContactBodyData);
    REQUIRE_FUNCTION_POINTER(HkWorld_CastShapeReturnContactBodyData)
    return pHkWorld_CastShapeReturnContactBodyData(world, to, shape, transform, collisionFilterInfo, extraPenetration, hitInfo);
}

bool HkWorld_CastShapeReturnContactBodyDatas(void* world, Vector3 to, void* shape, Matrix transform, uint32_t collisionFilterInfo, float extraPenetration, void* buffer) { EnsureThreadInfo();
    LOG_CALL(HkWorld_CastShapeReturnContactBodyDatas);
    REQUIRE_FUNCTION_POINTER(HkWorld_CastShapeReturnContactBodyDatas)
    return pHkWorld_CastShapeReturnContactBodyDatas(world, to, shape, transform, collisionFilterInfo, extraPenetration, buffer);
}

void HkWorld_CastRayAll(void* world, Vector3 from, Vector3 to, int32_t raycastFilterLayer, void* buffer) { EnsureThreadInfo();
    LOG_CALL(HkWorld_CastRayAll);
    REQUIRE_FUNCTION_POINTER(HkWorld_CastRayAll)
    pHkWorld_CastRayAll(world, from, to, raycastFilterLayer, buffer);
}

bool HkWorld_CastRayCollisionFilter(void* world, Vector3 from, Vector3 to, uint32_t colllisionFilter, bool ignoreConvexShape, void* outConvexRadius, void* hitInfo) { EnsureThreadInfo();
    LOG_CALL(HkWorld_CastRayCollisionFilter);
    REQUIRE_FUNCTION_POINTER(HkWorld_CastRayCollisionFilter)
    return pHkWorld_CastRayCollisionFilter(world, from, to, colllisionFilter, ignoreConvexShape, outConvexRadius, hitInfo);
}

bool HkWorld_CastRayFilterLayer(void* world, Vector3 from, Vector3 to, int32_t raycastFilterLayer, bool useFilter, void* hitInfo) { EnsureThreadInfo();
    LOG_CALL(HkWorld_CastRayFilterLayer);
    REQUIRE_FUNCTION_POINTER(HkWorld_CastRayFilterLayer)
    return pHkWorld_CastRayFilterLayer(world, from, to, raycastFilterLayer, useFilter, hitInfo);
}

void HkWorld_MarkForWrite(void* world) { EnsureThreadInfo();
    LOG_CALL(HkWorld_MarkForWrite);
    REQUIRE_FUNCTION_POINTER(HkWorld_MarkForWrite)
    pHkWorld_MarkForWrite(world);
}

void HkWorld_UnmarkForWrite(void* world) { EnsureThreadInfo();
    LOG_CALL(HkWorld_UnmarkForWrite);
    REQUIRE_FUNCTION_POINTER(HkWorld_UnmarkForWrite)
    pHkWorld_UnmarkForWrite(world);
}

void HkWorld_RefreshCollisionFilterOnEntity(void* world, void* entity) { EnsureThreadInfo();
    LOG_CALL(HkWorld_RefreshCollisionFilterOnEntity);
    REQUIRE_FUNCTION_POINTER(HkWorld_RefreshCollisionFilterOnEntity)
    pHkWorld_RefreshCollisionFilterOnEntity(world, entity);
}

void HkWorld_RefreshCollisionFilterOnWorld(void* world) { EnsureThreadInfo();
    LOG_CALL(HkWorld_RefreshCollisionFilterOnWorld);
    REQUIRE_FUNCTION_POINTER(HkWorld_RefreshCollisionFilterOnWorld)
    pHkWorld_RefreshCollisionFilterOnWorld(world);
}

void HkWorld_ReintegrateEntity(void* world, void* entity) { EnsureThreadInfo();
    LOG_CALL(HkWorld_ReintegrateEntity);
    REQUIRE_FUNCTION_POINTER(HkWorld_ReintegrateEntity)
    pHkWorld_ReintegrateEntity(world, entity);
}

void HkWorld_AddAction(void* world, void* action) { EnsureThreadInfo();
    LOG_CALL(HkWorld_AddAction);
    REQUIRE_FUNCTION_POINTER(HkWorld_AddAction)
    pHkWorld_AddAction(world, action);
}

void HkWorld_RemoveAction(void* world, void* action) { EnsureThreadInfo();
    LOG_CALL(HkWorld_RemoveAction);
    REQUIRE_FUNCTION_POINTER(HkWorld_RemoveAction)
    pHkWorld_RemoveAction(world, action);
}

void* HkWorld_EnsureBatchSizes(void* arr, void* size, int32_t count, int32_t newCount) { EnsureThreadInfo();
    LOG_CALL(HkWorld_EnsureBatchSizes);
    REQUIRE_FUNCTION_POINTER(HkWorld_EnsureBatchSizes)
    return pHkWorld_EnsureBatchSizes(arr, size, count, newCount);
}

void HkWorld_SetBatchBody(void* arr, int32_t index, void* body) { EnsureThreadInfo();
    LOG_CALL(HkWorld_SetBatchBody);
    REQUIRE_FUNCTION_POINTER(HkWorld_SetBatchBody)
    pHkWorld_SetBatchBody(arr, index, body);
}

void HkWorld_AddEntityBatch(void* world, void* arr, int32_t count) { EnsureThreadInfo();
    LOG_CALL(HkWorld_AddEntityBatch);
    REQUIRE_FUNCTION_POINTER(HkWorld_AddEntityBatch)
    pHkWorld_AddEntityBatch(world, arr, count);
}

void HkWorld_RemoveEntityBatch(void* world, void* arr, int32_t count) { EnsureThreadInfo();
    LOG_CALL(HkWorld_RemoveEntityBatch);
    REQUIRE_FUNCTION_POINTER(HkWorld_RemoveEntityBatch)
    pHkWorld_RemoveEntityBatch(world, arr, count);
}

int32_t HkWorld_GetActiveSimulationIslandsCount(void* world) { EnsureThreadInfo();
    LOG_CALL(HkWorld_GetActiveSimulationIslandsCount);
    REQUIRE_FUNCTION_POINTER(HkWorld_GetActiveSimulationIslandsCount)
    return pHkWorld_GetActiveSimulationIslandsCount(world);
}

int32_t HkWorld_GetActiveSimulationIslandEntities(void* world, int32_t islandIndex, void* entities) { EnsureThreadInfo();
    LOG_CALL(HkWorld_GetActiveSimulationIslandEntities);
    REQUIRE_FUNCTION_POINTER(HkWorld_GetActiveSimulationIslandEntities)
    return pHkWorld_GetActiveSimulationIslandEntities(world, islandIndex, entities);
}

void HkWorld_DeactivateSimulationIslandRigidBodies(void* world, void* rigidBody) { EnsureThreadInfo();
    LOG_CALL(HkWorld_DeactivateSimulationIslandRigidBodies);
    REQUIRE_FUNCTION_POINTER(HkWorld_DeactivateSimulationIslandRigidBodies)
    pHkWorld_DeactivateSimulationIslandRigidBodies(world, rigidBody);
}

bool HkWorld_IsActiveSimulationIsland(void* world, void* rigidBody) { EnsureThreadInfo();
    LOG_CALL(HkWorld_IsActiveSimulationIsland);
    REQUIRE_FUNCTION_POINTER(HkWorld_IsActiveSimulationIsland)
    return pHkWorld_IsActiveSimulationIsland(world, rigidBody);
}

int32_t HkWorld_GetConstraintCount(void* world) { EnsureThreadInfo();
    LOG_CALL(HkWorld_GetConstraintCount);
    REQUIRE_FUNCTION_POINTER(HkWorld_GetConstraintCount)
    return pHkWorld_GetConstraintCount(world);
}

int32_t HkWorld_GetActionCount(void* world) { EnsureThreadInfo();
    LOG_CALL(HkWorld_GetActionCount);
    REQUIRE_FUNCTION_POINTER(HkWorld_GetActionCount)
    return pHkWorld_GetActionCount(world);
}

void* HkWorld_GetFixedBody(void* world) { EnsureThreadInfo();
    LOG_CALL(HkWorld_GetFixedBody);
    REQUIRE_FUNCTION_POINTER(HkWorld_GetFixedBody)
    return pHkWorld_GetFixedBody(world);
}

void HkWorld_ReadSimulationIslandInfos(void* world, void* buffer) { EnsureThreadInfo();
    LOG_CALL(HkWorld_ReadSimulationIslandInfos);
    REQUIRE_FUNCTION_POINTER(HkWorld_ReadSimulationIslandInfos)
    pHkWorld_ReadSimulationIslandInfos(world, buffer);
}

Vector3 HkWorld_GetGravity(void* world) { EnsureThreadInfo();
    LOG_CALL(HkWorld_GetGravity);
    REQUIRE_FUNCTION_POINTER(HkWorld_GetGravity)
    return pHkWorld_GetGravity(world);
}

void HkWorld_SetGravity(void* world, Vector3 value) { EnsureThreadInfo();
    LOG_CALL(HkWorld_SetGravity);
    REQUIRE_FUNCTION_POINTER(HkWorld_SetGravity)
    pHkWorld_SetGravity(world, value);
}

float HkWorld_GetDeactivationRotationSqrdA(void* world) { EnsureThreadInfo();
    LOG_CALL(HkWorld_GetDeactivationRotationSqrdA);
    REQUIRE_FUNCTION_POINTER(HkWorld_GetDeactivationRotationSqrdA)
    return pHkWorld_GetDeactivationRotationSqrdA(world);
}

void HkWorld_SetDeactivationRotationSqrdA(void* world, float value) { EnsureThreadInfo();
    LOG_CALL(HkWorld_SetDeactivationRotationSqrdA);
    REQUIRE_FUNCTION_POINTER(HkWorld_SetDeactivationRotationSqrdA)
    pHkWorld_SetDeactivationRotationSqrdA(world, value);
}

float HkWorld_GetDeactivationRotationSqrdB(void* world) { EnsureThreadInfo();
    LOG_CALL(HkWorld_GetDeactivationRotationSqrdB);
    REQUIRE_FUNCTION_POINTER(HkWorld_GetDeactivationRotationSqrdB)
    return pHkWorld_GetDeactivationRotationSqrdB(world);
}

void HkWorld_SetDeactivationRotationSqrdB(void* world, float value) { EnsureThreadInfo();
    LOG_CALL(HkWorld_SetDeactivationRotationSqrdB);
    REQUIRE_FUNCTION_POINTER(HkWorld_SetDeactivationRotationSqrdB)
    pHkWorld_SetDeactivationRotationSqrdB(world, value);
}

void HkWorld_AddWorldExtension(void* world, void* extension) { EnsureThreadInfo();
    LOG_CALL(HkWorld_AddWorldExtension);
    REQUIRE_FUNCTION_POINTER(HkWorld_AddWorldExtension)
    pHkWorld_AddWorldExtension(world, extension);
}

void HkWorld_Release(void* world, void* filter, void* penetrationHits, void* addBatch, void* removeBatch) { EnsureThreadInfo();
    LOG_CALL(HkWorld_Release);
    REQUIRE_FUNCTION_POINTER(HkWorld_Release)
    pHkWorld_Release(world, filter, penetrationHits, addBatch, removeBatch);
}

void* HkPhysicsContext_Create(void) { EnsureThreadInfo();
    LOG_CALL(HkPhysicsContext_Create);
    REQUIRE_FUNCTION_POINTER(HkPhysicsContext_Create)
    return pHkPhysicsContext_Create();
}

void HkPhysicsContext_RegisterAllPhysicsProcesses(void) { EnsureThreadInfo();
    LOG_CALL(HkPhysicsContext_RegisterAllPhysicsProcesses);
    REQUIRE_FUNCTION_POINTER(HkPhysicsContext_RegisterAllPhysicsProcesses)
    pHkPhysicsContext_RegisterAllPhysicsProcesses();
}

void HkPhysicsContext_AddWorld(void* physicsContext, void* world) { EnsureThreadInfo();
    LOG_CALL(HkPhysicsContext_AddWorld);
    REQUIRE_FUNCTION_POINTER(HkPhysicsContext_AddWorld)
    pHkPhysicsContext_AddWorld(physicsContext, world);
}

void HkPhysicsContext_RemoveWorld(void* physicsContext, void* world) { EnsureThreadInfo();
    LOG_CALL(HkPhysicsContext_RemoveWorld);
    REQUIRE_FUNCTION_POINTER(HkPhysicsContext_RemoveWorld)
    pHkPhysicsContext_RemoveWorld(physicsContext, world);
}

int32_t HkPhysicsContext_GetNumWorlds(void* physicsContext) { EnsureThreadInfo();
    LOG_CALL(HkPhysicsContext_GetNumWorlds);
    REQUIRE_FUNCTION_POINTER(HkPhysicsContext_GetNumWorlds)
    return pHkPhysicsContext_GetNumWorlds(physicsContext);
}

void HkPhysicsContext_SyncTimers(void* physicsContext, void* threadPool) { EnsureThreadInfo();
    LOG_CALL(HkPhysicsContext_SyncTimers);
    REQUIRE_FUNCTION_POINTER(HkPhysicsContext_SyncTimers)
    pHkPhysicsContext_SyncTimers(physicsContext, threadPool);
}

void HkPhysicsContext_Release(void* physicsContext) { EnsureThreadInfo();
    LOG_CALL(HkPhysicsContext_Release);
    REQUIRE_FUNCTION_POINTER(HkPhysicsContext_Release)
    pHkPhysicsContext_Release(physicsContext);
}

void* HkGroupFilter_Create(void* world) { EnsureThreadInfo();
    LOG_CALL(HkGroupFilter_Create);
    REQUIRE_FUNCTION_POINTER(HkGroupFilter_Create)
    return pHkGroupFilter_Create(world);
}

bool HkGroupFilter_IsCollisionEnabled(void* filter, uint32_t colllinfo1, uint32_t collinfo2) { EnsureThreadInfo();
    LOG_CALL(HkGroupFilter_IsCollisionEnabled);
    REQUIRE_FUNCTION_POINTER(HkGroupFilter_IsCollisionEnabled)
    return pHkGroupFilter_IsCollisionEnabled(filter, colllinfo1, collinfo2);
}

void* HkpAabbPhantom_Create(Vector3 min, Vector3 max, uint32_t collisionFilterInfo, void* collidableAddedD, void* collidableRemovedD) { EnsureThreadInfo();
    LOG_CALL(HkpAabbPhantom_Create);
    REQUIRE_FUNCTION_POINTER(HkpAabbPhantom_Create)
    auto result = pHkpAabbPhantom_Create(min, max, collisionFilterInfo, bridge_void_ptr_ptr(collidableAddedD), bridge_void_ptr_ptr(collidableRemovedD));
    register_callback_owner(result, {callback_owner_binding{&release_void_ptr_ptr, collidableAddedD}, callback_owner_binding{&release_void_ptr_ptr, collidableRemovedD}});
    return result;
}

void HkpAabbPhantom_GetAabb(void* instance, void* min, void* max) { EnsureThreadInfo();
    LOG_CALL(HkpAabbPhantom_GetAabb);
    REQUIRE_FUNCTION_POINTER(HkpAabbPhantom_GetAabb)
    pHkpAabbPhantom_GetAabb(instance, min, max);
}

void HkpAabbPhantom_SetAabb(void* instance, Vector3 min, Vector3 max) { EnsureThreadInfo();
    LOG_CALL(HkpAabbPhantom_SetAabb);
    REQUIRE_FUNCTION_POINTER(HkpAabbPhantom_SetAabb)
    pHkpAabbPhantom_SetAabb(instance, min, max);
}

void HkpAabbPhantom_Release(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkpAabbPhantom_Release);
    REQUIRE_FUNCTION_POINTER(HkpAabbPhantom_Release)
    pHkpAabbPhantom_Release(instance);
    release_callback_owner(instance);
}

void* HkpCollidableAddedEvent_GetRigidBody(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkpCollidableAddedEvent_GetRigidBody);
    REQUIRE_FUNCTION_POINTER(HkpCollidableAddedEvent_GetRigidBody)
    return pHkpCollidableAddedEvent_GetRigidBody(instance);
}

void* HkpCollidableRemovedEvent_GetRigidBody(void* instance) { EnsureThreadInfo();
    LOG_CALL(HkpCollidableRemovedEvent_GetRigidBody);
    REQUIRE_FUNCTION_POINTER(HkpCollidableRemovedEvent_GetRigidBody)
    return pHkpCollidableRemovedEvent_GetRigidBody(instance);
}

void HkSimpleShapePhantom_SetTransform(void* instance, Matrix matrix) { EnsureThreadInfo();
    LOG_CALL(HkSimpleShapePhantom_SetTransform);
    REQUIRE_FUNCTION_POINTER(HkSimpleShapePhantom_SetTransform)
    pHkSimpleShapePhantom_SetTransform(instance, matrix);
}

void HkIntermediateBuffer_ReleaseUnmanaged(void* memory) { EnsureThreadInfo();
    LOG_CALL(HkIntermediateBuffer_ReleaseUnmanaged);
    REQUIRE_FUNCTION_POINTER(HkIntermediateBuffer_ReleaseUnmanaged)
    pHkIntermediateBuffer_ReleaseUnmanaged(memory);
}

} // extern "C"