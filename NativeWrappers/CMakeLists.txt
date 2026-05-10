cmake_minimum_required(VERSION 3.10)

project(DllLoader VERSION 1.0 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# Define source files
set(SOURCES
        src/support.cpp
        src/pe_loader.cpp
        src/winlibs.cpp
        src/cpu_features.cpp
        src/dll_loader.cpp
)

# Include directories
include_directories(src)

file(GLOB HAVOK_THUNK_SOURCES "src/HavokThunk_*.cpp")

# Add executable
add_executable(
        ${PROJECT_NAME}
        ${SOURCES}
        src/main.cpp
)

set_target_properties(${PROJECT_NAME} PROPERTIES
        RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}"
)

# Wrapper libraries
add_library(
        RecastDetour
        SHARED
        ${SOURCES}
        src/RecastDetour.cpp
)

add_library(
        Havok
        SHARED
        ${SOURCES}
        src/Havok.cpp
        ${HAVOK_THUNK_SOURCES}
)

set_target_properties(RecastDetour PROPERTIES
        LIBRARY_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}"
)

add_library(
        VRageNative
        SHARED
        ${SOURCES}
        src/VRageNative.cpp
)

set_target_properties(Havok PROPERTIES
        LIBRARY_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}"
)

set_target_properties(VRageNative PROPERTIES
        LIBRARY_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}"
)

add_library(
        D3DCompiler
        SHARED
        ${SOURCES}
        src/D3DCompiler.cpp
)

set_target_properties(D3DCompiler PROPERTIES
        LIBRARY_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}"
)

# Link libraries (if any)
target_link_libraries(${PROJECT_NAME} PRIVATE c)
target_link_libraries(RecastDetour PRIVATE c)
target_link_libraries(Havok PRIVATE c)
target_link_libraries(VRageNative PRIVATE c)
target_link_libraries(D3DCompiler PRIVATE c)

# Compiler flags
#-Werror -Wextra
target_compile_options(${PROJECT_NAME} PRIVATE -Wall -fpermissive)
target_compile_options(RecastDetour PRIVATE -Wall -fpermissive -fPIC)
target_compile_options(Havok PRIVATE -Wall -fpermissive -fPIC)
target_compile_options(VRageNative PRIVATE -Wall -fpermissive -fPIC)
target_compile_options(D3DCompiler PRIVATE -Wall -fpermissive -fPIC)

# Install target (optional)
#install(TARGETS ${PROJECT_NAME} DESTINATION bin)
