#ifndef SUPPORT_H
#define SUPPORT_H

#include <stdio.h>
#include <stdint.h>

#define LogMessage(fmt) fprintf(stdout, fmt "\n"); fflush(stderr)
#define LogMessageA(fmt, ...) fprintf(stdout, fmt "\n", __VA_ARGS__); fflush(stderr)

#endif //SUPPORT_H
