/*
 * party_anywhere.h: SDK do Party Anywhere (ABI 1). https://github.com/joaocaioo/party-anywhere
 *
 * O jogo não tem código de controle: o Hub descobre controles, celulares e teclados, organiza o lobby
 * (P1..P10) e reconecta quem cai. O jogo só lê. Documentação: docs/SDK.md.
 *
 * Uso (estilo stb): inclua onde precisar e, em UM arquivo .c/.cpp, antes do include:
 *
 *     #define PARTY_ANYWHERE_IMPLEMENTATION
 *     #include "party_anywhere.h"
 *
 * Windows, C89/C99/C++, x86 e x64. Sem DLL, sem thread escondida, sem alocação depois do pa_connect.
 * Licença: MIT.
 */
#ifndef PARTY_ANYWHERE_H
#define PARTY_ANYWHERE_H

#include <stdint.h>
#include <stddef.h>
#ifndef __cplusplus
#include <stdbool.h>
#endif

#ifdef __cplusplus
extern "C" {
#endif

#define PA_ABI_VERSION 1
#define PA_MAX_SEATS 10
#define PA_ALL_SEATS (-1)

typedef enum pa_result {
    PA_OK = 0,
    PA_ERR_HUB_NOT_RUNNING = 1, /* o Hub não está aberto: avise o jogador ou use controles próprios */
    PA_ERR_VERSION = 2,         /* o Hub fala outra versão maior do SDK */
    PA_ERR_BUSY = 3,            /* outro jogo já está conectado ao Hub */
    PA_ERR_INVALID_ARG = 4
} pa_result;

typedef enum pa_seat_state { PA_SEAT_EMPTY = 0, PA_SEAT_ACTIVE = 1, PA_SEAT_RECONNECTING = 2 } pa_seat_state;
typedef enum pa_device_kind { PA_DEVICE_GAMEPAD = 0, PA_DEVICE_PHONE = 1, PA_DEVICE_KEYBOARD = 2 } pa_device_kind;

typedef enum pa_event_type {
    PA_EVENT_NONE = 0,
    PA_EVENT_SEAT_JOINED = 1,       /* alguém entrou nesta vaga */
    PA_EVENT_SEAT_RECONNECTING = 2, /* caiu; a vaga fica guardada (pause ou proteja o personagem) */
    PA_EVENT_SEAT_RESUMED = 3,      /* voltou para a mesma vaga */
    PA_EVENT_SEAT_LEFT = 4,         /* saiu do lobby */
    PA_EVENT_SEAT_RENAMED = 5,
    PA_EVENT_HUB_LOST = 6           /* o Hub fechou: os pads ficam neutros */
} pa_event_type;

typedef struct pa_event {
    pa_event_type type;
    int seat; /* 0..9 → P1..P10; -1 em HUB_LOST */
} pa_event;

enum {
    PA_BTN_A = 1u << 0, PA_BTN_B = 1u << 1, PA_BTN_X = 1u << 2, PA_BTN_Y = 1u << 3,
    PA_BTN_LB = 1u << 4, PA_BTN_RB = 1u << 5, PA_BTN_BACK = 1u << 6, PA_BTN_START = 1u << 7,
    PA_BTN_LS = 1u << 8, PA_BTN_RS = 1u << 9,
    PA_BTN_UP = 1u << 10, PA_BTN_DOWN = 1u << 11, PA_BTN_LEFT = 1u << 12, PA_BTN_RIGHT = 1u << 13,
    PA_BTN_GUIDE = 1u << 14
};

/* Sempre no formato de controle de Xbox, venha de um DualSense, de um celular ou de um teclado. */
typedef struct pa_pad {
    uint32_t buttons;  /* seguros agora */
    uint32_t pressed;  /* apertados desde o pa_poll anterior */
    uint32_t released; /* soltos desde o pa_poll anterior */
    float lx, ly, rx, ry; /* -1..1, direita e cima positivos, zona morta aplicada */
    float lt, rt;         /* 0..1 */
} pa_pad;

typedef struct pa_seat_info {
    uint8_t index; /* 0..9 → P1..P10 */
    pa_seat_state state;
    pa_device_kind device;
    uint8_t r, g, b; /* cor do jogador, fixa por número (P1 é sempre azul) */
    char name[32];   /* UTF-8, ex.: "João" */
} pa_seat_info;

typedef struct pa_game_info {
    uint32_t struct_size; /* sizeof(pa_game_info) */
    const char* game_id;  /* o mesmo id do party-anywhere.json */
    const char* game_name;
    int min_players;
    int max_players;
} pa_game_info;

typedef enum pa_layout { PA_LAYOUT_GAMEPAD = 0, PA_LAYOUT_SIMPLE = 1, PA_LAYOUT_DRIVING = 2, PA_LAYOUT_MENU = 3 } pa_layout;

/* Só para pa_set_label: gatilhos e analógicos não são botões em pa_pad.buttons, mas têm nome no celular. */
enum { PA_LABEL_LT = 1u << 16, PA_LABEL_RT = 1u << 17, PA_LABEL_STICK_L = 1u << 18, PA_LABEL_STICK_R = 1u << 19 };

typedef struct pa_client pa_client;

/* conexão */
pa_result   pa_connect(const pa_game_info* info, pa_client** out);
void        pa_disconnect(pa_client* pa);
void        pa_poll(pa_client* pa); /* uma vez por frame, na thread do jogo */
bool        pa_is_connected(const pa_client* pa);
const char* pa_join_url(const pa_client* pa); /* URL do QR, para o jogo desenhar o próprio lobby */
bool        pa_next_event(pa_client* pa, pa_event* out);

/* vagas e controles */
bool pa_get_seat(const pa_client* pa, int seat, pa_seat_info* out);
int  pa_active_seat_count(const pa_client* pa);
bool pa_get_pad(const pa_client* pa, int seat, pa_pad* out); /* false (e zerado) se a vaga não está ativa */

/* do jogo para o jogador */
void pa_set_accepting_players(pa_client* pa, bool accepting);
void pa_set_layout(pa_client* pa, int seat, pa_layout layout);
void pa_set_label(pa_client* pa, int seat, uint32_t button, const char* label); /* "Correr", "Atirar"; "" tira */
void pa_set_mode(pa_client* pa, int seat, int mode); /* modo do controle do celular: 0 = o primeiro ("A pé") */
void pa_toast(pa_client* pa, int seat, const char* text);                      /* aviso curto no celular */
void pa_rumble(pa_client* pa, int seat, float low, float high, int duration_ms);
void pa_set_light(pa_client* pa, int seat, uint8_t r, uint8_t g, uint8_t b);

/* A tela do jogo no celular (padrão para todo jogo e mod): quem joga pelo celular vê a própria parte do
 * jogo nele, e ela sai da TV.
 *
 *   if (pa_view_wanted(pa, s)) {           // este jogador está vendo no celular:
 *       // não desenhe a parte dele na TV (a tela dividida fica para os outros)
 *       // desenhe a visão dele num alvo à parte, leia os pixels (BGRA, até 1280x720) e entregue:
 *       pa_submit_view(pa, s, pixels, 640, 360, 640 * 4);
 *   }
 *
 * Sem o Hub, ou com o jogador sem celular, pa_view_wanted é false e o jogo segue como sempre. Entregue a
 * cada frame (ou a cada 2): o Hub manda para o celular no ritmo que a rede aguenta. */
bool pa_view_wanted(pa_client* pa, int seat);
bool pa_submit_view(pa_client* pa, int seat, const void* bgra, int width, int height, int stride_bytes);

/* ------------------------------------------------------------------------------------------------
 * Memória compartilhada (ABI 1). O Hub cria "Local\PartyAnywhere.v1"; o jogo abre e lê.
 * Mudar este layout é mudança de ABI (crates/pa-sdk tem o espelho em Rust e testa os dois juntos).
 * ------------------------------------------------------------------------------------------------ */
#define PA_SHM_NAME_W L"Local\\PartyAnywhere.v1"
#define PA_SHM_MAGIC 0x31484150u /* "PAH1" */
#define PA_SHM_CMDS 64

typedef struct pa_shm_seat {
    volatile uint32_t seq; /* seqlock: ímpar = o Hub está escrevendo */
    uint8_t state;         /* pa_seat_state */
    uint8_t device;        /* pa_device_kind */
    uint8_t r, g, b;
    uint8_t pad_[3];
    char name[32];
    uint32_t buttons;
    float lx, ly, rx, ry, lt, rt;
} pa_shm_seat;

enum { PA_CMD_RUMBLE = 1, PA_CMD_TOAST = 2, PA_CMD_ACCEPTING = 3, PA_CMD_LABEL = 4, PA_CMD_LAYOUT = 5, PA_CMD_LIGHT = 6, PA_CMD_MODE = 7 };

typedef struct pa_shm_cmd {
    uint8_t type;
    int8_t seat;          /* -1 = todas */
    uint16_t duration_ms; /* rumble */
    float low, high;      /* rumble; light usa low/high/button como cor */
    uint32_t value;       /* accepting (0/1), layout, botão do label, cor */
    char text[48];        /* toast, label */
} pa_shm_cmd;

typedef struct pa_shm {
    uint32_t magic;
    uint32_t abi;
    uint32_t size;
    volatile uint32_t hub_heartbeat; /* o Hub soma 1 a cada 100 ms */
    volatile int32_t game_pid;       /* 0 = livre; o jogo conectado escreve o próprio pid */
    uint32_t reserved_;
    char join_url[128];
    pa_shm_seat seats[PA_MAX_SEATS];
    volatile uint32_t cmd_write; /* só o jogo escreve */
    volatile uint32_t cmd_read;  /* só o Hub escreve */
    pa_shm_cmd cmds[PA_SHM_CMDS];
} pa_shm;

#define PA_SHM_SIZE 4976
typedef char pa__shm_size_check[(sizeof(pa_shm) == PA_SHM_SIZE) ? 1 : -1];
typedef char pa__seat_size_check[(sizeof(pa_shm_seat) == 72) ? 1 : -1];
typedef char pa__cmd_size_check[(sizeof(pa_shm_cmd) == 64) ? 1 : -1];

/* A tela de cada jogador: uma memória por vaga, que o Hub cria quando o celular liga a tela
 * ("Local\PartyAnywhere.v1.view.<vaga>"). Cabeçalho de 64 bytes e os pixels BGRA logo depois. */
#ifndef PA_VIEW_NAME_PREFIX
#define PA_VIEW_NAME_PREFIX L"Local\\PartyAnywhere.v1.view."
#endif
#define PA_VIEW_MAGIC 0x57454956u /* "VIEW" */
#define PA_VIEW_MAX_W 1280
#define PA_VIEW_MAX_H 720
#define PA_VIEW_HEADER 64
#define PA_VIEW_SIZE (PA_VIEW_HEADER + PA_VIEW_MAX_W * PA_VIEW_MAX_H * 4)

typedef struct pa_view_header {
    uint32_t magic;
    uint32_t version;         /* 1 */
    volatile uint32_t wanted; /* o Hub escreve: 1 = o celular mostra a tela deste jogador */
    uint32_t width, height, stride;
    volatile uint32_t seq;    /* seqlock: ímpar = o jogo está escrevendo */
    uint32_t reserved_;
    uint64_t timestamp_us;    /* QueryPerformanceCounter em microssegundos, ao terminar o quadro */
    uint8_t pad_[24];
} pa_view_header;
typedef char pa__view_header_check[(sizeof(pa_view_header) == PA_VIEW_HEADER) ? 1 : -1];

/* Uso interno e testes: liga o cliente a uma memória já mapeada. */
pa_result pa__attach(pa_shm* shm, int32_t pid, pa_client** out);

#ifdef __cplusplus
}
#endif
#endif /* PARTY_ANYWHERE_H */

