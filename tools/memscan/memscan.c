#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <fcntl.h>
#include <unistd.h>

/* JudgeSettings_16 Tutorial float signature (44 bytes):
   120.0, 200.0, 150.0, 0.0625, 0.09375, 0.125, 0.15625, 0.125, 0.125, 0.25, 0.5 */
static const unsigned char PAT[] = {
 0x00,0x00,0xF0,0x42, 0x00,0x00,0x48,0x43, 0x00,0x00,0x16,0x43,
 0x00,0x00,0x80,0x3D, 0x00,0x00,0xC0,0x3D, 0x00,0x00,0x00,0x3E,
 0x00,0x00,0x20,0x3E, 0x00,0x00,0x00,0x3E, 0x00,0x00,0x00,0x3E,
 0x00,0x00,0x80,0x3E, 0x00,0x00,0x00,0x3F };
#define PATLEN ((unsigned)sizeof(PAT))
#define CHUNK (1u<<20)

int main(int argc, char **argv) {
    if (argc < 2) { fprintf(stderr, "usage: %s <pid>\n", argv[0]); return 1; }
    int pid = atoi(argv[1]);
    char path[64];
    snprintf(path, sizeof path, "/proc/%d/maps", pid);
    FILE *f = fopen(path, "r");
    if (!f) { perror("maps"); return 1; }
    char mempath[64];
    snprintf(mempath, sizeof mempath, "/proc/%d/mem", pid);
    int m = open(mempath, O_RDONLY);
    if (m < 0) { perror("mem"); return 1; }

    unsigned char *buf = malloc(CHUNK + 64);
    char line[512];
    unsigned long long scanned = 0;
    while (fgets(line, sizeof line, f)) {
        unsigned long long s, e; char perm[8] = {0};
        if (sscanf(line, "%llx-%llx %4s", &s, &e, perm) != 3) continue;
        if (perm[0] != 'r' || perm[1] != 'w') continue;
        unsigned long long size = e - s;
        if (size == 0 || size > (512ULL << 20)) continue;
        fprintf(stderr, "range %llx-%llx (%llu KB)\n", s, e, size >> 10);
        unsigned long long off = 0;
        unsigned carry = 0;
        while (off < size) {
            size_t want = CHUNK;
            if (off + want > size) want = (size_t)(size - off);
            ssize_t r = pread(m, buf + carry, want, (off_t)(s + off));
            if (r <= 0) break;
            size_t haylen = carry + (size_t)r;
            for (size_t i = 0; i + PATLEN <= haylen; i++) {
                if (memcmp(buf + i, PAT, PATLEN) == 0) {
                    unsigned long long abs = s + off - carry + i;
                    printf("MATCH %llx\n", abs);
                    unsigned char ctx[224];
                    ssize_t cr = pread(m, ctx, sizeof ctx, (off_t)(abs - 32));
                    if (cr > 0) {
                        for (ssize_t k = 0; k < cr; k++) printf("%02x", ctx[k]);
                        printf("\n");
                    }
                    fflush(stdout);
                }
            }
            carry = haylen < PATLEN ? (unsigned)haylen : PATLEN;
            memmove(buf, buf + haylen - carry, carry);
            off += (unsigned long long)r;
        }
        scanned += size;
    }
    fprintf(stderr, "done, scanned %llu MB\n", scanned >> 20);
    return 0;
}
