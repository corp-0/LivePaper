#include <EGL/egl.h>
#include <EGL/eglext.h>
#include <GLES3/gl3.h>
#include <GLES2/gl2ext.h>
#include <stdint.h>
#include <stdlib.h>
#include <string.h>
#include <wayland-client.h>
#include <wayland-egl.h>
#include "wlr-layer-shell-client.h"

struct lp_presenter {
    struct wl_display *display;
    struct wl_compositor *compositor;
    struct zwlr_layer_shell_v1 *layer_shell;
    struct wl_seat *seat;
    struct wl_pointer *pointer;
    struct wl_surface *surface;
    struct zwlr_layer_surface_v1 *layer_surface;
    struct wl_egl_window *window;
    EGLDisplay egl_display;
    EGLContext context;
    EGLSurface egl_surface;
    GLuint texture;
    GLuint framebuffer;
    uint32_t width;
    uint32_t height;
    int configured;
    int closed;
    int pointer_x;
    int pointer_y;
    uint32_t pointer_modifiers;
    void (*input_callback)(void *, uint32_t, uint32_t, int32_t, int32_t, uint32_t, int32_t);
    void *input_data;
};

enum lp_input_type { LP_INPUT_MOTION = 1, LP_INPUT_BUTTON = 2, LP_INPUT_AXIS = 3 };

static void pointer_enter(void *data, struct wl_pointer *pointer, uint32_t serial,
    struct wl_surface *surface, wl_fixed_t x, wl_fixed_t y)
{
    struct lp_presenter *p = data;
    p->pointer_x = wl_fixed_to_int(x); p->pointer_y = wl_fixed_to_int(y);
    if (p->input_callback)
        p->input_callback(p->input_data, LP_INPUT_MOTION, 0, p->pointer_x, p->pointer_y, 0, 0);
}
static void pointer_leave(void *data, struct wl_pointer *pointer, uint32_t serial,
    struct wl_surface *surface) { }
static void pointer_motion(void *data, struct wl_pointer *pointer, uint32_t time,
    wl_fixed_t x, wl_fixed_t y)
{
    struct lp_presenter *p = data;
    p->pointer_x = wl_fixed_to_int(x); p->pointer_y = wl_fixed_to_int(y);
    if (p->input_callback)
        p->input_callback(p->input_data, LP_INPUT_MOTION, time, p->pointer_x, p->pointer_y, 0, 0);
}
static uint32_t pointer_button_number(uint32_t button)
{
    switch (button) {
    case 0x110: return 1;
    case 0x111: return 3;
    case 0x112: return 2;
    default: return button >= 0x110 ? button - 0x110 + 1 : button;
    }
}
static void pointer_button(void *data, struct wl_pointer *pointer, uint32_t serial,
    uint32_t time, uint32_t button, uint32_t state)
{
    struct lp_presenter *p = data;
    uint32_t number = pointer_button_number(button);
    uint32_t mask = number <= 5 ? 1u << (19 + number) : 0;
    if (state) p->pointer_modifiers |= mask; else p->pointer_modifiers &= ~mask;
    if (p->input_callback)
        p->input_callback(p->input_data, LP_INPUT_BUTTON, time, p->pointer_x,
            p->pointer_y, number, state ? 1 : 0);
}
static void pointer_axis(void *data, struct wl_pointer *pointer, uint32_t time,
    uint32_t axis, wl_fixed_t value)
{
    struct lp_presenter *p = data;
    if (p->input_callback)
        p->input_callback(p->input_data, LP_INPUT_AXIS, time, p->pointer_x,
            p->pointer_y, axis, wl_fixed_to_int(value));
}
static void pointer_frame(void *data, struct wl_pointer *pointer) { }
static void pointer_axis_source(void *data, struct wl_pointer *pointer, uint32_t source) { }
static void pointer_axis_stop(void *data, struct wl_pointer *pointer, uint32_t time,
    uint32_t axis) { }
static void pointer_axis_discrete(void *data, struct wl_pointer *pointer, uint32_t axis,
    int32_t discrete) { }
static const struct wl_pointer_listener pointer_listener = {
    .enter = pointer_enter, .leave = pointer_leave, .motion = pointer_motion,
    .button = pointer_button, .axis = pointer_axis, .frame = pointer_frame,
    .axis_source = pointer_axis_source, .axis_stop = pointer_axis_stop,
    .axis_discrete = pointer_axis_discrete
};