/* ================================================================================================ */
#ifdef PARTY_ANYWHERE_IMPLEMENTATION
#ifndef PARTY_ANYWHERE_IMPLEMENTED
#define PARTY_ANYWHERE_IMPLEMENTED

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>
#include <stdlib.h>
#include <string.h>

#define PA__EVENTS 64
#define PA__HUB_LOST_MS 1500

struct pa_client {
    pa_shm* shm;
    HANDLE mapping; /* NULL quando ligado por pa__attach */
    int32_t pid;
    bool lost;
    uint32_t last_heartbeat;
    ULONGLONG heartbeat_at;
    pa_shm_seat seen[PA_MAX_SEATS]; /* foto do último pa_poll */
    uint32_t prev_buttons[PA_MAX_SEATS];
    uint32_t pressed[PA_MAX_SEATS];
    uint32_t released[PA_MAX_SEATS];
    pa_event events[PA__EVENTS];
    uint32_t ev_head, ev_tail;
    uint8_t sent_accepting; /* 0 = nunca mandado, 1 = não, 2 = sim (chamadas repetidas não geram tráfego) */
    /* a tela de cada jogador no celular: aberta quando o Hub cria; procura de novo a cada ~1 s */
    HANDLE view_map[PA_MAX_SEATS];
    pa_view_header* view[PA_MAX_SEATS];
    ULONGLONG view_next_try[PA_MAX_SEATS];
};

