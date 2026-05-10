// PE (Portable Executable) format structures and loader API.
// Covers PE32+ (64-bit) format used by x86-64 Windows DLLs.

#ifndef PE_LOADER_H
#define PE_LOADER_H

#include "win_types.h"

// DllMain function pointer type (Windows x64 ABI)
typedef BOOL (WINAPI *DllEntry_t)(PVOID hinstDLL, DWORD fdwReason, PVOID lpvReserved);

// Generic function pointer for the export table
typedef void (*generic_func)();

// ---------------------------------------------------------------------------
// PE file format structures
// ---------------------------------------------------------------------------

// DOS header (MZ stub) - 64 bytes at offset 0

typedef struct _IMAGE_DOS_HEADER {
    WORD  e_magic;      // 0x5A4D ("MZ")
    WORD  e_cblp;
    WORD  e_cp;
    WORD  e_crlc;
    WORD  e_cparhdr;
    WORD  e_minalloc;
    WORD  e_maxalloc;
    WORD  e_ss;
    WORD  e_sp;
    WORD  e_csum;
    WORD  e_ip;
    WORD  e_cs;
    WORD  e_lfarlc;
    WORD  e_ovno;
    WORD  e_res[4];
    WORD  e_oemid;
    WORD  e_oeminfo;
    WORD  e_res2[10];
    DWORD e_lfanew;     // Offset to PE signature
} IMAGE_DOS_HEADER, *PIMAGE_DOS_HEADER;

#define IMAGE_DOS_SIGNATURE 0x5A4D     // "MZ"
#define IMAGE_NT_SIGNATURE  0x00004550 // "PE\0\0"

// COFF file header - 20 bytes

typedef struct _IMAGE_FILE_HEADER {
    WORD  Machine;
    WORD  NumberOfSections;
    DWORD TimeDateStamp;
    DWORD PointerToSymbolTable;
    DWORD NumberOfSymbols;
    WORD  SizeOfOptionalHeader;
    WORD  Characteristics;
} IMAGE_FILE_HEADER, *PIMAGE_FILE_HEADER;

// File header Characteristics flags

#define IMAGE_FILE_RELOCS_STRIPPED     0x0001
#define IMAGE_FILE_EXECUTABLE_IMAGE   0x0002
#define IMAGE_FILE_LARGE_ADDRESS_AWARE 0x0020
#define IMAGE_FILE_32BIT_MACHINE      0x0100
#define IMAGE_FILE_DLL                0x2000

// Machine types (only the ones we support)

#define IMAGE_FILE_MACHINE_I386  0x014c
#define IMAGE_FILE_MACHINE_AMD64 0x8664

// Data directory entry

typedef struct _IMAGE_DATA_DIRECTORY {
    DWORD VirtualAddress;
    DWORD Size;
} IMAGE_DATA_DIRECTORY, *PIMAGE_DATA_DIRECTORY;

#define IMAGE_NUMBEROF_DIRECTORY_ENTRIES 16

// Optional header magic values

#define IMAGE_NT_OPTIONAL_HDR32_MAGIC 0x010b
#define IMAGE_NT_OPTIONAL_HDR64_MAGIC 0x020b

// Optional header - PE32 (32-bit)

typedef struct _IMAGE_OPTIONAL_HEADER32 {
    WORD  Magic;
    BYTE  MajorLinkerVersion;
    BYTE  MinorLinkerVersion;
    DWORD SizeOfCode;
    DWORD SizeOfInitializedData;
    DWORD SizeOfUninitializedData;
    DWORD AddressOfEntryPoint;
    DWORD BaseOfCode;
    DWORD BaseOfData;
    DWORD ImageBase;
    DWORD SectionAlignment;
    DWORD FileAlignment;
    WORD  MajorOperatingSystemVersion;
    WORD  MinorOperatingSystemVersion;
    WORD  MajorImageVersion;
    WORD  MinorImageVersion;
    WORD  MajorSubsystemVersion;
    WORD  MinorSubsystemVersion;
    DWORD Win32VersionValue;
    DWORD SizeOfImage;
    DWORD SizeOfHeaders;
    DWORD CheckSum;
    WORD  Subsystem;
    WORD  DllCharacteristics;
    DWORD SizeOfStackReserve;
    DWORD SizeOfStackCommit;
    DWORD SizeOfHeapReserve;
    DWORD SizeOfHeapCommit;
    DWORD LoaderFlags;
    DWORD NumberOfRvaAndSizes;
    IMAGE_DATA_DIRECTORY DataDirectory[IMAGE_NUMBEROF_DIRECTORY_ENTRIES];
} IMAGE_OPTIONAL_HEADER32, *PIMAGE_OPTIONAL_HEADER32;