static void seat_capabilities(void *data, struct wl_seat *seat, uint32_t capabilities)
{
    struct lp_presenter *p = data;
    if ((capabilities & WL_SEAT_CAPABILITY_POINTER) && !p->pointer) {
        p->pointer = wl_seat_get_pointer(seat);
        wl_pointer_add_listener(p->pointer, &pointer_listener, p);
    } else if (!(capabilities & WL_SEAT_CAPABILITY_POINTER) && p->pointer) {
        wl_pointer_destroy(p->pointer); p->pointer = NULL;
    }
}
static void seat_name(void *data, struct wl_seat *seat, const char *name) { }
static const struct wl_seat_listener seat_listener = {
    .capabilities = seat_capabilities, .name = seat_name
};

static void registry_global(void *data, struct wl_registry *registry, uint32_t name,
    const char *interface, uint32_t version)
{
    struct lp_presenter *p = data;
    if (!strcmp(interface, wl_compositor_interface.name))
        p->compositor = wl_registry_bind(registry, name, &wl_compositor_interface, version < 4 ? version : 4);
    else if (!strcmp(interface, zwlr_layer_shell_v1_interface.name))
        p->layer_shell = wl_registry_bind(registry, name, &zwlr_layer_shell_v1_interface, version < 4 ? version : 4);
    else if (!strcmp(interface, wl_seat_interface.name)) {
        p->seat = wl_registry_bind(registry, name, &wl_seat_interface, version < 5 ? version : 5);
        wl_seat_add_listener(p->seat, &seat_listener, p);
    }
}

static void registry_remove(void *data, struct wl_registry *registry, uint32_t name) { }
static const struct wl_registry_listener registry_listener = { registry_global, registry_remove };

struct lp_probe {
    int compositor;
    int layer_shell;
};

static void probe_global(void *data, struct wl_registry *registry, uint32_t name,
    const char *interface, uint32_t version)
{
    struct lp_probe *probe = data;
    if (!strcmp(interface, wl_compositor_interface.name))
        probe->compositor = 1;
    else if (!strcmp(interface, zwlr_layer_shell_v1_interface.name))
        probe->layer_shell = 1;
}

static const struct wl_registry_listener probe_listener = { probe_global, registry_remove };

int lp_probe_layer_shell(void)
{
    struct wl_display *display = wl_display_connect(NULL);
    if (!display) return 0;

    struct lp_probe probe = {0};
    struct wl_registry *registry = wl_display_get_registry(display);
    wl_registry_add_listener(registry, &probe_listener, &probe);
    int supported = wl_display_roundtrip(display) >= 0 && probe.compositor && probe.layer_shell;

    wl_registry_destroy(registry);
    wl_display_disconnect(display);
    return supported;
}

static void layer_configure(void *data, struct zwlr_layer_surface_v1 *surface,
    uint32_t serial, uint32_t width, uint32_t height)
{
    struct lp_presenter *p = data;
    zwlr_layer_surface_v1_ack_configure(surface, serial);
    if (width) p->width = width;
    if (height) p->height = height;
    p->configured = 1;
}

static void layer_closed(void *data, struct zwlr_layer_surface_v1 *surface)
{
    ((struct lp_presenter *)data)->closed = 1;
}

static const struct zwlr_layer_surface_v1_listener layer_listener = { layer_configure, layer_closed };

struct lp_presenter *lp_presenter_create(uint32_t width, uint32_t height, int interactive)
{
    struct lp_presenter *p = calloc(1, sizeof(*p));
    p->width = width; p->height = height;
    p->display = wl_display_connect(NULL);
    if (!p->display) goto fail;
    struct wl_registry *registry = wl_display_get_registry(p->display);
    wl_registry_add_listener(registry, &registry_listener, p);
    if (wl_display_roundtrip(p->display) < 0 || !p->compositor || !p->layer_shell) goto fail;
    p->surface = wl_compositor_create_surface(p->compositor);
    p->layer_surface = zwlr_layer_shell_v1_get_layer_surface(
        p->layer_shell, p->surface, NULL, ZWLR_LAYER_SHELL_V1_LAYER_BOTTOM, "livepaper-direct");
    zwlr_layer_surface_v1_add_listener(p->layer_surface, &layer_listener, p);
    zwlr_layer_surface_v1_set_size(p->layer_surface, width, height);
    zwlr_layer_surface_v1_set_anchor(p->layer_surface,
        ZWLR_LAYER_SURFACE_V1_ANCHOR_TOP | ZWLR_LAYER_SURFACE_V1_ANCHOR_BOTTOM |
        ZWLR_LAYER_SURFACE_V1_ANCHOR_LEFT | ZWLR_LAYER_SURFACE_V1_ANCHOR_RIGHT);
    zwlr_layer_surface_v1_set_exclusive_zone(p->layer_surface, -1);
    zwlr_layer_surface_v1_set_keyboard_interactivity(p->layer_surface, 0);
    if (!interactive) {
        struct wl_region *empty = wl_compositor_create_region(p->compositor);
        wl_surface_set_input_region(p->surface, empty);
        wl_region_destroy(empty);
    }
    wl_surface_commit(p->surface);
    while (!p->configured && !p->closed && wl_display_roundtrip(p->display) >= 0) { }
    if (!p->configured || p->closed) goto fail;

