// Windows NT type definitions for Linux PE loader
// Only types actually used by the DLL loader and Win32 shim layer.

#ifndef WINNT_TYPES_H
#define WINNT_TYPES_H

#include <stddef.h>
#include <stdint.h>
#include <pthread.h>

// Calling conventions

#define WINAPI __attribute__((ms_abi))

// Boolean constants

#define TRUE  1
#define FALSE 0

// DLL lifecycle constants

#define DLL_PROCESS_ATTACH 1
#define DLL_PROCESS_DETACH 0
#define DLL_THREAD_ATTACH  2
#define DLL_THREAD_DETACH  3

// Primitive types

typedef uint8_t  BOOLEAN, BOOL, *PBOOL, UBYTE;
typedef void    *PVOID, *LPVOID;
typedef const void *LPCVOID;
typedef uint8_t  BYTE, *PBYTE, *LPBYTE;
typedef int8_t   CHAR;
typedef char    *PCHAR;
typedef CHAR    *LPSTR;
typedef const char *LPCSTR;
typedef uint16_t WCHAR, *PWCHAR;
typedef WCHAR   *LPWSTR, *PWSTR;
typedef const WCHAR *LPCWSTR, *LPCWCH;
typedef uint8_t  UCHAR, *PUCHAR;
typedef uint16_t SHORT, USHORT, *PUSHORT, WORD;
typedef int32_t  INT, LONG, *LPLONG;
typedef uint32_t UINT, DWORD, *PDWORD, *LPDWORD;
typedef DWORD   *DWORD_PTR;
typedef uint32_t ULONG, *PULONG;
typedef int64_t  LONGLONG, DWORD64, *PDWORD64;
typedef uint64_t ULONGLONG, *PULONGLONG;
typedef ULONGLONG DWORDLONG;
typedef uint64_t ULONG64, *PULONG64;
typedef uint64_t QWORD, *PQWORD;

// Pointer-width types (64-bit on x86-64)

typedef unsigned long ULONG_PTR;
typedef size_t SIZE_T;
typedef ULONG_PTR KAFFINITY;

// Handle types

#define HANDLE  PVOID
#define HMODULE PVOID
typedef HANDLE *PHANDLE;
#define INVALID_HANDLE_VALUE ((HANDLE)(-1))

// Function pointer types

typedef LONG HRESULT;
typedef LONG NTSTATUS;
typedef int (WINAPI *FARPROC)();

// Opaque/forward types used in API signatures

typedef void *LPSECURITY_ATTRIBUTES;
typedef void *LPTOP_LEVEL_EXCEPTION_FILTER;
typedef void *PSLIST_HEADER;
typedef void *_locale_t;

// Composite type aliases

typedef CHAR CCHAR;
typedef SHORT CSHORT;
typedef LONGLONG LARGE_INTEGER;
typedef LARGE_INTEGER PHYSICAL_ADDRESS;
typedef UCHAR KIRQL;
typedef CHAR KPROCESSOR_MODE;
typedef LONG KPRIORITY;
typedef ULONG ACCESS_MASK;
typedef ULONG SECURITY_INFORMATION;
typedef ULONG_PTR PFN_NUMBER;

// NTSTATUS codes

#define STATUS_WAIT_0                   0
#define STATUS_SUCCESS                  0
#define STILL_ACTIVE                    259
#define STATUS_ALERTED                  0x00000101
#define STATUS_TIMEOUT                  0x00000102
#define STATUS_PENDING                  0x00000103
#define STATUS_FAILURE                  0xC0000001
#define STATUS_NOT_IMPLEMENTED          0xC0000002
#define STATUS_INVALID_PARAMETER        0xC000000D
#define STATUS_INVALID_DEVICE_REQUEST   0xC0000010
#define STATUS_MORE_PROCESSING_REQUIRED 0xC0000016
#define STATUS_NO_MEMORY                0xC0000017
#define STATUS_ACCESS_DENIED            0xC0000022
#define STATUS_BUFFER_TOO_SMALL         0xC0000023
#define STATUS_OBJECT_NAME_INVALID      0xC0000023
#define STATUS_MUTANT_NOT_OWNED         0xC0000046
#define STATUS_DELETE_PENDING           0xC0000056
#define STATUS_RESOURCES                0xC000009A
#define STATUS_INSUFFICIENT_RESOURCES   0xC000009A
#define STATUS_DEVICE_NOT_CONNECTED     0xC000009D
#define STATUS_NOT_SUPPORTED            0xC00000BB
#define STATUS_INVALID_PARAMETER_2      0xC00000F0
#define STATUS_CANCELLED                0xC0000120
#define STATUS_DEVICE_REMOVED           0xC00002B6
#define STATUS_BUFFER_OVERFLOW          0x80000005
#define STATUS_LONG_JUMP                0x80000026
#define STATUS_UNWIND_CONSOLIDATE       0x80000029
#define STATUS_UNWIND                   0xC0000027
#define STATUS_INVALID_DISPOSITION      ((DWORD)0xC0000026L)

