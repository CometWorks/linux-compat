// PE linker: loads, relocates, and links Windows PE (DLL) images on Linux.
//
// Two-pass linking:
//   Pass 1: Parse headers, expand sections to virtual layout, register exports.
//   Pass 2: Apply base relocations, resolve imports, set up TLS, resolve entry point.

#include <asm/prctl.h>
#include <asm/unistd.h>
#include <cerrno>
#include <err.h>
#include <climits>
#include <csignal>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <fcntl.h>
#include <mutex>
#include <pthread.h>
#include <sys/mman.h>
#include <sys/stat.h>
#include <sys/types.h>
#include <unistd.h>

#include "pe_loader.h"
#include "support.h"

// ---------------------------------------------------------------------------
// Export table
// ---------------------------------------------------------------------------

struct pe_export {
    const char *dll;
    const char *name;
    generic_func addr;
};

static constexpr int MAX_EXPORTS = 4096;
static pe_export pe_export_list[MAX_EXPORTS];
static int num_pe_exports = 0;

void register_function(const char *dll_name, const char *func_name, generic_func func)
{
    if (num_pe_exports >= MAX_EXPORTS) {
        LogMessage("Too many exports");
        return;
    }
    pe_export_list[num_pe_exports].dll = dll_name;
    pe_export_list[num_pe_exports].name = func_name;
    pe_export_list[num_pe_exports].addr = func;
    num_pe_exports++;
}

generic_func get_export(const char *name)
{
    for (int i = 0; i < num_pe_exports; i++) {
        if (strcmp(pe_export_list[i].name, name) == 0) {
            return pe_export_list[i].addr;
        }
    }
    return nullptr;
}

// ---------------------------------------------------------------------------
// Shared kernel data
// ---------------------------------------------------------------------------

PKUSER_SHARED_DATA SharedUserData;

// ---------------------------------------------------------------------------
// FLS callback array (used by winlibs.cpp)
// ---------------------------------------------------------------------------

PFLS_CALLBACK_FUNCTION FlsCallbacks[1024] = {nullptr};

// ---------------------------------------------------------------------------
// TLS bitmap and loaded image tracking
// ---------------------------------------------------------------------------

static ULONG TlsBitmapData[32];
static RTL_BITMAP TlsBitmap = {
    .SizeOfBitMap = sizeof(TlsBitmapData) * CHAR_BIT,
    .Buffer = (LPBYTE)&TlsBitmapData[0],
};

static constexpr size_t MAX_LOADED_IMAGES = 16;
static pe_image *g_loaded_images[MAX_LOADED_IMAGES] = {};
static size_t g_loaded_image_count = 0;
static std::mutex g_loaded_images_mutex;

DWORD WINAPI TlsAlloc();

uintptr_t InitialLocalStorage[1024] = {0};

// ---------------------------------------------------------------------------
// Per-thread context (TEB + TLS storage)
// ---------------------------------------------------------------------------

struct nt_thread_context {
    EXCEPTION_FRAME exception_frame;
    TEB thread_environment;
    uintptr_t local_storage[1024];
};

static pthread_key_t g_nt_thread_context_key;
static pthread_once_t g_nt_thread_context_key_once = PTHREAD_ONCE_INIT;

static void make_nt_thread_context_key()
{
    pthread_key_create(&g_nt_thread_context_key, nullptr);
}

static nt_thread_context *get_nt_thread_context()
{
    pthread_once(&g_nt_thread_context_key_once, make_nt_thread_context_key);

    auto *ctx = static_cast<nt_thread_context *>(
        pthread_getspecific(g_nt_thread_context_key));
    if (!ctx) {
        ctx = static_cast<nt_thread_context *>(
            std::calloc(1, sizeof(nt_thread_context)));
        if (!ctx)
            return nullptr;

        std::memcpy(ctx->local_storage, InitialLocalStorage, sizeof(ctx->local_storage));
        ctx->thread_environment.Tib.Self = &ctx->thread_environment.Tib;
        ctx->thread_environment.ThreadLocalStoragePointer = ctx->local_storage;
        pthread_setspecific(g_nt_thread_context_key, ctx);
    }
    return ctx;
}

// ---------------------------------------------------------------------------
// TLS support
// ---------------------------------------------------------------------------