static void pa__push_event(pa_client* pa, pa_event_type type, int seat) {
    if (pa->ev_tail - pa->ev_head >= PA__EVENTS) return; /* jogo não leu: descarta o mais novo */
    pa->events[pa->ev_tail % PA__EVENTS].type = type;
    pa->events[pa->ev_tail % PA__EVENTS].seat = seat;
    pa->ev_tail++;
}

/* Lê uma vaga sem pegar o Hub no meio da escrita. Com limite de tentativas: um Hub encerrado à força no meio
 * de uma escrita deixa a vaga "escrevendo" para sempre, e o jogo não pode travar esperando. Sem leitura
 * inteira, fica a anterior (e o Hub sumido zera os controles logo depois, pelo heartbeat). */
#define PA__READ_TRIES 1000
static bool pa__read_seat(const pa_shm_seat* src, pa_shm_seat* dst) {
    int tries;
    for (tries = 0; tries < PA__READ_TRIES; ++tries) {
        uint32_t a = src->seq;
        MemoryBarrier();
        if (a & 1u) { YieldProcessor(); continue; }
        memcpy(dst, (const void*)src, sizeof *dst);
        MemoryBarrier();
        if (src->seq == a) return true;
    }
    return false;
}

pa_result pa__attach(pa_shm* shm, int32_t pid, pa_client** out) {
    pa_client* pa;
    LONG owner;
    if (!shm || !out) return PA_ERR_INVALID_ARG;
    if (shm->magic != PA_SHM_MAGIC) return PA_ERR_HUB_NOT_RUNNING;
    if (shm->abi != PA_ABI_VERSION || shm->size < sizeof(pa_shm)) return PA_ERR_VERSION;
    owner = InterlockedCompareExchange((volatile LONG*)&shm->game_pid, (LONG)pid, 0);
    if (owner != 0 && owner != pid) return PA_ERR_BUSY;
    pa = (pa_client*)calloc(1, sizeof *pa);
    if (!pa) {
        InterlockedCompareExchange((volatile LONG*)&shm->game_pid, 0, (LONG)pid);
        return PA_ERR_INVALID_ARG;
    }
    pa->shm = shm;
    pa->pid = pid;
    pa->last_heartbeat = shm->hub_heartbeat;
    pa->heartbeat_at = GetTickCount64();
    *out = pa;
    return PA_OK;
}