// Optional header - PE32+ (64-bit, primary format)

typedef struct _IMAGE_OPTIONAL_HEADER64 {
    WORD  Magic;
    BYTE  MajorLinkerVersion;
    BYTE  MinorLinkerVersion;
    DWORD SizeOfCode;
    DWORD SizeOfInitializedData;
    DWORD SizeOfUninitializedData;
    DWORD AddressOfEntryPoint;
    DWORD BaseOfCode;
    ULONGLONG ImageBase;
    DWORD SectionAlignment;
    DWORD FileAlignment;
    WORD  MajorOperatingSystemVersion;
    WORD  MinorOperatingSystemVersion;
    WORD  MajorImageVersion;
    WORD  MinorImageVersion;
    WORD  MajorSubsystemVersion;
    WORD  MinorSubsystemVersion;
    DWORD Win32VersionValue;
    DWORD SizeOfImage;
    DWORD SizeOfHeaders;
    DWORD CheckSum;
    WORD  Subsystem;
    WORD  DllCharacteristics;
    ULONGLONG SizeOfStackReserve;
    ULONGLONG SizeOfStackCommit;
    ULONGLONG SizeOfHeapReserve;
    ULONGLONG SizeOfHeapCommit;
    DWORD LoaderFlags;
    DWORD NumberOfRvaAndSizes;
    IMAGE_DATA_DIRECTORY DataDirectory[IMAGE_NUMBEROF_DIRECTORY_ENTRIES];
} IMAGE_OPTIONAL_HEADER64, *PIMAGE_OPTIONAL_HEADER64;

// Default to 64-bit
typedef IMAGE_OPTIONAL_HEADER64 IMAGE_OPTIONAL_HEADER;
typedef PIMAGE_OPTIONAL_HEADER64 PIMAGE_OPTIONAL_HEADER;

// NT headers

typedef struct _IMAGE_NT_HEADERS32 {
    DWORD Signature;
    IMAGE_FILE_HEADER FileHeader;
    IMAGE_OPTIONAL_HEADER32 OptionalHeader;
} IMAGE_NT_HEADERS32, *PIMAGE_NT_HEADERS32;

typedef struct _IMAGE_NT_HEADERS64 {
    DWORD Signature;
    IMAGE_FILE_HEADER FileHeader;
    IMAGE_OPTIONAL_HEADER64 OptionalHeader;
} IMAGE_NT_HEADERS64, *PIMAGE_NT_HEADERS64;

typedef IMAGE_NT_HEADERS64 IMAGE_NT_HEADERS;
typedef PIMAGE_NT_HEADERS64 PIMAGE_NT_HEADERS;

// Section header - 40 bytes

#define IMAGE_SIZEOF_SHORT_NAME 8

typedef struct _IMAGE_SECTION_HEADER {
    BYTE  Name[IMAGE_SIZEOF_SHORT_NAME];
    union {
        DWORD PhysicalAddress;
        DWORD VirtualSize;
    } Misc;
    DWORD VirtualAddress;
    DWORD SizeOfRawData;
    DWORD PointerToRawData;
    DWORD PointerToRelocations;
    DWORD PointerToLinenumbers;
    WORD  NumberOfRelocations;
    WORD  NumberOfLinenumbers;
    DWORD Characteristics;
} IMAGE_SECTION_HEADER, *PIMAGE_SECTION_HEADER;

#define IMAGE_FIRST_SECTION(ntheader) \
    ((PIMAGE_SECTION_HEADER)((LPBYTE)&((PIMAGE_NT_HEADERS)(ntheader))->OptionalHeader + \
    ((PIMAGE_NT_HEADERS)(ntheader))->FileHeader.SizeOfOptionalHeader))

// Section characteristic flags (only commonly used ones)

#define IMAGE_SCN_CNT_CODE                 0x00000020
#define IMAGE_SCN_CNT_INITIALIZED_DATA     0x00000040
#define IMAGE_SCN_CNT_UNINITIALIZED_DATA   0x00000080
#define IMAGE_SCN_MEM_DISCARDABLE          0x02000000
#define IMAGE_SCN_MEM_EXECUTE              0x20000000
#define IMAGE_SCN_MEM_READ                 0x40000000
#define IMAGE_SCN_MEM_WRITE                0x80000000

// Data directory indices (only those used by the loader)

#define IMAGE_DIRECTORY_ENTRY_EXPORT    0
#define IMAGE_DIRECTORY_ENTRY_IMPORT    1
#define IMAGE_DIRECTORY_ENTRY_RESOURCE  2
#define IMAGE_DIRECTORY_ENTRY_EXCEPTION 3
#define IMAGE_DIRECTORY_ENTRY_BASERELOC 5
#define IMAGE_DIRECTORY_ENTRY_TLS       9
#define IMAGE_DIRECTORY_ENTRY_IAT       12

