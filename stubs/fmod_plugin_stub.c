#include <stdint.h>
#include <stdlib.h>

struct FMOD_DSP_DESCRIPTION;
struct FMOD_CODEC_DESCRIPTION;
struct FMOD_OUTPUT_DESCRIPTION;

typedef void* FMOD_SYSTEM_PTR;

typedef uint32_t (*REGISTER_DSP_METHOD)(FMOD_SYSTEM_PTR system, struct FMOD_DSP_DESCRIPTION* description, uint32_t* handle);
typedef uint32_t (*REGISTER_CODEC_METHOD)(FMOD_SYSTEM_PTR system, struct FMOD_CODEC_DESCRIPTION* description, uint32_t* handle);
typedef uint32_t (*REGISTER_OUTPUT_METHOD)(FMOD_SYSTEM_PTR system, struct FMOD_OUTPUT_DESCRIPTION* description, uint32_t* handle);

typedef struct {
    FMOD_SYSTEM_PTR system;
    REGISTER_DSP_METHOD register_dsp_method;
    REGISTER_CODEC_METHOD register_codec_method;
    REGISTER_OUTPUT_METHOD register_output_method;
} FMOD_IOS_INTERFACE;

__attribute__((visibility("default"))) __attribute__((used))
uint32_t* load_all_fmod_plugins(FMOD_IOS_INTERFACE* p_interface, uint32_t* r_count) {
    if (r_count) {
        *r_count = 0;
    }
    return NULL;
}