pa_result pa_connect(const pa_game_info* info, pa_client** out) {
    HANDLE mapping;
    void* view;
    pa_result r;
    if (!out || (info && info->struct_size < sizeof(uint32_t))) return PA_ERR_INVALID_ARG;
    *out = NULL;
    mapping = OpenFileMappingW(FILE_MAP_ALL_ACCESS, FALSE, PA_SHM_NAME_W);
    if (!mapping) return PA_ERR_HUB_NOT_RUNNING;
    view = MapViewOfFile(mapping, FILE_MAP_ALL_ACCESS, 0, 0, sizeof(pa_shm));
    if (!view) { CloseHandle(mapping); return PA_ERR_HUB_NOT_RUNNING; }
    r = pa__attach((pa_shm*)view, (int32_t)GetCurrentProcessId(), out);
    if (r != PA_OK) { UnmapViewOfFile(view); CloseHandle(mapping); return r; }
    (*out)->mapping = mapping;
    return PA_OK;
}

void pa_disconnect(pa_client* pa) {
    int s;
    if (!pa) return;
    for (s = 0; s < PA_MAX_SEATS; s++) {
        if (pa->view[s]) UnmapViewOfFile(pa->view[s]);
        if (pa->view_map[s]) CloseHandle(pa->view_map[s]);
    }
    InterlockedCompareExchange((volatile LONG*)&pa->shm->game_pid, 0, (LONG)pa->pid);
    if (pa->mapping) { UnmapViewOfFile(pa->shm); CloseHandle(pa->mapping); }
    free(pa);
}