static void run_tls_callbacks(pe_image *pe, DWORD reason)
{
    if (!pe || !pe->tls_directory)
        return;

    auto *tls = static_cast<PIMAGE_TLS_DIRECTORY>(pe->tls_directory);
    if (!tls->AddressOfCallbacks)
        return;

    auto *callbacks = reinterpret_cast<DllEntry_t *>(tls->AddressOfCallbacks);
    for (; *callbacks; ++callbacks)
        (*callbacks)(pe->image, reason, nullptr);
}

bool pe_initialize_tls_for_current_thread(struct pe_image *pe, DWORD reason)
{
    if (!pe || !pe->tls_directory)
        return true;

    auto *ctx = get_nt_thread_context();
    if (!ctx)
        return false;

    constexpr size_t MAX_TLS_SLOTS = sizeof(ctx->local_storage) / sizeof(ctx->local_storage[0]);
    if (pe->tls_index >= MAX_TLS_SLOTS) {
        LogMessageA("TLS index out of range for %s", pe->name ? pe->name : "<unnamed>");
        return false;
    }

    bool newly_initialized = false;
    if (!ctx->local_storage[pe->tls_index]) {
        auto *tls_block = static_cast<uint8_t *>(std::calloc(1, pe->tls_total_size));
        if (!tls_block) {
            LogMessageA("Failed to allocate TLS block for %s", pe->name ? pe->name : "<unnamed>");
            return false;
        }

        auto *tls = static_cast<PIMAGE_TLS_DIRECTORY>(pe->tls_directory);
        if (pe->tls_data_size > 0 && tls->RawDataStart)
            std::memcpy(tls_block, tls->RawDataStart, pe->tls_data_size);

        ctx->local_storage[pe->tls_index] = reinterpret_cast<uintptr_t>(tls_block);
        newly_initialized = true;
    }

    if (newly_initialized && (reason == DLL_PROCESS_ATTACH || reason == DLL_THREAD_ATTACH))
        run_tls_callbacks(pe, reason);

    return true;
}

void pe_initialize_tls_for_loaded_images(DWORD reason)
{
    std::lock_guard<std::mutex> lock(g_loaded_images_mutex);
    for (size_t i = 0; i < g_loaded_image_count; ++i)
        pe_initialize_tls_for_current_thread(g_loaded_images[i], reason);
}

void pe_notify_loaded_images(DWORD reason)
{
    std::lock_guard<std::mutex> lock(g_loaded_images_mutex);
    for (size_t i = 0; i < g_loaded_image_count; ++i) {
        auto *pe = g_loaded_images[i];
        if (!pe)
            continue;
        if (reason == DLL_THREAD_ATTACH || reason == DLL_PROCESS_ATTACH) {
            if (!pe_initialize_tls_for_current_thread(pe, reason))
                continue;
        }
        if (pe->entry)
            pe->entry(pe->image, reason, nullptr);
    }
}

void pe_ensure_tls_for_loaded_images()
{
    std::lock_guard<std::mutex> lock(g_loaded_images_mutex);
    for (size_t i = 0; i < g_loaded_image_count; ++i)
        pe_initialize_tls_for_current_thread(g_loaded_images[i], DLL_THREAD_ATTACH);
}

// ---------------------------------------------------------------------------
// PE header validation
// ---------------------------------------------------------------------------

static int check_nt_hdr(IMAGE_NT_HEADERS *nt_hdr)
{
    if (nt_hdr->Signature != IMAGE_NT_SIGNATURE) {
        LogMessageA("Bad PE signature: %08x", nt_hdr->Signature);
        return -EINVAL;
    }

    auto *opt_hdr = &nt_hdr->OptionalHeader;

    if (opt_hdr->Magic != IMAGE_NT_OPTIONAL_HDR32_MAGIC &&
        opt_hdr->Magic != IMAGE_NT_OPTIONAL_HDR64_MAGIC) {
        LogMessageA("Bad optional header magic: %04X", opt_hdr->Magic);
        return -EINVAL;
    }

    if (nt_hdr->FileHeader.Machine != IMAGE_FILE_MACHINE_I386 &&
        nt_hdr->FileHeader.Machine != IMAGE_FILE_MACHINE_AMD64) {
        LogMessageA("Unsupported machine type: %04X", nt_hdr->FileHeader.Machine);
        return -EINVAL;
    }

    if (!(nt_hdr->FileHeader.Characteristics & IMAGE_FILE_EXECUTABLE_IMAGE))
        return -EINVAL;

    if (nt_hdr->FileHeader.Characteristics & IMAGE_FILE_RELOCS_STRIPPED)
        return -EINVAL;

    if (nt_hdr->FileHeader.NumberOfSections == 0)
        return -EINVAL;

    if (opt_hdr->SectionAlignment < opt_hdr->FileAlignment) {
        LogMessageA("Alignment mismatch: section: 0x%x, file: 0x%x",
                    opt_hdr->SectionAlignment, opt_hdr->FileAlignment);
        return -EINVAL;
    }

    if (nt_hdr->FileHeader.Characteristics & IMAGE_FILE_EXECUTABLE_IMAGE)
        return IMAGE_FILE_EXECUTABLE_IMAGE;
    if (nt_hdr->FileHeader.Characteristics & IMAGE_FILE_DLL)
        return IMAGE_FILE_DLL;
    return -EINVAL;
}