// Export directory

typedef struct _IMAGE_EXPORT_DIRECTORY {
    DWORD Characteristics;
    DWORD TimeDateStamp;
    WORD  MajorVersion;
    WORD  MinorVersion;
    DWORD Name;
    DWORD Base;
    DWORD NumberOfFunctions;
    DWORD NumberOfNames;
    DWORD AddressOfFunctions;
    DWORD AddressOfNames;
    DWORD AddressOfNameOrdinals;
} IMAGE_EXPORT_DIRECTORY, *PIMAGE_EXPORT_DIRECTORY;

// Import structures

typedef struct _IMAGE_IMPORT_BY_NAME {
    WORD Hint;
    BYTE Name[1];
} IMAGE_IMPORT_BY_NAME, *PIMAGE_IMPORT_BY_NAME;

typedef struct _IMAGE_THUNK_DATA64 {
    union {
        ULONGLONG ForwarderString;
        ULONGLONG Function;
        ULONGLONG Ordinal;
        ULONGLONG AddressOfData;
    } u1;
} IMAGE_THUNK_DATA64, *PIMAGE_THUNK_DATA64;

typedef struct __attribute__((packed)) _IMAGE_IMPORT_DESCRIPTOR {
    union {
        DWORD Characteristics;
        DWORD OriginalFirstThunk;
    } u;
    DWORD TimeDateStamp;
    DWORD ForwarderChain;
    DWORD Name;
    DWORD FirstThunk;
} IMAGE_IMPORT_DESCRIPTOR, *PIMAGE_IMPORT_DESCRIPTOR;

// Ordinal import helpers

#define IMAGE_ORDINAL_FLAG64        0x8000000000000000UL
#define IMAGE_ORDINAL_FLAG          IMAGE_ORDINAL_FLAG64
#define IMAGE_SNAP_BY_ORDINAL64(o)  (((o) & IMAGE_ORDINAL_FLAG64) != 0)
#define IMAGE_SNAP_BY_ORDINAL(o)    IMAGE_SNAP_BY_ORDINAL64(o)
#define IMAGE_ORDINAL(o)            ((o) & 0xffff)

// Base relocation

typedef struct _IMAGE_BASE_RELOCATION {
    DWORD VirtualAddress;
    DWORD SizeOfBlock;
    WORD  TypeOffset[0];
} IMAGE_BASE_RELOCATION, *PIMAGE_BASE_RELOCATION;

// Relocation types (only those supported by the loader)

#define IMAGE_REL_BASED_ABSOLUTE 0
#define IMAGE_REL_BASED_HIGHLOW  3
#define IMAGE_REL_BASED_DIR64    10

// TLS directory

typedef struct _IMAGE_TLS_DIRECTORY {
    PVOID  RawDataStart;
    PVOID  RawDataEnd;
    PDWORD AddressOfIndex;
    PVOID  AddressOfCallbacks;
    DWORD  SizeOfZeroFill;
    DWORD  Characteristics;
} IMAGE_TLS_DIRECTORY, *PIMAGE_TLS_DIRECTORY;

// ---------------------------------------------------------------------------
// Thread Environment Block (TEB) and Process Environment Block (PEB)
// ---------------------------------------------------------------------------

typedef struct _NT_TIB {
    PVOID ExceptionList;
    PVOID StackBase;
    PVOID StackLimit;
    PVOID SubSystemTib;
    ULONG Version;
    PVOID UserPointer;
    PVOID Self;
} NT_TIB, *PNT_TIB;

typedef struct _CLIENT_ID {
    HANDLE UniqueProcess;
    HANDLE UniqueThread;
} CLIENT_ID;

typedef struct _LIST_ENTRY {
    struct _LIST_ENTRY *Flink;
    struct _LIST_ENTRY *Blink;
} LIST_ENTRY, *PLIST_ENTRY;

typedef struct _PEB_LDR_DATA {
    ULONG    Length;
    BOOLEAN  Initialized;
    PVOID    SsHandle;
    LIST_ENTRY InLoadOrderModuleList;
    LIST_ENTRY InMemoryOrderModuleList;
    LIST_ENTRY InInitializationOrderModuleList;
} PEB_LDR_DATA, *PPEB_LDR_DATA;