#define NT_SUCCESS(status) ((NTSTATUS)(status) >= 0)

// Shared user data address

#define MM_SHARED_USER_DATA_VA 0x7ffe0000

// Wait object limits

#define MAX_WAIT_OBJECTS 64
#define PROCESSOR_FEATURE_MAX 64

// Core structures

typedef struct _FILETIME {
    DWORD dwLowDateTime;
    DWORD dwHighDateTime;
} FILETIME, *PFILETIME, *LPFILETIME;

typedef struct ansi_string {
    USHORT length;
    USHORT max_length;
    char *buf;
} ANSI_STRING, *PANSI_STRING;

typedef struct unicode_string {
    USHORT Length;
    USHORT MaximumLength;
    wchar_t *Buffer;
} UNICODE_STRING, *PUNICODE_STRING;

typedef struct guid {
    ULONG data1;
    USHORT data2;
    USHORT data3;
    UCHAR data4[8];
} GUID, *PGUID, *LPGUID;

typedef struct _MEMORYSTATUSEX {
    DWORD     dwLength;
    DWORD     dwMemoryLoad;
    DWORDLONG ullTotalPhys;
    DWORDLONG ullAvailPhys;
    DWORDLONG ullTotalPageFile;
    DWORDLONG ullAvailPageFile;
    DWORDLONG ullTotalVirtual;
    DWORDLONG ullAvailVirtual;
    DWORDLONG ullAvailExtendedVirtual;
} MEMORYSTATUSEX, *LPMEMORYSTATUSEX;

// RTL bitmap (used for TLS slot allocation)

typedef struct _RTL_BITMAP {
    ULONG SizeOfBitMap;
    LPBYTE Buffer;
} RTL_BITMAP, *PRTL_BITMAP;

typedef const RTL_BITMAP *PCRTL_BITMAP;

typedef struct _RTL_BITMAP_RUN {
    ULONG StartingIndex;
    ULONG NumberOfBits;
} RTL_BITMAP_RUN, *PRTL_BITMAP_RUN;

typedef const RTL_BITMAP_RUN *PCRTL_BITMAP_RUN;

// One-time initialization

typedef union _RTL_RUN_ONCE {
    PVOID Ptr;
} RTL_RUN_ONCE, *PRTL_RUN_ONCE;

// FLS callback

typedef void (*PFLS_CALLBACK_FUNCTION)(PVOID lpFlsData);

// Critical section (POSIX-backed)

typedef struct {
    pthread_mutex_t mutex;
    uint32_t spin_count;
} POSIX_CRITICAL_SECTION;

typedef struct {
    POSIX_CRITICAL_SECTION *impl;
} CRITICAL_SECTION, *LPCRITICAL_SECTION;

// String comparison results

#define CSTR_LESS_THAN    0
#define CSTR_EQUAL        1
#define CSTR_GREATER_THAN 2

// KUSER_SHARED_DATA and dependencies

struct ksystem_time {
    ULONG low_part;
    LONG high1_time;
    LONG high2_time;
};

enum nt_product_type {
    nt_product_win_nt = 1, nt_product_lan_man_nt, nt_product_server
};

enum alt_arch_type {
    arch_type_standard, arch_type_nex98x86, end_alternatives
};

