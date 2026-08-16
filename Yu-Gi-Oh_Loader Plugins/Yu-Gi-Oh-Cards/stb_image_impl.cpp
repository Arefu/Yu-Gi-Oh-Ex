// Separate translation unit that provides the stb_image implementation.
// Keep STB_IMAGE_IMPLEMENTATION defined in exactly one .cpp - if it ends up
// in a header that gets included twice you'll get duplicate symbol errors.
#define STB_IMAGE_IMPLEMENTATION
#define STBI_NO_STDIO   // we hand it bytes we already read ourselves (see Cards.cpp), not FILE*
#include "stb_image.h"
