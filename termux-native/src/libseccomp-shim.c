/*
 * libseccomp-shim.so — LD_PRELOAD shim for Termux glibc programs on Android
 * where the app-sandbox seccomp policy TRAPs syscalls missing from the
 * whitelist (e.g. statx on Android 10, SDK 29).
 *
 * The SIGSYS handler emulates statx via fstatat and reports any other trapped
 * syscall as ENOSYS (standard "syscall not provided" semantics) so callers
 * fall back gracefully. On aarch64 the seccomp trap frame already has PC past
 * the svc instruction, so only x0 needs to be set.
 */
#define _GNU_SOURCE
#include <signal.h>
#include <ucontext.h>
#include <sys/syscall.h>
#include <sys/stat.h>
#include <sys/sysmacros.h>
#include <fcntl.h>
#include <errno.h>
#include <stdint.h>
#include <string.h>
#include <unistd.h>

#ifndef SYS_statx
#define SYS_statx 291
#endif
#ifndef SYS_pidfd_open
#define SYS_pidfd_open 434
#endif

/* statx flags we must not pass through to fstatat */
#define AT_STATX_SYNC_TYPE 0x6000
#define AT_STATX_SYNC_AS_STAT 0x0000
#define AT_STATX_SYNC_FORCE_SYNC 0x2000
#define AT_STATX_SYNC_DONT_SYNC 0x4000

/* bionic does not export __errno_location (it has __errno instead), so it is
 * referenced weakly: in glibc processes it resolves normally, in bionic
 * processes it stays NULL and we fall back to a generic errno. */
extern int *__errno_location(void) __attribute__((weak));

static int
shim_errno(void)
{
    if (__errno_location)
        return *__errno_location();
    return EPERM;
}

static void
re_raise(void)
{
    signal(SIGSYS, SIG_DFL);
    raise(SIGSYS);
}

/* Emulate statx(dirfd, path, flags, mask, buf) via fstatat. */
static void
emulate_statx(siginfo_t *si, ucontext_t *uc)
{
    long args[6];
    struct stat st;
    int fd;
    int flags;
    struct statx *sx;

    memcpy(args, &uc->uc_mcontext.regs[0], sizeof(args));
    fd = (int)args[0];
    flags = (int)args[2];

    if (fstatat(fd, (const char *)args[1], &st, flags & (AT_SYMLINK_NOFOLLOW | AT_EMPTY_PATH)) != 0) {
        uc->uc_mcontext.regs[0] = -shim_errno();
        return;
    }

    sx = (struct statx *)args[4];
    memset(sx, 0, sizeof(*sx));
    sx->stx_mask = STATX_BASIC_STATS;
    sx->stx_blksize = st.st_blksize;
    sx->stx_nlink = st.st_nlink;
    sx->stx_uid = st.st_uid;
    sx->stx_gid = st.st_gid;
    sx->stx_mode = st.st_mode;
    sx->stx_ino = st.st_ino;
    sx->stx_size = st.st_size;
    sx->stx_blocks = st.st_blocks;
    sx->stx_atime.tv_sec = st.st_atim.tv_sec;
    sx->stx_atime.tv_nsec = st.st_atim.tv_nsec;
    sx->stx_mtime.tv_sec = st.st_mtim.tv_sec;
    sx->stx_mtime.tv_nsec = st.st_mtim.tv_nsec;
    sx->stx_ctime.tv_sec = st.st_ctim.tv_sec;
    sx->stx_ctime.tv_nsec = st.st_ctim.tv_nsec;
    sx->stx_dev_major = major(st.st_dev);
    sx->stx_dev_minor = minor(st.st_dev);
    sx->stx_rdev_major = major(st.st_rdev);
    sx->stx_rdev_minor = minor(st.st_rdev);

    uc->uc_mcontext.regs[0] = 0;
    /* On aarch64 the seccomp trap frame already has PC past the svc
     * instruction, so no PC adjustment is needed. */
}

static void
handle_sigsys(int sig, siginfo_t *si, void *ucp)
{
    ucontext_t *uc = (ucontext_t *)ucp;

    (void)sig;
    if (si->si_code != SYS_SECCOMP)
        re_raise();

    switch (si->si_syscall) {
    case SYS_statx:
        emulate_statx(si, uc);
        break;
    default:
        /* Any other syscall the Android seccomp policy traps (pidfd_open,
         * clone3, faccessat2, ...) is reported as ENOSYS, the standard
         * "kernel does not provide this syscall" signal, so well-behaved
         * callers fall back to older syscalls instead of dying. */
        uc->uc_mcontext.regs[0] = -ENOSYS;
        break;
    }
}

__attribute__((constructor)) static void
shim_init(void)
{
    struct sigaction sa;

    memset(&sa, 0, sizeof(sa));
    sa.sa_sigaction = handle_sigsys;
    sa.sa_flags = SA_SIGINFO;
    sigemptyset(&sa.sa_mask);
    sigaction(SIGSYS, &sa, NULL);
}