typedef struct _KUSER_SHARED_DATA {
    ULONG tick_count;
    ULONG tick_count_multiplier;
    volatile struct ksystem_time interrupt_time;
    volatile struct ksystem_time system_time;
    volatile struct ksystem_time time_zone_bias;
    USHORT image_number_low;
    USHORT image_number_high;
    wchar_t nt_system_root[260];
    ULONG max_stack_trace_depth;
    ULONG crypto_exponent;
    ULONG time_zone_id;
    ULONG large_page_min;
    ULONG reserved2[7];
    enum nt_product_type nt_product_type;
    BOOLEAN product_type_is_valid;
    ULONG nt_major_version;
    ULONG nt_minor_version;
    BOOLEAN processor_features[PROCESSOR_FEATURE_MAX];
    ULONG reserved1;
    ULONG reserved3;
    volatile LONG time_slip;
    enum alt_arch_type alt_arch_type;
    LARGE_INTEGER system_expiration_date;
    ULONG suite_mask;
    BOOLEAN kdbg_enabled;
    volatile ULONG active_console;
    volatile ULONG dismount_count;
    ULONG com_plus_package;
    ULONG last_system_rite_event_tick_count;
    ULONG num_phys_pages;
    BOOLEAN safe_boot_mode;
    ULONG trace_log;
    ULONGLONG fill0;
    ULONGLONG sys_call[4];
    union {
        volatile struct ksystem_time tick_count;
        volatile ULONG64 tick_count_quad;
    } tick;
} KUSER_SHARED_DATA, *PKUSER_SHARED_DATA;

// SEH / Exception handling

#define EXCEPTION_MAXIMUM_PARAMETERS 15
#define MAXIMUM_SUPPORTED_EXTENSION  512
#define SIZE_OF_80387_REGISTERS      80

typedef enum {
    ExceptionContinueExecution = 0,
    ExceptionContinueSearch = 1,
    ExceptionNestedException = 2,
    ExceptionCollidedUnwind = 3
} EXCEPTION_DISPOSITION;

typedef struct _EXCEPTION_RECORD {
    DWORD ExceptionCode;
    DWORD ExceptionFlags;
    struct _EXCEPTION_RECORD *ExceptionRecord;
    PVOID ExceptionAddress;
    long NumberParameters;
    ULONG_PTR ExceptionInformation[EXCEPTION_MAXIMUM_PARAMETERS];
} EXCEPTION_RECORD, *PEXCEPTION_RECORD;

typedef struct _FLOATING_SAVE_AREA {
    DWORD ControlWord;
    DWORD StatusWord;
    DWORD TagWord;
    DWORD ErrorOffset;
    DWORD ErrorSelector;
    DWORD DataOffset;
    DWORD DataSelector;
    BYTE RegisterArea[SIZE_OF_80387_REGISTERS];
    DWORD Cr0NpxState;
} FLOATING_SAVE_AREA;

typedef struct _M128A {
    ULONGLONG Low;
    LONGLONG High;
} M128A, *PM128A;

typedef struct _XSAVE_FORMAT {
    WORD   ControlWord;
    WORD   StatusWord;
    BYTE   TagWord;
    BYTE   Reserved1;
    WORD   ErrorOpcode;
    DWORD  ErrorOffset;
    WORD   ErrorSelector;
    WORD   Reserved2;
    DWORD  DataOffset;
    WORD   DataSelector;
    WORD   Reserved3;
    DWORD  MxCsr;
    DWORD  MxCsr_Mask;
    M128A  FloatRegisters[8];
    M128A  XmmRegisters[16];
    BYTE   Reserved4[96];
} XSAVE_FORMAT, *PXSAVE_FORMAT;

typedef XSAVE_FORMAT XMM_SAVE_AREA32, *PXMM_SAVE_AREA32;

typedef struct _CONTEXT {
    DWORD64 P1Home;
    DWORD64 P2Home;
    DWORD64 P3Home;
    DWORD64 P4Home;
    DWORD64 P5Home;
    DWORD64 P6Home;
    DWORD ContextFlags;
    DWORD MxCsr;
    WORD   SegCs;
    WORD   SegDs;
    WORD   SegEs;
    WORD   SegFs;
    WORD   SegGs;
    WORD   SegSs;
    DWORD EFlags;
    DWORD64 Dr0;
    DWORD64 Dr1;
    DWORD64 Dr2;
    DWORD64 Dr3;
    DWORD64 Dr6;
    DWORD64 Dr7;
    DWORD64 Rax;
    DWORD64 Rcx;
    DWORD64 Rdx;
    DWORD64 Rbx;
    DWORD64 Rsp;
    DWORD64 Rbp;
    DWORD64 Rsi;
    DWORD64 Rdi;
    DWORD64 R8;
    DWORD64 R9;
    DWORD64 R10;
    DWORD64 R11;
    DWORD64 R12;
    DWORD64 R13;
    DWORD64 R14;
    DWORD64 R15;
    DWORD64 Rip;
    union {
        XMM_SAVE_AREA32 FltSave;
        struct {
            M128A Header[2];
            M128A Legacy[8];
            M128A Xmm0;
            M128A Xmm1;
            M128A Xmm2;
            M128A Xmm3;
            M128A Xmm4;
            M128A Xmm5;
            M128A Xmm6;
            M128A Xmm7;
            M128A Xmm8;
            M128A Xmm9;
            M128A Xmm10;
            M128A Xmm11;
            M128A Xmm12;
            M128A Xmm13;
            M128A Xmm14;
            M128A Xmm15;
        } DUMMYSTRUCTNAME;
    } DUMMYUNIONNAME;
    M128A VectorRegister[26];
    DWORD64 VectorControl;
    DWORD64 DebugControl;
    DWORD64 LastBranchToRip;
    DWORD64 LastBranchFromRip;
    DWORD64 LastExceptionToRip;
    DWORD64 LastExceptionFromRip;
} CONTEXT, *PCONTEXT;