// ---------------------------------------------------------------------------
// Import resolution stubs
// ---------------------------------------------------------------------------

static void ordinal_import_stub()
{
    warnx("function at %p attempted to call a symbol imported by ordinal",
          __builtin_return_address(0));
    raise(SIGTRAP);
}

static void unknown_symbol_stub()
{
    warnx("function at %p attempted to call an unknown symbol",
          __builtin_return_address(0));
    raise(SIGTRAP);
}

// ---------------------------------------------------------------------------
// WS2_32.dll ordinal resolution
// ---------------------------------------------------------------------------

static const char *resolve_ws2_32_ordinal(unsigned long ordinal)
{
    switch (ordinal) {
    case 1:   return "accept";
    case 2:   return "bind";
    case 3:   return "closesocket";
    case 4:   return "connect";
    case 6:   return "getsockname";
    case 9:   return "htons";
    case 10:  return "ioctlsocket";
    case 13:  return "listen";
    case 15:  return "ntohs";
    case 16:  return "recv";
    case 18:  return "select";
    case 19:  return "send";
    case 21:  return "setsockopt";
    case 57:  return "gethostname";
    case 111: return "WSAGetLastError";
    case 115: return "WSAStartup";
    case 151: return "__WSAFDIsSet";
    default:  return nullptr;
    }
}

static generic_func resolve_ordinal_import(const char *dll, unsigned long ordinal)
{
    if (strcasecmp(dll, "WS2_32.dll") == 0) {
        if (const char *name = resolve_ws2_32_ordinal(ordinal))
            return get_export(name);
    }
    return nullptr;
}

// ---------------------------------------------------------------------------
// Import processing
// ---------------------------------------------------------------------------

static int process_import_descriptor(void *image, IMAGE_IMPORT_DESCRIPTOR *dirent, char *dll)
{
    auto *lookup_tbl = RVA2VA(image, dirent->u.OriginalFirstThunk, ULONG_PTR *);
    auto *address_tbl = RVA2VA(image, dirent->FirstThunk, ULONG_PTR *);

    for (int i = 0; lookup_tbl[i]; i++) {
        if (IMAGE_SNAP_BY_ORDINAL(lookup_tbl[i])) {
            auto ordinal = IMAGE_ORDINAL(lookup_tbl[i]);
            auto adr = resolve_ordinal_import(dll, ordinal);
            if (!adr) {
                LogMessageA("Ordinal import not supported: %s:#%lu", dll, ordinal);
                address_tbl[i] = reinterpret_cast<ULONG_PTR>(ordinal_import_stub);
                continue;
            }
            address_tbl[i] = reinterpret_cast<ULONG_PTR>(adr);
        } else {
            auto *symname = RVA2VA(image, (lookup_tbl[i] & ~IMAGE_ORDINAL_FLAG) + 2, char *);
            auto adr = get_export(symname);
            if (!adr) {
                LogMessageA("Unknown symbol: %s:%s", dll, symname);
                address_tbl[i] = reinterpret_cast<ULONG_PTR>(unknown_symbol_stub);
                continue;
            }
            address_tbl[i] = reinterpret_cast<ULONG_PTR>(adr);
        }
    }
    return 0;
}

// ---------------------------------------------------------------------------
// Export reading
// ---------------------------------------------------------------------------