typedef struct _PEB {
    BOOLEAN       InheritedAddressSpace;
    BOOLEAN       ReadImageFileExecOptions;
    BOOLEAN       BeingDebugged;
    BOOLEAN       SpareBool;
    HANDLE        Mutant;
    HMODULE       ImageBaseAddress;
    PPEB_LDR_DATA LdrData;
    PVOID         ProcessParameters;
    PVOID         SubSystemData;
    HANDLE        ProcessHeap;
    PVOID         FastPebLock;
    PVOID         FastPebLockRoutine;
    PVOID         FastPebUnlockRoutine;
    ULONG         EnvironmentUpdateCount;
    PVOID         KernelCallbackTable;
    PVOID         EventLogSection;
    PVOID         EventLog;
    PVOID         FreeList;
    ULONG         TlsExpansionCounter;
    PRTL_BITMAP   TlsBitmap;
    ULONG         TlsBitmapBits[2];
    PVOID         ReadOnlySharedMemoryBase;
    PVOID         ReadOnlySharedMemoryHeap;
    PVOID        *ReadOnlyStaticServerData;
    PVOID         AnsiCodePageData;
    PVOID         OemCodePageData;
    PVOID         UnicodeCaseTableData;
    ULONG         NumberOfProcessors;
    ULONG         NtGlobalFlag;
    BYTE          Spare2[4];
    LARGE_INTEGER CriticalSectionTimeout;
    ULONG         HeapSegmentReserve;
    ULONG         HeapSegmentCommit;
    ULONG         HeapDeCommitTotalFreeThreshold;
    ULONG         HeapDeCommitFreeBlockThreshold;
    ULONG         NumberOfHeaps;
    ULONG         MaximumNumberOfHeaps;
    PVOID        *ProcessHeaps;
    PVOID         GdiSharedHandleTable;
    PVOID         ProcessStarterHelper;
    PVOID         GdiDCAttributeList;
    PVOID         LoaderLock;
    ULONG         OSMajorVersion;
    ULONG         OSMinorVersion;
    ULONG         OSBuildNumber;
    ULONG         OSPlatformId;
    ULONG         ImageSubSystem;
    ULONG         ImageSubSystemMajorVersion;
    ULONG         ImageSubSystemMinorVersion;
    ULONG         ImageProcessAffinityMask;
    ULONG         GdiHandleBuffer[34];
    ULONG         PostProcessInitRoutine;
    PRTL_BITMAP   TlsExpansionBitmap;
    ULONG         TlsExpansionBitmapBits[32];
    ULONG         SessionId;
} PEB, *PPEB;

// TEB is deliberately truncated after ProcessEnvironmentBlock.
// Access to fields beyond this point will fault, making missing
// fields obvious during development.
typedef struct _TEB {
    NT_TIB  Tib;
    PVOID   EnvironmentPointer;
    CLIENT_ID Cid;
    PVOID   ActiveRpcInfo;
    PVOID   ThreadLocalStoragePointer;
    PPEB    ProcessEnvironmentBlock;
} TEB, *PTEB;

// Linux LDT entry (used by GS segment setup)

struct user_desc {
    unsigned int  entry_number;
    unsigned long base_addr;
    unsigned int  limit;
    unsigned int  seg_32bit : 1;
    unsigned int  contents : 2;
    unsigned int  read_exec_only : 1;
    unsigned int  limit_in_pages : 1;
    unsigned int  seg_not_present : 1;
    unsigned int  useable : 1;
    unsigned int  lm : 1;
};

// ---------------------------------------------------------------------------
// PE loader internal state
// ---------------------------------------------------------------------------

// Loader's representation of a loaded PE image
struct pe_image {
    const char *name;
    DllEntry_t entry;
    void *image;
    size_t size;
    int type;

    IMAGE_NT_HEADERS *nt_hdr;
    IMAGE_OPTIONAL_HEADER *opt_hdr;
    DWORD tls_index;
    PVOID tls_directory;
    size_t tls_data_size;
    size_t tls_total_size;
};

// RVA (Relative Virtual Address) to VA (Virtual Address) conversion
#define RVA2VA(image, rva, type) ((type)(ULONG_PTR)((char *)(image) + (rva)))

// ---------------------------------------------------------------------------
// PE loader API
// ---------------------------------------------------------------------------

bool pe_load_library(const char *filename, void **image, size_t *size);
int  link_pe_images(struct pe_image *pe_image, unsigned short n);
bool pe_unload_library(struct pe_image &pe);

bool pe_initialize_tls_for_current_thread(struct pe_image *pe, DWORD reason);
void pe_initialize_tls_for_loaded_images(DWORD reason);
void pe_notify_loaded_images(DWORD reason);
void pe_ensure_tls_for_loaded_images();

generic_func get_export(const char *name);
void register_function(const char *dll_name, const char *func_name, generic_func func);

bool setup_nt_threadinfo(PEXCEPTION_HANDLER handler);
bool setup_kuser_shared_data();

extern PKUSER_SHARED_DATA SharedUserData;

#endif // PE_LOADER_H
