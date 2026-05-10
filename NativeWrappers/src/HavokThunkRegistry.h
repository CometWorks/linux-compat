#pragma once

#include <initializer_list>

struct callback_owner_binding {
    void (*release_fn)(void *target_ptr);
    void *target_ptr;
};

void register_callback_owner(void *owner, std::initializer_list<callback_owner_binding> bindings);
void release_callback_owner(void *owner);

void *bridge_bool_ptr_ptr(void *target_ptr);
void release_bool_ptr_ptr(void *target_ptr);
void *bridge_float_ptr(void *target_ptr);
void release_float_ptr(void *target_ptr);
void *bridge_int_ptr_ptr_uint_ptr(void *target_ptr);
void release_int_ptr_ptr_uint_ptr(void *target_ptr);
void *bridge_void_charptr(void *target_ptr);
void release_void_charptr(void *target_ptr);
void *bridge_void_charptr_int(void *target_ptr);
void release_void_charptr_int(void *target_ptr);
void *bridge_void_i64(void *target_ptr);
void release_void_i64(void *target_ptr);
void *bridge_void_ptr(void *target_ptr);
void release_void_ptr(void *target_ptr);
void *bridge_void_ptr_int(void *target_ptr);
void release_void_ptr_int(void *target_ptr);
void *bridge_void_ptr_int_ptr(void *target_ptr);
void release_void_ptr_int_ptr(void *target_ptr);
void *bridge_void_ptr_ptr(void *target_ptr);
void release_void_ptr_ptr(void *target_ptr);
void *bridge_void_void(void *target_ptr);
void release_void_void(void *target_ptr);