static int read_exports(struct pe_image *pe)
{
    auto *opt_hdr = &pe->nt_hdr->OptionalHeader;
    auto *export_data_dir = &opt_hdr->DataDirectory[IMAGE_DIRECTORY_ENTRY_EXPORT];

    if (export_data_dir->Size == 0)
        return 0;

    auto *export_dir = RVA2VA(pe->image, export_data_dir->VirtualAddress,
                              IMAGE_EXPORT_DIRECTORY *);

    auto *name_table = (uint32_t *)((char *)pe->image + export_dir->AddressOfNames);
    auto *ordinal_table = (uint16_t *)((char *)pe->image + export_dir->AddressOfNameOrdinals);
    auto *func_table = (uint32_t *)((char *)pe->image + export_dir->AddressOfFunctions);

    for (DWORD i = 0; i < export_dir->NumberOfNames; i++) {
        uint32_t address = func_table[ordinal_table[i]];

        if (num_pe_exports >= MAX_EXPORTS) {
            LogMessage("Too many exports");
            break;
        }

        register_function(
            pe->name,
            (char *)pe->image + name_table[i],
            (generic_func)((char *)pe->image + address));
    }
    return 0;
}

// ---------------------------------------------------------------------------
// Import fixup
// ---------------------------------------------------------------------------

static int fixup_imports(void *image, IMAGE_NT_HEADERS *nt_hdr)
{
    auto *opt_hdr = &nt_hdr->OptionalHeader;
    auto *import_data_dir = &opt_hdr->DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT];
    auto *dirent = RVA2VA(image, import_data_dir->VirtualAddress, IMAGE_IMPORT_DESCRIPTOR *);

    int ret = 0;
    for (int i = 0; dirent[i].Name; i++) {
        char *name = RVA2VA(image, dirent[i].Name, char *);
        ret += process_import_descriptor(image, &dirent[i], name);
    }
    return ret;
}

// ---------------------------------------------------------------------------
// Base relocation fixup
// ---------------------------------------------------------------------------

static int fixup_reloc(void *image, IMAGE_NT_HEADERS *nt_hdr)
{
    auto *opt_hdr = &nt_hdr->OptionalHeader;
    ULONG_PTR base = opt_hdr->ImageBase;
    auto *base_reloc_dir = &opt_hdr->DataDirectory[IMAGE_DIRECTORY_ENTRY_BASERELOC];

    if (base_reloc_dir->Size == 0)
        return 0;

    auto *block = RVA2VA(image, base_reloc_dir->VirtualAddress, IMAGE_BASE_RELOCATION *);

    while (block->SizeOfBlock) {
        ULONG count = (block->SizeOfBlock - sizeof(IMAGE_BASE_RELOCATION)) / sizeof(WORD);

        for (ULONG i = 0; i < count; i++) {
            WORD fixup = block->TypeOffset[i];
            WORD offset = fixup & 0xfff;
            int type = (fixup >> 12) & 0x0f;

            switch (type) {
            case IMAGE_REL_BASED_ABSOLUTE:
                break;

            case IMAGE_REL_BASED_HIGHLOW: {
                auto *loc = RVA2VA(image, block->VirtualAddress + offset, uint32_t *);
                *loc = RVA2VA(image, (*loc - base), uint32_t);
                break;
            }

            case IMAGE_REL_BASED_DIR64: {
                auto *loc = RVA2VA(image, block->VirtualAddress + offset, uint64_t *);
                *loc = RVA2VA(image, (*loc - base), uint64_t);
                break;
            }

            default:
                LogMessageA("Unknown relocation type: %d", type);
                return -EOPNOTSUPP;
            }
        }

        block = (IMAGE_BASE_RELOCATION *)((char *)block + block->SizeOfBlock);
    }
    return 0;
}

// ---------------------------------------------------------------------------
// Image expansion (file layout -> virtual layout)
// ---------------------------------------------------------------------------