    p->egl_display = eglGetDisplay((EGLNativeDisplayType)p->display);
    EGLint major, minor;
    if (p->egl_display == EGL_NO_DISPLAY || !eglInitialize(p->egl_display, &major, &minor)) goto fail;
    static const EGLint config_attrs[] = { EGL_SURFACE_TYPE, EGL_WINDOW_BIT,
        EGL_RENDERABLE_TYPE, EGL_OPENGL_ES3_BIT, EGL_RED_SIZE, 8, EGL_GREEN_SIZE, 8,
        EGL_BLUE_SIZE, 8, EGL_ALPHA_SIZE, 8, EGL_NONE };
    EGLConfig config; EGLint count;
    if (!eglChooseConfig(p->egl_display, config_attrs, &config, 1, &count) || !count) goto fail;
    static const EGLint context_attrs[] = { EGL_CONTEXT_CLIENT_VERSION, 3, EGL_NONE };
    p->context = eglCreateContext(p->egl_display, config, EGL_NO_CONTEXT, context_attrs);
    p->window = wl_egl_window_create(p->surface, p->width, p->height);
    p->egl_surface = eglCreateWindowSurface(
        p->egl_display, config, (EGLNativeWindowType)p->window, NULL);
    if (p->context == EGL_NO_CONTEXT || p->egl_surface == EGL_NO_SURFACE ||
        !eglMakeCurrent(p->egl_display, p->egl_surface, p->egl_surface, p->context)) goto fail;
    glGenTextures(1, &p->texture);
    glGenFramebuffers(1, &p->framebuffer);
    wl_registry_destroy(registry);
    return p;
fail:
    free(p);
    return NULL;
}

void lp_presenter_set_input_callback(struct lp_presenter *p,
    void (*callback)(void *, uint32_t, uint32_t, int32_t, int32_t, uint32_t, int32_t),
    void *data)
{
    p->input_callback = callback;
    p->input_data = data;
}

void *lp_presenter_egl_display(struct lp_presenter *p) { return p->egl_display; }
int lp_presenter_fd(struct lp_presenter *p) { return wl_display_get_fd(p->display); }
int lp_presenter_dispatch(struct lp_presenter *p) { return wl_display_dispatch(p->display); }

int lp_presenter_present(struct lp_presenter *p, void *image, uint32_t source_width,
    uint32_t source_height)
{
    PFNGLEGLIMAGETARGETTEXTURE2DOESPROC bind_image =
        (PFNGLEGLIMAGETARGETTEXTURE2DOESPROC)eglGetProcAddress("glEGLImageTargetTexture2DOES");
    if (!bind_image || !eglMakeCurrent(p->egl_display, p->egl_surface, p->egl_surface, p->context)) return 0;
    glBindTexture(GL_TEXTURE_2D, p->texture);
    bind_image(GL_TEXTURE_2D, image);
    glBindFramebuffer(GL_READ_FRAMEBUFFER, p->framebuffer);
    glFramebufferTexture2D(GL_READ_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_TEXTURE_2D, p->texture, 0);
    glBindFramebuffer(GL_DRAW_FRAMEBUFFER, 0);
    glBlitFramebuffer(0, source_height, source_width, 0, 0, 0, p->width, p->height, GL_COLOR_BUFFER_BIT, GL_LINEAR);
    glFinish();
    return eglSwapBuffers(p->egl_display, p->egl_surface);
}

