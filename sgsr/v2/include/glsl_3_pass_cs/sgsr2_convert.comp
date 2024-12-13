#version 320 es

//============================================================================================================
//
//
//                  Copyright (c) 2024, Qualcomm Innovation Center, Inc. All rights reserved.
//                              SPDX-License-Identifier: BSD-3-Clause
//
//============================================================================================================

vec2 decodeVelocityFromTexture(vec2 ev) {
    const float inv_div = 1.0f / (0.499f * 0.5f);
    vec2 dv;
    dv.xy = ev.xy * inv_div - 32767.0f / 65535.0f * inv_div;
    //dv.z = uintBitsToFloat((uint(round(ev.z * 65535.0f)) << 16) | uint(round(ev.w * 65535.0f)));
    return dv;
}

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(set = 0, binding = 1) uniform highp sampler2D InputOpaqueColor;
layout(set = 0, binding = 2) uniform highp sampler2D InputColor;
layout(set = 0, binding = 3) uniform highp sampler2D InputDepth;
layout(set = 0, binding = 4) uniform highp sampler2D InputVelocity;
layout(set = 0, binding = 5, r32ui) uniform writeonly highp uimage2D YCoCgColor;
layout(set = 0, binding = 6, rgba16f) uniform writeonly mediump image2D MotionDepthAlphaBuffer;

layout(std140, set = 0, binding = 0) uniform readonly Params
{
    uvec2                renderSize;
    uvec2                displaySize;
    vec2                 ViewportSizeInverse;
    vec2                 displaySizeRcp;
    vec2                 jitterOffset;
    vec2                 padding1;
    vec4                 clipToPrevClip[4];
    float                preExposure;
    float                cameraFovAngleHor;
    float                cameraNear;
    float                MinLerpContribution;
    uint                 bSameCamera;
    uint                 reset;
} params;

void main()
{
    mediump float h0 = params.preExposure;
    vec2 ViewportSizeInverse = params.ViewportSizeInverse.xy;
    uvec2 InputPos = gl_GlobalInvocationID.xy;

    vec2 gatherCoord = vec2(gl_GlobalInvocationID.xy) * ViewportSizeInverse;
    vec2 ViewportUV = gatherCoord + vec2(0.5) * ViewportSizeInverse;

    //derived from ffx_fsr2_reconstruct_dilated_velocity_and_previous_depth.h
    //FindNearestDepth

    ivec2 InputPosBtmRight = ivec2(1) + ivec2(gl_GlobalInvocationID.xy);
    float NearestZ = texelFetch(InputDepth, InputPosBtmRight, 0).x;
    vec4 topleft = textureGather(InputDepth, gatherCoord, 0);

    NearestZ = min(topleft.x, NearestZ);
	NearestZ = min(topleft.y, NearestZ);
	NearestZ = min(topleft.z, NearestZ);
	NearestZ = min(topleft.w, NearestZ);

    vec2 v11 = vec2(ViewportSizeInverse.x, 0.0);
    vec2 topRight = textureGather(InputDepth, (gatherCoord + v11), 0).yz;

	NearestZ = min(topRight.x, NearestZ);
	NearestZ = min(topRight.y, NearestZ);

    vec2 v13 = vec2(0.0, ViewportSizeInverse.y);
    vec2 bottomLeft = textureGather(InputDepth, (gatherCoord + v13), 0).xy;

    NearestZ = min(bottomLeft.x, NearestZ);
    NearestZ = min(bottomLeft.y, NearestZ);

    //refer to ue/fsr2 PostProcessFFX_FSR2ConvertVelocity.usf, and using nearest depth for dilated motion

    vec4 EncodedVelocity = texelFetch(InputVelocity, ivec2(gl_GlobalInvocationID.xy), 0);

    vec2 motion;
    if (EncodedVelocity.x > 0.0)
    {
        motion = decodeVelocityFromTexture(EncodedVelocity.xy);
    }
    else
    {
#ifdef REQUEST_NDC_Y_UP
        vec2 ScreenPos = vec2(2.0f * ViewportUV.x - 1.0f, 1.0f - 2.0f * ViewportUV.y);
#else
        vec2 ScreenPos = vec2(2.0f * ViewportUV - 1.0f);
#endif
        vec3 Position = vec3(ScreenPos, NearestZ);    //this_clip
        vec4 PreClip = params.clipToPrevClip[3] + ((params.clipToPrevClip[2] * Position.z) + ((params.clipToPrevClip[1] * ScreenPos.y) + (params.clipToPrevClip[0] * ScreenPos.x)));
        vec2 PreScreen = PreClip.xy / PreClip.w;
        motion = Position.xy - PreScreen;
    }

    ////////////compute luma
    mediump vec3 Colorrgb = texelFetch(InputColor, ivec2(InputPos), 0).xyz;

    ///simple tonemap
    Colorrgb /= vec3(max(max(Colorrgb.x, Colorrgb.y), Colorrgb.z) + h0);

    vec3 Colorycocg;
    Colorycocg.x = 0.25 * (Colorrgb.x + 2.0 * Colorrgb.y + Colorrgb.z);
    Colorycocg.y = clamp(0.5 * Colorrgb.x + 0.5 - 0.5 * Colorrgb.z, 0.0, 1.0);
    Colorycocg.z = clamp(Colorycocg.x + Colorycocg.y - Colorrgb.x, 0.0, 1.0);

    //now color YCoCG all in the range of [0,1]
    uint x11 = uint(Colorycocg.x * 2047.5);
    uint y11 = uint(Colorycocg.y * 2047.5);
    uint z10 = uint(Colorycocg.z * 1023.5);

    mediump vec3 Colorprergb = texelFetch(InputOpaqueColor, ivec2(InputPos), 0).xyz;

    ///simple tonemap
    Colorprergb /= max(max(Colorprergb.x, Colorprergb.y), Colorprergb.z) + h0;
    mediump vec3 delta = abs(Colorrgb - Colorprergb);
    mediump float alpha_mask = max(delta.x, max(delta.y, delta.z));
    alpha_mask = (0.35f * 1000.0f) * alpha_mask;

    imageStore(YCoCgColor, ivec2(gl_GlobalInvocationID.xy), uvec4(((x11 << 21u) | (y11 << 10u)) | z10));

    mediump vec4 v29 = vec4(motion, NearestZ, alpha_mask);
    imageStore(MotionDepthAlphaBuffer, ivec2(gl_GlobalInvocationID.xy), v29);
}