static int fix_pe_image(pe_image *pe)
{
    if (pe->size == pe->opt_hdr->SizeOfImage)
        return 0;

    DWORD image_size = pe->opt_hdr->SizeOfImage;

    void *image = mmap((PVOID)(pe->opt_hdr->ImageBase),
                       image_size + getpagesize(),
                       PROT_READ | PROT_WRITE | PROT_EXEC,
                       MAP_ANONYMOUS | MAP_PRIVATE,
                       -1, 0);

    if (image == MAP_FAILED) {
        LogMessageA("Failed to mmap image: %u bytes at base %lx",
                    image_size, pe->opt_hdr->ImageBase);
        return -ENOMEM;
    }

    memset(image, 0, image_size);

    int sections = pe->nt_hdr->FileHeader.NumberOfSections;
    auto *sect_hdr = IMAGE_FIRST_SECTION(pe->nt_hdr);

    // Copy headers (everything before the first section)
    memcpy(image, pe->image, sect_hdr->PointerToRawData);

    // Copy each section to its virtual address
    for (int i = 0; i < sections; i++) {
        if (sect_hdr->VirtualAddress + sect_hdr->SizeOfRawData > image_size) {
            LogMessageA("Invalid section %s", (const char *)sect_hdr->Name);
            munmap(image, image_size + getpagesize());
            return -EINVAL;
        }
        memcpy((char *)image + sect_hdr->VirtualAddress,
               (char *)pe->image + sect_hdr->PointerToRawData,
               sect_hdr->SizeOfRawData);
        sect_hdr++;
    }

    munmap(pe->image, pe->size);
    pe->image = image;
    pe->size = image_size;

    // Update internal pointers
    pe->nt_hdr = (IMAGE_NT_HEADERS *)
        ((char *)pe->image + ((IMAGE_DOS_HEADER *)pe->image)->e_lfanew);
    pe->opt_hdr = &pe->nt_hdr->OptionalHeader;

    return 0;
}

// ---------------------------------------------------------------------------
// Two-pass PE linker
// ---------------------------------------------------------------------------

int link_pe_images(pe_image *pe_image, unsigned short n)
{
    // Pass 1: Parse, expand, register exports
    for (int i = 0; i < n; i++) {
        auto *pe = &pe_image[i];
        auto *dos_hdr = (IMAGE_DOS_HEADER *)pe->image;

        if (pe->size < sizeof(IMAGE_DOS_HEADER)) {
            LogMessageA("Image too small: %ld", pe->size);
            return -EINVAL;
        }

        pe->nt_hdr = (IMAGE_NT_HEADERS *)((char *)pe->image + dos_hdr->e_lfanew);
        pe->opt_hdr = &pe->nt_hdr->OptionalHeader;

        pe->type = check_nt_hdr(pe->nt_hdr);
        if (pe->type <= 0) {
            LogMessage("Invalid PE header");
            return -EINVAL;
        }

        if (fix_pe_image(pe)) {
            LogMessage("Failed to expand PE image");
            return -EINVAL;
        }

        if (read_exports(pe)) {
            LogMessage("Failed to read exports");
            return -EINVAL;
        }
    }

    // Pass 2: Relocate, resolve imports, set up TLS
    for (int i = 0; i < n; i++) {
        auto *pe = &pe_image[i];

        if (fixup_reloc(pe->image, pe->nt_hdr)) {
            LogMessage("Failed to apply relocations");
            return -EINVAL;
        }

        if (fixup_imports(pe->image, pe->nt_hdr)) {
            LogMessage("Failed to resolve imports");
            return -EINVAL;
        }

        pe->entry = RVA2VA(pe->image, pe->opt_hdr->AddressOfEntryPoint, DllEntry_t);

        // Set up TLS if present
        if (pe->opt_hdr->NumberOfRvaAndSizes > IMAGE_DIRECTORY_ENTRY_TLS &&
            pe->opt_hdr->DataDirectory[IMAGE_DIRECTORY_ENTRY_TLS].VirtualAddress != 0) {

            auto *tls_data = RVA2VA(pe->image,
                pe->opt_hdr->DataDirectory[IMAGE_DIRECTORY_ENTRY_TLS].VirtualAddress,
                IMAGE_TLS_DIRECTORY *);

            pe->tls_directory = tls_data;
            pe->tls_data_size = reinterpret_cast<uintptr_t>(tls_data->RawDataEnd)
                              - reinterpret_cast<uintptr_t>(tls_data->RawDataStart);
            pe->tls_total_size = pe->tls_data_size + tls_data->SizeOfZeroFill;

            pe->tls_index = TlsAlloc();
            if (pe->tls_index == 0xffffffffu) {
                LogMessageA("Failed to allocate TLS index for %s",
                            pe->name ? pe->name : "<unnamed>");
                return -EINVAL;
            }

            if (tls_data->AddressOfIndex)
                *tls_data->AddressOfIndex = pe->tls_index;

            std::lock_guard<std::mutex> lock(g_loaded_images_mutex);
            if (g_loaded_image_count < MAX_LOADED_IMAGES) {
                g_loaded_images[g_loaded_image_count++] = pe;
            } else {
                LogMessage("Too many loaded images with TLS");
                return -EINVAL;
            }
        }
    }

    return 0;
}

