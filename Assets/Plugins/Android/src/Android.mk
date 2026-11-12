include $(CLEAR_VARS)

# Prefer build_plugin.cmd (calls the NDK's clang directly). ndk-build does not
# cope with spaces in the project path, e.g. "S:\Unity Projects\...".

LOCAL_ARM_MODE  := arm
LOCAL_PATH      := $(NDK_PROJECT_PATH)
# Module name must match [DllImport("SoundStageNative")] -> libSoundStageNative.so
LOCAL_MODULE    := SoundStageNative
LOCAL_CFLAGS    := -O2 -Wall
LOCAL_SRC_FILES := main.cpp Filter.cpp
LOCAL_LDLIBS    := -lm
LOCAL_LDFLAGS   := -Wl,-z,max-page-size=16384

include $(BUILD_SHARED_LIBRARY)