void pa_poll(pa_client* pa) {
    int s;
    ULONGLONG now;
    if (!pa) return;
    now = GetTickCount64();
    if (pa->shm->hub_heartbeat != pa->last_heartbeat) {
        pa->last_heartbeat = pa->shm->hub_heartbeat;
        pa->heartbeat_at = now;
        pa->lost = false;
    } else if (!pa->lost && now - pa->heartbeat_at > PA__HUB_LOST_MS) {
        pa->lost = true;
        pa__push_event(pa, PA_EVENT_HUB_LOST, -1);
    }
    for (s = 0; s < PA_MAX_SEATS; ++s) {
        pa_shm_seat cur;
        pa_shm_seat* old = &pa->seen[s];
        if (!pa__read_seat(&pa->shm->seats[s], &cur)) cur = *old;
        if (pa->lost) cur.state = old->state; /* sem Hub: a foto congela, os pads ficam neutros */
        if (cur.state != old->state) {
            if (old->state == PA_SEAT_EMPTY && cur.state != PA_SEAT_EMPTY) pa__push_event(pa, PA_EVENT_SEAT_JOINED, s);
            else if (cur.state == PA_SEAT_RECONNECTING) pa__push_event(pa, PA_EVENT_SEAT_RECONNECTING, s);
            else if (old->state == PA_SEAT_RECONNECTING && cur.state == PA_SEAT_ACTIVE) pa__push_event(pa, PA_EVENT_SEAT_RESUMED, s);
            else if (cur.state == PA_SEAT_EMPTY) pa__push_event(pa, PA_EVENT_SEAT_LEFT, s);
        } else if (cur.state != PA_SEAT_EMPTY && memcmp(cur.name, old->name, sizeof cur.name) != 0) {
            pa__push_event(pa, PA_EVENT_SEAT_RENAMED, s);
        }
        if (cur.state != PA_SEAT_ACTIVE || pa->lost) {
            /* nunca o último estado preso: vaga fora do ar devolve controle neutro */
            cur.buttons = 0;
            cur.lx = cur.ly = cur.rx = cur.ry = cur.lt = cur.rt = 0.0f;
        }
        pa->pressed[s] = cur.buttons & ~pa->prev_buttons[s];
        pa->released[s] = ~cur.buttons & pa->prev_buttons[s];
        pa->prev_buttons[s] = cur.buttons;
        *old = cur;
    }
}

bool pa_is_connected(const pa_client* pa) { return pa && !pa->lost; }

const char* pa_join_url(const pa_client* pa) { return pa ? pa->shm->join_url : ""; }

bool pa_next_event(pa_client* pa, pa_event* out) {
    if (!pa || !out || pa->ev_head == pa->ev_tail) return false;
    *out = pa->events[pa->ev_head % PA__EVENTS];
    pa->ev_head++;
    return true;
}