struct _EXCEPTION_FRAME;

typedef EXCEPTION_DISPOSITION (*PEXCEPTION_HANDLER)(
    struct _EXCEPTION_RECORD *ExceptionRecord,
    struct _EXCEPTION_FRAME *EstablisherFrame,
    PVOID *ContextRecord,
    struct _EXCEPTION_FRAME **DispatcherContext);

typedef struct _EXCEPTION_FRAME {
    struct _EXCEPTION_FRAME *prev;
    PEXCEPTION_HANDLER handler;
} EXCEPTION_FRAME, *PEXCEPTION_FRAME;

// Exception flags

#define EXCEPTION_NONCONTINUABLE  0x1
#define EXCEPTION_UNWINDING       0x2
#define EXCEPTION_EXIT_UNWIND     0x4
#define EXCEPTION_STACK_INVALID   0x8
#define EXCEPTION_NESTED_CALL     0x10
#define EXCEPTION_TARGET_UNWIND   0x20
#define EXCEPTION_COLLIDED_UNWIND 0x40

#define EXCEPTION_UNWIND (EXCEPTION_UNWINDING | EXCEPTION_EXIT_UNWIND | \
                          EXCEPTION_TARGET_UNWIND | EXCEPTION_COLLIDED_UNWIND)

#define IS_UNWINDING(Flag)    ((Flag & EXCEPTION_UNWIND) != 0)
#define IS_DISPATCHING(Flag)  ((Flag & EXCEPTION_UNWIND) == 0)
#define IS_TARGET_UNWIND(Flag) (Flag & EXCEPTION_TARGET_UNWIND)

#define EH_NONCONTINUABLE 0x01
#define EH_UNWINDING      0x02
#define EH_EXIT_UNWIND    0x04
#define EH_STACK_INVALID  0x08
#define EH_NESTED_CALL    0x10

typedef struct _EXCEPTION_POINTERS {
    PEXCEPTION_RECORD ExceptionRecord;
    PCONTEXT          ContextRecord;
} EXCEPTION_POINTERS, *PEXCEPTION_POINTERS;

// x86-64 unwind structures

typedef union _UNWIND_CODE {
    struct {
        UBYTE CodeOffset;
        UBYTE UnwindOp: 4;
        UBYTE OpInfo: 4;
    };
    USHORT FrameOffset;
} UNWIND_CODE, *PUNWIND_CODE;

#define UWOP_PUSH_NONVOL     0
#define UWOP_ALLOC_LARGE     1
#define UWOP_ALLOC_SMALL     2
#define UWOP_SET_FPREG       3
#define UWOP_SAVE_NONVOL     4
#define UWOP_SAVE_NONVOL_FAR 5
#define UWOP_SAVE_XMM        6
#define UWOP_EPILOG          6
#define UWOP_SAVE_XMM_FAR    7
#define UWOP_SPARE_CODE      7
#define UWOP_SAVE_XMM128     8
#define UWOP_SAVE_XMM128_FAR 9
#define UWOP_PUSH_MACHFRAME  10

enum {
    UNW_FLAG_NHANDLER  = 0x00,
    UNW_FLAG_EHANDLER  = 0x01,
    UNW_FLAG_UHANDLER  = 0x02,
    UNW_FLAG_CHAININFO = 0x04,
};

typedef struct _UNWIND_INFO {
    UBYTE Version: 3;
    UBYTE Flags: 5;
    UBYTE SizeOfProlog;
    UBYTE CountOfCodes;
    UBYTE FrameRegister: 4;
    UBYTE FrameOffset: 4;
    UNWIND_CODE UnwindCode[1];
    union {
        ULONG ExceptionHandler;
        ULONG FunctionEntry;
    };
    ULONG ExceptionData[];
} UNWIND_INFO, *PUNWIND_INFO;

