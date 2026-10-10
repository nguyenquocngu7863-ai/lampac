/*
 * ca-redirect.c — LD_PRELOAD companion for Termux glibc processes.
 *
 * Android has no /etc/ssl/certs/ca-certificates.crt, so GnuTLS-based stacks
 * (glib-networking -> libsoup -> GStreamer souphttpsrc, gst-discoverer)
 * fail TLS verification with "zero trusted certificates".
 *
 * This interposes open/openat (and the 64-bit variants) and redirects exactly
 * one absolute path — the GnuTLS system trust bundle — to a CA bundle
 * borrowed read-only from the Ubuntu proot rootfs (the "sysroot").
 *
 * Linked together with libseccomp-shim.c into a single libseccomp-shim.so.
 * Only the exact path is redirected; everything else passes through.
 * If the target bundle is absent, the original path is used (normal ENOENT).
 */
#define _GNU_SOURCE
#include <dlfcn.h>
#include <fcntl.h>
#include <stdarg.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/stat.h>
#include <unistd.h>

static const char CA_FROM[] = "/etc/ssl/certs/ca-certificates.crt";
/* sysroot CA bundle (override with CA_BUNDLE_PATH env) */
static const char CA_DEFAULT[] =
    "/data/data/com.termux/files/usr/var/lib/proot-distro/containers/ubuntu/rootfs/etc/ssl/certs/ca-certificates.crt";

static const char *
ca_target(void)
{
    static const char *cached = 0;
    static int done = 0;

    if (!done) {
        done = 1;
        const char *env = getenv("CA_BUNDLE_PATH");
        const char *cand = (env && env[0]) ? env : CA_DEFAULT;
        if (access(cand, R_OK) == 0)
            cached = cand;
    }
    return cached;
}

static const char *
redir_path(const char *path)
{
    const char *to;

    if (!path || path[0] != '/')
        return path;
    if (strcmp(path, CA_FROM) != 0)
        return path;
    to = ca_target();
    return to ? to : path;
}

typedef int (*open_fn_t)(const char *, int, ...);
typedef int (*openat_fn_t)(int, const char *, int, ...);

int
open(const char *path, int flags, ...)
{
    static open_fn_t real_open = 0;
    va_list ap;
    mode_t mode;

    if (!real_open)
        real_open = (open_fn_t)dlsym(RTLD_NEXT, "open");
    path = redir_path(path);
    va_start(ap, flags);
    mode = va_arg(ap, mode_t);
    va_end(ap);
    return real_open(path, flags, mode);
}

int
open64(const char *path, int flags, ...)
{
    static open_fn_t real_open64 = 0;
    va_list ap;
    mode_t mode;

    if (!real_open64)
        real_open64 = (open_fn_t)dlsym(RTLD_NEXT, "open64");
    path = redir_path(path);
    va_start(ap, flags);
    mode = va_arg(ap, mode_t);
    va_end(ap);
    return real_open64(path, flags, mode);
}

int
openat(int dirfd, const char *path, int flags, ...)
{
    static openat_fn_t real_openat = 0;
    va_list ap;
    mode_t mode;

    if (!real_openat)
        real_openat = (openat_fn_t)dlsym(RTLD_NEXT, "openat");
    if (dirfd == AT_FDCWD)
        path = redir_path(path);
    va_start(ap, flags);
    mode = va_arg(ap, mode_t);
    va_end(ap);
    return real_openat(dirfd, path, flags, mode);
}

int
openat64(int dirfd, const char *path, int flags, ...)
{
    static openat_fn_t real_openat64 = 0;
    va_list ap;
    mode_t mode;

    if (!real_openat64)
        real_openat64 = (openat_fn_t)dlsym(RTLD_NEXT, "openat64");
    if (dirfd == AT_FDCWD)
        path = redir_path(path);
    va_start(ap, flags);
    mode = va_arg(ap, mode_t);
    va_end(ap);
    return real_openat64(dirfd, path, flags, mode);
}

/*
 * Fortified variants: Termux builds with _FORTIFY_SOURCE, so open() with
 * compile-time-known flags becomes __open_2() (and friends). Without these,
 * fortified binaries bypass the interposition entirely.
 */
int
__open_2(const char *path, int flags)
{
    static open_fn_t real = 0;

    if (!real)
        real = (open_fn_t)dlsym(RTLD_NEXT, "__open_2");
    return real(redir_path(path), flags, 0);
}

int
__open64_2(const char *path, int flags)
{
    static open_fn_t real = 0;

    if (!real)
        real = (open_fn_t)dlsym(RTLD_NEXT, "__open64_2");
    return real(redir_path(path), flags, 0);
}

int
__openat_2(int dirfd, const char *path, int flags)
{
    static openat_fn_t real = 0;

    if (!real)
        real = (openat_fn_t)dlsym(RTLD_NEXT, "__openat_2");
    if (dirfd == AT_FDCWD)
        path = redir_path(path);
    return real(dirfd, path, flags, 0);
}

int
__openat64_2(int dirfd, const char *path, int flags)
{
    static openat_fn_t real = 0;

    if (!real)
        real = (openat_fn_t)dlsym(RTLD_NEXT, "__openat64_2");
    if (dirfd == AT_FDCWD)
        path = redir_path(path);
    return real(dirfd, path, flags, 0);
}

/*
 * stdio-level variants: some libraries (GnuTLS) open the bundle with
 * fopen(), whose internal open() bypasses PLT interposition.
 */
typedef FILE *(*fopen_fn_t)(const char *, const char *);

FILE *
fopen(const char *path, const char *mode)
{
    static fopen_fn_t real_fopen = 0;

    if (!real_fopen)
        real_fopen = (fopen_fn_t)dlsym(RTLD_NEXT, "fopen");
    return real_fopen(redir_path(path), mode);
}

FILE *
fopen64(const char *path, const char *mode)
{
    static fopen_fn_t real_fopen64 = 0;

    if (!real_fopen64)
        real_fopen64 = (fopen_fn_t)dlsym(RTLD_NEXT, "fopen64");
    return real_fopen64(redir_path(path), mode);
}

typedef int (*access_fn_t)(const char *, int);

int
access(const char *path, int mode)
{
    static access_fn_t real_access = 0;

    if (!real_access)
        real_access = (access_fn_t)dlsym(RTLD_NEXT, "access");
    return real_access(redir_path(path), mode);
}