static const uint8_t font[][5] = {
    [' '] = {0, 0, 0, 0, 0}, ['.'] = {0, 0, 0, 6, 6}, [':'] = {0, 6, 0, 6, 0},
    ['/'] = {1, 2, 4, 8, 16}, ['-'] = {0, 0, 31, 0, 0}, ['_'] = {0, 0, 0, 0, 31},
    ['0'] = {14, 17, 19, 21, 14}, ['1'] = {4, 12, 4, 4, 14},
    ['2'] = {14, 17, 2, 4, 31}, ['3'] = {30, 1, 6, 1, 30},
    ['4'] = {2, 6, 10, 31, 2}, ['5'] = {31, 16, 30, 1, 30},
    ['6'] = {14, 16, 30, 17, 14}, ['7'] = {31, 1, 2, 4, 4},
    ['8'] = {14, 17, 14, 17, 14}, ['9'] = {14, 17, 15, 1, 14},
    ['A'] = {14, 17, 31, 17, 17}, ['B'] = {30, 17, 30, 17, 30},
    ['C'] = {15, 16, 16, 16, 15}, ['D'] = {30, 17, 17, 17, 30},
    ['E'] = {31, 16, 30, 16, 31}, ['F'] = {31, 16, 30, 16, 16},
    ['G'] = {15, 16, 19, 17, 15}, ['H'] = {17, 17, 31, 17, 17},
    ['I'] = {14, 4, 4, 4, 14}, ['J'] = {7, 2, 2, 18, 12},
    ['K'] = {17, 18, 28, 18, 17}, ['L'] = {16, 16, 16, 16, 31},
    ['M'] = {17, 27, 21, 17, 17}, ['N'] = {17, 25, 21, 19, 17},
    ['O'] = {14, 17, 17, 17, 14}, ['P'] = {30, 17, 30, 16, 16},
    ['Q'] = {14, 17, 21, 18, 13}, ['R'] = {30, 17, 30, 18, 17},
    ['S'] = {15, 16, 14, 1, 30}, ['T'] = {31, 4, 4, 4, 4},
    ['U'] = {17, 17, 17, 17, 14}, ['V'] = {17, 17, 17, 10, 4},
    ['W'] = {17, 17, 21, 27, 17}, ['X'] = {17, 10, 4, 10, 17},
    ['Y'] = {17, 10, 4, 4, 4}, ['Z'] = {31, 2, 4, 8, 31}
};

static void draw_text(struct lp_presenter *p, const char *text, int x, int y, int scale)
{
    int origin_x = x;
    glEnable(GL_SCISSOR_TEST);
    for (; *text; text++) {
        unsigned char ch = (unsigned char)*text;
        if (ch == '\n' || x + 6 * scale > (int)p->width - origin_x) {
            x = origin_x; y += 7 * scale;
            if (ch == '\n') continue;
        }
        if (y + 5 * scale > (int)p->height) break;
        if (ch >= 'a' && ch <= 'z') ch -= 'a' - 'A';
        if (ch >= sizeof(font) / sizeof(font[0])) ch = ' ';
        for (int row = 0; row < 5; row++)
            for (int column = 0; column < 5; column++)
                if (font[ch][row] & (1 << (4 - column))) {
                    glScissor(x + column * scale, p->height - y - (row + 1) * scale,
                        scale, scale);
                    glClear(GL_COLOR_BUFFER_BIT);
                }
        x += 6 * scale;
    }
    glDisable(GL_SCISSOR_TEST);
}

int lp_presenter_present_fallback(struct lp_presenter *p, const char *message)
{
    if (!eglMakeCurrent(p->egl_display, p->egl_surface, p->egl_surface, p->context)) return 0;
    glClearColor(0.035f, 0.045f, 0.065f, 1.0f);
    glClear(GL_COLOR_BUFFER_BIT);
    glClearColor(0.92f, 0.94f, 0.98f, 1.0f);
    draw_text(p, "LIVEPAPER COULD NOT LOAD THIS WALLPAPER", 80, 100, 4);
    glClearColor(0.62f, 0.67f, 0.76f, 1.0f);
    draw_text(p, message, 80, 180, 3);
    draw_text(p, "CHECK THE LIVEPAPER SERVICE LOG FOR DETAILS.", 80, 250, 3);
    glFinish();
    return eglSwapBuffers(p->egl_display, p->egl_surface);
}