bool pa_get_seat(const pa_client* pa, int seat, pa_seat_info* out) {
    const pa_shm_seat* s;
    if (!pa || !out || seat < 0 || seat >= PA_MAX_SEATS) return false;
    s = &pa->seen[seat];
    out->index = (uint8_t)seat;
    out->state = (pa_seat_state)s->state;
    out->device = (pa_device_kind)s->device;
    out->r = s->r; out->g = s->g; out->b = s->b;
    memcpy(out->name, s->name, sizeof out->name);
    out->name[sizeof out->name - 1] = 0;
    return s->state != PA_SEAT_EMPTY;
}

int pa_active_seat_count(const pa_client* pa) {
    int s, n = 0;
    if (!pa) return 0;
    for (s = 0; s < PA_MAX_SEATS; ++s) n += pa->seen[s].state == PA_SEAT_ACTIVE;
    return n;
}

bool pa_get_pad(const pa_client* pa, int seat, pa_pad* out) {
    const pa_shm_seat* s;
    if (!out) return false;
    memset(out, 0, sizeof *out);
    if (!pa || seat < 0 || seat >= PA_MAX_SEATS || pa->lost) return false;
    s = &pa->seen[seat];
    if (s->state != PA_SEAT_ACTIVE) return false;
    out->buttons = s->buttons;
    out->pressed = pa->pressed[seat];
    out->released = pa->released[seat];
    out->lx = s->lx; out->ly = s->ly; out->rx = s->rx; out->ry = s->ry;
    out->lt = s->lt; out->rt = s->rt;
    return true;
}

static pa_shm_cmd* pa__cmd(pa_client* pa, uint8_t type, int seat) {
    pa_shm* m;
    pa_shm_cmd* c;
    if (!pa || seat < PA_ALL_SEATS || seat >= PA_MAX_SEATS) return NULL;
    m = pa->shm;
    if (m->cmd_write - m->cmd_read >= PA_SHM_CMDS) return NULL; /* Hub atrasado: descarta */
    c = &m->cmds[m->cmd_write % PA_SHM_CMDS];
    memset(c, 0, sizeof *c);
    c->type = type;
    c->seat = (int8_t)seat;
    return c;
}

static void pa__commit(pa_client* pa) {
    MemoryBarrier();
    pa->shm->cmd_write++;
}

static void pa__copy_text(char* dst, size_t cap, const char* src) {
    size_t n = src ? strlen(src) : 0;
    if (n >= cap) n = cap - 1;
    if (n) memcpy(dst, src, n);
    dst[n] = 0;
}

void pa_set_accepting_players(pa_client* pa, bool accepting) {
    pa_shm_cmd* c;
    uint8_t want = accepting ? 2 : 1;
    if (!pa || pa->sent_accepting == want) return;
    if ((c = pa__cmd(pa, PA_CMD_ACCEPTING, PA_ALL_SEATS)) == NULL) return;
    c->value = accepting ? 1u : 0u;
    pa__commit(pa);
    pa->sent_accepting = want;
}

void pa_set_layout(pa_client* pa, int seat, pa_layout layout) {
    pa_shm_cmd* c = pa__cmd(pa, PA_CMD_LAYOUT, seat);
    if (!c) return;
    c->value = (uint32_t)layout;
    pa__commit(pa);
}

void pa_set_label(pa_client* pa, int seat, uint32_t button, const char* label) {
    pa_shm_cmd* c = pa__cmd(pa, PA_CMD_LABEL, seat);
    if (!c) return;
    c->value = button;
    pa__copy_text(c->text, sizeof c->text, label);
    pa__commit(pa);
}

void pa_set_mode(pa_client* pa, int seat, int mode) {
    pa_shm_cmd* c = pa__cmd(pa, PA_CMD_MODE, seat);
    if (!c || mode < 0) return;
    c->value = (uint32_t)mode;
    pa__commit(pa);
}

void pa_toast(pa_client* pa, int seat, const char* text) {
    pa_shm_cmd* c = pa__cmd(pa, PA_CMD_TOAST, seat);
    if (!c) return;
    pa__copy_text(c->text, sizeof c->text, text);
    pa__commit(pa);
}

