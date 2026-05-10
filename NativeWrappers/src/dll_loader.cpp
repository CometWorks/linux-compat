// High-level DLL loader: ties together CPU detection, Win32 shim registration,
// PE loading/linking, TEB setup, TLS initialization, and DllMain invocation.

#include <cstdlib>

#include "dll_loader.h"
#include "support.h"
#include "winlibs.h"
#include "cpu_features.h"

// Top-level SEH handler installed for loaded DLLs.
// Aborts on any unhandled exception from PE code.
static EXCEPTION_DISPOSITION ExceptionHandler(
    _EXCEPTION_RECORD *ExceptionRecord,
    _EXCEPTION_FRAME *EstablisherFrame,
    PVOID *ContextRecord,
    _EXCEPTION_FRAME **DispatcherContext)
{
    LogMessage("Top-level exception handler caught exception");
    abort();
}

bool load_dll(pe_image *image, const char *name)
{
    if (!parseCPUInfo()) {
        LogMessage("Cannot parse CPU info");
        return false;
    }

    register_windows_library_functions();

    image->name = name;
    if (!pe_load_library(image->name, &image->image, &image->size)) {
        LogMessageA("Missing DLL: %s", image->name);
        return false;
    }

    link_pe_images(image, 1);
    setup_nt_threadinfo(&ExceptionHandler);
    pe_initialize_tls_for_current_thread(image, DLL_PROCESS_ATTACH);

    // 0x0d110001 is a sentinel fake HINSTANCE
    return image->entry((PVOID)0x0d110001, DLL_PROCESS_ATTACH, nullptr);
}