typedef struct _RUNTIME_FUNCTION {
    DWORD BeginAddress;
    DWORD EndAddress;
    DWORD UnwindData;
} RUNTIME_FUNCTION, *PRUNTIME_FUNCTION;

#define UNWIND_HISTORY_TABLE_SIZE 12

typedef struct _UNWIND_HISTORY_TABLE_ENTRY {
    DWORD64 ImageBase;
    PRUNTIME_FUNCTION FunctionEntry;
} UNWIND_HISTORY_TABLE_ENTRY, *PUNWIND_HISTORY_TABLE_ENTRY;

typedef struct _UNWIND_HISTORY_TABLE {
    DWORD Count;
    BYTE LocalHint;
    BYTE GlobalHint;
    BYTE Search;
    BYTE Once;
    DWORD64 LowAddress;
    DWORD64 HighAddress;
    UNWIND_HISTORY_TABLE_ENTRY Entry[UNWIND_HISTORY_TABLE_SIZE];
} UNWIND_HISTORY_TABLE, *PUNWIND_HISTORY_TABLE;

typedef struct _FRAME_POINTERS {
    ULONGLONG MemoryStackFp;
    ULONGLONG BackingStoreFp;
} FRAME_POINTERS, *PFRAME_POINTERS;

typedef EXCEPTION_DISPOSITION
WINAPI EXCEPTION_ROUTINE(
    struct _EXCEPTION_RECORD *ExceptionRecord,
    PVOID EstablisherFrame,
    struct _CONTEXT *ContextRecord,
    PVOID DispatcherContext);

typedef EXCEPTION_ROUTINE *PEXCEPTION_ROUTINE;

typedef struct _DISPATCHER_CONTEXT {
    ULONG64 ControlPc;
    ULONG64 ImageBase;
    struct _RUNTIME_FUNCTION *FunctionEntry;
    ULONG64 EstablisherFrame;
    ULONG64 TargetIp;
    CONTEXT *ContextRecord;
    PEXCEPTION_ROUTINE LanguageHandler;
    PVOID HandlerData;
    struct _UNWIND_HISTORY_TABLE *HistoryTable;
    ULONG ScopeIndex;
    ULONG Fill0;
} DISPATCHER_CONTEXT, *PDISPATCHER_CONTEXT;

typedef struct _KNONVOLATILE_CONTEXT_POINTERS {
    union {
        PM128A FloatingContext[16];
        struct {
            PM128A Xmm0;
            PM128A Xmm1;
            PM128A Xmm2;
            PM128A Xmm3;
            PM128A Xmm4;
            PM128A Xmm5;
            PM128A Xmm6;
            PM128A Xmm7;
            PM128A Xmm8;
            PM128A Xmm9;
            PM128A Xmm10;
            PM128A Xmm11;
            PM128A Xmm12;
            PM128A Xmm13;
            PM128A Xmm14;
            PM128A Xmm15;
        } DUMMYSTRUCTNAME;
    } DUMMYUNIONNAME;
    union {
        PDWORD64 IntegerContext[16];
        struct {
            PDWORD64 Rax;
            PDWORD64 Rcx;
            PDWORD64 Rdx;
            PDWORD64 Rbx;
            PDWORD64 Rsp;
            PDWORD64 Rbp;
            PDWORD64 Rsi;
            PDWORD64 Rdi;
            PDWORD64 R8;
            PDWORD64 R9;
            PDWORD64 R10;
            PDWORD64 R11;
            PDWORD64 R12;
            PDWORD64 R13;
            PDWORD64 R14;
            PDWORD64 R15;
        } DUMMYSTRUCTNAME;
    } DUMMYUNIONNAME2;
} KNONVOLATILE_CONTEXT_POINTERS, *PKNONVOLATILE_CONTEXT_POINTERS;

// Enums used by Win32 shim layer