void pa_rumble(pa_client* pa, int seat, float low, float high, int duration_ms) {
    pa_shm_cmd* c = pa__cmd(pa, PA_CMD_RUMBLE, seat);
    if (!c) return;
    c->low = low < 0 ? 0 : (low > 1 ? 1 : low);
    c->high = high < 0 ? 0 : (high > 1 ? 1 : high);
    c->duration_ms = (uint16_t)(duration_ms < 0 ? 0 : (duration_ms > 5000 ? 5000 : duration_ms));
    pa__commit(pa);
}

void pa_set_light(pa_client* pa, int seat, uint8_t r, uint8_t g, uint8_t b) {
    pa_shm_cmd* c = pa__cmd(pa, PA_CMD_LIGHT, seat);
    if (!c) return;
    c->value = ((uint32_t)r << 16) | ((uint32_t)g << 8) | b;
    pa__commit(pa);
}

static pa_view_header* pa__view(pa_client* pa, int seat) {
    ULONGLONG now;
    wchar_t name[64];
    HANDLE h;
    void* v;
    if (!pa || seat < 0 || seat >= PA_MAX_SEATS) return NULL;
    if (pa->view[seat]) return pa->view[seat];
    now = GetTickCount64();
    if (now < pa->view_next_try[seat]) return NULL;
    pa->view_next_try[seat] = now + 1000;
    lstrcpyW(name, PA_VIEW_NAME_PREFIX); /* + a vaga (0 a 9) */
    {
        int n = lstrlenW(name);
        name[n] = (wchar_t)(L'0' + seat);
        name[n + 1] = 0;
    }
    h = OpenFileMappingW(FILE_MAP_ALL_ACCESS, FALSE, name);
    if (!h) return NULL;
    v = MapViewOfFile(h, FILE_MAP_ALL_ACCESS, 0, 0, PA_VIEW_SIZE);
    if (!v) { CloseHandle(h); return NULL; }
    if (((pa_view_header*)v)->magic != PA_VIEW_MAGIC) { UnmapViewOfFile(v); CloseHandle(h); return NULL; }
    pa->view_map[seat] = h;
    pa->view[seat] = (pa_view_header*)v;
    return pa->view[seat];
}

bool pa_view_wanted(pa_client* pa, int seat) {
    pa_view_header* v = pa__view(pa, seat);
    if (!v || !v->wanted) return false;
    /* só quem está ativo (reconectando, a vaga fica na TV como sempre) */
    return pa->seen[seat].state == PA_SEAT_ACTIVE;
}

bool pa_submit_view(pa_client* pa, int seat, const void* bgra, int width, int height, int stride_bytes) {
    pa_view_header* v;
    LARGE_INTEGER t, f;
    uint8_t* dst;
    const uint8_t* src = (const uint8_t*)bgra;
    int y, w, h, row;
    if (!bgra || width <= 0 || height <= 0 || !pa_view_wanted(pa, seat)) return false;
    v = pa->view[seat];
    w = width > PA_VIEW_MAX_W ? PA_VIEW_MAX_W : width;
    h = height > PA_VIEW_MAX_H ? PA_VIEW_MAX_H : height;
    row = w * 4;
    dst = (uint8_t*)v + PA_VIEW_HEADER;
    InterlockedIncrement((volatile LONG*)&v->seq); /* ímpar: escrevendo */
    for (y = 0; y < h; y++) memcpy(dst + (size_t)y * row, src + (size_t)y * stride_bytes, (size_t)row);
    v->width = (uint32_t)w;
    v->height = (uint32_t)h;
    v->stride = (uint32_t)row;
    QueryPerformanceCounter(&t);
    QueryPerformanceFrequency(&f);
    v->timestamp_us = (uint64_t)(t.QuadPart / f.QuadPart) * 1000000u + (uint64_t)(t.QuadPart % f.QuadPart) * 1000000u / (uint64_t)f.QuadPart;
    InterlockedIncrement((volatile LONG*)&v->seq); /* par: pronto */
    return true;
}

#endif /* PARTY_ANYWHERE_IMPLEMENTED */
#endif /* PARTY_ANYWHERE_IMPLEMENTATION */