// ---------------------------------------------------------------------------
// Library loading / unloading
// ---------------------------------------------------------------------------

bool pe_load_library(const char *filename, void **image, size_t *size)
{
    *image = MAP_FAILED;
    *size = 0;

    int fd = open(filename, O_RDONLY);
    if (fd < 0) {
        LogMessageA("Failed to open PE library: %s", filename);
        return false;
    }

    struct stat st;
    if (fstat(fd, &st) < 0) {
        LogMessageA("Failed to stat PE library: %s", filename);
        close(fd);
        return false;
    }

    *size = st.st_size;
    *image = mmap(nullptr, *size, PROT_READ, MAP_SHARED, fd, 0);
    close(fd);

    if (*image == MAP_FAILED) {
        LogMessageA("Failed to mmap PE library: %s", filename);
        return false;
    }

    setup_nt_threadinfo(nullptr);
    setup_kuser_shared_data();

    return true;
}

bool pe_unload_library(pe_image &pe)
{
    num_pe_exports = 0;
    munmap(pe.image, pe.size);
    return true;
}

// ---------------------------------------------------------------------------
// Thread environment setup
// ---------------------------------------------------------------------------

bool setup_nt_threadinfo(PEXCEPTION_HANDLER handler)
{
    static PEB ProcessEnvironmentBlock = {
        .TlsBitmap = &TlsBitmap,
    };

    auto *ctx = get_nt_thread_context();
    if (!ctx) {
        LogMessage("Failed to allocate nt_thread_context");
        return false;
    }

    auto &teb = ctx->thread_environment;
    teb.ProcessEnvironmentBlock = &ProcessEnvironmentBlock;
    ctx->local_storage[0] = InitialLocalStorage[0];

    // Initialize stack bounds if not already set
    if (!teb.Tib.StackBase || !teb.Tib.StackLimit) {
        pthread_attr_t attr;
        if (pthread_getattr_np(pthread_self(), &attr) == 0) {
            void *stack_addr = nullptr;
            size_t stack_size = 0;
            if (pthread_attr_getstack(&attr, &stack_addr, &stack_size) == 0) {
                teb.Tib.StackLimit = stack_addr;
                teb.Tib.StackBase = static_cast<char *>(stack_addr) + stack_size;
            }
        } else {
            // Fallback: estimate 8 MB stack around current position
            uintptr_t stack_marker = 0;
            teb.Tib.StackBase = reinterpret_cast<void *>(
                reinterpret_cast<uintptr_t>(&stack_marker) + (8u << 20));
            teb.Tib.StackLimit = reinterpret_cast<void *>(
                reinterpret_cast<uintptr_t>(&stack_marker) - (8u << 20));
        }
    }

    // Install SEH handler if provided
    if (handler) {
        ctx->exception_frame.handler = handler;
        ctx->exception_frame.prev = nullptr;
        teb.Tib.ExceptionList = &ctx->exception_frame;
    }

    // Set GS base to point at our TEB so Windows gs:[offset] accesses work
    long result = syscall(__NR_arch_prctl, ARCH_SET_GS, &teb);
    if (result != 0) {
        LogMessageA("Failed to set GS base (ARCH_SET_GS). Error: %d", errno);
        return false;
    }

    return true;
}

bool setup_kuser_shared_data()
{
    SharedUserData = (PKUSER_SHARED_DATA)mmap(
        (PVOID)(MM_SHARED_USER_DATA_VA),
        sizeof(KUSER_SHARED_DATA),
        PROT_READ | PROT_WRITE,
        MAP_PRIVATE | MAP_ANONYMOUS | MAP_FIXED,
        -1, 0);

    if (SharedUserData == MAP_FAILED) {
        LogMessage("Failed to map KUSER_SHARED_DATA");
        return false;
    }
    return true;
}
