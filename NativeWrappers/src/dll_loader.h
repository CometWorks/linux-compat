// High-level DLL loading API.
// Orchestrates the full sequence: CPU detection, Win32 shim registration,
// PE loading, linking, TEB setup, TLS initialization, and DllMain invocation.

#ifndef DLL_LOADER_H
#define DLL_LOADER_H

#include "pe_loader.h"

bool load_dll(pe_image *image, const char *name);

#endif // DLL_LOADER_H