typedef enum _PROCESSINFOCLASS {
    ProcessBasicInformation = 0,
    ProcessQuotaLimits = 1,
    ProcessIoCounters = 2,
    ProcessVmCounters = 3,
    ProcessTimes = 4,
    ProcessBasePriority = 5,
    ProcessRaisePriority = 6,
    ProcessDebugPort = 7,
    ProcessExceptionPort = 8,
    ProcessAccessToken = 9,
    ProcessLdtInformation = 10,
    ProcessLdtSize = 11,
    ProcessDefaultHardErrorMode = 12,
    ProcessIoPortHandlers = 13,
    ProcessPooledUsageAndLimits = 14,
    ProcessWorkingSetWatch = 15,
    ProcessUserModeIOPL = 16,
    ProcessEnableAlignmentFaultFixup = 17,
    ProcessPriorityClass = 18,
    ProcessWx86Information = 19,
    ProcessHandleCount = 20,
    ProcessAffinityMask = 21,
    ProcessPriorityBoost = 22,
    ProcessDeviceMap = 23,
    ProcessSessionInformation = 24,
    ProcessForegroundInformation = 25,
    ProcessWow64Information = 26,
    ProcessImageFileName = 27,
    ProcessLUIDDeviceMapsEnabled = 28,
    ProcessBreakOnTermination = 29,
    ProcessDebugObjectHandle = 30,
    ProcessDebugFlags = 31,
    ProcessHandleTracing = 32,
    ProcessExecuteFlags = 34,
    ProcessTlsInformation = 35,
    ProcessCookie = 36,
    ProcessImageInformation = 37,
    ProcessCycleTime = 38,
    ProcessPagePriority = 39,
    ProcessInstrumentationCallback = 40,
    ProcessThreadStackAllocation = 41,
    ProcessWorkingSetWatchEx = 42,
    ProcessImageFileNameWin32 = 43,
    ProcessImageFileMapping = 44,
    ProcessAffinityUpdateMode = 45,
    ProcessMemoryAllocationMode = 46,
    ProcessGroupInformation = 47,
    ProcessTokenVirtualizationEnabled = 48,
    ProcessConsoleHostProcess = 49,
    ProcessWindowInformation = 50,
    MaxProcessInfoClass
} PROCESSINFOCLASS, PROCESS_INFORMATION_CLASS;

typedef enum _HEAP_INFORMATION_CLASS {
    HeapCompatibilityInformation,
    HeapEnableTerminationOnCorruption
} HEAP_INFORMATION_CLASS;

typedef enum _EVENT_INFO_CLASS {
    EventProviderBinaryTrackInfo,
    EventProviderSetReserved1,
    EventProviderSetTraits,
    EventProviderUseDescriptorType,
    MaxEventInfo
} EVENT_INFO_CLASS;

typedef enum _PROCESS_MITIGATION_POLICY {
    ProcessDEPPolicy,
    ProcessASLRPolicy,
    ProcessDynamicCodePolicy,
    ProcessStrictHandleCheckPolicy,
    ProcessSystemCallDisablePolicy,
    ProcessMitigationOptionsMask,
    ProcessExtensionPointDisablePolicy,
    ProcessControlFlowGuardPolicy,
    ProcessSignaturePolicy,
    ProcessFontDisablePolicy,
    ProcessImageLoadPolicy,
    ProcessSystemCallFilterPolicy,
    ProcessPayloadRestrictionPolicy,
    ProcessChildProcessPolicy,
    ProcessSideChannelIsolationPolicy,
    ProcessUserShadowStackPolicy,
    MaxProcessMitigationPolicy
} PROCESS_MITIGATION_POLICY, *PPROCESS_MITIGATION_POLICY;

typedef enum _FILE_INFO_BY_HANDLE_CLASS {
    FileBasicInfo,
    FileStandardInfo,
    FileNameInfo,
    FileRenameInfo,
    FileDispositionInfo,
    FileAllocationInfo,
    FileEndOfFileInfo,
    FileStreamInfo,
    FileCompressionInfo,
    FileAttributeTagInfo,
    FileIdBothDirectoryInfo,
    FileIdBothDirectoryRestartInfo,
    FileIoPriorityHintInfo,
    FileRemoteProtocolInfo,
    FileFullDirectoryInfo,
    FileFullDirectoryRestartInfo,
    FileStorageInfo,
    FileAlignmentInfo,
    FileIdInfo,
    FileIdExtdDirectoryInfo,
    FileIdExtdDirectoryRestartInfo,
    FileDispositionInfoEx,
    FileRenameInfoEx,
    FileCaseSensitiveInfo,
    FileNormalizedNameInfo,
    MaximumFileInfoByHandleClass
} FILE_INFO_BY_HANDLE_CLASS, *PFILE_INFO_BY_HANDLE_CLASS;

#endif // WINNT_TYPES_H
